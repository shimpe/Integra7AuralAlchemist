# Closing the librarian gap — programme plan

> **Not an implementation plan.** It sequences the work from `docs/FEATURE_GAP_ANALYSIS.md` into
> stages that each stand on their own. Each stage gets its own task-level plan in this directory when
> it is started — writing six detailed plans now would produce five stale ones.

**Goal:** turn an editor into an editor *and* librarian, in an order where every stage ships
something usable and the expensive stages inherit foundations from the cheap ones.

---

## Sequencing, and why this order

```
Stage 1  Studio Set snapshot files      ── foundation for 4, 6, 7; standalone value immediately
Stage 2  Undo / redo / compare          ── independent; one central change, whole-app effect
Stage 3  All-parts mixer & zone overview ── independent; assembly from existing view models
Stage 4  Library management              ── needs 1
Stage 5  Tone-level init / copy / randomise
Stage 6  Patch diff & text export        ── needs 1
Stage 7  Safety net: auto-backup, verify ── needs 1
Stage 8  Set lists, MIDI learn, multi-device
```

Stages 1–3 are the ones that change what the application *is*. Everything after is refinement.

---

## Stage 1 — Studio Set snapshot files

**Why first.** It is the only thing on the list that protects work already done. A Studio Set is the
unit of effort — 16 parts, their tones, EQ and effects — and today it exists nowhere but in the
instrument's volatile memory. It is also the substrate stages 4, 6 and 7 all build on.

**Scope.** Capture every domain that makes up a Studio Set to a file, and write one back.

| Block | Domains |
| --- | --- |
| Common | `StudioSetCommon`, `StudioSetCommonChorus`, `StudioSetCommonReverb`, `StudioSetCommonMotionalSurround`, `StudioSetCommonMasterEQ` |
| Per part ×16 | `StudioSetPart(i)`, `StudioSetPartEQ(i)`, `StudioSetMidi(i)` |

53 domains in total.

**Key decisions** (argued in the stage-1 plan):

- **Store displayed values, not raw sysex.** The whole application works in display space
  (`FullyQualifiedParameter.StringValue`), writes already convert on the way out, and a JSON file of
  `path → displayed value` is readable, diffable and greppable. A raw `.syx` export is a later
  addition for interchange with other tools, not the primary format.
- **Restore uses the bulk write.** `DomainBase.WriteToIntegraAsync(lease)` writes a domain's whole
  address range in one transmission. 53 of those, not ~1400 single-parameter writes.
- **Tones are referenced, not captured.** A Studio Set names each part's tone by bank/PC. If that
  points at a *user* tone that later changes, the sound changes. Stated as a limitation; capturing
  the temporary tone data is stage 1b, and is a much bigger file.

**Detailed plan:** `2026-07-25-studio-set-snapshot-files.md`.

## Stage 2 — Undo, redo and compare

**Why second.** Disproportionate effect on how the application feels, and it is one change rather
than twenty: every write already funnels through `ParamInt` / `ParamString` / `ParamBool` and the
`"ui2hw"` bus. An edit journal at those choke points covers the whole app at once.

**Scope.**

- An `EditJournal` recording (parameter path, domain key, old displayed value, new displayed value).
- Undo/redo commands that replay entries in reverse/forward through the same write path.
- Coalescing: a knob dragged for two seconds is one undo step, not two hundred. The existing
  per-key debounce in `ThrottledParameterWriter` gives a natural boundary.
- **Compare**: hold a snapshot of the part's tone as it was when loaded, and toggle between it and
  the edit buffer — the hardware's own Compare button. Stage 1's snapshot machinery does the
  capture; this only adds the toggle and the "which am I hearing" indicator.

**Risk to design around.** Inbound changes from the front panel must not enter the journal as user
edits — undo would then fight the hardware. The inbound path is distinct (`"hw2ui"` →
`ModifySingleParameterDisplayedValue`), so the journal must hook the *outbound* choke points only.

## Stage 3 — All-parts mixer and zone overview

**Why third.** The largest usability gap for a 16-part module, and almost entirely assembly: a
`StudioSetPartEditorViewModel` already exposes level, pan, mute, output assign and sends for a part,
and `PmtZoneEditorControl` already draws a key×velocity map.

