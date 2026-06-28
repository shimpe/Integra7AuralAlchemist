# Group advanced parameter tabs under one "Advanced" tab — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan

## Goal

The per-part tab strip is crowded: for any preset it shows `Midi`, `Set Part`, `Set EQ`, the engine's
friendly `Editor`, and that engine's several `Advanced —…` raw-parameter tabs (4–5 plus a Partials
tab). Group all the **`Advanced —…`** tabs under a single top-level **`Advanced`** tab (as sub-tabs),
so the top strip becomes `Midi`, `Set Part`, `Set EQ`, `Editor`, `Advanced`. The friendly editors'
"Advanced —…" navigation must keep working.

**Decisions (from brainstorming):**
- Only the `Advanced —…` tabs move under `Advanced`. `Midi` / `Set Part` / `Set EQ` (studio-set raw
  tabs) and the `Editor` (friendly) tabs stay top-level.
- Sub-tabs drop the `"Advanced — "` prefix (the parent already says "Advanced") → `Common`, `MFX`,
  `PMT`, `Kit`, `Comp-EQ`, `Partials`, etc.

## Background — current structure

In `Src/Views/MainWindow.axaml`, each part is rendered by one templated per-part `TabControl`
(`behaviors:TabControlBehaviors.SelectTabByTag="{Binding ToneTabKey}"`). Its tabs (each
`Advanced —…`/`Editor` tab has a stable `Tag` and an `IsVisible` bound to the selected preset's engine):

- Always visible: `Midi`, `Set Part`, `Set EQ` (studio-set raw grids).
- Per engine: an `Editor` (friendly; `Tag` = `PCMS`/`PCMD`/`SN-S`/`SN-A`/`SN-D`) and its `Advanced —…`
  tabs (`Tag`s like `PCM-SYN-COMMON`, `PCM-SYN-MFX`, `SN-A-TONE`, …), including an `Advanced — Partials`
  tab whose content is a per-partial `TabControl` bound to `AdvancedPartialIndex`.

`ToneTabKey` + `SelectTabByTag` drive two things, both tag-based:
1. **Friendly "Advanced —…" buttons** call `navigateToRawTab(tag, partialIdx)` →
   `ToneTabKey = ""; ToneTabKey = tag;` (clear-then-set, so repeat navigations re-fire), selecting the
   tab whose `Tag` equals the value.
2. **Preset-type change** sets `ToneTabKey = _selectedPreset.ToneTypeStr` (the engine code = the
   `Editor` tab's `Tag`), defaulting to the friendly Editor and dropping the previous type's stale
   hidden-tab content (Avalonia #16879 workaround).

`SelectTabByTag` today (`Src/Behaviors/TabControlBehaviors.cs`) does a single-level scan of
`control.Items` and sets `SelectedItem` to the first TabItem whose `Tag` matches; no-op on empty/no
match.

## Approach

Wrap the `Advanced —…` tabs in one `Advanced` TabItem containing an inner `TabControl`, and make
`SelectTabByTag` recursive so a tag at any depth selects its tab plus all ancestor TabItems. The
friendly editors keep passing a single tag — **no view-model or navigation-contract changes**.

### 1. Tab structure (`MainWindow.axaml`)

The per-part outer `TabControl` keeps `Midi`, `Set Part`, `Set EQ`, the five `Editor` TabItems, and a
new **always-visible `Advanced` TabItem**. The `Advanced` TabItem's direct content is an inner
`TabControl` (same `TabStripPlacement`/stretch as the outer) containing **all** the former
`Advanced —…` TabItems, moved verbatim except:
- `Header` loses the `"Advanced — "` prefix (`"Advanced — Common"` → `"Common"`, etc.).
- `Tag` values and `IsVisible` bindings are unchanged.
- The four nested per-partial `TabControl`s (and `AdvancedPartialIndex`) move in unchanged as the
  content of their (now `"Partials"`-labelled) TabItems.

Only the current engine's sub-tabs are visible (existing `IsVisible` bindings), so `Advanced` shows
e.g. `Common / Common 2 / MFX / PMT / Partials` for a PCM Synth preset.

### 2. Navigation (`TabControlBehaviors.SelectTabByTag`)

Extend the attached behavior to two responsibilities, run when the bound value changes:

**a. Recursive select.** Search the bound TabControl's `Items` for a TabItem whose `Tag` matches; if
none, for each TabItem whose direct content is a `TabControl`, recurse into it. On a match, set that
inner TabControl's `SelectedItem` to the matched tab and, as the recursion unwinds, set each ancestor
TabControl's `SelectedItem` to the containing TabItem. Net effect:
- `ToneTabKey = "PCMS"` → selects the top-level `Editor` (found at depth 0).
- `ToneTabKey = "PCM-SYN-COMMON"` → selects inner `Common` **and** the outer `Advanced` tab.
- `ToneTabKey = "PCM-SYN-PARTIALS"` → selects inner `Partials` + `Advanced`; `AdvancedPartialIndex`
  (separate binding) still selects the partial.

Selecting the inner tab works even when `Advanced` isn't yet realized: the inner `TabControl` is the
`Advanced` TabItem's inline `Content`, so it exists and its `SelectedItem` can be set before the outer
selection makes it visible.

**b. Repair hidden inner selections.** After selecting, walk every TabControl in the subtree and, for
any whose `SelectedItem` is a TabItem with `IsVisible == false`, set `SelectedItem` to that control's
first visible TabItem (or null if none). This handles a preset-type change while an inner sub-tab is
selected: e.g. you were on PCM's `Common`, load an SN-A preset → `ToneTabKey = "SN-A"` selects the
top-level SN-A `Editor`, and the repair reselects the inner control's first visible SN-A sub-tab so
opening `Advanced` never shows the previous engine's stale content. Run the repair via
`Dispatcher.UIThread.Post(...)` so the per-engine `IsVisible` bindings have settled before it reads
them.

### 3. Unchanged

`ToneTabKey`, the editors' `navigateToRawTab` callbacks and their `Advanced…` commands,
`AdvancedPartialIndex`, the per-partial `TabControl`s, and the top-level studio-set/`Editor` tabs. The
behavior absorbs the new two-level selection, so no binding contract changes.

## Edge cases

- **Inner tab realized lazily:** setting an inner `SelectedItem` before the `Advanced` parent is
  selected is fine (inline content exists); the recursion selects inner-first, then the ancestor.
- **Repair when `Advanced` is the current tab:** if the user is on `Advanced → Common` and switches to
  a same-engine preset, `ToneTabKey` is unchanged (same `ToneTypeStr`), so nothing re-selects — their
  sub-tab is kept (matches today's "same-type leaves the tab alone" behavior). On a different-engine
  preset, the repair reselects a visible sub-tab.
- **Direct content assumption:** the recursion treats a TabItem's content as a nested `TabControl`
  only when `TabItem.Content is TabControl`. The `Advanced` TabItem's content is exactly that; most
  TabItems (parameter grids, friendly editors) are not, so they are not recursed into. A `Partials`
  TabItem's content *is* a (per-partial) `TabControl`, so when searching for some *other* tag the
  recursion may descend into it — but those tabs come from an `ItemsSource` of `PartialViewModel`s, not
  `TabItem`s with `Tag`s, so the inner loop's `item is TabItem` check skips them and it returns no
  match. Harmless. (A Partials target like `PCM-SYN-PARTIALS` matches the `Partials` TabItem's own
  `Tag` at the `Advanced` level, so no descent happens for it.)

## Testing

This is XAML plus an Avalonia attached behavior; the project has no headless-UI test harness and the
existing `TabControlBehaviors` is not unit-tested. Verification is build + manual:
- For each engine, every friendly "Advanced —…" button selects the matching sub-tab under `Advanced`
  (and the correct partial for Partials).
- Switching preset type defaults to the friendly `Editor`; opening `Advanced` afterwards shows the new
  engine's sub-tabs with a valid (non-stale) selection.
- The top strip shows only `Midi / Set Part / Set EQ / Editor / Advanced`.
- MFX/Common/etc. sub-tab content and search boxes behave as before.

The recursive find/repair logic is kept small and self-contained in `TabControlBehaviors.cs`.

## Files

- **Modify:** `Src/Views/MainWindow.axaml` (wrap the `Advanced —…` TabItems in an `Advanced` TabItem +
  inner `TabControl`; drop the header prefix), `Src/Behaviors/TabControlBehaviors.cs` (recursive select
  + hidden-selection repair).

## Out of scope

No changes to the friendly editors, the parameter grids, `ParameterCollection`, the view-models, or the
navigation contract. The top-level `SRX Loader` / `Motional Surround` tabs and the studio-set/`Editor`
tabs are untouched. No new parameters or behaviors beyond the recursive selection.
