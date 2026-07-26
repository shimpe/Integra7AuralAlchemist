using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the WMT velocity map: four horizontal lanes
/// (WMT1..4, top→bottom); X is velocity 0..127 left→right. No Avalonia dependency.</summary>
public static class WmtVelocityMapping
{
    public const int Min = 0, Max = 127;
    public const int LaneCount = 4;

    // This class used to declare its own `Rect` record struct, structurally identical to
    // PmtZoneMapping.Rect. It returns PmtZoneMapping.Rect now, and the local one is gone.
    //
    // Not tidiness: ZoneShading -- the one place that knows how a crossfade is painted -- takes a
    // PmtZoneMapping.Rect, and two record structs with the same four fields are still two unrelated types to
    // C#. Keeping a local twin would have meant a conversion at every call that draws a fade band, which is
    // exactly the kind of per-chart adapter that lets one chart's idea of a rectangle drift from another's.
    // LayerMapGeometry already returns PmtZoneMapping.Rect for the same reason, so all three charts' geometry
    // now speaks one pixel-rectangle type and one painter consumes it.
    //
    // `Handle` below stays local because it is genuinely narrower than PmtZoneMapping's: a WMT lane is a
    // horizontal band with a left and a right edge and no top or bottom to grab.

    /// <summary>What a press landed on: an edge of the band, its body, or nothing.</summary>
    public enum Handle { None, Body, Left, Right }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double VelToX(int vel, double w) => Clamp(vel) / 127.0 * w;

    public static int XToVel(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Full-width pixel strip for lane <paramref name="index"/> (0..3), with vertical padding.</summary>
    public static PmtZoneMapping.Rect LaneRect(int index, double w, double h, double pad = 3)
    {
        var laneH = h / LaneCount;
        var y = index * laneH + pad;
        return new PmtZoneMapping.Rect(0, y, w, Math.Max(0, laneH - 2 * pad));
    }

    /// <summary>Lane index (0..3) containing pixel y, or -1 if out of range.</summary>
    public static int LaneAt(double y, double h)
    {
        if (h <= 0 || y < 0 || y > h) return -1;
        var i = (int)(y / (h / LaneCount));
        return i < 0 ? 0 : i >= LaneCount ? LaneCount - 1 : i;
    }

    /// <summary>Velocity band rect [lo..hi] within lane <paramref name="index"/>. Tolerates lo/hi swapped.</summary>
    public static PmtZoneMapping.Rect BandRect(int lo, int hi, int index, double w, double h, double pad = 3)
    {
        var lane = LaneRect(index, w, h, pad);
        var x = VelToX(Math.Min(lo, hi), w);
        var x2 = VelToX(Math.Max(lo, hi), w);
        return new PmtZoneMapping.Rect(x, lane.Y, x2 - x, lane.H);
    }

    // ---- Fade bands --------------------------------------------------------------------------------------
    //
    // The third and last chart to grow these. PmtZoneMapping has four (a key band and a velocity band at each
    // end of a whole-chart box) and LayerMapGeometry has four more (the same, per lane); this one has two,
    // because a WMT lane is a range on one axis only. There is no key axis here at all: a drum note *is* the
    // key, so the four WMT layers are split by velocity and by nothing else.
    //
    // Those two are horizontal bands, and that is why PmtZoneMapping's VelFadeLowerRect/VelFadeUpperRect could
    // not simply be called. On the key x velocity charts velocity runs up the Y axis with loud at the top, so
    // their velocity bands sit above and below the body; here velocity runs along X, left to right, so the same
    // parameter produces a band to the *left* and to the *right*. The band this chart needs is shaped like
    // PmtZoneMapping's *key* fade -- horizontal, spanning the body's full height -- while meaning what its
    // velocity fade means, so neither of the four existing functions is the one, and the arithmetic below is a
    // third form rather than a reuse. What is reused is everything downstream of it: the rectangle type, and
    // ZoneShading, which decides what a band actually looks like.
    //
    // ASSUMPTION, unchanged and still not confirmed on hardware, shared with both other charts: a fade width is
    // the span *outside* the range across which the layer fades in or out, so a `WMT1 Velocity Fade Width
    // Lower` of 20 means WMT1 fades in over the twenty velocity steps *below* `WMT1 Velocity Range Lower`. That
    // is how Roland documents it. If it is wrong it is wrong on all three charts, and these two functions plus
    // the other eight are the only places that change -- which is the whole reason none of it is in a control.
    //
    // Each band's inner edge comes from BandRect's own rectangle rather than being recomputed from the range,
    // so the gradient meets the body's fill with no seam however the arithmetic rounds, and each band inherits
    // the lane's padded height so it sits inside the lane exactly as the body does. The outer edge is clamped
    // into 0..127 before it is mapped, so a fade wider than the room available becomes a narrower band and
    // never one that starts off the left of the chart or runs off the right of it.

    /// <summary>The band below the velocity range over which the layer fades in: from <c>lo - fade</c> (clipped
    /// at velocity 0) across to the body's left edge, over the lane's own height. Zero-width when
    /// <paramref name="fade"/> is 0, or when the range already starts at velocity 0.</summary>
    public static PmtZoneMapping.Rect FadeLowerRect(int lo, int hi, int index, int fade, double w, double h,
        double pad = 3)
    {
        var body = BandRect(lo, hi, index, w, h, pad);
        var x = VelToX(Clamp(Math.Min(lo, hi) - fade), w);
        return new PmtZoneMapping.Rect(x, body.Y, body.X - x, body.H);
    }

    /// <summary>The band above the velocity range over which the layer fades out: from the body's right edge
    /// across to <c>hi + fade</c>, clipped at velocity 127.</summary>
    public static PmtZoneMapping.Rect FadeUpperRect(int lo, int hi, int index, int fade, double w, double h,
        double pad = 3)
    {
        var body = BandRect(lo, hi, index, w, h, pad);
        var x = body.X + body.W;
        return new PmtZoneMapping.Rect(x, body.Y, VelToX(Clamp(Math.Max(lo, hi) + fade), w) - x, body.H);
    }

    /// <summary>Hit a band: Left/Right edge within <paramref name="margin"/> wins over Body; else None.</summary>
    public static Handle HitBand(double px, double py, PmtZoneMapping.Rect band, double margin)
    {
        var inY = py >= band.Y - margin && py <= band.Y + band.H + margin;
        if (!inY) return Handle.None;
        var inX = px >= band.X - margin && px <= band.X + band.W + margin;
        if (!inX) return Handle.None;
        if (Math.Abs(px - band.X) <= margin) return Handle.Left;
        if (Math.Abs(px - (band.X + band.W)) <= margin) return Handle.Right;
        if (px > band.X && px < band.X + band.W) return Handle.Body;
        return Handle.None;
    }
}
