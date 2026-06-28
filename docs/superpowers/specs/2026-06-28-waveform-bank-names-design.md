# Group-Type/ID-Aware Waveform Names — Design Spec

**Date:** 2026-06-28
**Status:** Approved (brainstorm)

## Goal

Make PCM waveform names display **correctly** in both the advanced parameter tabs and the friendly editors, by selecting the right per-bank name list from the now-reverse-engineered files (`PartialWaveForms_INT.csv` + `PartialWaveForms_SRX1..12.csv`). Resolves the issue tracked in `docs/KNOWN_ISSUES.md` ("Waveform name list ignores the Wave Group Type / Group ID dependency").

## Problem

A waveform's display name depends on three sibling parameters: **Wave Group Type** (`0=Internal`, `1=SRX`, `2/3=Reserved`), **Wave Group ID** (`0` for Internal; `1..12` selecting the SRX board), and **Wave Number** (index into the selected bank). The runtime currently maps a wave number through a single flat repr (`PARTIAL_WAVEFORMS`) attached to the Wave Number parameter, which ignores Group Type/ID, so names are wrong outside the Internal bank. The flat list was also incorrect even for Internal.

The bank-selection rule is simple and fixed: **`Internal → INT`, `SRX → SRX{GroupID}`** (with `Internal` having `GroupID 0`, and `SRX{n}` having `GroupID n`).

## Affected parameters (scope)

Exactly the 10 wave-number parameters, each governed by its own sibling Group Type + Group ID:

- `PCM Synth Tone Partial/Wave Number L (Mono)` and `… /Wave Number R` → discriminators `PCM Synth Tone Partial/Wave Group Type` + `… /Wave Group ID`.
- `PCM Drum Kit Partial/WMT{1..4} Wave Number L (Mono)` and `… R` → discriminators `… /WMT{n} Wave Group Type` + `… /WMT{n} Wave Group ID`.

Out of scope: SN-Drums (uses its own correct `SN_DRUM_KIT_WAVE_NAME` list), and any parameter not keyed by Group Type/ID.

## Architecture (Approach A: banked repr)

Both surfaces resolve names through one lever — a parameter's name list. The advanced grid builds its combo items from the list and shows `StringValue` (computed from it); the friendly editors derive `ParamString.Options` + value from it. So the design introduces a per-parameter **effective name list** that, for the 10 wave-number params, is the bank selected by their live sibling Group Type/ID.

### 1. Data delivery — `WaveformBanks` service

The 13 CSVs ship as app resources (`AvaloniaResource`, like `parameters.bin`). A runtime `WaveformBanks` service loads each into an `IReadOnlyDictionary<int,string>` keyed by bank name (`"INT"`, `"SRX1"`…`"SRX12"`), using the existing 0-based line-order convention (skip the header comment line; line N → wave number N−1). The old single `PARTIAL_WAVEFORMS` repr and `PartialWaveForms.csv` are retired.

The service exposes:
- `IReadOnlyDictionary<int,string> Bank(int groupType, int groupId)` — via the pure `WaveBankResolver` rule (`Internal→INT`, `SRX→SRX{id}`, Reserved/unknown→INT fallback).
- `string Name(int groupType, int groupId, int number)` — the wave name, or an "unresolved" marker (the raw number) if not present.
- `int? Number(int groupType, int groupId, string name)` — reverse lookup.
- `int FirstWave(int groupType, int groupId)` — the lowest wave number in the bank (for the out-of-range reset).

### 2. `EffectiveRepr` on `FullyQualifiedParameter`

A new nullable `IReadOnlyDictionary<int,string>? EffectiveRepr` on the FQP. For ordinary parameters it is null and everything behaves as today; all name/option/reverse call sites use `EffectiveRepr ?? ParSpec.Repr`. For the 10 wave-number params it is set to the resolved bank.

### 3. Banked-wave registry (code)

A small static registry lists the 10 wave-number parameter paths, each paired with its sibling Group Type + Group ID paths. This is the selection *wiring*; the name *data* is in the CSV assets.

### 4. Domain resolution pass (read path)

After `ReadFromIntegraAsync` completes (the domain holds every FQP), a pass over the registered wave params: read each one's sibling Group Type/ID values, ask `WaveformBanks.Bank(...)`, set the FQP's `EffectiveRepr`, and override its `StringValue` to the resolved name. `EffectiveRepr` is therefore cached on the FQP and refreshed on every read. (The interpreter still produces the raw number first; the pass overrides it for these params only.)

### 5. Reactivity (discriminators)

