using System;
using System.Collections.Generic;
using Avalonia;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Where the corners are, and how much of each one a point is made of.
///
/// <b>Inverse distance to the first power</b>, normalised: for two corners this is exactly a linear
/// crossfade along the diameter, which is what interpolating linearly means, and it generalises to any
/// corner count with no special cases. Squaring the distance would make the nearest corner dominate far
/// harder and stop two corners being linear at all.
///
/// Pure, and the source of everything else: the blend, the sticky winner and the colour of every pixel
/// are all functions of these numbers.</summary>
public static class MorphWeights
{
    /// <summary>Corner positions on the unit circle, evenly spaced, first at the top. Two corners are
    /// the ends of the horizontal diameter instead: a crossfade reads as a left-to-right movement, and a
    /// vertical one does not.</summary>
    public static IReadOnlyList<Point> Corners(int count)
    {
        if (count == 2) return [new Point(-1, 0), new Point(1, 0)];

        var corners = new List<Point>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = -Math.PI / 2 + i * 2 * Math.PI / count;
            corners.Add(new Point(Math.Cos(angle), Math.Sin(angle)));
        }

        return corners;
    }

    /// <summary>Each corner's share, summing to 1.</summary>
    public static IReadOnlyList<double> For(Point p, IReadOnlyList<Point> corners)
    {
        var weights = new double[corners.Count];

        // A point on a corner is that corner outright. Without this the reciprocal below divides by
        // zero, and "exactly one of the saved patches" is the case a user reaches for most.
        for (var i = 0; i < corners.Count; i++)
            if (Distance(p, corners[i]) < 1e-9)
            {
                weights[i] = 1;
                return weights;
            }

        var total = 0.0;
        for (var i = 0; i < corners.Count; i++)
        {
            weights[i] = 1.0 / Distance(p, corners[i]);
            total += weights[i];
        }

        for (var i = 0; i < corners.Count; i++) weights[i] /= total;
        return weights;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
