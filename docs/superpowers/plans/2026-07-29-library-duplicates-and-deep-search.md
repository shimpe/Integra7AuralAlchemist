# Library duplicates and deep search — implementation plan (phase 4 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** find the patches that are the same sound saved more than once, and search inside patches rather
than only their names.

**Architecture:** two more forward-only readers alongside `SnapshotHead` — one matches a substring against
the displayed values a file already stores, the other collects a packed vector of raw values — plus one pure
grouping function over those vectors. Nothing is parsed into objects, and nothing is cached on disk.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI 24, NUnit 4.

**Spec:** `docs/superpowers/specs/2026-07-29-library-overhaul-design.md`, the "Phase 4" section and the
"Architecture" section. Read both before starting; the architecture section explains why these are readers
rather than a parse.

**Phase 4 of five.** Phases 1–3 (version history, bulk operations, audition) are merged. Phase 5, the DAW
patch list, is a separate plan.

---

## The file shape both readers walk

A snapshot is metadata and then one deeply nested `Blocks` object. This is the whole of what the readers
need to know, and it was read off a real library file rather than inferred:

```json
{
  "FormatVersion": 3, "Name": "Full Grand 1", "Kind": "tone", "ToneType": "SN-A",
  "Category": "Ac.Piano", "Tags": [], "Notes": "", "Rating": 0, "Favourite": false,
  "Blocks": {
    "Temporary Tone Part 1": {
      "Offset/Temporary SuperNATURAL Acoustic Tone": {
        "Offset2/SuperNATURAL Acoustic Tone Common": {
          "SuperNATURAL Acoustic Tone Common": {
            "Tone Name": "Full Grand 1",
            "Reserved1": " ",
            "Tone Level": [127, "127"],
            "Mono-Poly": [1, "Poly"]
          }
        }
      }
    }
  }
}
```

- **Five levels of object** inside `Blocks` before the leaves: start address, offset, offset2, block name,
  then the parameters.
- **A leaf is either a bare string** — a text parameter such as a tone name, which has no raw value — **or a
  two-element array** `[raw, "displayed"]`.
- **The full parameter path** is the block name, a slash, and the leaf name:
  `SuperNATURAL Acoustic Tone Common/Tone Level`.

`SnapshotHead` already walks the metadata and calls `reader.Skip()` on `Blocks`. These two readers do the
opposite: skip nothing, interpret only the half they need, and build no objects.

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

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **982 passed, 0 failed**.

**Traps this project has actually hit**, all of which apply here:

- **An XML comment may not contain `--`**, and **a comment may not sit between an element's attributes**.
  The first makes MSBuild fail to *load* the project (`MSB4025`), so nothing compiles and the error count
  reads as zero. Check for `MSB4025` before believing a sudden green. Prose uses real em dashes.
- **Never hardcode a colour in XAML.** Use `{StaticResource ...}`.
- **A `ToolTip` is a popup and swallows clicks on its own control.**
- **Do not edit `.axaml` with `sed` or rewrite source through PowerShell** — CRLF with a BOM, and
  PowerShell 5.1's `Set-Content` defaults to ANSI.
- Compiled bindings are checked at build time; `AVLN2000` means a binding names a member that does not exist.
- **A view model cannot be constructed in a test** under ReactiveUI 24, so anything worth testing goes in a
  service.
- **A leading byte-order mark must come off first.** `Utf8JsonReader` does not skip one and an editor that
  re-saved a snapshot may well have added one — use `ByteOrderMark.SkipIn`, as `SnapshotHead` does.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/library-duplicates`, which already holds this plan. Explicit paths only; never
`git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do not merge or push.

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `Src/Models/Services/SnapshotTextScan.cs` | Does any displayed value contain this text, and which |
| Create `Src/Models/Services/SnapshotRawVector.cs` | Kind, engine, and a packed vector of raw values |
| Create `Src/Models/Services/DuplicateGroups.cs` | Vectors plus a threshold to groups |
| Create `Src/ViewModels/DuplicateScanViewModel.cs` | The duplicates panel |
| Create `Src/Views/DuplicateScanView.axaml` (+ `.axaml.cs`) | Its markup |
| Modify `Src/ViewModels/LibraryViewModel.cs` | The deep-search pass and the vector cache |
| Modify `Src/Views/LibraryView.axaml` | The checkbox, and the panel |

