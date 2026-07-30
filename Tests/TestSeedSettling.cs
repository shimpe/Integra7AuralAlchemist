using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>When a sweep is allowed to believe the four expansion slots.
///
/// The rule this pins replaced one that settled on two consecutive agreeing readings, which cost a user
/// three evicted boards: an instrument that has not yet acted on a request reports the same thing twice as
/// readily as one that has finished. Every case below is a state that was seen on the user's own unit on
/// 2026-07-30 -- an idle instrument answering in milliseconds, a loading one answering nothing at all for
/// six to thirteen seconds, and a loadout it already held producing no change whatsoever.</summary>
public class SeedSettlingTests
{
    /// <summary>The plain case: the instrument answered, and went on answering the same thing.</summary>
    [Test]
    public void Three_agreeing_answers_are_a_settled_loadout()
    {
        var settling = new SeedSettling();

        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False);
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False);
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.True);
    }

    /// <summary>And two are not, which is the whole of the defect this class exists for. The instrument goes
    /// quiet within one poll of being sent a loadout, so a rule that stopped at two would be relying on that
    /// margin holding on every unit and every loadout; the third reading costs 1.5 seconds a round and buys
    /// three seconds of it.</summary>
    [Test]
    public void Two_are_not_enough()
    {
        var settling = new SeedSettling();

        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False);
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False);
    }

    /// <summary>A reading the instrument did not answer is not a reading, and it does not merely fail to
    /// count -- it puts the count back to nothing. A device that answers, goes quiet, and answers again is a
    /// device that was doing something in between, and the two answers on either side of the silence are
    /// about different loadouts.</summary>
    [Test]
    public void A_reading_the_instrument_did_not_answer_starts_the_count_again()
    {
        var settling = new SeedSettling();

        settling.Settled([2, 13, 6, 0]);
        settling.Settled([2, 13, 6, 0]);
        Assert.That(settling.Settled(null), Is.False, "the instrument has gone quiet");
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False, "so this is the first reading again");
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.False);
        Assert.That(settling.Settled([2, 13, 6, 0]), Is.True);
    }

    /// <summary>Silence never settles, however much of it there is. This is what a board load looks like from
    /// the outside for six to thirteen seconds -- every read runs out its 1.5-second deadline and is reported
    /// as (0,0,0,0) because there is nothing else the wire can say -- and a rule that let those agree with
    /// each other would settle on all-zeros in the middle of every load, which is precisely how a sweep came
    /// to send a restore into an instrument that was not listening.</summary>
    [Test]
    public void An_instrument_that_answers_nothing_never_settles()
    {
        var settling = new SeedSettling();

        for (var poll = 0; poll < 8; poll++)
            Assert.That(settling.Settled(null), Is.False);
    }

    /// <summary>A reading that disagrees with the one before starts the count again rather than adding to
    /// it. The device applies a loadout of three boards over about thirteen seconds; anything it says on the
    /// way there is a state it is passing through.</summary>
    [Test]
    public void A_reading_that_disagrees_starts_the_count_again()
    {
        var settling = new SeedSettling();

        settling.Settled([2, 13, 6, 0]);
        settling.Settled([2, 13, 6, 0]);
        Assert.That(settling.Settled([2, 0, 0, 0]), Is.False, "a different set, so this is the first of it");
        Assert.That(settling.Settled([2, 0, 0, 0]), Is.False);
        Assert.That(settling.Settled([2, 0, 0, 0]), Is.True);
    }

    /// <summary>All four slots Off settles like any other loadout, and it has to: an idle instrument with
    /// empty slots answers (0,0,0,0) in two milliseconds, which is what the restore waits for on behalf of a
    /// user who had no boards loaded when the sweep started. The difference between that and a load in
    /// flight is not in these four numbers at all -- it is whether the instrument answered -- which is why
    /// the caller passes null rather than zeros for the second one.</summary>
    [Test]
    public void Empty_slots_settle_like_any_other_loadout()
    {
        var settling = new SeedSettling();

        Assert.That(settling.Settled([0, 0, 0, 0]), Is.False);
        Assert.That(settling.Settled([0, 0, 0, 0]), Is.False);
        Assert.That(settling.Settled([0, 0, 0, 0]), Is.True);
    }

    /// <summary>Once settled, it stays settled: the poll loop stops at the first true and a fourth agreeing
    /// answer must not read as anything else. Trivial, and it is the sort of trivial that a counter written
    /// with an equality test rather than a threshold gets wrong.</summary>
    [Test]
    public void More_agreement_is_still_agreement()
    {
        var settling = new SeedSettling();

        settling.Settled([12, 0, 0, 0]);
        settling.Settled([12, 0, 0, 0]);
        settling.Settled([12, 0, 0, 0]);
        Assert.That(settling.Settled([12, 0, 0, 0]), Is.True);
    }
}
