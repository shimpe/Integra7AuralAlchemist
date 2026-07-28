# Snapshot diff and text export — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** compare two snapshots — saved files, or what the instrument holds right now — and let the
answer leave the application as plain text.

**Architecture:** two pure services do all the thinking (`SnapshotDiff` computes a comparison from two
`Integra7Snapshot` values; `ComparisonText` renders one). A new top-level Compare tab holds two slots and
the result. Nothing here writes to the instrument.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI source generators, NUnit 3.

**Spec:** `docs/superpowers/specs/2026-07-28-snapshot-diff-and-text-export-design.md`. Read it first; it
records why each decision is what it is.

---

## Conventions for every task

**Build and test with the user-local SDK** — the system `dotnet` is 8/9 and too old. `Src/bin` is
routinely locked by the user's own running application or Rider's Avalonia previewer; **never kill
either**, redirect the output instead. The four-deep path and the junction are both load-bearing, because
several tests find `Src\Assets\parameters.bin` by walking `..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` must come **before** `-p:OutputPath`, or `dotnet test` silently runs everything. The suite
stands at **827 passed, 0 failed**.

**XAML rules that fail the build:** never hardcode a colour (`{StaticResource ...}`); an em dash in prose
must be the character `—`, because a literal `--` inside an XML comment is illegal; compiled bindings are
checked at build time and a wrong member name is `AVLN2000`.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/snapshot-diff-and-text-export`. Stage explicit paths only — never `git add -A`
or `git add .`, and never stage `Src/Assets/new-icon-orig.svg`, which is the user's own untracked file.
Never `--no-verify`. Do not merge and do not push.

---

## File structure

**New — pure services, fully unit-tested:**

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/SnapshotDiff.cs` | The comparison: what differs, what is on one side only |
| `Src/Models/Services/ComparisonText.cs` | Rendering one comparison as plain text |

**New — UI:**

| File | Responsibility |
| --- | --- |
| `Src/ViewModels/CompareViewModel.cs` | Two slots, the compare command, the search box, the exports |
| `Src/Views/CompareView.axaml` (+ `.axaml.cs`) | The Compare tab |

**Modified:**

| File | Change |
| --- | --- |
| `Src/ViewModels/MainWindowViewModel.cs` | Owns `CompareVm`, supplies its callbacks, adds a save-text interaction |
| `Src/Views/MainWindow.axaml` | The new `TabItem` |
| `Src/Views/MainWindow.axaml.cs` | Handlers for the save-text picker and the clipboard |
| `Src/ViewModels/LibraryViewModel.cs` | A `CompareThis` command handing the selected entry to a callback |
| `Src/Views/LibraryView.axaml` | The button for it |

**New tests:** `Tests/TestSnapshotDiff.cs`, `Tests/TestComparisonText.cs`.

---

### Task 1: The comparison