**New tests:** `Tests/TestSnapshotTextScan.cs`, `Tests/TestSnapshotRawVector.cs`,
`Tests/TestDuplicateGroups.cs`.

---

### Task 1: `SnapshotTextScan`

**Files:** Create `Src/Models/Services/SnapshotTextScan.cs`; Test `Tests/TestSnapshotTextScan.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Searching inside a patch. The file already stores what each parameter reads as on screen, so
/// this matches against that and never consults the parameter database.</summary>
public class SnapshotTextScanTests
{
    /// <summary>A snapshot with two blocks: one text parameter, two with raw values. Written as JSON rather
    /// than built through the model, because what is being tested is a reader.</summary>
    private const string Json = """
    {
      "FormatVersion": 3, "Name": "Warm Rhodes", "Kind": "tone", "ToneType": "SN-S",
      "Category": "E.Piano", "Tags": [], "Notes": "", "Rating": 0, "Favourite": false,
      "Blocks": {
        "Temporary Tone Part 1": {
          "Offset/Temporary SuperNATURAL Synth Tone": {
            "Offset2/SuperNATURAL Synth Tone Common": {
              "SuperNATURAL Synth Tone Common": {
                "Tone Name": "Warm Rhodes",
                "Tone Level": [127, "127"]
              }
            },
            "Offset2/SuperNATURAL Synth Tone Partial 1": {
              "SuperNATURAL Synth Tone Partial": {
                "OSC Wave": [6, "SuperSaw"]
              }
            }
          }
        }
      }
    }
    """;

    private static Stream Of(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Test]
    public void A_displayed_value_is_found_and_the_parameter_is_named()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "supersaw");

        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Path, Is.EqualTo("SuperNATURAL Synth Tone Partial/OSC Wave"));
        Assert.That(hit.Value.Value, Is.EqualTo("SuperSaw"));
    }

    /// <summary>Ordinal, ignoring case -- LibraryFilter's rule, so that the same library searches the same
    /// way on every machine.</summary>
    [Test]
    public void Matching_ignores_case()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "SUPERSAW"), Is.Not.Null);
    }

    /// <summary>A text parameter has no raw half and is stored as a bare string. It is still a value the
    /// user can see, so it is still searchable.</summary>
    [Test]
    public void A_text_parameter_is_searched_too()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "rhodes");

        Assert.That(hit!.Value.Path, Is.EqualTo("SuperNATURAL Synth Tone Common/Tone Name"));
    }

    [Test]
    public void Nothing_matching_answers_null()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "trumpet"), Is.Null);
    }

    /// <summary>The metadata is the list's business, not this reader's: searching it is what LibraryFilter
    /// already does over the heads, and matching it here as well would make the same entry hit twice and
    /// report a parameter that does not exist.</summary>
    [Test]
    public void The_name_and_the_category_outside_Blocks_are_not_searched()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "E.Piano"), Is.Null,
            "the category is metadata, matched by LibraryFilter over the head");
    }

    /// <summary>A file that is not a snapshot, or not JSON, is passed over rather than throwing -- the same
    /// contract SnapshotHead has, and for the same reason: a library folder is a folder.</summary>
    [Test]
    public void Something_that_is_not_json_is_not_a_match()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of("this is not JSON"), "anything"), Is.Null);
    }

    /// <summary>An editor that re-saved a snapshot may have added a byte order mark. Utf8JsonReader does
    /// not skip one.</summary>
    [Test]
    public void A_byte_order_mark_does_not_hide_a_match()
    {
        var marked = new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Json)]);

        Assert.That(SnapshotTextScan.FirstMatch(marked, "supersaw"), Is.Not.Null);
    }
}
```

- [ ] **Step 2: Run and watch it fail.** Expected: `CS0103`, `SnapshotTextScan` does not exist.

- [ ] **Step 3: Implement**

