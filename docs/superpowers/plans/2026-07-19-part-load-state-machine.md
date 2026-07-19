# Per-part load state machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the six ad-hoc fields that track a part's load lifecycle in `PartViewModel` with one pure, unit-tested state machine, fixing a completion window, an echoed program change, and a zero-settle read along the way.

**Architecture:** A new pure class `PartLoadState` in `Src/Models/Services/` holds the phase, epoch and reload-pending marker, and exposes transition methods that return *what the caller must do*. It contains no MIDI, no tasks and no ReactiveUI. `PartViewModel` owns one instance, keeps owning the `Task` and `CancellationTokenSource`, and performs the side effects each decision asks for.

**Tech Stack:** .NET 10, C# 13, Avalonia 12.0.5, ReactiveUI (`[Reactive]` / `[ReactiveCommand]` source generators), NUnit.

**Spec:** `docs/superpowers/specs/2026-07-19-part-load-state-machine-design.md`

---

## Domain background (read this first)

This application edits a Roland INTEGRA-7 hardware synthesizer over MIDI. It has 16 "parts", each
holding one tone (a patch). Reading a part's full state costs dozens of round trips, so a part is
loaded lazily: opening its tab triggers the load.

Three facts drive the whole design:

- The device answers a read **only for the tone the part currently holds**. Once the preset changes,
  every outstanding read of the old tone goes unanswered and costs a 1.5 s timeout.
- The device does **not** echo a program change the application itself sent. It only echoes changes
  made on its own front panel.
- After a program change the device needs ~250 ms before reads return the new patch. Reading earlier
  returns the *outgoing* patch — answered promptly and completely wrong.

"Preset comes from the device" means the application read the part's bank/program numbers and matched
them to a known patch, i.e. the device is *reporting* what it holds. It must never be answered by
sending a program change back.

## Build and test commands

The system `dotnet` on this machine is version 8/9 and **too old** for this solution. Always use the
user-local SDK:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

If a build fails with MSB3027 / MSB3021 ("cannot copy … being used by another process"), an instance
of the application is running and holding the output files. Close it and retry.

## Conventions

- Never pass `--no-verify` to `git commit`.
- Every commit message ends with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work happens on the branch `part-load-state-machine`, which already exists and already contains the
  spec commit. Do not merge to `main`; the user does that.
- Tests live in `Tests/`, one file per subject, named `TestXxx.cs`, `namespace Tests;`, NUnit
  `[TestFixture]`. See `Tests/TestFailedReadKeepsValues.cs` for house style.

## File structure

| File | Responsibility |
|---|---|
| `Src/Models/Services/PartLoadState.cs` (create) | The phase/epoch/reload-pending state machine and its four public transition methods. Pure: no MIDI, no tasks, no ReactiveUI. |
| `Tests/TestPartLoadState.cs` (create) | The full transition table as unit tests. |
| `Src/ViewModels/PartViewModel.cs` (modify) | Owns one `PartLoadState`; performs the side effects its decisions ask for. |
| `Src/ViewModels/MainWindowViewModel.cs` (modify) | Adopts `WantsRefresh` in the three places that ask `IsInitialized \|\| NeedsReinitialization`. |

`PartViewModel.cs` is 1984 lines. This plan does not restructure it — the change removes six fields
and rewires four methods, and an unrelated split would make the hardware verification pass much harder
to interpret.

---

### Task 1: `PartLoadState` — phases and `RequestOpen`

**Files:**
- Create: `Src/Models/Services/PartLoadState.cs`
- Test: `Tests/TestPartLoadState.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestPartLoadState.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestPartLoadState"
```

Expected: build failure, `CS0246: The type or namespace name 'PartLoadState' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/PartLoadState.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestPartLoadState"
```

Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/PartLoadState.cs Tests/TestPartLoadState.cs
git commit -m "feat: add a part load state machine with open and finish transitions

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `RequestPreset` — the preset transition table

**Files:**
- Modify: `Src/Models/Services/PartLoadState.cs`
- Test: `Tests/TestPartLoadState.cs`

