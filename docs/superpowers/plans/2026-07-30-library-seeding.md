# Seeding the Library from the Instrument — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Sweep the instrument — each chosen preset and each non-`INIT` user slot — selecting it on one part,
capturing its temporary tone and writing it into the snapshot library, so the library's search, compare,
duplicate and morph features apply to every sound the instrument can make.

**Architecture:** A pure planner turns the preset table plus a selection plus the files already on disk into
an ordered work list grouped by SRX board loadout. A runner walks that list behind an interface, so failure
isolation, cancellation and restore are testable against a fake instrument. One adapter implements that
interface over the real device. The view model sequences and shows progress; it holds no rules.

**Tech Stack:** .NET 10, C# 13, NUnit 4, Avalonia 12, ReactiveUI 24. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-07-30-library-seeding-design.md`. Read it first — every number in it
was measured against the user's own instrument on 2026-07-30 and the design is shaped by those measurements
rather than by expectation.

---

## What the spike established, in one screen

Do not re-derive these. They were measured over ~1,600 selections and ~1,000 captures.

- **There is nothing to poll.** The device withholds the read reply until the tone has loaded. Forty captures
  started with **zero delay** after the bank/program writes were byte-identical to captures taken 1.5 s later,
  on all five engines. `CaptureToneAsync` **is** the settle check.
- **Never settle by name.** `Presets.csv` disagreed with the device for 102 of 5,227 rows; 97 have since been
  corrected, 3 are deliberate, and 2 more sit in banks that expose nothing. Names are labels, not signals.
- **Reads do not flake.** 0 unanswered in ~17,000 requests against a loaded engine's area, and 0 in the
  5,444-request name audit. Every silence observed was a patch that is genuinely unavailable, and those are
  deterministic — retried three times, they fail three times.
- **Cost per patch:** SN-A 116 ms, SN-S 186 ms, PCMS 376 ms, SN-D 1,380 ms, **PCMD 6,018 ms**. Sustained
  2.07 patches/s. A full factory sweep is ~54 minutes; PCM drum kits are 3.6% of the presets, 40% of the
  clock and 137 MB of the ~320 MB written.
- **796 factory rows cannot be captured on this unit** — every GM2 (265) and ExPCM (531) row. The Studio Set
  Part stores the bank and program, then all five engines' temporary areas stay silent. `HQ GM2 + HQ Pcm`
  does not unlock them; that was tested with positive controls in the same loadout.
- **The program parameter is 0-based; the table is 1-based.** Write `Pc - 1`.
- **Write through the domain, not `PartViewModel.ChangePresetAsync`**, which posts `UpdateResyncPart` to the
  message bus and would resync the part once per patch. Domain writes produce no bus traffic.
- **The three writes are three DT1s.** Hold one lease across them, or an abort leaves a mixed bank.
- **SRX:** `GetLoadedSrxAsync` converging on the expected set is the completion signal. It returns
  `(0,0,0,0)` mid-load, and **the device rewrites what you send** — `SendLoadSrxAsync(19,0,0,0)` reads back
  `(19,20,21,22)`. **Compare against what it settles on, never against what you sent.** A normal load
  converges in ~23 s; restoring three boards ~14.6 s.

---

## Conventions for every task

**Build and test with the user-local SDK** — the system `dotnet` is 8/9. `Src/bin` is routinely locked by the
user's own running application or Rider's previewer; **never kill either**, redirect instead. The four-deep
path and the junction are load-bearing, because several tests find `Src\Assets\parameters.bin` by walking
`..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` goes **before** `-p:OutputPath`. Baseline: **1135 passed, 0 failed**, 134 warnings, 0 errors.

**Traps this project has actually hit:**

- **An XML comment may not contain `--`** (`MSB4025` makes the project fail to *load*, and the error count
  then reads zero), and a comment may not sit between an element's attributes. Prose uses real em dashes.
- **Never hardcode a colour in XAML** — `{StaticResource ...}` only.
- **A `ToolTip` is a popup and swallows clicks on its own control** — never on a repeatedly-clicked button.
- **Do not edit `.axaml` or source through PowerShell** — files are CRLF with a BOM and PowerShell 5.1's
  `Set-Content` defaults to ANSI. Use Edit/Write. (`Presets.csv` is CRLF **without** a BOM.)
- Compiled bindings are checked at build time; `AVLN2000` means a binding names a member that does not exist.
- **A view model cannot be constructed in a test** under ReactiveUI 24. That is why the rules go in services.
- **A commit message containing double quotes cannot be passed through PowerShell to `git commit -m`** — use
  `git commit -F` with a message file in the scratchpad.

**House style:** comments say *why*, not *what*, and are discursive — `Src/Models/Services/DeepSearch.cs`,
`PatchListSource.cs` and `DuplicateGroups.cs` are the register.

**Git:** branch `feature/library-seeding`, which already holds the spec and the preset-name corrections.
Explicit paths only; never `git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do
not merge or push. Every commit message ends with:

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## File structure

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/SeedSelection.cs` | What to sweep: engines, banks, internal/user, the part. A record. |
| `Src/Models/Services/SeedBoards.cs` | Which SRX/ExSN board a bank needs, and how to group banks into loadouts of four. The only place a bank name meets a board number. |
| `Src/Models/Services/SeedPlan.cs` | Presets + selection + files on disk + loaded boards → ordered rounds, skip reasons, estimate. Pure. |
| `Src/Models/Services/SeedRun.cs` | The loop: restore-in-`finally`, per-patch outcomes, cancellation. Behind `ISeedInstrument`. |
| `Src/Models/Services/SeedInstrument.cs` | `ISeedInstrument` over the real device. Thin, untested by design. |
| `Src/ViewModels/SeedRunViewModel.cs` + `Src/Views/SeedRunView.axaml` (+`.axaml.cs`) | The selection screen and progress. No rules. |
| `Tests/TestSeedBoards.cs`, `TestSeedPlan.cs`, `TestSeedRun.cs` | |

