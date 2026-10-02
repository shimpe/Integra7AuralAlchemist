# What other synth editors do that we don't

A gap analysis, not a roadmap. First written 2026-07-25, and rechecked against the code on 2026-10-02.
Compared against Roland's own INTEGRA-7 Librarian and Editor, Sound Quest's Midi Quest, and Patch
Base.

**Short answer:** when this was first written, the application was ahead on *editing* and absent on
*keeping* — it could not write a sound to a file at all. That gap is closed. There is now a library
of tone and Studio Set snapshots with search, metadata, version history and duplicate detection,
undo/redo and compare, a 16-part mixer and layer map, and tone-level copy, init, randomise, diff and
morph. What is left is mostly about *interchange and performance*: `.syx` import/export, organising
the instrument's own user memory, set lists, MIDI learn, more than one instrument, and running inside
a DAW.

Each section below keeps the original question and says what has since been built and what is still
missing.

---

## 1. Librarian — was the biggest gap, now mostly closed

**When first written:** `SaveUserTone` was the entire persistence story. Nothing could write a sound
to a file.

**Now:** the **Library** tab works over a folder of snapshot files (`SnapshotLibrary`, format v3). A
snapshot is a single tone or a whole Studio Set — 16 parts, their tones, EQ, chorus, reverb and
master EQ — saved as JSON with every parameter's raw and displayed value.

| Originally missing | Status |
| --- | --- |
| Save/load a Studio Set to a file | **Done** — save and load from the Library, or open a file. |
| Save/load an individual tone to a file | **Done** — load into the selected part, or audition it there first and get the part back afterwards. |
| Import a `.syx` from elsewhere | **Missing.** Our own format is JSON; there is no `.syx` reader or writer. |
| Bulk dump of user memory to an offline library | **Done, one way** — *Seed from instrument* sweeps the factory banks and every non-empty user slot into the library. Restoring a whole bank back to the instrument was costed and set aside (see below). |
| Bank management on the device: rename, reorder, copy between slots | **Missing.** The library can be organised freely; the instrument's own user memory can only be written one slot at a time with Save User Tone. |
| Search across a library | **Done**, including *Look inside patches*, which searches parameter values. |
| Favourites, tags, ratings | **Done**, along with categories and notes. Category, rating, favourite and tags can also be changed for many snapshots at once. |

Built since, and not on the original list: version history for every library write (restorable, and
deletes go to a history folder), duplicate detection with a tolerance, *Use as the init tone* per
engine, and patch-list export so a DAW shows the instrument's sounds by name.

## 2. Undo, redo and compare — done

**Now:** every edit is journaled (`EditJournal`), so **Undo** and **Redo** work across parameters and
editors. **Compare** rolls the whole session's edits back so the original can be heard, then forward
again — the hardware Compare button's job.

The original note on why this was tractable held: the journal sits at the write choke points
(`ParamInt` / `ParamString` / `ParamBool` and the `"ui2hw"` bus), so it covered every editor at once.

*Snapshot slots, to park a sound while trying something else* are covered by the library and by
copying a tone to another part; there is no separate quick-slot feature.

## 3. A whole-instrument overview — done

**Now:** the **Mixer** tab shows all sixteen parts plus the external input as channel strips (level,
pan, both effect sends, output assignment, mute and solo), and the **Layers** tab draws every part's
key and velocity range on one chart, editable by dragging, with click-to-audition at the note and
velocity under the pointer. Motional Surround and Master EQ have graphical pages of their own.

## 4. Sound-design helpers — done

| Originally missing | Status |
| --- | --- |
| Init, randomise and copy at tone level | **Done** — tones can be initialised, copied between parts and pasted. |
| Copy a whole tone from one part to another | **Done.** |
| Constrained randomisation | **Done** — *Randomise…* moves only the groups you tick, by a strength you choose. |
| Diff two patches | **Done** — the **Compare** tab, between library snapshots, files, or what is in the instrument now. |
| Morph between sounds | **Done** — the **Morph** tab blends two to seven tones of one engine on a pad, live, and can save the blend. |

## 5. Performance and set lists — still open

