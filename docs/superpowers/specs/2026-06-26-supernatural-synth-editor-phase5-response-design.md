# SuperNATURAL Synth (SN-S) Editor — Design (Phase 5: Response / Touch panel)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** Phases 1–4 (merged to `main`). New branch: `sns-response`.

## 1. Goal

Add a friendly **"Response"** sub-tab to the selected-partial editor that consolidates *how the
partial reacts to playing* into one musical **3×2 matrix**: the three expression **sources**
(Velocity, Aftertouch, Keyboard position) against the two **destinations** they affect
(Brightness = filter, Loudness = amp). Four of the six parameters already exist as wrappers and are
currently scattered in the Sound tab's Filter/Amp sections; this phase **moves** them into the
matrix and adds the two missing Aftertouch parameters, so the Sound tab becomes purely tone/envelope
shaping and "playing response" has one clear home.

Reuse all Phase 1–4 infrastructure (param wrappers, throttled writer, live FQP binding, `sliderLabel`
style, color resources). See memory `parameter-domain-infrastructure`, `sn-s-tone-parameters`,
`visual-editor-pattern`, `no-hardcoded-colors-in-xaml`.

## 2. Decisions locked during brainstorming

1. **Area:** Performance & response (chosen over Solo/Mute, starter patches, polish pass).
2. **Placement:** a new **"Response"** `TabItem`, peer to Sound / Motion / FX in the selected-partial
   editor's `TabControl` (per-partial, like Sound/Motion). Content bound to `SelectedPartial`.
3. **Move, don't duplicate:** the four existing velocity/keyfollow sliders are **removed** from the
   Sound tab and live only in the Response matrix (no slider appears in two tabs).
4. **No Portamento Mode** in this phase (kept out of scope; the header already covers portamento
   on/off + time + legato).

## 3. SN-S parameters used (verified; all per-partial, bipolar, lookup path = `ParSpec.Path`)

Prefix `SuperNATURAL Synth Tone Partial/`. The 3×2 matrix:

| Source ↓ \ Destination → | Brightness (filter)                         | Loudness (amp)                       |
|--------------------------|---------------------------------------------|--------------------------------------|
| **Velocity**             | `Filter Env Velocity Sens` (−63..+63)       | `AMP Level Velocity Sens` (−63..+63) |
| **Aftertouch**           | `Cutoff Aftertouch Sens` (−63..+63) **new** | `Level Aftertouch Sens` (−63..+63) **new** |
| **Keyboard**             | `Filter Cutoff Keyfollow` (−100..+100)      | `AMP Level Keyfollow` (−100..+100)   |

Already wrapped in `SNSPartialViewModel` (and in `_editable` + `InitDefaults`): `FilterEnvVeloSens`,
`AmpVeloSens`, `FilterCutoffKeyfollow`, `AmpKeyfollow`. **New** wrappers needed: the two Aftertouch
sens params (display range −63..+63; the DB maps them omin/omax −63..+63).

There is no per-partial key/velocity **range** (those live at the Studio Set part level) and no pitch
velocity-sens param, so the matrix above is the complete SN-S per-partial response set.

## 4. New / changed components

### 4a. `SNSPartialViewModel` changes (`Src/ViewModels/`)
- Add two `ParamInt` wrappers via the existing `PI(name, min, max)` helper:
  - `public ParamInt CutoffAftertouchSens { get; }` ← `PI("Cutoff Aftertouch Sens", -63, 63)`
  - `public ParamInt LevelAftertouchSens { get; }` ← `PI("Level Aftertouch Sens", -63, 63)`
- Add both to the `_editable` array (so copy/paste/init carry them).
- Add both to `InitDefaults` with value `"0"` (neutral — no aftertouch response).
- No change to the existing four wrappers (they stay; only their *view* placement moves).

### 4b. View changes (`Src/Views/SNSynthToneEditorView.axaml`)
- **Remove** the four sliders + labels from the Sound tab:
  - Filter section: "Cutoff Keyfollow" (`FilterCutoffKeyfollow`) and "Env Velocity Sens"
    (`FilterEnvVeloSens`).
  - Amp section: "Velocity (Consistent → Dynamic)" (`AmpVeloSens`) and
    "Keyfollow (Lower → Higher notes louder)" (`AmpKeyfollow`).
  (Leave the rest of the Filter/Amp sections — cutoff, resonance, mode, slope, HPF, levels, pan,
  envelopes — untouched.)
