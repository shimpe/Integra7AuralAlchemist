# SuperNATURAL Synth (SN-S) Editor — Design (Phase 2: Filter, Pitch & Combined Envelopes)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** Phase 1 (`docs/superpowers/specs/2026-06-26-supernatural-synth-editor-design.md`), branch `sns-editor`.

## 1. Goal

Add the Filter and Pitch shaping to the friendly SN-S editor, and make envelope *interaction*
legible. The centerpiece, per user direction: the **Amp and Filter envelopes are drawn on a
single graph that shares one X (time) and Y (0–127 level) axis**, so you can see how they line
up in time and level. Everything reuses the Phase-1 infrastructure (param wrappers, throttled
writer, live FQP binding, color resources) — see the Phase-1 spec and memory
`parameter-domain-infrastructure`, `sn-s-tone-parameters`, `visual-editor-pattern`,
`no-hardcoded-colors-in-xaml`.

## 2. Decisions locked during brainstorming

1. **Amp + Filter envelopes overlaid on one shared-axis graph**, color-coded (Amp = blue,
   Filter = amber), both editable. A small **Amp / Filter toggle** picks which envelope the
   keyboard edits and brings it to the front; a pointer drag grabs the **nearest handle of
   either** envelope. Numeric A/D/S/R boxes for both beneath the graph.
2. **Filter Env Depth** (−63…+63) is a **bipolar control beside the graph**, not part of the
   plotted shape (the graph shows the raw ADSR shapes; depth scales the cutoff effect).
3. **Pitch envelope is separate** — it is **bipolar** (Attack/Decay + depth around a center
   line, no sustain/release), so it gets its **own compact bipolar graph** in the Pitch
   section (center = no change, up = sharp, down = flat).
4. Layout: compact parameter sections on top (Oscillator, Pitch, Filter, Amp), the **big
   shared-axis Amp+Filter envelope filling the bottom**.
5. **No hardcoded colors** — all new brushes are `App.axaml` resources; envelope controls take
   brush `StyledProperty`s set from those resources. The Phase-1 `AdsrEnvelopeControl` is
   retrofitted with brush properties so the card previews can show an amber filter preview.

## 3. SN-S parameters used (verified; lookup path = `ParSpec.Path` = `<prefix>/<name>`)

**Partial** (`SuperNATURAL Synth Tone Partial/…`, via `SNSynthTonePartial(part, partial)`):
- Filter: `Filter Mode` (enum FILTER_MODE: Bypass, Low pass, High pass, Band pass, Peaking,
  Low pass 2, Low pass 3, Low pass 4), `Filter Slope` (display `-12`/`-24` dB),
  `Filter Cutoff` (0–127), `Filter Resonance` (0–127), `Filter Cutoff Keyfollow` (−100..100),
  `HPF Cutoff` (0–127), `Filter Env Velocity Sens` (−63..63).
- Filter envelope: `Filter Env Attack Time`, `Filter Env Decay Time`, `Filter Env Sustain
  Level`, `Filter Env Release Time` (0–127), `Filter Env Depth` (−63..63).
- Pitch envelope: `OSC Pitch Env Attack Time` (0–127), `OSC Pitch Env Decay` (0–127),
  `OSC Pitch Env Depth` (−63..63).

**Common** (`SuperNATURAL Synth Tone Common/…`, via `SNSynthToneCommon(part)`):
- `Octave Shift` (−3..+3), `Pitch Bend Range Up` (0–24), `Pitch Bend Range Down` (0–24).

(All bipolar/shifted ranges are display-space passthrough handled by the existing `ParamInt`
clamp + the domain converter, exactly as in Phase 1.)

## 4. New / changed components

### 4a. Color resources (`App.axaml`)
Add brushes: `SnAmpEnvelopeBrush` (blue, reuse the Phase-1 line blue), `SnAmpEnvelopeFillBrush`,
`SnFilterEnvelopeBrush` (amber, e.g. `#E0A23D`), `SnFilterEnvelopeFillBrush` (translucent amber),
`SnPitchEnvelopeBrush`, `SnEnvelopeAxisBrush`, `SnEnvelopeGridBrush`, `SnEnvelopeBackgroundBrush`,
`SnFilterCurveBrush`. Referenced from XAML; never inlined.

