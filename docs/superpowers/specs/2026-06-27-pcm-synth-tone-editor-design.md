# PCM Synth Tone — Friendly Editor Design

**Status:** Design (awaiting review)
**Date:** 2026-06-27
**Author:** brainstorming session (Integra7AuralAlchemist)

## Goal

Add a friendly, musical visual editor for the INTEGRA-7 **PCM Synth Tone** engine, consistent
with the existing SuperNATURAL Synth (SN-S) and SuperNATURAL Acoustic (SN-A) editors. The raw
parameter tabs stay as the "Advanced" fallback.

## Guiding principle

**Consistency with the previous editors is the primary driver.** Mirror SN-S wherever possible
(header recipe, partial rack + audition, Sound/Motion/FX/Response tabs, draggable controls,
Copy/Paste/Init, "Advanced…" navigation). Introduce genuinely new UI only where PCM has no
SN-S precedent.

## Engine shape (verified)

`Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE = 4`. Domains (in `Integra7Domain.cs`):

| Accessor | Offset2 path prefix | Params |
|---|---|---|
| `PCMSynthToneCommon(part)` | `PCM Synth Tone Common/` | 68 |
| `PCMSynthToneCommon2(part)` | `PCM Synth Tone Common 2/` | 56 (≈50 Reserved) |
| `PCMSynthToneCommonMFX(part)` | `PCM Synth Tone Common MFX/` | (per-type conditional set) |
| `PCMSynthTonePMT(part)` | `PCM Synth Tone Partial Mix Table/` | 41 |
| `PCMSynthTonePartial(part, partial)` | `PCM Synth Tone Partial/` | 142 × 4 |

**Key facts that shape the design:**

- PCM partials are shaped almost exactly like SN-S partials (TVP/TVF/TVA envelopes, two LFOs,
  step LFO, per-partial mod-matrix switches, output/pan/sends).
- **No conditional gating** in PCM partials (`par:` count = 0) — every partial param is always
  valid. No `DiscriminatedParamSectionViewModel` needed in the rack (simpler than MFX/SN-A).
