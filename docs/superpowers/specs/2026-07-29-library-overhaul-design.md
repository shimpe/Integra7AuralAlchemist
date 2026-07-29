# Library overhaul — design

**Date:** 2026-07-29
**Status:** approved, not yet planned

Six features the library lacks, chosen from a survey of what synthesizer forums complain about in
librarian tools. They are designed together because three of them share machinery, and sequenced into five
phases that can each be built, reviewed and merged on their own.

---

## Why these six

The library today lists a folder of snapshots, filters them seven ways, annotates them one at a time, and
loads one into a part. What it cannot do:

| # | Gap | Phase |
| --- | --- | --- |
| 9 | Nothing survives a mistake — no history, no way back from a save | 1 |
| 3 | One row at a time, so annotating a real library is unaffordable | 2 |
| 2 | Hearing a patch means overwriting the part you were working on | 3 |
| 5 | Nothing notices that a sound has been saved four times | 4 |
| 7 | No way to ask what is *inside* a patch | 4 |
| 8 | Nothing a DAW can read | 5 |

**Version history is first, and that is a change from the order the features were picked in.** Phase 2 adds
bulk delete; phase 1 is what makes bulk delete recoverable. Building them the other way round means a period
in which the most destructive button in the application has no undo behind it.

Two gaps from the same survey are deliberately **not** here: importing `.syx` / `.svd` collections, and a
bulk dump of the instrument's own memory. The first is the largest single piece of work on the list and
changes what the application is for; the second was costed and declined on 2026-07-28.

**Assumed library size: hundreds of patches (100–1000).** Every decision below about cost is made against
that number. The user's current library is 9 files, ranging from an 8 KB SuperNATURAL Acoustic tone to a
633 KB PCM drum kit — the kit is the outlier that dominates any full-library read.

---

## Architecture

**Everything that can be got wrong is a pure service; the view models hold state and wiring only.** That is
not a preference. Since ReactiveUI 24 a view model cannot be constructed in a test at all — `WhenAnyValue`
throws `InvalidOperationException` demanding `RxAppBuilder`'s `.BuildApp()` — so anything that deserves a
test has to live outside one. `MorphCandidates` was carved out of `TonePickerViewModel` for exactly this
reason and is the precedent.

### The readers

Three forward-only `Utf8JsonReader` walks over a snapshot file, none of which materialises the parameter
data. The first already exists; the other two are this design's central idea.

| Reader | Collects | Used by |
| --- | --- | --- |
| `SnapshotHead` (exists) | metadata; **skips** parameter data | the list |
| `SnapshotTextScan` (new) | whether any stored displayed value contains a substring, and the first that does | deep search |
| `SnapshotRawVector` (new) | kind, engine, and a packed `long[]` of raw values | duplicates |

`SnapshotHead`'s remarks explain why the walk is affordable and a `FromJson` parse is not: a snapshot is
almost entirely parameter values, and turning them into records is exactly the work these features have no
use for. The new readers keep that property — they collect one primitive per parameter and build no objects.

**Deep search needs no re-rendering.** A `SnapshotValue` carries both the raw value and the displayed string,
and the file stores both, so matching "supersaw" is a substring test against text already on disk. Nothing
consults the parameter database.

**Two patches of the same engine have identical path lists by construction** — the same blocks in the same
order, from `ToneDomainNames`. So raw vectors compare positionally, and the paths need not be stored per
patch at all. This is what keeps a 900-patch cache at roughly 11 MB rather than a repeated copy of every
parameter name.

### The cache

One dictionary, in memory, for the life of the process: path → (last-write time, size, raw vector). A scan
re-reads only the files whose timestamp or size has changed. Changing the duplicate threshold regroups
without touching the disk.

Deliberately **not** an on-disk index. `SnapshotLibrary`'s own remarks record why the library has none: the
metadata lives in the files, so a file copied in from elsewhere is complete the moment it lands, and there is
nothing that can go stale. An index would be the first thing in the library able to disagree with the disk.
An in-memory cache keyed on timestamp and size cannot outlive the process, so it cannot be wrong across runs.

