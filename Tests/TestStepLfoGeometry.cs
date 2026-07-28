using System.Linq;
using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>The step editor's arithmetic. It lives apart from the control for the reason every other
/// visual editor here does: a control cannot be unit-tested, and this is the part that can be wrong.
///
/// The fixture is 160 x 100 over sixteen steps of -36..+36, so a bar is 10 wide and the centre line is at
/// 50 -- numbers chosen so the expectations below are readable rather than arithmetic of their own.</summary>
public class StepLfoGeometryTests
{
    private static StepLfoGeometry Geometry() => new(width: 160, height: 100, steps: 16, minValue: -36, maxValue: 36);

    [Test]
    public void The_first_and_last_bars_answer_at_the_edges()
    {
        var g = Geometry();

        Assert.That(g.StepAt(0), Is.EqualTo(0));
        Assert.That(g.StepAt(159.9), Is.EqualTo(15));
    }

    [Test]
    public void A_pointer_outside_the_bars_is_over_no_step()
    {
        var g = Geometry();

        Assert.That(g.StepAt(-1), Is.Null);
        Assert.That(g.StepAt(160), Is.Null);
    }

    /// <summary>Every step is reachable and none is reachable twice: the failure this catches is an
    /// off-by-one in the division that makes one bar unclickable or two bars share an x.</summary>
    [Test]
    public void The_width_divides_evenly_over_the_steps()
    {
        var g = Geometry();

        var hit = Enumerable.Range(0, 160).Select(x => g.StepAt(x + 0.5)).ToList();

        Assert.That(hit, Has.None.Null);
        Assert.That(hit.Distinct().Count(), Is.EqualTo(16));
        Assert.That(hit.Select(h => h!.Value).Distinct().OrderBy(h => h), Is.EqualTo(Enumerable.Range(0, 16)));
    }

    [Test]
    public void The_top_is_the_maximum_the_bottom_the_minimum_and_the_middle_zero()
    {
        var g = Geometry();

        Assert.That(g.ValueAt(0), Is.EqualTo(36));
        Assert.That(g.ValueAt(100), Is.EqualTo(-36));
        Assert.That(g.ValueAt(50), Is.EqualTo(0));
    }

    /// <summary>A drag does not stop at the control's edge, so a pointer above or below it must clamp
    /// rather than ask for a value the parameter cannot hold.</summary>
    [Test]
    public void A_pointer_dragged_past_either_edge_clamps()
    {
        var g = Geometry();

        Assert.That(g.ValueAt(-500), Is.EqualTo(36));
        Assert.That(g.ValueAt(500), Is.EqualTo(-36));
    }

    [Test]
    public void A_positive_bar_stands_above_the_centre_and_a_negative_one_below()
    {
        var g = Geometry();

        var up = g.BarFor(0, 36);
        var down = g.BarFor(0, -36);

        Assert.That(up.Bottom, Is.EqualTo(g.CentreY).Within(0.001));
        Assert.That(up.Top, Is.LessThan(g.CentreY));
        Assert.That(down.Top, Is.EqualTo(g.CentreY).Within(0.001));
        Assert.That(down.Bottom, Is.GreaterThan(g.CentreY));
    }

    /// <summary>A step at rest is the state the editor opens in, sixteen times over. A zero-height bar
    /// would be an invisible control that cannot be aimed at.</summary>
    [Test]
    public void A_step_at_zero_still_draws_something_to_aim_at()
    {
        var g = Geometry();

        var bar = g.BarFor(0, 0);

        Assert.That(bar.Height, Is.GreaterThan(0));
        Assert.That(bar.Top, Is.LessThanOrEqualTo(g.CentreY));
        Assert.That(bar.Bottom, Is.GreaterThanOrEqualTo(g.CentreY));
    }

    /// <summary>The two halves have to agree, or a bar is drawn in one place and clicked in another.</summary>
    [Test]
    public void A_bar_contains_the_x_that_maps_back_to_it()
    {
        var g = Geometry();

        for (var step = 0; step < 16; step++)
        {
            var bar = g.BarFor(step, 20);
            Assert.That(g.StepAt(bar.Center.X), Is.EqualTo(step), $"step {step}");
        }
    }

    /// <summary>A control is measured before it is laid out, so the first call can arrive at zero size.
    /// Answering is better than dividing by zero.</summary>
    [Test]
    public void A_control_with_no_size_yet_answers_rather_than_throwing()
    {
        var g = new StepLfoGeometry(width: 0, height: 0, steps: 16, minValue: -36, maxValue: 36);

        Assert.That(g.StepAt(0), Is.Null);
        Assert.That(g.ValueAt(0), Is.InRange(-36, 36));
        Assert.That(g.BarFor(0, 0).Width, Is.GreaterThanOrEqualTo(0));
    }
}
