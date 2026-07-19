using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The part load lifecycle as a table. Every case here needs no hardware and no view model,
/// which is the point: the six flags this class replaces could only be exercised against a live
/// device.</summary>
[TestFixture]
public class TestPartLoadState
{
    [Test]
    public void APartStartsOutNeverOpened()
    {
        var s = new PartLoadState();

        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.NeverOpened));
        Assert.That(s.Busy, Is.False);
        Assert.That(s.ReloadPending, Is.False);
    }

    [Test]
    public void OpeningAPartForTheFirstTimeStartsALoad()
    {
        var s = new PartLoadState();

        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.StartLoad));
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading));
        Assert.That(s.Busy, Is.True);
    }

    [Test]
    public void OpeningAPartThatIsAlreadyLoadingJoinsTheRunningLoad()
    {
        var s = new PartLoadState();
        s.RequestOpen();

        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.JoinExisting));
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading));
    }

    [Test]
    public void OpeningAnAlreadyLoadedPartDoesNothing()
    {
        var s = new PartLoadState();
        s.RequestOpen();
        s.LoadFinished(LoadOutcome.Completed, s.Epoch);

        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.None));
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loaded));
    }

    [Test]
    public void OpeningAnAbandonedPartLoadsItAgain()
    {
        var s = new PartLoadState();
        s.RequestOpen();
        s.LoadFinished(LoadOutcome.Failed, s.Epoch);
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Abandoned));

        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.StartLoad));
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading));
    }
}