Modified: `Src/ViewModels/LibraryViewModel.cs` and `Src/Views/LibraryView.axaml` (the button),
`Src/ViewModels/MainWindowViewModel.cs` (the callback and the interaction).

---

### Task 1: which board a bank needs

**Files:** Create `Src/Models/Services/SeedBoards.cs`, `Tests/TestSeedBoards.cs`

`Integra7SysexHelpers.SrxIdForLoad` already names the values: `Srx01`–`Srx12` are 1–12, `ExSN1`–`ExSN6` are
13–18, `HQPcm` is 19, `Off` is 0. The bank strings in `Presets.csv` are `SRX01`…`SRX12` and `ExSN1`…`ExSN6`,
so the mapping is by name — but it is written down once, here, rather than parsed at four call sites.

- [ ] **Step 1: Write the failing tests**

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which expansion board a bank lives on, and how few loadouts cover a set of banks.</summary>
public class SeedBoardsTests
{
    [Test]
    public void A_bank_on_no_board_needs_none()
    {
        Assert.That(SeedBoards.For("PRST"), Is.Null);
        Assert.That(SeedBoards.For("GM2/GM2#"), Is.Null);
    }

    [Test]
    public void An_srx_bank_names_its_board()
    {
        Assert.That(SeedBoards.For("SRX01"), Is.EqualTo(1));
        Assert.That(SeedBoards.For("SRX12"), Is.EqualTo(12));
    }

    /// <summary>The ExSN boards continue the same numbering, which is the instrument's, not ours.</summary>
    [Test]
    public void An_exsn_bank_names_its_board()
    {
        Assert.That(SeedBoards.For("ExSN1"), Is.EqualTo(13));
        Assert.That(SeedBoards.For("ExSN6"), Is.EqualTo(18));
    }

