# SuperNATURAL Synth (SN-S) Editor — Design (Phase 1: Vertical Slice)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)

## 1. Goal

Make the Integra-7 SuperNATURAL Synth feel like an approachable software synth: a visual,
musical editor that answers *"what does this sound do and how do I shape it"* rather than
exposing raw Roland addresses. The existing raw parameter grid (`ParameterCollection`) stays
as the **advanced fallback**, reached through contextual links — it is **not duplicated**.

This is a large deliverable. It is decomposed into phases (§9). **This spec covers Phase 1
only**: an end-to-end *vertical slice* that proves the full pipeline — including the riskiest
new piece, a draggable graphical envelope — and lays down the reusable shell that later
phases extend.

## 2. Decisions locked during brainstorming

1. **Vertical slice first** (not foundation-library-first, not one giant spec).
2. **Envelope graphs** render via a **custom Avalonia `Control` with a `Render` override**
   (new rendering paradigm for this codebase; the existing code only uses native shapes).
3. **Friendly editor is the default tab** for SN-S tones; the raw Tone/MFX/Partials grids
   become "Advanced" sibling tabs reached via contextual links.
4. **Solo/Mute is deferred** (no non-destructive audition path exists yet — see §8).

## 3. Reality check — SN-S parameter set (verified against source)

Parameters come from the build-generated blob; definitions live in
`Tools/ParameterBlobGenerator/ParameterDefinitions.cs` (SN-S partial defs ≈ line 29204, enum
dicts ≈ line 1197). Domains are reached via `Integra7Domain`:
`SNSynthToneCommon(part)`, `SNSynthToneCommonMFX(part)`, `SNSynthTonePartial(part, partial)`
(zero-based; 3 partials). These accessors return the **shared, live `DomainBase` instances**
whose `FullyQualifiedParameter` (FQP) values are kept current by the hardware→UI path, exactly
as `MotionalSurroundViewModel` relies on for `StudioSetPart(i)`. The editor **binds to these
same FQP instances**, so preset changes / hardware edits / resyncs sync for free.

**Lookup paths** — the dictionary key and the `DomainBase.WriteToIntegraAsync(path, …)`
argument is the `ParSpec.Path` (= `<prefix>/<name>`). The part/partial context comes from
*which domain instance* you call, **not** from the path (exactly as the Motional Surround code
keys on `"Studio Set Part/Motional Surround L-R"` and gets the part from `StudioSetPart(i)`):
- Partial — via `SNSynthTonePartial(part, partial)`: `SuperNATURAL Synth Tone Partial/<name>`
- Common — via `SNSynthToneCommon(part)`: `SuperNATURAL Synth Tone Common/<name>`
- MFX — via `SNSynthToneCommonMFX(part)`: `SuperNATURAL Synth Tone Common MFX/<name>`

Build `GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path)` per domain. All
three partial domains share the **same** path set (they differ only by domain instance), which
makes partial copy/paste trivial: snapshot `{path → StringValue}` from the source partial and
write each to the target partial's domain.

**Spec items that do NOT exist for SN-S** (belong to other engines; will not be invented):
Envelope Loop mode/sync-note, Attack/Release/Portamento *Interval Sensitivity*, Chromatic
Portamento. The eventual "Performance Response" panel (a later phase) reduces to the two
per-partial aftertouch sensitivities plus Common voice controls.

**Data fix included in this work:** the amp-envelope attack parameter was misspelled in
`ParameterDefinitions.cs` as `AMP Env Atttack Time` (triple-t). It is corrected to
`AMP Env Attack Time`; the git-ignored parameter blob is regenerated on the next build
(see memory `parameter-blob-build-pipeline`).

### Parameters used in Phase 1

Common (Tone Header):
`Tone Name` (ASCII 12), `Tone Level` (0–127), `Mono Switch` (OFF/ON → Poly/Mono),
`Legato Switch` (OFF/ON), `Portamento Switch` (OFF/ON), `Portamento Time` (0–127),
`Unison Switch` (OFF/ON), `Unison Size` (0–3 → 2/4/6/8), `Analog Feel` (0–127),
`Ring Switch` (0–2: Off/Reserved/On), `Partial1/2/3 Switch` (OFF/ON).