**Files:**
- Create: `Src/Models/Services/SnapshotDiff.cs`
- Test: `Tests/TestSnapshotDiff.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestSnapshotDiff.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What counts as a difference, and what counts as an answer rather than a failure.
///
/// The whole point of these is the raw-value rule: format v2 added the raw value for exactly this, and a
/// comparison that used the display string would report every parameter of a renamed enum as changed, in
/// every comparison, for ever.</summary>
public class SnapshotDiffTests
{
    private const string Start = "Temporary Tone Part 1";
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Common = "Offset2/SuperNATURAL Synth Tone Common";
    private const string Partial = "Offset2/SuperNATURAL Synth Tone Partial 1";

    private static Integra7Snapshot Tone(string name, params SnapshotDomain[] domains) =>
        new(Integra7Snapshot.CurrentFormatVersion, name, [.. domains], SnapshotKinds.Tone, "SN-S");

    private static SnapshotDomain Block(string offset2, params SnapshotValue[] values) =>
        new(Start, Offset, offset2, [.. values]);

    private static SnapshotDomain BlockIn(string start, string offset2, params SnapshotValue[] values) =>
        new(start, Offset, offset2, [.. values]);

    [Test]
    public void Two_identical_snapshots_differ_in_nothing()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
        Assert.That(result.Blocks, Is.Empty, "a block with nothing to report is not listed");
        Assert.That(result.ParametersCompared, Is.EqualTo(1));
    }

    [Test]
    public void A_changed_value_is_reported_with_both_sides_as_the_user_reads_them()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "118", 118)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
        var difference = result.Blocks.Single().Differences.Single();
        Assert.That(difference.Path, Is.EqualTo("Tone/Level"));
        Assert.That(difference.LeftValue, Is.EqualTo("100"));
        Assert.That(difference.RightValue, Is.EqualTo("118"));
    }

    /// <summary>The reason the raw value is in the file. Renaming an enum label -- "Low pass" to "LPF" --
    /// must not turn every filter in the library into a difference.</summary>
    [Test]
    public void A_renamed_label_over_the_same_raw_value_is_not_a_difference()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 1)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Filter Mode", "LPF", 1)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
    }

    /// <summary>And the converse: the same string over a different raw value is a real difference, which
    /// a string comparison would have missed.</summary>
    [Test]
    public void The_same_label_over_a_different_raw_value_is_a_difference()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 1)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 5)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
    }

    /// <summary>A text parameter's value IS its string -- it carries no raw -- so for it the strings are
    /// the comparison rather than a fallback.</summary>
    [Test]
    public void A_text_parameter_is_compared_on_its_string()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Tone Name", "Warm Rhodes")));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Tone Name", "Glass Pad")));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
    }

    /// <summary>Start says which part a tone was captured from and nothing about the sound. Matching on
    /// it would make a tone captured from part 3 differ from the same tone in part 5 in every
    /// parameter.</summary>
    [Test]
    public void The_same_tone_captured_from_two_different_parts_does_not_differ()
    {
        var one = Tone("a", BlockIn("Temporary Tone Part 3", Common,
            new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", BlockIn("Temporary Tone Part 5", Common,
            new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
    }

    [Test]
    public void A_studio_set_against_a_tone_is_refused_and_the_message_names_both()
    {
        var tone = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var studioSet = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "b",
            [new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
                [new SnapshotValue("Studio Set Common/Studio Set Tempo", "120", 120)])]);

        var e = Assert.Throws<SnapshotFormatException>(() => SnapshotDiff.Compare(studioSet, tone));

        Assert.That(e!.Message, Does.Contain(SnapshotKinds.StudioSet));
        Assert.That(e.Message, Does.Contain(SnapshotKinds.Tone));
    }

    [Test]
    public void Two_tones_of_different_engines_are_refused_and_the_message_names_both()
    {
        var sns = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var pcm = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "b",
            [new SnapshotDomain(Start, "Offset/Temporary PCM Synth Tone",
                "Offset2/PCM Synth Tone Common",
                [new SnapshotValue("PCM Synth Tone Common/PCM Synth Tone Level", "100", 100)])],
            SnapshotKinds.Tone, "PCMS");

        var e = Assert.Throws<SnapshotFormatException>(() => SnapshotDiff.Compare(sns, pcm));

        Assert.That(e!.Message, Does.Contain("SN-S"));
        Assert.That(e.Message, Does.Contain("PCMS"));
    }

    /// <summary>An older file, or one from a build that has since gained a parameter. A real answer, and
    /// refusing it would make exactly the snapshots most worth comparing uncomparable.</summary>
    [Test]
    public void A_path_on_one_side_only_is_reported_rather_than_thrown()
    {
        var one = Tone("a", Block(Common,
            new SnapshotValue("Tone/Level", "100", 100),
            new SnapshotValue("Tone/Only Here", "1", 1)));
        var two = Tone("b", Block(Common,
            new SnapshotValue("Tone/Level", "100", 100),
            new SnapshotValue("Tone/Only There", "2", 2)));

        var result = SnapshotDiff.Compare(one, two);

        var block = result.Blocks.Single();
        Assert.That(block.Differences, Is.Empty);
        Assert.That(block.PathsOnlyOnLeft, Is.EqualTo(new[] { "Tone/Only Here" }));
        Assert.That(block.PathsOnlyOnRight, Is.EqualTo(new[] { "Tone/Only There" }));
        Assert.That(result.Identical, Is.False, "the two are not the same snapshot");
        Assert.That(result.ParametersCompared, Is.EqualTo(1), "only the path both sides carry");
    }

    [Test]
    public void A_block_on_one_side_only_is_reported_rather_than_thrown()
    {
        var one = Tone("a",
            Block(Common, new SnapshotValue("Tone/Level", "100", 100)),
            Block(Partial, new SnapshotValue("Partial/Cutoff", "127", 127)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.BlocksOnlyOnLeft, Is.EqualTo(new[] { Partial }));
        Assert.That(result.BlocksOnlyOnRight, Is.Empty);
        Assert.That(result.Identical, Is.False);
    }

    /// <summary>Reproducible, and in the order everything else in the application shows these values:
    /// the left snapshot's block order, address order within a block.</summary>
    [Test]
    public void Blocks_and_values_keep_the_left_snapshots_order()
    {
        var one = Tone("a",
            Block(Partial, new SnapshotValue("Partial/B", "1", 1), new SnapshotValue("Partial/A", "1", 1)),
            Block(Common, new SnapshotValue("Tone/Level", "1", 1)));
        var two = Tone("b",
            Block(Common, new SnapshotValue("Tone/Level", "2", 2)),
            Block(Partial, new SnapshotValue("Partial/A", "2", 2), new SnapshotValue("Partial/B", "2", 2)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Blocks.Select(b => b.Offset2), Is.EqualTo(new[] { Partial, Common }));
        Assert.That(result.Blocks[0].Differences.Select(d => d.Path),
            Is.EqualTo(new[] { "Partial/B", "Partial/A" }));
    }

    [Test]
    public void A_block_listed_twice_is_refused()
    {
        var one = Tone("a",
            Block(Common, new SnapshotValue("Tone/Level", "1", 1)),
            Block(Common, new SnapshotValue("Tone/Level", "2", 2)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "1", 1)));

        Assert.That(() => SnapshotDiff.Compare(one, two), Throws.TypeOf<SnapshotFormatException>());
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter SnapshotDiffTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error — `SnapshotDiff` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/SnapshotDiff.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter that differs, with both sides as the user reads them. The raw values decided
/// that it differs (see <see cref="SnapshotDiff"/>); these strings are what gets shown.</summary>
public sealed record ValueDifference(string Path, string LeftValue, string RightValue);

/// <summary>What differs within one block, and what exists in only one side of it.</summary>
public sealed record BlockDifference(
    string Offset,
    string Offset2,
    IReadOnlyList<ValueDifference> Differences,
    IReadOnlyList<string> PathsOnlyOnLeft,
    IReadOnlyList<string> PathsOnlyOnRight)
{
    /// <summary>The block's name without the address prefix -- "SuperNATURAL Synth Tone Common" rather
    /// than "Offset2/SuperNATURAL Synth Tone Common". What a heading should say.</summary>
    public string Name => Offset2.StartsWith("Offset2/", System.StringComparison.Ordinal)
        ? Offset2["Offset2/".Length..]
        : Offset2;

    public bool HasAnything =>
        Differences.Count > 0 || PathsOnlyOnLeft.Count > 0 || PathsOnlyOnRight.Count > 0;
}

/// <summary>A whole comparison. Blocks with nothing to report are absent, which is what keeps a Studio
/// Set comparison readable: 53 blocks in, usually a handful out.</summary>
public sealed record SnapshotComparison(
    string LeftName,
    string RightName,
    IReadOnlyList<BlockDifference> Blocks,
    int ParametersCompared,
    IReadOnlyList<string> BlocksOnlyOnLeft,
    IReadOnlyList<string> BlocksOnlyOnRight)
{
    public int DifferenceCount => Blocks.Sum(b => b.Differences.Count);

    /// <summary>Nothing to report at all. A path or block present on only one side counts against this:
    /// the two are not the same snapshot, and calling them identical would be wrong in the way that
    /// matters.</summary>
    public bool Identical =>
        DifferenceCount == 0 && BlocksOnlyOnLeft.Count == 0 && BlocksOnlyOnRight.Count == 0 &&
        Blocks.All(b => b.PathsOnlyOnLeft.Count == 0 && b.PathsOnlyOnRight.Count == 0);
}

/// <summary>What differs between two snapshots.
///
/// <b>Difference is decided on the raw value, not on the displayed string.</b> The raw value is what the
/// device stores; the string is a rendering of it. A build that renames an enum label -- "Low pass" to
/// "LPF" -- would otherwise report every parameter of that type as changed, in every comparison, for
/// ever. Where either side carries no raw value the strings are compared, which for a text parameter is
/// not a fallback but the right question: its value *is* its string.
///
/// <b>Blocks are matched on (Offset, Offset2), never on Start.</b> Start encodes which part a tone sat in
/// when it was captured, so matching on it would make a tone captured from part 3 differ from the
/// identical tone in part 5 in every single parameter. <c>StudioSetSnapshotService.RestoreToneAsync</c>
/// re-targets on exactly this reasoning; this is the same fact from the other side.
///
/// Pure -- two snapshots in, one comparison out, no domain, no parameter database, no device -- which is
/// what lets every rule above be tested. Capturing from the instrument is the caller's job, before this
/// is called.</summary>
public static class SnapshotDiff
{
    public static SnapshotComparison Compare(Integra7Snapshot left, Integra7Snapshot right)
    {
        // Refused rather than compared: two snapshots of different kinds share no blocks at all, so
        // "everything differs" is technically true and tells the user nothing.
        if (left.Kind != right.Kind)
            throw new SnapshotFormatException(
                $"These cannot be compared: one holds \"{left.Kind}\" and the other \"{right.Kind}\".");

        if (left.ToneType != right.ToneType)
            throw new SnapshotFormatException(
                $"These cannot be compared: one is a {left.ToneType} tone and the other a " +
                $"{right.ToneType} tone. Their parameters are different sounds' parameters.");

        var rightBlocks = Index(right, "right");
        var leftBlocks = Index(left, "left");

        List<BlockDifference> blocks = [];
        List<string> onlyOnLeft = [];
        var compared = 0;

        // The left snapshot's order, which is capture order and therefore address order.
        foreach (var block in left.Domains)
        {
            if (!rightBlocks.TryGetValue((block.Offset, block.Offset2), out var other))
            {
                onlyOnLeft.Add(block.Offset2);
                continue;
            }

            var difference = CompareBlock(block, other, ref compared);
            if (difference.HasAnything) blocks.Add(difference);
        }

        var onlyOnRight = right.Domains
            .Where(b => !leftBlocks.ContainsKey((b.Offset, b.Offset2)))
            .Select(b => b.Offset2)
            .ToList();

        return new SnapshotComparison(left.Name, right.Name, blocks, compared, onlyOnLeft, onlyOnRight);
    }

    /// <summary>The blocks of one snapshot, keyed the way they are matched. A snapshot listing the same
    /// block twice is refused rather than silently having one of them win -- the same guard, for the same
    /// reason, as <c>RestoreToneAsync</c>'s.</summary>
    private static Dictionary<(string Offset, string Offset2), SnapshotDomain> Index(
        Integra7Snapshot snapshot, string side)
    {
        Dictionary<(string, string), SnapshotDomain> blocks = [];
        foreach (var block in snapshot.Domains)
            if (!blocks.TryAdd((block.Offset, block.Offset2), block))
                throw new SnapshotFormatException(
                    $"The {side} snapshot lists block (\"{block.Offset}\", \"{block.Offset2}\") more " +
                    "than once, so there is no telling which one to compare.");

        return blocks;
    }

    private static BlockDifference CompareBlock(SnapshotDomain left, SnapshotDomain right, ref int compared)
    {
        var rightValues = new Dictionary<string, SnapshotValue>();
        foreach (var value in right.Values) rightValues[value.Path] = value;

        List<ValueDifference> differences = [];
        List<string> onlyOnLeft = [];
        var seen = new HashSet<string>();

        foreach (var value in left.Values)
        {
            seen.Add(value.Path);
            if (!rightValues.TryGetValue(value.Path, out var other))
            {
                onlyOnLeft.Add(value.Path);
                continue;
            }

            compared++;
            if (Differs(value, other))
                differences.Add(new ValueDifference(value.Path, value.Value, other.Value));
        }

        var onlyOnRight = right.Values.Where(v => !seen.Contains(v.Path)).Select(v => v.Path).ToList();
        return new BlockDifference(left.Offset, left.Offset2, differences, onlyOnLeft, onlyOnRight);
    }

    /// <summary>Raw against raw whenever both sides have one; strings otherwise. A value with a raw on
    /// one side only is a file from a build before raw values existed compared against a current one --
    /// the strings are all they have in common, so the strings are what decides.</summary>
    private static bool Differs(SnapshotValue left, SnapshotValue right) =>
        left.Raw is { } leftRaw && right.Raw is { } rightRaw
            ? leftRaw != rightRaw
            : left.Value != right.Value;
}
```

