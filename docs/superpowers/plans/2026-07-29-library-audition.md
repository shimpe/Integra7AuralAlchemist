# Library audition — implementation plan (phase 3 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** hear a library patch in the selected part without losing what that part holds.

**Architecture:** `AuditionState` — a small immutable record and the four transitions over it — decides what
is borrowed and what has to be given back. `Audition` does the two device operations: capture then write,
and write back. The window owns one state and wires the triggers that end a session.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI 24, NUnit 4.

**Spec:** `docs/superpowers/specs/2026-07-29-library-overhaul-design.md`, the "Phase 3" section. **Read it
first** — it records a change of mind made while planning this phase, and the paragraph explaining why
audition is same-engine only is the single most important thing to understand before writing any of this.

**Phase 3 of five.** Phases 1 and 2 are merged. Phases 4 and 5 are separate plans.

---

## What this phase is, in one paragraph

Pressing **Audition** on a library tone captures the selected part's current tone into memory, writes the
library tone over it, and leaves it playing. Pressing **Stop**, leaving the Library tab, loading anything
for real, or closing the application writes the captured tone back. Choosing a different candidate while a
session is running keeps the *original* memory and only writes the new candidate — so browsing ten patches
still gives the part back exactly as it was.

**Same engine only.** A tone can only be written into a part whose temporary tone is already that engine
(`EnsureToneFitsPart`). Making cross-engine work needs a preset change and a full part reload each way; the
spec records why that is out of scope here. A candidate whose engine differs is refused with the message
`Load` already gives.

---

## Conventions for every task

**Build and test with the user-local SDK** — the system `dotnet` is 8/9 and too old. `Src/bin` is routinely
locked by the user's own running application or Rider's previewer; **never kill either**, redirect instead.
The four-deep path and the junction are both load-bearing, because several tests find
`Src\Assets\parameters.bin` by walking `..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **970 passed, 0 failed**.

**Traps this project has actually hit**, all of which apply here:

- **An XML comment may not contain `--`**, and **a comment may not sit between an element's attributes**.
  The first makes MSBuild fail to *load* the project (`MSB4025`), so nothing compiles and the error count
  reads as zero. Check for `MSB4025` before believing a sudden green. Prose uses real em dashes.
- **Never hardcode a colour in XAML.** Use `{StaticResource ...}`.
- **A `ToolTip` is a popup and swallows clicks on its own control.**
- **Do not edit `.axaml` with `sed` or rewrite source through PowerShell** — CRLF with a BOM, and
  PowerShell 5.1's `Set-Content` defaults to ANSI.
- Compiled bindings are checked at build time; a wrong member name is `AVLN2000`.
- **A view model cannot be constructed in a test** under ReactiveUI 24. Anything worth testing goes in a
  service.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/library-audition`, which already holds this plan and the spec amendment. Explicit
paths only; never `git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do not
merge or push.

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `Src/Models/Services/AuditionState.cs` | What is borrowed, and the four transitions over it |
| Create `Src/Models/Services/Audition.cs` | The two device operations: borrow, and give back |
| Modify `Src/ViewModels/LibraryEditorViewModel.cs` | The Audition/Stop button and what it says |
| Modify `Src/Views/LibraryEditorView.axaml` | Its markup |
| Modify `Src/ViewModels/LibraryViewModel.cs` | Passes the callback through; stops on a real load |
| Modify `Src/ViewModels/MainWindowViewModel.cs` | Owns the session; the triggers that end one |

**New tests:** `Tests/TestAuditionState.cs`, `Tests/TestAudition.cs`.

---

### Task 1: `AuditionState`

**Files:** Create `Src/Models/Services/AuditionState.cs`; Test `Tests/TestAuditionState.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What a running audition is holding, and the four things that can happen to it.
///
/// These are transitions rather than arithmetic, and every one of them is a way to lose a user's sound: a
/// start that forgets what was there, a switch that overwrites the memory, a stop that gives back the wrong
/// thing. That is why they are a record with tests rather than three fields on a view model.</summary>
public class AuditionStateTests
{
    private static Integra7Snapshot Tone(string name) =>
        new(Integra7Snapshot.CurrentFormatVersion, name, [], SnapshotKinds.Tone, "SN-S");

    [Test]
    public void Nothing_is_borrowed_to_begin_with()
    {
        Assert.That(AuditionState.Idle.IsRunning, Is.False);
    }

