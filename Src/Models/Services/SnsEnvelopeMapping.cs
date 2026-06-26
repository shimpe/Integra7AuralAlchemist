using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure value↔geometry mapping for the ADSR envelope graph (no Avalonia dependency, so it is
/// unit-testable). The graph reserves a fixed-width sustain plateau; the three time segments
/// (attack, decay, release) share the remaining width equally as their max extent at value 127.
/// </summary>
public static class SnsEnvelopeMapping
{
    public const int Min = 0;
    public const int Max = 127;

    public readonly record struct Point(double X, double Y);

    public readonly record struct EnvPoints(
        Point Start, Point Peak, Point SustainStart, Point SustainEnd, Point End);

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    /// <summary>Max pixel width of one time segment at full (127) value.</summary>
    public static double SegmentMax(double width, double sustainWidth)
    {
        var usable = width - sustainWidth;
        return usable < 0 ? 0 : usable / 3.0;
    }

    public static double TimeToWidth(int value, double segMax) => Clamp(value) / 127.0 * segMax;

    public static int TimeFromWidth(double w, double segMax)
    {
        if (segMax <= 0) return Min;
        return Clamp((int)Math.Round(w / segMax * 127.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Y pixel for a 0..127 level (0 at the bottom = height, 127 at the top = 0).</summary>
    public static double LevelToY(int level, double height) => height - Clamp(level) / 127.0 * height;

    public static int LevelFromY(double y, double height)
    {
        if (height <= 0) return Min;
        var lvl = (1.0 - y / height) * 127.0;
        return Clamp((int)Math.Round(lvl, MidpointRounding.AwayFromZero));
    }

    public static EnvPoints ComputePoints(int a, int d, int s, int r,
        double width, double height, double sustainWidth)
    {
        var seg = SegmentMax(width, sustainWidth);
        var aW = TimeToWidth(a, seg);
        var dW = TimeToWidth(d, seg);
        var rW = TimeToWidth(r, seg);
        var sy = LevelToY(s, height);
        var x1 = aW;
        var x2 = aW + dW;
        var x3 = aW + dW + sustainWidth;
        var x4 = aW + dW + sustainWidth + rW;
        return new EnvPoints(
            new Point(0, height),
            new Point(x1, 0),
            new Point(x2, sy),
            new Point(x3, sy),
            new Point(x4, height));
    }

    public static int AttackFromX(double x, double segMax) => TimeFromWidth(x, segMax);

    public static int DecayFromX(double x, double attackWidth, double segMax)
        => TimeFromWidth(x - attackWidth, segMax);

    public static int ReleaseFromX(double x, double attackWidth, double decayWidth, double sustainWidth, double segMax)
        => TimeFromWidth(x - attackWidth - decayWidth - sustainWidth, segMax);
}
