# Motional Surround Editor — Design

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia / .NET 10 / ReactiveUI MVVM)

## 1. Goal

Replace the need to hunt across many parameter tabs by adding a **spatial, musical
Motional Surround editor**: a 2D "room map" where each of the 16 internal parts and the
external part can be dragged into place, with prominent global room/ambience controls and
fast per-part fine-tuning. It must reuse the existing SysEx/parameter infrastructure, map
values exactly, and avoid flooding MIDI while dragging.

The existing raw "Motional Surround" parameter list (under Common) is **kept unchanged** as
an advanced fallback. The new editor is **additive**.

## 2. Key constraint discovered in the codebase

The Motional Surround parameters live in **two different domains**:

- **Global + external part** → the single `Studio Set Common Motional Surround/` domain,
  reachable via `Integra7Domain.StudioSetCommonMotionalSurround`. Paths:
  - `Motional Surround Switch` (repr `OFF_ON`)
  - `Room Type` (repr `ROOM_TYPE`: `Room1`/`Room2`/`Hall1`/`Hall2`)
  - `Room Size` (repr `ROOM_SIZE`: `Small`/`Medium`/`Large`)
  - `Motional Surround Depth` (0–100)
  - `Ambience Level` (0–127), `Ambience Time` (0–100), `Ambience Density` (0–100),
    `Ambience HF Damp` (0–100)
  - `Ext Part L-R` (−64..+63), `Ext Part F-B` (−64..+63), `Ext Part Width` (0..32),
    `Ext Part Ambience Send Level` (0..127), `Ext Part Control Channel` (repr `_1_16_OFF`:
    `"1"`..`"16"`, `"OFF"`)
- **Per internal part (1–16)** → each part's `Studio Set Part/` block (16 separate domains),
  reachable via `Integra7Domain.StudioSetPart(zeroBasedPartNo)`. Paths:
  - `Motional Surround L-R` (−64..+63), `Motional Surround F-B` (−64..+63),
    `Motional Surround Width` (0..32), `Motional Surround Ambience Send Level` (0..127)

This split is precisely why the stock UI scatters these controls across tabs.

Both domains already hold **live, shared `FullyQualifiedParameter` (FQP)** instances:
- populated on connect (`PartViewModel.InitializeParameterSourceCachesAsync` reads
  `StudioSetCommonMotionalSurround` and every `StudioSetPart(i)`),
- kept current by the hardware→UI path (`MainWindowViewModel.UpdateUiFromIntegraAsync` →
  `domain.ModifySingleParameterDisplayedValue`) and by `PartViewModel.ResyncPartAsync`,
- each raises `INotifyPropertyChanged` on `StringValue`/`RawNumericValue`.

The new editor **binds to these same instances**, so state-sync (preset changes, hardware
edits, resyncs) is free.

## 3. Write path — chosen approach

The existing write path (`MessageBus … "ui2hw"`) applies a **single global `Throttle(250 ms)`
debounce to the whole stream**. That is fine when one control changes one parameter, but a
**2-axis puck drag changes L-R and F-B together**, and a global debounce would drop one axis.

**Chosen:** the new `MotionalSurroundViewModel` owns a **per-key debounced write pipeline**
and writes through the domain directly via `DomainBase.WriteToIntegraAsync(path, displayValue)`
— the same call the `ui2hw` handler ultimately makes for simple numeric params (Motional
Surround params are never `IsParent`, so no parent re-read branch is needed).

- **Key = part** for puck drags → both L-R and F-B for that part flush together.
- **Key = parameter path** for single-value controls (sliders, toggle, room cards, channel).
- Debounce interval reuses `Constants.THROTTLE` (250 ms), matching existing behavior
  (hardware update on pause, immediate visual update during the gesture). Implemented with an
  idiomatic Rx per-key group, e.g. `subject.GroupBy(x => x.Key).SelectMany(g => g.Throttle(
  TimeSpan.FromMilliseconds(Constants.THROTTLE))).Subscribe(flush)`.
- **Batch/preset operations bypass the debounce** and write each changed value once,
  sequentially awaited, so nothing is dropped.

Rejected alternatives: routing everything through `ui2hw` (drops a sibling axis on diagonal
drags); adding a new batch-write API on the communicator (more surface area than warranted).

## 4. Placement & layout

- **Placement:** a **new top-level `TabItem` "Motional Surround"** in `MainWindow.axaml`,
  beside "Parameters" and "SRX Loader" (full window width; naturally spans all parts +
  external). When not connected/syncing it shows a "Connect to your Integra-7" placeholder,
  consistent with the rest of the app.
- **Layout (Option A — top ribbon + three columns):**
  - **Top ribbon (globals):** Motional Surround ON/OFF toggle; Room Type and Room Size as
    segmented/card controls; sliders for Depth, Ambience Level, Ambience Time, Ambience
    Density, Ambience HF Damp — each with plain-language helper text
    (e.g. Depth = "How strongly parts are placed in the surround field.", Ambience Level =
    "Overall room/reverb presence.", HF Damp = "Higher values darken the ambience.").
  - **Left column — parts overview:** rows for Parts 1–16 and Ext, each showing part
    name/number, human-readable L-R/F-B position, Width, and Ambience send. Clicking a row
    selects that part. Selected row highlighted.
  - **Center — room map:** a `Canvas` surface; each internal part is a numbered draggable
    puck, the external part a visually distinct puck (different shape/color). Axis labels:
    **Front (top) / Back (bottom)**, **L / R**. Center crosshair. Selected puck highlighted.
  - **Right column — selected-part detail:** part name/number; L-R slider (Left·Center·Right);
    F-B slider (Front·Center·Back); Width slider (Narrow→Wide); Ambience Send slider
    (Dry→Ambient); numeric readouts that accept direct entry; reset-to-center button for
    position; reset buttons for Width and Ambience Send. For the external part, also the
    Control Channel selector (1–16 / OFF).
  - **Presets row:** Center all · Wide stereo spread · Front band layout · Ambient hall
    layout · Reset Motional Surround.