- [ ] **Step 1: Write the failing tests**

Append inside the `TestPartLoadState` class in `Tests/TestPartLoadState.cs`, before the closing brace:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestPartLoadState"
```

Expected: build failure, `CS0246: The type or namespace name 'PresetSource' could not be found`.

- [ ] **Step 3: Write the implementation**

Add to `Src/Models/Services/PartLoadState.cs`, after the `LoadOutcome` enum:

```csharp
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
```

Add this method to the `PartLoadState` class, after `RequestOpen`:

```csharp
    /// <summary>Ask to change the part's preset.</summary>
    public PresetDecision RequestPreset(PresetSource source)
    {
        // Changing the preset mid-load means the load is reading a tone the device is about to drop.
        // The device is exempt: reporting what it holds is how the part learns what it is, including
        // during a load.
        if (source == PresetSource.User && Busy)
            return new PresetDecision(false, false, false, false, Epoch);

        var cancel = Phase == PartLoadPhase.Loading;
        var reload = cancel || Phase == PartLoadPhase.Loaded;

        Epoch++;
        ReloadPending = reload;

        // A reload is performed by RequestOpen, which answers None on a Loaded part. Abandoned is what
        // makes it start one, and is honest about the state in between: the patch has changed, so
        // nothing read so far still describes the part.
        if (reload) Phase = PartLoadPhase.Abandoned;

        return new PresetDecision(true, source == PresetSource.User, cancel, reload, Epoch);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestPartLoadState"
```

Expected: `Passed! - Failed: 0, Passed: 20` — 5 from Task 1 plus 15 here, counting the `[TestCase]`
rows individually (2 for `APartWithNoLoadedStateNeverReloadsOnAPresetChange`, 4 for
`APresetTheDeviceReportedIsNeverEchoedBack`).

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/PartLoadState.cs Tests/TestPartLoadState.cs
git commit -m "feat: decide preset changes from the part load state

A preset the device reported is no longer echoed back to it as a program
change, and a user change is refused for as long as the part is busy rather
than only while a load task happens to be incomplete.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Wire the load lifecycle into `PartViewModel`

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs:263-275` (fields), `:1358-1450` (lifecycle methods)

No new tests: this task is a rewiring whose behaviour is covered by Task 1–2 unit tests plus the
hardware pass in Task 8. The existing 314 tests must stay green.

- [ ] **Step 1: Replace the lifecycle fields**

In `Src/ViewModels/PartViewModel.cs`, replace lines 263-275:

```csharp
    /// <summary>The deferred initialization, once started. Doubles as the "already initialized" flag
    /// and as the handle concurrent callers await, so the work happens exactly once per part.</summary>
    private Task? _deferredInit;

    /// <summary>Cancels the deferred initialization when the part's preset changes underneath it.</summary>
    private CancellationTokenSource? _initCts;

    /// <summary>Counts preset changes, so a reload can tell whether it is still the current one.
    /// Picking presets faster than the device can load them is easy; only the last one should read.</summary>
    private int _presetGeneration;

    /// <summary>How long to let the device load a patch before reading it back.</summary>
    private const int PresetSettleMilliseconds = 250;
```

with:

```csharp
    /// <summary>Where this part is in its load lifecycle, and what to do about each request that
    /// touches it. See <see cref="PartLoadState"/>.</summary>
    private readonly PartLoadState _load = new();

    /// <summary>The running load, so concurrent callers await one load rather than starting three.</summary>
    private Task? _loadTask;

    /// <summary>Cancels a load that is reading a tone the device is about to drop.</summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>How long to let the device load a patch before reading it back.</summary>
    private const int PresetSettleMilliseconds = 250;
```

Add `using Integra7AuralAlchemist.Models.Services;` to the file's usings if it is not already there.

- [ ] **Step 2: Replace the lifecycle methods**

Replace `Src/ViewModels/PartViewModel.cs:1358-1450` — that is everything from `public Task EnsureInitializedAsync()`
through the closing brace of `RunDeferredInitAsync`, including `CancelDeferredInit`, `IsInitialized`,
`ToneTypeKey`, `IsLoading`, `_isLoading`, `_applyingPresetFromDevice`, `NeedsReinitialization` and
`_everOpened` — with:

```csharp
    /// <summary>Everything this part needs that its own tab has to be open to show: its MIDI and EQ
    /// blocks, the tone-type-specific domains, the ~157 partial view models and the friendly editors.
    /// Runs at most once — callers share the same task, so the tab selection, a resync and a hardware
    /// message racing to be first all wait on one initialization rather than starting three.</summary>
    public Task EnsureInitializedAsync()
    {
        if (_i7domain is null || IsCommonTab) return Task.CompletedTask;

        switch (_load.RequestOpen())
        {
            case OpenDecision.None:
                return Task.CompletedTask;
            case OpenDecision.JoinExisting:
                return _loadTask ?? Task.CompletedTask;
        }

        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        RaiseLoadStateChanged();
        _loadTask = RunDeferredInitAsync(_loadCts.Token, _load.Epoch);
        return _loadTask;
    }

    /// <summary>True once the deferred work has finished. Callers that only want to refresh a part
    /// use this to skip parts that were never opened: those read current hardware state when they are
    /// opened, so refreshing them early would spend round trips on data nobody is looking at.</summary>
    public bool IsInitialized => _load.Phase == PartLoadPhase.Loaded;

    /// <summary>True for a part that was opened and then had its load cancelled or failed. It has no
    /// usable tone state, so a refresh must re-initialize it rather than skip it the way it skips
    /// parts nobody ever opened.</summary>
    public bool NeedsReinitialization => _load.Phase == PartLoadPhase.Abandoned;

    /// <summary>True for a part that has been opened at some point and is not currently loading — the
    /// parts a refresh should actually visit.</summary>
    public bool WantsRefresh => _load.Phase is PartLoadPhase.Loaded or PartLoadPhase.Abandoned;

    /// <summary>This part's tone type, as the key the Advanced tabs repair their selection against.
    /// Which of those tabs are visible depends on it, and Avalonia keeps rendering a selected tab's
    /// content after it is hidden (#16879) — so opening a part whose engine differs from the selected
    /// sub-tab would show the other engine's parameters, which were never read.</summary>
    public string ToneTypeKey => _selectedPreset?.ToneTypeStr ?? "";

    /// <summary>True while this part is loading or owes a reload. The preset list is disabled
    /// meanwhile: changing the preset mid-load means the load is reading a tone the device is about to
    /// drop. This is the same predicate the state machine refuses user changes on, so the visible rule
    /// and the enforced rule cannot drift apart.</summary>
    public bool IsLoading => _load.Busy;

    /// <summary>Announce every property that reads off the load state. They are computed rather than
    /// stored, so nothing raises these notifications on their behalf.</summary>
    private void RaiseLoadStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsLoading));
        this.RaisePropertyChanged(nameof(IsInitialized));
        this.RaisePropertyChanged(nameof(NeedsReinitialization));
        this.RaisePropertyChanged(nameof(WantsRefresh));
    }

    private async Task RunDeferredInitAsync(CancellationToken token, int epoch)
    {
        // Never run synchronously: EnsureInitializedAsync assigns _loadTask from the return value,
        // so a body that reset the field before that assignment would have its reset overwritten.
        await Task.Yield();

        var outcome = LoadOutcome.Completed;
        try
        {
            // Bracketed here rather than at the call site, so a re-initialization triggered by a preset
            // change is traced too — not just the one a tab click starts.
            UserActionLog.Begin($"initialize part {PartNo}");
            await InitializeDeferredPartStateAsync(token);
            UserActionLog.End($"initialize part {PartNo}");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a preset change; the fresh load it started owns the part now.
            outcome = LoadOutcome.Cancelled;
            UserActionLog.Action($"part {PartNo}: initialization abandoned, the preset changed");
        }
        catch
        {
            // Let a later open (or resync) try again rather than caching the failure forever.
            outcome = LoadOutcome.Failed;
            throw;
        }
        finally
        {
            // Tagged with the epoch this load started at, so a cancelled load reporting late cannot
            // bury the replacement that took over from it.
            _load.LoadFinished(outcome, epoch);
            RaiseLoadStateChanged();
        }
    }
```

- [ ] **Step 3: Build**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: errors in `PartViewModel.cs` only where `_deferredInit`, `_initCts`, `_presetGeneration`,
`_applyingPresetFromDevice` and `CancelDeferredInit` are still referenced — the `SelectedPreset`
setter (around `:1105-1152`), `PreSelectConfiguredPreset` (around `:1180-1188`) and
`ChangePresetAndReloadAsync` (around `:1780-1798`). Tasks 4 and 5 fix those. Do not fix them here.

- [ ] **Step 4: Commit**

Commit even though the build is red — the next two tasks are the other half of one rewiring, and
splitting them keeps each diff reviewable.

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "refactor: drive the part load lifecycle from PartLoadState

Intermediate commit: the preset setter and reload path still reference the
removed fields and are rewired in the next two commits.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Route preset changes through the state machine

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs:1105-1152` (`SelectedPreset`), `:1168-1192` (`PreSelectConfiguredPreset`)

- [ ] **Step 1: Replace the `SelectedPreset` setter**

Replace `Src/ViewModels/PartViewModel.cs:1105-1152` (the whole `public Integra7Preset SelectedPreset` property) with:

```csharp
    public Integra7Preset SelectedPreset
    {
        get => _selectedPreset;
        set => ApplyPreset(value, PresetSource.User);
    }

    /// <summary>Apply a preset the device reported holding, as opposed to one the user picked. The
    /// difference matters twice over: it is allowed through mid-load, and it must not be answered with
    /// a program change for a patch the device is already holding.</summary>
    public void ApplyDevicePreset(Integra7Preset value) => ApplyPreset(value, PresetSource.Device);

    private void ApplyPreset(Integra7Preset value, PresetSource source)
    {
        if (_selectedPreset == value || value is null) return;

        var decision = _load.RequestPreset(source);
        if (!decision.Accepted)
        {
            UserActionLog.Action(
                $"part {PartNo}: ignoring preset '{value.Name}', the part is still loading");
            // Snap the list back to what is really selected. Named explicitly: the caller-member-name
            // overload would announce this method rather than the bound property.
            this.RaisePropertyChanged(nameof(SelectedPreset));
            return;
        }

        UserActionLog.Action(
            $"part {PartNo}: select preset '{value.Name}' ({value.ToneTypeStr} {value.InternalUserDefinedStr}, " +
            $"msb {value.Msb} lsb {value.Lsb} pc {value.Pc})");

        _selectedPreset = value;

        // Stop a load that is reading the outgoing tone's domains: those reads go to a tone the device
        // is about to stop holding, and each one waits out its timeout.
        if (decision.CancelCurrentLoad)
        {
            _loadCts?.Cancel();
            _loadTask = null;
        }

        _ = ChangePresetAndReloadAsync(decision);
        this.RaisePropertyChanged(nameof(SelectedPreset));
        RaiseLoadStateChanged();
    }
```

- [ ] **Step 2: Route `PreSelectConfiguredPreset` through the device path**

In `Src/ViewModels/PartViewModel.cs`, inside `PreSelectConfiguredPreset`, replace:

```csharp
                // This reflects what the device reports, so it is allowed through even mid-load.
                _applyingPresetFromDevice = true;
                try
                {
                    SelectedPreset = p;
                }
                finally
                {
                    _applyingPresetFromDevice = false;
                }

                return;
```

with:

```csharp
                // This reflects what the device reports, so it is allowed through even mid-load — and
                // is never answered with a program change for the patch the device already holds.
                ApplyDevicePreset(p);
                return;
```

- [ ] **Step 3: Build**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: the only remaining errors are in `ChangePresetAndReloadAsync` (around `:1780`), which still
takes `(bool reload, int generation)` and references `_presetGeneration`. Task 5 fixes it.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "refactor: decide preset changes by source rather than by an ambient flag

PreSelectConfiguredPreset now calls ApplyDevicePreset instead of setting the
bound property inside a try/finally that flipped a field, which removes the
reentrancy window between the two.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Make the state machine own the reload

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs:1773-1798` (`ChangePresetAndReloadAsync`)

- [ ] **Step 1: Replace `ChangePresetAndReloadAsync`**

Replace `Src/ViewModels/PartViewModel.cs:1773-1798` — the `[ReactiveCommand]` attribute, the doc
comment and the whole `ChangePresetAndReloadAsync` method — with:

```csharp
    [ReactiveCommand]
    /// <summary>Send the program change if this part is the one asking for it, then reload.
    ///
    /// The order is the whole point: reading before the device has switched returns the outgoing
    /// patch, promptly and wrongly. This is the only path that refreshes a part after a preset
    /// change — the UpdateResyncPart that Integra7Api posts alongside every program change is dropped
    /// by ResyncPartAsync while the reload is pending, because it carries no settle delay.</summary>
    private async Task ChangePresetAndReloadAsync(PresetDecision decision)
    {
        // A preset the device reported is already loaded there; sending it back would be an echo.
        if (decision.SendProgramChange) await ChangePresetAsync();

        if (!decision.Reload) return;

        // Another preset was picked while this one was being sent. That change is doing its own
        // reload, and this one would read the device before its program change had arrived — the
        // reads are answered, just for the wrong patch.
        if (!_load.IsCurrent(decision.Epoch)) return;

        // The device needs a moment to actually load the patch. Reading the instant the program
        // change goes out returns the outgoing tone, which is how a part ended up showing a partial
        // that was never read: correct-looking common values, zeroed partials.
        await Task.Delay(PresetSettleMilliseconds);
        if (!_load.IsCurrent(decision.Epoch)) return;

        await EnsureInitializedAsync();
    }
```

- [ ] **Step 2: Build and run the full test suite**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: build succeeds with no errors; `Failed: 0, Passed: 334` (314 existing + 20 from Tasks 1–2).

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "fix: give the reload after a preset change a single owner

The reload path now always applies the settle delay, and a preset the device
reported no longer causes a program change to be sent back to it.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Drop the racing resync

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs:1812-1822` (top of `ResyncPartAsync`)

`Integra7Api.ChangePresetAsync` (`Src/Models/Services/Integra7Api.cs:445-451`) posts
`UpdateResyncPart` immediately after its three MIDI messages, with no settle delay. That resync and
the reload from Task 5 both refresh the same part, and the resync reads too early.

- [ ] **Step 1: Add the guard**

In `Src/ViewModels/PartViewModel.cs`, in `ResyncPartAsync`, replace:

```csharp
    public async Task ResyncPartAsync(byte part)
    {
        if (_i7domain is null)
            return;

        if (part != PartNo)
            return;
```

with:

```csharp
    public async Task ResyncPartAsync(byte part)
    {
        if (_i7domain is null)
            return;

        if (part != PartNo)
            return;

        // Integra7Api posts UpdateResyncPart alongside every program change (Integra7Api.cs:450), with
        // no settle delay — so for a preset change it would read the outgoing patch, and would be the
        // second refresh of the same part. The reload started by the preset change owns that refresh
        // and applies the delay. Resyncs from any other trigger — the device's own front panel, an SRX
        // load, ResyncAllPartsAsync — never see this flag set.
        if (!IsCommonTab && _load.ReloadPending)
        {
            UserActionLog.Action($"part {PartNo}: skipping resync, a reload is already pending");
            return;
        }
```

- [ ] **Step 2: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 334`.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "fix: skip the untimed resync that races a pending reload

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Adopt `WantsRefresh` in `MainWindowViewModel`

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs:737`, `:763`, `:794`

- [ ] **Step 1: Replace the three call sites**

At `Src/ViewModels/MainWindowViewModel.cs:737`, replace:

```csharp
                    if (!pvm.IsCommonTab && !pvm.IsInitialized && !pvm.NeedsReinitialization) continue;
```

with:

```csharp
                    if (!pvm.IsCommonTab && !pvm.WantsRefresh) continue;
```

At `:763`, replace:

```csharp
                    if (!pvm.IsCommonTab && !pvm.IsInitialized && !pvm.NeedsReinitialization) continue;
```

with:

```csharp
                    if (!pvm.IsCommonTab && !pvm.WantsRefresh) continue;
```

At `:794`, replace:

```csharp
                        if (pvm.IsCommonTab || pvm.IsInitialized || pvm.NeedsReinitialization)
```

with:

```csharp
                        if (pvm.IsCommonTab || pvm.WantsRefresh)
```

- [ ] **Step 2: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 334`.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs
git commit -m "refactor: name the opened-at-some-point condition WantsRefresh

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: Hardware verification

**Files:** none — this task runs the application against the real INTEGRA-7.

Tasks 5 and 6 change behaviour on the device, so this task cannot be skipped or simulated. The
implementer must **stop and hand this to the user**, who has the hardware. Do not mark the plan
complete without it.

- [ ] **Step 1: Confirm no other instance is running**

An already-running instance both locks the build output and makes screenshots ambiguous. Check before
launching:

```bash
tasklist | grep -i integra7
```

Expected: no rows. If there are, close that instance first.

- [ ] **Step 2: Launch and collect the log**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj
```

The log is written to `Src/bin/Debug/net10.0/logs/I7AuralAlchemist<YYYYMMDD>.log`. The `[UI]` lines
from `UserActionLog` bracket each part load with `BEGIN` / `END`.

- [ ] **Step 3: Run the four scenarios**

1. **Preset picked during a load.** Start up, switch to a part while the application is still
   initializing, then click a preset. Expected: the click is refused and logged as
   `ignoring preset '…', the part is still loading`; the list stays disabled; the preset applies
   normally once the load finishes.
2. **Preset changed on a loaded part.** Expected: exactly one refresh in the log — one
   `BEGIN initialize part N` — and a `skipping resync, a reload is already pending` line for the
   resync that `Integra7Api` posted. No read is issued inside the 250 ms after the program change.
3. **Front-panel preset change while a part is loading.** Change the preset on the INTEGRA-7 itself
   during a load. Expected: **no program change is sent by the application** (this is the echo the old
   code produced); the running load is abandoned and a fresh one starts. This is the untested path
   into the deadlock recorded in the `device-access-model-redesign` memory, which is why it is called
   out rather than left to incidental testing.
4. **Front-panel preset change on a loaded part.** Expected: the part refreshes once, with no echoed
   program change, and the Advanced sub-tabs show the new engine's parameters rather than blanks or
   construction defaults.

- [ ] **Step 4: Confirm no regression in the previously broken areas**

For each of a PCM Synth, an SN-S and an SN-D part: open the tab, check that Advanced → Partials is
populated and that the Sound sub-tab's waveform and oscillator show real values rather than empty or
default ones. These are the symptoms the original lazy-loading defects presented as.

- [ ] **Step 5: Report**

Report the outcome of each scenario with the relevant log lines. If any scenario fails, stop and
report rather than patching around it — a failure here most likely means a transition in
`PartLoadState` is wrong, and it should be fixed as a unit test first.

---

## Notes for the implementer

- **`ToneTypeKey` change notifications are deliberately unchanged.** Nothing raises them today either;
  the binding at `Src/Views/MainWindow.axaml:227` is re-evaluated when the tab switches. Adding a
  notification would be a separate behaviour change and would muddy the hardware pass.
- **`RaisePropertyChanged()` without an argument uses `[CallerMemberName]`.** Inside `ApplyPreset` that
  would announce `ApplyPreset`, not `SelectedPreset`. Every call in this plan names its property.
- **Avalonia bindings write through `SetValue` and bypass CLR property setters** on `StyledProperty`.
  That does not apply here — `SelectedPreset` is a plain view model property bound normally — but it is
  the reason similar-looking code elsewhere in this repository moves logic out of setters.
- **Do not merge to `main`.** The user does that, after the hardware pass.
