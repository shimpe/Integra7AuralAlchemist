using Integra7AuralAlchemist.Models.Services;

namespace Tests;

[TestFixture]
public class TestVelocityMapping
{
    [Test]
    public void LeftEdgeIsMinimumVelocity()
    {
        Assert.That(VelocityMapping.FromPointerX(0.0, 88.0), Is.EqualTo(1));
    }

    [Test]
    public void RightEdgeIsMaximumVelocity()
    {
        Assert.That(VelocityMapping.FromPointerX(88.0, 88.0), Is.EqualTo(127));
    }

    [Test]
    public void MiddleIsHalfwayVelocity()
    {
        Assert.That(VelocityMapping.FromPointerX(44.0, 88.0), Is.EqualTo(64));
    }

    [Test]
    public void PositionsOutsideTheRowAreClamped()
    {
        Assert.That(VelocityMapping.FromPointerX(-20.0, 88.0), Is.EqualTo(1));
        Assert.That(VelocityMapping.FromPointerX(500.0, 88.0), Is.EqualTo(127));
    }

    [Test]
    public void InterpolatesLinearlyInBetween()
    {
        // A quarter along a 340px drum row: 1 + 0.25 * 126 = 32.5 -> 33.
        Assert.That(VelocityMapping.FromPointerX(85.0, 340.0), Is.EqualTo(33));
        // Three quarters: 1 + 0.75 * 126 = 95.5 -> 96.
        Assert.That(VelocityMapping.FromPointerX(255.0, 340.0), Is.EqualTo(96));
    }

    [Test]
    public void UnmeasuredRowFallsBackToTheMiddleVelocity()
    {
        Assert.That(VelocityMapping.FromPointerX(10.0, 0.0), Is.EqualTo(64));
        Assert.That(VelocityMapping.FromPointerX(10.0, double.NaN), Is.EqualTo(64));
    }

    [Test]
    public void EveryPositionStaysInsideTheMidiRange()
    {
        for (var x = -50.0; x <= 150.0; x += 0.5)
        {
            var v = VelocityMapping.FromPointerX(x, 88.0);
            Assert.That(v, Is.InRange(1, 127), $"x={x}");
        }
    }
}
