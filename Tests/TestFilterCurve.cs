using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class FilterCurveTests
{
    private static double NearestY(string mode, bool steep, int cutoff, int resonance, double x)
    {
        var pts = FilterCurve.Sample(mode, steep, cutoff, resonance);
        return pts.OrderBy(p => Math.Abs(p.X - x)).First().Y;
    }

    [Test]
    public void Returns_sample_points_spanning_full_x()
    {
        var pts = FilterCurve.Sample("Low pass", false, 64, 0);
        Assert.That(pts.Count, Is.EqualTo(FilterCurve.SampleCount));
        Assert.That(pts.First().X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts.Last().X, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Low_pass_passes_lows_and_rolls_off_highs()
    {
        var low = NearestY("Low pass", false, 64, 0, 0.1);
        var high = NearestY("Low pass", false, 64, 0, 0.95);
        Assert.That(low, Is.GreaterThan(high));
    }

    [Test]
    public void High_pass_is_the_mirror()
    {
        var low = NearestY("High pass", false, 64, 0, 0.1);
        var high = NearestY("High pass", false, 64, 0, 0.95);
        Assert.That(high, Is.GreaterThan(low));
    }

    [Test]
    public void Bypass_is_flat()
    {
        var pts = FilterCurve.Sample("Bypass", false, 64, 100);
        Assert.That(pts.All(p => Math.Abs(p.Y - FilterCurve.PassLevel) < 1e-9), Is.True);
    }

    [Test]
    public void Resonance_raises_the_peak_at_cutoff()
    {
        var noRes = NearestY("Low pass", false, 64, 0, 64 / 127.0);
        var hiRes = NearestY("Low pass", false, 64, 127, 64 / 127.0);
        Assert.That(hiRes, Is.GreaterThan(noRes));
    }

    [TestCase(0, 0.5)]
    [TestCase(127, 1.0)]
    public void PeakLevel_maps_resonance_to_height(int resonance, double expected)
        => Assert.That(FilterCurve.PeakLevel(resonance), Is.EqualTo(expected).Within(1e-9));
}
