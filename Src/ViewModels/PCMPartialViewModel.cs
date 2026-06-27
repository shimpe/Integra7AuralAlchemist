using System;
using System.Collections.Generic;
using System.ComponentModel;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one PCM Synth partial: wave selection, output/amp, filter (TVF),
/// the three rate/level envelopes (pitch/TVF/TVA), and bias. On/off is the PMT per-partial switch.
/// Solo/Mute audition is coordinated by the parent editor VM.</summary>
public sealed class PCMPartialViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";                 // partial path prefix
    private const string PMT = "PCM Synth Tone Partial Mix Table/";      // pmt path prefix

    private readonly PCMSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..3
    public string Title => $"Partial {Index + 1}";

    // --- Wave ---
    public ParamString WaveGroupType { get; }   // Internal / SRX
    public ParamString WaveNumberL { get; }     // wave name (mono / left)
    public ParamString WaveNumberR { get; }     // wave name (right, stereo)
    public ParamString WaveGain { get; }        // -6 / 0 / 6 / 12 dB
    public ParamBool WaveFxmSwitch { get; }
    public ParamInt WaveFxmColor { get; }
    public ParamInt WaveFxmDepth { get; }

    // --- Output / Amp ---
    public ParamInt PartialLevel { get; }
    public ParamInt PartialPan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    // --- Filter (TVF) ---
    public ParamString TvfFilterType { get; }
    public ParamInt TvfCutoff { get; }
    public ParamInt TvfResonance { get; }
    public ParamInt TvfEnvDepth { get; }

    // --- Pitch envelope (TVP): 4 times, 5 bipolar levels + depth ---
    public ParamInt PitchEnvDepth { get; }
    public ParamInt PitchEnvTime1 { get; }
    public ParamInt PitchEnvTime2 { get; }
    public ParamInt PitchEnvTime3 { get; }
    public ParamInt PitchEnvTime4 { get; }
    public ParamInt PitchEnvLevel0 { get; }
    public ParamInt PitchEnvLevel1 { get; }
    public ParamInt PitchEnvLevel2 { get; }
    public ParamInt PitchEnvLevel3 { get; }
    public ParamInt PitchEnvLevel4 { get; }

    // --- Filter envelope (TVF): 4 times, 5 levels ---
    public ParamInt TvfEnvTime1 { get; }
    public ParamInt TvfEnvTime2 { get; }
    public ParamInt TvfEnvTime3 { get; }
    public ParamInt TvfEnvTime4 { get; }
    public ParamInt TvfEnvLevel0 { get; }
    public ParamInt TvfEnvLevel1 { get; }
    public ParamInt TvfEnvLevel2 { get; }
    public ParamInt TvfEnvLevel3 { get; }
    public ParamInt TvfEnvLevel4 { get; }

    // --- Amp envelope (TVA): 4 times, 3 levels (start/end implicitly 0) ---
    public ParamInt TvaEnvTime1 { get; }
    public ParamInt TvaEnvTime2 { get; }
    public ParamInt TvaEnvTime3 { get; }
    public ParamInt TvaEnvTime4 { get; }
    public ParamInt TvaEnvLevel1 { get; }
    public ParamInt TvaEnvLevel2 { get; }
    public ParamInt TvaEnvLevel3 { get; }

    // --- Bias ---
    public ParamInt BiasLevel { get; }
    public ParamInt BiasPosition { get; }
    public ParamString BiasDirection { get; }

    /// <summary>Card on/off — the PMT per-partial switch (audition saves/restores this).</summary>
    public ParamBool IsOn { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public PCMPartialViewModel(PCMSynthToneEditorViewModel parent, DomainBase partialDomain,
        DomainBase pmtDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> pmtByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        var byPath = ToDict(partialDomain);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(partialDomain, byPath[PP + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(partialDomain, byPath[PP + n], writer));

        WaveGroupType = PS("Wave Group Type");
        WaveNumberL = PS("Wave Number L (Mono)");
        WaveNumberR = PS("Wave Number R");
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        WaveFxmSwitch = PB("Wave FXM Switch");
        WaveFxmColor = PI("Wave FXM Color", 1, 4);
        WaveFxmDepth = PI("Wave FXM Depth", 0, 16);

        PartialLevel = PI("Partial Level", 0, 127);
        PartialPan = PI("Partial Pan", -64, 63);
        CoarseTune = PI("Partial Coarse Tune", -48, 48);
        FineTune = PI("Partial Fine Tune", -50, 50);
        ChorusSend = PI("Partial Chorus Send Level", 0, 127);
        ReverbSend = PI("Partial Reverb Send Level", 0, 127);

        TvfFilterType = PS("TVF Filter Type");
        TvfCutoff = PI("TVF Cutoff Frequency", 0, 127);
        TvfResonance = PI("TVF Resonance", 0, 127);
        TvfEnvDepth = PI("TVF Env Depth", -63, 63);

        PitchEnvDepth = PI("Pitch Env Depth", -12, 12);
        PitchEnvTime1 = PI("Pitch Env Time 1", 0, 127);
        PitchEnvTime2 = PI("Pitch Env Time 2", 0, 127);
        PitchEnvTime3 = PI("Pitch Env Time 3", 0, 127);
        PitchEnvTime4 = PI("Pitch Env Time 4", 0, 127);
        PitchEnvLevel0 = PI("Pitch Env Level 0", -63, 63);
        PitchEnvLevel1 = PI("Pitch Env Level 1", -63, 63);
        PitchEnvLevel2 = PI("Pitch Env Level 2", -63, 63);
        PitchEnvLevel3 = PI("Pitch Env Level 3", -63, 63);
        PitchEnvLevel4 = PI("Pitch Env Level 4", -63, 63);

        TvfEnvTime1 = PI("TVF Env Time 1", 0, 127);
        TvfEnvTime2 = PI("TVF Env Time 2", 0, 127);
        TvfEnvTime3 = PI("TVF Env Time 3", 0, 127);
        TvfEnvTime4 = PI("TVF Env Time 4", 0, 127);
        TvfEnvLevel0 = PI("TVF Env Level 0", 0, 127);
        TvfEnvLevel1 = PI("TVF Env Level 1", 0, 127);
        TvfEnvLevel2 = PI("TVF Env Level 2", 0, 127);
        TvfEnvLevel3 = PI("TVF Env Level 3", 0, 127);
        TvfEnvLevel4 = PI("TVF Env Level 4", 0, 127);

        TvaEnvTime1 = PI("TVA Env Time 1", 0, 127);
        TvaEnvTime2 = PI("TVA Env Time 2", 0, 127);
        TvaEnvTime3 = PI("TVA Env Time 3", 0, 127);
        TvaEnvTime4 = PI("TVA Env Time 4", 0, 127);
        TvaEnvLevel1 = PI("TVA Env Level 1", 0, 127);
        TvaEnvLevel2 = PI("TVA Env Level 2", 0, 127);
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);

        BiasLevel = PI("Bias Level", -100, 100);
        BiasPosition = PI("Bias Position", 0, 127);
        BiasDirection = PS("Bias Direction");

        IsOn = Track(new ParamBool(pmtDomain, pmtByPath[PMT + $"PMT {index + 1} Partial Switch"], writer));

        _editable = new IParam[]
        {
            WaveGroupType, WaveNumberL, WaveNumberR, WaveGain, WaveFxmSwitch, WaveFxmColor, WaveFxmDepth,
            PartialLevel, PartialPan, CoarseTune, FineTune, ChorusSend, ReverbSend,
            TvfFilterType, TvfCutoff, TvfResonance, TvfEnvDepth,
            PitchEnvDepth, PitchEnvTime1, PitchEnvTime2, PitchEnvTime3, PitchEnvTime4,
            PitchEnvLevel0, PitchEnvLevel1, PitchEnvLevel2, PitchEnvLevel3, PitchEnvLevel4,
            TvfEnvTime1, TvfEnvTime2, TvfEnvTime3, TvfEnvTime4,
            TvfEnvLevel0, TvfEnvLevel1, TvfEnvLevel2, TvfEnvLevel3, TvfEnvLevel4,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4,
            TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
            BiasLevel, BiasPosition, BiasDirection,
        };

        // Card summaries follow the wave / level / pan / filter the user actually sees on the card.
        WaveNumberL.PropertyChanged += OnSummaryChanged;
        PartialLevel.PropertyChanged += OnSummaryChanged;
        PartialPan.PropertyChanged += OnSummaryChanged;
        TvfFilterType.PropertyChanged += OnSummaryChanged;
        TvfCutoff.PropertyChanged += OnSummaryChanged;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T wrapper) where T : IDisposable { _wrappers.Add(wrapper); return wrapper; }

    // --- Card summary ---
    public string WaveSummary => WaveNumberL.Value;
    public string LevelLabel => PartialLevel.Value.ToString();
    public string PanLabel => PartialPan.Value == 0 ? "C" : PartialPan.Value < 0 ? $"L{-PartialPan.Value}" : $"R{PartialPan.Value}";
    public string FilterSummary => $"{PcmTvfRules.Abbrev(TvfFilterType.Value)} {TvfCutoff.Value}";

    // FilterCurveControl reads a mode string + steep flag; derive them from the TVF filter type.
    public string FilterCurveMode => PcmTvfRules.CurveMode(TvfFilterType.Value);
    public bool FilterCurveSteep => PcmTvfRules.CurveSteep(TvfFilterType.Value);

    private void OnSummaryChanged(object? s, PropertyChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(WaveSummary));
        this.RaisePropertyChanged(nameof(LevelLabel));
        this.RaisePropertyChanged(nameof(PanLabel));
        this.RaisePropertyChanged(nameof(FilterSummary));
        this.RaisePropertyChanged(nameof(FilterCurveMode));
        this.RaisePropertyChanged(nameof(FilterCurveSteep));
    }

    // --- Audition (transient solo/mute; coordinated by the parent editor VM) ---
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

    // --- Copy / Paste / Init (edit-buffer only; no save) ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);
    public void Paste() { if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    public void Init() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset. Leaves the wave selection untouched (the wave is the instrument); resets
    // output/filter/envelopes to a simple, audible shape.
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Wave Gain"] = "0",
        [PP + "Wave FXM Switch"] = "OFF",
        [PP + "Partial Level"] = "127",
        [PP + "Partial Pan"] = "0",
        [PP + "Partial Coarse Tune"] = "0",
        [PP + "Partial Fine Tune"] = "0",
        [PP + "TVF Filter Type"] = "Low-pass filter",
        [PP + "TVF Cutoff Frequency"] = "127",
        [PP + "TVF Resonance"] = "0",
        [PP + "TVF Env Depth"] = "0",
        [PP + "Pitch Env Depth"] = "0",
        [PP + "Pitch Env Time 1"] = "0",
        [PP + "Pitch Env Time 2"] = "0",
        [PP + "Pitch Env Time 3"] = "0",
        [PP + "Pitch Env Time 4"] = "0",
        [PP + "Pitch Env Level 0"] = "0",
        [PP + "Pitch Env Level 1"] = "0",
        [PP + "Pitch Env Level 2"] = "0",
        [PP + "Pitch Env Level 3"] = "0",
        [PP + "Pitch Env Level 4"] = "0",
        [PP + "TVF Env Time 1"] = "0",
        [PP + "TVF Env Time 2"] = "64",
        [PP + "TVF Env Time 3"] = "64",
        [PP + "TVF Env Time 4"] = "30",
        [PP + "TVF Env Level 0"] = "127",
        [PP + "TVF Env Level 1"] = "127",
        [PP + "TVF Env Level 2"] = "127",
        [PP + "TVF Env Level 3"] = "127",
        [PP + "TVF Env Level 4"] = "127",
        [PP + "TVA Env Time 1"] = "0",
        [PP + "TVA Env Time 2"] = "64",
        [PP + "TVA Env Time 3"] = "64",
        [PP + "TVA Env Time 4"] = "30",
        [PP + "TVA Env Level 1"] = "127",
        [PP + "TVA Env Level 2"] = "127",
        [PP + "TVA Env Level 3"] = "110",
        [PP + "Bias Level"] = "0",
        [PP + "Bias Position"] = "0",
    };

    public void Dispose()
    {
        WaveNumberL.PropertyChanged -= OnSummaryChanged;
        PartialLevel.PropertyChanged -= OnSummaryChanged;
        PartialPan.PropertyChanged -= OnSummaryChanged;
        TvfFilterType.PropertyChanged -= OnSummaryChanged;
        TvfCutoff.PropertyChanged -= OnSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
