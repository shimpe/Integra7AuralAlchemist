# DAW Patch-List Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Write the instrument's whole patch list — every factory tone and whatever user-memory names have
been read — as a file the user's DAW can read, so a track's program dropdown says "Full Grand 1" instead of
"Program 1".

**Architecture:** One pure builder turns the presets already in memory into a `PatchList` of banks and
patches addressed by `(MSB, LSB, program)`. Four writers turn that into text. Everything that can be got
wrong — the program-number base, the escaping, the two patches that share one address — lives in the
builder or a writer, where a test can reach it. The view model does nothing but ask which format, ask where,
and write the bytes.

**Tech stack:** .NET 10, C# 13, NUnit 4, Avalonia 12 (one button and one dialog), no new dependencies.

---

## What the source data actually is — measured, not assumed

Read this before writing anything. Every number here came from the real files, and two of them are traps.

`Src/Assets/Presets.csv` has **6,023 data rows** and this header:

```
"Tone Type","Tone Bank","No.","Tone Name","MSB","LSB","PC","Category"
"SN-A","PRST",0001,"Full Grand 1",89,64,1,"Ac.Piano"
```

`MainWindowViewModel.LoadPresets()` parses it into `Integra7Preset`, which exposes `ToneTypeStr` (`SN-A`,
`SN-S`, `SN-D`, `PCMS`, `PCMD`), `ToneBankStr` (`PRST`, `GM2/GM2#`, `ExSN1`…`ExSN6`, `SRX01`…`SRX12`,
`ExPCM`), `Name`, `Msb`, `Lsb`, `Pc`, `CategoryStr`, and `InternalUserDefinedStr` (`INT` or `USR`).

(**6,023, not 6,022** — corrected 2026-07-30 after review, and the off-by-one is worth knowing because every
later count in this plan is built on it. The file ends without a trailing newline, so `wc -l` counts 6,023
newline characters for 6,024 lines and the header gets subtracted from the wrong number. Measured through
the builder itself: 6,023 presets in, 6,023 patches out, 0 skipped.)

**Trap 1: `Pc` is 1-based.** The CSV's range is **1 to 128**, because that is how Roland prints a tone list.
Every DAW format wants the byte that goes on the wire, which is **0 to 127**. The conversion happens **once**,
in the builder, and is tested there. A writer that subtracts one is a writer that will eventually be joined
by one that forgot.

**Trap 2: two patches share one address.** In the GM2 bank, MSB 121 / LSB 0 / PC 116 — **program 115** once
trap 1 is applied, and that is the number everything downstream of the builder says — carries **both**
`Woodblock` (No. 0206) and `Castanets` (No. 0207). That is why bank 121/0 has 129 rows for 128 programs. It
is in the instrument's own data, it is not a parsing error, and every one of the four formats has a different
way of quietly mangling it. Decided here: **the list keeps both, in document order, and reports the
collision**; the writers emit both faithfully; the export tells the user it happened. Losing a patch silently
to make a file look tidy is the one outcome that is not allowed.

**Banks.** There are **75 distinct `(MSB, LSB)` pairs** in the factory data, and each maps to exactly one
`(ToneTypeStr, ToneBankStr)` — verified across all 6,023 rows. The largest bank has 129 rows, the smallest 1.

**Trap 3, found in review of task 1: that mapping does not go both ways.** No address carries two
`(type, bank)` pairs, but one `(type, bank)` spans up to ten addresses. `PCMS GM2/GM2#` is ten banks
(121/0–121/9), `SN-S PRST` nine (95/64–95/72, 1,109 presets), `PCMS PRST` seven; **51 of the 75 banks share
a name with another**. The CSV survives it by printing MSB and LSB as columns; the other three formats show
the user a name and nothing else. So a bank's name **ends with its address** — `SN-S PRST (95/64)` — on
every bank, not only the ambiguous ones, so that a name never depends on which other banks were exported
beside it. See `PatchListSource.NameOf`, which is the only place this is decided.

**User memory.** `AddUserDefinedPresets` appends presets with `InternalUserDefinedStr == "USR"` at MSB 86–89
with LSB 0 or 1, which never collides with the factory banks (factory PRST banks sit at LSB 64+). Their
`ToneBankStr` is `"PRST"` and their `CategoryStr` is a placeholder — both are marked `/*todo incorrect*/` in
the source. **So a bank's name must come from `InternalUserDefinedStr` first**, not from `ToneBankStr`, or
every user bank would be labelled "PRST" and read as factory. Their names come from the device, so when
nothing is plugged in they are simply absent — which is what the spec means by "absent rather than wrong".

**Where the live list is.** `PartViewModels[1].AllPresets` — the *unfiltered* list, held by reference and
appended to as the user banks arrive. `MainWindowViewModel` has no field of its own; `PartViewModels[1]
.AllPresets` is what line 564 already uses for the same reason. Read it **when the button is pressed**, not
when the view model is built, or the export will be missing every user tone.

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

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **1051 passed, 0 failed**, 134
warnings, 0 errors.

**Traps this project has actually hit**, all of which apply here:

- **An XML comment may not contain `--`**, and a comment may not sit between an element's attributes. The
  first makes MSBuild fail to *load* the project (`MSB4025`), so nothing compiles and the error count reads
  as zero. Check for `MSB4025` before believing a sudden green. Prose uses real em dashes.
- **Never hardcode a colour in XAML.** Use `{StaticResource ...}`.
- **A `ToolTip` is a popup and swallows clicks on its own control.**
- **Do not edit `.axaml` with `sed` and do not rewrite source through PowerShell** — CRLF with a BOM, and
  PowerShell 5.1's `Set-Content` defaults to ANSI, which corrupts UTF-8. Use the Edit/Write tools.
- Compiled bindings are checked at build time; `AVLN2000` means a binding names a member that does not exist.
- **A view model cannot be constructed in a test** under ReactiveUI 24 (`WhenAnyValue` throws demanding
  `RxAppBuilder.BuildApp()`). That is a reason to put logic in a service, **not** a reason to leave it
  untested — phase 4 shipped a bug by taking it the other way.