Mark each wave group's **Group Type and Group ID as `IsParent`** discriminators. Changing either re-runs the existing machinery: the write triggers a domain re-read → the resolution pass refreshes every wave param's `EffectiveRepr` + name; the advanced grid's discriminator-recompute rebuilds the affected editors. No new reactivity plumbing.

### 6. Advanced grid

Two surgical edits:
- `DataTemplateProvider` builds a wave param's editor from `p.EffectiveRepr ?? p.ParSpec.Repr` — so a wave-number param renders as a **name combo from the selected bank** (it no longer has a static repr, so without this it would be a numeric slider).
- `DisplayValueToRawValueConverter` uses `EffectiveRepr ?? ParSpec.Repr` for name→number on write.

### 7. Friendly editors

The wave pickers (`PCMPartialViewModel`, `PCMDrumWmtLayerViewModel`) get the wave-number field's **Options + current value + name→number** from `WaveformBanks` using their live Group Type/ID, recomputing when either changes. This replaces the static `ParamString`-over-repr for the wave-number field only; the rest of those VMs is unchanged.

### 8. Out-of-range reset on bank switch (both surfaces)

When a wave group's **Group Type or Group ID is changed** (either surface — both route writes through the domain), after the discriminator re-read the domain checks each governed wave-number param; if its stored number is not a valid key in the newly-selected bank, it **rewrites that wave number to `WaveformBanks.FirstWave(...)`** (the bank's lowest number). This fires **only on an actual Group-Type/ID edit, never on plain load/read**, so a stored value is never silently clobbered (safe even if a bank list is incomplete). It is implemented centrally (keyed on a banked-wave discriminator write) so both surfaces share it; the implementation plan must ensure both the friendly and advanced write paths trigger this post-discriminator-write correction.

## Data flow

- **Load:** read partial → interpreter sets raw numbers → resolution pass sets `EffectiveRepr` + correct names from siblings → advanced grid (combo from `EffectiveRepr`, shows name) and friendly picker (Options/value from `WaveformBanks`) both display correctly. No value is changed on load.
- **Change wave:** user picks a name → reverse lookup via `EffectiveRepr`/`WaveformBanks` → write number to hardware.
- **Change bank (Group Type/ID):** write discriminator → re-read → resolution pass refreshes names/options → if the stored wave number is now out of range, central correction rewrites it to the bank's first wave.

## Error handling

- Unknown/Reserved Group Type → INT bank (safe default).
- Wave number absent from the resolved bank (on load) → display the raw number as an unresolved marker; never auto-changed on load.
- Missing/short CSV asset → the bank loads as far as it can; absent entries render as unresolved. A missing asset is a build/packaging error surfaced at startup (logged), not a crash.

## Testing

Pure/near-pure logic is unit-tested (NUnit):
- `WaveBankResolver` — `(groupType, groupId) → bank name`: Internal→INT, SRX 1..12→SRX{n}, Reserved/unknown→INT.
- `WaveformBanks` — `Name`/`Number` round-trip, out-of-range → unresolved, `FirstWave` returns the lowest key.
- The out-of-range reset decision — given (oldNumber, newBank), decide keep vs reset-to-first.
- The domain resolution pass — given a small fake set of wave + sibling FQPs, it sets the right `EffectiveRepr` and overrides `StringValue`.

The existing 244 tests stay green; the binary blob format is unchanged (banks ship as separate assets), so the parameter-load path is unaffected.

## Cleanup (part of this change)

- Remove the flat `PARTIAL_WAVEFORMS` repr and the `repr:PARTIAL_WAVEFORMS` tags on the 10 wave-number params (they become banked-wave).
- Retire `PartialWaveForms.csv` (superseded by `PartialWaveForms_INT.csv`).
- Mark the waveform-name issue resolved in `docs/KNOWN_ISSUES.md` and update the `waveform-name-list-bug` memory.

## Build approach (phased)

1. **Core:** ship the 13 CSV assets; `WaveformBanks` + `WaveBankResolver` (+ tests); `EffectiveRepr` on the FQP; the banked-wave registry; the domain resolution pass; mark Group Type/ID `IsParent`; retire `PARTIAL_WAVEFORMS`.
2. **Surfaces + reset:** advanced grid (`EffectiveRepr` in `DataTemplateProvider` + reverse converter); friendly wave pickers; the central out-of-range reset; docs/memory cleanup.

The writing-plans step breaks these into bite-sized tasks.

## Out of scope

SN-Drums wave names; any non-wave parameter; changing how Group Type/ID themselves are edited beyond marking them discriminators; embedding the banks in the binary blob (kept as assets by design).
