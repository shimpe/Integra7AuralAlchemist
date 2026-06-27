using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWmtVelocityMapping
{
    [Test]
    public void Clamp_BoundsTo0_127()
    {
        Assert.That(WmtVelocityMapping.Clamp(-5), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.Clamp(200), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.Clamp(64), Is.EqualTo(64));
    }

    [Test]
    public void VelToX_Endpoints()
    {
        Assert.That(WmtVelocityMapping.VelToX(0, 254), Is.EqualTo(0).Within(1e-9));
        Assert.That(WmtVelocityMapping.VelToX(127, 254), Is.EqualTo(254).Within(1e-9));
        Assert.That(WmtVelocityMapping.VelToX(64, 127), Is.EqualTo(64).Within(1e-9));
    }

    [Test]
    public void XToVel_RoundTripsAndClamps()
    {
        Assert.That(WmtVelocityMapping.XToVel(0, 127), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.XToVel(127, 127), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.XToVel(-10, 127), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.XToVel(999, 127), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.XToVel(0, 0), Is.EqualTo(0));
    }

    [Test]
    public void LaneRect_PartitionsHeightIntoFour()
    {
        var l0 = WmtVelocityMapping.LaneRect(0, 200, 400, 0);
        var l3 = WmtVelocityMapping.LaneRect(3, 200, 400, 0);
        Assert.That(l0.W, Is.EqualTo(200).Within(1e-9));
        Assert.That(l0.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(l0.H, Is.EqualTo(100).Within(1e-9));
        Assert.That(l3.Y, Is.EqualTo(300).Within(1e-9));
    }

    [Test]
    public void LaneAt_MapsYToLaneIndex()
    {
        Assert.That(WmtVelocityMapping.LaneAt(10, 400), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.LaneAt(150, 400), Is.EqualTo(1));
        Assert.That(WmtVelocityMapping.LaneAt(399, 400), Is.EqualTo(3));
        Assert.That(WmtVelocityMapping.LaneAt(-1, 400), Is.EqualTo(-1));
        Assert.That(WmtVelocityMapping.LaneAt(401, 400), Is.EqualTo(-1));
    }

    [Test]
    public void BandRect_ToleratesSwappedLoHi()
    {
        var a = WmtVelocityMapping.BandRect(20, 100, 0, 127, 400, 0);
        var b = WmtVelocityMapping.BandRect(100, 20, 0, 127, 400, 0);
        Assert.That(a.X, Is.EqualTo(20).Within(1e-9));
        Assert.That(a.W, Is.EqualTo(80).Within(1e-9));
        Assert.That(b.X, Is.EqualTo(a.X).Within(1e-9));
        Assert.That(b.W, Is.EqualTo(a.W).Within(1e-9));
    }

    [Test]
    public void HitBand_DetectsEdgesBodyAndMiss()
    {
        var band = new WmtVelocityMapping.Rect(50, 100, 40, 20); // x 50..90, y 100..120
        Assert.That(WmtVelocityMapping.HitBand(50, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Left));
        Assert.That(WmtVelocityMapping.HitBand(90, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Right));
        Assert.That(WmtVelocityMapping.HitBand(70, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Body));
        Assert.That(WmtVelocityMapping.HitBand(10, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
        Assert.That(WmtVelocityMapping.HitBand(70, 10, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
    }
}
