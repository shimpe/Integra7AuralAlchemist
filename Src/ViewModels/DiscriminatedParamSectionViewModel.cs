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
/// </summary>
public sealed class DiscriminatedParamSectionViewModel : ViewModelBase, IDisposable
{
    private readonly DomainBase _domain;
    private readonly List<FullyQualifiedParameter> _allParams;
    private readonly List<FullyQualifiedParameter> _discriminators;
    private readonly IDisposable _valueSub;
    private readonly string _gridPathSegment;
    private readonly IReadOnlyList<string> _families;
    private readonly Func<int, string> _familyOf;
    private readonly Func<string, IReadOnlyList<int>> _valuesIn;
    private readonly Func<string, IReadOnlyList<string>, IReadOnlyList<string>> _friendlyLabels;
    private bool _syncing;

    /// <summary>The discriminator wrapper (e.g. MFX Type / Instrument). Exposed for engine-specific extras.</summary>
    public ParamString Discriminator { get; }
    public IReadOnlyList<string> Families => _families;
    public ObservableCollection<DisplayParam> Params { get; } = [];
    public bool HasParams => Params.Count > 0;

    public DiscriminatedParamSectionViewModel(DomainBase domain, ThrottledParameterWriter writer,
        string discriminatorLeafName, string gridPathSegment,
        IReadOnlyList<string> families, Func<int, string> familyOf,
        Func<string, IReadOnlyList<int>> valuesIn,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>> friendlyLabels)
    {
        _domain = domain;
        _gridPathSegment = gridPathSegment;
        _families = families; _familyOf = familyOf; _valuesIn = valuesIn; _friendlyLabels = friendlyLabels;
        _allParams = domain.GetRelevantParameters(true, true);

        var disc = _allParams.First(p => p.ParSpec.Name == discriminatorLeafName);
        // Options from Repr (enum) or Discrete (e.g. Instrument list).
        var opts = disc.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                   ?? disc.ParSpec.Discrete?.Select(d => d.Item2).ToList()
                   ?? new List<string>();
        Discriminator = new ParamString(domain, disc, writer, opts);

        var parentPaths = _allParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        _discriminators = _allParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        _valueSub = Discriminator.WhenAnyValue(t => t.Value).Subscribe(SyncPickerFromValue);
        Recompute();
    }

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            ValuesInFamily = _valuesIn(value).Select(i => Discriminator.Options[i]).ToList();
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
            if (!_syncing) Discriminator.Value = value;
        }
    }

    private void SyncPickerFromValue(string valueName)
    {
        _syncing = true;
        try
        {
            var idx = Discriminator.Options is { Count: > 0 } ? IndexOf(valueName) : -1;
            if (idx >= 0)
            {
                SelectedFamily = _familyOf(idx);
                if (!ValuesInFamily.Contains(valueName))
                {
                    ValuesInFamily = _valuesIn(SelectedFamily).Select(i => Discriminator.Options[i]).ToList();
                    this.RaisePropertyChanged(nameof(ValuesInFamily));
                }
                SelectedValue = valueName;
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
