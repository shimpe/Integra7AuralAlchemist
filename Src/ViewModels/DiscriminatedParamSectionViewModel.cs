using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One dynamic per-value parameter: the live FQP plus its cleaned label.</summary>
public sealed record DisplayParam(FullyQualifiedParameter Param, string Label);

/// <summary>
/// Reusable "discriminated parameter section": a family -> value two-combo picker over one discriminator
/// param, plus a grid of the value-specific named params (filtered by a path segment), rendered via
/// DataTemplateProvider.ParameterValueTemplate. Recomputes only on discriminator changes (UI-thread).
/// Used by the friendly MFX panel and the SN-A instrument detail. Catalog is supplied as delegates so
/// the engine catalogs (MfxCatalog / InstrumentCatalog) need no shared base type.
///
/// Optional loaded-aware hooks (all default to identity, so MFX is unaffected): a <c>familiesSupplier</c>
/// for a dynamic family list, a <c>displayName</c>/<c>toRealValue</c> pair so the instrument combo can
/// show a transformed label (e.g. "(not loaded)") while still writing the real discrete value, and a
/// public <see cref="Reproject"/> to refresh after the loaded set changes. The VM always keeps the
/// current selection's family and value visible so a filtered list can never blank the selection.
/// </summary>
public sealed class DiscriminatedParamSectionViewModel : ViewModelBase, IDisposable
{
    private readonly DomainBase _domain;
    private readonly List<FullyQualifiedParameter> _allParams;
    private readonly List<FullyQualifiedParameter> _discriminators;
    private readonly IDisposable _valueSub;
    private readonly string _gridPathSegment;
    private readonly Func<IReadOnlyList<string>> _familiesSupplier;
    private readonly Func<int, string> _familyOf;
    private readonly Func<string, IReadOnlyList<int>> _valuesIn;
    private readonly Func<string, IReadOnlyList<string>, IReadOnlyList<string>> _friendlyLabels;
    private readonly Func<int, string, string> _displayName;
    private readonly Func<string, string> _toRealValue;
    private bool _syncing;

    /// <summary>The discriminator wrapper (e.g. MFX Type / Instrument). Exposed for engine-specific extras.</summary>
    public ParamString Discriminator { get; }
    public ObservableCollection<DisplayParam> Params { get; } = [];
    public bool HasParams => Params.Count > 0;

    public DiscriminatedParamSectionViewModel(DomainBase domain, ThrottledParameterWriter writer,
        string discriminatorLeafName, string gridPathSegment,
        IReadOnlyList<string> families, Func<int, string> familyOf,
        Func<string, IReadOnlyList<int>> valuesIn,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>> friendlyLabels,
        Func<IReadOnlyList<string>>? familiesSupplier = null,
        Func<int, string, string>? displayName = null,
        Func<string, string>? toRealValue = null)
    {
        _domain = domain;
        _gridPathSegment = gridPathSegment;
        _familiesSupplier = familiesSupplier ?? (() => families);
        _familyOf = familyOf; _valuesIn = valuesIn; _friendlyLabels = friendlyLabels;
        _displayName = displayName ?? ((_, name) => name);
        _toRealValue = toRealValue ?? (d => d);
        _allParams = domain.GetRelevantParameters(true, true);

        var disc = _allParams.First(p => p.ParSpec.Name == discriminatorLeafName);
        // Options from Repr (enum) or Discrete (e.g. Instrument list).
        var opts = disc.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                   ?? disc.ParSpec.Discrete?.Select(d => d.Item2).ToList()
                   ?? new List<string>();
        Discriminator = new ParamString(domain, disc, writer, opts);

        _families = BuildFamilies();

        var parentPaths = _allParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        _discriminators = _allParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        _valueSub = Discriminator.WhenAnyValue(t => t.Value).Subscribe(SyncPickerFromValue);
        Recompute();
    }

