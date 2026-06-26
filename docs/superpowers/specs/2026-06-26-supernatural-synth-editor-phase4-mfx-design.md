# SuperNATURAL Synth (SN-S) Editor — Design (Phase 4: MFX panel)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** Phases 1–3 (merged to `main`). New branch: `sns-mfx`.

## 1. Goal

Add the SN-S tone's **Multi-Effect (MFX)** to the friendly editor as a usable, musical panel —
not a duplicate of the raw "Advanced — MFX" tab. The Integra-7 MFX has **68 effect types**
(`0 = Thru`), and each type repurposes a generic bank of 32 numbered parameters with type-specific
meaning. A **hybrid** approach gives the friendliness without a hand-built 68×32 map: a categorized
effect-type picker + bypass + send faders + the current type's parameters rendered **dynamically**
from the parameter DB (which already names them per type), with generic label cleanup and a link to
the advanced tab for the deep routing.

Reuse all Phase 1–3 infrastructure: param wrappers, throttled writer, live FQP binding, color
resources, and — new for this phase — the existing generic value renderer
`DataTemplateProvider.ParameterValueTemplate`. See memory `parameter-domain-infrastructure`,
`sn-s-tone-parameters`, `visual-editor-pattern`, `no-hardcoded-colors-in-xaml`.

## 2. Decisions locked during brainstorming

1. **Hybrid** scope (not a pure dynamic dump, not a hand-curated 68-type map): categorized picker +
   bypass + sends + **dynamic per-type controls** reusing the names already in the DB + generic
   label cleanup + advanced link. Per-effect hand-curation is explicitly a possible *later* pass.
2. **Placement:** a third **"FX"** `TabItem` in the selected-partial editor's `TabControl` (peer to
   Sound/Motion), headed "Multi-Effect — applies to the whole tone" so the tone-wide scope is clear.
   Copy/Paste/Init stay below all three tabs (they are partial-scoped and do not touch MFX).
3. **Effect-type picker:** **two combos** — Family (~11) → Type (within family). Short, scannable
   lists; no custom combo grouping template. The Family combo auto-syncs to the current type.

## 3. SN-S MFX parameters and infrastructure (verified)

Domain: `Integra7Domain.SNSynthToneCommonMFX(part)` → shared cached `DomainBase`, prefix
`SuperNATURAL Synth Tone Common MFX/`. Lookup path = `ParSpec.Path` = `<prefix>/<name>`.

Type-independent params (always present):
- `MFX Type` (enum `MFX_TYPE`, 0–67; `0 = Thru`).
- `MFX Chorus Send Level` (0–127), `MFX Reverb Send Level` (0–127).
- `MFX Control 1–4 Source/Sens/Assign` and reserved — **left to the Advanced tab** (routing).

Per-type params: `MFX Parameter 1 … 32`, each defined once **per effect type** with a conditional
discriminator. In the blob each variant is a distinct spec whose `ParSpec`:
- `Path` ends `.../MFX Parameter N/<EffectName> <ParamName>` (e.g.
  `.../MFX Parameter 1/Equalizer Low Freq`).
- `Name = Path.Split('/')[^1]` = the leaf, i.e. `"Equalizer Low Freq"`.
- `ParentCtrl = ".../MFX Type"`, `ParentCtrlDispValue = "<EffectName>"` (e.g. `"Equalizer"`);
  some have a second discriminator (`ParentCtrl2`/`ParentCtrlDispValue2`).

Key reuse points (no new write path, no new renderer):
- `DomainBase.GetRelevantParameters(IncludeReserved:false, IncludeInvalidInContext:false)` returns
  exactly the params **valid for the current `MFX Type`** (it evaluates `ValidInContext`, which
  checks `ParentCtrlDispValue` against the live parent value). Recompute on `MFX Type` change to get
  the per-type list.
