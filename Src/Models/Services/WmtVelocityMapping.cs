using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the WMT velocity map: four horizontal lanes
/// (WMT1..4, top→bottom); X is velocity 0..127 left→right. No Avalonia dependency.</summary>
public static class WmtVelocityMapping
{
    public const int Min = 0, Max = 127;
    public const int LaneCount = 4;

    public readonly record struct Rect(double X, double Y, double W, double H);
    public enum Handle { None, Body, Left, Right }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double VelToX(int vel, double w) => Clamp(vel) / 127.0 * w;

    public static int XToVel(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Full-width pixel strip for lane <paramref name="index"/> (0..3), with vertical padding.</summary>
    public static Rect LaneRect(int index, double w, double h, double pad = 3)
    {
        var laneH = h / LaneCount;
        var y = index * laneH + pad;
        return new Rect(0, y, w, Math.Max(0, laneH - 2 * pad));
    }

    /// <summary>Lane index (0..3) containing pixel y, or -1 if out of range.</summary>
    public static int LaneAt(double y, double h)
    {
        if (h <= 0 || y < 0 || y > h) return -1;
        var i = (int)(y / (h / LaneCount));
        return i < 0 ? 0 : i >= LaneCount ? LaneCount - 1 : i;
    }

    /// <summary>Velocity band rect [lo..hi] within lane <paramref name="index"/>. Tolerates lo/hi swapped.</summary>
    public static Rect BandRect(int lo, int hi, int index, double w, double h, double pad = 3)
    {
        var lane = LaneRect(index, w, h, pad);
        var x = VelToX(Math.Min(lo, hi), w);
        var x2 = VelToX(Math.Max(lo, hi), w);
        return new Rect(x, lane.Y, x2 - x, lane.H);
    }

    /// <summary>Hit a band: Left/Right edge within <paramref name="margin"/> wins over Body; else None.</summary>
    public static Handle HitBand(double px, double py, Rect band, double margin)
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
