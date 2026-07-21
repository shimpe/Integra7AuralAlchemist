using System;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The value-to-angle and fill maths for the rotary knob, deliberately free of any Avalonia
/// type so it can be unit-tested without a running application (like PartLoadState and SyncCounter).
///
/// The dial sweeps a fixed arc from <see cref="StartAngleDegrees"/> clockwise by
/// <see cref="SweepAngleDegrees"/>, leaving a gap at the bottom -- the synth-knob convention. Angles are
/// measured clockwise from the positive x-axis in screen coordinates (y pointing down), so the top of
/// the dial is at 270 degrees.</summary>
public static class KnobGeometry
{
    /// <summary>Lower-left, at 7 o'clock.</summary>
    public const double StartAngleDegrees = 135.0;

    /// <summary>Clockwise to 5 o'clock, leaving 90 degrees open at the bottom.</summary>
    public const double SweepAngleDegrees = 270.0;

    /// <summary>Where <paramref name="value"/> sits in [0, 1] across the range, clamped.</summary>
    public static double ValueFraction(double value, double min, double max)
    {
        if (max <= min) return 0.0;
        var t = (value - min) / (max - min);
        return t < 0 ? 0 : t > 1 ? 1 : t;
    }

    /// <summary>A range straddling zero is drawn from the centre outward.</summary>
    public static bool IsBipolar(double min, double max) => min < 0 && max > 0;

    /// <summary>The angle, in degrees, of <paramref name="value"/> along the sweep.</summary>
    public static double ValueToAngle(double value, double min, double max)
        => StartAngleDegrees + SweepAngleDegrees * ValueFraction(value, min, max);

    /// <summary>The (from, to) fractions of the sweep to paint in the accent colour.
    ///
    /// Unipolar grows from the start of the sweep; bipolar grows out of the zero point, so a negative
    /// value paints toward the start and a positive value toward the end, both anchored at the centre.
    /// The pair is always ordered (from &lt;= to).</summary>
    public static (double From, double To) FillRange(double value, double min, double max)
    {
        var t = ValueFraction(value, min, max);
        if (!IsBipolar(min, max)) return (0.0, t);

        var zero = ValueFraction(0, min, max);
        return t < zero ? (t, zero) : (zero, t);
    }

    /// <summary>The point on the sweep at <paramref name="fraction"/> of it, on a circle of
    /// <paramref name="radius"/> about (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public static (double X, double Y) PointFor(double fraction, double cx, double cy, double radius)
    {
        var angle = (StartAngleDegrees + SweepAngleDegrees * fraction) * Math.PI / 180.0;
        return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
    }
}
