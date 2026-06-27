# PCM Drums Friendly Editor — Design Spec

**Date:** 2026-06-27
**Status:** Approved (brainstorm)
**Engine:** PCM Drum Kit (preset tone type `PCMD`)

## Goal

A friendly, musical visual editor for the INTEGRA-7 **PCM Drum Kit** engine — one editor per part — consistent with the existing PCM Synth and SuperNATURAL Drums editors. It surfaces the musically useful subset of each drum key's ~148 parameters cleanly, while the full set stays available in the Advanced raw tabs.

## Context

- 88 drum keys per kit, MIDI notes **21 (A0) → 108 (C8)**. `Constants.FIRST_PARTIAL_PCM_DRUM = 21`, `NO_OF_PARTIALS_PCM_DRUM = 88`.
- Domain accessors already exist: `PCMDrumKitCommon`, `PCMDrumKitCommon2`, `PCMDrumKitCommonMFX`, `PCMDrumKitCompEQ`, `PCMDrumKitPartial(part, partial)`.
- A PCM drum **partial** (one key) is the richest drum: ~148 params — general/identity, four velocity-switched WMT waves, and Pitch / TVF / TVA envelopes.
- This editor is the natural next step after the merged SuperNATURAL Drums editor and reuses most of its machinery.

## Overall structure

Mirrors the SN-Drums editor shell:

- **Header** — badge "PCM Drums", read-only **Kit Name** (`PCM Drum Kit Common/Kit Name`), **Kit Level** (0–127), **Phrase Number** (`Common 2`, enum), **Tone FX** switch (`Common 2/TFX Switch`), and an **"Advanced common…"** button. (No Ambience — SN-only.)
- **88-key note rail** (left, fixed-width column): notes 21/A0 → 108/C8, **low note at the bottom**. Each row: a sideways piano key with C-note labels (`MidiNote`), the note name, and the key's **Partial Name**. Clicking a row auditions the note (NoteOn/NoteOff on the part channel). Lightweight row VM (reads only Partial Name) + on-selection-rebuild — no 88× pre-build.
- **Right-side tabs:** **Drum** (per-key editor, default), **Comp-EQ**, **FX**.
- **Advanced raw tabs** retagged `Advanced — …` (Common, Common 2, MFX, Comp-EQ, Partials) with the friendly Editor tab as default, exactly like SN-Drums. The Partials raw tab tracks `AdvancedPartialIndex`.

## Per-key Drum editor — five sub-tabs

Selecting a key rebuilds a per-key editor with sub-tabs; default lands on **Wave**.

### Wave (WMT)
The defining tab. Up to four waves (`WMT1..4`) switched/faded by velocity.
- **Graphic velocity map** (0–127): four draggable layer bands showing each WMT's velocity range with faded edges; overlaps visible at a glance. Velocity-only adaptation of the PCM Synth zone-editor approach.
- Shared **WMT Velocity Control** selector.
- **WMT 1–4** selector; the selected layer's detail panel:
  - Wave Switch; searchable **Wave picker** (Group Type / Group ID / Wave Number L (Mono) / R) via the existing `AutoCompleteBox` + browse-arrow pattern.
  - Level, Gain, Pan, Coarse Tune, Fine Tune.
  - FXM Switch / Color / Depth; Tempo Sync.
  - Velocity Range Lower/Upper; Velocity Fade Width Lower/Upper; Alternate/Random Pan Switch.

### Pitch
- Pitch Env Depth, Pitch Env Velocity Sens, Time 1/4 Velocity Sens.
- Draggable **5-level / 4-time** pitch envelope (`MultiStageEnvelopeControl`, bipolar): Levels 0–4, Times 1–4.

### Filter (TVF)
- Filter Type, Cutoff Frequency, Resonance; Cutoff Velocity Curve/Sens, Resonance Velocity Sens.
- `FilterCurveControl` preview.
- TVF Env Depth, Env Velocity Curve/Sens; **5-level / 4-time** TVF envelope.