- [ ] **Step 4: Run until green, then the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter SnapshotDiffTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnapshotDiff.cs Tests/TestSnapshotDiff.cs
git commit -m "feat: compare two snapshots on their raw values"
```

---

### Task 2: Rendering a comparison as text

**Files:**
- Create: `Src/Models/Services/ComparisonText.cs`
- Test: `Tests/TestComparisonText.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestComparisonText.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The text a user pastes into a forum post or an email. Pinned exactly, because "readable" is
/// the whole feature and nothing else would notice it regressing.</summary>
public class ComparisonTextTests
{
    private static SnapshotComparison Comparison(params BlockDifference[] blocks) =>
        new("Warm Rhodes", "the instrument", blocks, ParametersCompared: 1402, [], []);

    [Test]
    public void Renders_a_heading_a_summary_and_one_section_per_block()
    {
        var comparison = Comparison(new BlockDifference(
            "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common",
            [
                new ValueDifference("SuperNATURAL Synth Tone Common/Tone Level", "100", "118"),
                new ValueDifference("SuperNATURAL Synth Tone Common/Portamento Switch", "OFF", "ON"),
            ], [], []));

        var text = ComparisonText.Format(comparison, "library file Warm Rhodes.json",
            "read 2026-07-28 10:14");

        Assert.That(text, Does.StartWith("Integra-7 Aural Alchemist — comparison"));
        Assert.That(text, Does.Contain("Left:   Warm Rhodes — library file Warm Rhodes.json"));
        Assert.That(text, Does.Contain("Right:  the instrument — read 2026-07-28 10:14"));
        Assert.That(text, Does.Contain("2 differences across 1 block; 1402 parameters compared."));
        Assert.That(text, Does.Contain("SuperNATURAL Synth Tone Common  (2 differences)"));
        // Paths are shown without the block name they already sit under, and the values are aligned on
        // the longest path in the section.
        Assert.That(text, Does.Contain("  Tone Level          100  ->  118"));
        Assert.That(text, Does.Contain("  Portamento Switch   OFF  ->  ON"));
    }

