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

    // ---- Fade bands ------------------------------------------------------------------------------------
    //
    // 254 x 254 throughout, so one key and one velocity step are both exactly two pixels and every expected
    // number below can be read as "twice the number of semitones (or velocity steps) it stands for". A fade of
    // 12 is 24 pixels wide; anything else means the band is not the width the parameter asked for.

    private const double W = 254, H = 254;

    [Test]
    public void Key_fade_bands_lie_outside_the_range_and_meet_it()
    {
        // Keys 24..60, the whole velocity axis, twelve semitones of fade at each end.
        var body = PmtZoneMapping.ToRect(24, 60, 0, 127, W, H);
        var lower = PmtZoneMapping.KeyFadeLowerRect(24, 60, 0, 127, 12, W, H);
        var upper = PmtZoneMapping.KeyFadeUpperRect(24, 60, 0, 127, 12, W, H);

        // Twelve semitones below key 24 and twelve above key 60 — outside the range, never inside it.
        Assert.That(lower.X, Is.EqualTo(24).Within(1e-9));
        Assert.That(lower.W, Is.EqualTo(24).Within(1e-9));
        Assert.That(upper.X, Is.EqualTo(120).Within(1e-9));
        Assert.That(upper.W, Is.EqualTo(24).Within(1e-9));

        // No seam and no overlap where a band meets the body: the gradient's solid end has to land exactly on
        // the fill it is matching, or a taper reads as a step.
        Assert.That(lower.X + lower.W, Is.EqualTo(body.X).Within(1e-9));
        Assert.That(upper.X, Is.EqualTo(body.X + body.W).Within(1e-9));

        // A key band spans the zone's own velocity extent, so it sits beside the body and not beside the chart.
        Assert.That(lower.Y, Is.EqualTo(body.Y).Within(1e-9));
        Assert.That(lower.H, Is.EqualTo(body.H).Within(1e-9));
        Assert.That(upper.Y, Is.EqualTo(body.Y).Within(1e-9));
        Assert.That(upper.H, Is.EqualTo(body.H).Within(1e-9));
    }

    [Test]
    public void Velocity_fade_bands_lie_outside_the_range_with_loud_at_the_top()
    {
        // Velocities 40..100 across the whole key axis, twenty steps of fade at each end.
        var body = PmtZoneMapping.ToRect(0, 127, 40, 100, W, H);
        var lower = PmtZoneMapping.VelFadeLowerRect(0, 127, 40, 100, 20, W, H);
        var upper = PmtZoneMapping.VelFadeUpperRect(0, 127, 40, 100, 20, W, H);

        // The *lower* band is drawn below the box and the upper one above it, because loud is up.
        Assert.That(lower.Y, Is.EqualTo(174).Within(1e-9));
        Assert.That(lower.H, Is.EqualTo(40).Within(1e-9));
        Assert.That(upper.Y, Is.EqualTo(14).Within(1e-9));
        Assert.That(upper.H, Is.EqualTo(40).Within(1e-9));

        Assert.That(lower.Y, Is.EqualTo(body.Y + body.H).Within(1e-9));
        Assert.That(upper.Y + upper.H, Is.EqualTo(body.Y).Within(1e-9));

        // A velocity band spans the zone's own key extent.
        Assert.That(lower.X, Is.EqualTo(body.X).Within(1e-9));
        Assert.That(lower.W, Is.EqualTo(body.W).Within(1e-9));
        Assert.That(upper.X, Is.EqualTo(body.X).Within(1e-9));
        Assert.That(upper.W, Is.EqualTo(body.W).Within(1e-9));
    }

    [Test]
    public void A_fade_wider_than_the_room_available_is_clipped_to_it()
    {
        // Twelve semitones of fade below a range starting at key 3: three semitones (six pixels) of keyboard is
        // all there is to fade across, and the band must stop at the edge of the chart rather than start off it.
        var lower = PmtZoneMapping.KeyFadeLowerRect(3, 60, 0, 127, 12, W, H);
        Assert.That(lower.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(lower.W, Is.EqualTo(6).Within(1e-9));

        // The same above a range ending at key 120: seven semitones, not twelve.
        var upper = PmtZoneMapping.KeyFadeUpperRect(24, 120, 0, 127, 12, W, H);
        Assert.That(upper.X, Is.EqualTo(240).Within(1e-9));
        Assert.That(upper.W, Is.EqualTo(14).Within(1e-9));

        // Twenty steps of fade below velocity 5: five steps, ending at the bottom of the chart.
        var velLower = PmtZoneMapping.VelFadeLowerRect(0, 127, 5, 100, 20, W, H);
        Assert.That(velLower.H, Is.EqualTo(10).Within(1e-9));
        Assert.That(velLower.Y + velLower.H, Is.EqualTo(H).Within(1e-9));

        // Twenty steps above velocity 120: seven steps, ending at the top.
        var velUpper = PmtZoneMapping.VelFadeUpperRect(0, 127, 40, 120, 20, W, H);
        Assert.That(velUpper.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(velUpper.H, Is.EqualTo(14).Within(1e-9));
    }

    [Test]
    public void A_zero_fade_is_a_band_of_no_extent()
    {
        // The common case by a long way, and the one the drawing code checks for: a zero-extent rectangle is
        // nothing to draw, and a gradient across it would have no direction.
        Assert.That(PmtZoneMapping.KeyFadeLowerRect(24, 60, 0, 127, 0, W, H).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.KeyFadeUpperRect(24, 60, 0, 127, 0, W, H).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.VelFadeLowerRect(0, 127, 40, 100, 0, W, H).H, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.VelFadeUpperRect(0, 127, 40, 100, 0, W, H).H, Is.EqualTo(0).Within(1e-9));

        // A range already at the end of its axis has nowhere to fade, however wide the fade asks to be.
        Assert.That(PmtZoneMapping.KeyFadeLowerRect(0, 60, 0, 127, 24, W, H).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.KeyFadeUpperRect(24, 127, 0, 127, 24, W, H).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.VelFadeLowerRect(0, 127, 0, 100, 24, W, H).H, Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.VelFadeUpperRect(0, 127, 40, 127, 24, W, H).H, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Fade_bands_tolerate_lo_and_hi_being_swapped()
    {
        // ToRect does, and the bands are built from it, so a caller mid-drag cannot make one jump to the wrong
        // side of the box.
        var straight = PmtZoneMapping.KeyFadeLowerRect(24, 60, 40, 100, 12, W, H);
        var swapped = PmtZoneMapping.KeyFadeLowerRect(60, 24, 100, 40, 12, W, H);
        Assert.That(swapped, Is.EqualTo(straight));

        var straightVel = PmtZoneMapping.VelFadeUpperRect(24, 60, 40, 100, 20, W, H);
        var swappedVel = PmtZoneMapping.VelFadeUpperRect(60, 24, 100, 40, 20, W, H);
        Assert.That(swappedVel, Is.EqualTo(straightVel));
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

    // ---- ResolveDrag -------------------------------------------------------------------------------------
    //
    // The *rules* -- an edge blocking at its opposite instead of swapping, a body drag preserving both spans at
    // the ends of the axes, a drag resolving from the press rather than accumulating -- are pinned in
    // TestLayerMapGeometry, exhaustively, through the LayerZone-shaped wrapper that now forwards to this
    // function. Repeating them here would be copying a fixture, not testing anything new, and the wrapper's
    // tests passing unchanged is precisely the evidence that the rules survived being moved.
    //
    // What is new is the *shape*: four positional ints in and a four-int tuple out, where the wrapper had eleven
    // named record fields and `origin with { KeyLo = … }`. The three tests below cover what that shape can get
    // wrong and what the wrapper's own tests never reach.

    [Test]
    public void ResolveDrag_moves_only_the_grabbed_value_and_returns_the_other_three_untouched()
    {
        // The tuple has no field names in it. `origin with { VelHi = … }` could not put a velocity in a key's
        // place; `(keyLo, keyHi, velLo, Math.Max(vel, velLo))` written one element out is a transposition the
        // compiler is delighted to accept and which would show up as dragging the top of a zone moving its
        // bottom. Every handle is checked, including the two whose orientation reads backwards -- Top is the
        // *loud* edge and so owns VelHi, Bottom is the soft one and owns VelLo, because loud is up on both
        // charts.
        const int keyLo = 60, keyHi = 72, velLo = 40, velHi = 100;

        // Each drag is an ordinary one, well inside the axis and nowhere near its opposite edge, so the only
        // thing under test is which of the four numbers came back different.
        var left = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.Left,
            50, 64, 60, 64);
        Assert.That(left, Is.EqualTo((50, 72, 40, 100)));

        var right = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.Right,
            80, 64, 72, 64);
        Assert.That(right, Is.EqualTo((60, 80, 40, 100)));

        var top = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.Top,
            66, 110, 66, 100);
        Assert.That(top, Is.EqualTo((60, 72, 40, 110)), "the top edge is the loud one: it owns VelHi");

        var bottom = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.Bottom,
            66, 30, 66, 40);
        Assert.That(bottom, Is.EqualTo((60, 72, 30, 100)), "and the bottom edge is the soft one: VelLo");

        var body = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.Body,
            70, 74, 66, 70);
        Assert.That(body, Is.EqualTo((64, 76, 44, 104)), "all four, shifted by the same delta on each axis");

        var none = PmtZoneMapping.ResolveDrag(keyLo, keyHi, velLo, velHi, PmtZoneMapping.Handle.None,
            0, 0, 66, 70);
        Assert.That(none, Is.EqualTo((60, 72, 40, 100)), "a press that grabbed nothing is a question");
    }

    [Test]
    public void ResolveDrag_keeps_a_range_stored_the_wrong_way_round_inside_the_axis()
    {
        // ToRect promises to tolerate lo and hi being swapped, so every function in this class inherits that
        // promise -- and a body drag is where it bites. The span-preserving shift stops the movement when the
        // range's *upper* end would leave the axis, and for a range stored the wrong way round the upper end is
        // the field called `KeyLo`: shifting 72..60 sixty-one keys to the right leaves KeyHi at a legal 121 and
        // carries KeyLo to 133. A key that is not a key, heading for a 0..127 parameter through a two-way
        // binding.
        //
        // Worth a test of its own because the final clamp is the one thing the extraction changed about the
        // arithmetic rather than about where the arithmetic lives. The layer map cannot produce this input -- it
        // builds its zones from the parameters, in order -- so nothing in its fixture reaches the case, and the
        // PMT chart's old inline version wrote the 133.
        var right = PmtZoneMapping.ResolveDrag(72, 60, 40, 100, PmtZoneMapping.Handle.Body, 127, 70, 66, 70);
        Assert.That(right, Is.EqualTo((127, 121, 40, 100)));

        // And the same downwards, where it is the velocities that are the wrong way round: VelHi would be -30.
        var down = PmtZoneMapping.ResolveDrag(60, 72, 100, 40, PmtZoneMapping.Handle.Body, 66, 0, 66, 70);
        Assert.That(down, Is.EqualTo((60, 72, 30, 0)));
    }

    [Test]
    public void An_edge_blocked_against_its_opposite_can_be_dragged_back_out()
    {
        // Block-don't-swap collapses a zone to a single key or a single velocity step when an edge is pushed
        // past its opposite, and `lo == hi` is legal and means exactly that. Which is only a livable rule if the
        // collapse is reversible: a user who overshoots must be able to drag straight back out in the same
        // gesture rather than being left with a one-key zone and no way back except the numeric grid.
        //
        // Nothing pinned that. The wrapper's fixture checks that the block happens and that it lands on `lo ==
        // hi`; the press *after* the block -- which starts from a collapsed origin, the state the rule creates
        // -- is not covered anywhere, and it is the half of the rule the user actually feels.
        var collapsed = PmtZoneMapping.ResolveDrag(60, 72, 0, 127, PmtZoneMapping.Handle.Left, 100, 64, 60, 64);
        Assert.That(collapsed, Is.EqualTo((72, 72, 0, 127)), "blocked at its opposite, one key wide");

        var reopened = PmtZoneMapping.ResolveDrag(collapsed.KeyLo, collapsed.KeyHi, collapsed.VelLo,
            collapsed.VelHi, PmtZoneMapping.Handle.Left, 48, 64, 72, 64);
        Assert.That(reopened, Is.EqualTo((48, 72, 0, 127)), "and dragged straight back out again");

        // The velocity axis behaves the same way: collapsed to a single step, then reopened.
        var pinned = PmtZoneMapping.ResolveDrag(0, 127, 40, 100, PmtZoneMapping.Handle.Top, 64, 10, 64, 100);
        Assert.That(pinned, Is.EqualTo((0, 127, 40, 40)));

        var released = PmtZoneMapping.ResolveDrag(pinned.KeyLo, pinned.KeyHi, pinned.VelLo, pinned.VelHi,
            PmtZoneMapping.Handle.Top, 64, 90, 64, 40);
        Assert.That(released, Is.EqualTo((0, 127, 40, 90)));
    }
}