    [Test]
    public void Starting_remembers_the_part_its_engine_and_what_was_on_it()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\Warm Rhodes.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.ZeroBasedPartNo, Is.EqualTo(2));
            Assert.That(state.ToneType, Is.EqualTo("SN-S"));
            Assert.That(state.Borrowed!.Name, Is.EqualTo("what was there"));
        });
    }

    /// <summary>The rule the whole feature rests on. Browsing ten patches must still give back the one
    /// sound that was there before the first of them, so a second candidate replaces what is playing and
    /// never what is remembered.</summary>
    [Test]
    public void Switching_candidate_keeps_the_original_memory_and_the_engine()
    {
        var state = AuditionState.Idle
            .Start(2, "SN-S", Tone("what was there"), @"C:\lib\Warm Rhodes.json")
            .Switch(@"C:\lib\Glass Bell.json")
            .Switch(@"C:\lib\Old Pad.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.Borrowed!.Name, Is.EqualTo("what was there"));
            Assert.That(state.ZeroBasedPartNo, Is.EqualTo(2));
            Assert.That(state.ToneType, Is.EqualTo("SN-S"),
                "the engine is the part's, so a later candidate has something to be checked against");
            Assert.That(state.IsPlaying(@"C:\lib\Old Pad.json"), Is.True);
        });
    }

    /// <summary>Which row the panel offers Stop on. By path, because two library files can hold tones of
    /// the same name and a name comparison would put Stop on the wrong row.</summary>
    [Test]
    public void The_playing_file_is_recognised_by_path_whatever_its_case()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("x"), @"C:\lib\Warm Rhodes.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.IsPlaying(@"c:\LIB\warm rhodes.json"), Is.True);
            Assert.That(state.IsPlaying(@"C:\lib\Other.json"), Is.False);
            Assert.That(AuditionState.Idle.IsPlaying(@"C:\lib\Warm Rhodes.json"), Is.False,
                "and nothing is playing when nothing is running");
        });
    }

    /// <summary>Switching without a session is not a session. It cannot happen through the user interface,
    /// which only offers Stop while one is running -- and a state machine that quietly invented a session
    /// with nothing remembered would give back nothing on Stop.</summary>
    [Test]
    public void Switching_with_nothing_running_stays_idle()
    {
        Assert.That(AuditionState.Idle.Switch(@"C:\lib\Glass Bell.json").IsRunning, Is.False);
    }

    [Test]
    public void Stopping_gives_up_what_it_was_holding()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\a.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.Stop().IsRunning, Is.False);
            Assert.That(state.Stop().Borrowed, Is.Null);
        });
    }

    [Test]
    public void Stopping_when_nothing_is_running_is_harmless()
    {
        Assert.That(AuditionState.Idle.Stop().IsRunning, Is.False);
    }

    /// <summary>A restore that failed must leave the session intact so Stop can be pressed again -- the
    /// instrument is still holding the candidate, and forgetting the memory would strand it there.</summary>
    [Test]
    public void A_state_that_could_not_be_given_back_is_still_running()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\a.json");

        Assert.That(state.IsRunning, Is.True, "Stop is the caller's to retry; the state itself is unchanged");
    }
}
```

- [ ] **Step 2: Run and watch it fail.** Expected: `CS0103`, `AuditionState` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a running audition has borrowed from a part, and what it is playing there instead.
///
/// <b>Immutable, and a record, because the one thing this must never do is lose the memory.</b> Every
/// transition answers a new state rather than editing this one, so there is no path on which a field is
/// half updated -- and the caller that holds it can only replace it, never quietly mutate it.
///
/// <b><see cref="Borrowed"/> is set once per session.</b> Choosing a second candidate while one is playing
/// replaces <see cref="Playing"/> and nothing else. That is what lets a user browse ten patches and still
/// get back the sound that was on the part before the first of them.</summary>
/// <param name="ZeroBasedPartNo">The part being borrowed, or -1 when nothing is.</param>
/// <param name="ToneType">The engine that part holds. <b>Carried by the session, not taken from each new
/// candidate</b>: it is what a second candidate has to match, and reading it off the candidate itself would
/// make every candidate match itself and let a tone of another engine through the guard.</param>
/// <param name="Borrowed">The part's own tone, captured before the first candidate was written.</param>
/// <param name="PlayingPath">The file being heard. <b>The path, not the name</b> -- two library files can
/// hold tones of the same name, and the panel decides whether its button says Stop by asking whether the
/// selected row is this one.</param>
public sealed record AuditionState(int ZeroBasedPartNo, string ToneType, Integra7Snapshot? Borrowed,
    string PlayingPath)
{
    public static readonly AuditionState Idle = new(-1, "", null, "");

    public bool IsRunning => Borrowed is not null;

    /// <summary>Whether this file is the one being heard. Case-insensitive, because Windows and macOS both
    /// hand back a path that differs from the stored one only in case.</summary>
    public bool IsPlaying(string filePath) =>
        IsRunning && string.Equals(PlayingPath, filePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Begin, remembering what was there. A start over a running session is a start: the caller
    /// has already given the previous one back, or has decided not to.</summary>
    public AuditionState Start(int zeroBasedPartNo, string toneType, Integra7Snapshot borrowed,
        string playingPath) =>
        new(zeroBasedPartNo, toneType, borrowed, playingPath);

    /// <summary>Play something else in the same part, keeping the memory and the engine. Idle stays idle: a
    /// switch with nothing running would otherwise invent a session holding nothing, and Stop would then
    /// write nothing back over the candidate the instrument is still playing.</summary>
    public AuditionState Switch(string playingPath) =>
        IsRunning ? this with { PlayingPath = playingPath } : this;

    public AuditionState Stop() => Idle;
}
```

