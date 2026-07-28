using System;
using Avalonia;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The disc's arithmetic, apart from the control for the reason every visual editor here keeps
/// it apart: a control cannot be unit-tested and this is the part that can be wrong.
///
/// A disc rather than the polygon its corners describe. That makes clamping a scale rather than a
/// projection onto the nearest hull edge, makes the fill's inside test a comparison of squares, and
/// removes the two-corner special case entirely -- a line has no interior to drag inside.</summary>
public sealed class MorphPadGeometry(Rect bounds)
{
    /// <summary>The disc fills the smaller dimension, so a control that is not square still holds a
    /// circle rather than an ellipse.</summary>
    private double Radius => Math.Min(bounds.Width, bounds.Height) / 2;

    private Point Centre => new(bounds.Width / 2, bounds.Height / 2);

    public Point ToControl(Point unit) => new(Centre.X + unit.X * Radius, Centre.Y + unit.Y * Radius);

    public Point ToUnit(Point control) =>
        Radius <= 0 ? new Point(0, 0)
                    : new Point((control.X - Centre.X) / Radius, (control.Y - Centre.Y) / Radius);

    /// <summary>Keep a point inside the disc, losing its length but not its direction, so a drag that
    /// leaves the circle slides around the rim instead of escaping.</summary>
    public static Point Clamp(Point unit)
    {
        var length = Math.Sqrt(unit.X * unit.X + unit.Y * unit.Y);
        return length <= 1 || length < 1e-12 ? unit : new Point(unit.X / length, unit.Y / length);
    }
}
