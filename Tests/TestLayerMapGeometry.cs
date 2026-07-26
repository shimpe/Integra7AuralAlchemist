using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class LayerMapGeometryTests
{
    // A chart 1270px wide and 320px tall over 16 lanes: 10px per key, 20px per lane.
    private const double W = 1270, H = 320;

    [Test]
    public void A_lane_belongs_to_one_part_and_they_tile_the_height()
    {
        var first = LayerMapGeometry.LaneRect(0, W, H);
        var last = LayerMapGeometry.LaneRect(15, W, H);

        Assert.That(first.Y, Is.EqualTo(0).Within(0.001), "part 1's lane is at the top");
        Assert.That(first.H, Is.EqualTo(H / 16).Within(0.001));
        Assert.That(last.Y + last.H, Is.EqualTo(H).Within(0.001), "part 16's lane ends at the bottom");
        Assert.That(first.W, Is.EqualTo(W).Within(0.001), "a lane spans the full key range");
    }

    [Test]
    public void The_lane_under_a_point_is_the_one_that_contains_it()
    {
        Assert.That(LayerMapGeometry.LaneAt(0.0, W, H), Is.EqualTo(0));
        Assert.That(LayerMapGeometry.LaneAt(H / 16 * 1.5, W, H), Is.EqualTo(1), "just inside part 2's lane");
        Assert.That(LayerMapGeometry.LaneAt(H - 0.001, W, H), Is.EqualTo(15));
        // Off the ends reads as no lane rather than clamping: a drag that leaves the chart must stop, not
        // silently start editing the first or last part.
        Assert.That(LayerMapGeometry.LaneAt(-1, W, H), Is.Null);
        Assert.That(LayerMapGeometry.LaneAt(H + 1, W, H), Is.Null);
    }

    [Test]
    public void A_zone_is_drawn_inside_its_own_lane_and_nowhere_else()
    {
        // Part 3, the middle two octaves, the top half of the velocity range.
        var r = LayerMapGeometry.ZoneRect(2, 48, 72, 64, 127, W, H);
        var lane = LayerMapGeometry.LaneRect(2, W, H);

        Assert.That(r.X, Is.EqualTo(PmtZoneMapping.KeyToX(48, W)).Within(0.001));
        Assert.That(r.X + r.W, Is.EqualTo(PmtZoneMapping.KeyToX(72, W)).Within(0.001));
        Assert.That(r.Y, Is.GreaterThanOrEqualTo(lane.Y), "never above its lane");
        Assert.That(r.Y + r.H, Is.LessThanOrEqualTo(lane.Y + lane.H + 0.001), "never below it");
        Assert.That(r.Y, Is.EqualTo(lane.Y).Within(0.001), "velocity 127 reaches the top of the lane");
    }

    [Test]
    public void Velocity_reads_top_loud_within_the_lane()
    {
        var full = LayerMapGeometry.ZoneRect(0, 0, 127, 0, 127, W, H);
        var lane = LayerMapGeometry.LaneRect(0, W, H);
        Assert.That(full.H, Is.EqualTo(lane.H).Within(0.001), "0..127 fills the lane");

        var quiet = LayerMapGeometry.ZoneRect(0, 0, 127, 0, 63, W, H);
        Assert.That(quiet.Y + quiet.H, Is.EqualTo(lane.Y + lane.H).Within(0.001),
            "a low velocity range sits at the bottom of the lane, because loud is up");
        Assert.That(quiet.H, Is.LessThan(lane.H));
    }

    [Test]
    public void The_velocity_under_a_point_is_read_within_its_lane()
    {
        var lane = LayerMapGeometry.LaneRect(4, W, H);
        Assert.That(LayerMapGeometry.VelocityAt(4, lane.Y, H), Is.EqualTo(127), "the top of the lane is 127");
        Assert.That(LayerMapGeometry.VelocityAt(4, lane.Y + lane.H, H), Is.EqualTo(0), "the bottom is 0");
        // One Y, two answers: the top edge of part 5's lane is the bottom edge of part 4's, so the same pixel
        // row is velocity 127 to one part and 0 to the other. That is the whole point of lanes. (Asking a
        // lane about a Y outside it clamps rather than refusing -- 127 above, 0 below -- so the demonstration
        // has to use a shared edge, not a distant lane.)
        Assert.That(LayerMapGeometry.VelocityAt(3, lane.Y, H), Is.EqualTo(0));
    }

    [Test]
    public void Hit_testing_names_the_part_and_the_edge()
    {
        // Part 2 (index 1), keys 60..72, full velocity.
        var zones = new[]
        {
            new LayerZone(0, 0, 127, 0, 127, 0, 0, 0, 0, "1", ""),
            new LayerZone(1, 60, 72, 0, 127, 0, 0, 0, 0, "2", ""),
        };
        var lane = LayerMapGeometry.LaneRect(1, W, H);
        var margin = 4.0;

        var left = LayerMapGeometry.HitTest(PmtZoneMapping.KeyToX(60, W), lane.Y + lane.H / 2, zones, W, H, margin);
        Assert.That(left.Part, Is.EqualTo(1));
        Assert.That(left.Handle, Is.EqualTo(PmtZoneMapping.Handle.Left));

        var body = LayerMapGeometry.HitTest(PmtZoneMapping.KeyToX(66, W), lane.Y + lane.H / 2, zones, W, H, margin);
        Assert.That(body.Part, Is.EqualTo(1));
        Assert.That(body.Handle, Is.EqualTo(PmtZoneMapping.Handle.Body));

        // Inside part 2's lane but outside its key range: the lane still identifies the part, because that
        // is what makes clicking an empty spot in a lane audition *that* part and hear nothing.
        var empty = LayerMapGeometry.HitTest(PmtZoneMapping.KeyToX(20, W), lane.Y + lane.H / 2, zones, W, H, margin);
        Assert.That(empty.Part, Is.EqualTo(1));
        Assert.That(empty.Handle, Is.EqualTo(PmtZoneMapping.Handle.None));

        var nowhere = LayerMapGeometry.HitTest(10, H + 20, zones, W, H, margin);
        Assert.That(nowhere.Part, Is.Null);
    }

    [Test]
    public void A_fade_is_as_wide_as_its_fade_width_says()
    {
        // 12 semitones of fade at the lower edge, none at the upper.
        var z = new LayerZone(0, 60, 72, 0, 127, 12, 0, 0, 0, "1", "");
        // Stated independently of KeyToX rather than in terms of it: a fade width is a *span* of keys, so
        // twelve semitones of fade is twelve semitones' worth of pixels wherever the zone happens to sit.
        // Asserting KeyToX(12) would pass for an implementation that mistook the width for a position.
        Assert.That(LayerMapGeometry.KeyFadeLowerWidth(z, W), Is.EqualTo(12.0 / 127.0 * W).Within(0.001));
        Assert.That(LayerMapGeometry.KeyFadeUpperWidth(z, W), Is.EqualTo(0).Within(0.001));
    }
}