    /// <summary>Four slots, so four boards per loadout and no more.</summary>
    [Test]
    public void Boards_are_grouped_four_at_a_time()
    {
        var rounds = SeedBoards.Loadouts([1, 2, 3, 4, 5]);

        Assert.That(rounds, Has.Count.EqualTo(2));
        Assert.That(rounds[0], Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(rounds[1], Is.EqualTo(new[] { 5, 0, 0, 0 }));
    }

    /// <summary>A loadout is always four values, padded with Off, because that is what the device is sent.
    /// </summary>
    [Test]
    public void A_short_loadout_is_padded_with_off()
    {
        Assert.That(SeedBoards.Loadouts([7]), Is.EqualTo(new[] { new[] { 7, 0, 0, 0 } }));
    }

    [Test]
    public void No_boards_is_no_loadouts()
    {
        Assert.That(SeedBoards.Loadouts([]), Is.Empty);
    }

    /// <summary>Ordered, so that two plans over the same banks load the boards in the same order and a run
    /// that was interrupted resumes into the same rounds rather than reloading boards it already used.
    /// </summary>
    [Test]
    public void Loadouts_are_in_board_order_whatever_order_the_banks_came_in()
    {
        Assert.That(SeedBoards.Loadouts([9, 2, 5, 1]), Is.EqualTo(new[] { new[] { 1, 2, 5, 9 } }));
    }
}
```

- [ ] **Step 2: Run them and watch them fail.** Expected: the name `SeedBoards` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which expansion board a preset bank lives on, and how to cover a set of them in as few loadouts
/// as the instrument's four slots allow.
///
/// <b>One place where a bank name meets a board number.</b> The mapping is by name and looks trivial --
/// "SRX07" is board 7 -- which is exactly why it would otherwise be written inline at every call site and
/// then disagree with itself once. <see cref="Integra7SysexHelpers.SrxIdForLoad"/> is the authority for the
/// numbers; this is the authority for which bank asks for which.</summary>
public static class SeedBoards
{
    /// <summary>The board a bank needs, or null when it needs none. PRST and GM2 are in the instrument
    /// itself; ExPCM is a bank the unit exposes no temporary tone for at all (see the spec), and it needs no
    /// board either.</summary>
    public static int? For(string bank) => bank switch
    {
        _ when bank.StartsWith("SRX", StringComparison.Ordinal)
               && int.TryParse(bank.AsSpan(3), out var srx) && srx is >= 1 and <= 12 => srx,
        _ when bank.StartsWith("ExSN", StringComparison.Ordinal)
               && int.TryParse(bank.AsSpan(4), out var exsn) && exsn is >= 1 and <= 6 => 12 + exsn,
        _ => null,
    };

    /// <summary>The loadouts that cover <paramref name="boards"/>, four slots at a time.
    ///
    /// <b>Ordered, and padded to four.</b> Ordered so that two plans over one selection load the boards in
    /// the same sequence -- a sweep resumed after an interruption then walks the same rounds and does not
    /// reload a board it has already finished with, which is 23 seconds each time. Padded because four
    /// values is what <c>SendLoadSrxAsync</c> takes, and a slot left unnamed is not the same as a slot set
    /// to Off.</summary>
    public static IReadOnlyList<int[]> Loadouts(IEnumerable<int> boards) =>
    [
        .. boards.Distinct().OrderBy(board => board)
            .Chunk(4)
            .Select(round => round.Concat(Enumerable.Repeat(0, 4 - round.Length)).ToArray()),
    ];
}
```

- [ ] **Step 4: Green, then the whole suite.** Expected 1135 + 7 = **1142**.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SeedBoards.cs Tests/TestSeedBoards.cs
git commit -m "feat: say which expansion board a preset bank needs"
```

---

### Task 2: the plan

**Files:** Create `Src/Models/Services/SeedSelection.cs`, `Src/Models/Services/SeedPlan.cs`,
`Tests/TestSeedPlan.cs`

This is where every rule that can be got wrong quietly lives. It opens no file and touches no device.

- [ ] **Step 1: The records**

`SeedSelection.cs`:

```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What to sweep.
///
/// <b>Engines and banks are sets rather than flags</b>, because the screen is a list of tick boxes over the
/// vocabulary the preset table actually uses and a bool per engine would have to be extended by hand every
/// time the table gains one. Empty means "none selected", not "all": a sweep is an hour of the user's
/// instrument and it starts from nothing ticked being nothing swept.</summary>
/// <param name="Engines">Tone types to include -- "SN-A", "SN-S", "PCMS", "PCMD", "SN-D".</param>
/// <param name="Banks">Bank strings as the table spells them -- "PRST", "SRX07", "ExSN1", "GM2/GM2#".</param>
/// <param name="IncludeInternal">Factory presets.</param>
/// <param name="IncludeUser">The instrument's user slots.</param>
/// <param name="ZeroBasedPartNo">The part the sweep borrows. Its tone is overwritten once per patch and the
/// Studio Set is restored at the end, so which part it is matters only to what the user hears while it
/// runs.</param>
public sealed record SeedSelection(
    IReadOnlyCollection<string> Engines,
    IReadOnlyCollection<string> Banks,
    bool IncludeInternal = true,
    bool IncludeUser = true,
    int ZeroBasedPartNo = 0);
```

`SeedPlan.cs` holds the result records and the builder:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Why a preset is not in the work list.</summary>
public enum SeedSkip
{
    /// <summary>Its engine, bank or internal/user side was not ticked.</summary>
    NotSelected,

    /// <summary>A file of that name is already in the library folder. This is what makes an interrupted
    /// sweep resumable at no cost.</summary>
    AlreadyInLibrary,

    /// <summary>An untouched user slot. The instrument names them "INIT TONE", "INIT KIT" and the like, and
    /// capturing 900 copies of the same empty patch is the single largest waste a sweep can commit.</summary>
    EmptySlot,
}

/// <param name="Preset">The row this came from.</param>
/// <param name="FileName">What the file will be called, without a folder. See
/// <see cref="SeedPlan.FileNameFor"/> -- the catalogue name and the address, because the resume compares
/// this against the folder before anything is captured and neither the device's name nor a collision
/// suffix is knowable that early.</param>
/// <param name="Metadata">The annotations to write with it. Built here rather than at the write, so that
/// what a swept snapshot carries is decided in one tested place -- the tag is what makes "only my own
/// patches" a filter afterwards, and a sweep that forgot it would need 6,000 files re-annotated.</param>
public sealed record SeedItem(Integra7Preset Preset, string FileName, SnapshotMetadata Metadata);

/// <param name="Boards">The four slot values to send, or null when the round needs no board change.</param>
public sealed record SeedRound(int[]? Boards, IReadOnlyList<SeedItem> Items);

/// <param name="Rounds">The work, grouped so the boards are loaded as few times as possible.</param>
/// <param name="Skipped">Every preset left out, with its reason. Carried rather than counted so the screen
/// can say "412 already in your library" rather than "412 skipped", which is a different sentence.</param>
/// <param name="Estimate">How long the run should take, from the per-engine costs measured on 2026-07-30
/// plus the board loads.</param>
public sealed record SeedWork(
    IReadOnlyList<SeedRound> Rounds,
    IReadOnlyList<(Integra7Preset Preset, SeedSkip Why)> Skipped,
    TimeSpan Estimate)
{
    public int Count => Rounds.Sum(round => round.Items.Count);
}
```

- [ ] **Step 2: The failing tests**

`Integra7Preset`'s constructor validates its strings, so fixtures must use real vocabulary — a tone type of
`SN-A`/`SN-S`/`SN-D`/`PCMS`/`PCMD`, a bank the table uses, `INT` or `USR`, and a category from
`Integra7Preset.ToneCategories`. Anything else throws `MidiException`, which is a fixture bug that reads like
a product bug.

```csharp
using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Turning a selection into an ordered list of work.</summary>
public class SeedPlanTests
{
    private static Integra7Preset Preset(string name, string type = "SN-A", string bank = "PRST",
        string usage = "INT", int pc = 1) =>
        new(0, usage, type, bank, pc, name, 89, 64, pc, "Ac.Piano");

    private static SeedSelection Everything(params string[] banks) =>
        new(["SN-A", "SN-S", "PCMS", "PCMD", "SN-D"], banks.Length == 0 ? ["PRST"] : banks);

    [Test]
    public void A_selected_preset_becomes_one_item()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
        Assert.That(work.Rounds[0].Items[0].FileName, Is.EqualTo("Full Grand 1 [89-64-1].json"));
    }

    /// <summary>The address is in the file name because the name alone is not unique and the library will
    /// not overwrite: 405 of the 6,022 catalogue rows share a name with another row -- three Harps, three
    /// Shakuhachis, three Snare Menu 1s -- and <c>SnapshotLibrary.Create</c> answers a collision with
    /// " (2)". A sweep that let it would write ~208 files under names its own planner never predicts, so
    /// every re-run would capture them again and the folder would grow by 208 files each time while the
    /// resume looked like it was working. Unique by construction is the only version of this that stays
    /// true after the second run.</summary>
    [Test]
    public void Two_presets_with_one_name_get_two_file_names()
    {
        var work = SeedPlan.Build(
            [Preset("Harp", bank: "PRST", pc: 12), Preset("Harp", bank: "SRX07", pc: 40)],
            Everything("PRST", "SRX07"), [], []);

        var names = work.Rounds.SelectMany(round => round.Items).Select(item => item.FileName).ToList();
        Assert.That(names, Is.Unique);
    }

    [Test]
    public void An_engine_that_was_not_ticked_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Pad", type: "SN-S")],
            new SeedSelection(["SN-A"], ["PRST"]), [], []);

        Assert.That(work.Count, Is.EqualTo(0));
        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.NotSelected));
    }

    [Test]
    public void A_bank_that_was_not_ticked_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Pad", bank: "SRX07")], Everything("PRST"), [], []);

        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.NotSelected));
    }

    /// <summary>The resume, and it costs nothing: a file already in the folder is not read, not compared,
    /// just not swept again. Matched on the file name because that is what the sweep would write and what
    /// the folder can be asked for cheaply -- the alternative, opening every snapshot to compare its
    /// address, is a folder read to save a folder read.</summary>
    [Test]
    public void A_preset_already_in_the_library_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(),
            ["Full Grand 1 [89-64-1].json"], []);

        Assert.That(work.Count, Is.EqualTo(0));
        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.AlreadyInLibrary));
    }

    /// <summary>Case-insensitively, because the folder is on Windows and "full grand 1.json" is the same
    /// file. A sweep that captured it again would write a second file the folder cannot hold.</summary>
    [Test]
    public void An_existing_file_matches_whatever_its_case()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(),
            ["FULL GRAND 1 [89-64-1].JSON"], []);

        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.AlreadyInLibrary));
    }

    /// <summary>An untouched user slot. The instrument names them "INIT TONE", "INIT KIT" and so on, and
    /// there are up to 1,120 slots -- so this is the difference between a sweep of the user's own sounds and
    /// a sweep of nine hundred copies of the same empty patch.</summary>
    [Test]
    public void An_empty_user_slot_is_skipped()
    {
        var work = SeedPlan.Build([Preset("INIT TONE", usage: "USR"), Preset("INIT KIT", usage: "USR"),
            Preset("Mine", usage: "USR")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
        Assert.That(work.Rounds[0].Items[0].Preset.Name, Is.EqualTo("Mine"));
        Assert.That(work.Skipped.Select(s => s.Why),
            Is.EqualTo(new[] { SeedSkip.EmptySlot, SeedSkip.EmptySlot }));
    }

    /// <summary>Only a user slot. A factory preset legitimately called "Init Tone" is a sound somebody
    /// designed, and the instrument ships one -- dropping it because of its name would be this feature
    /// deciding it knows better than the tone list.</summary>
    [Test]
    public void A_factory_preset_named_init_is_not_an_empty_slot()
    {
        var work = SeedPlan.Build([Preset("INIT TONE")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
    }

    [Test]
    public void The_two_sides_can_be_asked_for_separately()
    {
        Integra7Preset[] presets = [Preset("Factory"), Preset("Mine", usage: "USR")];

        var userOnly = SeedPlan.Build(presets,
            new SeedSelection(["SN-A"], ["PRST"], IncludeInternal: false), [], []);
        var factoryOnly = SeedPlan.Build(presets,
            new SeedSelection(["SN-A"], ["PRST"], IncludeUser: false), [], []);

        Assert.That(userOnly.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Mine"));
        Assert.That(factoryOnly.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Factory"));
    }

    /// <summary>Presets needing no board come first and in one round, so a sweep starts producing files
    /// immediately instead of spending 23 seconds loading a board before the first capture.</summary>
    [Test]
    public void The_boardless_presets_are_one_round_and_come_first()
    {
        var work = SeedPlan.Build(
            [Preset("On a board", bank: "SRX07"), Preset("Built in", bank: "PRST")],
            Everything("PRST", "SRX07"), [], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(2));
        Assert.That(work.Rounds[0].Boards, Is.Null);
        Assert.That(work.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Built in"));
        Assert.That(work.Rounds[1].Boards, Is.EqualTo(new[] { 7, 0, 0, 0 }));
    }

    /// <summary>Four boards to a round, because the instrument has four slots -- so eight selected boards
    /// are two loads, not eight.</summary>
    [Test]
    public void Up_to_four_boards_share_a_round()
    {
        var presets = new[] { "SRX01", "SRX02", "SRX03", "SRX04", "SRX05" }
            .Select(bank => Preset($"On {bank}", bank: bank)).ToArray();

        var work = SeedPlan.Build(presets, Everything("SRX01", "SRX02", "SRX03", "SRX04", "SRX05"), [], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(2));
        Assert.That(work.Rounds[0].Items, Has.Count.EqualTo(4));
        Assert.That(work.Rounds[1].Items, Has.Count.EqualTo(1));
    }

    /// <summary>A round whose every patch is already in the library is not a round: loading four boards to
    /// capture nothing is 23 seconds spent on an empty answer, and an interrupted sweep resumed near its end
    /// would otherwise spend minutes reloading boards before reaching the work that is left.</summary>
    [Test]
    public void A_round_with_nothing_left_to_do_is_dropped()
    {
        var work = SeedPlan.Build(
            [Preset("Built in"), Preset("On a board", bank: "SRX07")],
            Everything("PRST", "SRX07"), ["On a board [89-64-1].json"], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(1));
        Assert.That(work.Rounds[0].Boards, Is.Null);
    }

    /// <summary>The boards already loaded do not need loading again, which is the difference between a
    /// one-board sweep that starts now and one that starts in 23 seconds.</summary>
    [Test]
    public void A_board_that_is_already_loaded_costs_no_round_of_its_own()
    {
        var work = SeedPlan.Build([Preset("On a board", bank: "SRX07")],
            Everything("SRX07"), [], [7, 0, 0, 0]);

        Assert.That(work.Rounds, Has.Count.EqualTo(1));
        Assert.That(work.Rounds[0].Boards, Is.Null);
    }

    /// <summary>The estimate is built from times measured on the instrument, so a drum kit counts for what
    /// it costs -- 6 s against 116 ms for an SN-A tone. An estimate that averaged them would promise ten
    /// minutes for a sweep that takes an hour.</summary>
    [Test]
    public void The_estimate_charges_each_engine_what_it_measured()
    {
        var synth = SeedPlan.Build([Preset("Tone", type: "SN-A")], Everything(), [], []);
        var kit = SeedPlan.Build([Preset("Kit", type: "PCMD")], Everything(), [], []);

        Assert.That(kit.Estimate, Is.GreaterThan(synth.Estimate * 10));
    }

    /// <summary>Loading boards is most of a small sweep's time and none of its captures, so it is in the
    /// estimate. Two rounds of one board each cost two loads.</summary>
    [Test]
    public void The_estimate_includes_the_board_loads()
    {
        var withoutBoards = SeedPlan.Build([Preset("A")], Everything(), [], []);
        var withBoards = SeedPlan.Build(
            [Preset("A"), Preset("B", bank: "SRX07")], Everything("PRST", "SRX07"), [], []);

        Assert.That(withBoards.Estimate - withoutBoards.Estimate, Is.GreaterThan(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public void Nothing_selected_is_no_work_and_no_failure()
    {
        var work = SeedPlan.Build([Preset("A")], new SeedSelection([], []), [], []);

        Assert.That(work.Rounds, Is.Empty);
        Assert.That(work.Estimate, Is.EqualTo(TimeSpan.Zero));
    }

    /// <summary>The category comes from the table, which is the instrument's own vocabulary and the same one
    /// the library's category filter offers -- a sweep that invented its own would put 6,000 snapshots
    /// outside every filter the browser has.</summary>
    [Test]
    public void A_swept_snapshot_carries_the_presets_category()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(), [], []);

        Assert.That(work.Rounds[0].Items[0].Metadata.Category, Is.EqualTo("Ac.Piano"));
    }

    /// <summary>Two tags: where it came from, and which side it came from. The bank tag is how a user finds
    /// the SRX07 sounds again; the factory/user tag is how they find the ones that are theirs among six
    /// thousand that are not, which is the whole reason a sweep is survivable.</summary>
    [Test]
    public void A_swept_snapshot_is_tagged_with_its_bank_and_its_side()
    {
        var factory = SeedPlan.Build([Preset("A", bank: "SRX07")], Everything("SRX07"), [], []);
        var mine = SeedPlan.Build([Preset("B", usage: "USR")], Everything(), [], []);

        Assert.That(factory.Rounds[0].Items[0].Metadata.TagList,
            Is.EquivalentTo(new[] { "SRX07", "factory" }));
        Assert.That(mine.Rounds[0].Items[0].Metadata.TagList, Is.EquivalentTo(new[] { "PRST", "user" }));
    }
}
```

- [ ] **Step 3: Run them and watch them fail.**

- [ ] **Step 4: Implement `SeedPlan.Build`**

```csharp
    /// <summary>The work a selection asks for, in the order it should be done.
    ///
    /// <b>Grouped by board loadout, boardless first.</b> A board load converges in about 23 seconds, so the
    /// grouping is most of what decides whether a small sweep takes one minute or five, and putting the
    /// built-in banks first means files start appearing before the first load rather than after it.
    ///
    /// <b>Skips are carried, not counted.</b> "412 are already in your library" and "412 were skipped" are
    /// different sentences and only one of them tells the user their last run worked.</summary>
    /// <param name="existingFiles">File names, not paths, already in the library folder.</param>
    /// <param name="loadedBoards">What the instrument has loaded right now, so a board already in a slot
    /// costs no round.</param>
    public static SeedWork Build(IReadOnlyList<Integra7Preset> presets, SeedSelection selection,
        IReadOnlyCollection<string> existingFiles, IReadOnlyCollection<int> loadedBoards)
    {
        var have = existingFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engines = selection.Engines.ToHashSet(StringComparer.Ordinal);
        var banks = selection.Banks.ToHashSet(StringComparer.Ordinal);
        var loaded = loadedBoards.Where(board => board != 0).ToHashSet();

        List<(Integra7Preset, SeedSkip)> skipped = [];
        List<(Integra7Preset Preset, SeedItem Item, int? Board)> work = [];

        foreach (var preset in presets)
        {
            var user = preset.InternalUserDefinedStr == "USR";
            if (!engines.Contains(preset.ToneTypeStr) || !banks.Contains(preset.ToneBankStr)
                || (user ? !selection.IncludeUser : !selection.IncludeInternal))
            {
                skipped.Add((preset, SeedSkip.NotSelected));
                continue;
            }

            // Only a user slot: the instrument ships factory tones with Init in the name, and dropping one
            // of those would be this feature overruling the tone list about what is a sound.
            if (user && preset.Name.TrimStart().StartsWith("INIT", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add((preset, SeedSkip.EmptySlot));
                continue;
            }

            var fileName = FileNameFor(preset);
            if (have.Contains(fileName))
            {
                skipped.Add((preset, SeedSkip.AlreadyInLibrary));
                continue;
            }

            // The category is the instrument's own vocabulary and the same list the library's filter offers,
            // so a swept snapshot lands inside the filters rather than outside them. The two tags are where
            // it came from and which side it came from: the second is how a user finds their own patches
            // among six thousand that are not theirs.
            var metadata = new SnapshotMetadata(
                preset.CategoryStr, [preset.ToneBankStr, user ? "user" : "factory"]);

            var board = SeedBoards.For(preset.ToneBankStr);
            work.Add((preset, new SeedItem(preset, fileName, metadata),
                loaded.Contains(board ?? 0) ? null : board));
        }

        // Boardless first and in one round -- including the banks whose board is already in a slot, which
        // is the same thing from the sweep's point of view: nothing to load before capturing.
        List<SeedRound> rounds = [];
        var boardless = work.Where(w => w.Board is null).Select(w => w.Item).ToList();
        if (boardless.Count > 0) rounds.Add(new SeedRound(null, boardless));

        foreach (var loadout in SeedBoards.Loadouts(work.Where(w => w.Board is not null)
                     .Select(w => w.Board!.Value)))
        {
            var items = work.Where(w => w.Board is { } board && loadout.Contains(board))
                .Select(w => w.Item).ToList();
            // Cannot be empty by construction -- every board in a loadout came from an item -- but a round
            // that captured nothing would be 23 seconds spent on an empty answer, so it is asserted by being
            // impossible rather than by comment alone.
            if (items.Count > 0) rounds.Add(new SeedRound(loadout, items));
        }

        return new SeedWork(rounds, skipped, Estimate(rounds));
    }

    /// <summary>Per-engine costs measured against the user's instrument on 2026-07-30, full round trip --
    /// three parameter writes, the selection settling, and the whole capture. A drum kit is fifty times an
    /// SN-A tone because it reads 88 partial blocks whether or not they hold anything, so an average would
    /// promise ten minutes for an hour's work.</summary>
    private static readonly Dictionary<string, int> MillisecondsPerPatch = new(StringComparer.Ordinal)
    {
        ["SN-A"] = 116, ["SN-S"] = 186, ["PCMS"] = 376, ["SN-D"] = 1380, ["PCMD"] = 6018,
    };

    /// <summary>A board loadout converges in about 23 seconds, measured over five of them. It is most of a
    /// small sweep's time and none of its captures.</summary>
    private static readonly TimeSpan PerLoadout = TimeSpan.FromSeconds(23);

    private static TimeSpan Estimate(IReadOnlyList<SeedRound> rounds) =>
        TimeSpan.FromMilliseconds(rounds.Sum(round => round.Items.Sum(item =>
            MillisecondsPerPatch.GetValueOrDefault(item.Preset.ToneTypeStr, 400))))
        + PerLoadout * rounds.Count(round => round.Boards is not null);
```

And the file name itself, in the same class:

```csharp
    /// <summary>What a swept preset's file is called: its catalogue name, then its address.
    ///
    /// <b>The address is there because the name is not unique and the library will not overwrite.</b> 405
    /// of the 6,022 catalogue rows share a name with another row -- three Harps, three Shakuhachis, three
    /// Snare Menu 1s -- and <see cref="SnapshotLibrary.Create"/> answers a collision by suffixing " (2)",
    /// which is right for a user saving a sound by hand and wrong here: the sweep predicts this name before
    /// it captures anything, and a file that landed under a name the planner cannot predict would be
    /// captured again on every re-run, the folder growing by ~208 files each time while the resume looked
    /// like it was working. MSB, LSB and PC together are unique across every row in the table and across
    /// the user slots as well, which are at their own addresses, so a name built from them collides only
    /// with itself.
    ///
    /// <b>Not the device's name</b>, though the snapshot inside will carry it. This is chosen before the
    /// capture, because it is what the resume compares against the folder, and a name only knowable after a
    /// capture cannot decide whether to capture. The library already treats the two as different things.
    /// </summary>
    public static string FileNameFor(Integra7Preset preset) =>
        SnapshotLibrary.FileNameFor($"{preset.Name} [{preset.Msb}-{preset.Lsb}-{preset.Pc}]");
```

`SnapshotLibrary.FileNameFor` is public already, scrubs what a file name may not hold, appends `.json`, and
is the same call `SnapshotLibrary.Create` makes — so the two cannot disagree about what a legal name is.

**`SnapshotLibrary.Create` needs one change, in Task 3 or 4 rather than here:** it derives the file name from
the snapshot's own name, and the sweep must supply its own instead. Give it an optional last parameter,
`string? fileName = null`, null meaning what it means today. Keep the `UniquePath` call — with names unique
by construction the suffix now only fires if the folder changed under a running sweep, and overwriting a file
the user has is worse than one file that gets swept twice.

- [ ] **Step 5: Green, then the whole suite.** Expected 1142 + 18 = **1160**.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/SeedSelection.cs Src/Models/Services/SeedPlan.cs Tests/TestSeedPlan.cs
git commit -m "feat: plan a sweep of the instrument into the library"
```

---

### Task 3: the run

**Files:** Create `Src/Models/Services/SeedRun.cs`, `Tests/TestSeedRun.cs`

- [ ] **Step 1: The interface, in `SeedRun.cs`**

```csharp
/// <summary>What a sweep needs of an instrument, so that the loop above it can be tested without one.
///
/// <b>Every method is one conversation.</b> The three parameter writes and the capture are a single
/// operation from the run's point of view, and they must share one lease -- they are three DT1 messages plus
/// a read, and an abort between them leaves the part on a mixed bank. So the interface exposes the pair
/// rather than the pieces.</summary>
public interface ISeedInstrument
{
    /// <summary>The four slot values the instrument reports right now.</summary>
    Task<int[]> LoadedBoardsAsync();