Read `Src/Models/Services/SnapshotHead.cs` in full first: this is the same technique, and its remarks
explain why the walk is affordable where a parse is not.

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Whether any parameter of a snapshot reads as something, and which one.
///
/// <b>The file already stores what every parameter shows on screen</b> -- a leaf is either a bare string,
/// for a text parameter, or <c>[raw, "displayed"]</c> -- so searching inside a patch is a substring test
/// against text that is already on disk. Nothing here consults the parameter database and nothing is
/// rendered.
///
/// <b>Built like <see cref="SnapshotHead"/> and for the same reason</b>: a forward-only walk that
/// interprets one primitive at a time and materialises nothing. Where that one skips <c>Blocks</c> whole,
/// this one walks into it and skips the metadata instead.
///
/// <b>It stops at the first hit.</b> The caller wants to know whether this file matches and what to show as
/// the reason; a second match would not change either answer.
///
/// A file that is not a snapshot, or not JSON at all, is simply not a match. A library folder is a folder,
/// and the user can and will put other things in it -- the same contract the listing has.</summary>
public static class SnapshotTextScan
{
    /// <summary>The first parameter whose displayed value contains <paramref name="text"/>, or null.
    /// Ordinal and ignoring case, matching <see cref="LibraryFilter"/>: the same library must search the
    /// same way on every machine, and nobody searching their own sounds is thinking about capitals.</summary>
    public static (string Path, string Value)? FirstMatch(Stream json, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        using var buffer = new MemoryStream();
        json.CopyTo(buffer);

        try
        {
            return Match(ByteOrderMark.SkipIn(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)), text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Path, string Value)? Match(ReadOnlySpan<byte> utf8, string text)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) return null;

            var property = reader.GetString()!;
            reader.Read();

            // Everything except Blocks is metadata, which LibraryFilter already searches over the heads.
            // Matching it here as well would hit the same entry twice and name a parameter that does not
            // exist.
            if (property != "Blocks") { reader.Skip(); continue; }

            return MatchInBlocks(ref reader, text);
        }

        return null;
    }

    /// <summary>Walk the five levels of object inside Blocks -- start, offset, offset2, block, parameters --
    /// and test every leaf. The block name is the level whose children are the parameters, which is what
    /// makes the path "block/leaf" without having to know any of the addresses above it.</summary>
    private static (string Path, string Value)? MatchInBlocks(ref Utf8JsonReader reader, string text)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return null;

        // Depth is counted rather than the levels being named, because naming them would be a second place
        // that has to agree with the writer about how deep the nesting is.
        var blockDepth = reader.CurrentDepth + 4;
        var blockName = "";

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth <= blockDepth - 4) break;

            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var name = reader.GetString()!;
            if (reader.CurrentDepth == blockDepth) { blockName = name; continue; }
            if (reader.CurrentDepth != blockDepth + 1) continue;

            reader.Read();
            var value = ValueOf(ref reader);
            if (value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase))
                return ($"{blockName}/{name}", value);
        }

        return null;
    }

    /// <summary>What a leaf reads as: itself when it is a bare string, and the second element when it is
    /// the <c>[raw, "displayed"]</c> pair. Anything else is stepped over rather than guessed at.</summary>
    private static string? ValueOf(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();

        if (reader.TokenType != JsonTokenType.StartArray) { reader.Skip(); return null; }

        string? displayed = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            if (reader.TokenType == JsonTokenType.String)
                displayed = reader.GetString();

        return displayed;
    }
}
```

**`ByteOrderMark.SkipIn` takes a span and answers one** — confirm its exact signature in
`Src/Models/Services/` before relying on it; `SnapshotHead` line 95 is a working call.

- [ ] **Step 4: Green, then the whole suite.** Expected: 7 in the filter, 989 overall.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnapshotTextScan.cs Tests/TestSnapshotTextScan.cs
git commit -m "feat: search inside a patch without parsing it"
```

---

### Task 2: `SnapshotRawVector`

**Files:** Create `Src/Models/Services/SnapshotRawVector.cs`; Test `Tests/TestSnapshotRawVector.cs`

- [ ] **Step 1: Write the failing tests**

Reuse the JSON fixture shape from task 1 — write it out again in this file rather than sharing it, because
the two readers must be able to disagree about a file and have the tests say so.

