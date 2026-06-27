# SuperNATURAL Drums — Friendly Editor Design

**Status:** Design (awaiting review)
**Date:** 2026-06-27
**Author:** brainstorming session (Integra7AuralAlchemist)

## Goal

Add a friendly visual editor for the INTEGRA-7 **SuperNATURAL Drum Kit** engine, consistent with
the existing SN-S / SN-A / PCM Synth editors. The raw parameter tabs stay as the "Advanced" fallback.

## Guiding principle

**Consistency with the previous editors is the primary driver.** Reuse the established kit (header
recipe, left-selector / right-tabs shape, searchable picker, generalized MFX panel, copy/paste,
"Advanced…" navigation). Introduce new UI only where SN-Drums has no precedent — chiefly the
**note-navigation rail**.

## Engine shape (verified)

`Constants.NO_OF_PARTIALS_SN_DRUM = 62`, `FIRST_PARTIAL_SN_DRUM = 27` → 62 drum slots on MIDI notes
**27–88**. Domains (in `Integra7Domain.cs`):

| Accessor | Offset2 path prefix | Notes |
|---|---|---|
| `SNDrumKitCommon(part)` | `SuperNATURAL Drum Kit Common/` | kit-level (Kit Name, Kit Level, Ambience Level, Phrase Number, TFX Switch) |
| `SNDrumKitCommonMFX(part)` | `SuperNATURAL Drum Kit Common MFX/` | per-type conditional set (reuse the generalized MFX panel) |
| `SNDrumKitCompEQ(part)` | `SuperNATURAL Drum Kit Common Comp-EQ/` | **6** comp + 6 EQ units (~84 params) |
| `SNDrumKitPartial(part, partial)` | `SuperNATURAL Drum Kit Partial/` | 62 notes × **13** params |

**Per-note (partial) params (13):** `Inst Number` (drum sound; `SN_DRUM_KIT_WAVE_NAME`, ~513 instruments),
Level, Pan, Chorus Send Level, Reverb Send Level, Tune, Attack, Decay, Brilliance, Variation,
Dynamic Range, Stereo Width, Output Assign.

**Comp-EQ unit n (1..6):** Comp{n} Switch / Attack Time / Release Time / Threshold / Ratio / Output Gain;
EQ{n} Switch / Low Freq / Low Gain / Mid Freq / Mid Gain / Mid Q / High Freq / High Gain.

**Key facts that shape the design:**
- Per-note editing is simple (13 params); the genuinely new piece is **navigating 62 notes**.
- `Inst Number` has ~513 options → use the searchable picker (the PCM wave-field pattern).
- The Comp-EQ has **6 units** → a unit selector (1..6) + the selected unit's controls, rather than
  84 params at once.

## Architecture & reuse

New: `Src/ViewModels/SNDrumKitEditorViewModel.cs`, `Src/Views/SNDrumKitEditorView.axaml`, plus
`SNDrumNoteViewModel` (rail item), `SNDrumNoteEditorViewModel` (selected-note editor), and
`SNDrumCompEqPanelViewModel`/view. `PartViewModel` builds it with the same navigation callback used
for the other engines. `MainWindow.axaml` gains the friendly **Editor** tab (default, carrying the
drum kit's `ToneTypeStr` tag) + re-tagged raw "Advanced — …" tabs (`SN-D-*`).

| Component | Status |
|---|---|
| `ParamInt/String/Bool` wrappers, `MidiNote`, `SnsPartialClipboard` | reuse as-is |
| `MfxPanelViewModel` + view (generalized) | reuse as-is (`SNDrumKitCommonMFX`) |
| Header recipe (badge/name/level/phrase row) | reuse pattern |
| Searchable drum-sound picker (AutoCompleteBox + browse arrow) | reuse the PCM wave-field pattern |
| Note-navigation rail (vertical keyboard list) | **new** |
| Selected-drum editor | **new** (simple — 13 params) |
| Comp-EQ panel (6-unit selector) | **new** |

## Layout

### Header (`Common`)
Badge "SN Drums" · Kit Name · Kit Level · Ambience Level · Phrase Number · TFX Switch · "Advanced common…".

### Left: the note rail
A `ListBox` over 62 `SNDrumNoteViewModel`, ordered **note 88 (top) → 27 (bottom)** so the low notes
sit at the bottom (true vertical-piano orientation). Each row's `ItemTemplate`: a white/black key
cell (color from `MidiNote.IsBlack`; the **C** rows — `MidiNote.IsC` — show their octave label via
`MidiNote.Name`) + the drum sound name. Selected row highlighted; `SelectedItem` drives the editor.

**Performance note:** `SNDrumNoteViewModel` is intentionally lightweight — it exposes the note's
metadata and the drum-sound *name* read directly from the `Inst Number` FQP's `StringValue` (no
`ParamString`/513-item options list per row). The full 13-param editor is built only for the
selected note.

### Right: TabControl `Drum · Comp-EQ · FX`
- **Drum** (`SNDrumNoteEditorViewModel`, rebuilt when the rail selection changes): the searchable
  **Sound** picker (AutoCompleteBox over `Inst Number` + browse arrow), then Level, Pan, Tune,
  Attack, Decay, Brilliance, Variation, Dynamic Range, Stereo Width, Chorus/Reverb sends, Output
  Assign. Copy / Paste / Init drum (over the 13 params, via `SnsPartialClipboard`). Changing the
  sound updates the rail row's name (shared FQP).
- **Comp-EQ** (`SNDrumCompEqPanelViewModel`): a unit selector (1..6); the selected unit shows its
  compressor (On, Threshold, Ratio, Attack, Release, Output Gain) and 3-band EQ (On, Low/Mid/High
  Freq + Gain, Mid Q).
- **FX:** the generalized MFX panel (`SNDrumKitCommonMFX`), horizontal-scroll disabled so the grid
  wraps.

## Friendly vs. advanced

The friendly editor surfaces the musically-useful params. The full raw set (all 62 partials, all
Comp-EQ units, Common, MFX) stays on the raw "Advanced — …" tabs reached via the "Advanced …"
buttons, mirroring the other engines.

## Phases

Each phase is its own spec→plan→subagent build→finishing cycle.

1. **Skeleton + rail + FX** — editor VM/View, header, the 62-note rail (`SNDrumNoteViewModel` +
   vertical-keyboard `ListBox`) with selection, the MFX panel, and advanced-tab wiring.
2. **Drum tab** — `SNDrumNoteEditorViewModel` (searchable Sound picker + the sound params) +
   Copy/Paste/Init.
3. **Comp-EQ tab** — `SNDrumCompEqPanelViewModel` + view (6-unit selector + comp + 3-band EQ).

## Out of scope

PCM **Drum** Kit (separate engine, 88 notes — later). The note rail does not (yet) trigger sounds;
it only navigates.
