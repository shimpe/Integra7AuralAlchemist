# Rotary knob controls — design

**Date:** 2026-07-21
**Goal:** Replace the sliders and spin boxes in the friendly editors with rotary knobs that look good,
carry a colored value strip, an editable value box, a unit, and a grouping color — while observing and
steering the hardware exactly as the existing controls do.

## Why this is low-risk on the hardware side

The friendly editors bind their value controls `TwoWay` to `ParamInt.Value` (and siblings) from
`SynthParam.cs`. Those wrappers already do the throttled write, echo suppression, and model→control
refresh. So a new control that exposes a `Value` (TwoWay `double`) property and binds the same way
inherits all of the hardware behaviour with **no new MIDI code**. This is a UI-shape change, not a
data-flow change. See `docs/UI_HARDWARE_DATAFLOW.md`.

The control it replaces, `Src/Controls/ValueSlider.axaml`, already defines exactly the surface we need:
`Value` (TwoWay), `Minimum`, `Maximum`, `Unit`, plus a formatted `ValueText`. The knob is a superset.

## The three pieces

### 1. `KnobGeometry` (pure, unit-tested)

A static helper holding all the value↔angle and fill math, with no Avalonia types in its signatures, so
it is testable headless like `PartLoadState` and `SyncCounter`.

- The dial sweeps a fixed arc: **start 135°, sweep 270°** (7-o'clock to 5-o'clock, clockwise), leaving
  the bottom 90° open — the synth-knob convention.
- `ValueToAngle(value, min, max)` → degrees along that sweep.
- `FillRange(value, min, max)` → the `(fromFraction, toFraction)` of the sweep to paint in the accent
  color:
  - **unipolar** (`min >= 0`): from 0 to the value fraction.
  - **bipolar** (`min < 0`): from the zero point to the value fraction — so a negative value paints
    left-of-top and a positive value right-of-top, both growing out of 12 o'clock.
- `PointFor(fraction, center, radius)` → the point on the arc, for the pointer and for building the
  colored arc geometry.

### 2. `RotaryKnobDial : Control` (owner-drawn)

Follows the existing owner-drawn controls (`LfoWaveformControl`, the envelope controls): styled
properties with `AffectsRender`, an `OnRender` that draws, and pointer handlers that change `Value`.

- Properties: `Value` (TwoWay), `Minimum`, `Maximum`, `AccentBrush`, `TrackBrush`.
- Render: a dim full-sweep **track** arc, the accent **value** arc on top (per `FillRange`), a filled
  knob body, and a **pointer** line from center to the value angle.
- Interaction: **vertical drag** (up = increase; the precise, synth-standard gesture — not rotation),
  `Shift` for fine (÷5) steps, mouse **wheel** for ±1, and **double-click to reset** (to 0 for
  bipolar, to `Minimum` otherwise). Captures the pointer on press.
- Steps are integer-rounded: every friendly parameter is an integer MIDI value.

### 3. `RotaryKnob : UserControl` (the drop-in)

Composes the dial with an editable readout, so a view replaces `<controls:ValueSlider .../>` with
`<controls:RotaryKnob .../>` and the same three bindings.

- Properties: `Value` (TwoWay), `Minimum`, `Maximum`, `Unit`, `AccentBrush` (defaulted to a neutral
  brush so callers that do not group still look right).
- Layout: the dial on top; below it a compact `TextBox` bound to the value with the `Unit` shown beside
  it. Typing commits on `Enter`/lost-focus, parsed and **clamped** to `[Minimum, Maximum]`; a value that
  will not parse reverts to the model value. The box must not echo mid-typing — it commits, it does not
  stream keystrokes to the hardware.
- The existing label `TextBlock` stays *outside* the control, exactly where the `ValueSlider` label is
  now, so the surrounding layout is unchanged.

## Color grouping

`AccentBrush` is the grouping mechanism. Reuse the envelope palette already in `App.axaml`
(`SnAmpEnvelopeBrush` blue, `SnFilterEnvelopeBrush` amber, `SnPitchEnvelopeBrush` purple) and add a
small set of neutral/group brushes as needed. No hardcoded hex in XAML — every color is a
`{StaticResource}` (see memory: no-hardcoded-colors-in-xaml).

## Rollout

1. Build the three pieces with the geometry unit-tested; wire the knob into **one** panel
   (`LfoPanelView`) as proof.
2. Get a visual/hardware check before the wide change.
3. Replace every `ValueSlider` / bare `Slider` / `NumericUpDown` in the friendly editor views with a
   `RotaryKnob`, assigning group colors per functional cluster (amp / filter / pitch / lfo / common).
   `ValueSlider` is retired once nothing references it.

## Out of scope

- The advanced (raw parameter grid) tabs keep their existing rendering.
- **The FX and Instrument sections** (`DiscriminatedParamSectionView`) render through
  `DataTemplateProvider.ParameterValueTemplate`, which is **shared with the advanced grid**
  (`ParameterCollection.axaml`). Converting it would change the advanced tabs too — out of scope — and
  those sliders also carry fractional values (`0.##`) that the integer knob is not built for. Left as
  bare sliders deliberately.
- Non-numeric controls (combo boxes, toggles) are unchanged.
- The Motional Surround puck editor is its own visual control and is not touched.

## Status

Complete for the friendly editors' own controls: all 202 `ValueSlider` / `NumericUpDown` across the 13
views are knobs, colored by function, hardware behaviour unchanged. Build clean, 423 tests green.
Pending a look on hardware before merge.