```csharp
using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The packed raw values a duplicate comparison works on.</summary>
public class SnapshotRawVectorTests
{
    private static string Json(string name, long level, long wave, string kind = "tone",
        string toneType = "SN-S") => $$"""
    {
      "FormatVersion": 3, "Name": "{{name}}", "Kind": "{{kind}}", "ToneType": "{{toneType}}",
      "Category": "", "Tags": [], "Notes": "", "Rating": 0, "Favourite": false,
      "Blocks": {
        "Temporary Tone Part 1": {
          "Offset/Temporary SuperNATURAL Synth Tone": {
            "Offset2/SuperNATURAL Synth Tone Common": {
              "SuperNATURAL Synth Tone Common": {
                "Tone Name": "{{name}}",
                "Reserved1": " ",
                "Tone Level": [{{level}}, "{{level}}"],
                "OSC Wave": [{{wave}}, "wave"]
              }
            }
          }
        }
      }
    }
    """;

    private static Stream Of(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Test]
    public void The_kind_and_the_engine_come_back_with_the_vector()
    {
        var vector = SnapshotRawVector.Read(Of(Json("a", 100, 6)));

        Assert.That(vector, Is.Not.Null);
        Assert.That(vector!.Kind, Is.EqualTo(SnapshotKinds.Tone));
        Assert.That(vector.ToneType, Is.EqualTo("SN-S"));
    }

    /// <summary>Only the raw halves, in document order. A text parameter has no raw half at all, and a
    /// reserved one is excluded for the reason the comparison report excludes it: it is filler.</summary>
    [Test]
    public void Only_the_raw_values_are_collected_and_reserved_ones_are_left_out()
    {
        var vector = SnapshotRawVector.Read(Of(Json("a", 100, 6)));

        Assert.That(vector!.Values, Is.EqualTo(new long[] { 100, 6 }));
    }

    /// <summary>The property everything downstream rests on: two files of the same engine produce vectors
    /// that line up position by position, so a comparison never has to match paths.</summary>
    [Test]
    public void Two_files_of_the_same_engine_produce_vectors_of_the_same_shape()
    {
        var a = SnapshotRawVector.Read(Of(Json("a", 100, 6)))!;
        var b = SnapshotRawVector.Read(Of(Json("b", 101, 7)))!;

        Assert.That(a.Values, Has.Length.EqualTo(b.Values.Length));
        Assert.That(a.Values, Is.Not.EqualTo(b.Values));
    }

    /// <summary>The name is not in the vector, so renaming a patch does not make it a different sound.
    /// That is the whole point: two files differing only in what has been said about them are duplicates.
    /// </summary>
    [Test]
    public void A_different_name_alone_produces_the_same_vector()
    {
        var a = SnapshotRawVector.Read(Of(Json("Warm Rhodes", 100, 6)))!;
        var b = SnapshotRawVector.Read(Of(Json("Bright Rhodes", 100, 6)))!;

        Assert.That(a.Values, Is.EqualTo(b.Values));
    }

    [Test]
    public void Something_that_is_not_a_snapshot_answers_null()
    {
        Assert.That(SnapshotRawVector.Read(Of("this is not JSON")), Is.Null);
    }

    [Test]
    public void A_byte_order_mark_does_not_prevent_a_read()
    {
        var marked = new MemoryStream([.. Encoding.UTF8.GetPreamble(),
            .. Encoding.UTF8.GetBytes(Json("a", 100, 6))]);

        Assert.That(SnapshotRawVector.Read(marked), Is.Not.Null);
    }
}
```

- [ ] **Step 2: Run and watch it fail.**

- [ ] **Step 3: Implement**

Same walk as task 1. The record and the shape:

```csharp
/// <summary>A snapshot reduced to what a duplicate comparison needs.</summary>
/// <param name="Kind">Tone or Studio Set. Part of the bucket key, so the two never pair.</param>
/// <param name="ToneType">The engine, or null for a Studio Set.</param>
/// <param name="Values">Every raw value, in document order, reserved ones left out.</param>
public sealed record RawVector(string Kind, string? ToneType, long[] Values);
```

`Read` walks exactly as `SnapshotTextScan.Match` does, except that it also reads `Kind` and `ToneType` from
the metadata, and in the leaf it takes the **first** element of the pair rather than the second, skipping a
bare string entirely. Reserved parameters are left out by the rule `SnapshotDiff` already uses — its
`IsReserved` is `path.Contains("Reserved")`, and the three shapes it covers are recorded in its remarks.
**Match that rule exactly**, and say in a comment that the two must agree.

Positional comparison is only sound because the same engine always yields the same sequence: the writer
emits blocks and parameters in a fixed order, text parameters are always absent from the vector and reserved
ones always excluded. Put that sentence in the class remarks — it is the assumption everything downstream
rests on.

