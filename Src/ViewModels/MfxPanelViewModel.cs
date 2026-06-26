using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
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
/// </summary>
public sealed class MfxPanelViewModel : ViewModelBase, IDisposable
{
    private const string PFX = "SuperNATURAL Synth Tone Common MFX/";
    private const string Thru = "Thru";

    private readonly DomainBase _mfxDomain;
    private readonly List<FullyQualifiedParameter> _allMfxParams; // all variants, context-independent
    private readonly IDisposable _typeSub;

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
        var byPath = _allMfxParams.ToDictionary(p => p.ParSpec.Path);

        Type = new ParamString(mfxDomain, byPath[PFX + "MFX Type"], writer);
        ChorusSend = new ParamInt(mfxDomain, byPath[PFX + "MFX Chorus Send Level"], writer, 0, 127);
        ReverbSend = new ParamInt(mfxDomain, byPath[PFX + "MFX Reverb Send Level"], writer, 0, 127);

        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Fires immediately with the current type, then on every change (picker / hardware / raw tab).
        _typeSub = Type.WhenAnyValue(t => t.Value).Subscribe(OnTypeChanged);
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

        RecomputeTypeParameters(typeName);
        this.RaisePropertyChanged(nameof(Bypass));
    }

    private int IndexOfType(string typeName)
    {
        for (var i = 0; i < Type.Options.Count; i++)
            if (Type.Options[i] == typeName) return i;
        return -1;
    }

    private void RecomputeTypeParameters(string typeName)
    {
        var relevant = _allMfxParams
            .Where(p => !p.ParSpec.Reserved
                        && p.ParSpec.Path.Contains("/MFX Parameter ")
                        && p.ParSpec.ParentCtrlDispValue == typeName)
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
        _typeSub.Dispose();
        AdvancedMfxCommand.Dispose();
        Type.Dispose();
        ChorusSend.Dispose();
        ReverbSend.Dispose();
    }
}
