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

    // ---- Fade bands ------------------------------------------------------------------------------------
    //
    // 254 wide throughout, so one velocity step is exactly two pixels and every expected number below can be
    // read as "twice the number of velocity steps it stands for". A fade of 20 is 40 pixels wide; anything
    // else means the band is not the width the parameter asked for.
    //
    // 400 high with four lanes, so a lane is 100 pixels, and `pad: 0` wherever the vertical numbers are not
    // what is under test — the padding is real (it is what keeps two lanes' outlines off each other) but it
    // makes every Y in an assertion three pixels less obvious than the step count it comes from.

    private const double W = 254, H = 400;

    [Test]
    public void Fade_bands_lie_outside_the_range_and_meet_it()
    {
        // WMT1 (lane 0) over velocities 40..100, twenty steps of fade at each end.
        var body = WmtVelocityMapping.BandRect(40, 100, 0, W, H, 0);
        var lower = WmtVelocityMapping.FadeLowerRect(40, 100, 0, 20, W, H, 0);
        var upper = WmtVelocityMapping.FadeUpperRect(40, 100, 0, 20, W, H, 0);

        // Twenty steps below velocity 40 and twenty above velocity 100 — outside the range, never inside it.
        // Left and right rather than below and above: velocity is the X axis on this chart.
        Assert.That(lower.X, Is.EqualTo(40).Within(1e-9));
        Assert.That(lower.W, Is.EqualTo(40).Within(1e-9));
        Assert.That(upper.X, Is.EqualTo(200).Within(1e-9));
        Assert.That(upper.W, Is.EqualTo(40).Within(1e-9));

        // No seam and no overlap where a band meets the body: the gradient's solid end has to land exactly on
        // the fill it is matching, or a taper reads as a step.
        Assert.That(lower.X + lower.W, Is.EqualTo(body.X).Within(1e-9));
        Assert.That(upper.X, Is.EqualTo(body.X + body.W).Within(1e-9));

        // A band is as tall as the body, so it sits beside it rather than beside the lane.
        Assert.That(lower.Y, Is.EqualTo(body.Y).Within(1e-9));
        Assert.That(lower.H, Is.EqualTo(body.H).Within(1e-9));
        Assert.That(upper.Y, Is.EqualTo(body.Y).Within(1e-9));
        Assert.That(upper.H, Is.EqualTo(body.H).Within(1e-9));
    }

    [Test]
    public void Fade_bands_stay_inside_their_own_lane()
    {
        // The bands of WMT3 belong to WMT3's strip and must not bleed into WMT2's or WMT4's, padding included:
        // a lane's three pixels of vertical padding are the gap that keeps two neighbours' outlines apart, and
        // a band drawn to the unpadded lane height would close it.
        var lane = WmtVelocityMapping.LaneRect(2, W, H);
        var lower = WmtVelocityMapping.FadeLowerRect(40, 100, 2, 20, W, H);

        Assert.That(lower.Y, Is.EqualTo(lane.Y).Within(1e-9));
        Assert.That(lower.H, Is.EqualTo(lane.H).Within(1e-9));

        // And it really is lane 2's strip, not lane 0's: 400 / 4 lanes = 100 each, plus 3 of padding.
        Assert.That(lower.Y, Is.EqualTo(203).Within(1e-9));
        Assert.That(lower.H, Is.EqualTo(94).Within(1e-9));
    }

    [Test]
    public void A_fade_wider_than_the_room_available_is_clipped_to_it()
    {
        // Twenty steps of fade below a range starting at velocity 6: six steps (twelve pixels) is all the
        // velocity axis there is to fade across, and the band must stop at the edge of the chart rather than
        // start off it.
        var lower = WmtVelocityMapping.FadeLowerRect(6, 100, 0, 20, W, H, 0);
        Assert.That(lower.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(lower.W, Is.EqualTo(12).Within(1e-9));

        // The same above a range ending at velocity 120: seven steps, not twenty, ending at the right edge.
        var upper = WmtVelocityMapping.FadeUpperRect(40, 120, 0, 20, W, H, 0);
        Assert.That(upper.W, Is.EqualTo(14).Within(1e-9));
        Assert.That(upper.X + upper.W, Is.EqualTo(W).Within(1e-9));
    }

    [Test]
    public void A_zero_fade_is_a_band_of_no_extent()
    {
        // The common case by a long way, and the one the drawing code checks for: a zero-extent rectangle is
        // nothing to draw, and a gradient across it would have no direction.
        Assert.That(WmtVelocityMapping.FadeLowerRect(40, 100, 0, 0, W, H, 0).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(WmtVelocityMapping.FadeUpperRect(40, 100, 0, 0, W, H, 0).W, Is.EqualTo(0).Within(1e-9));

        // A range already at the end of the axis has nowhere to fade, however wide the fade asks to be.
        Assert.That(WmtVelocityMapping.FadeLowerRect(0, 100, 0, 24, W, H, 0).W, Is.EqualTo(0).Within(1e-9));
        Assert.That(WmtVelocityMapping.FadeUpperRect(40, 127, 0, 24, W, H, 0).W, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Fade_bands_tolerate_lo_and_hi_being_swapped()
    {
        // BandRect does, and the bands are built from it, so a caller mid-drag cannot make one jump to the
        // wrong side of the band.
        Assert.That(WmtVelocityMapping.FadeLowerRect(100, 40, 0, 20, W, H, 0),
            Is.EqualTo(WmtVelocityMapping.FadeLowerRect(40, 100, 0, 20, W, H, 0)));
        Assert.That(WmtVelocityMapping.FadeUpperRect(100, 40, 0, 20, W, H, 0),
            Is.EqualTo(WmtVelocityMapping.FadeUpperRect(40, 100, 0, 20, W, H, 0)));
    }

    [Test]
    public void HitBand_DetectsEdgesBodyAndMiss()
    {
        var band = new PmtZoneMapping.Rect(50, 100, 40, 20); // x 50..90, y 100..120
        Assert.That(WmtVelocityMapping.HitBand(50, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Left));
        Assert.That(WmtVelocityMapping.HitBand(90, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Right));
        Assert.That(WmtVelocityMapping.HitBand(70, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Body));
        Assert.That(WmtVelocityMapping.HitBand(10, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
        Assert.That(WmtVelocityMapping.HitBand(70, 10, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
    }
}