### Amp (TVA)
- Partial Level; TVA Velocity Curve, TVA Velocity Sens; Time 1/4 Velocity Sens.
- **3-level / 4-time** TVA envelope (Levels 1–3, releases to silence) via `MultiStageEnvelopeControl` with fixed endpoints.

### Setup
Identity & routing:
- Partial Name (editable; feeds the rail label), Assign Type, Mute Group, Partial Env Mode, One Shot Mode, Pitch Bend Range, Receive Expression, Receive Sustain.
- Pan, Coarse/Fine Tune, Output Assign, Output Level, Chorus Send, Reverb Send, Alternate Pan Depth, Random Pan Depth, Random Pitch Depth.

Reserved/undocumented fields are skipped. All sliders use `ValueSlider` (numeric value + unit).

## Comp-EQ and FX

- **Comp-EQ** — identical 6-unit compressor/EQ structure to SN-Drums (`Comp1..6`, `EQ1..6`, 84 params). Generalize `SNDrumCompEqUnitViewModel` / `SNDrumCompEqPanelViewModel` to accept the domain-path prefix as a constructor argument; both drum engines share them (SN-Drums passes its existing prefix). Pure refactor.
- **FX** — the shared, engine-agnostic `MfxPanelViewModel` / `MfxPanelView`, built from `PCMDrumKitCommonMFX`.

## Data flow

Built per part in `PartViewModel.InitializeParameterSourceCachesAsync` with nav-to-raw-tab `(tag, partialIdx)` and play-note callbacks. Values flow through `ParamInt/String/Bool` over the 250 ms per-key `ThrottledParameterWriter` with the `_suppress` echo-guard; re-read domain on parent/discriminator writes. Selecting a key disposes the old per-key editor and builds a fresh one.

**New control:** `WmtVelocityMapControl` (Control subclass + Render + StyledProperties) with a pure `WmtVelocityMapping` helper that maps the four WMTs' Velocity Range Lower/Upper + Fade Width Lower/Upper to/from screen bands. Follows the visual-editor pattern: per-key throttled `Subject`, `_suppress` sync, origin-based body drag.

**Copy/paste/init:** a per-key clipboard (path → value) mirroring SN-Drums' `DrumClipboard` — copy a key's friendly params, paste to another key, init to a sane default.

## Error handling

Conditional params (e.g. FXM Color/Depth gated by FXM Switch; WMT detail gated by Wave Switch) follow existing infrastructure: `GetRelevantParameters`, debounced writes, no assertions on the write/context race. Auditioning is best-effort (swallow MIDI errors).

## Reused from earlier work

`MultiStageEnvelopeControl` + `PcmEnvelopeMapping`, `FilterCurveControl`, the searchable wave picker, `ValueSlider`, `MidiNote`, click-to-audition, the zone-editor drag approach, and the SN-Drums rail + on-selection-rebuild + clipboard patterns. No hardcoded colors in XAML — color/brush resources in `App.axaml`.

## Testing

- NUnit tests for the pure `WmtVelocityMapping` helper (round-trip + edge cases), like `PcmEnvelopeMapping` / `PmtZoneMapping`.
- The 237-test baseline stays green at every phase; Comp-EQ generalization must not regress SN-Drums.
- Build/test with the user-local .NET 10 SDK in Release. No `--no-verify`.

## Build approach (multi-phase)

A multi-phase build like the PCM Synth editor; the writing-plans step breaks each into bite-sized tasks:

1. **Skeleton** — header + 88-key rail + tab shell + click-to-audition; `PartViewModel` wiring; friendly Editor tab + Advanced retag in `MainWindow`.
2. **Setup + Amp tabs** — establishes the per-key VM with the simpler identity/routing + TVA params.
3. **Wave / WMT tab** — the velocity-map control + WMT detail + wave picker.
4. **Pitch + Filter tabs** — envelopes + filter curve.
5. **Comp-EQ generalization + FX wiring + polish** — copy/paste/init, finishing.

## Out of scope

Full per-key parameter coverage in the friendly tabs (the Advanced raw tabs already provide it); changes to other engines beyond the shared Comp-EQ generalization.
