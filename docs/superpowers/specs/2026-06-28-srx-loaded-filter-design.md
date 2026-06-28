# Filter Wave Group ID (SRX board) to currently-loaded SRX — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan

## Goal

In the PCM sound editors, the **Wave Group ID** selector (the SRX board chosen when Bank/Wave
Group Type = `SRX`) should list **only the SRX boards currently loaded** into the INTEGRA-7's four
expansion slots — instead of always offering the full `1..12`. This applies to both the friendly
PCM editors and the raw "Advanced" parameter grid.

## Background — what's already there

- The INTEGRA-7 has **4 SRX slots**. The loaded set is read from the device at connect via
  `IIntegra7Api.GetLoadedSrxAsync()` and stored in `MainWindowViewModel.SrxSlot1..4` (each an int;
  the same encoding as the `SrxSelector` combo: `0`=Empty, `1..12`=SRX01..12, `13..18`=ExSN1..6,
  `19`=HQ). `LoadSrx` writes a new slot configuration to the device.
- **Wave Group ID** parameters (raw repr `null`, NUMERIC):
  - `PCM Synth Tone Partial/Wave Group ID` (sibling `Wave Group Type`, repr `INT_SRX_RES`).
  - `PCM Drum Kit Partial/WMT{1..4} Wave Group ID` (siblings `WMT{n} Wave Group Type`).
  - These pairings are already enumerated in `WaveBankRegistry.Entries` (as the siblings of the
    wave-number params).
- **Both surfaces already consume `FullyQualifiedParameter.EffectiveRepr ?? ParSpec.Repr`:**
  - Advanced grid: `DataTemplateProvider` builds the editor from this fallback
    (`DataTemplateProvider.cs:86,119`). A `null`-repr numeric param renders as a number field; once
    `EffectiveRepr` is set it renders as a **combo of those entries** (exactly how the wave-number
    params already become name combos).
  - Friendly editors: `ParamString.Options` already reflects `EffectiveRepr` when present, falling
    back to its static list (`SynthParam.cs:169-180`). The two friendly partial VMs
    (`PCMPartialViewModel`, `PCMDrumWmtLayerViewModel`) bind their SRX combo to `WaveGroupID.Options`.
  - The reverse converter `DisplayValueToRawValueConverter` maps display→raw via
    `EffectiveRepr ?? ParSpec.Repr`.
- The domain read path already runs a per-read resolution pass:
  `DomainBase.ReadFromIntegraAsync` calls `WaveNameResolution.Apply(_domainParameters,
  WaveformBanks.Default)` after copying the freshly-read values.

**Consequence:** setting a Wave Group ID param's `EffectiveRepr` to a filtered board list filters
*both* surfaces through machinery that already exists. This is the whole design.

## Approach

One `EffectiveRepr` resolution step, driven by a shared loaded-SRX set, run on every domain read.

### 1. Loaded-SRX state (`LoadedSrxState`)

A small singleton mirroring the existing `WaveformBanks.Default` static, holding the current loaded
board set:

- `LoadedSrxState.Default.Boards` → `IReadOnlyCollection<int>` of loaded SRX board numbers (values
  `1..12` only; ExSN/HQ ignored).
- `LoadedSrxState.Default.SetFromSlots(int s1, int s2, int s3, int s4)` → recomputes `Boards` as the
  distinct subset of `{s1,s2,s3,s4}` within `1..12`.

`MainWindowViewModel` calls `SetFromSlots(SrxSlot1..4)`:
- in `UpdateConnectedAsync`, immediately after `GetLoadedSrxAsync()` (so the set is known before the
  first domain read / part build), and
- at the end of `LoadSrx`, after the new configuration is sent.

The set as a parameter keeps the resolver pure/testable; the singleton is only the production wiring.

### 2. Pure filter + resolution pass (`SrxGroupIdResolution`)

A new service alongside `WaveNameResolution`:

- `VisibleBoards(IReadOnlyCollection<int> loaded, int current)` → ordered `List<int>`:
  `sort( (loaded ∪ {current}) ∩ [1..12] )`. Including `current` guarantees the patch's stored board
  stays selectable even when that SRX is not loaded (so the dropdown never blanks out / silently
  changes the value). Pure, unit-tested.
- `Apply(IReadOnlyList<FullyQualifiedParameter> ps, IReadOnlyCollection<int> loaded)`:
  for each distinct (Group Type path, Group ID path) pair derived from `WaveBankRegistry.Entries`:
  - resolve both params from `ps`; skip if either is absent;
  - if the Group **Type**'s `StringValue` == `"SRX"`:
    `current = (int)id.RawNumericValue`; `boards = VisibleBoards(loaded, current)`;
    `id.EffectiveRepr = boards.ToDictionary(b => b, b => b.ToString())`; then **re-assign
    `id.StringValue = id.StringValue`** so `INotifyPropertyChanged` fires and the friendly
    `ParamString.ApplyFromModel` / the advanced grid's `BindToModel` refresh callback pick up the new
    options;
  - else (Type != `"SRX"`): `id.EffectiveRepr = null` (numeric field, unchanged from today).

`EffectiveRepr` keys are the raw board numbers and the values are the same board strings the friendly
combo already uses, so display↔raw round-trips through the existing converter unchanged.

