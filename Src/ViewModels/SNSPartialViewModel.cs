using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one SN-S partial: oscillator, amp, and amp envelope.</summary>
public sealed class SNSPartialViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Synth Tone Partial/"; // partial path prefix
    private const string CP = "SuperNATURAL Synth Tone Common/";  // common path prefix

    private readonly SNSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..2
    public string Title => $"Partial {Index + 1}";

    // --- Oscillator ---
    public ParamString OscWave { get; }
    public ParamString OscWaveVariation { get; }
    public ParamInt OscPitch { get; }
    public ParamInt OscDetune { get; }
    public ParamInt OscPulseWidth { get; }
    public ParamInt OscPulseWidthShift { get; }
    public ParamInt OscPwmDepth { get; }
    public ParamInt SuperSawDetune { get; }
    public ParamString WaveGain { get; }
    public ParamString WaveNumber { get; }

    // --- Amp ---
    public ParamInt AmpLevel { get; }
    public ParamInt AmpPan { get; }
    public ParamInt AmpVeloSens { get; }
    public ParamInt AmpKeyfollow { get; }

    // --- Aftertouch response (cutoff + level) ---
    public ParamInt CutoffAftertouchSens { get; }
    public ParamInt LevelAftertouchSens { get; }

    // --- Amp envelope ---
    public ParamInt AmpEnvAttack { get; }
    public ParamInt AmpEnvDecay { get; }
    public ParamInt AmpEnvSustain { get; }
    public ParamInt AmpEnvRelease { get; }

    // --- Filter ---
    public ParamString FilterMode { get; }
    public ParamBool FilterSlopeSteep { get; }   // on = -24 dB, off = -12 dB
    public ParamInt FilterCutoff { get; }
    public ParamInt FilterResonance { get; }
    public ParamInt FilterCutoffKeyfollow { get; }
    public ParamInt HpfCutoff { get; }
    public ParamInt FilterEnvVeloSens { get; }

    // --- Filter envelope ---
    public ParamInt FilterEnvAttack { get; }
    public ParamInt FilterEnvDecay { get; }
    public ParamInt FilterEnvSustain { get; }
    public ParamInt FilterEnvRelease { get; }
    public ParamInt FilterEnvDepth { get; }

    // --- Pitch envelope (bipolar AD) ---
    public ParamInt PitchEnvAttack { get; }
    public ParamInt PitchEnvDecay { get; }
    public ParamInt PitchEnvDepth { get; }

    // --- Motion (two LFOs) ---
    public LfoPanelViewModel AutomaticMotion { get; }
    public LfoPanelViewModel ModWheelMotion { get; }

    private int _activeEnvelope; // 0 = Amp, 1 = Filter (bound to the dual control toggle)
    public int ActiveEnvelope { get => _activeEnvelope; set => this.RaiseAndSetIfChanged(ref _activeEnvelope, value); }

    // --- Card on/off (Common Partial{n} Switch) ---
    public ParamBool IsOn { get; }

    /// <summary>The card on/off switch as a bindable property. A USER toggle (target→source binding)
    /// runs this setter, which also selects this partial — mirroring <see cref="Solo"/>. Programmatic
    /// IsOn.Value changes (audition recompute, preset load) go through IsOn.Value directly and never call
    /// this setter, so they don't move the selection. The switch stays in sync via OnIsOnChanged.</summary>
    public bool Enabled
    {
        get => IsOn.Value;
        set { IsOn.Value = value; _parent.SelectedPartial = this; }
    }

    // --- Audition (transient solo/mute; coordinated by the parent editor VM, not sent as params) ---
    private bool _solo;
    public bool Solo
    {
        get => _solo;
        set { if (_solo == value) return; this.RaiseAndSetIfChanged(ref _solo, value); _parent.RecomputeAudition(); _parent.SelectedPartial = this; }
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

    // Params copied/pasted/initialised (oscillator + amp + amp env, NOT the common on/off switch).
    private readonly IReadOnlyList<IParam> _editable;

    public SNSPartialViewModel(SNSynthToneEditorViewModel parent, DomainBase partialDomain,
        DomainBase commonDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> commonByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        var byPath = ToDict(partialDomain);

        ParamInt PI(string name, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + name], writer, min, max));
        ParamString PS(string name, IReadOnlyList<string>? opts = null) => Track(new ParamString(partialDomain, byPath[PP + name], writer, opts));

        OscWave = PS("OSC Wave");
        OscWaveVariation = PS("OSC Wave Variation");
        OscPitch = PI("OSC Pitch", -24, 24);
        OscDetune = PI("OSC Detune", -50, 50);
        OscPulseWidth = PI("OSC Pulse Width", 0, 127);
        OscPulseWidthShift = PI("OSC Pulse Width shift", 0, 127);
        OscPwmDepth = PI("OSC Pulse Width Mod Depth", 0, 127);
        SuperSawDetune = PI("Super Saw Detune", 0, 127);
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        WaveNumber = PS("Wave Number");

        AmpLevel = PI("AMP Level", 0, 127);
        AmpPan = PI("AMP Pan", -64, 63);
        AmpVeloSens = PI("AMP Level Velocity Sens", -63, 63);
        AmpKeyfollow = PI("AMP Level Keyfollow", -100, 100);

        AmpEnvAttack = PI("AMP Env Attack Time", 0, 127);
        AmpEnvDecay = PI("AMP Env Decay Time", 0, 127);
        AmpEnvSustain = PI("AMP Env Sustain Level", 0, 127);
        AmpEnvRelease = PI("AMP Env Release Time", 0, 127);

        FilterMode = PS("Filter Mode");
        FilterSlopeSteep = Track(new ParamBool(partialDomain, byPath[PP + "Filter Slope"], writer, "-24", "-12"));
        FilterCutoff = PI("Filter Cutoff", 0, 127);
        FilterResonance = PI("Filter Resonance", 0, 127);
        FilterCutoffKeyfollow = PI("Filter Cutoff Keyfollow", -100, 100);
        HpfCutoff = PI("HPF Cutoff", 0, 127);
        FilterEnvVeloSens = PI("Filter Env Velocity Sens", -63, 63);

        CutoffAftertouchSens = PI("Cutoff Aftertouch Sens", -63, 63);
        LevelAftertouchSens = PI("Level Aftertouch Sens", -63, 63);

        FilterEnvAttack = PI("Filter Env Attack Time", 0, 127);
        FilterEnvDecay = PI("Filter Env Decay Time", 0, 127);
        FilterEnvSustain = PI("Filter Env Sustain Level", 0, 127);
        FilterEnvRelease = PI("Filter Env Release Time", 0, 127);
        FilterEnvDepth = PI("Filter Env Depth", -63, 63);

        PitchEnvAttack = PI("OSC Pitch Env Attack Time", 0, 127);
        PitchEnvDecay = PI("OSC Pitch Env Decay", 0, 127);
        PitchEnvDepth = PI("OSC Pitch Env Depth", -63, 63);

        IsOn = Track(new ParamBool(commonDomain, commonByPath[CP + $"Partial{index + 1} Switch"], writer));

        AutomaticMotion = Track(new LfoPanelViewModel(partialDomain, byPath, writer, "LFO ", "Automatic Motion", false));
        ModWheelMotion = Track(new LfoPanelViewModel(partialDomain, byPath, writer, "Modulation LFO ", "Mod Wheel Motion", true));

        _editable = new IParam[]
        {
            OscWave, OscWaveVariation, OscPitch, OscDetune, OscPulseWidth, OscPulseWidthShift,
            OscPwmDepth, SuperSawDetune, WaveGain, WaveNumber,
            AmpLevel, AmpPan, AmpVeloSens, AmpKeyfollow,
            AmpEnvAttack, AmpEnvDecay, AmpEnvSustain, AmpEnvRelease,
            FilterMode, FilterSlopeSteep, FilterCutoff, FilterResonance, FilterCutoffKeyfollow, HpfCutoff, FilterEnvVeloSens,
            FilterEnvAttack, FilterEnvDecay, FilterEnvSustain, FilterEnvRelease, FilterEnvDepth,
            PitchEnvAttack, PitchEnvDecay, PitchEnvDepth,
            CutoffAftertouchSens, LevelAftertouchSens
        }
        .Concat(AutomaticMotion.Params).Concat(ModWheelMotion.Params).ToArray();

        // Re-raise the conditional-visibility flags and card summary when the wave / level / pan change.
        OscWave.PropertyChanged += OnOscWaveChanged;
        AmpLevel.PropertyChanged += OnSummaryChanged;
        AmpPan.PropertyChanged += OnSummaryChanged;
        IsOn.PropertyChanged += OnIsOnChanged;
        FilterMode.PropertyChanged += OnFilterSummaryChanged;
        FilterCutoff.PropertyChanged += OnFilterSummaryChanged;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T wrapper) where T : IDisposable
    {
        _wrappers.Add(wrapper);
        return wrapper;
    }

    // --- Conditional oscillator controls ---
    public bool ShowsPulseWidth => SnsOscillatorRules.ShowsPulseWidth(OscWave.Value);
    public bool ShowsSuperSawDetune => SnsOscillatorRules.ShowsSuperSawDetune(OscWave.Value);
    public bool ShowsPcm => SnsOscillatorRules.ShowsPcm(OscWave.Value);

    private void OnOscWaveChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ParamString.Value)) return;
        this.RaisePropertyChanged(nameof(ShowsPulseWidth));
        this.RaisePropertyChanged(nameof(ShowsSuperSawDetune));
        this.RaisePropertyChanged(nameof(ShowsPcm));
        this.RaisePropertyChanged(nameof(WaveSummary));
    }

    private void OnSummaryChanged(object? s, PropertyChangedEventArgs e)
        => this.RaisePropertyChanged(nameof(PanLabel));

    private void OnFilterSummaryChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => this.RaisePropertyChanged(nameof(FilterSummary));

    private void OnIsOnChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParamBool.Value)) this.RaisePropertyChanged(nameof(Enabled));
    }

    // --- Card summary ---
    public string WaveSummary => OscWave.Value;
    public string PanLabel => AmpPan.Value == 0 ? "C" : AmpPan.Value < 0 ? $"L{-AmpPan.Value}" : $"R{AmpPan.Value}";
    public string FilterSummary => $"{SnsFilterRules.Abbrev(FilterMode.Value)} {FilterCutoff.Value}";

    // --- Utilities (edit-buffer only; no save) ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);

    public void Paste()
    {
        if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data);
    }

    public void Init() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "OSC Wave"] = "Saw",
        [PP + "OSC Wave Variation"] = "A",
        [PP + "OSC Pitch"] = "0",
        [PP + "OSC Detune"] = "0",
        [PP + "OSC Pulse Width"] = "0",
        [PP + "OSC Pulse Width shift"] = "0",
        [PP + "OSC Pulse Width Mod Depth"] = "0",
        [PP + "Super Saw Detune"] = "0",
        [PP + "Wave Gain"] = "0",
        [PP + "AMP Level"] = "100",
        [PP + "AMP Pan"] = "0",
        [PP + "AMP Level Velocity Sens"] = "0",
        [PP + "AMP Level Keyfollow"] = "0",
        [PP + "AMP Env Attack Time"] = "0",
        [PP + "AMP Env Decay Time"] = "64",
        [PP + "AMP Env Sustain Level"] = "110",
        [PP + "AMP Env Release Time"] = "30",
        [PP + "Filter Mode"] = "Low pass",
        [PP + "Filter Slope"] = "-24",
        [PP + "Filter Cutoff"] = "127",
        [PP + "Filter Resonance"] = "0",
        [PP + "Filter Cutoff Keyfollow"] = "0",
        [PP + "HPF Cutoff"] = "0",
        [PP + "Filter Env Velocity Sens"] = "0",
        [PP + "Cutoff Aftertouch Sens"] = "0",
        [PP + "Level Aftertouch Sens"] = "0",
        [PP + "Filter Env Attack Time"] = "0",
        [PP + "Filter Env Decay Time"] = "64",
        [PP + "Filter Env Sustain Level"] = "127",
        [PP + "Filter Env Release Time"] = "30",
        [PP + "Filter Env Depth"] = "0",
        [PP + "OSC Pitch Env Attack Time"] = "0",
        [PP + "OSC Pitch Env Decay"] = "64",
        [PP + "OSC Pitch Env Depth"] = "0",
        [PP + "LFO Shape"] = "Triangle",
        [PP + "LFO Rate"] = "64",
        [PP + "LFO Tempo Sync Switch"] = "OFF",
        [PP + "LFO Tempo Sync Note"] = "1/4",
        [PP + "LFO Fade Time"] = "0",
        [PP + "LFO Key Trigger"] = "OFF",
        [PP + "LFO Pitch Depth"] = "0",
        [PP + "LFO Filter Depth"] = "0",
        [PP + "LFO AMP Depth"] = "0",
        [PP + "LFO Pan Depth"] = "0",
        [PP + "Modulation LFO Shape"] = "Triangle",
        [PP + "Modulation LFO Rate"] = "64",
        [PP + "Modulation LFO Tempo Sync Switch"] = "OFF",
        [PP + "Modulation LFO Tempo Sync Note"] = "1/4",
        [PP + "Modulation LFO Pitch Depth"] = "0",
        [PP + "Modulation LFO Filter Depth"] = "0",
        [PP + "Modulation LFO AMP Depth"] = "0",
        [PP + "Modulation LFO Pan Depth"] = "0",
        [PP + "Modulation LFO Rate Control"] = "0"
    };

    public void Dispose()
    {
        OscWave.PropertyChanged -= OnOscWaveChanged;
        AmpLevel.PropertyChanged -= OnSummaryChanged;
        AmpPan.PropertyChanged -= OnSummaryChanged;
        IsOn.PropertyChanged -= OnIsOnChanged;
        FilterMode.PropertyChanged -= OnFilterSummaryChanged;
        FilterCutoff.PropertyChanged -= OnFilterSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
