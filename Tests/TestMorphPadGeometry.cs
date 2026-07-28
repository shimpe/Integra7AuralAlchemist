using Avalonia;
using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>The disc's arithmetic: unit circle to pixels and back, and keeping the point inside.</summary>
public class MorphPadGeometryTests
{
    private static MorphPadGeometry Geometry() => new(new Rect(0, 0, 200, 200));

    [Test]
    public void A_point_inside_the_disc_is_left_alone()
    {
        Assert.That(MorphPadGeometry.Clamp(new Point(0.3, -0.4)), Is.EqualTo(new Point(0.3, -0.4)));
    }

    /// <summary>A drag does not stop at the rim, so a point beyond it slides around the edge rather than
    /// escaping -- it keeps its direction and loses only its length.</summary>
    [Test]
    public void A_point_outside_is_pulled_back_onto_the_rim()
    {
        var clamped = MorphPadGeometry.Clamp(new Point(3, 4));   // length 5

        Assert.That(Math.Sqrt(clamped.X * clamped.X + clamped.Y * clamped.Y), Is.EqualTo(1).Within(1e-9));
        Assert.That(clamped.X, Is.EqualTo(0.6).Within(1e-9));
        Assert.That(clamped.Y, Is.EqualTo(0.8).Within(1e-9));
    }

    [Test]
    public void The_exact_centre_survives_the_clamp()
    {
        Assert.That(MorphPadGeometry.Clamp(new Point(0, 0)), Is.EqualTo(new Point(0, 0)));
    }

    [Test]
    public void The_centre_of_the_control_is_the_centre_of_the_disc()
    {
        var g = Geometry();

        Assert.That(g.ToControl(new Point(0, 0)), Is.EqualTo(new Point(100, 100)));
        Assert.That(g.ToUnit(new Point(100, 100)).X, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Control_and_unit_coordinates_round_trip()
    {
        var g = Geometry();
        var p = new Point(0.4, -0.6);

        var back = g.ToUnit(g.ToControl(p));

        Assert.That(back.X, Is.EqualTo(p.X).Within(1e-9));
        Assert.That(back.Y, Is.EqualTo(p.Y).Within(1e-9));
    }

    /// <summary>The disc fills the smaller dimension, so a wide control keeps a circle rather than
    /// stretching it into an ellipse.</summary>
    [Test]
    public void A_non_square_control_still_holds_a_circle()
    {
        var g = new MorphPadGeometry(new Rect(0, 0, 400, 200));

        var right = g.ToControl(new Point(1, 0));
        var bottom = g.ToControl(new Point(0, 1));

        Assert.That(right.X - 200, Is.EqualTo(bottom.Y - 100).Within(1e-9), "same radius both ways");
    }
}