### 4b. `AdsrEnvelopeControl` retrofit (`Src/Controls/AdsrEnvelopeControl.cs`)
Replace the private static brush fields with bindable `StyledProperty<IBrush>`:
`LineBrush`, `FillBrush`, `BackgroundBrush`, `GridBrush`, `AxisBrush`, `HandleBrush`,
`FocusBrush` (defaults preserve today's look). `Render` reads the properties. This lets the
card previews bind `LineBrush`/`FillBrush` from resources (amp = blue, filter = amber). No
behavior change otherwise. Existing mapping/axis logic untouched.

### 4c. `DualAdsrEnvelopeControl` (new, `Src/Controls/DualAdsrEnvelopeControl.cs`)
The shared-axis Amp+Filter graph. Custom `Control`, `Render` override.
- Bindable ints: `AmpAttack/AmpDecay/AmpSustain/AmpRelease`, `FilterAttack/FilterDecay/
  FilterSustain/FilterRelease` (0–127, TwoWay).
- Bindable `ActiveEnvelope` (enum-like int: 0 = Amp, 1 = Filter, TwoWay) — the front/keyboard
  envelope.
- Bindable brushes for amp/filter line+fill, axis, grid, background (from resources).
- Render: draw the shared axes + ticks every 16 (reuse the Phase-1 axis logic via a shared
  helper — factor `DrawAxes` into a reusable static so both controls use it). Draw the
  **inactive** envelope first (slightly dimmer), then the **active** one on top (full opacity).
  Both use the *same* `SnsEnvelopeMapping.ComputePoints` mapping, so equal values map to equal
  pixels → genuinely shared axes. Each envelope gets its 3 handles in its color.
- Pointer: hit-test nearest handle across **both** envelopes, preferring the active envelope on
  ties; dragging updates that envelope's A/D/S/R via the bound property (throttled writes flow
  through the VM wrappers exactly as today).
- Keyboard: arrows edit the **active** envelope's focused handle; Tab cycles handles within the
  active envelope and exits at the end (same non-trapping rule as Phase 1).
- Math reused from `SnsEnvelopeMapping` (no new ADSR math). Independently testable bits stay in
  the mapping class.

### 4d. `PitchEnvelopeControl` (new, `Src/Controls/PitchEnvelopeControl.cs`)
Compact **bipolar** Attack/Decay graph. Custom `Control`, `Render`.
- Bindable ints: `Attack`, `Decay` (0–127), `Depth` (−63..+63, TwoWay).
- Bindable brushes from resources.
- Render: a center line (depth 0). From the left the curve rises (positive depth) or falls
  (negative) over the attack time to the depth target, then returns to center over the decay
  time. Handles: attack-time (horizontal), decay-time (horizontal), depth (vertical, bipolar).
- Math: new pure helpers in `SnsEnvelopeMapping` — `BipolarToY(value, height)` (0 → center,
  +63 → top, −63 → bottom) and `BipolarFromY(y, height)`, plus pitch breakpoints. Unit-tested.

### 4e. `FilterResponseControl` (new, `Src/Controls/FilterResponseControl.cs`)
A small, **approximate** static filter-response curve (not interactive). Custom `Control`,
`Render`.
- Bindable: `Mode` (string), `Cutoff` (0–127), `Resonance` (0–127), brushes from resources.
- Render a simple polyline per mode: Low pass = flat then roll off after cutoff; High pass =
  roll off before cutoff; Band pass = peak at cutoff; Peaking = bump at cutoff; Bypass = flat.
  Resonance raises a bump at the cutoff. Purely indicative; pure shape math in a small
  `FilterCurve` helper (unit-tested for the breakpoint positions).

### 4f. View model additions
`SNSPartialViewModel` (`Src/ViewModels/SNSPartialViewModel.cs`):
- Filter: `FilterMode` (ParamString, Repr options), `FilterSlopeSteep` (ParamBool, on=`-24`
  off=`-12`), `FilterCutoff`, `FilterResonance` (ParamInt 0–127), `FilterCutoffKeyfollow`
  (−100..100), `HpfCutoff` (0–127), `FilterEnvVeloSens` (−63..63).
- Filter envelope: `FilterEnvAttack/Decay/Sustain/Release` (0–127), `FilterEnvDepth` (−63..63).
- Pitch envelope: `PitchEnvAttack`, `PitchEnvDecay` (0–127), `PitchEnvDepth` (−63..63).
- `ActiveEnvelope` (int, 0/1) — bound to the dual control's toggle.
- Card summary: `FilterSummary` (e.g. `"LPF 80"` from mode abbrev + cutoff), and the filter
  env values for the mini preview. Add these to the editable-param list for copy/paste/init,
  and to `InitDefaults` (sensible neutral filter/pitch defaults).

`SNSynthToneHeaderViewModel`: `OctaveShift` (−3..+3), `PitchBendUp`/`PitchBendDown` (0–24).

### 4g. View (`Src/Views/SNSynthToneEditorView.axaml`)
Reorganize the partial-editor right column:
- **Top, compact param sections** (wrap/columns): Oscillator (as today), **Pitch** (the
  per-partial `PitchEnvelopeControl` with its depth; tone-wide Octave/Bend live in the header,
  see below), **Filter** (mode
  button row, slope toggle "Gentle 12 dB / Steep 24 dB", cutoff, resonance, keyfollow, HPF,
  env velocity sens, + `FilterResponseControl`), Amp (as today).