    private IReadOnlyList<string> _families = [];
    /// <summary>Selectable families (loaded-aware via the supplier; always includes the current
    /// selection's family).</summary>
    public IReadOnlyList<string> Families { get => _families; private set => this.RaiseAndSetIfChanged(ref _families, value); }

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            ValuesInFamily = BuildValuesInFamily(value);
            this.RaisePropertyChanged(nameof(ValuesInFamily));
            if (!_syncing && ValuesInFamily.Count > 0 && !ValuesInFamily.Contains(SelectedValue))
                SelectedValue = ValuesInFamily[0];
        }
    }

    private IReadOnlyList<string> _valuesInFamily = [];
    public IReadOnlyList<string> ValuesInFamily { get => _valuesInFamily; private set => _valuesInFamily = value; }

    private string _selectedValue = "";
    public string SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (value is null || _selectedValue == value) return;
            this.RaiseAndSetIfChanged(ref _selectedValue, value);
            if (!_syncing) Discriminator.Value = _toRealValue(value);
        }
    }

    /// <summary>Recompute the family list + current family's values from the live loaded set and
    /// discriminator value. Call after the loaded ExSN/SRX set changes.</summary>
    public void Reproject()
    {
        Families = BuildFamilies();
        SyncPickerFromValue(Discriminator.Value);
    }

    /// <summary>Families from the supplier, plus the current selection's family if the supplier dropped it
    /// (so an unloaded-board patch never blanks the family combo).</summary>
    private IReadOnlyList<string> BuildFamilies()
    {
        var fams = _familiesSupplier().ToList();
        var idx = IndexOf(Discriminator.Value);
        if (idx >= 0)
        {
            var cf = _familyOf(idx);
            if (!fams.Contains(cf)) fams.Add(cf);
        }
        return fams;
    }

    /// <summary>The (display-transformed) value names in a family, plus the current selection's display if
    /// the filter dropped it (so the value combo never blanks the current selection).</summary>
    private IReadOnlyList<string> BuildValuesInFamily(string family)
    {
        var list = _valuesIn(family).Select(i => _displayName(i, Discriminator.Options[i])).ToList();
        var idx = IndexOf(Discriminator.Value);
        if (idx >= 0 && _familyOf(idx) == family)
        {
            var cur = _displayName(idx, Discriminator.Options[idx]);
            if (!list.Contains(cur)) list.Add(cur);
        }
        return list;
    }

    private void SyncPickerFromValue(string valueName)
    {
        _syncing = true;
        try
        {
            var idx = Discriminator.Options is { Count: > 0 } ? IndexOf(valueName) : -1;
            if (idx >= 0)
            {
                var family = _familyOf(idx);
                if (_selectedFamily != family)
                {
                    _selectedFamily = family;
                    this.RaisePropertyChanged(nameof(SelectedFamily));
                }
                // Rebuild only when the (loaded-aware) list actually changed — keeps MFX churn-free and
                // makes Reproject() refresh when the loaded set changes the filtered/labelled list.
                var values = BuildValuesInFamily(family);
                if (!values.SequenceEqual(_valuesInFamily))
                {
                    ValuesInFamily = values;
                    this.RaisePropertyChanged(nameof(ValuesInFamily));
                }
                SelectedValue = _displayName(idx, Discriminator.Options[idx]);
            }
        }
        finally { _syncing = false; }
    }

    private int IndexOf(string valueName)
    {
        for (var i = 0; i < Discriminator.Options.Count; i++)
            if (Discriminator.Options[i] == valueName) return i;
        return -1;
    }

    private void OnDiscriminatorChanged(object? s, PropertyChangedEventArgs e)
    {
        // Rebuild the grid when a parent changes. Dependent VALUES are resynced elsewhere — the param
        // wrappers re-read on an IsParent write, and ParameterValueTemplate's ui2hw path resyncs
        // sub-switches — so here we only need the structural rebuild.
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(Recompute);
    }

    private void Recompute()
    {
        var valueName = Discriminator.Value;
        var relevant = _domain.GetRelevantParameters(false, false)
            .Where(p => p.ParSpec.Path.Contains(_gridPathSegment))
            .OrderBy(p => p.ParSpec.AddressInt).ToList();
        var labels = _friendlyLabels(valueName, relevant.Select(p => p.ParSpec.Name).ToList());
        Params.Clear();
        for (var i = 0; i < relevant.Count; i++) Params.Add(new DisplayParam(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasParams));
    }

    public void Dispose()
    {
        foreach (var d in _discriminators) d.PropertyChanged -= OnDiscriminatorChanged;
        _valueSub.Dispose();
        Discriminator.Dispose();
    }
}
