# Snapshot diff and text export — design

**Stage 6** of `docs/superpowers/plans/2026-07-25-feature-roadmap.md`.

**Goal.** Answer "what is different?" — between two saved snapshots, or between a saved snapshot and what
the instrument holds right now — and let that answer leave the application as text.

**Not in scope.** A printable patch sheet (Stage 8: a layout job, not this one), writing a whole snapshot
out as text, editing either side from the comparison, merging or applying differences, and three-way
comparison. Verify — "read the device and check it still matches this snapshot" — is Stage 7, and this is
deliberately the machinery it will call.

---

## What this is built on

| Existing | What it gives this stage |
| --- | --- |
| `Integra7Snapshot` / `SnapshotDomain` / `SnapshotValue` | Both sides of every comparison, already parsed and validated |
| `SnapshotValue.Raw` (format v2, current v3) | The value the device actually stores, which is what a difference is really about |
| `StudioSetSnapshotService.CaptureAsync` / `CaptureToneAsync` | Reading the instrument into a snapshot, unchanged |
| `SnapshotLibrary.Read` and the Library tab | One of the three ways a side is filled |
| `MainWindowViewModel.ResolveSelectedToneAsync` | Which part a tone capture comes from, and its engine |
| `TopTabIndex` | Bringing the Compare tab forward from the Library tab |

Nothing in this stage writes to the instrument. It is the first feature in the application that only
reads, and that is worth stating: no lease is held across anything but a capture, and no failure here can
leave the device holding half of something.

---

## Decisions, and why

**A difference is decided on the raw value, not the display string.** Format v2 added the raw value for
exactly this kind of question, and it is what the device stores. Display strings are a rendering: a build
that renames an enum label — "Low pass" to "LPF", say — would otherwise report every parameter of that
type as changed, in every comparison, for ever. Where either side has no raw value the strings are
compared, which is right rather than a fallback: a text parameter's value *is* its string. Both display
values are carried into the result regardless, because they are what a human reads.

**Blocks are matched on `(Offset, Offset2)`, never on `Start`.** `Start` encodes which part a tone sat in
when it was captured. Matching on it would make a tone captured from part 3 differ from the identical tone
in part 5 in every single parameter. `RestoreToneAsync` already re-targets on exactly this reasoning, and
this is the same fact seen from the other side.

**A kind or engine mismatch is refused, not diffed.** A Studio Set and a tone share no blocks at all, and
so do a SuperNATURAL Synth tone and a PCM Synth tone. "Every parameter on both sides is different" is
technically true and tells the user nothing, so the comparison is refused before any reading happens, with
a message naming both kinds.

**A block or a path present on only one side is reported, not refused.** That is what an older file, or a
build that has since added a parameter, really looks like. It is a genuine answer — "this side has
something yours does not" — and refusing it would make old snapshots uncomparable, which is when comparing
is most valuable.

**The comparison holds no live objects.** It is computed from two `Integra7Snapshot` values and nothing
else: no domain, no parameter database, no device. That is what lets every rule above be unit-tested, and
it is why a capture is a separate step that happens before the comparison rather than inside it.

---

## Components

### `SnapshotDiff` (`Src/Models/Services/`)

Pure. The whole of the reasoning above lives here.

```csharp
/// <summary>One parameter that differs, with both sides as the user reads them.</summary>
public sealed record ValueDifference(string Path, string LeftValue, string RightValue);

/// <summary>What differs within one block, plus what exists on only one side of it.</summary>
public sealed record BlockDifference(
    string Offset,
    string Offset2,
    IReadOnlyList<ValueDifference> Differences,
    IReadOnlyList<string> PathsOnlyOnLeft,
    IReadOnlyList<string> PathsOnlyOnRight);

/// <summary>A whole comparison. Blocks with nothing to report are absent.</summary>
public sealed record SnapshotComparison(
    string LeftName,
    string RightName,
    IReadOnlyList<BlockDifference> Blocks,
    int ParametersCompared,
    IReadOnlyList<string> BlocksOnlyOnLeft,
    IReadOnlyList<string> BlocksOnlyOnRight)
{
    public int DifferenceCount => Blocks.Sum(b => b.Differences.Count);

    /// <summary>Nothing to report at all -- no differing value, and nothing on one side that is not on
    /// the other. A path present on only one side counts against this: the two are not the same
    /// snapshot, and saying "identical" of them would be wrong in the way that matters.</summary>
    public bool Identical => DifferenceCount == 0 &&
                             BlocksOnlyOnLeft.Count == 0 && BlocksOnlyOnRight.Count == 0 &&
                             Blocks.All(b => b.PathsOnlyOnLeft.Count == 0 && b.PathsOnlyOnRight.Count == 0);
}

public static class SnapshotDiff
{
    /// <summary>Compare two snapshots. Throws SnapshotFormatException when their kinds or engines
    /// differ, because such a comparison has no useful answer.</summary>
    public static SnapshotComparison Compare(Integra7Snapshot left, Integra7Snapshot right);
}
```

Ordering is the snapshots' own: blocks in the order the left side lists them, parameters in address order
within each block. That is capture order, which is the order everything else in the application already
presents these values in, and it makes a comparison reproducible rather than dictionary-ordered.

### `ComparisonText` (`Src/Models/Services/`)