- The **two genuinely new pieces**: the PCM **wave picker** (replaces SN-S's synth oscillator)
  and the **PMT zone editor** (key/velocity splits — no SN-S equivalent).
- PCM envelopes are **rate/level** (4 times + up to 5 levels), **not ADSR**. SN-S's
  `DualAdsrEnvelopeControl`/`PitchEnvelopeControl` do **not** fit; a new graphical multi-stage
  envelope control is required (styled identically with the existing envelope brushes).

## Architecture & reuse

New: `Src/ViewModels/PCMSynthToneEditorViewModel.cs`, `Src/Views/PCMSynthToneEditorView.axaml`,
plus per-partial `Src/ViewModels/PCMPartialViewModel.cs` (mirrors `SNSPartialViewModel`).
`PartViewModel` builds it with the same navigation callback used for SN-S/SN-A "Advanced…"
buttons. `MainWindow.axaml` gains friendly + advanced tabs with tags:
`PCM-SYN` (friendly Editor, default), `PCM-SYN-COMMON`, `PCM-SYN-PARTIAL`, `PCM-SYN-MFX`,
`PCM-SYN-PMT` (the existing raw `ParameterCollection` tabs, renamed "Advanced — …").

| Component | Status |
|---|---|
| `ParamInt/ParamString/ParamBool` wrappers (`SynthParam.cs`) | reuse as-is |
| Partial-rack `ListBox` + Solo/Mute audition overlay | reuse pattern (4 cards) |
| `FilterCurveControl` (draggable cutoff/res) | reuse as-is |
| `MfxPanelViewModel` + `MfxPanelView` (generalized) | reuse as-is (`PCMSynthToneCommonMFX`) |
| Header recipe (badge/name/level/voice/glide/phrase row) | reuse pattern |
| Copy / Paste / Init partial (`SnsPartialClipboard`) | reuse pattern |
| `LfoPanelViewModel` | **generalize** — parameterize path prefix + leaf-name scheme |
| `DualAdsrEnvelopeControl` / `PitchEnvelopeControl` | **does not fit** — new control needed |
| PCM wave picker | **new** |
| PMT 2-D zone editor | **new** |
| Multi-stage (rate/level) envelope control | **new** |

## Layout (mirrors SN-S)

### Header (tone-level, `Common` + `Common 2`)
Badge "PCM Synth" · Tone Name · Level · Voice (Mono-Poly + Legato) · Glide (Portamento Switch +
Time) · Vintage Drift (Analog Feel) · Octave/Bend (Octave Shift, Pitch Bend Range Up/Down) ·
"Advanced common…" button. Second row: **Character** strip — tone-wide Cutoff Offset, Resonance
Offset, Attack Time Offset, Release Time Offset (same idea as SN-A's Character). Third row:
Phrase (Phrase Number) + Phrase Octave (Phrase Octave Shift). Pan + Coarse/Fine Tune included
in the header cluster.

### Left: partial rack (4 cards)
Each card: Title, Solo/Mute (audition) + On toggle, wave name summary, Level/Pan, filter
summary, and the small filter-shape + envelope preview thumbnails (envelope preview uses the new
multi-stage preview rendering). Same selection/audition behaviour as SN-S.

### Right: TabControl `Sound · Motion · Zones · FX · Response` + Copy/Paste/Init row

**Sound** (selected partial):
1. **Wave** (new picker) — Bank (Wave Group Type: Internal/SRX), searchable wave field
   (`AutoCompleteBox` over `Wave Number L (Mono)` / `PARTIAL_WAVEFORMS`, ~1083 entries),
   **Stereo** toggle revealing Wave R, and an advanced row (Wave Gain, FXM Switch/Color/Depth,
   Pitch Keyfollow). Stereo hidden by default.
2. **Pitch** — graphical bipolar pitch envelope (new control) + Coarse/Fine Tune; tone
   Octave/Bend on the right (like SN-S).
3. **Filter (TVF)** — TVF Filter Type, draggable cutoff/resonance curve (`FilterCurveControl`),
   cutoff keyfollow.
4. **Amp (TVA)** — Partial Level, Pan, Bias.
5. **Envelopes** — dual multi-stage envelope (TVA + TVF on shared time axis, new control) with a
   numeric Time1-4 / Level row beneath (consistent with SN-S's graphical + numeric pattern).

**Motion** — LFO 1 + LFO 2 panels (generalized `LfoPanelViewModel`): Waveform, Rate, Fade Time,
Key Trigger, and Pitch/TVF/TVA/Pan depths. Step LFO and Offset/Rate-Detune/Delay/Keyfollow stay
behind "Advanced partial…".

**Zones (PMT)** — the new **2-D key×velocity map**: velocity (vertical) × keyboard (horizontal),
one draggable/resizable rectangle per partial. Each rectangle's label shows its velocity
min/max and keyboard range. Fade widths shaded at the edges. A **numeric row** beneath gives
precise values (Partial Switch, Keyboard Range Lo/Hi + Fade, Velocity Range Lo/Hi + Width).
Above the map: per-pair Structure Type & Booster (1&2, 3&4) and the PMT Velocity-Control switch.

**FX** — generalized MFX panel (`PCMSynthToneCommonMFX`), horizontal-scroll disabled so the grid
wraps (per the MFX layout lesson).

**Response** — velocity / aftertouch / keyfollow grid: TVF Cutoff Velocity Sens, TVA Velocity
Sens, TVF Cutoff Keyfollow, etc. — same grid shape as SN-S's Response tab.

## Friendly vs. advanced split

The friendly editor surfaces musically-important params. Everything else — the full 142/partial,
the tone-level Matrix Control (Common: 4 controls × Source + 4×Dest/Sens = 32 params), per-partial
Control 1-4 Switch 1-4, Step LFO, all Reserved/fine-velocity params — stays on the raw
"Advanced — …" tabs, reached via the same "Advanced …" buttons SN-S uses.

## New components — detail

### Wave picker
- `Wave Group Type` → `INT_SRX_RES` (Internal / SRX) as a 2-pill toggle.
- `Wave Number L (Mono)` rendered via `AutoCompleteBox` filtering `PARTIAL_WAVEFORMS` by substring;
  selection stores the int index. `Wave Number R` shown only when **Stereo** is on (default off;
  when off, leave R untouched / mirror L per current hardware value).
- Advanced row: `Wave Gain`, `Wave FXM Switch`/`Color`/`Depth`, `Wave Pitch Keyfollow`,
  `Wave Tempo Sync`.

### Multi-stage envelope control (`MultiStageEnvelopeControl`)
- Renders a 4-segment rate/level envelope: points L0→L1 (T1) → L2 (T2) → L3 (T3, sustain) → L4
  (T4, release). Draggable points; same brushes as the existing envelope controls.
- Bindable: `Time1..4`, `Level0..4`. TVA variant fixes L0 = L4 = 0 and exposes L1-L3
  (`TVA Env Level 1/2/3`); TVF/Pitch use L0-L4. Pitch variant is bipolar (sharp/flat).
- A **dual** mode (or two overlaid instances) renders TVA + TVF on shared axes, mirroring SN-S's
  `DualAdsrEnvelopeControl`.

### PMT 2-D zone editor (`PmtZoneEditorControl`)
- Canvas with 4 colored rectangles (one per partial), X = note (0-127), Y = velocity (1-127).
- Drag to move, drag edges to resize; fade widths (`Keyboard Fade Width Lower/Upper`,
  `Velocity Width Lower/Upper`) shaded as gradient margins.
- Per-key throttled writes via the visual-editor pattern (per-rectangle `_suppress` echo guard,
  `Subject` throttle) — see memory `visual-editor-pattern`.
- Label inside each rectangle: `Px  vel lo–hi  key lo–hi`. Numeric row beneath for exact entry.

## Phase decomposition

Each phase is its own spec→plan→subagent-driven build→finishing-a-development-branch cycle.

1. **Skeleton** — editor VM/View, header (Common/Common2), 4-partial rack + audition, tab shell,
   advanced-tab wiring in `MainWindow`/`PartViewModel`, Copy/Paste/Init.
2. **Sound tab** — wave picker (new), Pitch/Filter/Amp sections, the new multi-stage envelope
   control (single + dual), numeric rows.
3. **Motion tab** — generalize `LfoPanelViewModel`; LFO 1 + LFO 2 panels.
4. **Zones tab** — PMT 2-D zone editor control (new) + numeric row + Structure/Booster + Velocity
   Control.
5. **FX + Response** — MFX panel reuse + response grid.

## Testing

NUnit (existing suite, 185 tests). Per phase: pure/testable logic gets unit tests —
`SnsPartialClipboard`-style snapshot/apply for the PCM partial, wave-name filtering, PMT
zone↔param mapping math (note/velocity ↔ pixel, fade-width clamping), multi-stage envelope
point↔param mapping. View wiring verified by build + manual hardware check (consistent with how
SN-S/SN-A phases were validated).

## Out of scope

PCM **Drum** Kit (separate engine, later). SN-Drums. The tone-level Matrix Control friendly UI
(stays advanced-only for now).
