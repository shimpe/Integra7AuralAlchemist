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

/// <summary>Friendly SuperNATURAL Synth editor for ONE part: header + 3 partials.</summary>
public sealed partial class SNSynthToneEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;

    public SNSynthToneHeaderViewModel Header { get; }
    public ObservableCollection<SNSPartialViewModel> Partials { get; } = [];
    public MfxPanelViewModel Mfx { get; }
    public ToneNoteRailViewModel NoteRail { get; }

    /// <summary>Shared partial copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? PartialClipboard { get; set; }

    public SNSynthToneEditorViewModel(Integra7Domain domain, int partNo, Action<string, int?>? navigateToRawTab = null,
        Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(playNote);

        var common = domain.SNSynthToneCommon(partNo);
        var commonByPath = ToDict(common);
        Header = new SNSynthToneHeaderViewModel(common, commonByPath, _writer);

        for (var i = 0; i < Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; i++)
            Partials.Add(new SNSPartialViewModel(this, domain.SNSynthTonePartial(partNo, i),
                common, commonByPath, i, _writer));

        _selectedPartial = Partials[0];

        Mfx = new MfxPanelViewModel(domain.SNSynthToneCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("SN-S-MFX", null));
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private SNSPartialViewModel _selectedPartial;
    public SNSPartialViewModel SelectedPartial
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

    // Carry the friendly editor's selected partial so the raw "Advanced — Partials" tab opens on
    // the same partial the user was editing (otherwise they'd land on whatever partial was last
    // viewed there and silently edit the wrong one).
    [ReactiveCommand] public void AdvancedOscillator() => _navigateToRawTab?.Invoke("SN-S-PARTIALS", SelectedPartial.Index);
    [ReactiveCommand] public void AdvancedAmp() => _navigateToRawTab?.Invoke("SN-S-PARTIALS", SelectedPartial.Index);
    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("SN-S-COMMON", null);

    // --- Partial Solo/Mute audition ---
    private readonly bool[] _savedSwitches = new bool[Constants.NO_OF_PARTIALS_SN_SYNTH_TONE];
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
    public async System.Threading.Tasks.Task RestoreAuditionAsync()
    {
        if (!_auditing) return;
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        for (var i = 0; i < Partials.Count; i++) await Partials[i].IsOn.WriteImmediateAsync(_savedSwitches[i]);
        _suppressRecompute = false;
        _auditing = false;
        IsAuditioning = false;
    }

    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        Mfx.Dispose();
        NoteRail.Dispose();
        _writer.Dispose();
    }
}
