# SuperNATURAL Synth (SN-S) Editor — Design (Phase 3: Motion / LFO)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** Phases 1 & 2 (merged to `main`). Branch: `sns-motion`.

## 1. Goal

Add the two per-partial LFOs to the friendly SN-S editor as musical **"Motion"** controls
rather than a technical LFO page: an always-on **"Automatic Motion"** and a **"Mod Wheel
Motion"**. Reuse all Phase 1/2 infrastructure (param wrappers, throttled writer, live FQP
binding, color resources, the custom-control + `Preview` pattern). See memory
`parameter-domain-infrastructure`, `sn-s-tone-parameters`, `visual-editor-pattern`,
`no-hardcoded-colors-in-xaml`.

## 2. Decisions locked during brainstorming

1. The selected-partial editor becomes a **Sound / Motion `TabControl`**: *Sound* holds the
   existing sections (Oscillator/Pitch/Filter/Amp/Envelopes) unchanged; *Motion* holds the two
   LFO cards stacked. **Copy/Paste/Init stay below both tabs** (they act on the whole partial).
2. **One reusable LFO panel** (`LfoPanelViewModel` + `LfoPanelView`) used twice per partial; a
   flag selects which params/extra-controls to show.
3. **Rate ↔ Note** are shown by the Tempo Sync toggle (Rate when off, Note value when on).
4. Mostly standard controls + a small **non-interactive waveform preview** (`LfoWaveformControl`)
   — not draggable graphs.

## 3. SN-S parameters used (verified; lookup path = `ParSpec.Path` = `<prefix>/<name>`, per partial via `SNSynthTonePartial(part, partial)`)

Prefix for all: `SuperNATURAL Synth Tone Partial/`.

**Automatic Motion (Always-On LFO):**
- `LFO Shape` (enum LFO_SHAPE: Triangle, Sine, Sawtooth, Square, Sample&Hold, Random)
- `LFO Rate` (0–127)
- `LFO Tempo Sync Switch` (OFF/ON)
- `LFO Tempo Sync Note` (enum TEMP_SYNC_NOTE: 16, 12, 8, 4, 2, 1, 3/4, 2/3, 1/2, 3/8, 1/3, 1/4, 3/16, 1/6, 1/8, 3/32, 1/12, 1/16, 1/24, 1/32)
- `LFO Fade Time` (0–127)
- `LFO Key Trigger` (OFF/ON)
- `LFO Pitch Depth`, `LFO Filter Depth`, `LFO AMP Depth`, `LFO Pan Depth` (all −63..+63)

**Mod Wheel Motion (Modulation LFO):**
- `Modulation LFO Shape` (LFO_SHAPE), `Modulation LFO Rate` (0–127),
  `Modulation LFO Tempo Sync Switch` (OFF/ON), `Modulation LFO Tempo Sync Note` (TEMP_SYNC_NOTE),
  `Modulation LFO Pitch Depth`, `Modulation LFO Filter Depth`, `Modulation LFO AMP Depth`,
  `Modulation LFO Pan Depth` (all −63..+63), `Modulation LFO Rate Control` (−63..+63).

The two LFOs share a suffix set (`Shape`, `Rate`, `Tempo Sync Switch`, `Tempo Sync Note`,
`Pitch Depth`, `Filter Depth`, `AMP Depth`, `Pan Depth`) differing only by the `LFO ` vs
`Modulation LFO ` prefix — so a prefix-parameterized panel builds both. Extras differ: Always-On
adds `Fade Time` + `Key Trigger`; Mod Wheel adds `Rate Control`.

## 4. New / changed components

### 4a. `LfoPanelViewModel` (new, `Src/ViewModels/`)
Reusable VM for one LFO. Constructor:
`LfoPanelViewModel(DomainBase partialDomain, IReadOnlyDictionary<string,FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, string prefix, string title, bool isModWheel)`
where `prefix` is `"LFO "` or `"Modulation LFO "`. Builds wrappers for `PP + prefix + suffix`:
- `Shape` (ParamString, Repr options), `Rate` (ParamInt 0–127), `TempoSync` (ParamBool),
  `TempoSyncNote` (ParamString, Repr options), `PitchDepth`/`FilterDepth`/`AmpDepth`/`PanDepth`
  (ParamInt −63..63).
- If `!isModWheel`: `FadeTime` (ParamInt 0–127), `KeyTrigger` (ParamBool). Else null.
- If `isModWheel`: `RateControl` (ParamInt −63..63). Else null.
- `Title` (string), `IsModWheel`, `HasFade`/`HasKeyTrigger` (= `!isModWheel`), `HasRateControl`
  (= `isModWheel`) for the view's show/hide.
- `IReadOnlyList<IParam> Params` (all of the above non-null wrappers) so the partial VM folds
  them into copy/paste. `IDisposable` (disposes its wrappers).

### 4b. `LfoWaveformControl` (new, `Src/Controls/`)
Small non-interactive preview that draws one cycle of the selected shape. Custom `Control`,
`Render`. Bindable `Shape` (string) + brushes (`LineBrush`, `BackgroundBrush`, `AxisBrush`) from
resources. Shape geometry comes from a pure helper `LfoWaveform.Sample(shape, count)` →
normalized points (X 0..1 across one cycle, Y 0..1, 0.5 = center), unit-tested. Square/S&H are
stepped; Sawtooth ramps; Triangle up/down; Sine smooth; Random a fixed pseudo-pattern (no
RNG — deterministic so it renders/tests stably).