    /// <summary>Load these four and wait until the instrument settles.
    ///
    /// <b>Settling is not "what was sent".</b> The device rewrites a loadout -- sending (19,0,0,0) reads
    /// back as (19,20,21,22) -- and reports (0,0,0,0) while it works. An implementation polls until the
    /// reported set stops changing, never until it matches the request.</summary>
    Task LoadBoardsAsync(int[] boards, CancellationToken token);

    /// <summary>Select this preset on the part and capture what the part then holds. Null when the
    /// instrument exposed no tone for it -- an unloaded board, or a bank this unit does not answer for
    /// (every GM2 and ExPCM row on the measured unit).</summary>
    Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int zeroBasedPartNo, CancellationToken token);

    /// <summary>Everything the sweep is about to overwrite, so it can be put back.</summary>
    Task<Integra7Snapshot> CaptureStudioSetAsync();

    Task RestoreStudioSetAsync(Integra7Snapshot studioSet);
}
```

- [ ] **Step 2: The outcome record and the failing tests**

```csharp
/// <param name="Written">File paths, in the order they were written.</param>
/// <param name="Unavailable">Presets the instrument exposed no tone for.</param>
/// <param name="Failed">Presets whose capture or write threw, with the message.</param>
/// <param name="Cancelled">Whether the run stopped early because it was asked to.</param>
public sealed record SeedOutcome(
    IReadOnlyList<string> Written,
    IReadOnlyList<Integra7Preset> Unavailable,
    IReadOnlyList<(Integra7Preset Preset, string Why)> Failed,
    bool Cancelled);
