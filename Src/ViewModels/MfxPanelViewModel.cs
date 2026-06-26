using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One per-type MFX parameter for the dynamic grid: the live FQP plus its cleaned label.</summary>
public sealed record MfxParamDisplay(FullyQualifiedParameter Param, string Label);

/// <summary>
/// Friendly, tone-wide Multi-Effect panel. Two-combo (family -> type) picker, bypass (Thru),
/// Chorus/Reverb send faders, and the current effect type's parameters rendered dynamically from the
/// MFX domain (filtered by the MFX Type discriminator) using DataTemplateProvider.ParameterValueTemplate.
/// Engine-agnostic: pass ANY engine's "Common MFX" <see cref="DomainBase"/> (SN-S / SN-A / SN-D /
/// PCM Synth / PCM Drum) — the MFX parameter set is identical across engines, only the path prefix differs.
/// </summary>
public sealed class MfxPanelViewModel : ViewModelBase, IDisposable
{
    private const string Thru = "Thru";

    private readonly DomainBase _mfxDomain;
    private readonly List<FullyQualifiedParameter> _allMfxParams; // all variants, context-independent
    private readonly IDisposable _typeSub;
    private readonly List<FullyQualifiedParameter> _discriminators;

    private bool _syncing;            // true while syncing the picker from Type (suppress write-back)
    private string _lastEffectType = "Equalizer";

    public ParamString Type { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    public IReadOnlyList<string> Families { get; } = MfxCatalog.Families.Select(f => f.Name).ToList();
    public ObservableCollection<MfxParamDisplay> TypeParameters { get; } = [];
    public bool HasTypeParameters => TypeParameters.Count > 0;

    public ReactiveCommand<Unit, Unit> AdvancedMfxCommand { get; }

    public MfxPanelViewModel(DomainBase mfxDomain, ThrottledParameterWriter writer, Action navigateToAdvanced)
    {
        _mfxDomain = mfxDomain;
        _allMfxParams = mfxDomain.GetRelevantParameters(true, true);

        // Look up by leaf name (not full path) so the panel works for ANY engine's Common MFX domain —
        // the MFX param set is identical across engines, only the path prefix differs.
        FullyQualifiedParameter ByName(string name) => _allMfxParams.First(p => p.ParSpec.Name == name);

        Type = new ParamString(mfxDomain, ByName("MFX Type"), writer);
        ChorusSend = new ParamInt(mfxDomain, ByName("MFX Chorus Send Level"), writer, 0, 127);
        ReverbSend = new ParamInt(mfxDomain, ByName("MFX Reverb Send Level"), writer, 0, 127);

        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Recompute the per-type grid only when a *discriminator* (the MFX Type or a sub-switch such
        // as a delay's ms/Note selector) changes — not on every value edit, which would tear down a
        // control mid-drag. Discriminators are the params referenced as a parent by some MFX param.
        var parentPaths = _allMfxParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet();
        _discriminators = _allMfxParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        // Keeps the family/type pickers + Bypass in sync immediately; the grid follows via the
        // discriminator subscription once the underlying FQP settles.
        _typeSub = Type.WhenAnyValue(t => t.Value).Subscribe(OnTypeChanged);

        RecomputeTypeParameters(); // initial population
    }

    // ---- Picker projection -------------------------------------------------

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            TypesInFamily = MfxCatalog.TypesIn(value).Select(i => Type.Options[i]).ToList();
            this.RaisePropertyChanged(nameof(TypesInFamily));
            if (!_syncing && TypesInFamily.Count > 0 && !TypesInFamily.Contains(SelectedType))
                SelectedType = TypesInFamily[0]; // user switched family -> pick its first effect
        }
    }

    private IReadOnlyList<string> _typesInFamily = [];
    public IReadOnlyList<string> TypesInFamily
    {
        get => _typesInFamily;
        private set => _typesInFamily = value;
    }

    private string _selectedType = "";
    public string SelectedType
    {
        get => _selectedType;
        set
        {
            if (value is null || _selectedType == value) return;
            this.RaiseAndSetIfChanged(ref _selectedType, value);
            if (!_syncing) Type.Value = value; // write to hardware (throttled) -> OnTypeChanged
        }
    }

    // ---- Bypass (a view over Type) ----------------------------------------

    public bool Bypass
    {
        get => Type.Value == Thru;
        set
        {
            if (value == Bypass) return;
            if (value)
            {
                if (Type.Value != Thru) _lastEffectType = Type.Value;
                Type.Value = Thru;
            }
            else
            {
                Type.Value = _lastEffectType;
            }
            // Type.Value change -> OnTypeChanged raises Bypass + re-syncs picker + grid.
        }
    }

    // ---- Reaction to any MFX Type change ----------------------------------

    private void OnTypeChanged(string typeName)
    {
        _syncing = true;
        try
        {
            var idx = Type.Options is { Count: > 0 } ? IndexOfType(typeName) : -1;
            if (idx >= 0)
            {
                SelectedFamily = MfxCatalog.FamilyOf(idx);
                if (!TypesInFamily.Contains(typeName))
                {
                    // Family unchanged but list not yet built (e.g. initial) — rebuild for safety.
                    TypesInFamily = MfxCatalog.TypesIn(SelectedFamily).Select(i => Type.Options[i]).ToList();
                    this.RaisePropertyChanged(nameof(TypesInFamily));
                }
                SelectedType = typeName;
            }
        }
        finally { _syncing = false; }

        this.RaisePropertyChanged(nameof(Bypass));
    }

    private void OnDiscriminatorChanged(object? s, PropertyChangedEventArgs e)
    {
        // May fire on a MIDI / throttle-timer thread; marshal before touching TypeParameters.
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(RecomputeTypeParameters);
    }

    private int IndexOfType(string typeName)
    {
        for (var i = 0; i < Type.Options.Count; i++)
            if (Type.Options[i] == typeName) return i;
        return -1;
    }

    // Build the grid from the full context-valid set so 2-level discriminator chains
    // (MFX Type -> switch -> value, e.g. a delay's ms/Note time) are honoured.
    // GetRelevantParameters(false,false) excludes reserved + invalid-in-context and follows the chain.
    private void RecomputeTypeParameters()
    {
        var typeName = Type.Value;
        var relevant = _mfxDomain.GetRelevantParameters(false, false)
            .Where(p => p.ParSpec.Path.Contains("/MFX Parameter "))
            .OrderBy(p => p.ParSpec.AddressInt)
            .ToList();
        var labels = MfxCatalog.FriendlyParamNames(typeName, relevant.Select(p => p.ParSpec.Name).ToList());

        TypeParameters.Clear();
        for (var i = 0; i < relevant.Count; i++)
            TypeParameters.Add(new MfxParamDisplay(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasTypeParameters));
    }

    public void Dispose()
    {
        foreach (var d in _discriminators) d.PropertyChanged -= OnDiscriminatorChanged;
        _typeSub.Dispose();
        AdvancedMfxCommand.Dispose();
        Type.Dispose();
        ChorusSend.Dispose();
        ReverbSend.Dispose();
    }
}