### 4c. `LfoPanelView` (new, `Src/Views/`)
UserControl bound to `LfoPanelViewModel` (`x:DataType`). Layout:
- Title (e.g. "Automatic Motion" / "Mod Wheel Motion") + helper text.
- **Shape**: combo (`Shape.Options`) + `LfoWaveformControl` preview.
- **Speed**: `TempoSync` toggle; **Rate** slider (0–127, `IsVisible` = not synced); **Note value**
  combo (`TempoSyncNote.Options`, `IsVisible` = synced).
- **Always-On only** (`HasFade`/`HasKeyTrigger`): **Fade-in** slider + **Restart on note** toggle.
- **Mod Wheel only** (`HasRateControl`): **Wheel changes speed** bipolar slider (−63..63).
- **Destination depths**: four bipolar center-detented sliders (−63..63) with friendly primary
  labels / Roland secondary: **Vibrato** (Pitch Depth), **Wah** (Filter Depth), **Tremolo**
  (Amp Depth), **Auto-pan** (Pan Depth).
- Labels use the shared `sliderLabel` class for row alignment; all colors via `{StaticResource}`.

### 4d. `SNSPartialViewModel` changes
- Add `LfoPanelViewModel AutomaticMotion` (prefix `"LFO "`, title "Automatic Motion", isModWheel
  false) and `LfoPanelViewModel ModWheelMotion` (prefix `"Modulation LFO "`, title "Mod Wheel
  Motion", isModWheel true), built from the partial domain + the same writer.
- Fold `AutomaticMotion.Params` + `ModWheelMotion.Params` into the `_editable` set (copy/paste).
- Add LFO defaults to `InitDefaults` (neutral: shapes Triangle, rates 64, sync off, depths 0,
  fade 0, key trigger off, rate control 0).
- Dispose both panels in `Dispose`.

### 4e. View restructure (`Src/Views/SNSynthToneEditorView.axaml`)
Replace the right column's single `ScrollViewer`+Grid with:
```
Grid (RowDefinitions="*,Auto")
  TabControl (Grid.Row=0)
    TabItem "Sound"  → ScrollViewer → the existing sections grid (osc/pitch/filter/amp/env)
    TabItem "Motion" → ScrollViewer → StackPanel { LfoPanelView(AutomaticMotion), LfoPanelView(ModWheelMotion) }
  StackPanel (Grid.Row=1) → Copy / Paste / Init (moved out of the scroll, shared)
```
The Sound tab keeps the viewport-min-height trick so the combined envelope still fills. The
Motion tab scrolls if its content exceeds the viewport.

### 4f. Color resources (`App.axaml`)
Add `SnLfoWaveformBrush` (e.g. teal `#4FB0A0`) for the waveform preview line. Reuse
`SnEnvelopeBackgroundBrush`/`SnEnvelopeGridBrush`/`SnEnvelopeAxisBrush`.

## 5. State, sending, mapping
Identical to Phases 1/2: each control value flows through a `ParamInt`/`ParamString`/`ParamBool`
wrapper → per-(domain,path) throttled write → `DomainBase.WriteToIntegraAsync`. Hardware echoes
sync back via FQP `PropertyChanged`. Bipolar depths/rate-control are display-space passthrough
(−63..63), clamped by the wrapper. Tempo-sync note values are enum display strings ("1/4", etc.).
No new write path.

## 6. Accessibility
All controls are standard labeled inputs (combos, sliders, toggles) — keyboard-accessible by
default. The waveform preview is decorative (the shape combo carries the meaning). Conditional
Rate/Note and the LFO-specific extras use show/hide driven by the sync/kind flags; the depth
sliders are center-detented and labeled. Color is never the only signal (every control has text).

## 7. Testing (`Tests/`, NUnit)
- `LfoWaveform.Sample`: returns the requested point count; X spans 0..1; per-shape qualitative
  checks (Sine ≈ center at start/mid/end crossings; Sawtooth monotonic rise within the cycle;
  Square has two distinct levels; Triangle rises then falls; Random/S&H are stepped and
  deterministic).
- Copy/paste: the partial clipboard already has a pure test; confirm the LFO params are added to
  the editable set (the mechanism is covered — the inclusion is structural).
- The panel/control are otherwise verified by build + the hardware smoke test.

## 8. Acceptance criteria (Phase 3)
- Selecting an SN-S partial shows **Sound** and **Motion** sub-tabs; Sound is unchanged;
  Copy/Paste/Init work from below both tabs.
- The **Motion** tab shows two LFO cards (Automatic, Mod Wheel), each with shape + waveform
  preview, tempo-sync toggle switching Rate↔Note, the kind-specific extras (fade + restart, or
  wheel-speed), and the four bipolar depth controls (Vibrato/Wah/Tremolo/Auto-pan).
- All controls edit live with throttled sends and correct value mapping (bipolar depths,
  enum strings); hardware edits update the controls.
- LFO params are carried by copy/paste/init.
- No hardcoded colors in XAML; new brushes come from `App.axaml`.
- Tests in §7 pass; full solution builds.

## 9. Out of scope (later)
Performance Response panel, MFX slot, macros, starter patches, safe Solo/Mute, and an LFO/motion
indicator on the rack cards (cards are already dense).
