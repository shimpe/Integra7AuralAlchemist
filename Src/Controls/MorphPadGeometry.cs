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
    /// circle rather than an ellipse.
    ///
    /// Public, with <see cref="Centre"/>, because the control draws the rim outline and a second copy of
    /// this arithmetic there would be a copy that could disagree with the one the drags are resolved
    /// in.</summary>
    public double Radius => Math.Min(bounds.Width, bounds.Height) / 2;

    /// <summary>The centre of the rect handed over, its origin included. The control does not hand over
    /// its whole bounds: it insets them, so that a corner marker on the rim and the number beside it have
    /// somewhere to be drawn other than outside the control. A disc that ignored the inset's offset would
    /// sit up and to the left of the space reserved for it.</summary>
    public Point Centre => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

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