- **Set lists**: an ordered list of Studio Sets to step through on stage. **Missing.**
- **MIDI learn**: bind a hardware controller to an on-screen control. **Missing.**
- **A virtual keyboard**: partly there. Each tone editor has a playable note rail where the click
  position sets the velocity and holding sustains, and drum editors label it with the kit's drums.
  There is no pitch bend or modulation.

## 6. Integration — partly done

- **DAW plugin.** **Missing.** `PLUGIN_FEASIBILITY.md` costs it; it is not cheap.
- **Patch names in a DAW.** **Done** — *Export a patch list* writes the instrument's sounds in
  formats DAWs read, so a track's program menu shows names instead of numbers. Not on the original
  list.
- **More than one instrument.** **Missing.** We still assume a single INTEGRA-7 on a single port.
- **Text/JSON export of a patch.** **Done** — a snapshot *is* one: every parameter by path, with its
  raw and displayed value, suitable for diffing and version control.
- **Printable patch sheet.** **Declined** (see below).

## 7. Safety net — declined

- **Automatic backup of user memory before writing to it.** Not built. Writes into the *library*
  keep the version they replace; Save User Tone into the instrument's own memory does not.
- **Verify** against a stored copy. Nearly free if wanted: it is the Compare tab with one side pinned
  to a library snapshot and the other read from the instrument, which already works by hand.
- **Offline mode.** Partly true already: the Library, Compare-from-files and metadata editing do not
  need the instrument. Editing a sound does.

---

## Considered and set aside

These were talked through on 2026-07-28 and deliberately not built. They are recorded so that they are
not proposed again as though they were new.

- **Section 7 as a whole.** Not felt to be needed.
- **Restoring a bulk dump into user memory.** The INTEGRA-7 has no addressable user memory: a dump
  has to load each slot onto a part and capture it, and a restore writes into user memory, where a
  wrong slot number destroys a patch. The reading half exists as *Seed from instrument*; the writing
  half is not worth that risk.
- **Printable patch sheets.** A snapshot already carries every parameter as text, so a plain-text
  sheet adds nothing, and a printable one needs a curated per-engine selection — everything captured
  runs to about 200 pages for a PCM drum kit.

## What we have that they largely don't

For balance, and because it is worth not regressing:

- **Graphical editing throughout** — envelopes, filter curves, EQ response, PMT/WMT key × velocity zone
  maps, the layer map, the Motional Surround field, the morph pad. Most librarians are lists of
  numbers.
- **A library that knows what is inside its patches** — parameter-value search, duplicate detection
  with a tolerance, parameter-level diffs between any two sounds.
- **Expansion awareness** — SRX and ExSN board detection, with instrument and waveform lists filtered
  to what is actually loaded, and patches that want an absent board flagged rather than reset.
- **Live two-way sync** — edits made on the front panel appear in the UI.
- **A disciplined sysex layer** — leases and conversations (`docs/MIDI_DEVICE_ACCESS.md`), which is
  what makes bulk reads such as the seeding sweep reliable rather than hopeful.

---

## If the aim is to close the remaining gap

The order from the first version of this document (Studio Set files, undo/compare, the mixer, library
management) has been worked through. What is left, roughly by value:

1. **`.syx` import and export.** Interchange with other tools and with sounds shared online, which
   are almost always `.syx`. The JSON format already holds everything a `.syx` would need.
2. **Set lists.** Small on top of the library, since a Studio Set snapshot is already a file.
3. **MIDI learn.** Touches every control, so it wants the same single-choke-point treatment the
   journal got.
4. **Multi-device and the DAW plugin.** Both large; the plugin is costed in `PLUGIN_FEASIBILITY.md`.

On-device bank management (rename, reorder, copy between user slots) is the one librarian feature
still missing, but every operation in it writes into user memory, the risk that set the bulk restore
aside.

---

Sources consulted: [Roland INTEGRA-7 Librarian and Editor](https://apps.microsoft.com/detail/9nqtvxg509hm),
[Midi Quest INTEGRA-7 editor/librarian](https://squest.com/Products/MidiQuest13/Instruments/RolandIntegra-7/index.html),
[Patch Base INTEGRA-7](https://coffeeshopped.com/patch-base/editor/roland/integra-7).