```

Tests in `Tests/TestSeedRun.cs`, over a fake:

```csharp
/// <summary>A fake instrument, so the loop's rules -- isolation, restore, cancellation -- are tested
/// without hardware and without waiting an hour.</summary>
private sealed class FakeInstrument : ISeedInstrument
{
    public List<string> Calls { get; } = [];
    public HashSet<string> Silent { get; } = [];      // preset names that expose no tone
    public HashSet<string> Throws { get; } = [];      // preset names whose capture throws
    public int[] Boards { get; set; } = [0, 0, 0, 0];

    public Task<int[]> LoadedBoardsAsync() => Task.FromResult(Boards);

    public Task LoadBoardsAsync(int[] boards, CancellationToken token)
    {
        Calls.Add($"load {string.Join(',', boards)}");
        Boards = boards;
        return Task.CompletedTask;
    }

    public Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int part, CancellationToken token)
    {
        Calls.Add($"capture {item.Preset.Name}");
        if (Throws.Contains(item.Preset.Name)) throw new SnapshotFormatException("no answer");
        return Task.FromResult(Silent.Contains(item.Preset.Name)
            ? null
            : new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, item.Preset.Name, [],
                SnapshotKinds.Tone, item.Preset.ToneTypeStr));
    }

    public Task<Integra7Snapshot> CaptureStudioSetAsync()
    {
        Calls.Add("capture studio set");
        return Task.FromResult(new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "before", [],
            SnapshotKinds.StudioSet, null));
    }

    public Task RestoreStudioSetAsync(Integra7Snapshot studioSet)
    {
        Calls.Add("restore studio set");
        return Task.CompletedTask;
    }
}
```

The cases that must be pinned, each its own test:

1. `The_studio_set_is_captured_before_the_first_patch` — `Calls[0]` is `capture studio set`.
2. `The_studio_set_is_restored_at_the_end` — last call is `restore studio set`.
3. `The_studio_set_is_restored_when_a_capture_throws` — a throwing preset still ends in a restore.
4. `The_boards_are_put_back` — a run that loaded a loadout ends with a load of the original four.
5. `A_silent_preset_is_recorded_and_the_sweep_goes_on` — one `Silent` name, the next preset still captured,
   `Unavailable` has one entry and `Written` has the rest.
6. `A_throwing_preset_is_recorded_and_the_sweep_goes_on` — same shape, `Failed` names it and carries the
   message.
7. `Cancellation_stops_between_patches_and_still_restores` — a token cancelled after the first capture;
   `Cancelled` is true, `Written` has one entry, and the restore happened.
8. `Every_written_snapshot_reaches_the_library` — the write callback is called once per successful capture,
   with the file name the plan chose.
9. `A_round_loads_its_boards_before_capturing_any_of_its_items` — the `load` call precedes the round's
   captures in `Calls`.
10. `A_boardless_round_loads_nothing`.

- [ ] **Step 3: Implement `SeedRun.RunAsync`**, taking `(SeedWork work, SeedSelection selection,
      ISeedInstrument instrument, Func<SeedItem, Integra7Snapshot, string> write, IProgress<SeedProgress>
      progress, CancellationToken token)` and returning `SeedOutcome`. The write is a callback so the run
      does not know about folders, and the test can count without touching a disk.

**The restore is in a `finally`, and the boards are restored inside it too.** A run that threw and left the
user's Studio Set overwritten and their boards evicted is the worst outcome this feature has; it is worse
than not running at all, because they did not choose it.

- [ ] **Step 4: Green, then the whole suite.** Expected 1160 + 10 = **1170**.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SeedRun.cs Tests/TestSeedRun.cs
git commit -m "feat: sweep a plan into the library, isolating what fails"
```

