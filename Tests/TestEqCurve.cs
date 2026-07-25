using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class EqCurveTests
{
    private static EqBands Flat() => new(200, 0, 1000, 0, 1.0, 4000, 0);

    [Test]
    public void Frequency_axis_is_logarithmic_and_round_trips()
    {
        Assert.That(EqCurve.XFor(EqCurve.MinHz), Is.EqualTo(0).Within(1e-9));
        Assert.That(EqCurve.XFor(EqCurve.MaxHz), Is.EqualTo(1).Within(1e-9));
        // The decade 200->2000 spans the same width as 2000->20000.
        var lower = EqCurve.XFor(2000) - EqCurve.XFor(200);
        var upper = EqCurve.XFor(20000) - EqCurve.XFor(2000);
        Assert.That(lower, Is.EqualTo(upper).Within(1e-9));
        Assert.That(EqCurve.HzAt(EqCurve.XFor(1000)), Is.EqualTo(1000).Within(1e-6));
    }

    [Test]
    public void Gain_axis_round_trips_and_clamps()
    {
        Assert.That(EqCurve.Y01(0), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(EqCurve.DbAtY01(EqCurve.Y01(9)), Is.EqualTo(9).Within(1e-9));
        Assert.That(EqCurve.DbAtY01(-5), Is.EqualTo(EqCurve.RangeDb).Within(1e-9));
        Assert.That(EqCurve.DbAtY01(5), Is.EqualTo(-EqCurve.RangeDb).Within(1e-9));
        // A boost sits above the centre line, a cut below it.
        Assert.That(EqCurve.Y01(6), Is.LessThan(0.5));
        Assert.That(EqCurve.Y01(-6), Is.GreaterThan(0.5));
    }

    [Test]
    public void All_bands_flat_gives_a_flat_response()
    {
        foreach (var p in EqCurve.Sample(Flat()))
            Assert.That(p.Db, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Low_shelf_lifts_the_lows_and_leaves_the_highs()
    {
        var b = Flat() with { LowGainDb = 12 };
        Assert.That(EqCurve.GainDbAt(30, b), Is.GreaterThan(9));
        Assert.That(EqCurve.GainDbAt(10000, b), Is.LessThan(0.5));
    }

    [Test]
    public void High_shelf_lifts_the_highs_and_leaves_the_lows()
    {
        var b = Flat() with { HighGainDb = 12 };
        Assert.That(EqCurve.GainDbAt(16000, b), Is.GreaterThan(9));
        Assert.That(EqCurve.GainDbAt(50, b), Is.LessThan(0.5));
    }

    [Test]
    public void Shelf_corner_follows_its_frequency()
    {
        var at200 = Flat() with { LowHz = 200, LowGainDb = 12 };
        var at400 = Flat() with { LowHz = 400, LowGainDb = 12 };
        Assert.That(EqCurve.GainDbAt(300, at400), Is.GreaterThan(EqCurve.GainDbAt(300, at200)));
    }

    [Test]
    public void Mid_bell_peaks_at_its_frequency()
    {
        var b = Flat() with { MidHz = 1000, MidGainDb = 10, MidQ = 2.0 };
        var peak = EqCurve.GainDbAt(1000, b);
        Assert.That(peak, Is.EqualTo(10).Within(1e-9));
        Assert.That(EqCurve.GainDbAt(300, b), Is.LessThan(peak));
        Assert.That(EqCurve.GainDbAt(3000, b), Is.LessThan(peak));
    }

    [Test]
    public void Higher_q_narrows_the_mid_bell()
    {
        var wide = Flat() with { MidHz = 1000, MidGainDb = 10, MidQ = 0.5 };
        var narrow = Flat() with { MidHz = 1000, MidGainDb = 10, MidQ = 8.0 };
        Assert.That(EqCurve.GainDbAt(1414, narrow), Is.LessThan(EqCurve.GainDbAt(1414, wide)));
        // Both still reach the same peak at the centre.
        Assert.That(EqCurve.GainDbAt(1000, narrow), Is.EqualTo(EqCurve.GainDbAt(1000, wide)).Within(1e-9));
    }

    [Test]
    public void Cuts_go_below_the_line()
    {
        var b = Flat() with { MidHz = 1000, MidGainDb = -12, MidQ = 2.0 };
        Assert.That(EqCurve.GainDbAt(1000, b), Is.EqualTo(-12).Within(1e-9));
    }

    [Test]
    public void Sample_spans_the_full_x_range()
    {
        var pts = EqCurve.Sample(Flat());
        Assert.That(pts.Count, Is.EqualTo(EqCurve.SampleCount));
        Assert.That(pts.First().X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts.Last().X, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Nearest_band_picks_the_handle_under_the_pointer()
    {
        var b = new EqBands(200, 6, 1000, -6, 1.0, 4000, 0);
        Assert.That(EqCurve.NearestBand(EqCurve.XFor(200), EqCurve.Y01(6), b, 0.06), Is.EqualTo(0));
        Assert.That(EqCurve.NearestBand(EqCurve.XFor(1000), EqCurve.Y01(-6), b, 0.06), Is.EqualTo(1));
        Assert.That(EqCurve.NearestBand(EqCurve.XFor(4000), EqCurve.Y01(0), b, 0.06), Is.EqualTo(2));
    }

    [Test]
    public void Nearest_band_ignores_a_far_away_pointer()
    {
        var b = Flat();
        // Same frequency as the mid band but far above it vertically.
        Assert.That(EqCurve.NearestBand(EqCurve.XFor(1000), EqCurve.Y01(18), b, 0.06), Is.EqualTo(-1));
    }
}