- **Bottom, filling**: an Envelopes panel = the `DualAdsrEnvelopeControl` (shared axes) + the
  **Amp/Filter toggle** + the **Filter Env Depth** bipolar slider + numeric A/D/S/R boxes for
  both envelopes (grouped, color-labeled).
- Octave Shift + Pitch Bend Up/Down go in the **Tone Header** (tone-wide), next to the existing
  header controls.
- Cards: add the `FilterSummary` text and a **mini filter-env preview** (amber, Preview mode)
  beside the amp-env preview.
- All colors via `{StaticResource …}`.

## 5. Layout sketch (right column)
```
┌ Oscillator ─────────────────────────────────────────┐
│ Wave Var Pitch Detune … (as Phase 1)                 │
├ Pitch ───────────┬ Filter ──────────────┬ Amp ───────┤
│ pitch env (∿)    │ Mode[btns] Slope tgl  │ Lvl Pan    │
│ depth ◄─●─►      │ Cut Res KF HPF VelSens│ Vel KF     │
│                  │ [filter response ⌒]   │            │
├ Envelopes — shared X (time) / Y (0–127) ─────────────┤
│  ╱‾‾＼____  (amp, blue)      [Amp|Filter] active     │
│  ╱‾＼______ (filter, amber)  Filter Depth ◄─●─►       │
│  A D S R  (amp)   A D S R (filter)  numeric           │
├──────────────────────────────────────────────────────┤
│ Copy  Paste  Init                                     │
└──────────────────────────────────────────────────────┘
```

## 6. State, sending, mapping
Identical to Phase 1: each control value flows through a `ParamInt`/`ParamString`/`ParamBool`
wrapper → per-(domain,path) throttled write → `DomainBase.WriteToIntegraAsync`. Hardware echoes
sync back via the FQP `PropertyChanged` subscriptions. Bipolar/shifted values are display-space
passthrough, clamped by the wrapper. The dual control writes amp and filter A/D/S/R through
their respective wrappers (distinct paths → independent throttle keys, so both flush during a
drag). Clamp before send. No new write path.

## 7. Accessibility
The dual graph is fully keyboard-editable (active-envelope toggle + arrows) AND every value has
a numeric box. The pitch graph likewise (numeric Attack/Decay/Depth). Filter controls are
standard labeled inputs. Conditional/disabled-with-explanation where relevant (e.g. HPF only
meaningful when … — keep simple: always shown). Color is never the only signal — the dual
graph also labels Amp/Filter and uses the active-envelope toggle, so color-blind users can tell
them apart.

## 8. Testing (`Tests/`, NUnit, following Phase 1)
- `SnsEnvelopeMapping`: `BipolarToY`/`BipolarFromY` round-trip + center mapping; pitch breakpoint
  positions; clamping.
- `FilterCurve` helper: breakpoint positions per mode (LPF/HPF/BPF/Peak/Bypass) and resonance.
- Conditional/option logic if any new pure rule is added (e.g. filter-mode abbreviation for the
  card summary → a pure `FilterSummary` function, tested).
- Copy/paste includes the new filter/pitch params (extend the clipboard set; the pure clipboard
  test already covers the mechanism).
- Dual-control hit-testing math (nearest-handle-across-two-envelopes) extracted to a pure helper
  and tested.
- The graphical controls themselves are verified by build + the hardware smoke test.

## 9. Acceptance criteria (Phase 2)
- Selecting an SN-S partial shows Filter and Pitch sections plus the combined envelope.
- The Amp and Filter envelopes render on **one shared-axis graph** (same time/level scale,
  ticks every 16), color-coded, both draggable; the Amp/Filter toggle switches the active
  (keyboard) envelope; numeric boxes edit both; values clamp and send (throttled).
- Filter Env Depth and Pitch Env Depth are bipolar, center-detented, and send correctly.
- Filter params (mode/slope/cutoff/resonance/keyfollow/HPF/velocity-sens) edit live; a small
  filter-response curve reflects mode/cutoff/resonance.
- The Pitch envelope is a bipolar graph (up = sharp, down = flat) and edits live.
- Tone-wide Octave Shift and Pitch Bend Up/Down are in the header and send.
- Cards show a filter summary and a mini filter-env preview; copy/paste/init include the new
  params.
- Hardware-side edits update all new controls.
- No hardcoded colors in XAML; envelope control colors come from `App.axaml` resources.
- Tests in §8 pass; full solution builds.

## 10. Out of scope (later phases)
Motion/LFO panel, Performance Response, MFX slot, macros, starter patches, safe Solo/Mute
(Phase 3–4 per the Phase-1 spec §9).