---

### Task 4: the real instrument

**Files:** Create `Src/Models/Services/SeedInstrument.cs`

The adapter. Thin by design, and the one piece here with no tests — everything worth testing was pushed above
it in tasks 1–3.

- [ ] **Step 1: Implement `ISeedInstrument`** over `Integra7Domain` and `IIntegra7Api`:

- `CaptureAsync`: one lease for the whole patch. Write `Studio Set Part/Tone Bank Select MSB`,
  `.../Tone Bank Select LSB` and the tone program number through
  `DomainBase.WriteToIntegraAsync(parameterName, displayedValue, lease)` — **program is `Pc - 1`** — then
  `StudioSetSnapshotService.CaptureToneAsync(domain, part, preset.ToneTypeStr, preset.Name, lease)`
  immediately, with **no delay and no poll**. Catch `SnapshotFormatException` from the *first* block and
  answer null: that is what "this unit does not expose this patch" looks like. Let a failure on a later block
  through as a real failure — a tone that answered once and then stopped is not the same thing.

- [ ] **Step 1a: The snapshot is named by the device, not by the table.**

The captured blocks contain the tone's own name parameter, and where that disagrees with `Presets.csv` the
device is right — an audit on 2026-07-30 found 102 such rows, corrected 97, and left 3 where the table's
spelling is the better one and 2 in banks that answer nothing. So after the capture, read the tone name out
of the snapshot's own values and use it as the snapshot's name; when it differs from the catalogue name, put
the catalogue name in `Notes` as `Listed as "<name>"`.