Partial — Oscillator:
`OSC Wave` (enum: Saw, Square, Pulse Width Mod. Square, Triangle, Sine, Noise, SuperSaw, Pcm),
`OSC Wave Variation` (A/B/C), `OSC Pitch` (40–88 → −24..+24), `OSC Detune` (14–114 → −50..+50),
`OSC Pulse Width` (0–127), `OSC Pulse Width shift` (0–127), `OSC Pulse Width Mod Depth` (0–127),
`Super Saw Detune` (0–127), `Wave Number` (0–450 enum PCM_WAVEFORMS), `Wave Gain` (0–3 → −6..+12 dB).

Partial — Amp:
`AMP Level` (0–127), `AMP Pan` (0–127 → −64..+63), `AMP Level Velocity Sens` (1–127 → −63..+63),
`AMP Level Keyfollow` (54–74 → −100..+100).

Partial — Amp Envelope:
`AMP Env Attack Time`, `AMP Env Decay Time`, `AMP Env Sustain Level`, `AMP Env Release Time`
(all 0–127).

Read-only on the card summary: `OSC Wave`, `AMP Level`, `AMP Pan`, the four amp-env values
(for the mini preview), `Partial{n} Switch`.

## 4. Architecture & write path

Follows the proven **Motional Surround template** (see memory `visual-editor-pattern`):

- **`ThrottledParameterWriter`** (new, reusable; `Src/Models/Services/`): wraps
  `Subject<(string key, Func<Task> write)>` → `GroupBy(key).SelectMany(g => g.Throttle(Constants.THROTTLE))`
  → `Subscribe(run write, log on error)`. Key = parameter path, so independent controls never
  block each other during a drag. `Flush`/immediate-send entry point for drag-release.
  `IDisposable`. Writes go through `DomainBase.WriteToIntegraAsync(path, displayValue)` (the same
  call the `ui2hw` handler makes for simple numeric params — SN-S edit params are not parents).
- **Inbound sync**: subscribe to each bound FQP's `StringValue` `PropertyChanged`; on change
  re-seed the corresponding VM value on the UI thread (`Dispatcher.UIThread.Post`). All
  model→VM seeding is wrapped in a `_suppress` flag so echoes don't re-enqueue writes.
- **Clamping**: every value clamped to its raw/display range before send (the converter also
  autoclips, but the VM clamps first to keep the UI honest).
- **Drag**: update local visual state immediately; enqueue throttled writes during the drag;
  force a final exact write on pointer-release.

## 5. Components & files

New ViewModels (`Src/ViewModels/`):
- `SNSynthToneEditorViewModel : ViewModelBase, IDisposable` — shell for one part's SN-S tone.
  Holds the Common + 3 Partial `DomainBase`s (from `Integra7Domain`), the
  `ThrottledParameterWriter`, the header VM, 3 rack-item VMs, and the selected-partial VM.
  Exposes `SelectedPartialIndex`. Builds FQP dictionaries via
  `GetRelevantParameters(true,true).ToDictionary(p => p.ParSpec.Path)`.
- `SNSynthToneHeaderViewModel` — the Common controls in §6A.
- `SNSPartialViewModel` — one partial: editable Oscillator + Amp + Amp-envelope properties,
  plus read-only summary fields for its rack card, plus `Copy/Paste/Init` commands.

New Views / Controls:
- `Src/Views/SNSynthToneEditorView.axaml(.cs)` — layout: top Tone Header, left Partial Rack
  (3 cards), center Selected Partial Editor (Oscillator / Amp / Amp-Envelope). Empty-state
  guard (`IsNull` → "Select a SuperNATURAL Synth tone to edit.").
- `Src/Controls/AdsrEnvelopeControl.cs` (+ optional minimal style) — custom `Control` with
  `Render` override (see §7).

New helper:
- `Src/Models/Services/SnsEnvelopeMapping.cs` — static value↔pixel mapping + clamping for the
  envelope control; independently unit-testable.

