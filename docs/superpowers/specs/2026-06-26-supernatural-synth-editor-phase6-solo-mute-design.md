# SuperNATURAL Synth (SN-S) Editor — Design (Phase 6: Partial Solo/Mute)

**Date:** 2026-06-26
**Status:** Approved (pending spec review)
**Component:** Integra-7 Aural Alchemist (Avalonia 12 / .NET 10 / ReactiveUI MVVM)
**Builds on:** Phases 1–5 (merged to `main`). New branch: `sns-solo-mute`.

## 1. Goal

Add a **safe Solo/Mute audition** layer over the three SN-S partials so you can isolate or silence
partials while editing **without permanently changing the patch's saved partial on/off states**. The
hardware has only one switch per partial (`Partial{n} Switch`, the saved on/off shown on each rack
card); the audition layer overrides those switches for listening and **restores the snapshot** when
cleared.

Reuse the existing `IsOn` `ParamBool` wrapper (throttled write + hardware echo, idempotent setter),
color resources, and the rack-card template. See memory `parameter-domain-infrastructure`,
`visual-editor-pattern`, `no-hardcoded-colors-in-xaml`.

## 2. Decisions locked during brainstorming

1. **Model: audition overlay** (Solo + Mute), not a separate mixer strip and not solo-only.
2. **Safe via snapshot/restore:** entering audition snapshots all three real switch states; clearing
   audition writes them back. Listening never alters the saved patch.
3. **Effective rule:** `anySolo ? solo[i] : (saved[i] && !mute[i])` — solo isolates and can audition
   a saved-off partial; **solo overrides mute**.
4. **On/off toggle during audition:** shows what's *currently sounding* (it visibly flips) and is
   **disabled**; the S/M buttons + banner are the audition controls. On Restore the toggles return to
   the saved state. (Chosen over a frozen "intent" toggle to avoid an intent/effective duplication.)

