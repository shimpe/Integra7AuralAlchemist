# Friendly MFX panel — nested-discriminator fix (design)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI)
**Builds on:** Phase 4 (MFX panel), merged to `main`. Branch: `fix-mfx-nested-discriminators`.

## 1. Problem

The friendly **FX** panel's per-type parameter grid selects parameters with a single-level filter
(`MfxPanelViewModel.RecomputeTypeParameters`):
```csharp
.Where(p => p.ParSpec.Path.Contains("/MFX Parameter ") && p.ParSpec.ParentCtrlDispValue == typeName)
```
This matches only **direct MFX-Type children**. Several effects (the delays in particular) use a
**two-level discriminator chain**: `MFX Type → (Multi Tap Delay) → ms/Note switch param → (Note) →
the note-valued time`. The crashing parameter from the field report,
`…/MFX Parameter 3/Multi Tap Delay1 Time note`, is gated by its parent
`…/MFX Parameter 1/Multi Tap Delay1 Time (ms-note)` (`parval = "Note"`), **not** by the MFX Type
(verified: no MFX param uses a same-line second discriminator — `parval2` count = 0; chains are
always via separate `par`-referenced params).

Consequences of the single-level filter:
- The grid shows the **ms/Note switch** but **omits the actual delay-time value** (its
  `ParentCtrlDispValue` is `"msec"`/`"Note"`, never `"Multi Tap Delay"`), so the friendly panel is
  **incomplete** for delays and similar effects.
- It doesn't react when a sub-switch flips.
- (It did not directly cause the crash — the grid excludes that exact param — but it's the same
  structural gap. The crash itself was the harsh `Debug.Assert` on a stale write, already fixed
  separately on `main`.)

## 2. Decision (locked during brainstorming)

**Self-contained `ValidInContext` sourcing.** Build the grid from `GetRelevantParameters(false,
false)` (which follows the full discriminator chain), recomputing only on **discriminator** changes,
marshalled to the UI thread. Chosen over reusing the raw tab's collection (coupling) and over an
override-context approach (complexity). Accepts a ~250 ms grid-update lag after switching effects,
matching the raw "Advanced — MFX" tab.

## 3. Changes — all in `Src/ViewModels/MfxPanelViewModel.cs`

### 3a. Source the grid from the valid set
`RecomputeTypeParameters` (make it parameterless; read the type name from `Type.Value`):
```csharp
private void RecomputeTypeParameters()
{
    var typeName = Type.Value;
    var relevant = _mfxDomain.GetRelevantParameters(false, false)
        .Where(p => p.ParSpec.Path.Contains("/MFX Parameter "))
        .OrderBy(p => p.ParSpec.AddressInt)
        .ToList();
    var labels = MfxCatalog.FriendlyParamNames(typeName, relevant.Select(p => p.ParSpec.Name).ToList());
    TypeParameters.Clear();
    for (var i = 0; i < relevant.Count; i++)
        TypeParameters.Add(new MfxParamDisplay(relevant[i], labels[i]));
    this.RaisePropertyChanged(nameof(HasTypeParameters));
}
```
`GetRelevantParameters(false, false)` already excludes reserved + invalid-in-context, so the explicit
`!Reserved` and `ParentCtrlDispValue` checks are dropped. It is safe to call: the constructor already
calls `GetRelevantParameters(true, true)` on this same domain, so `ValidInContext` does not assert
here.

### 3b. Recompute on discriminator changes only (not value edits)
In the constructor, after `_allMfxParams` is captured, compute the set of parameter paths that are
referenced as a parent by *any* MFX param, and subscribe to those FQPs' `StringValue` changes:
```csharp
var parentPaths = _allMfxParams
    .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
    .Where(s => !string.IsNullOrEmpty(s))
    .ToHashSet();
_discriminators = _allMfxParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;
```
Handler (marshalled — these can fire on a MIDI/throttle-timer thread, the same path implicated in the
crash):
```csharp
private void OnDiscriminatorChanged(object? s, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
    Dispatcher.UIThread.Post(RecomputeTypeParameters);
}
```
Only discriminator params (MFX Type + the sub-switches) trigger a rebuild; dragging a value slider —
not a discriminator — never tears down the grid mid-drag.

### 3c. Decouple combo-sync from grid-recompute
`OnTypeChanged` (driven by `Type.WhenAnyValue`) keeps the **picker/Bypass sync** (instant) but **no
longer calls `RecomputeTypeParameters`** (the grid now updates via 3b when the underlying FQP
settles). Add one initial `RecomputeTypeParameters()` in the constructor (after wiring) so the grid
is populated at startup.

### 3d. Dispose
Unsubscribe the discriminator handlers:
```csharp
foreach (var d in _discriminators) d.PropertyChanged -= OnDiscriminatorChanged;
```
(plus the existing `_typeSub`, command, and wrapper disposals).

Add `using Avalonia.Threading;` and `using System.ComponentModel;` (for `PropertyChangedEventArgs`).

## 4. Behaviour after the fix
- Multi Tap Delay (and the other delays) show the ms/Note switch **and** the active time value; fl
  ipping the switch swaps the ms ↔ note value control (~instant; it's a sub-switch change → recompute).
- Switching MFX Type updates the grid within ~250 ms (after the type's throttled write settles); the
  family/type combos still update instantly.
- Dragging a per-type value slider does not rebuild the grid.
- Hardware-side type/switch changes update the grid (FQP echo → discriminator handler → recompute).
- No behaviour change for simple single-level effects (Equalizer, etc.).

## 5. Testing
The "which params are valid" logic is domain-level (`ValidInContext`), not unit-testable without live
FQPs, so this fix adds no new unit test. Verification:
- Full solution builds; existing tests still pass.
- Hardware smoke (§6).

## 6. Hardware smoke test (user-run)
1. FX tab → pick **Multi Tap Delay**. Confirm the grid now shows a time **value** control (not just
   the ms/Note switch).
2. Flip the **ms/Note** switch → the time control swaps between a millisecond value and a note value.
3. Drag a per-type **value** slider continuously → the grid does **not** reset/jump mid-drag.
4. Switch to another effect (e.g. Chorus) and back → grid repopulates correctly (small delay is fine).
5. Change the MFX type on the **hardware** → the friendly grid + pickers follow.
6. Edit a per-type value, then open the raw "Advanced — MFX" tab → same value shown (shared FQP).
7. Rapidly switch effects / flip switches → no crash (the earlier assert is gone; stale writes log).

## 7. Out of scope
Per-effect "hero" curation; refining `FriendlyParamName` for the `Multi Tap Delay1` style prefixes
(labels remain functional); MFX Control routing (stays in the Advanced tab).
