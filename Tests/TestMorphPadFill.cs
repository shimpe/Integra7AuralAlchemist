using System.Linq;
using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>The two shaping steps that turn weights into a colour. Chosen by rendering four candidates
/// and looking at them (see the design document); pinned here so they cannot drift unnoticed.</summary>
public class MorphPadFillTests
{
    private static readonly (double R, double G, double B)[] Colours =
        [(255, 0, 0), (0, 255, 0), (0, 0, 255)];

    [Test]
    public void Sharpened_weights_still_sum_to_one()
    {
        var sharpened = MorphPadFill.Sharpen([0.5, 0.3, 0.2]);

        Assert.That(sharpened.Sum(), Is.EqualTo(1).Within(1e-9));
    }

    /// <summary>Sharpening exists because with seven corners the raw weights let no colour dominate and
    /// the pad reads as one grey wash. The leader must come out further ahead than it went in.</summary>
    [Test]
    public void Sharpening_widens_the_gap_between_leader_and_rest()
    {
        var sharpened = MorphPadFill.Sharpen([0.4, 0.35, 0.25]);

        Assert.That(sharpened[0], Is.GreaterThan(0.4));
        Assert.That(sharpened[2], Is.LessThan(0.25));
    }

    [Test]
    public void At_a_corner_the_colour_is_that_corners_colour()
    {
        var c = MorphPadFill.ColourAt([1, 0, 0], Colours);

        Assert.That(c.R, Is.EqualTo(255).Within(1));
        Assert.That(c.G, Is.EqualTo(0).Within(1));
    }

    [Test]
    public void Dominance_is_one_at_a_corner_and_zero_where_all_are_equal()
    {
        Assert.That(MorphPadFill.Dominance([1, 0, 0]), Is.EqualTo(1).Within(1e-9));
        Assert.That(MorphPadFill.Dominance([1 / 3.0, 1 / 3.0, 1 / 3.0]), Is.EqualTo(0).Within(1e-9));
    }

    /// <summary>Never black in the middle, never blown out at the edge: the fill has to stay legible
    /// everywhere, which is what the two constants in Brightness are for.</summary>
    [Test]
    public void Brightness_stays_between_its_two_bounds()
    {
        foreach (var d in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            Assert.That(MorphPadFill.Brightness(d), Is.InRange(0.55, 1.15));
    }

    [Test]
    public void A_contested_point_is_dimmer_than_a_decided_one()
    {
        Assert.That(MorphPadFill.Brightness(0.0), Is.LessThan(MorphPadFill.Brightness(1.0)));
    }

    [Test]
    public void No_channel_ever_leaves_the_byte_range()
    {
        var c = MorphPadFill.ColourAt([0.34, 0.33, 0.33], [(255, 255, 255), (255, 255, 255), (255, 255, 255)]);

        Assert.That(c.R, Is.InRange(0, 255));
        Assert.That(c.G, Is.InRange(0, 255));
        Assert.That(c.B, Is.InRange(0, 255));
    }
}