- [ ] **Step 4: Green, then the whole suite.** Expected: 7 in the filter, 977 overall.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/AuditionState.cs Tests/TestAuditionState.cs
git commit -m "feat: what an audition borrows from a part"
```

---

### Task 2: `Audition` — the two device operations

**Files:** Create `Src/Models/Services/Audition.cs`; Test `Tests/TestAudition.cs`

- [ ] **Step 1: Write the failing tests**

The fixtures are `internal` members of `StudioSetSnapshotServiceTests` in `Tests/TestStudioSetSnapshot.cs` —
`BuildDomain(api)` and `BlankReplyApi`. **Confirm their names and their counter properties (`Transmissions`,
`Requests`) by reading that file first**; `TestMorphWriter.cs` is a working example of using them.

```csharp
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Borrowing a part and giving it back. The device path, against a fake instrument.</summary>
public class AuditionTests
{
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Common = "Offset2/SuperNATURAL Synth Tone Common";
    private const string ToneLevel = "SuperNATURAL Synth Tone Common/Tone Level";

    private static Integra7Snapshot Candidate(long level) =>
        new(Integra7Snapshot.CurrentFormatVersion, "candidate",
            [new SnapshotDomain("Temporary Tone Part 1", Offset, Common,
                [new SnapshotValue(ToneLevel, $"{level}", level)])],
            SnapshotKinds.Tone, "SN-S");

    /// <summary>Starting reads the part before it writes anything. That read is the whole safety of the
    /// feature -- it is the only copy of what the user had.</summary>
    [Test]
    public async Task Starting_captures_the_part_before_writing_the_candidate()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var borrowed = await Audition.StartAsync(domain, Candidate(64), zeroBasedPartNo: 0, "SN-S", null);