### 3. Wire into the read path

In `DomainBase.ReadFromIntegraAsync`, after the existing `WaveNameResolution.Apply(...)` line, add:

```csharp
SrxGroupIdResolution.Apply(_domainParameters, LoadedSrxState.Default.Boards);
```

This covers connect, every preset change, and every resync automatically (they all go through
`ReadFromIntegraAsync`). The two friendly partial VMs need **no change** — their hardcoded `1..12`
list stays as a harmless fallback that only applies when nothing is filtered (i.e. Type ≠ SRX, where
the combo is hidden anyway).

### 3a. Make the advanced-grid combo refresh content-aware

The friendly side recomputes `ParamString.Options` from `EffectiveRepr` on every read, so it is
always correct. The advanced grid's combo refresh in `DataTemplateProvider` only rebuilds its items
when **`c.Items.Count != repr.Count`** (`DataTemplateProvider.cs:120`). When the loaded SRX set
changes to a **same-size but different** set (e.g. `{2,6,11}` → `{3,7,9}`), the count is unchanged,
so the combo would keep stale board numbers (its SelectedItem would still be set correctly). Change
the rebuild condition to compare the **entries** (by key order), not just the count, so a same-size
change rebuilds the list. This is a small, general fix that also closes the same latent gap for the
wave-number name combos. The selected value is re-applied afterwards as today.

### 3b. Sibling Group Type must be `IsParent` (it already is)

Changing Group Type (Internal↔SRX) must re-run the resolution so the ID combo appears/clears. The
Group Type params are already `isparent:true`, so a Group Type write already triggers a domain
re-read → `SrxGroupIdResolution.Apply` runs with the current loaded set. No new reactivity needed.

### 4. Live refresh after "Load selected SRX!"

A normal preset change already refreshes via the read path. The only extra case is loading SRX
without changing the preset. After `LoadSrx` updates `LoadedSrxState`, **trigger the existing
per-part resync for all parts** (publish `UpdateResyncPart` for each part / reuse the existing
`ResyncPartAsync` loop). The resync re-reads each part, which re-runs `SrxGroupIdResolution.Apply`
with the new loaded set and refreshes both surfaces. A full resync is appropriate here anyway: the
device has just reloaded its wave memory.

## Data flow

```
connect / Load SRX ─▶ GetLoadedSrxAsync / SrxSlot1..4 ─▶ LoadedSrxState.SetFromSlots
                                                              │
preset change / resync / connect read ─▶ DomainBase.ReadFromIntegraAsync
                                                              │
                          WaveNameResolution.Apply (names)    │
                          SrxGroupIdResolution.Apply ◀─────────┘  (reads LoadedSrxState.Boards)
                                  │ sets EffectiveRepr on Wave Group ID (Type==SRX) + re-fires StringValue
                    ┌─────────────┴──────────────┐
       friendly ParamString.Options       advanced grid combo (DataTemplateProvider)
       (EffectiveRepr-aware)              (EffectiveRepr ?? Repr)
```

## Out of scope (YAGNI)

- The Group **Type** "SRX" option is left available even when no SRX is loaded (selecting it then
  yields an ID list of just the current board, or empty). Not hidden.
- ExSN / HQ slots and the SN (SuperNATURAL) editors are untouched — they use instrument variations,
  not SRX wave groups.
- No change to how SRX boards are *loaded* (the `SrxSelector` still lists all boards — that is the
  loader UI, not a sound editor).

## Testing

Pure unit tests (no hardware), in the existing NUnit project:

- `SrxGroupIdResolution.VisibleBoards`:
  - loaded `{2,6,11}`, current `5` → `[2,5,6,11]` (current merged in, sorted);
  - loaded `{2,6}`, current `6` → `[2,6]` (no duplicate);
  - loaded `{}`, current `5` → `[5]` (current only);
  - out-of-range values (`0`, `13`, `19`) dropped.
- `SrxGroupIdResolution.Apply` over a small hand-built FQP set:
  - Type=`SRX`, ID raw `5`, loaded `{2,6}` → ID `EffectiveRepr` == `{2,5,6}`;
  - Type=`Internal` → ID `EffectiveRepr` == `null`;
  - unloaded current board is retained in `EffectiveRepr`.
- `LoadedSrxState.SetFromSlots`: `(2,0,6,13)` → `Boards` == `{2,6}` (Empty/ExSN dropped, deduped).

## Files

- **Create:** `Src/Models/Services/LoadedSrxState.cs`, `Src/Models/Services/SrxGroupIdResolution.cs`,
  `Tests/TestSrxGroupIdResolution.cs`, `Tests/TestLoadedSrxState.cs`.
- **Modify:** `Src/Models/Domain/DomainBase.cs` (one call in `ReadFromIntegraAsync`),
  `Src/ViewModels/MainWindowViewModel.cs` (`SetFromSlots` on connect + after `LoadSrx`; resync-all
  after `LoadSrx`), `Src/DataTemplates/DataTemplateProvider.cs` (content-aware combo refresh, §3a).
- **Unchanged:** the friendly partial VMs, the reverse converter — they already honor
  `EffectiveRepr`.
