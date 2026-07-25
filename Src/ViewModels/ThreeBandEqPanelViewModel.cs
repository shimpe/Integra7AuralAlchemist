using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>
/// A 3-band EQ — low shelf, peaking mid, high shelf — shown as a draggable response curve alongside
/// knobs and combo boxes. Shared by the Studio Set Part EQ (which has a bypass switch) and the Master
/// EQ (which does not); the caller supplies the domain and says whether a switch is there.
///
/// Parameters are found by leaf name ("EQ Low Gain" …), which both domains use, so no path prefix is
/// needed. The writer belongs to the host and is not disposed here.
/// </summary>
public sealed class ThreeBandEqPanelViewModel : ViewModelBase, IDisposable
{
    private readonly List<IDisposable> _wrappers = [];
    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>The bypass switch, or null for an EQ that is always in circuit (the Master EQ).</summary>
    public ParamBool? EqSwitch { get; }
    public bool HasSwitch => EqSwitch is not null;

    /// <summary>Whether the EQ is actually shaping the sound — always true without a switch.</summary>
    public bool IsOn => EqSwitch?.Value ?? true;

    public ParamString LowFreq { get; }   // 200 / 400 Hz
    public ParamInt LowGain { get; }      // -15..15 dB
    public ParamString MidFreq { get; }   // 200..8000 Hz
    public ParamInt MidGain { get; }      // -15..15 dB
    public ParamString MidQ { get; }      // 0.5 .. 8.0
    public ParamString HighFreq { get; }  // 2000 / 4000 / 8000 Hz
    public ParamInt HighGain { get; }     // -15..15 dB

    public ThreeBandEqPanelViewModel(DomainBase eqDomain, ThrottledParameterWriter writer, bool hasSwitch)
    {
        var all = eqDomain.GetRelevantParameters(true, true);
        FullyQualifiedParameter ByName(string name) => all.First(p => p.ParSpec.Name == name);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(eqDomain, ByName(n), writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(eqDomain, ByName(n), writer, o));

        if (hasSwitch)
        {
            EqSwitch = Track(new ParamBool(eqDomain, ByName("EQ Switch"), writer));
            _subscriptions.Add(EqSwitch.WhenAnyValue(x => x.Value)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(IsOn))));
        }

        // Low Freq has no repr in the parameter database — it is a plain 0/1 mapped to 200/400 Hz —
        // so the two allowed values are supplied here, as the drum Comp-EQ panel does.
        LowFreq = PS("EQ Low Freq", ["200", "400"]);
        LowGain = PI("EQ Low Gain", -15, 15);
        MidFreq = PS("EQ Mid Freq");
        MidGain = PI("EQ Mid Gain", -15, 15);
        MidQ = PS("EQ Mid Q");
        HighFreq = PS("EQ High Freq");
        HighGain = PI("EQ High Gain", -15, 15);

        // Keep the curve's derived (double) views in step with the enum parameters behind them.
        Bridge(LowFreq, nameof(LowFreqHz));
        Bridge(MidFreq, nameof(MidFreqHz));
        Bridge(HighFreq, nameof(HighFreqHz));
        Bridge(MidQ, nameof(MidQValue));
        BridgeOptions(LowFreq, nameof(LowFreqValues));
        BridgeOptions(MidFreq, nameof(MidFreqValues));
        BridgeOptions(HighFreq, nameof(HighFreqValues));
    }

    // --- Bridges for the curve control -------------------------------------------------------
    // The band frequencies are enum parameters whose values happen to be Hz numbers ("200", "8000").
    // The curve works in continuous Hz, so reading parses and writing snaps to the nearest allowed
    // option — the graph handle and the combo box then always show the same band.
    //
    // Each setter announces itself even when the snap lands on the value already held: the graph is
    // mid-drag writing continuous frequencies, and without the notification its handle would stay
    // wherever the pointer left it instead of springing back to the band it actually selected.

    public double LowFreqHz
    {
        get => Hz(LowFreq, 200);
        set { SnapTo(LowFreq, value); this.RaisePropertyChanged(); }
    }

    public double MidFreqHz
    {
        get => Hz(MidFreq, 1000);
        set { SnapTo(MidFreq, value); this.RaisePropertyChanged(); }
    }

    public double HighFreqHz
    {
        get => Hz(HighFreq, 4000);
        set { SnapTo(HighFreq, value); this.RaisePropertyChanged(); }
    }

    /// <summary>Mid Q as a number, for the curve's bell width. Set through its combo box only.</summary>
    public double MidQValue => Parse(MidQ.Value, 1.0);

    /// <summary>The frequencies each band offers, as numbers — the same list its combo box shows. The
    /// graph snaps a dragged handle to these, so it can only land on a band the hardware can be set to.</summary>
    public IReadOnlyList<double> LowFreqValues => Values(LowFreq);
    public IReadOnlyList<double> MidFreqValues => Values(MidFreq);
    public IReadOnlyList<double> HighFreqValues => Values(HighFreq);

    private static IReadOnlyList<double> Values(ParamString p)
    {
        var list = new List<double>(p.Options.Count);
        foreach (var option in p.Options) list.Add(Parse(option, 0));
        return list;
    }

    private static double Hz(ParamString p, double fallback) => Parse(p.Value, fallback);

    /// <summary>Write the allowed option closest to <paramref name="hz"/>. Distance is measured in
    /// log frequency, matching the graph's axis, so the nearest option is the nearest one on screen.</summary>
    private static void SnapTo(ParamString p, double hz)
    {
        var target = Math.Log10(Math.Max(hz, 1));
        string? best = null;
        var bestDistance = double.MaxValue;
        foreach (var option in p.Options)
        {
            var d = Math.Abs(Math.Log10(Math.Max(Parse(option, 1), 1)) - target);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = option;
        }
        if (best is not null) p.Value = best;
    }

    private static double Parse(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void Bridge(ParamString p, string derivedProperty) =>
        _subscriptions.Add(p.WhenAnyValue(x => x.Value)
            .Subscribe(_ => this.RaisePropertyChanged(derivedProperty)));

    private void BridgeOptions(ParamString p, string derivedProperty) =>
        _subscriptions.Add(p.WhenAnyValue(x => x.Options)
            .Subscribe(_ => this.RaisePropertyChanged(derivedProperty)));

    private T Track<T>(T wrapper) where T : IDisposable
    {
        _wrappers.Add(wrapper);
        return wrapper;
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
        foreach (var w in _wrappers) w.Dispose();
    }
}