- `DataTemplateProvider.ParameterValueTemplate` (a `FuncDataTemplate<FullyQualifiedParameter>`)
  renders any FQP as the correct control — TextBox / discrete ComboBox / OFF-ON ToggleSwitch / enum
  ComboBox / bipolar (`OMin2/OMax2`) Slider / plain Slider — and wires both directions: it pushes
  edits via `MessageBus.Current.SendMessage(new UpdateMessageSpec(p, value), "ui2hw")` and tracks
  hardware via the FQP's `StringValue` `PropertyChanged`. The dynamic grid reuses it verbatim.
- The nav callback is already `Action<string,int?>`; `("SN-S-MFX", null)` selects the raw tab.

All MFX FQPs are the **shared** instances used by the raw "Advanced — MFX" tab, so edits made in this
panel, on the hardware, or in the raw tab all stay mutually consistent.

## 4. New / changed components

### 4a. `MfxCatalog` (new, `Src/Models/Services/`) — pure, unit-tested
Static data + helpers, no Avalonia/domain dependency:
- `Families`: ordered list of `(string Name, int[] TypeIndices)` partitioning **all** of 0–67 into
  ~11 families. Proposed grouping (indices per `MFX_TYPE`):
  - **EQ & Filter** 1,2,3,4,5 · **Wah & Voice** 6,7 · **Phaser** 9,10,11,12,13,14 ·
    **Chorus & Mod** 22,23,24,25,26,27 · **Tremolo & Pan** 15,16,17,18 · **Rotary** 19,20,21 ·
    **Amp & Distortion** 8,28,29,30 · **Dynamics** 31,32,33 ·
    **Delay** 34,35,36,37,38,39,40 · **Lo-Fi & Pitch** 41,42,43,44 · **Combos** 45–67 ·
    **Off** 0 (Thru). (Exact placement may be tuned during implementation; the test enforces total
    coverage, not a specific family per type.)
- `string FamilyOf(int typeIndex)`, `IReadOnlyList<int> TypesIn(string family)`.
- `string FriendlyParamName(string effectTypeName, string leafName)` and an overload
  `IReadOnlyList<string> FriendlyParamNames(string effectTypeName, IReadOnlyList<string> leafNames)`:
  strip the effect-name prefix. Rule: compute the **longest common leading prefix** shared by all of
  the type's `leafNames` that ends at a word boundary; strip it from each. For the single-param case
  or when no common word-boundary prefix exists, fall back to stripping `effectTypeName + " "` if the
  leaf starts with it; else return the leaf unchanged. Examples (from the DB):
  `("Equalizer","Equalizer Low Freq") → "Low Freq"`,
  `("Time Ctrl. Delay","Time Ctrl. Delay Time (ms-note)") → "Time (ms-note)"`,
  `("Overdrive->Chorus","Overdrive->Chorus Overdrive Drive") → "Overdrive Drive"`,
  punctuation mismatch `("Overdrive/Distortion->TouchWah","Overdrive-Distortion->TouchWah Drive Switch")`
  still strips via the common-prefix path when ≥2 params share it, else falls back to the raw leaf.

### 4b. `MfxPanelViewModel` (new, `Src/ViewModels/`)
Reusable VM for one tone's MFX, built from the CommonMFX domain.
Constructor: `MfxPanelViewModel(DomainBase mfxDomain, ThrottledParameterWriter writer, Action navigateToAdvanced)`.
- Builds `byPath = mfxDomain.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path)`.
- `Type` — `ParamString` over `…/MFX Type` (display = `MFX_TYPE` name).
- `ChorusSend`, `ReverbSend` — `ParamInt` 0–127 over the two send paths.
- Picker projection: `IReadOnlyList<string> Families` (from `MfxCatalog`), `SelectedFamily`
  (`[Reactive]`), `TypesInFamily` (recomputed from `SelectedFamily`), `SelectedType` (`[Reactive]`,
  a type display name). Setting `SelectedType` writes `Type.Value`. When `Type.Value` changes
  (picker / hardware / raw tab), `SelectedFamily`+`SelectedType` re-sync from the type index without
  echoing back (a `_suppress` guard, mirroring the Phase-2 editors).