        Assert.That(borrowed, Is.Not.Null);
        Assert.That(api.Requests, Is.GreaterThan(0), "the part was read");
        Assert.That(api.Transmissions, Is.GreaterThan(0), "and the candidate was written");
    }

    [Test]
    public async Task The_candidate_reaches_the_part()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        await Audition.StartAsync(domain, Candidate(64), zeroBasedPartNo: 0, "SN-S", null);

        var block = domain.GetDomain("Temporary Tone Part 1", Offset, Common);
        Assert.That(block.LookupSingleParameterDisplayedValue(ToneLevel), Is.EqualTo("64"));
    }

    /// <summary>And stopping puts back exactly what was captured, not something rebuilt from it.</summary>
    [Test]
    public async Task Stopping_writes_back_what_was_captured()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var borrowed = await Audition.StartAsync(domain, Candidate(64), 0, "SN-S", null);
        await Audition.StopAsync(domain, borrowed, 0, "SN-S", null);

        var block = domain.GetDomain("Temporary Tone Part 1", Offset, Common);
        Assert.That(block.LookupSingleParameterDisplayedValue(ToneLevel), Is.EqualTo("0"),
            "the blank instrument answered zeros, so that is what has to come back");
    }

    /// <summary>The engine guard is the restore path's, not this class's -- but a candidate of the wrong
    /// engine must be refused before the part is read, or the user pays for a capture that cannot be
    /// used.</summary>
    [Test]
    public void A_candidate_of_another_engine_is_refused_before_anything_is_read()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        Assert.That(async () => await Audition.StartAsync(domain, Candidate(64), 0, "PCMS", null),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(api.Requests, Is.Zero);
    }
}
```

- [ ] **Step 2: Run and watch it fail.**

- [ ] **Step 3: Implement**

```csharp
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Borrowing a part to hear something else in it, and giving it back.
///
/// <b>Two operations, and the first one reads.</b> Unlike a morph -- which never reads, because a blend
/// covers every parameter by construction -- an audition's whole safety is the capture it takes before it
/// writes anything. That capture is the only copy of the sound the user had, and it is why this cannot be
/// made faster by skipping the read.
///
/// <b>Same engine only.</b> A tone can only be written into a part whose temporary tone is already that
/// engine, and making the other case work costs a preset change and a full part reload each way -- see the
/// design document, which records why that is a later phase. The guard runs before the capture, so a
/// refusal costs nothing.</summary>
public static class Audition
{
    /// <summary>Capture what the part holds, then write <paramref name="candidate"/> over it. Answers the
    /// capture, which the caller must hold until it stops.</summary>
    public static async Task<Integra7Snapshot> StartAsync(Integra7Domain domain, Integra7Snapshot candidate,
        int zeroBasedPartNo, string currentToneType, IMidiLease? lease)
    {
        // Before the read, so a candidate that could never have been written does not cost a capture.
        StudioSetSnapshotService.EnsureToneFitsPart(candidate, zeroBasedPartNo, currentToneType);

        var borrowed = await StudioSetSnapshotService.CaptureToneAsync(domain, zeroBasedPartNo,
            currentToneType, "borrowed by audition", lease!);

        await StudioSetSnapshotService.RestoreToneAsync(domain, candidate, zeroBasedPartNo,
            currentToneType, lease!);

        return borrowed;
    }

    /// <summary>Write back what <see cref="StartAsync"/> captured. Throwing leaves the caller holding the
    /// capture, which is what lets Stop be pressed again.</summary>
    public static Task StopAsync(Integra7Domain domain, Integra7Snapshot borrowed, int zeroBasedPartNo,
        string currentToneType, IMidiLease? lease) =>
        StudioSetSnapshotService.RestoreToneAsync(domain, borrowed, zeroBasedPartNo, currentToneType, lease!);
}
```

**If `CaptureToneAsync` or `RestoreToneAsync` will not take a null lease**, do not force it — change the two
parameters to `IMidiLease` and have the tests pass whatever `TestMorphWriter.cs` passes. Report which you
did.

- [ ] **Step 4: Green, then the whole suite.** Expected: 981 overall.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/Audition.cs Tests/TestAudition.cs
git commit -m "feat: borrow a part to hear a library tone, and give it back"
```

---

### Task 3: the button and the session

**Files:** Modify `Src/ViewModels/LibraryEditorViewModel.cs`, `Src/Views/LibraryEditorView.axaml`,
`Src/ViewModels/LibraryViewModel.cs`, `Src/ViewModels/MainWindowViewModel.cs`

No tests: every file here is a view model. Verification is the build, the unchanged suite, and task 4.

- [ ] **Step 1: The button**

`LibraryEditorViewModel` gains a seventh callback, `Func<LibraryEntryViewModel, Task> audition`, a
`[Reactive] private bool _isAuditioning`, and:

```csharp
    /// <summary>What the audition button says. One button rather than two, because Stop is only ever
    /// wanted for the session this same panel started.</summary>
    public string AuditionLabel => IsAuditioning ? "Stop auditioning" : "Audition";

    /// <summary>Only for a tone. A Studio Set replaces all sixteen parts, which is not something to do to
    /// somebody who wanted to hear a patch.</summary>
    public bool CanAudition => SelectedIsTone;

    public async Task AuditionAsync()
    {
        UserActionLog.Action(IsAuditioning
            ? "button: Stop auditioning (library)"
            : "button: Audition (library)");
        if (Selected is { } row) await _audition(row);
    }
```

Raise `AuditionLabel` alongside the other flags when `IsAuditioning` changes, and `CanAudition` with the
rest.

- [ ] **Step 2: The markup**

In `Src/Views/LibraryEditorView.axaml`, immediately after the "Load into the instrument" button:

