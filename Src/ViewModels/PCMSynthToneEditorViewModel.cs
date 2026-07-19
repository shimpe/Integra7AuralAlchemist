using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly PCM Synth editor for ONE part: header (Common/Common 2), a 4-partial rack with
/// Solo/Mute audition, and the shared MFX panel. Sound/Motion/Zones/Response tab bodies are filled
/// in later phases.</summary>
public sealed partial class PCMSynthToneEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;

    public PCMSynthToneHeaderViewModel Header { get; }
    public ObservableCollection<PCMPartialViewModel> Partials { get; } = [];
    public MfxPanelViewModel Mfx { get; }
    public PcmPmtPanelViewModel Pmt { get; }
    public ToneNoteRailViewModel NoteRail { get; }

    /// <summary>Shared partial copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? PartialClipboard { get; set; }

    public PCMSynthToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null,
        Func<int, int, System.Threading.Tasks.Task>? noteOn = null, Func<int, System.Threading.Tasks.Task>? noteOff = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(noteOn, noteOff);

        var common = domain.PCMSynthToneCommon(partNo);
        var commonByPath = ToDict(common);
        var common2 = domain.PCMSynthToneCommon2(partNo);
        var common2ByPath = ToDict(common2);
        var pmt = domain.PCMSynthTonePMT(partNo);
        var pmtByPath = ToDict(pmt);

        Header = new PCMSynthToneHeaderViewModel(common, commonByPath, common2, common2ByPath, _writer);

        for (var i = 0; i < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; i++)
            Partials.Add(new PCMPartialViewModel(this, domain.PCMSynthTonePartial(partNo, i),
                pmt, pmtByPath, i, _writer));

        _selectedPartial = Partials[0];

        Mfx = new MfxPanelViewModel(domain.PCMSynthToneCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCM-SYN-MFX", null));

        Pmt = new PcmPmtPanelViewModel(pmt, pmtByPath, _writer);
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private PCMPartialViewModel _selectedPartial;
    public PCMPartialViewModel SelectedPartial
    {
        get => _selectedPartial;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedPartial)) return;
            this.RaiseAndSetIfChanged(ref _selectedPartial, value);
        }
    }

    [ReactiveCommand] public void CopyPartial() => SelectedPartial.Copy();
    [ReactiveCommand] public void PastePartial() => SelectedPartial.Paste();
    [ReactiveCommand] public void InitPartial() => SelectedPartial.Init();

    // Open the raw "Advanced — …" tabs (the friendly Editor tab owns Tag "PCMS").
    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("PCM-SYN-COMMON", null);
    [ReactiveCommand] public void AdvancedPartial() => _navigateToRawTab?.Invoke("PCM-SYN-PARTIALS", SelectedPartial.Index);

    // --- Partial Solo/Mute audition (reuses the engine-agnostic PartialAudition helper) ---
    private readonly bool[] _savedSwitches = new bool[Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE];
    private bool _auditing;
    private bool _suppressRecompute;

    private bool _isAuditioning;
    /// <summary>True while any partial is soloed or muted (drives the banner + disables card on/off).</summary>
    public bool IsAuditioning
    {
        get => _isAuditioning;
        private set => this.RaiseAndSetIfChanged(ref _isAuditioning, value);
    }

    /// <summary>Recompute effective partial on/off from the solo/mute flags. Snapshots the real
    /// switches when an audition begins and restores them when it ends (safe audition).</summary>
    public void RecomputeAudition()
    {
        if (_suppressRecompute) return;

        var solo = Partials.Select(p => p.Solo).ToList();
        var mute = Partials.Select(p => p.Mute).ToList();
        var active = PartialAudition.IsAuditioning(solo, mute);

        if (active && !_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) _savedSwitches[i] = Partials[i].IsOn.Value;
            _auditing = true;
        }

        if (active)
        {
            var saved = new bool[Partials.Count];
            for (var i = 0; i < Partials.Count; i++) saved[i] = _savedSwitches[i];
            var eff = PartialAudition.Effective(saved, solo, mute);
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = eff[i];
        }
        else if (_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = _savedSwitches[i];
            _auditing = false;
        }

        IsAuditioning = active;
    }

    /// <summary>Clear all solo/mute and restore the saved switches (single recompute).</summary>
    [ReactiveCommand]
    public void ClearAudition()
    {
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        _suppressRecompute = false;
        RecomputeAudition();
    }

    /// <summary>Restore the pre-audition partial on/off switches to hardware (awaited) and clear all
    /// solo/mute. No-op when not auditioning. Called before a preset change so the patch is restored first.</summary>
    public async System.Threading.Tasks.Task RestoreAuditionAsync(IMidiLease? lease = null)
    {
        if (!_auditing) return;
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        for (var i = 0; i < Partials.Count; i++) await Partials[i].IsOn.WriteImmediateAsync(_savedSwitches[i], lease);
        _suppressRecompute = false;
        _auditing = false;
        IsAuditioning = false;
    }

    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        Mfx.Dispose();
        Pmt.Dispose();
        NoteRail.Dispose();
        _writer.Dispose();
    }
}
