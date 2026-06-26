using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>
/// One LFO ("Automatic Motion" or "Mod Wheel Motion"). Prefix-parameterized so the same panel
/// builds both: prefix "LFO " or "Modulation LFO ". Always-On has Fade + Key Trigger; Mod Wheel
/// has Rate Control. Bipolar destination depths are Vibrato/Wah/Tremolo/Auto-pan.
/// </summary>
public sealed class LfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public string Title { get; }
    public bool IsModWheel { get; }
    public bool HasFade => !IsModWheel;
    public bool HasKeyTrigger => !IsModWheel;
    public bool HasRateControl => IsModWheel;

    public ParamString Shape { get; }
    public ParamInt Rate { get; }
    public ParamBool TempoSync { get; }
    public ParamString TempoSyncNote { get; }
    public ParamInt PitchDepth { get; }   // Vibrato
    public ParamInt FilterDepth { get; }  // Wah
    public ParamInt AmpDepth { get; }     // Tremolo
    public ParamInt PanDepth { get; }     // Auto-pan
    public ParamInt? FadeTime { get; }
    public ParamBool? KeyTrigger { get; }
    public ParamInt? RateControl { get; }

    public IReadOnlyList<IParam> Params { get; }

    public LfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer,
        string prefix, string title, bool isModWheel)
    {
        Title = title;
        IsModWheel = isModWheel;

        ParamInt PI(string suffix, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + prefix + suffix], writer, min, max));
        ParamBool PB(string suffix) => Track(new ParamBool(partialDomain, byPath[PP + prefix + suffix], writer));
        ParamString PS(string suffix) => Track(new ParamString(partialDomain, byPath[PP + prefix + suffix], writer));

        Shape = PS("Shape");
        Rate = PI("Rate", 0, 127);
        TempoSync = PB("Tempo Sync Switch");
        TempoSyncNote = PS("Tempo Sync Note");
        PitchDepth = PI("Pitch Depth", -63, 63);
        FilterDepth = PI("Filter Depth", -63, 63);
        AmpDepth = PI("AMP Depth", -63, 63);
        PanDepth = PI("Pan Depth", -63, 63);

        var ps = new List<IParam> { Shape, Rate, TempoSync, TempoSyncNote, PitchDepth, FilterDepth, AmpDepth, PanDepth };
        if (isModWheel)
        {
            RateControl = PI("Rate Control", -63, 63);
            ps.Add(RateControl);
        }
        else
        {
            FadeTime = PI("Fade Time", 0, 127);
            KeyTrigger = PB("Key Trigger");
            ps.Add(FadeTime);
            ps.Add(KeyTrigger);
        }
        Params = ps;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