- [ ] **Step 4: Green, then the whole suite.** Expected: 995 overall.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnapshotRawVector.cs Tests/TestSnapshotRawVector.cs
git commit -m "feat: reduce a snapshot to the raw values a duplicate check needs"
```

---

### Task 3: `DuplicateGroups`

**Files:** Create `Src/Models/Services/DuplicateGroups.cs`; Test `Tests/TestDuplicateGroups.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which patches are the same sound saved more than once.</summary>
public class DuplicateGroupsTests
{
    private static (string Path, RawVector Vector) Entry(string path, string engine, params long[] values) =>
        (path, new RawVector(SnapshotKinds.Tone, engine, values));

    [Test]
    public void Identical_vectors_are_a_group()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 1, 2, 3)], threshold: 0);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0], Is.EqualTo(new[] { "a.json", "b.json" }));
    }

    [Test]
    public void Nothing_alike_is_no_groups()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 9, 9, 9)], threshold: 1);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>The threshold is a count of differing parameters, and it is inclusive: "at most N".</summary>
    [Test]
    public void The_threshold_is_inclusive()
    {
        var pair = new[] { Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 1, 2, 9) };

        Assert.That(DuplicateGroups.Find(pair, threshold: 1), Has.Count.EqualTo(1));
        Assert.That(DuplicateGroups.Find(pair, threshold: 0), Is.Empty);
    }

    /// <summary>Different engines are never compared: the same position in two engines' vectors is two
    /// different parameters, so the count would be meaningless even where the lengths happened to match.
    /// </summary>
    [Test]
    public void Engines_are_never_mixed()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "PCMS", 1, 2, 3)], threshold: 0);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>And neither are a tone and a Studio Set, which is what the kind is in the key for.</summary>
    [Test]
    public void A_tone_and_a_studio_set_never_pair()
    {
        var groups = DuplicateGroups.Find(
            [("a.json", new RawVector(SnapshotKinds.Tone, "SN-S", [1, 2])),
             ("b.json", new RawVector(SnapshotKinds.StudioSet, null, [1, 2]))], threshold: 0);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>Grouping is transitive, and deliberately: A is near B and B is near C, so all three are one
    /// group even though A and C differ by more than the threshold. The panel says "each differs in at most
    /// N from at least one other here" rather than implying every pair is alike.</summary>
    [Test]
    public void Grouping_is_transitive()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 0, 0), Entry("b.json", "SN-S", 1, 0), Entry("c.json", "SN-S", 1, 1)],
            threshold: 1);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0], Has.Count.EqualTo(3));
    }

    /// <summary>Vectors of different lengths are the same engine written by two builds of this
    /// application, one of which knew a parameter the other did not. Comparing them positionally would
    /// line up the wrong parameters from the first difference onwards, so they are simply not compared.
    /// </summary>
    [Test]
    public void Vectors_of_different_lengths_are_not_compared()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2), Entry("b.json", "SN-S", 1, 2, 3)], threshold: 5);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public void An_empty_library_has_no_groups()
    {
        Assert.That(DuplicateGroups.Find([], threshold: 5), Is.Empty);
    }

    /// <summary>Order is fixed so that two scans of the same folder present the same list. Within a group,
    /// by path; between groups, by the first path.</summary>
    [Test]
    public void Groups_and_their_members_are_in_a_stable_order()
    {
        var groups = DuplicateGroups.Find(
            [Entry("z.json", "SN-S", 5, 5), Entry("m.json", "SN-S", 5, 5),
             Entry("a.json", "SN-S", 9, 9), Entry("b.json", "SN-S", 9, 9)], threshold: 0);

        Assert.That(groups[0], Is.EqualTo(new[] { "a.json", "b.json" }));
        Assert.That(groups[1], Is.EqualTo(new[] { "m.json", "z.json" }));
    }
}
```

- [ ] **Step 2: Run and watch it fail.**

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which snapshots are the same sound saved more than once.
///
/// <b>Near, not identical, and that is the case worth catching.</b> Exact duplicates happen -- a file copied
/// in twice -- but the complaint this answers is the sound saved four times while it was being edited, and
/// those differ by a handful of parameters. So the measure is a count of differing values and the user sets
/// the bar.
///
/// <b>Buckets first.</b> Nothing is compared across a kind, an engine or a vector length. The same position
/// in two engines' vectors is two different parameters, so a count across them would be a number with no
/// meaning; two lengths of the same engine are two builds of this application, one of which knew a
/// parameter the other did not, and lining those up positionally would mismatch everything after the first
/// difference. Bucketing is also what makes the pairwise comparison affordable -- that, and abandoning a
/// pair the moment it passes the threshold.
///
/// <b>Grouping is transitive, deliberately.</b> A near B and B near C puts all three together even where A
/// and C differ by more than the threshold. The alternative -- only reporting pairs -- would show the same
/// patch in three rows and leave the user to work out it was one family. What the panel must therefore say
/// is "each differs in at most N from at least one other here", not that every pair is alike.</summary>
public static class DuplicateGroups
{
    /// <summary>The groups, each two or more paths. Ordered so that two scans of one folder present the
    /// same list: within a group by path, and between groups by their first path.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Find(
        IReadOnlyList<(string Path, RawVector Vector)> entries, int threshold)
    {
        List<List<string>> groups = [];

        foreach (var bucket in entries.GroupBy(e => (e.Vector.Kind, e.Vector.ToneType, e.Vector.Values.Length)))
        {
            var members = bucket.ToList();

            // Which group each member has been put in, or -1. Small enough that a map beats a union-find.
            var groupOf = new int[members.Count];
            Array.Fill(groupOf, -1);
            List<List<string>> inBucket = [];

            for (var i = 0; i < members.Count; i++)
            for (var j = i + 1; j < members.Count; j++)
            {
                if (!Alike(members[i].Vector.Values, members[j].Vector.Values, threshold)) continue;

                if (groupOf[i] < 0 && groupOf[j] < 0)
                {
                    groupOf[i] = groupOf[j] = inBucket.Count;
                    inBucket.Add([members[i].Path, members[j].Path]);
                }
                else if (groupOf[i] < 0)
                {
                    groupOf[i] = groupOf[j];
                    inBucket[groupOf[j]].Add(members[i].Path);
                }
                else if (groupOf[j] < 0)
                {
                    groupOf[j] = groupOf[i];
                    inBucket[groupOf[i]].Add(members[j].Path);
                }
                else if (groupOf[i] != groupOf[j])
                {
                    // Two families turn out to be one. Merged into the lower index, and the higher one is
                    // left empty rather than removed, so the indices already handed out stay valid.
                    var (into, from) = (Math.Min(groupOf[i], groupOf[j]), Math.Max(groupOf[i], groupOf[j]));
                    inBucket[into].AddRange(inBucket[from]);
                    inBucket[from] = [];
                    for (var k = 0; k < groupOf.Length; k++)
                        if (groupOf[k] == from)
                            groupOf[k] = into;
                }
            }

            groups.AddRange(inBucket.Where(g => g.Count > 1));
        }

        foreach (var group in groups) group.Sort(StringComparer.OrdinalIgnoreCase);

        return [.. groups.OrderBy(g => g[0], StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether two vectors differ in at most <paramref name="threshold"/> positions.
    ///
    /// <b>It gives up as soon as it knows the answer is no</b>, which is what makes comparing every pair in
    /// a bucket acceptable: two patches that are nothing like each other cost a handful of comparisons
    /// rather than fifteen hundred.</summary>
    private static bool Alike(long[] a, long[] b, int threshold)
    {
        var differences = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (++differences > threshold) return false;
        }

        return true;
    }
}
```

