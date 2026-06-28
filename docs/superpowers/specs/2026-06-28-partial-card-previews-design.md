# Envelope/filter previews on PCMS (and SN-S) partial cards — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan

## Goal

The friendly PCM-Synth (PCMS) editor's partial cards are text-only. Add small, read-only
**envelope/filter preview thumbnails** to each PCMS partial card — pitch envelope, filter shape, filter
(TVF) envelope, and amp (TVA) envelope — and make them **look and feel consistent with the
SuperNATURAL-Synth (SN-S) partial cards**, which already show filter-shape + amp/filter-envelope
previews. While here, **add a pitch-envelope preview to the SN-S cards too** (it's currently missing).
Every envelope **type has its own colour**, consistent across both editors.

## Background — what exists

- The reusable graph controls already support a **`Preview="True"`** mode (no drag handles,
  non-interactive): `FilterCurveControl`, `MultiStageEnvelopeControl`, `PitchEnvelopeControl`, and
  `DualAdsrEnvelopeControl`. All take `…LineBrush`/`…FillBrush` + `Background/Grid/Axis` brush
  properties.
- **SN-S card** (`Src/Views/SNSynthToneEditorView.axaml`, ListBox item template) already shows, under
  the text rows: a `FilterCurveControl` (`Height=40, Preview`) + caption, then a
  `DualAdsrEnvelopeControl` (`Height=44, Preview`) overlaying amp+filter envelopes + caption.
- **PCMS card** (`Src/Views/PCMSynthToneEditorView.axaml`, ListBox item template) shows only text
  (title, solo/mute/on, wave summary, Lvl/Pan, a "Filter:" summary).
- The PCMS detail "Sound" tab already binds the partial's envelope/filter params to interactive
  controls, so the bindings exist on `PCMPartialViewModel`:
  - Pitch (bipolar multi-stage): `PitchEnvTime1..4`, `PitchEnvLevel0..4`.
  - Filter curve: `FilterCurveMode`, `FilterCurveSteep`, `TvfCutoff`, `TvfResonance`.
  - Filter env (multi-stage): `TvfEnvTime1..4`, `TvfEnvLevel0..4`.
  - Amp env (multi-stage, fixed endpoints): `TvaEnvTime1..4`, `TvaEnvLevel1..3` (Level0/Level4 = 0).
- SN-S partial VM (`SNSPartialViewModel`) exposes `PitchEnvAttack`, `PitchEnvDecay`, `PitchEnvDepth`
  (used by the detail-tab `PitchEnvelopeControl`).
- Shared colour resources (App.axaml) already define a distinct brush per envelope type:
  `SnPitchEnvelopeBrush` (purple), `SnFilterEnvelopeBrush` (amber), `SnAmpEnvelopeBrush` (blue), plus
  matching `…FillBrush`es and `SnEnvelope{Background,Grid,Axis}Brush`.

## Colour scheme (per envelope type, both editors)

| Graph | Brush | Colour |
|-------|-------|--------|
| Pitch envelope | `SnPitchEnvelopeBrush` / `…FillBrush` | purple |
| Filter (TVF) envelope | `SnFilterEnvelopeBrush` / `…FillBrush` | amber |
| Amp (TVA) envelope | `SnAmpEnvelopeBrush` / `…FillBrush` | blue |
| Filter **shape** curve | `SnFilterEnvelopeBrush` / `…FillBrush` | amber (filter family; not an envelope) |

So the three envelope types are always purple / amber / blue, identically on PCMS and SN-S.

## Card layout (identical on both editors)

Below the existing text rows, each partial card shows three small preview graphs in this order, each
`Preview="True" IsHitTestVisible="False" Opacity="0.85"`, each with a centered caption (FontSize 10,
Opacity 0.45):

1. **Filter shape** — `FilterCurveControl` (`Height=40`), amber. Caption "filter shape (preview)".
   *(SN-S already has this; add to PCMS.)*
2. **Pitch envelope** — `Height=40`, purple. Caption "pitch env (preview)". *(New on both cards.)*
   - PCMS → `MultiStageEnvelopeControl` (`Bipolar=True`), bound to `PitchEnvTime1..4`/`PitchEnvLevel0..4`.
   - SN-S → `PitchEnvelopeControl`, bound to `PitchEnvAttack`/`PitchEnvDecay`/`PitchEnvDepth`.
3. **Amp + Filter envelope overlay** — `Height=44`, amp blue + filter amber on shared axes. Caption
   "amp (blue) + filter (amber) env (preview)". *(SN-S already has this; add to PCMS.)*
   - SN-S → `DualAdsrEnvelopeControl` (existing).
   - PCMS → the new `DualMultiStageEnvelopeControl` (below).

This covers all four (pitch, filter shape, filter env, amp env) on both cards, with amp+filter sharing
one overlay graph exactly like SN-S.

## New control: `DualMultiStageEnvelopeControl`

PCMS envelopes are 5-point multi-stage (not ADSR), so the amp+filter overlay needs a control parallel
to `DualAdsrEnvelopeControl`. `Src/Controls/DualMultiStageEnvelopeControl.cs` (a templated `Control`,
same pattern as the others):

- **Int StyledProperties:** `AmpTime1..4`, `AmpLevel0..4`, `FilterTime1..4`, `FilterLevel0..4`.
- **Brush StyledProperties:** `AmpLineBrush`/`AmpFillBrush`, `FilterLineBrush`/`FilterFillBrush`,
  `BackgroundBrush`/`GridBrush`/`AxisBrush`; **bool** `Preview` (cards always set it true).
- **Render:** fill background + grid/axes once; compute both envelopes via the existing unit-tested
  `PcmEnvelopeMapping.ComputePoints(...)` (the same mapping `MultiStageEnvelopeControl` uses); draw the
  filter envelope (fill + line, amber) then the amp envelope (fill + line, blue) on the shared axes;
  no handles, no pointer interaction (it's a preview-only control). `AffectsRender` on all properties.

The card binds amp `Level0`/`Level4` to `0` (matching the detail tab's fixed-endpoint amp envelope).
This mirrors `DualAdsrEnvelopeControl`'s structure (two envelopes, separate line/fill brushes, shared
axes) so the two editors' overlays look the same.

## Data flow / unchanged

- No view-model changes — every binding already exists on `PCMPartialViewModel` / `SNSPartialViewModel`.
- Previews bind to the same `…​.Value` params the detail tab edits, so they **update live** as the user
  edits in the Sound tab (cards and detail share the partial VM instances).
- Cards grow taller; the partial ListBox already scrolls.

## Files

- **Create:** `Src/Controls/DualMultiStageEnvelopeControl.cs`.
- **Modify:** `Src/Views/PCMSynthToneEditorView.axaml` (add the 3 preview rows + captions to the card
  template), `Src/Views/SNSynthToneEditorView.axaml` (insert the pitch-env preview row + caption between
  the existing filter-shape and amp/filter rows).

## Testing

XAML + one new rendering control; the project has no headless-UI test harness, so verification is
build + manual. The new control's geometry comes entirely from the existing, unit-tested
`PcmEnvelopeMapping.ComputePoints`, so there is no new pure logic to unit-test (the control is thin
rendering). Manual checks: both editors' cards show pitch/filter/amp previews in the same order, sizes,
and colours (purple/amber/blue); previews update live while editing the Sound tab; the filter-shape and
amp+filter overlays match between PCMS and SN-S.

## Out of scope

- The friendly PCM/SN **Drum** editors, SN-Acoustic, and the advanced grids (this is the two synth-tone
  partial-card editors only). No change to the interactive detail-tab controls or to the envelope
  colour resources themselves.