Pure. One function, `Format(SnapshotComparison, string leftSource, string rightSource)`, answering the
text that is copied or saved. Separate from `SnapshotDiff` because a rendering and a computation are two
responsibilities, and because pinning the layout in tests is only worth doing if the layout has a home.

```
Integra-7 Aural Alchemist — comparison

Left:   Warm Rhodes  (tone, SN-S)  — library file Warm Rhodes.json
Right:  the instrument, part 4  (tone, SN-S)  — read 2026-07-28 10:14

43 differences across 9 blocks; 1402 parameters compared.

SuperNATURAL Synth Tone Common  (3 differences)
  Tone Level                      100  ->  118
  Portamento Time                   0  ->   24
  Portamento Switch               OFF  ->  ON

SuperNATURAL Synth Tone Partial 1  (18 differences)
  ...

Only in the left snapshot:
  SuperNATURAL Synth Tone Common/Reserved21
```

Plain text, not Markdown: it is pasted into forum posts, emails and notes as often as into anything that
renders. Values are column-aligned on the longest path in each block, which is what makes a long list
readable without a table.

### `CompareViewModel` (`Src/ViewModels/`) and `CompareView` (`Src/Views/`)

A new top-level tab, beside Mixer and Layers, which are also assembled views on their own tab.

**Two slots**, Left and Right. Each holds a snapshot and a one-line description of where it came from, and
each is filled by one of three buttons:

| Button | What it does |
| --- | --- |
| From the library… | Takes the Library tab's currently selected entry |
| From a file… | The existing open-snapshot file picker |
| From the instrument | Captures — a Studio Set, or the tone in the selected part |

**From the instrument** needs to know which it is capturing. The slot offers both: "Read the Studio Set"
and "Read the tone in part N", the second labelled with the part currently selected in the Parameters tab
and disabled on the Common tab, so there is nothing to guess. The capture is one conversation, exactly as
Save Studio Set and Save Tone already do.

**Compare** is enabled when both slots are full. The result is a summary line, then one collapsible
section per block that has differences, headed by the block's name and its count. A search box narrows by
parameter path across every section at once — "cutoff" answers "what did I change about the filters" for
all sixteen parts in one go. An identical pair says so in a line rather than showing an empty list.

**Copy** and **Save as text…** produce the same text. The clipboard is reached through a callback the view
model is constructed with, the way `LibraryViewModel` already takes its folder picker and its confirmation:
a view model inside a tab has no window to ask.

### Changes to existing files

- `Src/ViewModels/LibraryViewModel.cs` — a `CompareThis` command, which hands the selected entry to a
  callback the constructor takes. It knows nothing about the Compare tab.
- `Src/Views/LibraryView.axaml` — the button for it, beside "Load into the instrument".
- `Src/ViewModels/MainWindowViewModel.cs` — owns `CompareVm`, wires the library callback to it (first free
  slot, then bring the tab forward via `TopTabIndex`), and provides the capture and clipboard callbacks.
- `Src/Views/MainWindow.axaml` — the new `TabItem`, with `Classes="top"` like its neighbours.

---

## Failure, and what the user is told

| Situation | What happens |
| --- | --- |
| Kinds or engines differ | Refused before any read: "This compares a Studio Set with a tone" / "…an SN-S tone with a PCM tone." |
| The device does not answer during a capture | The capture's own per-block message; the slot is left as it was, so the previous contents are not lost |
| A slot is empty | Compare is disabled |
| No differences | "These two are identical — 1,402 parameters compared." |
| A path or block exists on one side only | Listed under its own heading, not an error |
| Saving the text fails | Reported on the status line; the comparison stays on screen, and Copy still works |

Nothing here writes to the instrument, so no failure leaves it in a half-applied state — the property every
other stage had to reason about at length does not arise.

---

## Testing

`SnapshotDiff` and `ComparisonText` are pure, and they carry the weight.

**`SnapshotDiff`**
- Two identical snapshots produce no differences, and `Identical` is true.
- A parameter whose raw value differs is reported, with both display values.
- **A parameter whose raw value matches but whose display string differs is *not* reported** — the
  renamed-enum case, and the single most important test here.
- A text parameter (no raw on either side) is compared on its string.
- A block present in both, but under a different `Start`, is matched — the tone-captured-from-another-part
  case.
- A Studio Set against a tone throws, and the message names both kinds; an SN-S tone against a PCM tone
  throws, and the message names both engines.
- A path on one side only appears under `PathsOnlyOnLeft`/`Right` and not as a difference; the same for a
  whole block.
- `ParametersCompared` counts what was actually compared, not the sum of both sides.
- Ordering follows the left snapshot's blocks and their address order within a block.

**`ComparisonText`**
- A small fixed comparison renders exactly the expected text, including the alignment.
- An identical pair renders the "identical" line and no sections.
- The one-side-only lists appear only when they have entries.

The view model and the view are not unit-tested, consistent with the rest of this repository; their
verification is that the solution builds — which compiles every binding — and the hand checks below.

---

## Verification by hand (user)

- Compare a library tone against the same tone loaded in a part: no differences.
- Change one knob, compare again: exactly that parameter, in the right block, with the old and new values
  the right way round.
- Compare two different Studio Sets: sections per block, counts adding up to the summary.
- Compare a Studio Set against a tone: refused, with a message naming both.
- Copy, then paste into a text editor: the alignment survives.
- Compare a tone captured from part 3 against the same tone in part 5: no differences.
