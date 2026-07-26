# What other synth editors do that we don't

A gap analysis, not a roadmap. Written 2026-07-25 against the code as it stands. Compared against
Roland's own INTEGRA-7 Librarian and Editor, Sound Quest's Midi Quest, and Patch Base.

**Short answer:** we are ahead on *editing* and absent on *keeping*. Every competitor is an "editor
**and librarian**"; we are only the first half. Nothing in this application can write a sound to a
file — verified: no `StorageProvider`, no file pickers, no `.syx` handling anywhere in `Src/`.

---

## 1. Librarian — the biggest gap by a distance

**What we have.** `SaveUserTone` writes the edited tone into one of the instrument's own user slots.
That is the entire persistence story. It is also the top-level command list in full: Panic, Play
Note, Play Phrase, Stop Phrase, Rescan MIDI Devices, Save User Tone.

**What the others have.** Roland's editor collects favourites into folders saved as a file on the
computer, and stores Studio Sets and system settings as XML. Midi Quest is a full librarian across
1000+ instruments — banks, collections, drag-and-drop between slots.

**Missing here:**

- **Save/load a Studio Set to a file.** The most valuable single feature we lack: the Studio Set is
  the unit of work — 16 parts, their tones, EQ, chorus, reverb, master EQ. Losing it means redoing
  it.
- Save/load an individual tone to a file, and import a `.syx` from elsewhere.
- Bulk dump of user memory to an offline library, and restore.
- Bank management: browse what is stored on the device, rename, reorder, copy between slots. We can
  only *select* presets, never organise them.
- Search across a library. We search the device's preset list; there is no library to search.
- Favourites, tags, ratings.

Everything else in this document is smaller than this.

## 2. Undo, redo and compare

**What we have.** Nothing. Every edit goes straight to the instrument through the throttled writer.
There is no way back except remembering the old value.

**What the others have.** Undo/redo as a matter of course, plus an A/B compare against the stored
patch — the hardware's own Compare button, which the editor is expected to mirror.

**Missing here:**

- Undo/redo of parameter edits.
- A/B compare: edit buffer versus the stored version of the same patch.
- Snapshot slots, to park a sound while trying something else.

**Why this is tractable:** every write in the application funnels through a small number of choke
points — `ParamInt` / `ParamString` / `ParamBool`, and the `"ui2hw"` bus for the raw grids. An edit
journal added there would cover the whole application at once, rather than per editor.

## 3. A whole-instrument overview

**What we have.** One part at a time, reached from the left tab strip. There is no page that shows
all sixteen parts together.

**What the others have.** A mixer page is standard for multitimbral machines: level, pan, mute,
solo, output assign and sends for every part on one screen.

**Missing here:**

- A 16-part mixer strip. Every parameter it needs is already wrapped in
  `StudioSetPartEditorViewModel`.
- A key/velocity zone map across all sixteen parts — layering and splitting is currently invisible
  unless you open each part in turn and remember what you saw. `PmtZoneEditorControl` already draws
  exactly this shape for one part's zones.

For a 16-part module this is probably the largest *usability* gap, as opposed to the largest missing
feature.

## 4. Sound-design helpers

**What we have.** Copy / paste / init, but only at partial and drum-note level
(`SnsPartialClipboard`), and only within a running session.

**Missing here:**

- Init, randomise and copy at **tone** level, not just partial level.
- Copy a whole tone from one part to another.
- Constrained randomisation ("randomise the filter, leave the oscillator alone").
- Diff two patches and show what differs — natural given that every parameter is already addressable
  by path.
- Morph/interpolate between two sounds.

## 5. Performance and set lists

- **Set lists**: an ordered list of Studio Sets to step through on stage, with a next/previous.
- **MIDI learn**: bind a hardware controller to any on-screen control. Common in editors and
  entirely absent here.
- A virtual keyboard with velocity, pitch bend and modulation. We have per-tone note rails and a
  global Play Note / Play Phrase, which is less.

## 6. Integration

- **DAW plugin.** Midi Quest ships AU, VST3, VST2 and AAX. See `PLUGIN_FEASIBILITY.md` for what that
  would cost us — it is not cheap.
- **More than one instrument.** We assume a single INTEGRA-7 on a single port.
- **Text/JSON export of a patch**, for diffing and for putting sounds under version control. Unusual
  in commercial editors, and a natural fit for our parameter model — every value already has a
  stable path and a displayed form.
- Printable patch sheet.

## 7. Safety net

- **Automatic backup of user memory** before anything is written to it.
- **Verify**: compare what is on the device against a stored copy and report the differences.
- **Offline mode**: browse and prepare without the instrument connected.

---

## What we have that they largely don't

For balance, and because it is worth not regressing:

- **Graphical editing throughout** — envelopes, filter curve, EQ response, PMT/WMT key×velocity zone
  maps, the Motional Surround field. Most librarians are lists of numbers.
- **Expansion awareness** — SRX and ExSN board detection, with instrument and waveform lists filtered
  to what is actually loaded.
- **Live two-way sync** — edits made on the front panel appear in the UI, which is why the Studio Set
  resync work mattered.
- **A disciplined sysex layer** — leases and conversations (`docs/MIDI_DEVICE_ACCESS.md`), which is
  what makes bulk reads reliable rather than hopeful.

---

## If the aim is to close the gap

An order that front-loads the value:

1. **Studio Set save/load to a file.** Unlocks backup, sharing and version control, and is the
   prerequisite for everything else in section 1.
2. **Undo/redo and compare.** One central change; disproportionate effect on how the app feels.
3. **The 16-part mixer/overview page.** Mostly assembly from parts that already exist.
4. **Library management** on top of (1) — collections, favourites, search.

Sections 4–7 are each worth having, but none of them changes what the application *is* the way the
first two do.

---

Sources consulted: [Roland INTEGRA-7 Librarian and Editor](https://apps.microsoft.com/detail/9nqtvxg509hm),
[Midi Quest INTEGRA-7 editor/librarian](https://squest.com/Products/MidiQuest13/Instruments/RolandIntegra-7/index.html),
[Patch Base INTEGRA-7](https://coffeeshopped.com/patch-base/editor/roland/integra-7).
