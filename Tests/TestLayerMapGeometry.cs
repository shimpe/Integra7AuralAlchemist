using Avalonia.Layout;
using Integra7AuralAlchemist.Controls;
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
        Assert.That(LayerMapGeometry.LaneAt(0.0, H), Is.EqualTo(0));
        Assert.That(LayerMapGeometry.LaneAt(H / 16 * 1.5, H), Is.EqualTo(1), "just inside part 2's lane");
        Assert.That(LayerMapGeometry.LaneAt(H - 0.001, H), Is.EqualTo(15));
        // Off the ends reads as no lane rather than clamping: a drag that leaves the chart must stop, not
        // silently start editing the first or last part.
        Assert.That(LayerMapGeometry.LaneAt(-1, H), Is.Null);
        Assert.That(LayerMapGeometry.LaneAt(H + 1, H), Is.Null);
    }

    [Test]
    public void A_zone_is_drawn_inside_its_own_lane_and_nowhere_else()
    {
        // Part 3, the middle two octaves, the top half of the velocity range.
        var r = LayerMapGeometry.ZoneRect(2, 48, 72, 64, 127, W, H);
        var lane = LayerMapGeometry.LaneRect(2, W, H);

        // Cell edges, not key centres: the zone starts at the left boundary of key 48's cell and ends at the
        // right boundary of key 72's, so its edges land on the gridlines the chart draws.
        Assert.That(r.X, Is.EqualTo(LayerMapGeometry.KeyBoundaryX(48, W)).Within(0.001));
        Assert.That(r.X + r.W, Is.EqualTo(LayerMapGeometry.KeyBoundaryX(73, W)).Within(0.001));
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

        // The left handle is where the edge is *drawn*: the boundary of key 60's cell, not the key's centre.
        // The centre of key 60 is now half a cell inside the zone, and pressing there is a Body press --
        // which is right, because that pixel is unambiguously within the range rather than on its edge.
        var left = LayerMapGeometry.HitTest(LayerMapGeometry.KeyBoundaryX(60, W), lane.Y + lane.H / 2, zones,
            W, H, margin);
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
    public void A_key_fade_band_lies_below_the_range_and_is_clipped_at_the_chart_edge()
    {
        // Part 3, keys 60..72, 12 semitones of fade in below and 6 out above.
        var z = new LayerZone(2, 60, 72, 0, 127, 12, 6, 0, 0, "3", "");
        var body = LayerMapGeometry.ZoneRect(2, 60, 72, 0, 127, W, H);

        var cell = W / 127.0; // one key's worth of pixels

        var lower = LayerMapGeometry.KeyFadeLowerRect(z, W, H);
        Assert.That(lower.X, Is.EqualTo(LayerMapGeometry.KeyBoundaryX(48, W)).Within(0.001),
            "the fade-in starts at the boundary twelve semitones below the range");
        Assert.That(lower.X + lower.W, Is.EqualTo(body.X).Within(0.001),
            "and ends exactly where the range begins -- no seam between the taper and the fill");
        Assert.That(lower.W, Is.EqualTo(12 * cell).Within(0.001), "twelve whole cells: keys 48 to 59");
        Assert.That(lower.Y, Is.EqualTo(body.Y).Within(0.001), "over the zone's own velocity extent");
        Assert.That(lower.H, Is.EqualTo(body.H).Within(0.001));

        var upper = LayerMapGeometry.KeyFadeUpperRect(z, W, H);
        Assert.That(upper.X, Is.EqualTo(body.X + body.W).Within(0.001), "the fade-out starts where the range ends");
        Assert.That(upper.W, Is.EqualTo(6 * cell).Within(0.001), "six whole cells: keys 73 to 78");

        // Clipped at the bottom of the chart: a 12-semitone fade on a zone starting at key 3 has room for keys
        // 0, 1 and 2 only. Two and a half cells and not three, because key 0's cell is a half cell -- the
        // mapping puts key 0's centre exactly on the chart's left edge, so half of its cell is off the chart.
        var low = new LayerZone(0, 3, 20, 0, 127, 12, 0, 0, 0, "1", "");
        var clippedLo = LayerMapGeometry.KeyFadeLowerRect(low, W, H);
        Assert.That(clippedLo.X, Is.EqualTo(0).Within(0.001), "never left of the chart");
        Assert.That(clippedLo.W, Is.EqualTo(2.5 * cell).Within(0.001));
        Assert.That(clippedLo.X + clippedLo.W,
            Is.EqualTo(LayerMapGeometry.ZoneRect(low, W, H).X).Within(0.001), "still meets the body");

        // And at the top, where key 127's cell is the half one.
        var high = new LayerZone(0, 100, 120, 0, 127, 0, 12, 0, 0, "1", "");
        var clippedHi = LayerMapGeometry.KeyFadeUpperRect(high, W, H);
        Assert.That(clippedHi.X + clippedHi.W, Is.EqualTo(W).Within(0.001), "never right of the chart");
        Assert.That(clippedHi.W, Is.EqualTo(6.5 * cell).Within(0.001));

        // A zone with no fade gets no band, rather than a hairline at the edge of its own fill.
        var hard = new LayerZone(0, 60, 72, 0, 127, 0, 0, 0, 0, "1", "");
        Assert.That(LayerMapGeometry.KeyFadeLowerRect(hard, W, H).W, Is.EqualTo(0).Within(0.001));
        Assert.That(LayerMapGeometry.KeyFadeUpperRect(hard, W, H).W, Is.EqualTo(0).Within(0.001));
    }

    [Test]
    public void A_key_owns_a_cell_and_XToKey_agrees_with_its_edges()
    {
        var cell = W / 127.0;

        // An ordinary key: a whole cell, centred on the key's position.
        var c4 = LayerMapGeometry.KeyCell(60, W);
        Assert.That(c4.W, Is.EqualTo(cell).Within(0.001));
        Assert.That(c4.X + c4.W / 2, Is.EqualTo(LayerMapGeometry.KeyX(60, W)).Within(0.001));

        // The cell is exactly the set of pixels that name that key -- which is what makes "cell" the right
        // word, and what makes drawing cells and hit-testing with XToKey agree by construction.
        foreach (var key in new[] { 0, 1, 60, 126, 127 })
        {
            var k = LayerMapGeometry.KeyCell(key, W);
            Assert.That(LayerMapGeometry.KeyAt(k.X + 0.001, W), Is.EqualTo(key), $"left edge of key {key}");
            Assert.That(LayerMapGeometry.KeyAt(k.X + k.W - 0.001, W), Is.EqualTo(key), $"right edge of key {key}");
        }

        // The two end keys get half cells, because the mapping puts their centres on the chart's edges. That
        // is accepted rather than shifting the whole axis, which would mean changing PmtZoneMapping -- shared
        // with the PMT zone editor, and right as it stands.
        Assert.That(LayerMapGeometry.KeyCell(0, W).X, Is.EqualTo(0).Within(0.001));
        Assert.That(LayerMapGeometry.KeyCell(0, W).W, Is.EqualTo(cell / 2).Within(0.001));
        var last = LayerMapGeometry.KeyCell(127, W);
        Assert.That(last.X + last.W, Is.EqualTo(W).Within(0.001));
        Assert.That(last.W, Is.EqualTo(cell / 2).Within(0.001));
    }

    [Test]
    public void A_gridline_falls_between_two_keys_and_the_boundaries_tile()
    {
        Assert.That(LayerMapGeometry.KeyBoundaryX(0, W), Is.EqualTo(0).Within(0.001),
            "key 0's boundary is the chart's left edge");

        // Each key's left boundary is the previous key's right edge, with no gap and no overlap -- so a line
        // drawn per key and a cell filled per key describe the same chart.
        for (var key = 1; key <= 127; key++)
        {
            var below = LayerMapGeometry.KeyCell(key - 1, W);
            Assert.That(LayerMapGeometry.KeyBoundaryX(key, W), Is.EqualTo(below.X + below.W).Within(0.001),
                $"boundary between keys {key - 1} and {key}");
        }

        // A boundary is half a cell left of the key's own position: that half cell is the shift the chart was
        // showing before cells existed, drawn one way and hit-tested the other.
        Assert.That(LayerMapGeometry.KeyX(60, W) - LayerMapGeometry.KeyBoundaryX(60, W),
            Is.EqualTo(W / 127.0 / 2).Within(0.001));
    }

    [Test]
    public void A_one_key_zone_is_one_cell_wide_and_not_invisible()
    {
        // The defect cells fix: spanning key centres, a range of a single key spanned from a point to itself
        // and drew as nothing at all.
        var one = LayerMapGeometry.ZoneRect(0, 60, 60, 0, 127, W, H);
        Assert.That(one.W, Is.EqualTo(W / 127.0).Within(0.001));
        Assert.That(one.X, Is.EqualTo(LayerMapGeometry.KeyBoundaryX(60, W)).Within(0.001));
        Assert.That(one.X + one.W, Is.EqualTo(LayerMapGeometry.KeyBoundaryX(61, W)).Within(0.001));

        // And a range still spans every cell it covers, ends included.
        var all = LayerMapGeometry.ZoneRect(0, 0, 127, 0, 127, W, H);
        Assert.That(all.X, Is.EqualTo(0).Within(0.001));
        Assert.That(all.W, Is.EqualTo(W).Within(0.001));
    }

    [Test]
    public void A_velocity_fade_band_lies_outside_the_range_and_stays_in_the_lane()
    {
        // Part 5, velocity 40..100, 20 steps of fade either side.
        var z = new LayerZone(4, 0, 127, 40, 100, 0, 0, 20, 20, "5", "");
        var body = LayerMapGeometry.ZoneRect(4, 0, 127, 40, 100, W, H);
        var lane = LayerMapGeometry.LaneRect(4, W, H);

        var lower = LayerMapGeometry.VelFadeLowerRect(z, W, H);
        Assert.That(lower.Y, Is.EqualTo(body.Y + body.H).Within(0.001),
            "the soft fade hangs below the zone, because loud is up");
        Assert.That(lower.H, Is.EqualTo(20.0 / 127.0 * lane.H).Within(0.001), "twenty velocity steps' worth");

        var upper = LayerMapGeometry.VelFadeUpperRect(z, W, H);
        Assert.That(upper.Y + upper.H, Is.EqualTo(body.Y).Within(0.001), "the loud fade sits above the zone");
        Assert.That(upper.H, Is.EqualTo(20.0 / 127.0 * lane.H).Within(0.001));

        // Clipped by the lane, not by the chart: a 40-step fade below velocity 10 has ten steps of room, and
        // the band must not spill into the neighbouring part's lane.
        var soft = new LayerZone(4, 0, 127, 10, 100, 0, 0, 40, 0, "5", "");
        var clippedLo = LayerMapGeometry.VelFadeLowerRect(soft, W, H);
        Assert.That(clippedLo.H, Is.EqualTo(10.0 / 127.0 * lane.H).Within(0.001));
        Assert.That(clippedLo.Y + clippedLo.H, Is.LessThanOrEqualTo(lane.Y + lane.H + 0.001));

        var loud = new LayerZone(4, 0, 127, 40, 120, 0, 0, 0, 40, "5", "");
        var clippedHi = LayerMapGeometry.VelFadeUpperRect(loud, W, H);
        Assert.That(clippedHi.H, Is.EqualTo(7.0 / 127.0 * lane.H).Within(0.001));
        Assert.That(clippedHi.Y, Is.GreaterThanOrEqualTo(lane.Y - 0.001));
    }

    [Test]
    public void A_key_edge_dragged_past_its_opposite_blocks_instead_of_inverting()
    {
        var origin = new LayerZone(0, 60, 72, 0, 127, 0, 0, 0, 0, "1", "");

        var pastRight = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left, 100, 64, 60, 64);
        Assert.That(pastRight.KeyLo, Is.EqualTo(72), "the left edge stops at the right edge");
        Assert.That(pastRight.KeyHi, Is.EqualTo(72), "which stayed where it was -- blocked, not swapped");

        var pastLeft = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Right, 20, 64, 72, 64);
        Assert.That(pastLeft.KeyHi, Is.EqualTo(60));
        Assert.That(pastLeft.KeyLo, Is.EqualTo(60));

        // Ordinary drags, and the ends of the axis.
        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left, 48, 64, 60, 64).KeyLo,
            Is.EqualTo(48));
        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left, -30, 64, 60, 64).KeyLo,
            Is.EqualTo(0), "clamped, not negative");
        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Right, 200, 64, 72, 64).KeyHi,
            Is.EqualTo(127));

        // A key drag leaves the velocity range alone, so it costs no undo step of its own.
        var moved = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left, 48, 100, 60, 64);
        Assert.That(moved.VelLo, Is.EqualTo(origin.VelLo));
        Assert.That(moved.VelHi, Is.EqualTo(origin.VelHi));
    }

    [Test]
    public void A_velocity_edge_dragged_past_its_opposite_blocks_too()
    {
        var origin = new LayerZone(0, 0, 127, 40, 100, 0, 0, 0, 0, "1", "");

        var pastBottom = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Top, 64, 10, 64, 100);
        Assert.That(pastBottom.VelHi, Is.EqualTo(40), "the top edge stops at the bottom edge");
        Assert.That(pastBottom.VelLo, Is.EqualTo(40));

        var pastTop = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Bottom, 64, 120, 64, 40);
        Assert.That(pastTop.VelLo, Is.EqualTo(100));
        Assert.That(pastTop.VelHi, Is.EqualTo(100));

        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Top, 64, 127, 64, 100).VelHi,
            Is.EqualTo(127));
        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Bottom, 64, 0, 64, 40).VelLo,
            Is.EqualTo(0));

        var moved = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Top, 20, 127, 64, 100);
        Assert.That(moved.KeyLo, Is.EqualTo(origin.KeyLo));
        Assert.That(moved.KeyHi, Is.EqualTo(origin.KeyHi));
    }

    [Test]
    public void A_body_drag_moves_both_ranges_and_never_squashes_them()
    {
        var origin = new LayerZone(0, 60, 72, 40, 100, 0, 0, 0, 0, "1", "");

        var shifted = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 76, 80, 66, 70);
        Assert.That(shifted.KeyLo, Is.EqualTo(70));
        Assert.That(shifted.KeyHi, Is.EqualTo(82));
        Assert.That(shifted.VelLo, Is.EqualTo(50));
        Assert.That(shifted.VelHi, Is.EqualTo(110));

        // Dragged well past each end, the shift stops but the span survives: squashing the zone against the
        // edge would silently narrow the thing the user was only trying to move.
        var offRight = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 127, 70, 66, 70);
        Assert.That(offRight.KeyHi, Is.EqualTo(127));
        Assert.That(offRight.KeyHi - offRight.KeyLo, Is.EqualTo(12), "the span is preserved");

        var offLeft = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 0, 70, 66, 70);
        Assert.That(offLeft.KeyLo, Is.EqualTo(0));
        Assert.That(offLeft.KeyHi - offLeft.KeyLo, Is.EqualTo(12));

        var offTop = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 66, 127, 66, 70);
        Assert.That(offTop.VelHi, Is.EqualTo(127));
        Assert.That(offTop.VelHi - offTop.VelLo, Is.EqualTo(60));

        var offBottom = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 66, 0, 66, 70);
        Assert.That(offBottom.VelLo, Is.EqualTo(0));
        Assert.That(offBottom.VelHi - offBottom.VelLo, Is.EqualTo(60));

        // A zone that already fills an axis cannot move along it at all.
        var everything = new LayerZone(0, 0, 127, 0, 127, 0, 0, 0, 0, "1", "");
        var nudged = LayerMapGeometry.ResolveDrag(everything, PmtZoneMapping.Handle.Body, 90, 90, 60, 60);
        Assert.That(nudged, Is.EqualTo(everything));
    }

    [Test]
    public void A_body_drag_back_to_the_press_point_restores_the_zone_exactly()
    {
        // Resolved from the press-time zone rather than accumulated, so returning to where the press happened
        // returns the values that were there -- no drift, whatever route the pointer took.
        var origin = new LayerZone(6, 60, 72, 40, 100, 3, 4, 5, 6, "7", "Some Tone");
        var back = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 66, 70, 66, 70);
        Assert.That(back, Is.EqualTo(origin));
    }

    [Test]
    public void A_drag_on_no_handle_changes_nothing()
    {
        // Pressing an empty spot in a lane is a question, not an edit.
        var origin = new LayerZone(6, 60, 72, 40, 100, 3, 4, 5, 6, "7", "Some Tone");
        Assert.That(LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.None, 0, 0, 100, 100),
            Is.EqualTo(origin));
    }

    [Test]
    public void A_body_drag_past_both_bounds_at_once_still_preserves_both_spans()
    {
        // Both axes clamped in the same call, from an origin that is degenerate on neither -- the case a drag
        // into the bottom-right corner of the chart actually produces.
        var origin = new LayerZone(0, 60, 72, 40, 100, 0, 0, 0, 0, "1", "");
        var corner = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body, 200, 200, 66, 70);

        Assert.That(corner.KeyLo, Is.EqualTo(115));
        Assert.That(corner.KeyHi, Is.EqualTo(127));
        Assert.That(corner.VelLo, Is.EqualTo(67));
        Assert.That(corner.VelHi, Is.EqualTo(127));
    }

    [Test]
    public void Hit_testing_and_LaneAt_agree_on_an_exact_lane_boundary()
    {
        // Sixteen full-range zones, so every lane boundary is a real edge to the parts on both sides of it.
        var zones = new LayerZone[LayerMapGeometry.Lanes];
        for (var i = 0; i < zones.Length; i++)
            zones[i] = new LayerZone(i, 0, 127, 0, 127, 0, 0, 0, 0, $"{i + 1}", "");

        var laneH = H / LayerMapGeometry.Lanes;
        foreach (var lane in new[] { 1, 8, 15 })
        {
            var y = lane * laneH; // the pixel row two lanes share
            Assert.That(LayerMapGeometry.LaneAt(y, H), Is.EqualTo(lane), "a boundary belongs to the lower lane");

            // Through HitTest, which is the API a click goes through: it must not disagree with LaneAt about
            // which part owns the row, or a click on a boundary would select one part and drag another.
            var hit = LayerMapGeometry.HitTest(LayerMapGeometry.KeyX(64, W), y, zones, W, H,
                LayerMapGeometry.HitMargin);
            Assert.That(hit.Part, Is.EqualTo(lane));
            Assert.That(hit.Handle, Is.EqualTo(PmtZoneMapping.Handle.Top),
                "the loud edge of the lower part, not the soft edge of the part above");
        }

        // The very bottom of the lane area is off the chart, not part 16's lane.
        Assert.That(LayerMapGeometry.LaneAt(H, H), Is.Null);
        Assert.That(LayerMapGeometry.HitTest(10, H, zones, W, H, LayerMapGeometry.HitMargin).Part, Is.Null);
    }

    [Test]
    public void The_two_ZoneRect_overloads_describe_the_same_rectangle()
    {
        // True by construction today; this is what stops a later edit to one of them from drifting.
        var z = new LayerZone(9, 36, 84, 20, 110, 0, 0, 0, 0, "10", "");
        Assert.That(LayerMapGeometry.ZoneRect(z, W, H),
            Is.EqualTo(LayerMapGeometry.ZoneRect(z.PartNo, z.KeyLo, z.KeyHi, z.VelLo, z.VelHi, W, H)));
    }

    [Test]
    public void The_key_axis_maps_both_ways_without_a_caller_reaching_past_this_class()
    {
        Assert.That(LayerMapGeometry.KeyX(0, W), Is.EqualTo(0).Within(0.001));
        Assert.That(LayerMapGeometry.KeyX(127, W), Is.EqualTo(W).Within(0.001));
        Assert.That(LayerMapGeometry.KeyX(60, W), Is.EqualTo(PmtZoneMapping.KeyToX(60, W)).Within(0.001));
        Assert.That(LayerMapGeometry.KeyAt(LayerMapGeometry.KeyX(60, W), W), Is.EqualTo(60), "and round-trips");
    }

    [Test]
    public void The_note_name_strip_comes_off_the_height_before_the_lanes_are_measured()
    {
        Assert.That(LayerMapGeometry.AxisHeight, Is.EqualTo(16).Within(0.001));
        Assert.That(LayerMapGeometry.LaneAreaHeight(336), Is.EqualTo(320).Within(0.001));
        Assert.That(LayerMapGeometry.LaneAreaHeight(10), Is.EqualTo(0).Within(0.001), "floored, never negative");

        // MinHeight is a *total*, strip included, so a control that honours it gets sixteen full-height lanes
        // and a strip -- not sixteen lanes a pixel short each.
        Assert.That(LayerMapGeometry.MinHeight,
            Is.EqualTo(LayerMapGeometry.Lanes * LayerMapGeometry.MinLaneHeight + LayerMapGeometry.AxisHeight)
                .Within(0.001));
        Assert.That(LayerMapGeometry.LaneAreaHeight(LayerMapGeometry.MinHeight), Is.EqualTo(H).Within(0.001),
            "and the lane area it leaves is the height this fixture's arithmetic is written around");
    }

    [Test]
    public void A_lane_is_tall_enough_that_a_zone_can_be_grabbed_as_well_as_resized()
    {
        Assert.That(LayerMapGeometry.MinLaneHeight, Is.EqualTo(20).Within(0.001));
        Assert.That(LayerMapGeometry.HitMargin, Is.EqualTo(4).Within(0.001));

        // The invariant the two constants exist in: on a zone that fills its lane -- which is the default for
        // every part -- the top and bottom velocity handles each eat HitMargin, and what is left is the only
        // place a press can select, audition or move the zone instead of resizing it. If that body is smaller
        // than the handles, most presses inside a zone resize it and the chart feels broken.
        var body = LayerMapGeometry.MinLaneHeight - 2 * LayerMapGeometry.HitMargin;
        var handles = 2 * LayerMapGeometry.HitMargin;
        Assert.That(body, Is.GreaterThan(handles),
            "raising HitMargin means raising MinLaneHeight to match, not shipping a lane that is all handle");
    }

    [Test]
    public void The_control_takes_MinHeight_whole_and_reserves_the_strip_once()
    {
        // The one thing about LayerMapControl a test in this repository can reach, and -- not coincidentally --
        // the one thing about it that went wrong. Nothing here is instantiated, laid out or rendered, so no
        // headless-Avalonia harness is involved: a styled property's default value is plain metadata registered
        // by the control's static constructor, and reading it needs no Application.
        //
        // It is worth pinning because the failure was invisible. The control was first written against a MinHeight
        // that excluded the note-name strip, and so set MinHeightProperty to MinHeight + AxisHeight. When the
        // strip moved inside MinHeight the addition stayed, reserving the strip twice: 352 instead of 336, sixteen
        // pixels of dead space under the last lane, no build error and no failing test. This fixture already
        // asserts that MinHeight is a total; this asserts that its only caller treats it as one.
        // Touching a static member first, because that is what runs the static constructor that registers the
        // override -- typeof() alone does not, and the assertion below would then read the base Layoutable
        // default of zero and pass for any control at all.
        Assert.That(LayerMapControl.ZonesProperty, Is.Not.Null);

        Assert.That(Layoutable.MinHeightProperty.GetDefaultValue(typeof(LayerMapControl)),
            Is.EqualTo(LayerMapGeometry.MinHeight).Within(0.001),
            "MinHeight already includes AxisHeight -- hand it over as-is, do not add the strip again");
    }
}
