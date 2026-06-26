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

    /// <summary>Result of a dual-envelope hit test. Env 0 = amp, 1 = filter; Handle 0/1/2 =
    /// Attack/Decay/Release; (-1,-1) = nothing within radius.</summary>
    public readonly record struct HandleHit(int Env, int Handle);

    /// <summary>Nearest editable handle across two envelopes. The active envelope wins ties
    /// (it is tested first, and later candidates must be strictly closer to replace it).</summary>
    public static HandleHit NearestHandle(double px, double py, EnvPoints amp, EnvPoints filter,
        int activeEnv, double radius)
    {
        var best = new HandleHit(-1, -1);
        var bestD = radius * radius;
        var order = activeEnv == 1 ? new[] { 1, 0 } : new[] { 0, 1 };
        foreach (var e in order)
        {
            var pts = e == 0 ? amp : filter;
            Consider(e, 0, pts.Peak);
            Consider(e, 1, pts.SustainStart);
            Consider(e, 2, pts.End);
        }
        return best;

        void Consider(int e, int handle, Point pt)
        {
            var dx = px - pt.X; var dy = py - pt.Y; var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = new HandleHit(e, handle); }
        }
    }

    public const int BipolarMax = 63;

    public static int ClampBipolar(int v) => v < -BipolarMax ? -BipolarMax : v > BipolarMax ? BipolarMax : v;

    /// <summary>Bipolar value (−63..+63) → Y pixel. 0 = center, +63 = top (0), −63 = bottom (height).</summary>
    public static double BipolarToY(int value, double height) =>
        height / 2.0 - ClampBipolar(value) / (double)BipolarMax * (height / 2.0);

    public static int BipolarFromY(double y, double height)
    {
        if (height <= 0) return 0;
        var v = (height / 2.0 - y) / (height / 2.0) * BipolarMax;
        return ClampBipolar((int)Math.Round(v, MidpointRounding.AwayFromZero));
    }

    /// <summary>Two time segments (attack, decay) share the width.</summary>
    public static double PitchSegmentMax(double width) => width / 2.0;

    public readonly record struct PitchPoints(Point Start, Point Peak, Point End);

    /// <summary>Bipolar AD pitch envelope: center → depth target (attack) → back to center (decay).</summary>
    public static PitchPoints ComputePitchPoints(int attack, int decay, int depth, double width, double height)
    {
        var seg = PitchSegmentMax(width);
        var aW = TimeToWidth(attack, seg);
        var dW = TimeToWidth(decay, seg);
        var cy = height / 2.0;
        return new PitchPoints(new Point(0, cy), new Point(aW, BipolarToY(depth, height)), new Point(aW + dW, cy));
    }

    public static int PitchAttackFromX(double x, double segMax) => TimeFromWidth(x, segMax);
    public static int PitchDecayFromX(double x, double attackWidth, double segMax) => TimeFromWidth(x - attackWidth, segMax);
}
