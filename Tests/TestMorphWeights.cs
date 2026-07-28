using System.Linq;
using Avalonia;
using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>How much of each corner a point is made of. The blend, the winner and the colour of every
/// pixel all come from these numbers, so they are pinned first.</summary>
public class MorphWeightsTests
{
    [TestCase(2)] [TestCase(3)] [TestCase(5)] [TestCase(7)]
    public void The_corners_sit_on_the_unit_circle(int count)
    {
        var corners = MorphWeights.Corners(count);

        Assert.That(corners, Has.Count.EqualTo(count));
        foreach (var c in corners)
            Assert.That(Math.Sqrt(c.X * c.X + c.Y * c.Y), Is.EqualTo(1).Within(0.0001));
    }

    /// <summary>A crossfade reads as a left-to-right movement, so two corners are the ends of the
    /// horizontal diameter rather than the top and bottom.</summary>
    [Test]
    public void Two_corners_are_placed_left_and_right()
    {
        var corners = MorphWeights.Corners(2);

        Assert.That(corners[0].X, Is.EqualTo(-1).Within(0.0001));
        Assert.That(corners[1].X, Is.EqualTo(1).Within(0.0001));
        Assert.That(corners[0].Y, Is.EqualTo(0).Within(0.0001));
    }

    [TestCase(2)] [TestCase(3)] [TestCase(7)]
    public void The_weights_always_sum_to_one(int count)
    {
        var corners = MorphWeights.Corners(count);

        foreach (var p in new[] { new Point(0, 0), new Point(0.3, -0.4), new Point(-0.9, 0.1) })
            Assert.That(MorphWeights.For(p, corners).Sum(), Is.EqualTo(1).Within(0.0001), $"{p}");
    }

    [Test]
    public void Standing_on_a_corner_gives_that_corner_everything()
    {
        var corners = MorphWeights.Corners(5);

        var w = MorphWeights.For(corners[2], corners);

        Assert.That(w[2], Is.EqualTo(1).Within(0.0001));
        Assert.That(w.Where((_, i) => i != 2), Is.All.EqualTo(0).Within(0.0001));
    }

    /// <summary>The property that made inverse distance to the first power the choice: two corners
    /// crossfade linearly, so a quarter of the way along is three parts to one.</summary>
    [Test]
    public void Two_corners_crossfade_linearly()
    {
        var corners = MorphWeights.Corners(2);

        var w = MorphWeights.For(new Point(-0.5, 0), corners);

        Assert.That(w[0], Is.EqualTo(0.75).Within(0.0001));
        Assert.That(w[1], Is.EqualTo(0.25).Within(0.0001));
    }

    [Test]
    public void The_centre_is_an_equal_share_of_every_corner()
    {
        var corners = MorphWeights.Corners(7);

        var w = MorphWeights.For(new Point(0, 0), corners);

        Assert.That(w, Is.All.EqualTo(1.0 / 7).Within(0.0001));
    }
}
