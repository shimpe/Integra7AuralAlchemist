using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>The value-to-angle and fill maths behind the rotary knob, kept UI-free so the awkward part
/// -- where the colored strip starts, and where it starts for a bipolar parameter -- is pinned down
/// without a running Avalonia app.</summary>
[TestFixture]
public class TestKnobGeometry
{
    private const double Tol = 1e-9;

    [Test]
    public void ValueFractionSpansTheRange()
    {
        Assert.That(KnobGeometry.ValueFraction(0, 0, 127), Is.EqualTo(0.0).Within(Tol));
        Assert.That(KnobGeometry.ValueFraction(127, 0, 127), Is.EqualTo(1.0).Within(Tol));
        Assert.That(KnobGeometry.ValueFraction(63.5, 0, 127), Is.EqualTo(0.5).Within(Tol));
    }

    [Test]
    public void ValueFractionClampsOutOfRange()
    {
        Assert.That(KnobGeometry.ValueFraction(-10, 0, 127), Is.EqualTo(0.0).Within(Tol));
        Assert.That(KnobGeometry.ValueFraction(200, 0, 127), Is.EqualTo(1.0).Within(Tol));
    }

    [Test]
    public void ValueFractionHandlesADegenerateRange()
    {
        // A zero-width range would divide by zero; it must not.
        Assert.That(KnobGeometry.ValueFraction(5, 5, 5), Is.EqualTo(0.0).Within(Tol));
    }

    [Test]
    public void TheSweepRunsFromLowerLeftOverTheTopToLowerRight()
    {
        // 135 deg (lower-left) to 405 deg (lower-right), through 270 deg (top). This is the synth-knob
        // convention: a 90 deg gap at the bottom.
        Assert.That(KnobGeometry.ValueToAngle(0, 0, 127), Is.EqualTo(135.0).Within(Tol));
        Assert.That(KnobGeometry.ValueToAngle(63.5, 0, 127), Is.EqualTo(270.0).Within(Tol));
        Assert.That(KnobGeometry.ValueToAngle(127, 0, 127), Is.EqualTo(405.0).Within(Tol));
    }

    [Test]
    public void AUnipolarFillGrowsFromTheStartOfTheSweep()
    {
        Assert.That(KnobGeometry.FillRange(0, 0, 127), Is.EqualTo((0.0, 0.0)).Within(Tol));
        Assert.That(KnobGeometry.FillRange(127, 0, 127), Is.EqualTo((0.0, 1.0)).Within(Tol));
        Assert.That(KnobGeometry.FillRange(63.5, 0, 127), Is.EqualTo((0.0, 0.5)).Within(Tol));
    }

    [Test]
    public void ABipolarFillGrowsOutOfTheCentre()
    {
        // -63..63: zero sits at fraction 0.5 (the top). Positive paints to the right of top, negative to
        // the left, both starting from the centre rather than from an end.
        Assert.That(KnobGeometry.FillRange(0, -63, 63), Is.EqualTo((0.5, 0.5)).Within(Tol));
        Assert.That(KnobGeometry.FillRange(63, -63, 63), Is.EqualTo((0.5, 1.0)).Within(Tol));
        Assert.That(KnobGeometry.FillRange(-63, -63, 63), Is.EqualTo((0.0, 0.5)).Within(Tol));
    }

    [Test]
    public void PointForPlacesTheEndsAndTheCentre()
    {
        // Screen coordinates, y down: top is -y.
        var (sx, sy) = KnobGeometry.PointFor(0.0, 0, 0, 1);   // lower-left
        Assert.That(sx, Is.EqualTo(-0.70710678).Within(1e-6));
        Assert.That(sy, Is.EqualTo(0.70710678).Within(1e-6));

        var (tx, ty) = KnobGeometry.PointFor(0.5, 0, 0, 1);   // top centre
        Assert.That(tx, Is.EqualTo(0.0).Within(1e-6));
        Assert.That(ty, Is.EqualTo(-1.0).Within(1e-6));

        var (ex, ey) = KnobGeometry.PointFor(1.0, 0, 0, 1);   // lower-right
        Assert.That(ex, Is.EqualTo(0.70710678).Within(1e-6));
        Assert.That(ey, Is.EqualTo(0.70710678).Within(1e-6));
    }

    [Test]
    public void IsBipolarIsDrivenByANegativeMinimum()
    {
        Assert.That(KnobGeometry.IsBipolar(-63, 63), Is.True);
        Assert.That(KnobGeometry.IsBipolar(0, 127), Is.False);
    }
}
