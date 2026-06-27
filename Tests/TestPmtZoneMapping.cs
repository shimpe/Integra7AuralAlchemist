using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPmtZoneMapping
{
    [Test]
    public void Key_maps_across_width()
    {
        Assert.That(PmtZoneMapping.KeyToX(0, 254), Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.KeyToX(127, 254), Is.EqualTo(254).Within(1e-9));
        Assert.That(PmtZoneMapping.XToKey(0, 254), Is.EqualTo(0));
        Assert.That(PmtZoneMapping.XToKey(254, 254), Is.EqualTo(127));
    }

    [Test]
    public void Velocity_is_inverted_on_Y()
    {
        Assert.That(PmtZoneMapping.VelToY(127, 254), Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.VelToY(0, 254), Is.EqualTo(254).Within(1e-9));
        Assert.That(PmtZoneMapping.YToVel(0, 254), Is.EqualTo(127));
        Assert.That(PmtZoneMapping.YToVel(254, 254), Is.EqualTo(0));
    }

    [Test]
    public void ToRect_spans_the_key_and_velocity_range()
    {
        var r = PmtZoneMapping.ToRect(0, 127, 0, 127, 254, 254);
        Assert.That(r.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(r.W, Is.EqualTo(254).Within(1e-9));
        Assert.That(r.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(r.H, Is.EqualTo(254).Within(1e-9));
    }

    [Test]
    public void HitRect_detects_edges_then_body_then_outside()
    {
        var r = new PmtZoneMapping.Rect(100, 100, 80, 80);
        Assert.That(PmtZoneMapping.HitRect(101, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Left));
        Assert.That(PmtZoneMapping.HitRect(179, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Right));
        Assert.That(PmtZoneMapping.HitRect(140, 101, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Top));
        Assert.That(PmtZoneMapping.HitRect(140, 179, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Bottom));
        Assert.That(PmtZoneMapping.HitRect(140, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Body));
        Assert.That(PmtZoneMapping.HitRect(10, 10, r, 6), Is.EqualTo(PmtZoneMapping.Handle.None));
    }
}