Integration edits:
- `Src/Views/MainWindow.axaml`: add `<TabItem Header="Editor" Tag="SN-S"
  IsVisible="{Binding SelectedPresetIsSNSynthTone}">` hosting `SNSynthToneEditorView`; remove
  `Tag="SN-S"` from the existing raw "Tone" tab and relabel raw SN-S tabs
  "Advanced — Common / MFX / Partials".
- `Src/ViewModels/PartViewModel.cs`: expose `SNSynthToneEditorViewModel? SNSynthToneEditor`,
  constructed when `SelectedPreset` becomes SN-S and disposed when it leaves SN-S; re-seeds
  from live FQPs on preset change. Requires the `Integra7Domain` reference + part index
  (thread through if not already present — confirm during planning).
- Contextual "Advanced … parameters…" links switch the inner `TabControl` to the matching raw
  tab (bound selected-tab property/command on `PartViewModel`).

## 6. Panel specs

### 6A. Tone Header
Tone name + "SuperNATURAL Synth" badge. Friendly primary label / Roland secondary text:
- **Tone Level** — large level slider (`Tone Level`).
- **Mono / Poly** — segmented control (`Mono Switch`: ON→Mono, OFF→Poly).
- **Legato** — toggle (`Legato Switch`); **disabled unless Mono**, with explanation.
- **Glide** / Portamento — toggle (`Portamento Switch`) + time slider (`Portamento Time`);
  time **disabled unless Glide on**.
- **Unison** — toggle (`Unison Switch`) + size selector 2/4/6/8 (`Unison Size`); size
  **disabled unless Unison on**.
- **Vintage Drift** / Analog Feel — slider "Clean/Stable → Vintage/Drifty" (`Analog Feel`).
- **Metallic Ring Mod** / Ring Switch — toggle (`Ring Switch`, Off/On).

### 6B. Partial Rack (3 cards)
Each card: on/off (`Partial{n} Switch`), waveform name, Level, Pan, a **mini amp-env preview**
(small non-interactive render of the 4 amp-env values), click-to-select (UI selection only —
does not touch `Partial{n} Select`), and **Copy / Paste / Init** (per-partial). Selected card
is visually highlighted. *Marked "coming in a later phase" on the card: filter summary, mini
filter-env preview, LFO indicator, solo/mute.*

Copy/Paste/Init operate on the temporary edit buffer via the writer:
- **Copy**: snapshot the partial's editable param display-values into an in-app clipboard.
- **Paste**: write each clipboard value to the target partial.
- **Init**: write opinionated neutral defaults to the partial (UI-level init, not a factory dump).

### 6C. Selected Partial Editor
**Oscillator** — waveform picker (cards/segmented from `OSC Wave`) + `OSC Wave Variation`,
`OSC Pitch` (semitone stepper), `OSC Detune` (center-detented). Context-aware extra controls
(disabled-with-explanation, not silently hidden):
- *Pulse Width Mod. Square* → `OSC Pulse Width`, `OSC Pulse Width shift`, `OSC Pulse Width Mod Depth`.
- *SuperSaw* → `Super Saw Detune` (prominent).
- *Pcm* → `Wave Number` (picker) + `Wave Gain`.
- When **Ring Mod on**, annotate controls on partials 1–2 that no longer have effect.
"Advanced oscillator parameters…" link.

**Amp** — `AMP Level` fader; `AMP Pan` (L/C/R, −64..+63); `AMP Level Velocity Sens`
("Consistent→Dynamic"); `AMP Level Keyfollow` ("Lower louder/Even/Higher louder").
"Advanced amp parameters…" link.

