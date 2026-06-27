using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one PCM Synth partial. Phase 1 skeleton: identity, on/off
/// (the PMT per-partial switch), and the Solo/Mute audition flags. Sound/Motion params are added
/// in later phases; <see cref="_editable"/> (copy/paste/init buffer) grows with them.</summary>
public sealed class PCMPartialViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";

    private readonly PCMSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..3
    public string Title => $"Partial {Index + 1}";

    /// <summary>Card on/off — the PMT per-partial switch (audition saves/restores this).</summary>
    public ParamBool IsOn { get; }

    // Params copied/pasted/initialised (empty until later phases add Sound/Motion controls).
    private readonly IReadOnlyList<IParam> _editable = Array.Empty<IParam>();

    public PCMPartialViewModel(PCMSynthToneEditorViewModel parent,
        DomainBase pmtDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> pmtByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        IsOn = Track(new ParamBool(pmtDomain, pmtByPath[PMT + $"PMT {index + 1} Partial Switch"], writer));
    }

    private T Track<T>(T wrapper) where T : IDisposable { _wrappers.Add(wrapper); return wrapper; }

    // --- Audition (transient solo/mute; coordinated by the parent editor VM, not sent as params) ---
    private bool _solo;
    public bool Solo
    {
        get => _solo;
        set { if (_solo == value) return; this.RaiseAndSetIfChanged(ref _solo, value); _parent.RecomputeAudition(); }
    }

    private bool _mute;
    public bool Mute
    {
        get => _mute;
        set { if (_mute == value) return; this.RaiseAndSetIfChanged(ref _mute, value); _parent.RecomputeAudition(); }
    }

    /// <summary>Set solo/mute without triggering a parent recompute (used for bulk clear).</summary>
    internal void SetAuditionFlags(bool solo, bool mute)
    {
        this.RaiseAndSetIfChanged(ref _solo, solo, nameof(Solo));
        this.RaiseAndSetIfChanged(ref _mute, mute, nameof(Mute));
    }

    // --- Copy / Paste / Init (edit-buffer only; no save). No-op until _editable is populated. ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);
    public void Paste() { if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    public void Init() { }

    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
    }
}
