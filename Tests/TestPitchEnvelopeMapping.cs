using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class PitchEnvelopeMappingTests
{
    [TestCase(0, 200.0, 100.0)]
    [TestCase(63, 200.0, 0.0)]
    [TestCase(-63, 200.0, 200.0)]
    public void BipolarToY_centers_zero_and_inverts(int value, double h, double expected)
        => Assert.That(SnsEnvelopeMapping.BipolarToY(value, h), Is.EqualTo(expected).Within(1e-9));

    [TestCase(-63)]
    [TestCase(-20)]
    [TestCase(0)]
    [TestCase(31)]
    [TestCase(63)]
    public void Bipolar_y_roundtrip(int value)
    {
        var y = SnsEnvelopeMapping.BipolarToY(value, 200.0);
        Assert.That(SnsEnvelopeMapping.BipolarFromY(y, 200.0), Is.EqualTo(value));
    }

    [TestCase(200, 63)]
    [TestCase(-200, -63)]
    [TestCase(40, 40)]
    public void ClampBipolar_limits_to_63(int value, int expected)
        => Assert.That(SnsEnvelopeMapping.ClampBipolar(value), Is.EqualTo(expected));

    [Test]
    public void ComputePitchPoints_places_center_peak_center()
    {
        var p = SnsEnvelopeMapping.ComputePitchPoints(127, 127, 63, 200, 200);
        Assert.That(p.Start.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.Start.Y, Is.EqualTo(100).Within(1e-9));
        Assert.That(p.Peak.X, Is.EqualTo(100).Within(1e-9));
        Assert.That(p.Peak.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.End.X, Is.EqualTo(200).Within(1e-9));
        Assert.That(p.End.Y, Is.EqualTo(100).Within(1e-9));
    }
}