**Do not use it for the file name** — the file name was chosen before the capture, because that is what the
resume compares against the folder, and a name only knowable after a capture cannot decide whether to
capture. The library already treats those as two different things: `LibraryEntryViewModel.Name` documents
that the name lives inside the file and the file name is the user's.

The rename goes **through `SnapshotMetadata`, not by mutating the snapshot**: it already has a nullable
`Name` whose null means "leave what the file says", and a `Notes`, and `SnapshotLibrary.Annotated` is the one
place those are turned into a snapshot's own fields. So this service answers with a `SnapshotMetadata` — the
category and tags the plan built, plus the device's name and any note — and the write hands that to
`Create` along with the plan's file name.

This is a rule with an input and an output and no device in it, so **put it in a service and test it** —
given a captured snapshot and a preset, what the snapshot should be renamed to and what note it should carry.
Three cases: they agree (no note), they differ (device wins, note records the table), and the snapshot has no
readable tone name (keep the catalogue name, no note — never write an empty name).
- `LoadBoardsAsync`: `SendLoadSrxAsync`, then poll `GetLoadedSrxAsync` until the reported set **stops
  changing** for two consecutive reads, treating `(0,0,0,0)` as still loading. **Never compare against what
  was sent.** Give it a generous ceiling — the `HQ Pcm` loadout converges at 18.7 s and a normal one at ~23 s
  — and throw with a message naming the loadout if it never settles.