Deep search caches nothing at all — it streams, matches and discards.

### New files

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/PatchHistory.cs` | Archive a file before it is written or deleted; list and restore versions |
| `Src/Models/Services/BulkEdit.cs` | What a bulk change means for one entry |
| `Src/Models/Services/AuditionState.cs` | What was borrowed from a part, and how to give it back |
| `Src/Models/Services/SnapshotTextScan.cs` | Deep-search reader |
| `Src/Models/Services/SnapshotRawVector.cs` | Duplicate-detection reader |
| `Src/Models/Services/DuplicateGroups.cs` | Vectors + threshold → groups |
| `Src/Models/Services/PatchList.cs` | The rows a DAW list is made of |
| `Src/Models/Services/PatchListWriters.cs` | `IPatchListWriter` and its four implementations |
| `Src/ViewModels/LibraryEditorViewModel.cs` | The metadata panel, single and bulk, plus versions |
| `Src/ViewModels/DuplicateScanViewModel.cs` | The duplicates panel |
| `Src/Views/DuplicateScanView.axaml` | ditto |

### Changed files

| File | Change |
| --- | --- |
| `Src/Models/Services/SnapshotLibrary.cs` | `Write` and `Delete` archive first |
| `Src/ViewModels/LibraryViewModel.cs` | Multi-select; the editor half moves out; hosts the new panels |
| `Src/Views/LibraryView.axaml` | Multi-select, bulk panel, deep-search toggle, duplicates panel |
| `Src/ViewModels/MainWindowViewModel.cs` | Audition wiring; the export command |
| `Src/Views/MainWindow.axaml(.cs)` | The export dialog |

### One refactor, included deliberately

`LibraryViewModel` is around 540 lines before any of this, and these phases would add multi-select, bulk
commands, audition control, a duplicates panel and version restore to it. It splits along seams that already
exist:

- `LibraryViewModel` — the folder, the filters, the list, the selection
- `LibraryEditorViewModel` — the metadata panel, in both its single and bulk shapes, plus versions
- `DuplicateScanViewModel` — the duplicates panel

This is not general tidying and no other file is touched for its own sake. It is the file being modified in
four of the five phases, and it is already at the size where an edit is harder to make correctly than it
should be.

**The split happens in phase 1**, which is the first phase to add to the editor panel — it gains the version
list — and doing it then means the later phases extend three focused files rather than growing one large
one. `DuplicateScanViewModel` is created empty-handed in phase 1 only if it costs nothing to do so;
otherwise it arrives with phase 4, which is what needs it.

---

## Phase 1 — version history

**What a version is.** A copy of the file as it was, taken immediately before something overwrites or
deletes it, named after the file's **own last-write time** rather than the moment of archiving — so a
version says when its content was written. Stored at
`<library>/.history/<file stem>/<yyyyMMddTHHmmss>.json`. A collision within the same second gets a numeric
suffix.

**`.history` stays out of the listing for free.** `SnapshotLibrary.Read` enumerates
`SearchOption.TopDirectoryOnly`, so a sub-folder is already invisible to it.

**API.**

- `Archive(libraryFolder, filePath)` — no-op when the file does not exist, so `Create` and `WriteMetadata`
  can share one call site. Prunes to the newest `Keep = 10`.
- `Versions(libraryFolder, filePath)` — newest first.
- `Restore(libraryFolder, filePath, versionPath)` — archives the current file before overwriting it, so a
  restore is itself undoable.

**Wired into** `SnapshotLibrary.Write` and `SnapshotLibrary.Delete`.

**If archiving fails, the write is refused, and the message says why.** `Write` is atomic — a temp file and
then a move — so continuing would destroy the previous version at the exact moment it has been established
that no copy can be kept. Refusing an annotation is an annoyance; losing the only copy of a patch is not.

**UI.** In the editor panel: a list of versions with their dates, and a Restore button that confirms first.

**Tests.** Archiving creates the folder; a missing file is a no-op; pruning keeps exactly `Keep`; a
same-second collision does not overwrite; restoring archives the current file first; deleting archives; a
failure to archive prevents the write and leaves the original intact.

---

## Phase 2 — multi-select and bulk operations

**Selection.** `SelectionMode="Extended"` — control- and shift-click, the convention everywhere else.

**The editor panel has two shapes.** One row selected: what exists today. More than one: a bulk form
offering category, rating, favourite, add tags, remove tags, and delete. **Name is absent** — a rename cannot
be bulk.

**Tags are added and removed, never replaced.** Replacing would wipe each patch's own vocabulary, which is
the thing tags exist to hold.

**`BulkEdit` decides what a change means** for a single entry — tag union preserving the order already
there, case-insensitive removal, matching how `LibraryFilter` compares tags — and is pure.

**One file at a time, and a failure costs that file only.** A snapshot held open by a sync client must not
abandon the other thirteen. The outcome is reported as a count with the failures named: "12 of 14 updated;
2 could not be written: Warm Rhodes, Old Pad."

**Bulk delete confirms**, naming the count, and every deleted file is archived by phase 1.

**Tests.** `BulkEdit`'s union, removal, case handling and no-op cases. The batch loop's partial-failure
reporting is view-model wiring and is not tested; `BulkEdit` is where the decisions are.

---

## Phase 3 — audition

**What it is.** Hearing a library patch in the selected part without losing what that part holds.

**`AuditionState`** remembers the borrowed part, the preset that was selected on it, and a full capture of
its tone. Starting an audition captures; choosing another candidate while one is running keeps the original
memory and loads only the new candidate; stopping re-selects the preset and restores the tone.

**Stopping is triggered by** the Stop button, leaving the Library tab, performing a real load, or the
application closing.

**Cross-engine auditioning works.** A tone can only be written into a part whose temporary tone is already
the same engine — `EnsureToneFitsPart` enforces it. So when the candidate's engine differs, a preset of the
candidate's engine is selected on the part first. Which preset does not matter sonically: the restore
overwrites the whole temporary area, and the preset selection is only there to put the right block layout in
it.

**Two limitations, stated rather than hidden.**

1. **The edit journal is cleared when an audition starts**, for the reason `LoadToneAsync` clears it — its
   steps name parameters of a tone that is no longer loaded — and it is **not** restored at the end.
   Auditioning is therefore not free if there are unsaved edits in the journal.
2. **A crash or a kill during an audition leaves the part holding the candidate.** The temporary tone area
   is not persistent, so re-selecting a preset restores the instrument; nothing is permanently lost.

**Refused while Compare is showing the pre-edit sound**, for the reason a morph and a load are: the
journal's buffer is then the only copy of the edited values.

**A failed restore keeps the memory** so that Stop can be pressed again, and says what happened.

**Tests.** `AuditionState`'s transitions — start, switch candidate, stop, stop when nothing is running — as
plain functions. The device path is tested with the fake API that `MorphWriterTests` uses: an audition
writes the candidate, and a stop writes back exactly what was captured.

---

## Phase 4 — duplicates and deep search

### Duplicates

**`DuplicateGroups.Find(vectors, threshold)`** buckets by (kind, engine) and vector length, compares pairs
within a bucket abandoning a pair as soon as it exceeds the threshold, and unions what survives. Reserved
parameters are excluded, as they are from the comparison report. Name, notes, tags and rating are not part of
a vector at all — two files differing only in what has been said about them are the same sound.

**Grouping is transitive.** A near B and B near C puts all three in one group even where A and C differ by
more than the threshold. The UI says so — "each differs in at most N from at least one other here" — rather
than implying every pair is alike.

**Cost.** Bucketing by engine is what makes this affordable: comparison is between packed `long[]`s, with
early abandon, only within a bucket. For a library of hundreds this is a scan of the folder followed by
arithmetic measured in milliseconds.

**Studio Sets are included**, not excluded: (kind, engine) is the bucket key, so Studio Sets group among
themselves and can never be paired with a tone. A Studio Set saved twice is as much a duplicate as a tone
saved twice.

**UI.** Its own panel: a threshold defaulting to **5** — small enough that a group means "the same sound",
large enough to catch a patch saved again after a couple of tweaks — a Scan button with progress, the groups
with a checkbox per row, Delete ticked, and a button handing two rows to the Compare tab, which already
knows how to show what differs.

### Deep search

**A checkbox beside the search box**, run on Enter rather than on every keystroke, because it reads files.

**How it combines with the filters, exactly.** The text box is one axis of `LibraryFilter` among seven. The
deep pass widens *that axis only*: an entry is admitted when it passes every other axis — kind, engine,
category, rating, favourites, tags — **and** the text matches its name, notes, category or tags **or** any
of its parameter values. The other six still narrow. So ticking the box can only ever add rows, never remove
them, which is what a user will assume from a checkbox that says "look inside patches too".

Concretely: `LibraryFilter` is asked twice — once as it stands, and once with `Text` blanked, which yields
the entries the non-text axes admit. The deep scan runs over that second set, and its hits are unioned with
the first. `LibraryFilter` stays pure over heads and gains nothing.

Matching is **ordinal, ignoring case** — `LibraryFilter`'s own rule, and for its reason: the same library
must search the same way on every machine, and nobody searching their own sounds is thinking about capitals.

**The matching parameter is shown** — "Partial 1/OSC Wave = SuperSaw" — so a hit can be explained rather
than taken on trust.

**Tests.** `DuplicateGroups`: threshold boundaries, engines never mixed, the transitive case, deterministic
ordering, an empty library. Both readers against real snapshot files, including that a Studio Set and a tone
never land in one bucket, and that an unreadable file is skipped and logged rather than sinking the scan.

---

## Phase 5 — DAW patch-list export

**The source is the instrument, not the library.** A DAW patch list is addressable by bank select and
program change; a library file is not reachable that way at all. So the list is built from the presets
already in memory — the factory banks and the user memory names.

**`PatchList`** is rows of `(msb, lsb, pc, engine, category, name)`.

**`IPatchListWriter`** — an extension and a `Write(PatchList)` returning text — with four implementations:

| Format | Notes |
| --- | --- |
| `.reabank` | Reaper. `Bank <msb> <lsb> <name>` then `<pc> <name>` per patch |
| Cubase / Nuendo XML | Nested patch banks, each patch carrying its two control changes and a program change |
| `.midnam` | The MMA MIDINameDocument standard; read by Ardour and Mixbus |
| `.csv` | Not a DAW format. The honest fallback, and it opens in a spreadsheet |

**Escaping is the part that will be got wrong, so it is where the tests are.** XML entities for Cubase and
midnam; quoting and doubling for CSV; and sanitising for `.reabank`, which has **no escaping mechanism at
all** — a patch name containing a newline would silently corrupt the file.

**UI.** An "Export patch list…" button in the Library tab's folder row, beside Change and Refresh. The
library is not its source, but it is where the user is when thinking about patch organisation, and the
alternative — a second place in the window for one button — earns less than it costs. The command asks for
a format and a destination, then writes.

**It needs no instrument.** The preset list is built at start-up from this build's own data, so the export
works with nothing plugged in; only the user-memory names come from the device, and those are absent rather
than wrong when there is none.

**Tests.** Each writer against one fixture list containing a name with `&`, one with `"`, one with `,`, and
one non-ASCII name, asserting the exact bytes of a short document.

---

## Error handling, across all phases

The library's existing rules are kept and extended:

- **A stray or unreadable file costs that file only** and is logged, never thrown over. This already governs
  listing; it now governs scanning too.
- **A folder that cannot be enumerated does throw**, because "your library is empty" is a lie about a share
  that has gone away.
- **Anything that would destroy a version refuses instead** (phase 1).
- **A batch reports what failed, by name** (phase 2), rather than stopping at the first problem or claiming
  success.
- **A failed restore leaves the audition recoverable** (phase 3) rather than dropping what it was holding.

## Testing, across all phases

Every service named above is pure or takes a fake device, so all of it is reachable. `PatchHistory` is
tested against a GUID-named temp folder, the pattern `TestLibrarySettings` established. The view models get
no tests at all — which is the reason so little is left in them.
