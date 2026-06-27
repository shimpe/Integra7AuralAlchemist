using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping for a 4-segment rate/level envelope (PCM TVP/TVF/TVA):
/// 5 level points L0..L4 joined by 4 time segments T1..T4. No Avalonia dependency (unit-testable).
/// Levels are 0..127 (unipolar) or −63..+63 (bipolar, centered). The 4 segments share the width
/// equally as their max extent at value 127. Mirrors the conventions of SnsEnvelopeMapping.</summary>
public static class PcmEnvelopeMapping
{
    public const int Min = 0, Max = 127, BipolarMax = 63;

    public readonly record struct Point(double X, double Y);

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;
    public static int ClampBipolar(int v) => v < -BipolarMax ? -BipolarMax : v > BipolarMax ? BipolarMax : v;

    /// <summary>Max pixel width of one of the 4 time segments at full (127) value.</summary>
    public static double SegmentMax(double width) => width <= 0 ? 0 : width / 4.0;

    public static double TimeToWidth(int value, double segMax) => Clamp(value) / 127.0 * segMax;

    public static int TimeFromWidth(double w, double segMax)
    {
        if (segMax <= 0) return Min;
        if (w < 0) w = 0;
        return Clamp((int)Math.Round(w / segMax * 127.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Y pixel for a level. Unipolar: 0→bottom(height), 127→top(0). Bipolar: 0→center,
    /// +63→top, −63→bottom.</summary>
    public static double LevelToY(int level, double height, bool bipolar) => bipolar
        ? height / 2.0 - ClampBipolar(level) / (double)BipolarMax * (height / 2.0)
        : height - Clamp(level) / 127.0 * height;

    public static int LevelFromY(double y, double height, bool bipolar)
    {
        if (height <= 0) return bipolar ? 0 : Min;
        if (bipolar)
        {
            var v = (height / 2.0 - y) / (height / 2.0) * BipolarMax;
            return ClampBipolar((int)Math.Round(v, MidpointRounding.AwayFromZero));
        }
        var lvl = (1.0 - y / height) * 127.0;
        return Clamp((int)Math.Round(lvl, MidpointRounding.AwayFromZero));
    }

    /// <summary>The 5 envelope points (L0..L4) at the given times/levels.</summary>
    public static Point[] ComputePoints(int t1, int t2, int t3, int t4,
        int l0, int l1, int l2, int l3, int l4, double width, double height, bool bipolar)
    {
        var seg = SegmentMax(width);
        var x0 = 0.0;
        var x1 = x0 + TimeToWidth(t1, seg);
        var x2 = x1 + TimeToWidth(t2, seg);
        var x3 = x2 + TimeToWidth(t3, seg);
        var x4 = x3 + TimeToWidth(t4, seg);
        return new[]
        {
            new Point(x0, LevelToY(l0, height, bipolar)),
            new Point(x1, LevelToY(l1, height, bipolar)),
            new Point(x2, LevelToY(l2, height, bipolar)),
            new Point(x3, LevelToY(l3, height, bipolar)),
            new Point(x4, LevelToY(l4, height, bipolar)),
        };
    }

    /// <summary>Time value (0..127) for the segment ending at a dragged point, given the dragged X
    /// and the previous point's X.</summary>
    public static int TimeFromX(double x, double prevX, double segMax) => TimeFromWidth(x - prevX, segMax);

    /// <summary>Index (0..4) of the nearest point within <paramref name="radius"/>, else −1.</summary>
    public static int NearestHandle(double px, double py, Point[] pts, double radius)
    {
        var best = -1;
        var bestD = radius * radius;
        for (var i = 0; i < pts.Length; i++)
        {
            var dx = px - pts[i].X;
            var dy = py - pts[i].Y;
            var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
