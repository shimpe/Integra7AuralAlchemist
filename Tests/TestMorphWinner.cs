using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which corner owns the discrete values. Sticky so that hovering on a boundary does not
/// flicker between two patches, but decided by the weights alone when there is no history -- otherwise a
/// saved position would not reproduce the sound it was saved at.</summary>
public class MorphWinnerTests
{
    [Test]
    public void From_cold_the_highest_weight_wins()
    {
        var winner = new MorphWinner();

        Assert.That(winner.Winner([0.2, 0.5, 0.3]), Is.EqualTo(1));
    }

    [Test]
    public void From_cold_a_tie_goes_to_the_lowest_corner()
    {
        var winner = new MorphWinner();

        Assert.That(winner.Winner([0.5, 0.5]), Is.EqualTo(0));
    }

    [Test]
    public void A_challenger_within_the_margin_does_not_take_the_lead()
    {
        var winner = new MorphWinner();
        winner.Winner([0.6, 0.4]);

        // 0.49 beats 0.51 nowhere near enough: 0.51 * 1.05 is 0.5355.
        Assert.That(winner.Winner([0.49, 0.51]), Is.EqualTo(0), "the leader holds through a near tie");
    }

    [Test]
    public void A_challenger_beyond_the_margin_takes_the_lead()
    {
        var winner = new MorphWinner();
        winner.Winner([0.6, 0.4]);

        Assert.That(winner.Winner([0.3, 0.7]), Is.EqualTo(1));
    }

    [Test]
    public void Reset_restores_the_cold_behaviour()
    {
        var winner = new MorphWinner();
        winner.Winner([0.9, 0.1]);
        winner.Reset();

        Assert.That(winner.Winner([0.49, 0.51]), Is.EqualTo(1), "no history, so the weights decide");
    }

    /// <summary>The corner count changes when the user adds or removes one, and last time's leader may
    /// no longer exist.</summary>
    [Test]
    public void A_leader_that_no_longer_exists_is_forgotten()
    {
        var winner = new MorphWinner();
        winner.Winner([0.1, 0.2, 0.7]);

        Assert.That(winner.Winner([0.4, 0.6]), Is.EqualTo(1));
    }
}
