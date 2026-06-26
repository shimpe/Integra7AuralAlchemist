# SuperNATURAL Acoustic (SN-A) friendly editor — Design

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** the SN-S friendly editor + the generalized MFX panel (all merged to `main`).
**First of:** the multi-engine friendly-editor roadmap (SN-A → PCM Synth → SN-Drums → PCM Drums).

## 1. Goal

A friendly visual editor for the SuperNATURAL **Acoustic** tone — a single modelled instrument (no
partials). It answers "which instrument, how does it sound, how does it respond" with musical
controls, mirroring the SN-S editor's look so the two feel consistent, and **reuses** the generalized
MFX panel and the conditional-parameter infrastructure.

## 2. SN-A structure (verified in `ParameterDefinitions.cs`)

`Integra7Domain.SNAcousticToneCommon(part)` (prefix `SuperNATURAL Acoustic Tone Common/`) and
`SNAcousticToneCommonMFX(part)`. **No partials.** Key params:
- **Header / base:** `Tone Name`, `Tone Level`, `Mono-Poly`, `Portamento Time Offset`, `Octave Shift`,
  `Category`.
- **Character offsets** (tweak the model): `Cutoff Offset`, `Resonance Offset`, `Attack Time Offset`,
  `Release Time Offset` (bipolar), plus **Vibrato** `Rate`/`Depth`/`Delay`.
- **Instrument:** `Instrument` — a **DISCRETE** param (`discrete: INSTRUMENT_VARIATIONS`, ~100 named
  instruments `"INT 001: Concert Grand"…` + expansions, already ordered by family).
- **Instrument-specific tweaks:** `Modify Parameter 1…32`, **named per instrument and gated by
  `Instrument`** — e.g. `Modify Parameter 1/ConcertGrand String Resonance`
  (`par: ".../Instrument", parval: INSTRUMENT_VARIATIONS[k].Item2`). **Identical in shape to the MFX
  per-type params.** (Note: the param-name prefix `"ConcertGrand"` differs from the discrete display
  `"INT 001: Concert Grand"`, so label cleanup must use the common-prefix path — see §4a.)
- **Misc:** `Phrase Number`, `Phrase Octave Shift`, `TFX Switch`.

## 3. Decisions locked during brainstorming

1. **Layout: Header + `Tone`/`FX` tabs**, mirroring the SN-S editor (chosen over a single scrolling
   page for cross-editor familiarity).
2. **Instrument picker: two combos** (family → instrument), mirroring the MFX picker.
3. **Instrument-detail = the MFX dynamic-grid pattern** (picker + named-param grid + label cleanup).
4. **Extract a shared component**: the "discriminator picker + dynamic named-param grid + label
   cleanup" is factored out of the MFX panel into a reusable unit and used by **both** the MFX panel
   and SN-A's instrument detail (vs. duplicating it).

## 4. New / changed components

### 4a. `DiscriminatedParamCatalog` abstraction + `InstrumentCatalog` (new, `Src/Models/Services/`)
The shared grid needs a per-engine "family catalog". Generalize the idea behind `MfxCatalog`:
```csharp
public interface IDiscriminatorCatalog
{
    IReadOnlyList<string> Families { get; }          // display order
    string FamilyOf(int valueIndex);                  // "" if out of range
    IReadOnlyList<int> ValuesIn(string family);       // indices into the discriminator's option list
}
```
- `MfxCatalog` implements it (rename `TypeIndices`→value indices; `TypesIn`→`ValuesIn`); its existing
  tests stay green.
- `InstrumentCatalog` (new, tested): groups the ~100 `INSTRUMENT_VARIATIONS` indices into ~15 families
  (Pianos, E.Pianos, Clav, Mallets/Bells, Organ, Reeds/Accordion, Guitars, Basses, Strings, Choir,
  Brass, Sax, Woodwind, Ethnic, Expansion). Grouping is mechanical (instruments are family-ordered);
  the test asserts total coverage (every index in exactly one family), like `MfxCatalog`.