## 5. Value mapping (documented in code)

- **L-R:** UI left edge → −64, center → 0, right edge → +63.
- **F-B:** UI **top → −64 (Front)**, center → 0, **bottom → +63 (Back)**.
- **Width:** 0–32. **Ambience Send:** 0–127. Global controls use their exact listed ranges.
- Canvas mapping: `normalized = (value − (−64)) / (63 − (−64))` per axis; pixel =
  `normalized × extent`; inverse on drag, then **clamp to [−64, +63]** and round to integer
  before writing. All conversions go through the pure `MotionalSurroundMapping` helper.
- Display ↔ raw round-trips reuse the existing `DisplayValueToRawValueConverter` /
  parameter `Repr` maps (hardware mapping stays the single source of truth). Repr-based
  controls (Switch, Room Type/Size, Control Channel) write the exact display strings the
  param defines.
- Off-by-one guard: boundaries (−64, 0, +63, 0, 32, 0, 127) are covered by unit tests.

## 6. New / changed files

**New**
- `Src/Models/Services/MotionalSurroundMapping.cs` — pure static helpers: value↔normalized
  coord, clamping (per range), Ext control-channel validation (1–16 or OFF). No Avalonia deps.
- `Src/ViewModels/MotionalSurroundViewModel.cs` — orchestrates globals (bound to common-domain
  FQPs), an `ObservableCollection<MotionalSurroundPartViewModel>` (16 internal + 1 external),
  `SelectedPart`, preset commands, and the per-key debounced write pipeline. Constructed with
  the `Integra7Domain` communicator + `SemaphoreSlim`.
- `Src/ViewModels/MotionalSurroundPartViewModel.cs` — wraps one part's MS values (internal →
  a `StudioSetPart(i)` domain; external → the common domain, different paths). Holds references
  to the 4 (5 for Ext) shared FQPs, subscribes to their `PropertyChanged`, and exposes reactive
  numeric props, display strings, and computed canvas X/Y. Raises position/value changes to the
  parent's write pipeline. Flag distinguishes internal vs external.
- `Src/Views/MotionalSurroundView.axaml` + `.axaml.cs` — the layout above. The room map handles
  pointer press/move/release with pointer capture; pucks are focusable and arrow-key nudge
  L-R/F-B. Standard Avalonia controls elsewhere (`ToggleSwitch`, segmented buttons, `Slider`,
  numeric entry). No new package dependencies.
- `Tests/TestMotionalSurroundMapping.cs` — NUnit `[TestCase]` tests (matches existing style).

**Changed**
- `Src/Views/MainWindow.axaml` — add the top-level "Motional Surround" `TabItem` (bound to
  `MotionalSurroundViewModel`) + not-connected placeholder.
- `Src/ViewModels/MainWindowViewModel.cs` — after the `PartViewModels` loop in
  `UpdateConnectedAsync`, construct and expose `MotionalSurroundViewModel` (passing
  `_integra7Communicator` and `_semaphore`); reset/clear it on disconnect/rescan.

## 7. Data flow

- **On open:** read current display values from the already-synced shared FQPs (reflects real
  hardware state; no extra MIDI traffic).
- **Drag puck:** pointer move → px→normalized→L-R/F-B → set part VM values immediately (puck,
  list, detail update) → enqueue write keyed by part → debounced flush writes both axes.
- **Single controls:** same pipeline keyed by parameter path.
- **Hardware/preset change:** existing `hw2ui` + `ResyncPartAsync` update the shared FQPs →
  `PropertyChanged` → editor updates automatically.
- **Presets:** batch helpers set values and write each changed value once, sequentially awaited.

## 8. Accessibility & error handling

- Pucks focusable + arrow-key nudge; every value has a numeric readout that accepts direct
  entry; sliders are keyboard-native.
- Visible text labels (Left/Center/Right, Front/Center/Back, Narrow/Wide, Dry/Ambient) and
  helper text for unfamiliar synth terms; accessible focus/selection highlighting.
- Responsive enough for laptop screens (the three columns flex; the map has a sensible min size).
- All writes clamp/validate before calling the parameter API. The Ext Control Channel selector
  only offers 1–16 or OFF. The tab is inert (placeholder) until connected and not syncing.

## 9. Testing

- Pure NUnit tests for `MotionalSurroundMapping`: coordinate↔value round-trips; clamping at and
  beyond boundaries; off-by-one checks at −64 / 0 / +63, 0 / 32, 0 / 127; control-channel
  validation (1–16 valid, OFF valid, others rejected/clamped).
- Views/VMs are not unit-tested, consistent with the existing project (only pure services are
  unit-tested). Manual verification: drag, fine-tune, presets, and reflect a hardware-side edit.

## 10. Out of scope

- No changes to SysEx encoding, the parameter blob, or unrelated tabs.
- No new third-party dependencies.
- The raw Motional Surround parameter list is left as-is.
