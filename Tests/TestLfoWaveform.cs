using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class LfoWaveformTests
{
    [Test]
    public void Returns_count_points_spanning_x()
    {
        var pts = LfoWaveform.Sample("Triangle", 5);
        Assert.That(pts.Count, Is.EqualTo(5));
        Assert.That(pts.First().X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts.Last().X, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Sine_crosses_center_and_peaks()
    {
        var p = LfoWaveform.Sample("Sine", 5);
        Assert.That(p[0].Y, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(p[1].Y, Is.EqualTo(1.0).Within(1e-6));
        Assert.That(p[2].Y, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(p[3].Y, Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void Sawtooth_is_monotonic_rising()
    {
        var ys = LfoWaveform.Sample("Sawtooth", 8).Select(p => p.Y).ToList();
        for (var i = 1; i < ys.Count; i++) Assert.That(ys[i], Is.GreaterThanOrEqualTo(ys[i - 1]));
    }

    [Test]
    public void Square_has_two_levels()
    {
        var distinct = LfoWaveform.Sample("Square", 9).Select(p => p.Y).Distinct().ToList();
        Assert.That(distinct.Count, Is.EqualTo(2));
    }

    [Test]
    public void Triangle_peaks_in_the_middle()
    {
        var p = LfoWaveform.Sample("Triangle", 5);
        Assert.That(p[2].Y, Is.EqualTo(1.0).Within(1e-6));
        Assert.That(p[0].Y, Is.EqualTo(0.0).Within(1e-6));
        Assert.That(p[4].Y, Is.EqualTo(0.0).Within(1e-6));
    }

    [TestCase("Sample&Hold")]
    [TestCase("Random")]
    public void Stepped_shapes_are_deterministic_and_in_range(string shape)
    {
        var a = LfoWaveform.Sample(shape, 32);
        var b = LfoWaveform.Sample(shape, 32);
        Assert.That(a.Select(p => p.Y), Is.EqualTo(b.Select(p => p.Y)));
        Assert.That(a.All(p => p.Y is >= 0 and <= 1), Is.True);
    }

    [Test]
    public void SawUp_ramps_up()
    {
        var pts = LfoWaveform.Sample("Saw Up", 5);
        Assert.That(pts[0].Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void SawDown_ramps_down()
    {
        var pts = LfoWaveform.Sample("Saw Down", 5);
        Assert.That(pts[0].Y, Is.EqualTo(1).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void BendUp_rises_from_zero_to_one()
    {
        var pts = LfoWaveform.Sample("Bend Up", 9);
        Assert.That(pts[0].Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(1).Within(1e-9));
        Assert.That(pts[4].Y, Is.GreaterThan(0).And.LessThan(1));
    }

    [Test]
    public void BendDown_falls_from_one_to_zero()
    {
        var pts = LfoWaveform.Sample("Bend Down", 9);
        Assert.That(pts[0].Y, Is.EqualTo(1).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Step_is_a_rising_staircase()
    {
        var pts = LfoWaveform.Sample("Step", 11); // x = 0, .1, .2, … 1
        Assert.That(pts[0].Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[3].Y, Is.EqualTo(0.25).Within(1e-9)); // x = 0.3
        Assert.That(pts[5].Y, Is.EqualTo(0.5).Within(1e-9));  // x = 0.5
        Assert.That(pts[^1].Y, Is.EqualTo(1).Within(1e-9));   // x = 1
    }

    [Test]
    public void TrianglePulse_has_a_flat_top()
    {
        var pts = LfoWaveform.Sample("Triangle Pulse", 9); // x step 0.125
        Assert.That(pts[3].Y, Is.EqualTo(1).Within(1e-9));  // x = 0.375, on the plateau
        Assert.That(pts[^1].Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Chiff_spikes_near_the_onset_then_stays_low()
    {
        var pts = LfoWaveform.Sample("Chiff", 11); // x step 0.1
        Assert.That(pts[1].Y, Is.GreaterThan(0.7)); // x = 0.1, near the peak
        Assert.That(pts[^1].Y, Is.LessThan(0.3));   // x = 1, low
    }

    [Test]
    public void VariableSine_is_centered_at_start()
    {
        var pts = LfoWaveform.Sample("Variable Sine", 9);
        Assert.That(pts[0].Y, Is.EqualTo(0.5).Within(1e-9));
    }
}