## 3. Parameters / infrastructure used (verified)
- `IsOn` on `SNSPartialViewModel` = `ParamBool` over `SuperNATURAL Synth Tone Common/Partial{n} Switch`
  (already exists; bound to each card's on/off `ToggleSwitch`). The audition coordinator sets
  `IsOn.Value`; the `ParamBool` setter is idempotent (no write when unchanged) and echoes hardware.
- No new SN-S parameters. Solo/Mute are transient VM state only.
- Audition state lives on `SNSynthToneEditorViewModel` (it spans all three partials) and resets
  naturally on preset change (the editor VM is rebuilt in `PartViewModel`).

## 4. New / changed components

### 4a. `PartialAudition` (new, `Src/Models/Services/`) — pure, unit-tested
No Avalonia/domain dependency:
```csharp
public static class PartialAudition
{
    // Effective on/off per partial. Solo isolates (overrides mute and saved-off); with no solo,
    // mutes silence saved-on partials. All three input lists are the same length.
    public static bool[] Effective(IReadOnlyList<bool> saved, IReadOnlyList<bool> solo, IReadOnlyList<bool> mute);

    // True when any solo or any mute is engaged (i.e. an audition is active).
    public static bool IsAuditioning(IReadOnlyList<bool> solo, IReadOnlyList<bool> mute);
}
```
`Effective` computes `anySolo = solo.Any(true); result[i] = anySolo ? solo[i] : (saved[i] && !mute[i])`.

### 4b. `SNSPartialViewModel` changes
- Add transient `bool Solo` and `bool Mute` properties (manual `RaiseAndSetIfChanged`); each setter
  calls `_parent.RecomputeAudition()` after raising (the parent owns cross-partial coordination).
- Add a parent-silent setter used by bulk clear: `internal void SetAuditionFlags(bool solo, bool mute)`
  that sets the backing fields + raises `Solo`/`Mute` change notifications **without** calling
  `RecomputeAudition` (so the parent can clear all three then recompute once).
- No change to `IsOn` (still the real switch; still bound to the card on/off toggle).

### 4c. `SNSynthToneEditorViewModel` changes (the coordinator)
- Fields: `private readonly bool[] _saved = new bool[Constants.NO_OF_PARTIALS_SN_SYNTH_TONE];`,
  `private bool _auditing;`, `private bool _suppressRecompute;`.
- `[Reactive] public bool IsAuditioning { get; private set; }` (or manual property with
  `RaiseAndSetIfChanged`).
- `public void RecomputeAudition()`:
  - Return early if `_suppressRecompute`.
  - Build `solo`/`mute`/current `saved` lists from `Partials[i].Solo/Mute/IsOn.Value`.
  - `active = PartialAudition.IsAuditioning(solo, mute)`.
  - On `false→true` transition (`active && !_auditing`): snapshot `_saved[i] = Partials[i].IsOn.Value`;
    set `_auditing = true`.
  - If `active`: `eff = PartialAudition.Effective(_saved, solo, mute)`; set each
    `Partials[i].IsOn.Value = eff[i]` (idempotent).
  - Else if `_auditing` (true→false): restore each `Partials[i].IsOn.Value = _saved[i]`;
    set `_auditing = false`.
  - Set `IsAuditioning = active`.
- `[ReactiveCommand] public void ClearAudition()`: set `_suppressRecompute = true`; for each partial
  `SetAuditionFlags(false, false)`; set `_suppressRecompute = false`; then `RecomputeAudition()`
  (single restore).
- Dispose: nothing extra (no new subscriptions; the `bool[]` is plain state).

### 4d. View changes (`Src/Views/SNSynthToneEditorView.axaml`)
- **Rack card** (`DataTemplate` for `SNSPartialViewModel`): next to the on/off `ToggleSwitch`, add two
  compact `ToggleButton`s **S** and **M** bound to `Solo`/`Mute` (`IsChecked`, TwoWay), with tooltips
  "Solo (audition)" / "Mute (audition)". Active states use new brushes (4e). Set the on/off
  `ToggleSwitch` `IsEnabled="{Binding !#Root.((vm:SNSynthToneEditorViewModel)DataContext).IsAuditioning}"`
  so it's disabled during audition.
- **Audition banner**: restructure the left column (currently just the rack `ListBox` at
  `Grid.Column="0"`) into a `Grid RowDefinitions="Auto,*"`: row 0 = a `Border`
  (`IsVisible="{Binding IsAuditioning}"`, `SnAuditionBannerBrush`) containing the text
  "Auditioning (solo/mute) — restore before saving" + a **Restore** `Button`
  (`Command="{Binding ClearAuditionCommand}"`); row 1 = the existing `ListBox`.
- All colors via `{StaticResource}`.

### 4e. Color resources (`App.axaml`)
Add `SnSoloBrush` (e.g. green `#4FB06A`), `SnMuteBrush` (e.g. red `#C25A4F`), and
`SnAuditionBannerBrush` (e.g. amber `#5A4A1E`). Used for the active S/M toggle backgrounds and the
banner. No new accent is required elsewhere.

## 5. State, sending, mapping
Solo/Mute are pure VM state; the coordinator writes only the existing `IsOn` (`Partial{n} Switch`) via
its throttled wrapper, exactly as the card toggle already does. Idempotent setters mean only real
changes emit SysEx. The snapshot/restore guarantees the saved on/off returns after audition. Hardware
echoes during audition reflect the coordinator's own writes; not-auditioning behavior is unchanged
(the card toggle remains a live two-way bind to the real switch).

## 6. Edge cases
- **Solo a saved-off partial:** it sounds (solo forces on) — intended for auditioning.
- **Solo + mute same partial:** solo wins (it sounds).
- **Preset change mid-audition:** editor VM rebuilt → audition state gone; the new preset sets its own
  switches, so the un-restored audition state is discarded harmlessly.
- **Save mid-audition:** would save the audition switch state; mitigated by the banner wording
  ("restore before saving") and the one-click Restore.
- **Clear with nothing active:** no-op (`_auditing` false).

## 7. Accessibility
S/M are labelled `ToggleButton`s (text "S"/"M" + tooltips), keyboard-focusable; the banner text and
Restore button are standard controls. Color is never the only signal (text labels + the on/off toggle
state convey what's audible).

## 8. Testing (`Tests/`, NUnit, pure)
`TestPartialAudition`:
- No solo, no mute → `Effective == saved`.
- Mute one (no solo) → that one off, others = saved; muting a saved-off partial stays off.
- Solo one → only it on (others off) regardless of their saved/mute; soloing a saved-off partial → on.
- Solo overrides mute (solo+mute same partial → on).
- Multiple solos → all soloed on, rest off.
- `IsAuditioning`: false when all clear; true if any solo or any mute.
The coordinator wiring + view are verified by build and the hardware smoke test.

## 9. Acceptance criteria (Phase 6)
- Each rack card shows **S** and **M** buttons beside the on/off toggle.
- Engaging Solo/Mute auditions per the effective rule; a banner with **Restore** appears; the on/off
  toggles disable and reflect what's sounding.
- **Restore** (and clearing all S/M) returns every partial to its pre-audition saved on/off.
- The saved patch on/off is unchanged by an audition that is restored.
- No hardcoded colors; `PartialAudition` tests pass; full solution builds; existing 172 tests pass.

## 10. Hardware smoke test (user-run)
1. SN-S part → Editor. Note the three cards' on/off states.
2. Press **S** on Partial 2 → only Partial 2 sounds; cards 1 & 3 show off + disabled; banner appears.
3. Press **S** on Partial 1 too → Partials 1 & 2 sound (multi-solo).
4. **Restore** → all three return to their original on/off; banner clears; toggles re-enable.
5. With no solo, press **M** on Partial 1 → Partial 1 silenced, others unchanged; Restore returns it.
6. Solo a partial that was off → it sounds; Restore → it's off again.
7. Confirm that after Restore the saved on/off states match step 1 exactly (e.g. re-open the tone or
   check the raw Common tab's Partial Switches).

## 11. Out of scope (later)
Starter patches/macros; tone-level copy/paste & A/B; value readouts; Portamento Mode; other engines.
