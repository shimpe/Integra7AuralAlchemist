using System;
using System.Collections.Generic;
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

    // --- Amp envelope ---
    public ParamInt AmpEnvAttack { get; }
    public ParamInt AmpEnvDecay { get; }
    public ParamInt AmpEnvSustain { get; }
    public ParamInt AmpEnvRelease { get; }

    // --- Card on/off (Common Partial{n} Switch) ---
    public ParamBool IsOn { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

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

        IsOn = Track(new ParamBool(commonDomain, commonByPath[CP + $"Partial{index + 1} Switch"], writer));

        _editable = new IParam[]
        {
            OscWave, OscWaveVariation, OscPitch, OscDetune, OscPulseWidth, OscPulseWidthShift,
            OscPwmDepth, SuperSawDetune, WaveGain, WaveNumber,
            AmpLevel, AmpPan, AmpVeloSens, AmpKeyfollow,
            AmpEnvAttack, AmpEnvDecay, AmpEnvSustain, AmpEnvRelease
        };

        // Re-raise the conditional-visibility flags and card summary when the wave / level / pan change.
        OscWave.PropertyChanged += OnOscWaveChanged;
        AmpLevel.PropertyChanged += OnSummaryChanged;
        AmpPan.PropertyChanged += OnSummaryChanged;
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

    // --- Card summary ---
    public string WaveSummary => OscWave.Value;
    public string PanLabel => AmpPan.Value == 0 ? "C" : AmpPan.Value < 0 ? $"L{-AmpPan.Value}" : $"R{AmpPan.Value}";

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
        [PP + "AMP Env Release Time"] = "30"
    };

    public void Dispose()
    {
        OscWave.PropertyChanged -= OnOscWaveChanged;
        AmpLevel.PropertyChanged -= OnSummaryChanged;
        AmpPan.PropertyChanged -= OnSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
