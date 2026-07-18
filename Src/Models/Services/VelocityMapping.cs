using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Maps a horizontal click position on a playable key row to a MIDI velocity, so the note
/// rails play soft on the left of a key and hard on the right.</summary>
public static class VelocityMapping
{
    public const byte MinVelocity = 1;
    public const byte MaxVelocity = 127;

    /// <summary>Linear map of <paramref name="x"/> across <paramref name="width"/> onto 1..127: the
    /// left edge gives 1, the right edge 127. Positions outside the row are clamped, and a
    /// zero/invalid width falls back to the middle velocity (the row has not been measured yet, so
    /// no position is meaningful).</summary>
    public static byte FromPointerX(double x, double width)
    {
        if (double.IsNaN(width) || width <= 0) return (MinVelocity + MaxVelocity) / 2;
        var fraction = Math.Clamp(x / width, 0.0, 1.0);
        return (byte)Math.Round(MinVelocity + fraction * (MaxVelocity - MinVelocity),
            MidpointRounding.AwayFromZero);
    }
}
