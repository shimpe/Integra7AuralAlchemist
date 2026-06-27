using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One PMT zone (partial 1..4): its on/off plus key &amp; velocity range and fade widths.</summary>
public sealed class PcmPmtZoneViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }                 // 1..4
    public string Title => $"P{Index}";

    public ParamBool On { get; }
    public ParamInt KeyLo { get; }
    public ParamInt KeyHi { get; }
    public ParamInt KeyFadeLo { get; }
    public ParamInt KeyFadeHi { get; }
    public ParamInt VelLo { get; }
    public ParamInt VelHi { get; }
    public ParamInt VelWidthLo { get; }
    public ParamInt VelWidthHi { get; }

    public PcmPmtZoneViewModel(DomainBase pmtDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var p = $"PMT {index} ";
        ParamInt PI(string n) => Track(new ParamInt(pmtDomain, byPath[PMT + p + n], writer, 0, 127));
        ParamBool PB(string n) => Track(new ParamBool(pmtDomain, byPath[PMT + p + n], writer));

        On = PB("Partial Switch");
        KeyLo = PI("Keyboard Range Lower");
        KeyHi = PI("Keyboard Range Upper");
        KeyFadeLo = PI("Keyboard Fade Width Lower");
        KeyFadeHi = PI("Keyboard Fade Width Upper");
        VelLo = PI("Velocity Range Lower");
        VelHi = PI("Velocity Range Upper");
        VelWidthLo = PI("Velocity Width Lower");
        VelWidthHi = PI("Velocity Width Upper");
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