- `MfxCatalog.FriendlyParamNames`/`FriendlyParamName` (label cleanup) is **already generic** — keep
  it, but move/alias it to a neutral home (e.g. a static `DiscriminatedParams.FriendlyNames`) shared
  by both. SN-A relies on its **common-prefix** branch (the param prefix `"ConcertGrand"` ≠ the
  display `"INT 001: Concert Grand"`, so the typed-strip falls through to common-prefix, which
  correctly yields `"String Resonance"`).

### 4b. `DiscriminatedParamSectionViewModel` (new, `Src/ViewModels/`) — the shared picker+grid
Factored from `MfxPanelViewModel`'s picker+grid logic. Constructor:
`(DomainBase domain, ThrottledParameterWriter writer, string discriminatorLeafName, IDiscriminatorCatalog catalog, string gridPathSegment)`.
- Builds the discriminator wrapper as a `ParamString` over the param whose `ParSpec.Name ==
  discriminatorLeafName`, with `Options` from the param's `Repr` **or** `Discrete` (so it works for
  the MFX Type enum and the Instrument discrete list).
- Picker projection: `Families` (from catalog), `SelectedFamily`, `ValuesInFamily`, `SelectedValue`
  — same `_syncing`-guarded two-combo logic the MFX panel has now.
- Dynamic grid: `ObservableCollection<DisplayParam> Params` (a `record DisplayParam(FQP Param, string
  Label)` — rename of `MfxParamDisplay`), recomputed from `domain.GetRelevantParameters(false,false)`
  filtered to `Path.Contains(gridPathSegment)`, ordered by `AddressInt`, labelled via the shared
  `FriendlyNames`. **Recompute only on discriminator changes** (the parent-referenced FQPs), marshalled
  with `Dispatcher.UIThread.Post` — exactly the hardened MFX logic. `HasParams`. `IDisposable`.
- A matching `DiscriminatedParamSectionView` (UserControl): family combo + value combo, then the
  `ItemsControl` grid using `DataTemplateProvider.ParameterValueTemplate`, empty-state text.

### 4c. `MfxPanelViewModel` / `MfxPanelView` refactor to use 4b
- `MfxPanelViewModel` keeps its MFX-specific extras (Chorus/Reverb sends, Bypass=Thru, AdvancedMfx
  link, "tone-wide" heading) and **embeds** a `DiscriminatedParamSectionViewModel` built with
  `discriminatorLeafName:"MFX Type"`, `catalog: MfxCatalog`, `gridPathSegment:"/MFX Parameter "`.
- The SN-S **FX tab** and the MFX nested-discriminator behaviour are the regression tests — they must
  behave identically. (Bypass/sends/labels unchanged.)

### 4d. SN-A view-models (new, `Src/ViewModels/`)
- `SNAcousticToneEditorViewModel` — owns a `ThrottledParameterWriter`, the header/character wrappers,
  the **Instrument** `DiscriminatedParamSectionViewModel` (`"Instrument"`, `InstrumentCatalog`,
  `"/Modify Parameter "`), the **Mfx** `MfxPanelViewModel` (from `SNAcousticToneCommonMFX`), a
  `navigateToRawTab` callback (to the raw SN-A tabs), and `IDisposable`.
  - Header wrappers: `ToneLevel`, `IsMono` (Mono-Poly), `Portamento`, `OctaveShift`, `Category`,
    `ToneName` (read-only display).
  - Character wrappers: `CutoffOffset`, `ResonanceOffset`, `AttackOffset`, `ReleaseOffset` (bipolar),
    `VibratoRate`, `VibratoDepth`, `VibratoDelay`.
  - Phrase/TFX: `PhraseNumber`, `PhraseOctaveShift`, `TfxSwitch`.
- No partial/rack VM (SN-A has none).

