namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Where a part is in its load lifecycle.</summary>
public enum PartLoadPhase
{
    /// <summary>The tab was never opened, so no tone state exists. Nothing to refresh.</summary>
    NeverOpened,

    /// <summary>The read sequence is running.</summary>
    Loading,

    /// <summary>Usable tone state.</summary>
    Loaded,

    /// <summary>Opened at some point, then cancelled or failed. No usable state, load when asked.</summary>
    Abandoned
}

/// <summary>What a caller must do about an open request.</summary>
public enum OpenDecision
{
    /// <summary>Create a cancellation source and start the read sequence.</summary>
    StartLoad,

    /// <summary>A load is already running; await that one rather than starting a second.</summary>
    JoinExisting,

    /// <summary>Already loaded; nothing to do.</summary>
    None
}

/// <summary>How a load ended.</summary>
public enum LoadOutcome { Completed, Cancelled, Failed }

/// <summary>Who asked for a preset change. The device reporting which patch a part holds is not the
/// same event as the user picking one, and the two get different answers.</summary>
public enum PresetSource
{
    /// <summary>The user picked a preset from the list.</summary>
    User,

    /// <summary>The application read the part's bank and program numbers and matched a patch, i.e.
    /// the device is reporting what it already holds.</summary>
    Device
}

/// <summary>What a caller must do about a preset change. When <see cref="Accepted"/> is false the
/// change is refused and the caller must raise a change notification so the bound list snaps back to
/// the real selection.</summary>
public readonly record struct PresetDecision(
    bool Accepted,
    bool SendProgramChange,
    bool CancelCurrentLoad,
    bool Reload,
    int Epoch);

/// <summary>A part's load lifecycle, as an explicit state machine rather than a set of flags.
///
/// Pure by design: it holds no task, no cancellation source and no device handle, so the whole
/// transition table is unit-testable without hardware. Callers own the side effects; this type only
/// decides what those side effects should be.</summary>
public sealed class PartLoadState
{
    public PartLoadPhase Phase { get; private set; } = PartLoadPhase.NeverOpened;

    /// <summary>Counts accepted preset changes, so a reload can tell whether it is still the current
    /// one. Picking presets faster than the device can load them is easy; only the last should read.
    /// A load records this when it starts and hands it back to <see cref="LoadFinished"/>.</summary>
    public int Epoch { get; private set; }

    /// <summary>True between an accepted preset change and the completion of the reload it asked for.
    /// No load is running during the settle delay, but the part is not idle either.</summary>
    public bool ReloadPending { get; private set; }

    /// <summary>True while the part is loading or owes a reload. This — not the phase alone — is what
    /// refuses a user preset change and what the preset list's enabled state binds to, so the two can
    /// never disagree.</summary>
    public bool Busy => Phase == PartLoadPhase.Loading || ReloadPending;

    /// <summary>True when <paramref name="epoch"/> is still the newest accepted preset change.</summary>
    public bool IsCurrent(int epoch) => epoch == Epoch;

    /// <summary>Ask to open the part.</summary>
    public OpenDecision RequestOpen()
    {
        switch (Phase)
        {
            case PartLoadPhase.Loading:
                return OpenDecision.JoinExisting;
            case PartLoadPhase.Loaded:
                return OpenDecision.None;
            default:
                Phase = PartLoadPhase.Loading;
                return OpenDecision.StartLoad;
        }
    }

    /// <summary>Ask to change the part's preset.</summary>
    public PresetDecision RequestPreset(PresetSource source)
    {
        // Changing the preset mid-load means the load is reading a tone the device is about to drop.
        // The device is exempt: reporting what it holds is how the part learns what it is, including
        // during a load.
        if (source == PresetSource.User && Busy)
            return new PresetDecision(false, false, false, false, Epoch);

        var cancel = Phase == PartLoadPhase.Loading;

        // A reload already owed stays owed. ReloadPending is cleared by LoadFinished and by nothing
        // else: a change arriving inside the settle window must not cancel the reload that window
        // exists for.
        var reload = cancel || Phase == PartLoadPhase.Loaded || ReloadPending;

        Epoch++;
        ReloadPending = reload;

        // A reload is performed by RequestOpen, which answers None on a Loaded part. Abandoned is what
        // makes it start one, and is honest about the state in between: the patch has changed, so
        // nothing read so far still describes the part.
        if (reload) Phase = PartLoadPhase.Abandoned;

        return new PresetDecision(true, source == PresetSource.User, cancel, reload, Epoch);
    }

    /// <summary>Report that the load started at <paramref name="epoch"/> has ended.
    ///
    /// A report carrying a stale epoch is ignored. A cancelled load can finish long after its
    /// replacement started — cancellation only takes effect at the next checkpoint, and an in-flight
    /// read cannot be interrupted at all — so an untagged report would overwrite the new load's
    /// phase.</summary>
    public void LoadFinished(LoadOutcome outcome, int epoch)
    {
        if (!IsCurrent(epoch)) return;

        Phase = outcome == LoadOutcome.Completed ? PartLoadPhase.Loaded : PartLoadPhase.Abandoned;
        ReloadPending = false;
    }
}