- `Bypass` (`bool`, raised when `Type` changes): a single-source-of-truth view over `Type` —
  `get => Type.Value == "Thru"`. The setter is the only place that remembers state: setting `true`
  stores the current non-Thru type in `_lastEffectType` then sets `Type.Value = "Thru"`; setting
  `false` restores `_lastEffectType` (default `"Equalizer"` if none yet). No separate backing flag,
  so hardware/raw-tab type changes keep `Bypass` correct automatically.
- `ObservableCollection<MfxParamDisplay> TypeParameters`, where
  `MfxParamDisplay { FullyQualifiedParameter Param; string Label }`. Recompute whenever `Type.Value`
  changes: take `mfxDomain.GetRelevantParameters(false, false)`, keep only
  `Path.Contains("/MFX Parameter ")`, order by `ParSpec.AddressInt`, and project each into
  `MfxParamDisplay` with `Label = MfxCatalog.FriendlyParamName(currentTypeName, leaf)` (computed via
  the batch overload so the common-prefix is shared). Marshal updates to the UI thread
  (`Dispatcher.UIThread.Post`) since the trigger may arrive on a MIDI thread.
- `HasTypeParameters => TypeParameters.Count > 0` for the empty-state note.
- `[ReactiveCommand] AdvancedMfx()` → `navigateToAdvanced()`.
- `IReadOnlyList<IParam> Params` = `{ Type, ChorusSend, ReverbSend }` — exposed for potential future
  *tone-level* actions. It is deliberately **not** folded into the partial copy/paste set (MFX is
  tone-wide; see §6).
- `IDisposable`: disposes the wrappers and unsubscribes the `Type` watcher.

### 4c. `MfxPanelView` (new, `Src/Views/`) + `MfxPanelView.axaml.cs`
`UserControl` bound to `MfxPanelViewModel` (`x:DataType`, `x:CompileBindings`). Layout (top→bottom):
- Title "Multi-Effect" + muted "— applies to the whole tone"; a **Bypass** `ToggleSwitch` at the right.
- **Effect**: Family `ComboBox` (`Families`/`SelectedFamily`) + Type `ComboBox`
  (`TypesInFamily`/`SelectedType`). Disabled-look is unnecessary; when Bypass is on the type is "Thru".
- **Sends**: "Chorus send" + "Reverb send" sliders (0–127), friendly `sliderLabel` style.
- Separator, then **`<Effect> — parameters`** and an `ItemsControl` over `TypeParameters` in a
  `WrapPanel`; each item = fixed-width cell with a `TextBlock Text="{Binding Label}"` and a
  `ContentControl Content="{Binding Param}"
   ContentTemplate="{x:Static dataTemplates:DataTemplateProvider.ParameterValueTemplate}"`.
- Empty state (`IsVisible="{Binding !HasTypeParameters}"`): "This effect has no extra parameters."
- **"Advanced MFX parameters…"** `Button` → `AdvancedMfxCommand`.
- All colors via `{StaticResource}` (`Sn*` brushes); a new `SnPanelBackgroundBrush` is already
  available. No new brush is strictly required; add one only if a new accent is needed.

### 4d. `SNSynthToneEditorViewModel` changes
- Add `public MfxPanelViewModel Mfx { get; }`, built in the constructor from
  `domain.SNSynthToneCommonMFX(partNo)`, the shared `_writer`, and
  `() => _navigateToRawTab?.Invoke("SN-S-MFX", null)`.
- Add `[ReactiveCommand] AdvancedMfx()` is **not** needed on the editor VM (the panel relays its own);
  the editor only passes the navigate delegate.
