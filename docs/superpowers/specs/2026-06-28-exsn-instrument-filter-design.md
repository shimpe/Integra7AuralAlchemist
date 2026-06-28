# Filter SN-Acoustic instrument picker to loaded ExSN expansions (Phase 2) — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan
**Predecessor:** Phase 1 — `docs/superpowers/specs/2026-06-28-srx-loaded-filter-design.md` (SRX wave groups, merged)

## Goal

In the friendly SuperNATURAL-Acoustic editor, the instrument picker's **"Expansion" family** lists every
ExSN1–5 instrument regardless of which expansion boards are loaded. Filter it so it shows **only
instruments from currently-loaded ExSN boards**:

- Hide the **"Expansion" family entirely** when no ExSN board is loaded.
- Within "Expansion", show only instruments whose ExSN board is loaded.
- **Keep the patch's current instrument visible** even when its ExSN board isn't loaded, **labelled
  "(not loaded)"** (consistent with Phase 1's SRX-board treatment) — so the picker never goes blank.
- **Refresh live** when expansions are (re)loaded via "Load selected SRX!".

## Background — what's there

- The "Instrument" parameter (`SuperNATURAL Acoustic Tone Common/Instrument`) is a **discrete** param
  (`type:DISC`, `discrete:INSTRUMENT_VARIATIONS`). Its display string **is** the catalog name, e.g.
  `"ExSN3 001: TC Guitar w/Fing"`, and write-back matches that exact string against the static discrete
  list (`DisplayValueToRawValueConverter`, discrete branch). A `"(not loaded)"`-suffixed string would
  therefore **not** round-trip through the discrete write path — unlike Phase 1's numeric + `EffectiveRepr`
  boards, which used a reverse-lookup we controlled.
- `INSTRUMENT_VARIATIONS` (127 entries): indices 0–76 = `"INT NNN: …"`; 77–126 = the Expansion block,
  with clean `"ExSN{k} NNN: …"` prefixes — ExSN1 = 77–86, ExSN2 = 87–101, ExSN3 = 102–109,
  ExSN4 = 110–115, ExSN5 = 116–126.
- `InstrumentCatalog` groups these into families; the last family **"Expansion"** = indices 77–126.
- The picker is the reusable two-step **`DiscriminatedParamSectionViewModel`** (family combo →
  instrument combo + a modify-param grid), **also used by the friendly MFX panel**. Catalog is supplied
  as delegates (`families`, `familyOf`, `valuesIn`, friendly labels). `ValuesInFamily` is built
  positionally: `_valuesIn(family).Select(i => Discriminator.Options[i])`, where `Discriminator` is a
  `ParamString` over the Instrument param whose `Options` is the full 127-name list. `SelectedValue`
  (the instrument combo's selection) ↔ `Discriminator.Value` (the discrete name). The modify-param grid
  recomputes from `Discriminator.Value`.
- `LoadedSrxState` (Phase 1) maps the 4 slots to loaded SRX boards (1–12) and is set by
  `MainWindowViewModel` at connect and after `LoadSrx`. ExSN occupy the same slots with values 13–18
  (ExSN1–6); the acoustic catalog only references ExSN1–5.

## Approach

Extend the **shared** `DiscriminatedParamSectionViewModel` with *additive, default-identity* hooks so
the SN-A editor can supply loaded-aware filtering + display labelling + a re-projection trigger, while
the friendly MFX panel is unchanged. Filtering logic lives in a pure, unit-tested service.

### 1. Loaded-ExSN source (`LoadedSrxState` extension)

- Add `ExSnBoards` → `IReadOnlyCollection<int>` of loaded ExSN board numbers (slot values 13–18 →
  boards 1–6; the acoustic filter only ever matches 1–5), computed in `SetFromSlots` alongside the
  existing SRX `Boards`.
- Add an event `event Action? Changed;` raised at the end of `SetFromSlots`.
- `MainWindowViewModel` already calls `SetFromSlots(...)` at connect and after `LoadSrx`, so `Changed`
  fires at both points with no new call sites.

### 2. Pure filter (`ExSnInstrumentFilter`, unit-tested)

A new static service:

- `int? ExSnBoardOf(string instrumentName)` → the board number if the name starts with `"ExSN{k}"`
  (k = 1..6), else null. (Parse the digit after `"ExSN"`.)
- `List<string> VisibleFamilies(IReadOnlyList<string> allFamilyNames, IReadOnlyCollection<int> loadedExSn)`
  → `allFamilyNames` with `"Expansion"` removed when `loadedExSn` is empty; order otherwise preserved.
- `List<int> LoadedExpansionIndices(IReadOnlyList<int> expansionIndices, IReadOnlyList<string> names,
  IReadOnlyCollection<int> loadedExSn)` → the subset of `expansionIndices` whose `names[i]` parses to a
  loaded ExSN board.
- `string DisplayName(string instrumentName, IReadOnlyCollection<int> loadedExSn)` →
  `instrumentName + " (not loaded)"` when it's an ExSN instrument whose board isn't loaded, else the
  name unchanged. Reuse `SrxGroupIdResolution.NotLoadedSuffix` for the suffix so Phase 1 and Phase 2
  stay consistent.

### 3. Picker VM extension (`DiscriminatedParamSectionViewModel`) — additive

All new parameters are optional with identity defaults; the MFX call site passes none (only adapting
to the families-supplier signature). New capabilities:

- **Dynamic families.** Replace the captured static `families` list with a **supplier**
  `Func<IReadOnlyList<string>> familiesSupplier`; `Families` becomes a change-notifying property
  recomputed from the supplier (so "Expansion" can appear/disappear). MFX passes `() => itsFamilies`.
- **Display hooks.** Optional `Func<int, string, string>? displayName` (index, realName → shown text)
  and `Func<string, string>? toRealValue` (shown text → real discrete name), default identity. The
  instrument combo's `ValuesInFamily` and `SelectedValue` use `displayName(...)`; the `SelectedValue`
  setter maps back with `toRealValue(...)` before assigning `Discriminator.Value`. So the labelled
  string never reaches the discrete write path, and the modify-param grid (keyed on `Discriminator.Value`,
  the real name) is unaffected.
- **Keep current selection visible (generic).** After computing `Families`/`ValuesInFamily`, the VM
  injects the current selection's family into `Families` (if absent) and its display into
  `ValuesInFamily` (if absent), so a filtered list can never blank the current selection. No-op for MFX
  (the current value is always in the unfiltered lists).
- **`Reproject()`** (public): recompute `Families` from the supplier and rebuild the current family's
  `ValuesInFamily`/`SelectedValue` (i.e. re-run the sync from `Discriminator.Value`). Used for live
  refresh.

### 4. SN-A wiring + live refresh (`SNAcousticToneEditorViewModel`)

- Build the `Instrument` section with loaded-aware delegates that read `LoadedSrxState.Default.ExSnBoards`
  and `ExSnInstrumentFilter`:
  - `familiesSupplier` = `() => ExSnInstrumentFilter.VisibleFamilies(InstrumentCatalog family names, ExSnBoards)`.
  - `valuesIn` = `family => family == "Expansion"
      ? ExSnInstrumentFilter.LoadedExpansionIndices(InstrumentCatalog.ValuesIn("Expansion"), names, ExSnBoards)
      : InstrumentCatalog.ValuesIn(family)`.
  - `familyOf` = `InstrumentCatalog.FamilyOf` (unchanged).
  - `displayName` = `(i, name) => ExSnInstrumentFilter.DisplayName(name, ExSnBoards)`.
  - `toRealValue` = strip the `" (not loaded)"` suffix if present.
- **Subscribe to `LoadedSrxState.Default.Changed`** in the ctor and call `Instrument.Reproject()` on
  change; **unsubscribe in `Dispose`** (SN-A editors are disposed/rebuilt on preset change). This makes
  "Load selected SRX!" live-update the picker.

## Data flow

```
connect / Load SRX ─▶ SetFromSlots ─▶ LoadedSrxState.ExSnBoards updated ─▶ Changed event
                                                                                │
SN-A editor (subscribed) ─▶ Instrument.Reproject() ─▶ familiesSupplier()/valuesIn()/displayName()
                                                          read ExSnBoards via ExSnInstrumentFilter
                                                                                │
                                family combo (Families)         instrument combo (ValuesInFamily, labelled)
                                                                                │
                              SelectedValue ──toRealValue──▶ Discriminator.Value (real discrete name)
                                                                                │
                                                       modify-param grid (Recompute, unchanged)
```

## Edge cases

- **Patch uses an unloaded ExSN instrument:** `VisibleFamilies` drops "Expansion" only when no ExSN is
  loaded; but the VM's keep-current step re-injects the current instrument's family ("Expansion") and its
  labelled display, so it stays visible and selected. Selecting it again maps the label back to the real
  name and writes the correct (unchanged) instrument.
- **No ExSN loaded and current is an INT instrument:** "Expansion" hidden; INT families and selection
  unchanged.
- **MFX panel:** passes the constant families supplier and no display hooks; keep-current and the
  identity transforms make its behavior identical to today.

## Testing

Pure unit tests (no hardware):

- `ExSnInstrumentFilter.ExSnBoardOf`: `"ExSN3 001: …"` → 3; `"ExSN5 011: …"` → 5; `"INT 001: …"` → null;
  `"Strings"` → null.
- `VisibleFamilies`: with `loadedExSn = {}` drops "Expansion"; with `{2}` keeps it; order preserved.
- `LoadedExpansionIndices`: given the Expansion indices + names + `loadedExSn = {3}`, returns only the
  ExSN3 indices.
- `DisplayName`: ExSN3 name with `{2}` loaded → `"… (not loaded)"`; with `{3}` loaded → unchanged; INT
  name → unchanged.
- `LoadedSrxState`: `ExSnBoards` from slots `(2, 13, 17, 0)` → `{1, 5}` (SRX2 ignored here; ExSN1, ExSN5);
  `Changed` fires on `SetFromSlots`.

The VM hook wiring (display round-trip, keep-current, Reproject) is thin glue verified by build + the
full suite staying green; the MFX panel's unchanged behavior is confirmed in review.

## Files

- **Create:** `Src/Models/Services/ExSnInstrumentFilter.cs`, `Tests/TestExSnInstrumentFilter.cs`,
  and add `ExSnBoards`/`Changed` tests to `Tests/TestLoadedSrxState.cs`.
- **Modify:** `Src/Models/Services/LoadedSrxState.cs` (ExSnBoards + Changed),
  `Src/ViewModels/DiscriminatedParamSectionViewModel.cs` (families supplier, display hooks,
  keep-current, Reproject), `Src/ViewModels/SNAcousticToneEditorViewModel.cs` (loaded-aware delegates +
  Changed subscription), and the MFX call site (`MfxPanelViewModel`) for the families-supplier signature.

## Out of scope

- SN-Synth (no ExSN), SN-Drums, and phrase lists (not slot-gated); ExSN6/SFX (not in the acoustic
  catalog). No change to how expansions are loaded (the `SrxSelector`).
