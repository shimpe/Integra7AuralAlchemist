using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One PCM Synth LFO (LFO 1 or LFO 2) — the friendly subset: waveform, rate, fade-in,
/// key-trigger, and the four bipolar destination depths (Vibrato/Wah/Tremolo/Auto-pan).
/// Prefix-parameterized: "LFO1 " or "LFO2 ". Mirrors the SN-S LfoPanelViewModel.</summary>
public sealed class PcmLfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public string Title { get; }
    /// <summary>Roland leaf-name prefix ("LFO1 "/"LFO2 ") — drives tooltips.</summary>
    public string Prefix { get; }

    public ParamString Waveform { get; }
    public ParamString Rate { get; }       // value table (PARTIAL_DELAY_TIME) → combo
    public ParamInt FadeTime { get; }
    public ParamBool KeyTrigger { get; }
    public ParamInt PitchDepth { get; }    // Vibrato
    public ParamInt TvfDepth { get; }      // Wah
    public ParamInt TvaDepth { get; }      // Tremolo
    public ParamInt PanDepth { get; }      // Auto-pan

    public IReadOnlyList<IParam> Params { get; }

    public PcmLfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer,
        string prefix, string title)
    {
        Prefix = prefix;
        Title = title;

        ParamInt PI(string s, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + prefix + s], writer, min, max));
        ParamBool PB(string s) => Track(new ParamBool(partialDomain, byPath[PP + prefix + s], writer));
        ParamString PS(string s) => Track(new ParamString(partialDomain, byPath[PP + prefix + s], writer));

        Waveform = PS("Waveform");
        Rate = PS("Rate");
        FadeTime = PI("Fade Time", 0, 127);
        KeyTrigger = PB("Key Trigger");
        PitchDepth = PI("Pitch Depth", -63, 63);
        TvfDepth = PI("TVF Depth", -63, 63);
        TvaDepth = PI("TVA Depth", -63, 63);
        PanDepth = PI("Pan Depth", -63, 63);

        Params = new IParam[] { Waveform, Rate, FadeTime, KeyTrigger, PitchDepth, TvfDepth, TvaDepth, PanDepth };
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