- `CaptureStudioSetAsync` / `RestoreStudioSetAsync`: `StudioSetSnapshotService.CaptureAsync` /
  `RestoreAsync`, each with its own lease.

- [ ] **Step 2: Build. No tests** — say so in the commit message rather than leaving it to be noticed.

```bash
git add Src/Models/Services/SeedInstrument.cs
git commit -m "feat: drive a real INTEGRA-7 for a library sweep"
```

---

### Task 5: the panel

**Files:** Create `Src/ViewModels/SeedRunViewModel.cs`, `Src/Views/SeedRunView.axaml` (+ `.axaml.cs`);
Modify `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`,
`Src/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: The screen.** Tick boxes for engines and banks, radio or checkboxes for internal/user, a part
  selector, a live count and estimate from `SeedPlan.Build`, and Start/Cancel. Defaults, from the spec:

| ticked | unticked, with the reason shown |
| --- | --- |
| PRST, SRX, ExSN, user slots | **GM2, ExPCM** — "not available on this unit"; ~20 minutes of reply deadlines to prove it again |
| | **PCM drum kits** — "22 minutes, 137 MB for 216 patches" |

The count and estimate recompute as ticks change — `SeedPlan.Build` is pure and cheap, and it already reads
the folder's file names from the library view model.

- [ ] **Step 2: Progress and refusal.** A progress line (`n of m`, the current bank, elapsed and remaining
  from the measured rate) and Cancel. Refuse to start, with the reason, when: no device; the library folder
  cannot be written to; **Compare is holding edits** — while comparing, the journal's buffer is the only copy
  of them.

- [ ] **Step 3: Where it lives.** A "Seed from instrument…" button in the library's folder row, beside
  Change…, Refresh, Find duplicates… and Export patch list…. That row is now a `WrapPanel`, so a fifth button
  wraps rather than clipping.

- [ ] **Step 4: Refresh the library when the run ends**, including after a cancel — files were written and the
  list must show them.

- [ ] **Step 5: Build, run the suite, commit**

```bash
git add Src/ViewModels/SeedRunViewModel.cs Src/Views/SeedRunView.axaml Src/Views/SeedRunView.axaml.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryView.axaml Src/ViewModels/MainWindowViewModel.cs
git commit -m "feat: seed the library from the instrument"
```

---

### Task 6: verify by driving it

**Files:** none.

Harness pattern from phases 1–5: point the library folder at a throwaway directory by writing the settings
file and restoring it in a `finally`; drive through UI Automation, not synthetic mouse. Scripts to copy are in
`C:\Scripts\Temp\claude\D--Projects-Integra7AuralAlchemist\8c8d7f87-72b2-4a26-87a8-d5f4e2f3e26d\scratchpad\`.
**Never point a run at the user's own library.**

- [ ] **Step 1: A small sweep with a known answer.** Tick SN-A + PRST only — 364 presets, ~42 seconds — and
  let it finish. Expect 364 files, each parsing, each naming SN-A as its engine.

- [ ] **Step 2: The checks**

1. The count and estimate on screen match what the run actually does, within reason.
2. Re-running the same selection captures **nothing** and reports them all as already in the library.
3. Deleting ten files and re-running captures **exactly those ten**.
4. A selection including GM2 records them as unavailable, names them, and does not stop.
5. Cancel mid-run: it stops within a patch or two, the files written so far are intact and listed, and the
   Studio Set is restored.
6. A selection including one SRX bank loads the board, sweeps it, and **puts the original boards back** —
   check with `GetLoadedSrxAsync` before and after.
7. The user's Studio Set is byte-identical after a completed run and after a cancelled one. Capture it before
   and diff.
8. Tags and category are on the written snapshots, and the library's engine and bank filters narrow to them.

- [ ] **Step 3: Report** what was seen for each, with a screenshot of the selection screen and of a run in
  progress.

---

## Verification by hand (user)

**All four checked by the user on 2026-07-31, against their own instrument and a seeded library. All pass.**

- [x] A user-slots-only sweep captures your own patches and skips the empty slots.
- [x] The estimate is close enough to be worth trusting before an hour-long run.
- [x] After a full sweep, the library's search, compare and morph work over factory sounds.
- [x] The duplicate scan over a seeded library is still usable — this is the one the spec expects to get slow.

The last one is the interesting result, because it is the only place this feature predicted its own trouble
and the trouble did not arrive. See the spec's "What it costs the library": a seeded library buckets ~4,300
PCM tones into one engine, which is ~9M pairwise comparisons against the 268 ms measured over 500 files. The
early-out on the first pair that passes the threshold is evidently doing the work the estimate hoped it
would. **Nobody needs the second library folder that was held in reserve for this**, and no code was written
against a slowdown that was reasoned about rather than measured — which is the right order, and worth
recording as the outcome rather than quietly dropping the prediction now that it is wrong.
