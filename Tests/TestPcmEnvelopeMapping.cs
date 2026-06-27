using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPcmEnvelopeMapping
{
    [Test]
    public void SegmentMax_is_quarter_width()
    {
        Assert.That(PcmEnvelopeMapping.SegmentMax(400), Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.SegmentMax(0), Is.EqualTo(0));
    }

    [Test]
    public void Time_round_trips_through_width()
    {
        var seg = PcmEnvelopeMapping.SegmentMax(400);
        var w = PcmEnvelopeMapping.TimeToWidth(127, seg);
        Assert.That(w, Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.TimeFromWidth(w, seg), Is.EqualTo(127));
        Assert.That(PcmEnvelopeMapping.TimeFromWidth(0, seg), Is.EqualTo(0));
    }

    [Test]
    public void Unipolar_level_maps_top_and_bottom()
    {
        Assert.That(PcmEnvelopeMapping.LevelToY(127, 200, false), Is.EqualTo(0).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(0, 200, false), Is.EqualTo(200).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelFromY(0, 200, false), Is.EqualTo(127));
        Assert.That(PcmEnvelopeMapping.LevelFromY(200, 200, false), Is.EqualTo(0));
    }

    [Test]
    public void Bipolar_level_centers_at_zero()
    {
        Assert.That(PcmEnvelopeMapping.LevelToY(0, 200, true), Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(63, 200, true), Is.EqualTo(0).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(-63, 200, true), Is.EqualTo(200).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelFromY(100, 200, true), Is.EqualTo(0));
    }

    [Test]
    public void ComputePoints_accumulates_segment_x()
    {
        var pts = PcmEnvelopeMapping.ComputePoints(127, 127, 127, 127, 0, 127, 127, 127, 0, 400, 200, false);
        Assert.That(pts.Length, Is.EqualTo(5));
        Assert.That(pts[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[1].X, Is.EqualTo(100).Within(1e-9));
        Assert.That(pts[2].X, Is.EqualTo(200).Within(1e-9));
        Assert.That(pts[3].X, Is.EqualTo(300).Within(1e-9));
        Assert.That(pts[4].X, Is.EqualTo(400).Within(1e-9));
        Assert.That(pts[0].Y, Is.EqualTo(200).Within(1e-9));
        Assert.That(pts[1].Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void NearestHandle_finds_point_within_radius_else_minus1()
    {
        var pts = PcmEnvelopeMapping.ComputePoints(64, 64, 64, 64, 0, 100, 100, 100, 0, 400, 200, false);
        Assert.That(PcmEnvelopeMapping.NearestHandle(pts[2].X, pts[2].Y, pts, 10), Is.EqualTo(2));
        Assert.That(PcmEnvelopeMapping.NearestHandle(-50, -50, pts, 10), Is.EqualTo(-1));
    }

    [Test]
    public void TimeFromX_is_relative_to_previous_point()
    {
        var seg = PcmEnvelopeMapping.SegmentMax(400);
        Assert.That(PcmEnvelopeMapping.TimeFromX(350, 300, seg), Is.EqualTo(64).Within(1));
    }
}