**Amp Envelope** — the `AdsrEnvelopeControl` bound to the four amp-env values, with numeric
fallback inputs and an unobtrusive raw-value readout. Helper labels per handle (Attack "how
quickly the sound starts", etc.).

## 7. `AdsrEnvelopeControl`

Custom `Control`, `Render` override. Bindable `StyledProperty<int>` for `Attack`, `Decay`,
`Sustain`, `Release` (0–127). Designed for extension: an optional bipolar `Depth` (−63..+63)
and a Pitch-AD (two-stage) variant slot in later without redesign.

Behavior:
- Renders baseline grid, the ADSR curve, a fill under it, and large draggable handles.
- X = time (segment widths proportional to each 0–127 time value; sustain shown as a held
  plateau); Y = level (sustain handle vertical).
- Pointer: drag Attack/Decay/Release horizontally, Sustain vertically; snap to integers;
  clamp to range; show the active value while dragging.
- **Keyboard**: focusable; arrow keys adjust the focused handle; Tab cycles handles — every
  value editable without a mouse. Numeric fallback inputs bound to the same VM values.
- Emits value changes (bound two-way) that the VM routes to the `ThrottledParameterWriter`;
  the control raises a "drag completed" signal so the VM forces the final send.
- Pure mapping math lives in `SnsEnvelopeMapping` (value→pixel, pixel→value, clamp) for tests.

## 8. Deferred: Solo / Mute (design note for later)

No undo system and no non-destructive audition path exist. Solo/Mute would have to toggle
`Partial{n} Switch` (real device state) and restore prior switch states on release. Deferred
until that safe-restore (snapshot switches → apply solo/mute → restore) is designed. The card
will show on/off only for now.

## 9. Phasing (future, out of scope here)

- **Phase 2** — Filter (curve viz + filter ADSR+depth) and Pitch (pitch AD+depth) sections;
  combined Envelopes panel; filter summary + mini filter-env on cards.
- **Phase 3** — Motion panel (Always-On LFO + Mod-Wheel LFO) with destination depths; LFO
  indicator on cards.
- **Phase 4** — Performance Response (aftertouch sens + voice controls); MFX slot (type, sends,
  control assigns) with deep-link to advanced MFX; macros (Brightness/Punch/Motion/Width/
  Vintage Drift/Glide); editor utilities (init all, reset envelopes, center pans, starter
  patches); safe Solo/Mute.

## 10. Value mapping rules

- 0–127 params: clamp 0–127; send display string.
- Bipolar −63..+63 (`*Velocity Sens`, depths later): raw 1–127, display 0 = center; send display.
- `AMP Pan`: raw 0–127 → display −64..+63 (L/C/R); send display.
- Octave/keyfollow shifted ranges (e.g. 54–74 → −100..+100): send display.
- Enum params (`OSC Wave`, `Unison Size`, etc.): send the exact enum string.

## 11. Accessibility

Every envelope handle editable via keyboard and numeric input. All controls have visible text
labels (not icon-only). Accessible focus states, adequate contrast, large touch targets.
Tooltips/helper text explain synth concepts. Conditional controls prefer
disabled-with-explanation over silent hiding.

## 12. Testing (`Tests/`, following existing conventions)

- `SnsEnvelopeMapping`: value→pixel / pixel→value round-trip; clamping; sustain plateau math.
- ADSR drag math: pointer delta → new A/D/S/R value (per handle).
- Pan / bipolar display mapping.
- `ThrottledParameterWriter`: coalesces rapid same-key writes to the last value; distinct keys
  independent; final write on flush/release.
- Conditional oscillator visibility: PW controls only for Pulse Width Mod. Square; Super Saw
  Detune only for SuperSaw; Wave Number/Gain only for Pcm.
- Partial Copy→Paste yields an identical editable param set; Init writes the defined defaults.
- Integration: selecting an SN-S preset constructs the editor VM; selecting non-SN-S disposes
  it; "Advanced…" links route to the correct raw tab.

## 13. Acceptance criteria (Phase 1)

- Selecting an SN-S tone on a part shows the friendly editor as the default tab; non-SN-S tones
  are unaffected; raw grids remain reachable via "Advanced…" links.
- Tone Header edits (level, mono/poly, legato, glide+time, unison+size, vintage drift, ring)
  send correctly with the documented dependencies enforced.
- The 3 partial cards show live summaries; click selects; Copy/Paste/Init work on the edit
  buffer; on/off toggles `Partial{n} Switch`.
- The selected partial's Oscillator (context-aware) and Amp controls edit live with throttled
  sends and correct value mapping.
- The Amp Envelope is graphically draggable AND keyboard/numeric editable; updates are live,
  throttled during drag, with a final exact send on release; values clamped.
- Hardware-side edits to any bound parameter update the editor UI.
- All sends go through the existing domain/SysEx infrastructure; no raw parameter table is
  duplicated.
- Tests in §12 pass.