- Avalonia 12 `SelectionMode` is `Single|Multiple|Toggle|AlwaysSelected`; there is no `Extended`.

**House style:** comments say *why*, not *what*, and are unusually discursive — they record the reasoning and
the alternatives rejected. `Src/Models/Services/DeepSearch.cs` and `ComparisonText.cs` are the register to
match.

**Git:** branch `feature/patch-list-export` off `main`, which is where this plan is committed. Explicit paths
only; never `git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do not merge or
push. Every commit message ends with:

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## File structure

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/PatchList.cs` | The records: `PatchEntry`, `PatchBank`, `PatchList`. What a patch list *is*, with no opinion about any format. |
| `Src/Models/Services/PatchListSource.cs` | Presets in, `PatchList` out. Owns the 1-based to 0-based conversion, the bank naming, the ordering and the collision report. The only place that knows what an `Integra7Preset` is. |
| `Src/Models/Services/IPatchListWriter.cs` | `Label`, `Extension`, `WantsByteOrderMark`, `Write(PatchList)`. Four implementations and one list of them. |
| `Src/Models/Services/CsvPatchListWriter.cs` | The fallback, and the one whose escaping rule is quoting and doubling. |
| `Src/Models/Services/ReabankPatchListWriter.cs` | Reaper. The only format with **no escaping mechanism at all**, so the only one that sanitises. |
| `Src/Models/Services/CubasePatchListWriter.cs` | Steinberg MIDI device XML. |
| `Src/Models/Services/MidnamPatchListWriter.cs` | The MMA MIDINameDocument, read by Ardour and Mixbus. |
| `Src/ViewModels/PatchListExportViewModel.cs` | The format choice, as a dialog view model. |
| `Src/Views/PatchListExportDialog.axaml` (+`.axaml.cs`) | That dialog. |
| `Tests/TestPatchListSource.cs`, `Tests/TestPatchListWriters.cs` | One fixture list, shared, exercising `&`, `"`, `,`, a newline and a non-ASCII name against every writer. All four writers' tests share the one file, as tasks 2–5 say; the five separate files this row used to name were never created. |

