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
    public void A_key_fade_band_lies_below_the_range_and_is_clipped_at_the_chart_edge()
    {
        // Part 3, keys 60..72, 12 semitones of fade in below and 6 out above.
        var z = new LayerZone(2, 60, 72, 0, 127, 12, 6, 0, 0, "3", "");
        var body = LayerMapGeometry.ZoneRect(2, 60, 72, 0, 127, W, H);

        var lower = LayerMapGeometry.KeyFadeLowerRect(z, W, H);
        Assert.That(lower.X, Is.EqualTo(PmtZoneMapping.KeyToX(48, W)).Within(0.001),
            "the fade-in starts twelve semitones below the range");
        Assert.That(lower.X + lower.W, Is.EqualTo(body.X).Within(0.001), "and ends where the range begins");
        Assert.That(lower.Y, Is.EqualTo(body.Y).Within(0.001), "over the zone's own velocity extent");
        Assert.That(lower.H, Is.EqualTo(body.H).Within(0.001));

        var upper = LayerMapGeometry.KeyFadeUpperRect(z, W, H);
        Assert.That(upper.X, Is.EqualTo(body.X + body.W).Within(0.001), "the fade-out starts where the range ends");
        Assert.That(upper.X + upper.W, Is.EqualTo(PmtZoneMapping.KeyToX(78, W)).Within(0.001));

        // Clipped at the bottom of the chart: a 12-semitone fade on a zone starting at key 3 has only three
        // semitones of room, so it is a three-semitone band and not a band starting at key -9. Stated as a
        // span rather than as KeyToX(3) so an implementation that mistook the clip for a position fails.
        var low = new LayerZone(0, 3, 20, 0, 127, 12, 0, 0, 0, "1", "");
        var clippedLo = LayerMapGeometry.KeyFadeLowerRect(low, W, H);
        Assert.That(clippedLo.X, Is.EqualTo(0).Within(0.001), "never left of the chart");
        Assert.That(clippedLo.W, Is.EqualTo(3.0 / 127.0 * W).Within(0.001));

        // And at the top.
        var high = new LayerZone(0, 100, 120, 0, 127, 0, 12, 0, 0, "1", "");
        var clippedHi = LayerMapGeometry.KeyFadeUpperRect(high, W, H);
        Assert.That(clippedHi.X + clippedHi.W, Is.EqualTo(W).Within(0.001), "never right of the chart");
        Assert.That(clippedHi.W, Is.EqualTo(7.0 / 127.0 * W).Within(0.001));
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
}