```xml
                <!-- Beside Load, because it is the same act made temporary: the part is borrowed rather
                     than replaced. No ToolTip -- it is pressed repeatedly while browsing, and a tooltip is
                     a popup that swallows the click on the control it describes. -->
                <Button Content="{Binding AuditionLabel}"
                        Command="{Binding AuditionAsync}"
                        IsEnabled="{Binding CanAudition}"
                        HorizontalAlignment="Stretch"
                        HorizontalContentAlignment="Center" />
```

- [ ] **Step 3: The session in `MainWindowViewModel`**

Hold one state and the part it belongs to:

```csharp
    /// <summary>The audition in progress, or <see cref="AuditionState.Idle"/>. One at a time and one part
    /// at a time: a second borrowed part would be a second sound to give back, and nothing on screen would
    /// say which.</summary>
    private AuditionState _audition = AuditionState.Idle;
```

The callback the library gets — start, switch, or stop, depending on what is running:

```csharp
    /// <summary>Hear this snapshot in the selected part, or stop hearing it.
    ///
    /// <b>The part is resolved once, at the start</b>, and every later step uses the part the session
    /// remembers rather than whatever is selected now -- a user who changes tab mid-audition must still get
    /// back the part that was borrowed.</summary>
    private async Task AuditionAsync(LibraryEntryViewModel row)
    {
        // Pressing the button on the row that is playing means stop; on any other row it means play that
        // one instead. By path, not by name: two library files can hold tones of the same name.
        if (_audition.IsPlaying(row.FilePath))
        {
            await StopAuditionAsync();
            return;
        }

        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null)
        {
            SnapshotStatus = "Connect to your Integra-7 to audition a tone.";
            SnapshotFailed = true;
            return;
        }

        if (RefuseWhileComparing("audition")) return;

        Integra7Snapshot candidate;
        try
        {
            candidate = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(row.FilePath));
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"read '{row.FilePath}' to audition it", e.ToString());
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not read that file: {e.Message}";
            SnapshotFailed = true;
            return;
        }

        // A session already running keeps its own part **and its own engine**. Taking the engine from the
        // new candidate instead would make every candidate match itself, and RestoreToneAsync's guard --
        // which compares the snapshot's engine against the one it is told the part holds -- would pass for
        // a tone that cannot legally be written there at all.
        int part;
        string toneType;
        if (_audition.IsRunning)
        {
            part = _audition.ZeroBasedPartNo;
            toneType = _audition.ToneType;
        }
        else
        {
            if (await ResolveSelectedToneAsync("audition") is not { } selected) return;
            part = selected.ZeroBasedPartNo;
            toneType = selected.ToneType;
        }

        try
        {
            await using var lease = await api.BeginConversationAsync("audition");
            if (_audition.IsRunning)
            {
                await StudioSetSnapshotService.RestoreToneAsync(communicator, candidate, part, toneType, lease);
                _audition = _audition.Switch(row.FilePath);
            }
            else
            {
                // Cleared once, for the reason LoadToneAsync clears it: the steps in it name parameters of
                // a tone that is no longer loaded. Not restored at the end -- see the design document,
                // which states that as a limitation rather than hiding it.
                EditJournal.Default.Clear();
                var borrowed = await Audition.StartAsync(communicator, candidate, part, toneType, lease);
                _audition = _audition.Start(part, toneType, borrowed, row.FilePath);
            }

            RefreshAuditionButton();
            SnapshotStatus = $"Auditioning {row.Name} in part {part + 1}. Press Stop to put the part back.";
            SnapshotFailed = false;
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"audition '{row.FilePath}'", e.ToString());
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not audition that: {e.Message}";
            SnapshotFailed = true;
        }
    }

    /// <summary>Give the borrowed part back. Safe to call when nothing is running, which is what lets every
    /// trigger call it without asking first.
    ///
    /// <b>A failure keeps the memory</b>, so Stop can be pressed again: the instrument is still holding the
    /// candidate, and forgetting the capture would strand it there.</summary>
    private async Task StopAuditionAsync()
    {
        if (_audition is not { IsRunning: true, Borrowed: { } borrowed }) return;

        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null)
        {
            SnapshotStatus = "The instrument is not connected, so the part cannot be put back yet.";
            SnapshotFailed = true;
            return;
        }

        try
        {
            await using var lease = await api.BeginConversationAsync("audition");
            await Audition.StopAsync(communicator, borrowed, _audition.ZeroBasedPartNo,
                _audition.ToneType, lease);

            _audition = _audition.Stop();
            RefreshAuditionButton();
            SnapshotStatus = "Put the part back as it was.";
            SnapshotFailed = false;
        }
        catch (Exception e)
        {
            UserActionLog.Failed("stop an audition", e.ToString());
            SnapshotStatus = $"Could not put the part back: {e.Message} Press Stop again to retry.";
            SnapshotFailed = true;
        }
    }
```