### 4e. SN-A views (new, `Src/Views/`)
- `SNAcousticToneEditorView` — Header `Border` on top, then a `TabControl`:
  - **Tone** tab → ScrollViewer → Instrument section (`DiscriminatedParamSectionView` bound to the
    Instrument section) + Character section (offset/vibrato sliders) + Phrase/TFX + an "Advanced
    acoustic parameters…" link.
  - **FX** tab → the `MfxPanelView` (`ContentControl Content="{Binding Mfx}"`).
  - All colours via `{StaticResource}` `Sn*` brushes (reused; acoustic uses the same neutral palette).
- Resolved by `ViewLocator` (name convention).

### 4f. Hosting (`PartViewModel` + `MainWindow.axaml`)
- `PartViewModel` builds `SNAcousticToneEditor = new SNAcousticToneEditorViewModel(_i7domain, PartNo,
  (tag,_) => ToneTabKey = tag)` alongside the existing `SNSynthToneEditor`, disposed/rebuilt the same
  way.
- `MainWindow.axaml`: add an **"Editor"** `TabItem` (`Tag="SN-A"`-family) hosting
  `SNAcousticToneEditorView`, visible when `SelectedPresetIsSNAcousticTone`, placed beside the existing
  raw "Tone"/"MFX" SN-A tabs (which stay as the advanced view). Reuse the `ToneTabKey` /
  `SelectTabByTag` navigation (and its repeat-click fix).

## 5. State, sending, mapping
Identical to SN-S: each control is a `ParamInt`/`ParamString`/`ParamBool` → per-key throttled write →
`DomainBase.WriteToIntegraAsync`; hardware echoes sync via FQP `PropertyChanged`. The instrument-detail
grid uses `DataTemplateProvider.ParameterValueTemplate`'s `ui2hw` path. The Instrument discriminator is
a discrete param (display strings from `INSTRUMENT_VARIATIONS`). No new write path.

## 6. Testing (`Tests/`, NUnit, pure)
- `InstrumentCatalog`: every `INSTRUMENT_VARIATIONS` index in exactly one family; `FamilyOf`/`ValuesIn`
  round-trip (mirrors `TestMfxCatalog`).
- `MfxCatalog` tests stay green after the `IDiscriminatorCatalog` rename.
- Shared `FriendlyNames`: existing MFX cases + an SN-A case where the param prefix ≠ the display name
  (`("INT 001: Concert Grand", ["ConcertGrand String Resonance", "ConcertGrand Lid"]) →
  ["String Resonance","Lid"]` via common-prefix).
- The section VM, panels, and hosting are verified by build + the hardware smoke test.

## 7. Acceptance criteria
- Selecting an SN-Acoustic preset shows a friendly **Editor** tab: Header + **Tone**/**FX** sub-tabs.
- Tone: a family→instrument picker (~100 instruments), bipolar Character offsets + Vibrato, and a
  **dynamic instrument-detail grid** that changes with the selected instrument (named, cleaned labels),
  plus an advanced link.
- FX: the generalized MFX panel works against the SN-A MFX domain.
- All controls edit live with correct mapping; hardware edits update them; switching instrument updates
  the detail grid; dragging a value doesn't rebuild the grid.
- The SN-S FX tab is unchanged by the shared-component refactor.
- No hardcoded colours; tests in §6 pass; full solution builds.

## 8. Suggested phasing (for the implementation plan)
1. **Shared foundation:** `IDiscriminatorCatalog` + move `FriendlyNames`; `DiscriminatedParamSection`
   VM+View; refactor `MfxPanelViewModel`/`View` onto it (SN-S FX = regression). + `InstrumentCatalog`
   (+ tests).
2. **SN-A editor:** header/character/vibrato/phrase wrappers + `SNAcousticToneEditorViewModel`/`View`
   (Tone/FX tabs) using the shared section + the MFX panel.
3. **Hosting + polish:** `PartViewModel` wiring + `MainWindow` Editor tab; advanced links; smoke.

## 9. Out of scope
PCM/drum engines (later roadmap items); per-instrument "hero" curation of modify params; phrase/arp
deep editing; tone-level copy/paste.
