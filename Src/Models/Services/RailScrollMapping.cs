using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Scroll arithmetic for the uniform-row note rails.</summary>
public static class RailScrollMapping
{
    /// <summary>Vertical scroll offset that puts row <paramref name="index"/> in the middle of the
    /// viewport, clamped to the scrollable range. A viewport taller than the content yields 0, since
    /// everything is visible anyway.</summary>
    public static double CenterOffset(int index, int count, double extentHeight, double viewportHeight)
    {
        if (count <= 0 || extentHeight <= 0 || index < 0) return 0;

        var rowHeight = extentHeight / count;
        var target = (index + 0.5) * rowHeight - viewportHeight / 2;
        var maxOffset = Math.Max(0, extentHeight - viewportHeight);
        return Math.Clamp(target, 0, maxOffset);
    }
}