- **Add** a `TabItem Header="Response"` after the "FX" tab, mirroring Sound/Motion structure:
  `ScrollViewer` → root with `DataContext="{Binding SelectedPartial}"
  x:DataType="vm:SNSPartialViewModel"` → a titled `Border` (`SnPanelBackgroundBrush`) containing:
  - Heading "Response" + muted "— how this partial reacts to playing".
  - A `Grid` matrix: a header row ("", "Brightness", "Loudness") and three data rows
    (Velocity / Aftertouch / Keyboard). Each data cell = a bipolar `Slider` with the row/destination
    label via the shared `sliderLabel` style and a `ToolTip.Tip` naming the Roland parameter.
    Slider ranges: Velocity & Aftertouch rows `Minimum=-63 Maximum=63`; Keyboard row
    `Minimum=-100 Maximum=100`. Bindings: `{Binding FilterEnvVeloSens.Value, Mode=TwoWay}`,
    `{Binding AmpVeloSens.Value, Mode=TwoWay}`, `{Binding CutoffAftertouchSens.Value, Mode=TwoWay}`,
    `{Binding LevelAftertouchSens.Value, Mode=TwoWay}`, `{Binding FilterCutoffKeyfollow.Value,
    Mode=TwoWay}`, `{Binding AmpKeyfollow.Value, Mode=TwoWay}`.
  - An "Advanced partial parameters…" link reusing
    `AdvancedOscillatorCommand` (navigates to the raw "Advanced — Partials" tab on the same partial,
    via the Phase-3 partial-index fix).
- Copy/Paste/Init row stays below all tabs unchanged.
- All colors via `{StaticResource}` (`Sn*` brushes); no inline hex.

## 5. State, sending, mapping
Identical to prior phases: each slider drives a `ParamInt` (bipolar display-space passthrough,
clamped) → per-key throttled write → `DomainBase.WriteToIntegraAsync`; hardware echoes sync back via
the FQP `PropertyChanged`. The two new Aftertouch params follow the exact same path. No new write
path. Moving the four sliders is a pure view change — the wrappers, send path, copy/paste, and init
are unchanged for them.

## 6. Accessibility
All six controls are labelled bipolar sliders, keyboard-accessible. Row + column headers give the
source/destination meaning in text; tooltips carry the exact Roland parameter names. Color is never
the only signal.

## 7. Testing (`Tests/`, NUnit)
This phase adds no new pure logic — the two new params reuse the existing `ParamInt` wrapper and the
`SnsPartialClipboard` copy/paste mechanism (already unit-tested). Verification is therefore:
- Full solution **builds** (compiled bindings validate every new/moved binding path).
- Existing **172 tests** still pass (no regressions).
- The two new params are structurally included in copy/paste/init (covered by the existing clipboard
  test mechanism; inclusion is by construction).
- Functional behavior is confirmed by the **hardware smoke test** (§9).

## 8. Acceptance criteria (Phase 5)
- Selecting an SN-S partial shows **Sound / Motion / FX / Response** sub-tabs.
- The Sound tab no longer shows the four velocity/keyfollow sliders; nothing else in Sound changed.
- The Response tab shows the 3×2 matrix (Velocity/Aftertouch/Keyboard × Brightness/Loudness) with
  correct ranges, editing live with throttled sends and bipolar mapping; hardware edits update them.
- The two Aftertouch params are new and carried by copy/paste/init (init = 0).
- "Advanced partial parameters…" opens the raw Partials tab on the current partial.
- No hardcoded colors in XAML. Full solution builds; existing tests pass.

## 9. Hardware smoke test (user-run)
1. SN-S part → Editor → select a partial → confirm a **Response** sub-tab next to FX.
2. Confirm the Sound tab’s Filter/Amp sections no longer show Velocity Sens / Keyfollow.
3. In Response, move **Velocity → Brightness/Loudness**; play soft vs hard → filter/level respond.
4. Move **Aftertouch → Brightness/Loudness**; apply channel aftertouch → filter/level respond.
5. Move **Keyboard → Brightness/Loudness**; play low vs high → filter/level track the keyboard.
6. Edit on the hardware front panel → the matrix sliders update (round-trip).
7. Copy a partial → Paste onto another: response settings (incl. aftertouch) carry over; Init resets
   them to 0.
8. "Advanced partial parameters…" opens the raw Partials tab on the same partial.

## 10. Out of scope (later)
Portamento Mode; per-partial Solo/Mute; starter patches/macros; a graphical velocity curve (SN-S
exposes only a sensitivity amount, not a curve).