- [ ] **Step 4: Green, then the whole suite.** Expected: 1004 overall.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/DuplicateGroups.cs Tests/TestDuplicateGroups.cs
git commit -m "feat: group the patches that are the same sound saved twice"
```

---

### Task 4: deep search

**Files:** Modify `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`

No tests — view model. Verification is the build, the suite, and task 6.

- [ ] **Step 1: The checkbox**

A `[Reactive] private bool _searchInsidePatches;` on `LibraryViewModel`, and beside the search box in
`LibraryView.axaml`:

```xml
            <CheckBox IsChecked="{Binding SearchInsidePatches, Mode=TwoWay}"
                      Content="Look inside patches" />
```

**Do not add it to the `WhenAnyValue` that re-filters on every keystroke.** It reads files; it runs when the
user asks. Bind Enter in the search box, or give it its own Search button — say in your report which you
chose and why.

- [ ] **Step 2: The pass**

In `ApplyFilter`, after the ordinary filter has produced its admitted list:

```csharp
        // The deep pass widens the text axis and nothing else. An entry is admitted when it passes every
        // other axis AND the text matches its metadata OR any of its parameter values -- so ticking the box
        // can only ever add rows, which is what a user expects of a checkbox that says "look inside
        // patches too". LibraryFilter is asked twice for exactly this reason and stays pure over heads.
