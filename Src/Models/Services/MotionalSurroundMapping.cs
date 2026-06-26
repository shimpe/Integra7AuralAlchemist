using System.Globalization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure value/coordinate helpers for the Motional Surround editor. No UI dependencies.
///
/// Axis mapping on the 2D room map (documented so it is unambiguous):
///   L-R: left edge = -64, center = 0, right edge = +63   (normalized 0..1 left->right)
///   F-B: -64 = Front, center = 0, +63 = Back. To match the Integra-7's built-in editor the
///        vertical axis is inverted by the consumer (CanvasY / drag use 1 - normalized), so
///        Front (-64) is drawn at the BOTTOM and Back (+63) at the TOP.
/// Both L-R and F-B share the same inclusive integer range [-64, +63].
/// Width is [0, 32]; Ambience send is [0, 127].
/// </summary>
public static class MotionalSurroundMapping
{
    public const int LrFbMin = -64;
    public const int LrFbMax = 63;
    public const int WidthMin = 0;
    public const int WidthMax = 32;
    public const int AmbienceMin = 0;
    public const int AmbienceMax = 127;

    public static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    /// <summary>value in [min,max] -> fraction in [0,1] (clamped).</summary>
    public static double ToNormalized(int value, int min, int max)
    {
        if (max == min) return 0.0;
        var n = (double)(value - min) / (max - min);
        return n < 0.0 ? 0.0 : n > 1.0 ? 1.0 : n;
    }

    /// <summary>fraction in [0,1] -> nearest integer in [min,max] (clamped).</summary>
    public static int FromNormalized(double normalized, int min, int max)
    {
        if (normalized < 0.0) normalized = 0.0;
        if (normalized > 1.0) normalized = 1.0;
        var v = (int)System.Math.Round(min + normalized * (max - min),
            System.MidpointRounding.AwayFromZero);
        return Clamp(v, min, max);
    }

    /// <summary>Ext Part Control Channel: valid display values are "1".."16" or "OFF".</summary>
    public static bool IsValidControlChannel(string display)
    {
        if (display == "OFF") return true;
        return int.TryParse(display, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ch)
               && ch is >= 1 and <= 16;
    }

    /// <summary>Parse a parameter display value as an integer (InvariantCulture), defaulting to 0.</summary>
    public static int ParseDisplayInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