Modified: `Src/ViewModels/MainWindowViewModel.cs` (the callback and the two interactions),
`Src/Views/MainWindow.axaml.cs` (the save dialog's file types), `Src/ViewModels/LibraryViewModel.cs` and
`Src/Views/LibraryView.axaml` (the button in the folder row).

---

### Task 1: what a patch list is, and building one from the presets

**Files:**
- Create: `Src/Models/Services/PatchList.cs`, `Src/Models/Services/PatchListSource.cs`
- Test: `Tests/TestPatchListSource.cs`

> **Shipped, then amended after review (2026-07-30). Two things below are superseded; the source is the
> record.** Bank names now end with their address (trap 3 above), so `NameOf` returns `SN-A ExSN1 (89/96)`
> and `PCMS USER (87/0)`, not the bare strings the listings in steps 1 and 4 show. And three tests were
> added that the listing does not have: the two range boundaries (`Pc` 128 kept at program 127, `Pc` 0 and
> 129 left out) and one that two banks of the same engine and bank are told apart. The boundaries earn
> their place — with only the tests below, changing the range check to read `Pc` instead of `Pc - 1` drops
> one patch from each of the 41 banks that end on PC 128 and every test still passes.

- [ ] **Step 1: Write the failing tests**

`Integra7Preset`'s constructor validates its strings, so the fixtures must use real vocabulary: a tone type
of `SN-A`/`SN-S`/`SN-D`/`PCMS`/`PCMD`, a bank of `PRST`/`GM2/GM2#`/`ExSN1`…, `INT` or `USR`, and a category
from `Integra7Preset.ToneCategories`. Anything else throws `MidiException`, which is a fixture bug that
reads like a product bug.

```csharp
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Turning the instrument's presets into something addressable by bank select and program change.
/// </summary>
public class PatchListSourceTests
{
    private static Integra7Preset Preset(string name, int msb, int lsb, int pc,
        string type = "SN-A", string bank = "PRST", string usage = "INT", string category = "Ac.Piano") =>
        new(0, usage, type, bank, pc, name, msb, lsb, pc, category);

    /// <summary>The CSV counts programs from 1 because that is how Roland prints a tone list; every DAW
    /// format wants the byte that goes on the wire. The conversion happens here, once.</summary>
    [Test]
    public void Programs_are_numbered_from_nought()
    {
        var list = PatchListSource.From([Preset("Full Grand 1", 89, 64, 1)]);

        Assert.That(list.Banks[0].Patches[0].Program, Is.EqualTo(0));
    }

    [Test]
    public void One_bank_per_address()
    {
        var list = PatchListSource.From([
            Preset("A", 89, 64, 1), Preset("B", 89, 64, 2), Preset("C", 89, 65, 1)]);

        Assert.That(list.Banks, Has.Count.EqualTo(2));
        Assert.That(list.Banks[0].Patches, Has.Count.EqualTo(2));
    }

    /// <summary>Banks in address order and patches in program order, so that two exports of one instrument
    /// are the same file and a diff of them means something.</summary>
    [Test]
    public void Banks_and_patches_are_in_a_stable_order()
    {
        var list = PatchListSource.From([
            Preset("second", 89, 65, 1), Preset("later", 89, 64, 9), Preset("first", 89, 64, 2)]);

        Assert.That(list.Banks.Select(b => (b.Msb, b.Lsb)), Is.EqualTo(new[] { (89, 64), (89, 65) }));
        Assert.That(list.Banks[0].Patches.Select(p => p.Name), Is.EqualTo(new[] { "first", "later" }));
    }

    /// <summary>A factory bank is named for the engine and the bank it came from.</summary>
    [Test]
    public void A_factory_bank_is_named_for_its_engine_and_bank()
    {
        var list = PatchListSource.From([Preset("A", 89, 96, 1, bank: "ExSN1")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("SN-A ExSN1"));
    }

    /// <summary>A user bank is named for being one. Its ToneBankStr says "PRST" -- the source marks that
    /// wrong and it is -- so naming from the bank string alone would label the user's own tones as factory
    /// ones, which is the one label that must not be wrong in a patch list.</summary>
    [Test]
    public void A_user_bank_says_it_is_user_memory()
    {
        var list = PatchListSource.From([Preset("Mine", 87, 0, 1, type: "PCMS", usage: "USR")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("PCMS USER"));
    }

    /// <summary>Two patches at one address is in the instrument's own data: MSB 121 / LSB 0 / PC 116 is
    /// both Woodblock and Castanets. Both are kept -- a patch list that quietly drops one to look tidy is
    /// worse than one that reports the truth -- and the collision is named so the export can say so.
    /// </summary>
    [Test]
    public void Two_patches_at_one_address_are_both_kept_and_reported()
    {
        var list = PatchListSource.From([
            Preset("Woodblock", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion"),
            Preset("Castanets", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion")]);

        Assert.That(list.Banks[0].Patches, Has.Count.EqualTo(2));
        Assert.That(list.Collisions, Has.Count.EqualTo(1));
        Assert.That(list.Collisions[0], Does.Contain("Woodblock").And.Contain("Castanets"));
    }

    /// <summary>Document order decides which of two patches at one address comes first, because it is the
    /// order the instrument's own list is printed in and the only order a user could recognise.</summary>
    [Test]
    public void A_collision_keeps_the_order_the_presets_were_given_in()
    {
        var list = PatchListSource.From([
            Preset("Woodblock", 121, 0, 116), Preset("Castanets", 121, 0, 116)]);

        Assert.That(list.Banks[0].Patches.Select(p => p.Name),
            Is.EqualTo(new[] { "Woodblock", "Castanets" }));
    }

    /// <summary>A program the wire cannot carry is left out rather than written wrong: a file that names
    /// the patch at a program the DAW will never send is a file that lies about every one after it.
    /// </summary>
    [Test]
    public void A_program_outside_the_wire_range_is_left_out_and_reported()
    {
        var list = PatchListSource.From([Preset("Impossible", 89, 64, 200), Preset("Fine", 89, 64, 1)]);

        Assert.That(list.Banks[0].Patches.Select(p => p.Name), Is.EqualTo(new[] { "Fine" }));
        Assert.That(list.Skipped, Has.Count.EqualTo(1));
        Assert.That(list.Skipped[0], Does.Contain("Impossible"));
    }

    [Test]
    public void No_presets_is_an_empty_list_rather_than_a_failure()
    {
        var list = PatchListSource.From([]);

        Assert.That(list.Banks, Is.Empty);
        Assert.That(list.Collisions, Is.Empty);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter PatchListSourceTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"`

Expected: they do not compile — `CS0246: The type or namespace name 'PatchListSource' could not be found`.
That is the right failure. A test that fails on an assertion before the type exists is a test that was
already passing for the wrong reason.

- [ ] **Step 3: `PatchList.cs`**

```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One patch as a DAW addresses it: two control changes and a program change, with a name to put
/// in a dropdown.
///
/// <b>Program is the number that goes on the wire</b>, 0 to 127 -- not the 1 to 128 the instrument's own
/// tone list is printed with. The conversion happens once, in <see cref="PatchListSource"/>, because four
/// writers each subtracting one is three chances to forget.</summary>
/// <param name="Engine">The engine code, kept because a patch list is also read by a human deciding which
/// of two similarly named sounds they want.</param>
/// <param name="UserMemory">Whether this came from the instrument's user memory rather than the factory
/// data. What makes a bank's name honest -- see <see cref="PatchListSource"/>.</param>
public sealed record PatchEntry(int Program, string Name, string Engine, string Category, bool UserMemory);

/// <summary>Every patch reachable at one bank-select address.</summary>
public sealed record PatchBank(int Msb, int Lsb, string Name, IReadOnlyList<PatchEntry> Patches);

/// <summary>A whole instrument's worth of patches, and what could not be represented faithfully.
///
/// <b>The two lists of prose are part of the answer, not diagnostics.</b> A patch list that silently
/// dropped a patch would look exactly like a correct one, and the user would find out when a track played
/// the wrong sound. So what was left out and what shares an address are carried back to whoever asked, to
/// be said out loud.</summary>
/// <param name="Device">What the file calls the instrument.</param>
/// <param name="Collisions">Addresses carrying more than one patch, in words. The instrument's own data
/// has one: MSB 121 / LSB 0, program 115, is both Woodblock and Castanets.</param>
/// <param name="Skipped">Patches left out because their program cannot go on the wire.</param>
public sealed record PatchList(
    string Device,
    IReadOnlyList<PatchBank> Banks,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Skipped);
```

- [ ] **Step 4: `PatchListSource.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The instrument's presets as something a DAW can address.
///
/// <b>The source is the instrument, not the library.</b> A DAW patch list is reachable by bank select and
/// program change; a library file is not reachable that way at all. So this reads the presets already in
/// memory -- the factory data this build ships with, plus whatever user-memory names have been read from a
/// connected instrument -- and nothing here opens a file or needs a device.
///
/// <b>Everything that can be got wrong is here rather than in a writer.</b> The program base, the bank
/// naming, the ordering and the two patches that share one address are one decision each, made once, tested
/// once, and handed to all four formats already settled.</summary>
public static class PatchListSource
{
    /// <summary>The lowest and highest program a MIDI program change can carry. The instrument's own tone
    /// list counts from 1, so this is not the range of the numbers coming in.</summary>
    private const int FirstProgram = 0, LastProgram = 127;

    public static PatchList From(IReadOnlyList<Integra7Preset> presets, string device = "INTEGRA-7")
    {
        List<string> skipped = [];
        List<string> collisions = [];

        // Indexed rather than grouped straight away: a stable sort has to be able to say which of two
        // patches at one address came first, and the presets' own order is the only order a user could
        // recognise -- it is the order the instrument's tone list is printed in.
        var rows = presets
            .Select((preset, index) => (preset, index))
            .Where(row =>
            {
                var program = row.preset.Pc - 1;
                if (program is >= FirstProgram and <= LastProgram) return true;
                // Left out rather than clamped. A clamp would put this patch's name on some other patch's
                // program, and every name after it would be a lie the user only discovers by playing it.
                skipped.Add($"{row.preset.Name} (program {row.preset.Pc})");
                return false;
            })
            .ToList();

        var banks = rows
            .GroupBy(row => (row.preset.Msb, row.preset.Lsb))
            .OrderBy(bank => bank.Key.Msb).ThenBy(bank => bank.Key.Lsb)
            .Select(bank =>
            {
                var patches = bank
                    .OrderBy(row => row.preset.Pc).ThenBy(row => row.index)
                    .Select(row => new PatchEntry(row.preset.Pc - 1, row.preset.Name, row.preset.ToneTypeStr,
                        row.preset.CategoryStr, row.preset.InternalUserDefinedStr == "USR"))
                    .ToList();

                foreach (var shared in patches.GroupBy(patch => patch.Program).Where(g => g.Count() > 1))
                    collisions.Add($"MSB {bank.Key.Msb} LSB {bank.Key.Lsb} program {shared.Key}: " +
                                   string.Join(", ", shared.Select(patch => patch.Name)));

                return new PatchBank(bank.Key.Msb, bank.Key.Lsb, NameOf(bank.First().preset), patches);
            })
            .ToList();

        return new PatchList(device, banks, collisions, skipped);
    }

    /// <summary>What to call a bank, taken from any one of its members because an address is one engine's
    /// one bank -- verified across all 6,023 rows of the factory data. (Superseded: see the note at the
    /// top of this task. The converse does not hold and the address is now part of the name.)
    ///
    /// <b>User memory is asked about first.</b> The presets built from the instrument's user-tone names
    /// carry a <c>ToneBankStr</c> of "PRST", which the source that builds them marks as wrong and is: they
    /// are not the factory bank. Naming from the bank string alone would label the user's own sounds as
    /// factory ones in every exported file, which is the one label in a patch list that must not be
    /// wrong.</summary>
    private static string NameOf(Integra7Preset preset) =>
        preset.InternalUserDefinedStr == "USR"
            ? $"{preset.ToneTypeStr} USER"
            : $"{preset.ToneTypeStr} {preset.ToneBankStr}";
}
```

- [ ] **Step 5: Green, then the whole suite**

Run the filtered test, then the whole suite. Expected: 1051 + 9 = **1060**.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/PatchList.cs Src/Models/Services/PatchListSource.cs Tests/TestPatchListSource.cs
git commit -m "feat: address the instrument's tones the way a DAW does"
```

---

### Task 2: the writer interface and the CSV writer

**Files:**
- Create: `Src/Models/Services/IPatchListWriter.cs`, `Src/Models/Services/CsvPatchListWriter.cs`,
  `Tests/TestPatchListWriters.cs`
- Test: the shared fixture lives in `Tests/TestPatchListWriters.cs` and every later task adds to it.

- [ ] **Step 1: The shared fixture and the CSV tests**

Every writer is tested against the same list, which carries the four characters that break a format plus one
that breaks a line-oriented one.

```csharp
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One awkward patch list, shared by every writer's tests.
///
/// The names are the four things that break a text format -- an ampersand, a double quote, a comma and a
/// non-ASCII letter -- plus a newline, which is the one that breaks a format with no escaping at all.
/// Every writer is asked the same question so that the answers can be compared.</summary>
public static class AwkwardPatchList
{
    public static PatchList Build() => new(
        "INTEGRA-7",
        [
            new PatchBank(89, 64, "SN-A PRST",
            [
                new PatchEntry(0, "Rock & Roll", "SN-A", "E.Guitar", false),
                new PatchEntry(1, "The \"Big\" One", "SN-A", "Brass", false),
                new PatchEntry(2, "Strings, Warm", "SN-A", "Strings", false),
                new PatchEntry(3, "Café Piano", "SN-A", "Ac.Piano", false),
                new PatchEntry(4, "Split\nName", "SN-A", "FX", false),
            ]),
            new PatchBank(87, 0, "PCMS USER", [new PatchEntry(0, "Mine", "PCMS", "Synth Lead", true)]),
        ],
        [],
        []);
}

public class CsvPatchListWriterTests
{
    private static string Written() => new CsvPatchListWriter().Write(AwkwardPatchList.Build());

    /// <summary>A header, because this one is opened in a spreadsheet by a human rather than parsed by a
    /// DAW, and a column of bare numbers with no header is a puzzle.</summary>
    [Test]
    public void It_starts_with_a_header_row()
    {
        Assert.That(Written().Split("\r\n")[0], Is.EqualTo("MSB,LSB,Program,Bank,Name,Engine,Category,User"));
    }

    /// <summary>RFC 4180: a field containing a comma, a quote or a newline is quoted, and a quote inside is
    /// doubled. Excel and LibreOffice both read this; nothing else is portable.</summary>
    [Test]
    public void A_comma_a_quote_and_a_newline_are_quoted_and_doubled()
    {
        var rows = Written().Split("\r\n");

        Assert.That(rows[2], Does.Contain("\"The \"\"Big\"\" One\""));
        Assert.That(rows[3], Does.Contain("\"Strings, Warm\""));
        Assert.That(Written(), Does.Contain("\"Split\nName\""));
    }

    /// <summary>An ampersand and a non-ASCII letter are ordinary characters in CSV and are left alone --
    /// which is worth a test, because three of the four writers have to do something to them and copying
    /// that here would be the easy mistake.</summary>
    [Test]
    public void An_ampersand_and_an_accent_are_left_alone()
    {
        Assert.That(Written(), Does.Contain("Rock & Roll").And.Contain("Café Piano"));
    }

    /// <summary>Rows are separated by CRLF and the newline inside a name is a bare LF, so counting CRLFs
    /// counts rows -- which is the property a spreadsheet relies on and the reason the separator is not
    /// the LF this application uses everywhere else.</summary>
    [Test]
    public void A_newline_inside_a_name_does_not_make_a_second_row()
    {
        // One header and six patches: five in the first bank, one in the second.
        Assert.That(Written().TrimEnd().Split("\r\n").Length, Is.EqualTo(7));
    }

    [Test]
    public void User_memory_is_marked()
    {
        Assert.That(Written(), Does.Contain("87,0,0,PCMS USER,Mine,PCMS,Synth Lead,yes"));
    }
}
```

Note the seventh line in `Every_patch_gets_a_row`: the newline inside `Split\nName` sits **inside quotes**, so
a naive `Split("\r\n")` sees 8 lines for 7 rows only if the embedded newline is `\n` alone. Write the test to
match what the writer actually emits, and if the count surprises you, work out which of the two is wrong
before changing either.

- [ ] **Step 2: Run and watch it fail** (`CS0246` on `CsvPatchListWriter`).

- [ ] **Step 3: `IPatchListWriter.cs`**

```csharp
namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One patch-list format.
///
/// <b>Text in, text out, no file.</b> Writing the bytes belongs to whoever asked and can report a failure
/// to the user; what is here is the format and nothing else, which is what makes all four of them testable
/// against the same awkward list.</summary>
public interface IPatchListWriter
{
    /// <summary>What to call this format in the picker, including the extension the user will recognise.
    /// </summary>
    string Label { get; }

    /// <summary>The extension without its dot, for the save dialog and the suggested file name.</summary>
    string Extension { get; }

    string Write(PatchList list);
}
```

- [ ] **Step 4: `CsvPatchListWriter.cs`**

```csharp
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as a spreadsheet.
///
/// <b>Not a DAW format, and that is the point.</b> It is the honest fallback for the DAW nobody wrote a
/// writer for, and it is the only one of the four a user can read, sort and check by eye. A librarian
/// looking for "which bank is that pad in" is better served by this than by any of the others.
///
/// <b>RFC 4180 quoting</b>: a field carrying a comma, a quote or a newline is wrapped in quotes, and a
/// quote inside it is doubled. Excel and LibreOffice both read that; anything else is one spreadsheet's
/// habit.</summary>
public sealed class CsvPatchListWriter : IPatchListWriter
{
    public string Label => "Spreadsheet (.csv)";
    public string Extension => "csv";

    public string Write(PatchList list)
    {
        var text = new StringBuilder();
        text.Append("MSB,LSB,Program,Bank,Name,Engine,Category,User\r\n");

        foreach (var bank in list.Banks)
        foreach (var patch in bank.Patches)
            text.Append(string.Join(',',
                    bank.Msb, bank.Lsb, patch.Program, Field(bank.Name), Field(patch.Name),
                    Field(patch.Engine), Field(patch.Category), patch.UserMemory ? "yes" : ""))
                .Append("\r\n");

        return text.ToString();
    }

    /// <summary>CRLF, and deliberately, even though nothing else this application writes uses it: RFC 4180
    /// says CRLF, and a spreadsheet on Windows opening a LF-only file is the one place this would be
    /// noticed.</summary>
    private static string Field(string value) =>
        value.Any(c => c is ',' or '"' or '\r' or '\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
```

- [ ] **Step 5: Green, then the whole suite.** Expected: 1060 + 5 = **1065**.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/IPatchListWriter.cs Src/Models/Services/CsvPatchListWriter.cs Tests/TestPatchListWriters.cs
git commit -m "feat: write a patch list a spreadsheet can open"
```

---

### Task 3: the Reaper writer

**Files:**
- Create: `Src/Models/Services/ReabankPatchListWriter.cs`
- Modify: `Tests/TestPatchListWriters.cs`

`.reabank` is line-oriented and has **no escaping mechanism whatsoever**. A name containing a newline does
not produce a broken entry; it produces a *second entry* whose first word is read as a program number, and
Reaper then either shows a patch called nothing or refuses the whole bank. This is the format the spec names
as the one that will be got wrong.

The format:

```
// comment
Bank 89 64 SN-A PRST
0 Rock & Roll
1 The "Big" One
```

**Corrected 2026-07-30, after this shipped with the wrong marker.** This sketch said `;`, task 3 copied it,
and it took a review to catch. A comment is `//`: REAPER's own factory `Data/GM.reabank` opens
`// .reabank files define MIDI bank/program (patch) information` and has no semicolon-led line anywhere, and
Reaticulate — the most faithful third-party parser — recognises `//` and its own `//!` and nothing else. The
`;` convention belongs to REAPER's theme and langpack files. Nothing was visibly broken, because both
parsers drop an unrecognised line in silence and these two sit before the first `Bank`; that is the point.
`ReabankPatchListWriter` now pins the marker with a test of its own.

- [ ] **Step 1: The tests**

```csharp
public class ReabankPatchListWriterTests
{
    private static string Written() => new ReabankPatchListWriter().Write(AwkwardPatchList.Build());

    [Test]
    public void A_bank_is_its_address_and_its_name()
    {
        Assert.That(Written(), Does.Contain("Bank 89 64 SN-A PRST"));
    }

    [Test]
    public void A_patch_is_its_program_and_its_name()
    {
        Assert.That(Written(), Does.Contain("\n0 Rock & Roll\n"));
    }

    /// <summary>The format has no escaping at all, so a newline inside a name would end the line and the
    /// next word would be read as a program number -- a patch list that is wrong from that point on, in a
    /// file that still loads. Flattened to a space instead.</summary>
    [Test]
    public void A_newline_in_a_name_becomes_a_space()
    {
        Assert.That(Written(), Does.Contain("4 Split Name"));
        Assert.That(Written().Split('\n').Any(line => line.Trim() == "Name"), Is.False);
    }

    /// <summary>Quotes, ampersands and accents are ordinary characters here: the format has no syntax for
    /// them to break. Sanitising more than the line ending would be mangling names for no reason.</summary>
    [Test]
    public void Nothing_else_is_altered()
    {
        Assert.That(Written(), Does.Contain("1 The \"Big\" One").And.Contain("3 Café Piano"));
    }

    /// <summary>A name that sanitises away to nothing still needs a name, or the line is a program number
    /// with nothing after it and Reaper shows a blank entry the user cannot identify.</summary>
    [Test]
    public void A_name_that_is_only_whitespace_gets_one()
    {
        var list = new PatchList("INTEGRA-7",
            [new PatchBank(89, 64, "SN-A PRST", [new PatchEntry(0, "  \t ", "SN-A", "FX", false)])], [], []);

        Assert.That(new ReabankPatchListWriter().Write(list), Does.Contain("0 (unnamed)"));
    }
}
```

- [ ] **Step 2: Run and watch it fail.**

- [ ] **Step 3: The writer**

```csharp
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as a Reaper bank file.
///
/// <b>The format has no escaping mechanism at all</b>, which is what makes this the writer to be careful
/// in. It is lines: "Bank &lt;msb&gt; &lt;lsb&gt; &lt;name&gt;", then "&lt;program&gt; &lt;name&gt;" for each patch, and a
/// name runs to the end of its line. So a name carrying a newline does not produce a broken entry -- it
/// produces a second line whose first word Reaper reads as a program number, and every patch after it is
/// wrong in a file that still loads. Anything that could end a line is flattened to a space.
///
/// <b>And nothing else is touched.</b> Quotes, ampersands and accented letters have no meaning here, so
/// sanitising them would be mangling a user's patch names to protect against a syntax the format does not
/// have.</summary>
public sealed class ReabankPatchListWriter : IPatchListWriter
{
    public string Label => "Reaper (.reabank)";
    public string Extension => "reabank";

    public string Write(PatchList list)
    {
        var text = new StringBuilder();
        text.Append($"; {list.Device} patch names\n");
        text.Append("; Written by Integra-7 Aural Alchemist\n");

        foreach (var bank in list.Banks)
        {
            text.Append($"\nBank {bank.Msb} {bank.Lsb} {OneLine(bank.Name)}\n");
            foreach (var patch in bank.Patches)
                text.Append($"{patch.Program} {OneLine(patch.Name)}\n");
        }

        return text.ToString();
    }

    /// <summary>Everything that could end a line, flattened; runs of space collapsed, because two names
    /// that differed only by a tab would otherwise read as the same name with a gap in it.</summary>
    private static string OneLine(string value)
    {
        var flattened = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var collapsed = string.Join(' ', flattened.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? "(unnamed)" : collapsed;
    }
}
```

- [ ] **Step 4: Green, then the whole suite.** Expected **1070**.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ReabankPatchListWriter.cs Tests/TestPatchListWriters.cs
git commit -m "feat: write a patch list Reaper can read"
```

---

### Task 4: the Cubase / Nuendo writer

**Files:**
- Create: `Src/Models/Services/CubasePatchListWriter.cs`
- Modify: `Tests/TestPatchListWriters.cs`

- [ ] **Step 1: Settle the schema before writing a line of it**

**This is the one format in this plan whose exact shape is not established from the source data**, and
guessing it would produce a file that imports as nothing with no error. Before implementing:

1. Look up Steinberg's MIDI Device / patch script XML — `mcp__plugin_context7_context7__resolve-library-id`
   then `query-docs`, or a web search for the MIDI Device Manager's exported XML.
2. Write down, in the writer's doc comment, **which shape you settled on and where you saw it**.
3. If you cannot confirm it, say so in your report and implement the shape the spec describes — nested
   `PatchBank` elements, each patch carrying its two control changes and a program change — with the doc
   comment stating plainly that it is unverified and how the user can check it. **Do not claim it is
   verified when it is not.** A wrong claim in a comment is worse here than no claim, because the next
   person will not re-check it.

Whatever you settle on, the test asserts the exact bytes of a short document, so the decision is pinned.

- [ ] **Step 2: The tests** — write these against the shape settled in step 1. They must cover, at minimum:

```csharp
public class CubasePatchListWriterTests
{
    private static string Written() => new CubasePatchListWriter().Write(AwkwardPatchList.Build());

    [Test]
    public void It_is_well_formed_xml()
    {
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(Written()));
    }

    /// <summary>The five characters XML reserves, in a patch name. An ampersand alone is what makes an
    /// unescaped document fail to parse at all, which is the failure a user sees as "the import did
    /// nothing".</summary>
    [Test]
    public void Xml_entities_are_escaped()
    {
        Assert.That(Written(), Does.Contain("Rock &amp; Roll"));
        Assert.That(Written(), Does.Not.Contain("Rock & Roll"));
    }

    /// <summary>Read back rather than matched as text: what matters is that a parser sees the original
    /// name, not which of the legal escapes was used to write it.</summary>
    [Test]
    public void A_parser_reads_the_names_back_unchanged()
    {
        var names = System.Xml.Linq.XDocument.Parse(Written())
            .Descendants().Attributes("Name").Select(a => a.Value).ToList();

        Assert.That(names, Does.Contain("The \"Big\" One"));
        Assert.That(names, Does.Contain("Café Piano"));
        Assert.That(names, Does.Contain("Split\nName"));
    }

    [Test]
    public void Every_patch_carries_its_two_control_changes_and_its_program()
    {
        // Assert the exact elements of the shape settled in step 1, for the first patch of the first bank:
        // control 0 = 89, control 32 = 64, program = 0.
    }

    [Test]
    public void The_document_declares_utf8()
    {
        Assert.That(Written(), Does.StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>"));
    }
}
```

Fill in the fourth test's body against the settled shape — leaving it as a comment is a plan failure, not a
licence.

- [ ] **Step 3: The writer.** Build it with `System.Xml.Linq` (`XDocument`/`XElement`) rather than string
concatenation: `XElement` escapes attribute and element content itself, which is the whole of what the tests
above are about, and hand-rolled escaping is what the spec predicts will be got wrong. Save with
`SaveOptions.None` and an explicit UTF-8 declaration.

- [ ] **Step 4: Green, then the whole suite.** Commit:

```bash
git add Src/Models/Services/CubasePatchListWriter.cs Tests/TestPatchListWriters.cs
git commit -m "feat: write a patch list Cubase can read"
```

---

### Task 5: the midnam writer

**Files:**
- Create: `Src/Models/Services/MidnamPatchListWriter.cs`
- Modify: `Tests/TestPatchListWriters.cs`

The MMA MIDINameDocument is a published DTD and stable. The shape:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE MIDINameDocument PUBLIC "-//MIDI Manufacturers Association//DTD MIDINameDocument 1.0//EN" "http://www.midi.org/dtds/MIDINameDocument10.dtd">
<MIDINameDocument>
  <Author>Integra-7 Aural Alchemist</Author>
  <MasterDeviceNames>
    <Manufacturer>Roland</Manufacturer>
    <Model>INTEGRA-7</Model>
    <CustomDeviceMode Name="Default">
      <ChannelNameSetAssignments>
        <ChannelNameSetAssign Channel="1" NameSet="INTEGRA-7"/>
        <!-- ... one per channel, 1 to 16 ... -->
      </ChannelNameSetAssignments>
    </CustomDeviceMode>
    <ChannelNameSet Name="INTEGRA-7">
      <AvailableForChannels>
        <AvailableChannel Channel="1" Available="true"/>
        <!-- ... one per channel ... -->
      </AvailableForChannels>
      <PatchBank Name="SN-A PRST">
        <MIDICommands>
          <ControlChange Control="0" Value="89"/>
          <ControlChange Control="32" Value="64"/>
        </MIDICommands>
        <PatchNameList>
          <Patch Number="1" Name="Rock &amp; Roll" ProgramChange="0"/>
        </PatchNameList>
      </PatchBank>
    </ChannelNameSet>
  </MasterDeviceNames>
</MIDINameDocument>
```

**`Number` and `ProgramChange` are not the same number.** `ProgramChange` is the wire value, 0 to 127.
`Number` is a display index within the list, conventionally 1-based. Emitting the wire value for both is the
mistake to avoid, and it is silent — Ardour shows a list that is right and numbered from zero.

- [ ] **Step 1: The tests**

```csharp
public class MidnamPatchListWriterTests
{
    private static string Written() => new MidnamPatchListWriter().Write(AwkwardPatchList.Build());

    [Test]
    public void It_is_well_formed_and_declares_the_mma_doctype()
    {
        Assert.That(Written(), Does.Contain("<!DOCTYPE MIDINameDocument PUBLIC"));
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(Written()));
    }

    [Test]
    public void A_bank_carries_its_two_control_changes()
    {
        var bank = System.Xml.Linq.XDocument.Parse(Written()).Descendants("PatchBank").First();
        var changes = bank.Descendants("ControlChange")
            .Select(c => (c.Attribute("Control")!.Value, c.Attribute("Value")!.Value)).ToList();

        // The fixture's own first bank is 89/64 -- it is built by hand, not through PatchListSource, so it
        // is in the order it is written in rather than in address order.
        Assert.That(changes, Is.EqualTo(new[] { ("0", "89"), ("32", "64") }));
    }

    /// <summary>Number is a display index and ProgramChange is the wire value. Writing the wire value for
    /// both is the silent mistake: Ardour then shows a list that is correct and numbered from zero.
    /// </summary>
    [Test]
    public void The_display_number_and_the_program_change_are_not_the_same_number()
    {
        var patch = System.Xml.Linq.XDocument.Parse(Written()).Descendants("Patch").First();

        Assert.That(patch.Attribute("ProgramChange")!.Value, Is.EqualTo("0"));
        Assert.That(patch.Attribute("Number")!.Value, Is.EqualTo("1"));
    }

    [Test]
    public void Every_channel_is_offered_the_name_set()
    {
        var doc = System.Xml.Linq.XDocument.Parse(Written());

        Assert.That(doc.Descendants("AvailableChannel").Count(), Is.EqualTo(16));
        Assert.That(doc.Descendants("ChannelNameSetAssign").Count(), Is.EqualTo(16));
    }

    [Test]
    public void A_parser_reads_the_names_back_unchanged()
    {
        var names = System.Xml.Linq.XDocument.Parse(Written())
            .Descendants("Patch").Select(p => p.Attribute("Name")!.Value).ToList();

        Assert.That(names, Does.Contain("Rock & Roll").And.Contain("Café Piano"));
    }
}
```

The fixture is written by hand rather than built through `PatchListSource`, so its banks are in the order
they are written in — `(89, 64)` first — not in the address order the source would put them in. Assert
against the fixture, and if a writer's output disagrees with it, work out which of the two is wrong before
changing either.

- [ ] **Step 2: Run and watch it fail.**

- [ ] **Step 3: The writer.** `XDocument` again, with an `XDocumentType` for the DOCTYPE. The channel lists
are 1 to 16.

- [ ] **Step 4: Green, then the whole suite.** Commit:

```bash
git add Src/Models/Services/MidnamPatchListWriter.cs Tests/TestPatchListWriters.cs
git commit -m "feat: write a patch list Ardour can read"
```

---

### Task 6: the button, the format choice and the file

**Files:**
- Create: `Src/ViewModels/PatchListExportViewModel.cs`, `Src/Views/PatchListExportDialog.axaml` (+`.axaml.cs`)
- Modify: `Src/ViewModels/MainWindowViewModel.cs`, `Src/Views/MainWindow.axaml.cs`,
  `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`

- [ ] **Step 1: The list of writers**

Add to `IPatchListWriter.cs`:

```csharp
/// <summary>Every format offered, in the order the picker shows them. Reaper first because it is the one
/// the format was asked for; the spreadsheet last because it is the fallback rather than a DAW.</summary>
public static class PatchListWriters
{
    public static IReadOnlyList<IPatchListWriter> All { get; } =
    [
        new ReabankPatchListWriter(), new CubasePatchListWriter(),
        new MidnamPatchListWriter(), new CsvPatchListWriter(),
    ];
}
```

- [ ] **Step 2: The dialog**

`PatchListExportViewModel` holds `IReadOnlyList<IPatchListWriter> Formats`, a `[Reactive] IPatchListWriter?
_selected` defaulting to the first, and nothing else — it picks a format and closes. Copy the shape of
`ConfirmViewModel` and its dialog, which is the smallest existing example in this codebase; the view is a
`ListBox` or `ComboBox` of `Label` plus OK and Cancel. It is reached through a new
`Interaction<PatchListExportViewModel, IPatchListWriter?>` on `MainWindowViewModel`, registered in
`MainWindow.axaml.cs` beside the others.

- [ ] **Step 3: The save dialog takes a file type**

`DoShowSaveTextDialogAsync` in `MainWindow.axaml.cs` hardcodes `*.txt`. Do not widen it — add a second
handler for a new `Interaction<FilePickerRequest, string?> ShowSavePatchListDialog`, whose
`FilePickerSaveOptions` take the extension from the request. `FilePickerRequest` already carries
`(Title, Folder, SuggestedName)`; add the extension as a fourth member with a default, so the two existing
call sites are untouched.

The three-way answer is load-bearing and is already documented at `DoShowSaveSnapshotDialogAsync`: **null for
a cancellation, `""` for a file picked that has no local path**, and a real path otherwise. Copy it exactly —
a command that treats "picked but unusable" as "cancelled" says nothing and looks broken.

- [ ] **Step 4: The command**

On `MainWindowViewModel`:

```csharp
    /// <summary>Write the instrument's whole patch list where the user's DAW can find it.
    ///
    /// <b>The presets are read now, not captured.</b> The user banks arrive in the background after the
    /// instrument answers, so a list taken when this view model was built would be missing every user tone
    /// -- and missing them silently, which is the failure this feature exists to prevent. AllPresets is the
    /// unfiltered list; Presets is part 1's filtered view and would export whatever is typed in its search
    /// box.</summary>
    public async Task ExportPatchListAsync()
    {
        UserActionLog.Action("button: Export patch list");

        var writer = await ShowPatchListExportDialog.Handle(new PatchListExportViewModel());
        if (writer is null) return;

        var list = PatchListSource.From(PartViewModels[1].AllPresets);
        // ... save dialog, File.WriteAllText with new UTF8Encoding(writer.WantsByteOrderMark), status line
    }
```

**Ask the writer about the byte-order mark; do not decide it here.** `new UTF8Encoding(writer
.WantsByteOrderMark)` — the flag was added to `IPatchListWriter` in task 2 for this line. The four formats
disagree and both failures are silent: Reaper's parser and several midnam readers treat a leading BOM as
part of the first token, and the symptom is a bank that does not appear; Excel opening a BOM-less UTF-8
`.csv` by double-click falls back to the system code page and mangles the 84 factory names that carry a
curly apostrophe. Only `CsvPatchListWriter` answers `true`.

**Say what could not be represented.** If `list.Collisions` or `list.Skipped` is non-empty, the status line
must say so — something the user can act on, naming the first: *"Exported 6,023 patches. 1 address carries
two patches (MSB 121 LSB 0 program 115: Woodblock, Castanets); your DAW will show one of them."* This is the
whole reason `PatchList` carries those lists. **Program 115, not 116**: the builder's collision strings are
in wire numbering like everything else it produces, and a status line that says 116 disagrees with the file
it is describing.

- [ ] **Step 5: The button**

In `LibraryView.axaml`'s folder row, beside `Change…`, `Refresh` and `Find duplicates…`, bound to a new
callback on `LibraryViewModel` handed in from `MainWindowViewModel` — the same shape as `_compareTwo`. The
library is not its source; it is where the user is when thinking about patch organisation, and a second place
in the window for one button earns less than it costs.

- [ ] **Step 6: Build, run the suite, commit**

```bash
git add Src/ViewModels/PatchListExportViewModel.cs Src/Views/PatchListExportDialog.axaml Src/Views/PatchListExportDialog.axaml.cs Src/ViewModels/MainWindowViewModel.cs Src/Views/MainWindow.axaml.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryView.axaml Src/Models/Services/IPatchListWriter.cs
git commit -m "feat: export the instrument's patch list for a DAW"
```

---

### Task 7: verify by driving it

**Files:** none.

Use the harness pattern from phases 1–4: the scripts are in
`C:\Scripts\Temp\claude\D--Projects-Integra7AuralAlchemist\8c8d7f87-72b2-4a26-87a8-d5f4e2f3e26d\scratchpad\`
(`bulkcheck.ps1` for the settings swap and UIA, `libshot.ps1` for screenshots, `dupcheck.ps1` for the most
recent example). Write the exports to the scratchpad, never beside the user's library.

- [ ] **Step 1: Export all four formats** through the button, with nothing plugged in.

- [ ] **Step 2: Check the files, not the dialog**

1. Every file exists and is non-empty. The `.reabank`, the Cubase XML and the `.midnam` are UTF-8
   **without** a BOM; the `.csv` is UTF-8 **with** one (first three bytes `EF BB BF`), which is
   `WantsByteOrderMark` doing its job and not a defect.
2. The `.reabank` has 75 `Bank` lines and 6,023 patch lines; no line is a bare word; the first bank is
   `Bank 86 64 PCMD PRST (86/64)` (lowest address in the factory data — the parenthesised address is part
   of the bank's name, see trap 3).
3. The XML files parse — `[xml](Get-Content …)` in PowerShell, which is a real parser and will reject an
   unescaped `&`.
4. The `.csv` opens with 6,024 lines counting the header, and every quoted field closes.
5. The program numbers start at **0**, not 1: grep the `.reabank` for `^0 Full Grand 1` in bank `89 64`.
6. `Woodblock` and `Castanets` both appear in every file, and the status line said so — naming
   **program 115**, which is the same number the files carry.
7. A name with a non-ASCII character survives the round trip — one of the 84 carrying a curly apostrophe
   (`‘76 Pure`, `‘73 Tine`) is the case to pick, since those are the names the CSV's byte-order mark exists
   for. Find it in the CSV first, then look for it in the other three.

- [ ] **Step 3: The dialog itself** — cancelling the format picker writes nothing; cancelling the save dialog
writes nothing and says nothing alarming; picking each format suggests a file name with the right extension.

- [ ] **Step 4: Report** what was seen for each, with a screenshot of the format picker, and paste the first
fifteen lines of the `.reabank` and of the `.midnam`.

---

## Verification by hand (user)

- [ ] Reaper reads the `.reabank` and a track's program dropdown shows the tone names.
- [ ] Ardour or Mixbus reads the `.midnam`.
- [ ] Cubase or Nuendo reads the XML — **this is the one format that could not be verified without you**.
- [ ] With the instrument connected, the export includes the user-memory tones and labels them USER.
