using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsEnvelopeMappingTests
{
    [TestCase(-5, 0)]
    [TestCase(200, 127)]
    [TestCase(64, 64)]
    public void Clamp_keeps_in_range(int v, int expected)
        => Assert.That(SnsEnvelopeMapping.Clamp(v), Is.EqualTo(expected));

    [TestCase(0, 0.0)]
    [TestCase(127, 100.0)]
    [TestCase(64, 50.39370078740157)]
    public void TimeToWidth_scales_value_over_segment(int value, double expected)
        => Assert.That(SnsEnvelopeMapping.TimeToWidth(value, 100.0), Is.EqualTo(expected).Within(1e-9));

    [TestCase(0)]
    [TestCase(40)]
    [TestCase(127)]
    public void Time_width_roundtrip(int value)
    {
        var w = SnsEnvelopeMapping.TimeToWidth(value, 100.0);
        Assert.That(SnsEnvelopeMapping.TimeFromWidth(w, 100.0), Is.EqualTo(value));
    }

    [TestCase(0, 200.0, 200.0)]
    [TestCase(127, 200.0, 0.0)]
    public void LevelToY_maps_level_to_pixel(int level, double height, double expectedY)
        => Assert.That(SnsEnvelopeMapping.LevelToY(level, height), Is.EqualTo(expectedY).Within(1e-9));

    [TestCase(0)]
    [TestCase(63)]
    [TestCase(127)]
    public void Level_y_roundtrip(int level)
    {
        var y = SnsEnvelopeMapping.LevelToY(level, 200.0);
        Assert.That(SnsEnvelopeMapping.LevelFromY(y, 200.0), Is.EqualTo(level));
    }

    [Test]
    public void ComputePoints_places_the_five_breakpoints()
    {
        var p = SnsEnvelopeMapping.ComputePoints(127, 0, 127, 0, 340, 200, 40);
        Assert.That(p.Start.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.Start.Y, Is.EqualTo(200).Within(1e-9));
        Assert.That(p.Peak.X, Is.EqualTo(100).Within(1e-9));
        Assert.That(p.Peak.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.SustainStart.X, Is.EqualTo(100).Within(1e-9));
        Assert.That(p.SustainStart.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.SustainEnd.X, Is.EqualTo(140).Within(1e-9));
        Assert.That(p.End.X, Is.EqualTo(140).Within(1e-9));
        Assert.That(p.End.Y, Is.EqualTo(200).Within(1e-9));
    }

    [Test]
    public void DecayFromX_subtracts_attack_offset()
    {
        Assert.That(SnsEnvelopeMapping.DecayFromX(150, 100, 100), Is.EqualTo(64));
    }
}