```

Ask `LibraryFilter` a second time with `Text` blanked, scan the difference between that set and the admitted
one, and union the hits. Keep the matched parameter against the row so it can be shown.

**This is a folder read**, so it goes off the UI thread — `Task.Run` around the scan, the report and the
list rebuild back on the UI thread, exactly as phase 2's bulk loops do.

- [ ] **Step 3: Show why a row matched**

`LibraryEntryViewModel` gains a `[Reactive] private string _matchedInside = "";` set by the pass, and the
list gains a column or the row shows it under the name. Keep it out of the way when empty.

- [ ] **Step 4: Build, run the suite, commit**

```bash
git add Src/ViewModels/LibraryViewModel.cs Src/ViewModels/LibraryEntryViewModel.cs Src/Views/LibraryView.axaml
git commit -m "feat: search inside patches, not only their names"
```

---

### Task 5: the duplicates panel

**Files:** Create `Src/ViewModels/DuplicateScanViewModel.cs`, `Src/Views/DuplicateScanView.axaml` (+
`.axaml.cs`); Modify `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`

- [ ] **Step 1: The view model**

Holds a threshold (default **5**), a `Scan` command, an `ObservableCollection` of groups each holding rows
with a checkbox, a summary, and commands for **Delete ticked** and **Compare these two** (enabled only when
exactly two rows are ticked, and handed to the same callback phase 2 added for the library's own pair
compare).

**The cache.** One dictionary on `LibraryViewModel`, `path → (lastWriteTime, length, RawVector)`. A scan
re-reads only the files whose timestamp or size has changed. Deliberately not on disk — `SnapshotLibrary`'s
remarks record why the library has no index, and an in-memory cache cannot outlive the process so it cannot
be wrong across runs.

**The scan runs off the UI thread** with the folder read and the grouping both inside the `Task.Run`.

- [ ] **Step 2: The view**

A panel with the threshold, a Scan button, the groups, and the two actions. Say on it what a group means:
*"Each of these differs in at most N parameters from at least one other in the group."*

- [ ] **Step 3: Where it lives**

A third panel beside the editor and the bulk panel, shown when the user asks for it — a "Find duplicates…"
button in the folder row is the cheapest way in, matching where Change and Refresh already are.

- [ ] **Step 4: Build, run the suite, commit**

```bash
git add Src/ViewModels/DuplicateScanViewModel.cs Src/Views/DuplicateScanView.axaml Src/Views/DuplicateScanView.axaml.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryView.axaml
git commit -m "feat: find the patches saved more than once"
```

---

### Task 6: verify by driving it

**Files:** none.

Use the harness pattern from phases 1–3: point the library folder at a throwaway directory by writing the
settings file and restoring it in a `finally`; select rows through UI Automation rather than synthetic mouse
clicks. **Never point a check at the user's own library.**

- [ ] **Step 1: Build a folder that has a known answer**

Copy one library tone three times: unchanged, with one raw value edited, and with five edited. Copy a second
tone once. That folder has exactly one group of three at a threshold of 5, one group of two at 1, and none
at 0.

- [ ] **Step 2: Walk the checks**

1. Scanning at threshold 0 finds only the identical pair.
2. At 5 the three-file group appears, and the summary says what a group means.
3. A tone of another engine never appears in a group with these.
4. Ticking two rows enables Compare these two; pressing it fills both slots of the Compare tab.
5. Deleting ticked rows removes those files and leaves copies in `.history`.
6. Deep search off: a term that appears only inside a patch finds nothing.
7. Deep search on and Enter: it finds the patch, and the row says which parameter matched.
8. Deep search on with a rating filter that excludes the match: still nothing, because the other axes narrow.

- [ ] **Step 3: Report** what was seen for each, with a screenshot of the duplicates panel.

---

## Verification by hand (user)

- [ ] A scan of the real library finds the patches you know you saved twice, and not much else.
- [ ] The threshold behaves: raising it groups more, lowering it groups less.
- [ ] Searching "supersaw" with the box ticked finds the patches that use it.
- [ ] The scan is quick enough to be worth pressing twice.
