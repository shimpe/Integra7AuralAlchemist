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

    /// <summary>Drives a fresh state machine into one of the four phases.</summary>
    private static PartLoadState InPhase(PartLoadPhase phase)
    {
        var s = new PartLoadState();
        switch (phase)
        {
            case PartLoadPhase.NeverOpened:
                break;
            case PartLoadPhase.Loading:
                s.RequestOpen();
                break;
            case PartLoadPhase.Loaded:
                s.RequestOpen();
                s.LoadFinished(LoadOutcome.Completed, s.Epoch);
                break;
            case PartLoadPhase.Abandoned:
                s.RequestOpen();
                s.LoadFinished(LoadOutcome.Failed, s.Epoch);
                break;
        }

        Assert.That(s.Phase, Is.EqualTo(phase), "the fixture failed to reach the phase under test");
        return s;
    }

    [Test]
    public void AUserPresetChangeIsRefusedWhileThePartIsLoading()
    {
        var s = InPhase(PartLoadPhase.Loading);
        var epochBefore = s.Epoch;

        var d = s.RequestPreset(PresetSource.User);

        Assert.That(d.Accepted, Is.False);
        Assert.That(s.Epoch, Is.EqualTo(epochBefore), "a refused change must not consume an epoch");
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading));
    }

    [Test]
    public void AUserPresetChangeIsRefusedDuringTheSettleDelay()
    {
        // No load is running between the program change and its reload, but the part is not idle.
        var s = InPhase(PartLoadPhase.Loaded);
        s.RequestPreset(PresetSource.User);
        Assert.That(s.ReloadPending, Is.True);
        Assert.That(s.Busy, Is.True);

        var d = s.RequestPreset(PresetSource.User);

        Assert.That(d.Accepted, Is.False);
    }

    [Test]
    public void ADevicePresetChangeDuringALoadCancelsItAndSendsNothingBack()
    {
        var s = InPhase(PartLoadPhase.Loading);

        var d = s.RequestPreset(PresetSource.Device);

        Assert.That(d.Accepted, Is.True);
        Assert.That(d.SendProgramChange, Is.False,
            "the device already holds this patch; answering with a program change is the echo bug");
        Assert.That(d.CancelCurrentLoad, Is.True, "the running load's reads describe a stale patch");
        Assert.That(d.Reload, Is.True);
    }

    [Test]
    public void AUserPresetChangeOnALoadedPartSendsTheProgramChangeAndReloads()
    {
        var s = InPhase(PartLoadPhase.Loaded);

        var d = s.RequestPreset(PresetSource.User);

        Assert.That(d.Accepted, Is.True);
        Assert.That(d.SendProgramChange, Is.True);
        Assert.That(d.CancelCurrentLoad, Is.False);
        Assert.That(d.Reload, Is.True);
    }

    [Test]
    public void ADevicePresetChangeOnALoadedPartReloadsWithoutSendingAnything()
    {
        var s = InPhase(PartLoadPhase.Loaded);

        var d = s.RequestPreset(PresetSource.Device);

        Assert.That(d.SendProgramChange, Is.False);
        Assert.That(d.CancelCurrentLoad, Is.False);
        Assert.That(d.Reload, Is.True);
    }

    [TestCase(PartLoadPhase.NeverOpened)]
    [TestCase(PartLoadPhase.Abandoned)]
    public void APartWithNoLoadedStateNeverReloadsOnAPresetChange(PartLoadPhase phase)
    {
        var s = InPhase(phase);

        var d = s.RequestPreset(PresetSource.User);

        Assert.That(d.Accepted, Is.True);
        Assert.That(d.SendProgramChange, Is.True);
        Assert.That(d.Reload, Is.False, "there is no loaded state to refresh; opening the tab loads it");
        Assert.That(s.ReloadPending, Is.False);
    }

    [TestCase(PartLoadPhase.NeverOpened)]
    [TestCase(PartLoadPhase.Loading)]
    [TestCase(PartLoadPhase.Loaded)]
    [TestCase(PartLoadPhase.Abandoned)]
    public void APresetTheDeviceReportedIsNeverEchoedBack(PartLoadPhase phase)
    {
        var s = InPhase(phase);

        Assert.That(s.RequestPreset(PresetSource.Device).SendProgramChange, Is.False);
    }

    [Test]
    public void AReloadActuallyStartsALoad()
    {
        // Without this, RequestOpen would answer None on a Loaded part, no load would run,
        // ReloadPending would never clear, and the preset list would stay disabled forever.
        var s = InPhase(PartLoadPhase.Loaded);
        var d = s.RequestPreset(PresetSource.User);
        Assert.That(d.Reload, Is.True);

        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.StartLoad));
    }

    [Test]
    public void FinishingAReloadClearsThePendingMarker()
    {
        var s = InPhase(PartLoadPhase.Loaded);
        s.RequestPreset(PresetSource.User);
        s.RequestOpen();

        s.LoadFinished(LoadOutcome.Completed, s.Epoch);

        Assert.That(s.ReloadPending, Is.False);
        Assert.That(s.Busy, Is.False);
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loaded));
    }

    [Test]
    public void ASupersededLoadReportingLateChangesNothing()
    {
        var s = InPhase(PartLoadPhase.Loading);
        var staleEpoch = s.Epoch;
        s.RequestPreset(PresetSource.Device);   // cancels the running load, bumps the epoch
        s.RequestOpen();                        // the replacement load starts
        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading));

        s.LoadFinished(LoadOutcome.Cancelled, staleEpoch);

        Assert.That(s.Phase, Is.EqualTo(PartLoadPhase.Loading),
            "the cancelled load must not bury the replacement that took over from it");
        Assert.That(s.ReloadPending, Is.True);
    }

    [Test]
    public void EachAcceptedChangeSupersedesTheOneBeforeIt()
    {
        var s = InPhase(PartLoadPhase.Loaded);

        var first = s.RequestPreset(PresetSource.Device);
        var second = s.RequestPreset(PresetSource.Device);

        Assert.That(s.IsCurrent(first.Epoch), Is.False);
        Assert.That(s.IsCurrent(second.Epoch), Is.True);
    }

    [Test]
    public void AReportFromTheDeviceInsideTheSettleWindowDoesNotCancelTheOwedReload()
    {
        // The user changed the preset, so a reload is owed and the caller is waiting out the settle
        // delay before starting it. A report arriving from the device meanwhile must not silently
        // take that reload away — nothing else would ever start it, and the part would sit open with
        // no state anybody read.
        var s = InPhase(PartLoadPhase.Loaded);
        var owed = s.RequestPreset(PresetSource.User);
        Assert.That(owed.Reload, Is.True);
        Assert.That(s.ReloadPending, Is.True);

        var d = s.RequestPreset(PresetSource.Device);

        Assert.That(d.Reload, Is.True, "the reload the settle window exists for is still owed");
        Assert.That(s.ReloadPending, Is.True);
        Assert.That(s.Busy, Is.True, "the preset list must stay disabled until something reloads");
        Assert.That(s.RequestOpen(), Is.EqualTo(OpenDecision.StartLoad));
    }

    [Test]
    public void OnlyFinishingALoadClearsAnOwedReload()
    {
        var s = InPhase(PartLoadPhase.Loaded);
        s.RequestPreset(PresetSource.User);
        s.RequestPreset(PresetSource.Device);
        s.RequestPreset(PresetSource.Device);
        Assert.That(s.ReloadPending, Is.True);

        s.RequestOpen();
        s.LoadFinished(LoadOutcome.Completed, s.Epoch);

        Assert.That(s.ReloadPending, Is.False);
        Assert.That(s.Busy, Is.False);
    }
}