- Dispose `Mfx` in `Dispose()`.
- Copy/paste: include `Mfx.Params` (Type + sends) in the editable set **only if** the existing
  partial clipboard is intended to carry tone-common MFX. Per §6 this is left **out** of the
  partial copy/paste by default (MFX is tone-wide, copying a partial should not silently change the
  whole tone's effect). Decision recorded in §6.

### 4e. View restructure (`Src/Views/SNSynthToneEditorView.axaml`)
Add a third `TabItem` to the existing Sound/Motion `TabControl`:
```
TabItem "FX" → ScrollViewer → ContentControl Content="{Binding Mfx}"   (ViewLocator → MfxPanelView)
```
Sound/Motion unchanged; Copy/Paste/Init row (Grid.Row=1) unchanged.

## 5. State, sending, mapping
- Picker (`Type`) and sends use `ParamString`/`ParamInt` → per-key throttled write →
  `DomainBase.WriteToIntegraAsync`, with FQP `PropertyChanged` sync back (Phases 1–3 pattern).
- The dynamic per-type controls use `ParameterValueTemplate`'s `ui2hw` `MessageBus` path and its own
  `BindToModel` hardware sync — the same path the raw MFX tab uses. Both paths target the shared FQP
  instances, so the panel, hardware, and raw tab stay consistent.
- `MFX Type` change (any source) → recompute `TypeParameters` + re-sync Family/Type combos.
- Bypass is a pure convenience over `MFX Type` (Thru ↔ last effect); no separate hardware param.

## 6. Copy/paste scope (explicit)
MFX is **tone-common**, not partial-scoped. The friendly Copy/Paste/Init act on a *partial*.
Therefore the MFX type/sends/params are **excluded** from the partial copy/paste/init to avoid a
partial paste silently re-patching the whole tone's effect. (The raw tone-level save/restore paths
already carry MFX.) `Mfx.Params` exists for potential future tone-level actions but is not wired into
the partial clipboard.

## 7. Accessibility
All controls are standard labelled inputs (two combos, toggle, sliders, and the per-type controls
from `ParameterValueTemplate`), keyboard-accessible by default. Bypass and Thru both carry text; the
empty-state is text. Color is never the only signal.

## 8. Testing (`Tests/`, NUnit, pure)
- `MfxCatalog` coverage: the union of `Families[*].TypeIndices` equals `{0..67}` with no duplicates;
  `FamilyOf(i)` returns a family that contains `i` for every `i`; `TypesIn(FamilyOf(i))` contains `i`.
- `FriendlyParamName` / `FriendlyParamNames` against the real DB samples:
  `Equalizer Low Freq → Low Freq`, `Spectrum Band1 (250Hz) → Band1 (250Hz)`,
  `Time Ctrl. Delay Time (ms-note) → Time (ms-note)`,
  `Overdrive->Chorus Overdrive Drive → Overdrive Drive`, and the punctuation-mismatch case
  (`Overdrive-Distortion->TouchWah …`) returns a non-empty, prefix-trimmed-or-raw label (never empty).
- Single-param type: `FriendlyParamName` never returns an empty string (guard tested).
- The panel/view are otherwise verified by build + the hardware smoke test.

## 9. Acceptance criteria (Phase 4)
- Selecting an SN-S partial shows **Sound / Motion / FX** sub-tabs; FX is headed as tone-wide.
- FX shows: Family→Type pickers, a Bypass toggle, Chorus/Reverb send faders, the current type's
  parameters with cleaned labels (correct widgets via `ParameterValueTemplate`), an empty-state for
  Thru, and an "Advanced MFX parameters…" link that opens the raw tab.
- Changing the type (picker / hardware / raw tab) updates the per-type controls and re-syncs the
  Family/Type combos; Bypass toggles Thru ↔ last effect.
- All controls edit live with correct mapping; hardware and the raw tab stay consistent.
- No hardcoded colors in XAML. Tests in §8 pass; full solution builds.

## 10. Out of scope (later)
Hand-curated "hero" controls per effect; MFX Control 1–4 routing (stays in the Advanced tab);
tone-level copy/paste of MFX; Performance Response panel; macros; starter patches; safe Solo/Mute.
