using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class MotionalSurroundMappingTests
{
    // Clamp keeps values inside the inclusive range.
    [TestCase(-100, -64, 63, -64)]
    [TestCase(100, -64, 63, 63)]
    [TestCase(0, -64, 63, 0)]
    [TestCase(40, 0, 32, 32)]
    [TestCase(-1, 0, 127, 0)]
    public void TestClamp(int v, int min, int max, int expected)
    {
        Assert.That(MotionalSurroundMapping.Clamp(v, min, max), Is.EqualTo(expected));
    }

    // value -> normalized 0..1 across an inclusive integer range.
    [TestCase(-64, -64, 63, 0.0)]
    [TestCase(63, -64, 63, 1.0)]
    [TestCase(0, 0, 32, 0.0)]
    [TestCase(32, 0, 32, 1.0)]
    [TestCase(16, 0, 32, 0.5)]
    public void TestToNormalized(int value, int min, int max, double expected)
    {
        Assert.That(MotionalSurroundMapping.ToNormalized(value, min, max), Is.EqualTo(expected).Within(1e-9));
    }

    // normalized 0..1 -> nearest integer in range; edges and clamping covered.
    // Midpoint of -64..63 is -0.5, which rounds away-from-zero to -1.
    [TestCase(0.0, -64, 63, -64)]
    [TestCase(1.0, -64, 63, 63)]
    [TestCase(0.5, -64, 63, -1)]
    [TestCase(-0.2, -64, 63, -64)]
    [TestCase(1.2, -64, 63, 63)]
    [TestCase(0.5, 0, 32, 16)]
    public void TestFromNormalized(double n, int min, int max, int expected)
    {
        Assert.That(MotionalSurroundMapping.FromNormalized(n, min, max), Is.EqualTo(expected));
    }

    // Round-trip: a value -> normalized -> value is stable for representative values.
    [TestCase(-64)]
    [TestCase(-32)]
    [TestCase(0)]
    [TestCase(31)]
    [TestCase(63)]
    public void TestLrFbRoundTrip(int value)
    {
        var n = MotionalSurroundMapping.ToNormalized(value, -64, 63);
        Assert.That(MotionalSurroundMapping.FromNormalized(n, -64, 63), Is.EqualTo(value));
    }

    [TestCase("OFF", true)]
    [TestCase("1", true)]
    [TestCase("16", true)]
    [TestCase("0", false)]
    [TestCase("17", false)]
    [TestCase("", false)]
    [TestCase("x", false)]
    public void TestControlChannelValidation(string display, bool expected)
    {
        Assert.That(MotionalSurroundMapping.IsValidControlChannel(display), Is.EqualTo(expected));
    }
}