And the one place that decides what the button says:

```csharp
    /// <summary>Tell the panel whether the row it is showing is the one being heard.
    ///
    /// <b>Per row, not per session.</b> While something is playing, its own row offers Stop and every other
    /// row offers Audition -- so selecting a different tone and pressing the button plays that one instead
    /// of stopping, which is what browsing is. Called whenever the session changes and whenever the
    /// selection does.</summary>
    private void RefreshAuditionButton() =>
        LibraryVm.Editor.IsAuditioning =
            LibraryVm.Editor.Selected is { } row && _audition.IsPlaying(row.FilePath);
```

Call it from the selection subscription in `LibraryViewModel` as well — the simplest way is to give the
library a callback it invokes after `Editor.Selected` is assigned, and to point that at this method.

- [ ] **Step 4: The triggers that end a session**

Three, all calling `StopAuditionAsync`, which is harmless when nothing is running:

```csharp
        // Leaving the Library tab. Auditioning is a thing done while browsing; carrying a borrowed part to
        // another screen would leave a sound the user can no longer see the Stop button for.
        this.WhenAnyValue(x => x.TopTabIndex)
            .Subscribe(async index =>
            {
                if (index != LibraryTabIndex) await StopAuditionAsync();
            });
```

`LibraryTabIndex` does not exist yet — add it beside `CompareTabIndex` and `MorphPadTabIndex`, and
**check every `TopTabIndex = ` assignment still points at the tab it means**:
`grep -n "TopTabIndex = " Src/ViewModels/*.cs`.

And in `LoadFromLibraryAsync` and `LoadToneAsync`, before either loads anything:

```csharp
        // A real load replaces the part for good, so the borrowed sound has nowhere to go back to.
        await StopAuditionAsync();
```

- [ ] **Step 5: Pass the callback through `LibraryViewModel`**

Add a seventh constructor parameter `Func<LibraryEntry, Task> audition`, store it, and hand
`AuditionRowAsync` to the editor:

```csharp
    private Task AuditionRowAsync(LibraryEntryViewModel row) => _audition(row.Entry);
```

- [ ] **Step 6: Build and run the whole suite.** Expected: build succeeds, 981 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/LibraryEditorViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/ViewModels/MainWindowViewModel.cs Src/Views/LibraryEditorView.axaml
git commit -m "feat: audition a library tone in the selected part"
```

---

### Task 4: verify what can be verified without hearing it

**Files:** none.

Everything about *sound* is the user's to check. What can be driven is that the right SysEx goes out and the
right thing comes back.

- [ ] **Step 1: Drive it**

Use the harness pattern from phases 1 and 2: point the library folder at a throwaway directory by writing
the settings file and restoring it in a `finally`; select rows through UI Automation's `SelectionItemPattern`
rather than synthetic mouse clicks, which lose gestures. **Never point a check at the user's own library.**

- [ ] **Step 2: Walk the checks**

With the instrument connected and a part selected on the Parameters tab:

1. The Audition button is disabled for a Studio Set and enabled for a tone.
2. Auditioning a tone of the part's own engine: the log shows a capture (reads) followed by writes.
3. The button becomes "Stop auditioning", and the status line names the tone and the part.
4. Choosing a second tone while running: writes, and **no second capture** in the log.
5. Stop: writes, the button goes back to "Audition".
6. Auditioning a tone of a different engine: refused with the engine message, and **no reads** in the log.
7. Switching to another tab while auditioning stops it.

- [ ] **Step 3: Report** what was seen for each, with the log lines that show the read/write pattern.

---

## Verification by hand (user, with the instrument)

- [ ] Auditioning a tone plays it in the selected part.
- [ ] Stop puts the part back to exactly the sound it had, including any unsaved edits it was holding.
- [ ] Browsing several tones in a row and then stopping still gives back the original sound, not the
  second-to-last one.
- [ ] Leaving the Library tab puts the part back.
- [ ] **Known and deliberate:** the undo history is cleared when an audition starts and is not restored.
- [ ] **Known and deliberate:** if the application is killed mid-audition the part keeps the candidate;
  re-selecting a preset on that part fixes it.