    [Test]
    public void Says_so_when_there_is_nothing_to_report()
    {
        var text = ComparisonText.Format(Comparison(), "file A", "file B");

        Assert.That(text, Does.Contain("These two are identical; 1402 parameters compared."));
        Assert.That(text, Does.Not.Contain("differences across"));
    }

    [Test]
    public void Lists_what_exists_on_only_one_side_when_there_is_any()
    {
        var comparison = new SnapshotComparison("A", "B",
            [
                new BlockDifference("Offset/X", "Offset2/Common", [],
                    ["Common/Only Here"], ["Common/Only There"]),
            ],
            ParametersCompared: 3,
            BlocksOnlyOnLeft: ["Offset2/Partial 4"],
            BlocksOnlyOnRight: []);

        var text = ComparisonText.Format(comparison, "file A", "file B");

        Assert.That(text, Does.Contain("Only in the left snapshot:"));
        Assert.That(text, Does.Contain("  Common/Only Here"));
        Assert.That(text, Does.Contain("  block Partial 4"));
        Assert.That(text, Does.Contain("Only in the right snapshot:"));
        Assert.That(text, Does.Contain("  Common/Only There"));
    }

    [Test]
    public void Counts_one_block_and_one_difference_in_the_singular()
    {
        var comparison = Comparison(new BlockDifference("Offset/X", "Offset2/Common",
            [new ValueDifference("Common/Level", "1", "2")], [], []));

        var text = ComparisonText.Format(comparison, "file A", "file B");

        Assert.That(text, Does.Contain("1 difference across 1 block;"));
        Assert.That(text, Does.Contain("Common  (1 difference)"));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ComparisonTextTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error — `ComparisonText` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/ComparisonText.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A comparison as plain text, for the clipboard or a file.
///
/// <b>Plain text, not Markdown.</b> This is pasted into forum posts, emails and notes at least as often
/// as into anything that renders, and a table that only looks like a table in a renderer is worse in the
/// places it does not. Alignment is done with spaces, per section, on the longest path in that section --
/// which is what makes a hundred-line list readable without any renderer at all.
///
/// Pure, so the layout is pinned by tests rather than by eye.</summary>
public static class ComparisonText
{
    /// <param name="leftSource">Where the left side came from, e.g. "library file Warm Rhodes.json" or
    /// "read 2026-07-28 10:14". The comparison itself knows the snapshots' names but not their
    /// provenance, and provenance is most of what makes a pasted comparison meaningful.</param>
    public static string Format(SnapshotComparison comparison, string leftSource, string rightSource)
    {
        var text = new StringBuilder();
        text.AppendLine("Integra-7 Aural Alchemist — comparison");
        text.AppendLine();
        text.AppendLine($"Left:   {comparison.LeftName} — {leftSource}");
        text.AppendLine($"Right:  {comparison.RightName} — {rightSource}");
        text.AppendLine();

        text.AppendLine(comparison.Identical
            ? $"These two are identical; {comparison.ParametersCompared} parameters compared."
            : $"{Count(comparison.DifferenceCount, "difference")} across " +
              $"{Count(comparison.Blocks.Count(b => b.Differences.Count > 0), "block")}; " +
              $"{comparison.ParametersCompared} parameters compared.");

        foreach (var block in comparison.Blocks.Where(b => b.Differences.Count > 0))
        {
            text.AppendLine();
            text.AppendLine($"{block.Name}  ({Count(block.Differences.Count, "difference")})");

            // Paths are shown relative to the block they already sit under: every path in a block starts
            // with that block's own name, and repeating it on every line costs half the width.
            var rows = block.Differences
                .Select(d => (Path: ShortPath(d.Path, block.Name), d.LeftValue, d.RightValue))
                .ToList();
            var width = rows.Max(r => r.Path.Length);
            foreach (var row in rows)
                text.AppendLine($"  {row.Path.PadRight(width)}   {row.LeftValue}  ->  {row.RightValue}");
        }

        AppendOnlyOnOneSide(text, "left", comparison.BlocksOnlyOnLeft,
            comparison.Blocks.SelectMany(b => b.PathsOnlyOnLeft));
        AppendOnlyOnOneSide(text, "right", comparison.BlocksOnlyOnRight,
            comparison.Blocks.SelectMany(b => b.PathsOnlyOnRight));

        return text.ToString();
    }

    private static void AppendOnlyOnOneSide(StringBuilder text, string side,
        IReadOnlyList<string> blocks, IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        if (blocks.Count == 0 && pathList.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"Only in the {side} snapshot:");
        foreach (var block in blocks)
            text.AppendLine($"  block {Strip(block)}");
        foreach (var path in pathList)
            text.AppendLine($"  {path}");
    }

    /// <summary>"SuperNATURAL Synth Tone Common/Tone Level" under a heading that already says
    /// "SuperNATURAL Synth Tone Common" becomes "Tone Level". Anything that does not start with the
    /// block's name is left whole rather than truncated on a guess.</summary>
    private static string ShortPath(string path, string blockName) =>
        path.StartsWith(blockName + "/", System.StringComparison.Ordinal)
            ? path[(blockName.Length + 1)..]
            : path;

    private static string Strip(string offset2) =>
        offset2.StartsWith("Offset2/", System.StringComparison.Ordinal)
            ? offset2["Offset2/".Length..]
            : offset2;

    /// <summary>"1 difference", "2 differences". A count that reads as English is worth four lines.</summary>
    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
```

- [ ] **Step 4: Run until green, then the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ComparisonTextTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

If an alignment assertion fails, count the spaces in the test against `PadRight` plus the three-space
separator — fix whichever is wrong, but keep the separator consistent between the two.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ComparisonText.cs Tests/TestComparisonText.cs
git commit -m "feat: render a comparison as plain text"
```

---

### Task 3: The Compare tab

**Files:**
- Create: `Src/ViewModels/CompareViewModel.cs`
- Create: `Src/Views/CompareView.axaml`, `Src/Views/CompareView.axaml.cs`

No unit tests: view models are not under test in this repository. Verification is that the solution builds
— which compiles every binding — and the suite still passes.

- [ ] **Step 1: Write the view model**

Create `Src/ViewModels/CompareViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One side of a comparison: a snapshot and where it came from.
///
/// The provenance is held separately from the snapshot because the snapshot does not know it -- a file
/// and a capture of the same sound are the same bytes -- and because it is most of what makes a pasted
/// comparison meaningful. For a capture it includes the time, since "the instrument" means the instrument
/// *as it was then*.</summary>
public sealed partial class CompareSlotViewModel : ViewModelBase
{
    [Reactive] private Integra7Snapshot? _snapshot;
    [Reactive] private string _source = "";

    public bool IsFilled => Snapshot is not null;

    /// <summary>What the slot shows when it is empty, and what it shows when it is full.</summary>
    public string Description => Snapshot is { } s
        ? $"{s.Name} — {(s.Kind == SnapshotKinds.Tone ? $"tone, {s.ToneType}" : "Studio Set")} — {Source}"
        : "nothing chosen yet";

    public void Put(Integra7Snapshot snapshot, string source)
    {
        Snapshot = snapshot;
        Source = source;
        this.RaisePropertyChanged(nameof(IsFilled));
        this.RaisePropertyChanged(nameof(Description));
    }
}

/// <summary>One block's differences, as the result list shows them.</summary>
public sealed class CompareBlockViewModel(string heading, IReadOnlyList<ValueDifference> rows)
    : ViewModelBase
{
    public string Heading { get; } = heading;
    public IReadOnlyList<ValueDifference> Rows { get; } = rows;
}

/// <summary>Two snapshots side by side, and what differs between them.
///
/// <b>Reads only.</b> Every other feature that touches the instrument writes to it; this one captures and
/// compares, so there is no half-applied state to reason about and no confirmation to ask for.
///
/// The four callbacks are the pattern <c>LibraryViewModel</c> already uses: a view model inside a tab has
/// no window to reach for, so anything needing one -- a file picker, the clipboard -- arrives as a
/// function.</summary>
public sealed partial class CompareViewModel : ViewModelBase
{
    private readonly Func<Task<(Integra7Snapshot Snapshot, string Source)?>> _fromFile;
    private readonly Func<Task<(Integra7Snapshot Snapshot, string Source)?>> _fromLibrary;
    private readonly Func<bool, Task<(Integra7Snapshot Snapshot, string Source)?>> _fromInstrument;
    private readonly Func<string, Task> _copy;
    private readonly Func<string, Task<string?>> _saveText;
    private readonly Action<string, bool> _report;

    /// <param name="fromInstrument">True for the Studio Set, false for the tone in the selected part. One
    /// callback rather than two because the caller has to resolve the part either way, and the flag is
    /// what it already switches on.</param>
    public CompareViewModel(
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> fromFile,
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> fromLibrary,
        Func<bool, Task<(Integra7Snapshot Snapshot, string Source)?>> fromInstrument,
        Func<string, Task> copy,
        Func<string, Task<string?>> saveText,
        Action<string, bool> report)
    {
        _fromFile = fromFile;
        _fromLibrary = fromLibrary;
        _fromInstrument = fromInstrument;
        _copy = copy;
        _saveText = saveText;
        _report = report;

        this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplyFilter());
    }

    public CompareSlotViewModel Left { get; } = new();
    public CompareSlotViewModel Right { get; } = new();

    /// <summary>Every block with differences, before the search box narrows it. Kept so that typing in
    /// the box does not re-run the comparison.</summary>
    private IReadOnlyList<CompareBlockViewModel> _allBlocks = [];

    public ObservableCollection<CompareBlockViewModel> Blocks { get; } = [];

    [Reactive] private string _searchText = "";
    [Reactive] private string _summary = "";
    [Reactive] private bool _hasResult;

    public bool CanCompare => Left.IsFilled && Right.IsFilled;

    /// <summary>What the last comparison rendered to, ready for the clipboard or a file. Held rather than
    /// re-rendered so that the text a user copies is the text they are looking at.</summary>
    private string _text = "";

    public async Task FillLeftFromFileAsync() => await FillAsync(Left, _fromFile);
    public async Task FillRightFromFileAsync() => await FillAsync(Right, _fromFile);
    public async Task FillLeftFromLibraryAsync() => await FillAsync(Left, _fromLibrary);
    public async Task FillRightFromLibraryAsync() => await FillAsync(Right, _fromLibrary);
    public async Task FillLeftFromStudioSetAsync() => await FillAsync(Left, () => _fromInstrument(true));
    public async Task FillRightFromStudioSetAsync() => await FillAsync(Right, () => _fromInstrument(true));
    public async Task FillLeftFromToneAsync() => await FillAsync(Left, () => _fromInstrument(false));
    public async Task FillRightFromToneAsync() => await FillAsync(Right, () => _fromInstrument(false));

    /// <summary>Put a snapshot into whichever slot is free, or the left one when both are. What the
    /// Library tab's "Compare this" button reaches.</summary>
    public void PutInFirstFreeSlot(Integra7Snapshot snapshot, string source)
    {
        (Left.IsFilled && !Right.IsFilled ? Right : Left).Put(snapshot, source);
        this.RaisePropertyChanged(nameof(CanCompare));
    }

    private async Task FillAsync(CompareSlotViewModel slot,
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> source)
    {
        // A cancelled picker or a failed capture leaves the slot exactly as it was: the previous contents
        // are still a side of a comparison the user may be halfway through setting up.
        if (await source() is not { } filled) return;

        slot.Put(filled.Snapshot, filled.Source);
        this.RaisePropertyChanged(nameof(CanCompare));
    }

    public void Compare()
    {
        if (Left.Snapshot is not { } left || Right.Snapshot is not { } right) return;

        SnapshotComparison comparison;
        try
        {
            comparison = SnapshotDiff.Compare(left, right);
        }
        catch (SnapshotFormatException e)
        {
            // Written for the user -- it names both kinds or both engines -- so it is shown as it is.
            _report(e.Message, true);
            return;
        }

        _text = ComparisonText.Format(comparison, Left.Source, Right.Source);
        _allBlocks =
        [
            .. comparison.Blocks
                .Where(b => b.Differences.Count > 0)
                .Select(b => new CompareBlockViewModel(
                    $"{b.Name}  ({b.Differences.Count})", b.Differences)),
        ];

        Summary = comparison.Identical
            ? $"These two are identical; {comparison.ParametersCompared} parameters compared."
            : $"{comparison.DifferenceCount} difference(s) across {_allBlocks.Count} block(s); " +
              $"{comparison.ParametersCompared} parameters compared.";
        HasResult = true;
        ApplyFilter();
        _report(Summary, false);
    }

    /// <summary>Narrow by parameter path across every section at once -- "cutoff" answers "what did I
    /// change about the filters" for all sixteen parts in one go. A section whose every row is filtered
    /// out disappears with them, rather than leaving an empty heading.</summary>
    private void ApplyFilter()
    {
        Blocks.Clear();
        var needle = SearchText.Trim();
        foreach (var block in _allBlocks)
        {
            if (needle.Length == 0)
            {
                Blocks.Add(block);
                continue;
            }

            var rows = block.Rows
                .Where(r => r.Path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rows.Count > 0) Blocks.Add(new CompareBlockViewModel(block.Heading, rows));
        }
    }

    public async Task CopyAsync()
    {
        if (_text.Length == 0) return;

        try
        {
            await _copy(_text);
            _report("Copied the comparison to the clipboard.", false);
        }
        catch (Exception e)
        {
            _report($"Could not copy the comparison: {e.Message}", true);
        }
    }

    public async Task SaveAsync()
    {
        if (_text.Length == 0) return;

        var path = await _saveText("comparison.txt");
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            _report("Could not save the comparison: the selected file has no accessible local path.", true);
            return;
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(path, _text);
            _report($"Saved the comparison to {System.IO.Path.GetFileName(path)}.", false);
        }
        catch (Exception e)
        {
            _report($"Could not save the comparison: {e.Message}", true);
        }
    }
}
```

- [ ] **Step 2: Write the view**

Create `Src/Views/CompareView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:svc="using:Integra7AuralAlchemist.Models.Services"
             mc:Ignorable="d" d:DesignWidth="1600" d:DesignHeight="800"
             x:Class="Integra7AuralAlchemist.Views.CompareView"
             x:DataType="vm:CompareViewModel">

    <!-- Two slots and what differs between them. The only tab in the application that never writes to
         the instrument: filling a slot from it is a read, and comparing touches nothing at all. -->

    <Grid RowDefinitions="Auto,Auto,Auto,*" Margin="10" RowSpacing="8">

        <Grid Grid.Row="0" ColumnDefinitions="*,*" ColumnSpacing="16">
            <StackPanel Grid.Column="0" Orientation="Vertical" Spacing="6">
                <TextBlock Text="Left" FontWeight="Bold" />
                <TextBlock Text="{Binding Left.Description}" TextWrapping="Wrap"
                           Foreground="{StaticResource SnMutedTextBrush}" />
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <Button Content="From the library" Command="{Binding FillLeftFromLibraryAsync}" />
                    <Button Content="From a file…" Command="{Binding FillLeftFromFileAsync}" />
                    <Button Content="Read the Studio Set" Command="{Binding FillLeftFromStudioSetAsync}" />
                    <Button Content="Read the selected part's tone" Command="{Binding FillLeftFromToneAsync}" />
                </StackPanel>
            </StackPanel>
            <StackPanel Grid.Column="1" Orientation="Vertical" Spacing="6">
                <TextBlock Text="Right" FontWeight="Bold" />
                <TextBlock Text="{Binding Right.Description}" TextWrapping="Wrap"
                           Foreground="{StaticResource SnMutedTextBrush}" />
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <Button Content="From the library" Command="{Binding FillRightFromLibraryAsync}" />
                    <Button Content="From a file…" Command="{Binding FillRightFromFileAsync}" />
                    <Button Content="Read the Studio Set" Command="{Binding FillRightFromStudioSetAsync}" />
                    <Button Content="Read the selected part's tone" Command="{Binding FillRightFromToneAsync}" />
                </StackPanel>
            </StackPanel>
        </Grid>

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8">
            <Button Content="Compare" Command="{Binding Compare}" IsEnabled="{Binding CanCompare}"
                    Padding="14,3" />
            <TextBox Text="{Binding SearchText, Mode=TwoWay}"
                     PlaceholderText="Narrow by parameter name"
                     MinWidth="260"
                     IsEnabled="{Binding HasResult}" />
            <Button Content="Copy" Command="{Binding CopyAsync}" IsEnabled="{Binding HasResult}" />
            <Button Content="Save as text…" Command="{Binding SaveAsync}" IsEnabled="{Binding HasResult}" />
        </StackPanel>

        <TextBlock Grid.Row="2" Text="{Binding Summary}" TextWrapping="Wrap" />

        <ScrollViewer Grid.Row="3">
            <ItemsControl ItemsSource="{Binding Blocks}" Margin="0,0,16,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="vm:CompareBlockViewModel">
                        <StackPanel Orientation="Vertical" Spacing="2" Margin="0,0,0,12">
                            <TextBlock Text="{Binding Heading}" FontWeight="Bold" />
                            <ItemsControl ItemsSource="{Binding Rows}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate DataType="svc:ValueDifference">
                                        <Grid ColumnDefinitions="3*,*,*" Margin="12,1,0,1">
                                            <TextBlock Grid.Column="0" Text="{Binding Path}"
                                                       TextTrimming="CharacterEllipsis" />
                                            <TextBlock Grid.Column="1" Text="{Binding LeftValue}"
                                                       Foreground="{StaticResource SnMutedTextBrush}" />
                                            <TextBlock Grid.Column="2" Text="{Binding RightValue}" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</UserControl>
```

The `Margin="0,0,16,0"` on the results list is the vertical scrollbar's lane — Avalonia's bars float over
the content rather than reserving space, as `App.axaml` records at length.

Create `Src/Views/CompareView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class CompareView : UserControl
{
    public CompareView()
    {
        InitializeComponent();
    }
}
```

Check an existing simple view — `Src/Views/LibraryView.axaml.cs` — and match whatever it does; if it
derives from something other than `UserControl`, follow that instead.

- [ ] **Step 3: Build**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: build succeeds. The view is not reachable yet — Task 4 adds the tab. `AVLN2000` means a binding
names a member the view model does not have.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/CompareViewModel.cs Src/Views/CompareView.axaml Src/Views/CompareView.axaml.cs
git commit -m "feat: add the Compare tab's view model and view"
```

---

### Task 4: Wiring it into the window and the library

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/Views/MainWindow.axaml`
- Modify: `Src/Views/MainWindow.axaml.cs`
- Modify: `Src/ViewModels/LibraryViewModel.cs`
- Modify: `Src/Views/LibraryView.axaml`

- [ ] **Step 1: Add the save-text and clipboard interactions**

In `Src/ViewModels/MainWindowViewModel.cs`, beside the existing interactions (`ShowSaveSnapshotDialog`,
`ShowConfirmDialog` and the rest):

```csharp
    /// <summary>Ask where to write a text file, answering the path, "" for a file with no local path, or
    /// null for a cancellation -- the same three-way result as <see cref="ShowSaveSnapshotDialog"/>, whose
    /// doc comment explains why "" and null are not the same thing. A second interaction rather than a
    /// parameter on that one because the file type differs and the picker's filter is part of what makes
    /// it usable.</summary>
    public Interaction<string, string?> ShowSaveTextDialog { get; }

    /// <summary>Put text on the system clipboard. An interaction rather than a service because the
    /// clipboard belongs to the window, and this view model is not allowed to know about one.</summary>
    public Interaction<string, Unit> ShowCopyToClipboard { get; }
```

and in the constructor, beside the other `new Interaction<...>()` lines:

```csharp
        ShowSaveTextDialog = new Interaction<string, string?>();
        ShowCopyToClipboard = new Interaction<string, Unit>();
```

- [ ] **Step 2: Build the Compare view model**

Add the property beside `LibraryVm`:

```csharp
    public CompareViewModel CompareVm { get; }
```

and construct it in the constructor, next to where `LibraryVm` is built (around
`Src/ViewModels/MainWindowViewModel.cs:2139`):

```csharp
        CompareVm = new CompareViewModel(
            OpenSnapshotForComparisonAsync,
            LibrarySelectionForComparisonAsync,
            CaptureForComparisonAsync,
            async text => await ShowCopyToClipboard.Handle(text),
            async suggested => await ShowSaveTextDialog.Handle(suggested),
            (message, failed) =>
            {
                // The window's status bar, for the reason the library's own reporter gives: one channel,
                // visible from every tab.
                SnapshotStatus = message;
                SnapshotFailed = failed;
            });
```

- [ ] **Step 3: Write the three sources**

Add these to `MainWindowViewModel`, beside the other snapshot commands:

```csharp
    /// <summary>A snapshot read from a file the user picks, for one side of a comparison. Null for a
    /// cancellation or a failure -- both leave the slot as it was, and a failure has already been
    /// reported.</summary>
    private async Task<(Integra7Snapshot Snapshot, string Source)?> OpenSnapshotForComparisonAsync()
    {
        var path = await ShowOpenSnapshotDialog.Handle(Unit.Default);
        if (path is null) return null; // cancelled
        if (path.Length == 0)
        {
            SnapshotFailed = true;
            SnapshotStatus = "Could not read that file: it has no accessible local path.";
            return null;
        }

        try
        {
            var snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));
            return (snapshot, $"file {Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read a snapshot for comparison", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not read that file: {e.Message}";
            return null;
        }
    }

    /// <summary>The library's currently selected entry, for one side of a comparison.</summary>
    private async Task<(Integra7Snapshot Snapshot, string Source)?> LibrarySelectionForComparisonAsync()
    {
        if (LibraryVm.SelectedEntry is not { } entry)
        {
            SnapshotFailed = true;
            SnapshotStatus = "Select a snapshot in the Library tab first.";
            return null;
        }

        try
        {
            var snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(entry.FilePath));
            return (snapshot, $"library file {Path.GetFileName(entry.FilePath)}");
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read a library snapshot for comparison", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not read that file: {e.Message}";
            return null;
        }
    }

    /// <summary>Read the instrument into a snapshot for one side of a comparison: the whole Studio Set,
    /// or the tone in the selected part.
    ///
    /// Nothing is written. This is the only path in the application that opens a conversation purely to
    /// read, and the lease is held for the capture alone.</summary>
    private async Task<(Integra7Snapshot Snapshot, string Source)?> CaptureForComparisonAsync(bool studioSet)
    {
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null)
        {
            SnapshotFailed = true;
            SnapshotStatus = "Connect to your Integra-7 to read from it.";
            return null;
        }

        SelectedTone? selected = null;
        if (!studioSet)
        {
            selected = await ResolveSelectedToneAsync("compare");
            if (selected is null) return null; // ResolveSelectedToneAsync has already said why
        }

        try
        {
            SignalStartSync();
            SyncInfo = studioSet ? "Reading the Studio Set" : $"Reading tone from part {selected!.ZeroBasedPartNo + 1}";
            await using var lease = await api.BeginConversationAsync("read for comparison");

            // The captured-at time is part of what the slot says: "the instrument" means the instrument
            // as it was when it was read, and a comparison pasted into a message needs to say when.
            var at = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
            return studioSet
                ? (await StudioSetSnapshotService.CaptureAsync(communicator, "the instrument", lease),
                    $"read {at}")
                : (await StudioSetSnapshotService.CaptureToneAsync(communicator,
                        selected!.ZeroBasedPartNo, selected.ToneType, "the instrument", lease),
                    $"part {selected.ZeroBasedPartNo + 1}, read {at}");
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read the instrument for comparison", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not read the instrument: {e.Message}";
            return null;
        }
        finally
        {
            SignalStopSync();
        }
    }
```

Add `using System.Globalization;` if it is not already among the file's usings.

- [ ] **Step 4: Let the library send an entry to the Compare tab**

In `Src/ViewModels/LibraryViewModel.cs`, add a callback parameter after `confirm`:

```csharp
    /// <param name="compare">Hand this entry to the Compare tab. A callback for the same reason the
    /// others are: this view model knows nothing about its neighbours.</param>
```

with `Action<LibraryEntry> compare`, stored in a `_compare` field, and the command:

```csharp
    /// <summary>Send the selected snapshot to the Compare tab, which fills whichever of its two slots is
    /// free. The comparison itself is that tab's job; this is only a way in from the list.</summary>
    public void CompareThis()
    {
        UserActionLog.Action("button: Compare this");
        if (SelectedEntry is { } entry) _compare(entry.Entry);
    }
```

In `Src/Views/LibraryView.axaml`, after the "Use as the init tone" button, matching the alignment its
neighbours use:

```xml
                            <Button Content="Compare this"
                                    Command="{Binding CompareThis}"
                                    IsEnabled="{Binding HasSelection}"
                                    ToolTip.Tip="Send this snapshot to the Compare tab, to see how it differs from another one or from what the instrument holds now."
                                    HorizontalAlignment="Stretch"
                                    HorizontalContentAlignment="Center" />
```

In `MainWindowViewModel`, pass the new argument where `LibraryViewModel` is constructed, after the confirm
callback:

```csharp
            entry =>
            {
                try
                {
                    var snapshot = Integra7Snapshot.FromJson(File.ReadAllText(entry.FilePath));
                    CompareVm.PutInFirstFreeSlot(snapshot, $"library file {Path.GetFileName(entry.FilePath)}");
                    // Bring the tab forward: a slot filled on a tab the user cannot see looks like a
                    // button that did nothing.
                    TopTabIndex = CompareTabIndex;
                }
                catch (Exception e)
                {
                    UserActionLog.Failed("send a library snapshot to the Compare tab", e.ToString());
                    SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not read that file: {e.Message}";
                    SnapshotFailed = true;
                }
            },
```

`CompareVm` must be constructed **before** `LibraryVm` for this to compile as written; move the
`CompareVm = new CompareViewModel(...)` block above it if it is not already there.

Add the constant beside the other top-tab reasoning in `MainWindowViewModel`:

```csharp
    /// <summary>Where the Compare tab sits in the top-level TabControl. A constant rather than a search,
    /// because the strip is fixed in MainWindow.axaml -- but it has to be changed with it, which is why
    /// it is named here rather than written as a bare 4 at the call site.</summary>
    private const int CompareTabIndex = 4;
```

- [ ] **Step 5: Add the tab**

In `Src/Views/MainWindow.axaml`, immediately after the Library `TabItem` (which ends around line 725) and
before the Motional Surround one:

```xml
            <!-- No "connect your Integra-7" placeholder, for the same reason the Library tab has none:
                 comparing two saved snapshots works with nothing plugged in, and only the two "read the
                 instrument" buttons need a device — they say so themselves when there is none. -->
            <TabItem Header="Compare" Classes="top">
                <local:CompareView DataContext="{Binding CompareVm}" />
            </TabItem>
```

This makes Compare index 4 and pushes Motional Surround to 5 and SRX Loader to 6. Check whether any code
sets `TopTabIndex` to something other than 0 — `grep -n "TopTabIndex = " Src/ViewModels/*.cs` — and fix any
index that now points at the wrong tab.

- [ ] **Step 6: Register the two new handlers**

In `Src/Views/MainWindow.axaml.cs`, inside `RegisterDialogHandler`:

```csharp
            action(ViewModel!.ShowSaveTextDialog.RegisterHandler(DoShowSaveTextDialogAsync));
            action(ViewModel!.ShowCopyToClipboard.RegisterHandler(DoCopyToClipboardAsync));
```

and the handlers themselves, beside `DoShowSaveSnapshotDialogAsync`:

```csharp
    private async Task DoShowSaveTextDialogAsync(IInteractionContext<string, string?> interaction)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Comparison",
            SuggestedFileName = interaction.Input,
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
        });

        // "" for a picked file with no local path, null only for a cancellation -- see
        // DoShowSaveSnapshotDialogAsync, which answers the same three ways for the same reason.
        interaction.SetOutput(file is null ? null : file.TryGetLocalPath() ?? "");
    }

    /// <summary>The clipboard belongs to the top level, not to the view model, which is why this is an
    /// interaction. A null clipboard is possible on a platform that has none; saying so is better than a
    /// silent no-op.</summary>
    private async Task DoCopyToClipboardAsync(IInteractionContext<string, Unit> interaction)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) throw new InvalidOperationException("This platform has no clipboard.");

        await clipboard.SetTextAsync(interaction.Input);
        interaction.SetOutput(Unit.Default);
    }
```

Check the file's existing usings for `Avalonia.Platform.Storage` (`FilePickerFileType` lives there) and
`System.Reactive`; add what is missing. **`SetTextAsync` needs `using Avalonia.Input.Platform;`** — in
Avalonia 12 it is an extension method on `ClipboardExtensions` rather than a member of `IClipboard`, so
without that using this handler does not compile, and the using looks removable to anyone tidying later.

- [ ] **Step 7: Build and run the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: build succeeds and the suite still passes at its Task 2 count.

- [ ] **Step 8: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/Views/MainWindow.axaml Src/Views/MainWindow.axaml.cs Src/Views/LibraryView.axaml
git commit -m "feat: compare snapshots from the library, a file or the instrument"
```

---

## Verification by hand (user)

- [ ] Compare a library tone against the same tone loaded in a part: no differences.
- [ ] Change one knob and compare again: exactly that parameter, in the right block, old and new values
  the right way round.
- [ ] Compare two different Studio Sets: sections per block, and the per-block counts add up to the
  summary.
- [ ] Compare a Studio Set against a tone: refused, with a message naming both.
- [ ] Type "cutoff" in the search box: only matching rows, and sections with no match disappear.
- [ ] Copy, then paste into a text editor: the alignment survives.
- [ ] Save as text, then open the file: the same text.
- [ ] "Compare this" in the Library tab fills a slot and brings the Compare tab forward.
- [ ] With no instrument connected, the two "read" buttons say so rather than failing silently.