**Scope.**

- A **mixer page**: one strip per part — level, pan, mute, solo, output assign, chorus/reverb sends,
  the part's tone name, and a click-through to that part's tab.
- A **layer map**: all sixteen parts' key and velocity ranges on one chart, so splits and layers are
  visible at a glance. `PmtZoneEditorControl` draws four zones today; this needs sixteen, read-only
  first, draggable later.
- Both are new top-level tabs beside "Parameters", not new sub-tabs.

**Open question for the spec.** Whether the mixer edits live (each fader writing immediately, as
everything else does) or stages changes. Live is consistent with the rest of the app; that is the
default unless there is a reason otherwise.

## Stage 4 — Library management

**Needs stage 1.** With snapshots on disk, this is the browsing layer over them.

- A library folder of snapshots, with names, categories and free-text notes.
- Search and filter across the library.
- Favourites/tags.
- Bulk dump: read every user Studio Set and user tone slot into the library in one pass, and restore
  a slot from it.
- Bank management on the device: rename, reorder, copy between user slots.

The bulk dump is the part with real device risk — hundreds of reads, and writes into user memory.
It wants its own plan and a dry-run mode.

## Stage 5 — Tone-level init, copy and randomise

Today `SnsPartialClipboard` gives copy/paste/init at *partial* and drum-note level only, in memory,
within one session.

- Init / copy / paste at whole-tone level, and copy a tone from one part to another.
- Constrained randomisation ("randomise the filter, leave the oscillator alone"), which needs a
  per-parameter notion of what a sensible random value is — the parameter database has ranges and
  reprs, so this is derivable rather than hand-authored.

## Stage 6 — Diff and text export

**Needs stage 1.** Given two snapshots, list what differs by path and displayed value. Falls out of
the stage-1 format almost for free, and makes "what did I actually change?" answerable — including
against a snapshot taken minutes earlier.

## Stage 7 — Safety net

**Needs stage 1.**

- Automatic snapshot before anything is written to user memory, kept in a rolling backup folder.
- Verify: read the device and diff it against a stored snapshot (stage 6 does the diffing).
- Offline mode: browse and edit a snapshot with no instrument connected, then push it when connected.
  This one is larger than it sounds — it means the UI must tolerate `Integra7` being null far more
  thoroughly than it does now.

## Stage 8 — Performance and integration

Grouped because each is small and none is foundational: set lists (an ordered list of snapshots with
next/previous), MIDI learn (bind a hardware controller to any on-screen control), multi-device
support, printable patch sheets, `.syx` import/export for interchange.

The DAW plugin is deliberately **not** in this list — see `docs/PLUGIN_FEASIBILITY.md` for why it is
a different kind of project.

## Not a stage — a startup that cannot hang

**Depends on nothing, and it is not part of Stage 7**, which was set aside. It shares one sentence with
Stage 7's offline mode and none of its cost.

`Integra7Api.CheckIdentityAsync` is awaited on the startup path with nothing bounding it, and
`MainWindow` is not shown until it returns. A device that is half present — enumerating, but blocking on
open — therefore hangs the application indefinitely on a blank screen, with a log that stops at
`Opening the MIDI ports.` and says nothing further. Hit for real on 2026-07-30, three launches, no window
within 90 seconds each time; a power cycle of the instrument cleared it.

What it wants: a timeout around the identity check, and a path that **still opens the window** when the
instrument does not answer. The application is deliberately usable with nothing connected — every editor
tolerates it, the library and the patch-list export work without a device — so this is the one case where
a missing instrument is worse than no instrument at all.

The reason this is not Stage 7's offline mode: that item is about editing a snapshot with no instrument
and pushing it later, which touches far more of the UI. This is about not blocking on a socket, and it
is a timeout plus one message.

---

## What could change this order

- If the instrument's user memory is nearly full or heavily curated, stage 4's bulk dump becomes more
  valuable than stage 2 and should move up.
- If undo turns out to be needed to make stage 3's mixer safe to use (sixteen faders, no way back),
  stage 2 and 3 merge.
- Stage 7's offline mode is the one item that could reasonably be argued into stage 1, since it
  shares the "app works from a snapshot rather than the device" machinery. It is kept separate
  because it touches far more of the UI than the file format does.
