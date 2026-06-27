using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One SN-Drums Comp-EQ unit (1..6): a compressor + a 3-band EQ over the shared Comp-EQ domain.</summary>
public sealed class SNDrumCompEqUnitViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Common Comp-EQ/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 1..6
    public string Title => $"Unit {Index}";

    // Compressor
    public ParamBool CompSwitch { get; }
    public ParamString CompAttack { get; }   // ms (enum)
    public ParamString CompRelease { get; }  // ms (enum)
    public ParamInt CompThreshold { get; }   // 0..127
    public ParamString CompRatio { get; }
    public ParamInt CompGain { get; }        // 0..24 dB

    // EQ
    public ParamBool EqSwitch { get; }
    public ParamString EqLowFreq { get; }    // 200 / 400 Hz
    public ParamInt EqLowGain { get; }       // -15..15 dB
    public ParamString EqMidFreq { get; }    // Hz (enum)
    public ParamInt EqMidGain { get; }       // -15..15 dB
    public ParamString EqMidQ { get; }
    public ParamString EqHighFreq { get; }   // Hz (enum)
    public ParamInt EqHighGain { get; }      // -15..15 dB

    public SNDrumCompEqUnitViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var c = $"Comp{index} ";
        var q = $"EQ{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[PP + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[PP + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[PP + n], writer));

        CompSwitch = PB(c + "Switch");
        CompAttack = PS(c + "Attack Time");
        CompRelease = PS(c + "Release Time");
        CompThreshold = PI(c + "Threshold", 0, 127);
        CompRatio = PS(c + "Ratio");
        CompGain = PI(c + "Output Gain", 0, 24);

        EqSwitch = PB(q + "Switch");
        EqLowFreq = PS(q + "Low Freq", new[] { "200", "400" });
        EqLowGain = PI(q + "Low Gain", -15, 15);
        EqMidFreq = PS(q + "Mid Freq");
        EqMidGain = PI(q + "Mid Gain", -15, 15);
        EqMidQ = PS(q + "Mid Q");
        EqHighFreq = PS(q + "High Freq");
        EqHighGain = PI(q + "High Gain", -15, 15);
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
