# Enabling a partial selects it (PCM-Synth + SN-S) — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan

## Goal

In the friendly PCM-Synth (PCMS) and SuperNATURAL-Synth (SN-S) editors, toggling a partial's **on/off
switch** on its card should make that partial the **selected partial** — mirroring the existing
"soloing a partial selects it" behavior. Selection happens on *any* user toggle (enabling or
disabling), consistent with the Solo button.

## Background

- Each partial card has Solo / Mute / on-off controls. The `Solo` setter on the partial VMs already
  does `_parent.SelectedPartial = this;` (PCM `PCMPartialViewModel`, SN-S `SNSPartialViewModel`).
- The on/off switch binds two-way to `IsOn.Value` (a `ParamBool`): `PCMSynthToneEditorView.axaml:171`
  and `SNSynthToneEditorView.axaml:161`. It is the only place `IsOn.Value` is bound. Both switches are
  already `IsEnabled="{Binding !…IsAuditioning}"`, so the user cannot toggle them during solo/mute
  audition.
- The audition machinery (`RecomputeAudition`) and preset loads change each partial's `IsOn.Value`
  **programmatically**. A naive "select when IsOn changes" hook would make the selection jump as the
  machinery toggles partials, or when a preset loads — which is wrong.

## Design

Add a small `Enabled` view-model property to `PCMPartialViewModel` and `SNSPartialViewModel` that wraps
`IsOn.Value` and selects the partial on set (the user path only):

```csharp
public bool Enabled
{
    get => IsOn.Value;
    set { IsOn.Value = value; _parent.SelectedPartial = this; }   // mirrors the Solo setter
}
```

- **Bind the card's on/off `ToggleSwitch` to `Enabled`** instead of `IsOn.Value` (the two lines above).
- **Forward programmatic changes:** subscribe to `IsOn.PropertyChanged` in the ctor and, when its
  `Value` changes, `this.RaisePropertyChanged(nameof(Enabled))`; unsubscribe in `Dispose`. This keeps
  the switch visually in sync when the audition or a preset load changes `IsOn.Value`.

### Why it's audition/load-safe

`Enabled.set` is invoked **only** by the user toggling the switch (a target→source binding update). All
programmatic `IsOn.Value` changes (audition recompute, `RestoreAuditionAsync`, preset load/resync) go
through `IsOn.Value` directly and never call `Enabled.set`, so they never move the selection. The
switch is also disabled during audition, so user toggles can't even occur then. `SelectedPartial`'s
setter already ignores null / same-reference, so re-selecting the current partial is a no-op.

## Files

- **Modify:** `Src/ViewModels/PCMPartialViewModel.cs`, `Src/ViewModels/SNSPartialViewModel.cs` (add the
  `Enabled` property + the `IsOn.PropertyChanged` forward + unsubscribe in `Dispose`).
- **Modify:** `Src/Views/PCMSynthToneEditorView.axaml`, `Src/Views/SNSynthToneEditorView.axaml` (bind the
  on/off `ToggleSwitch` `IsChecked` to `Enabled`).

## Testing

No headless-UI test harness exists, and this mirrors the unit-untested `Solo` setter; verification is
build + manual:
- Toggling a partial's on/off switch (on→off or off→on) selects that partial in both editors.
- Engaging solo/mute audition (which toggles partials' on/off programmatically) does **not** change the
  selection; the on/off switches still reflect the audition state.
- Loading a preset does not change which partial is selected based on partials being on/off.

## Out of scope

The PCM/SN drum editors and SN-Acoustic (no partial rack). Mute-selects is unchanged (only Solo and now
on/off select). No change to the audition logic or `ParamBool`.
