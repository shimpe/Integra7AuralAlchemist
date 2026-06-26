# SuperNATURAL Synth (SN-S) Editor — Phase 6 (Partial Solo/Mute) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a safe Solo/Mute audition overlay over the three SN-S partials: transient solo/mute that override the real Partial Switches for listening and restore a snapshot when cleared.

**Architecture:** A pure `PartialAudition` helper (effective on/off + is-auditioning, unit-tested) drives a coordinator on `SNSynthToneEditorViewModel` that snapshots the real switches on entry and restores on exit, writing the existing per-partial `IsOn` wrapper. Each `SNSPartialViewModel` gains transient `Solo`/`Mute` flags; the rack cards get S/M buttons and an audition banner.

**Tech Stack:** Avalonia 12 (compiled bindings, control `Styles`/`:checked` selectors), ReactiveUI (`[ReactiveCommand]`, `RaiseAndSetIfChanged`), .NET 10, NUnit 3. Build/test with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-solo-mute` (created; spec committed).

Reference: spec `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase6-solo-mute-design.md`. Memory: `parameter-domain-infrastructure`, `visual-editor-pattern`, `no-hardcoded-colors-in-xaml`.

---

## File Structure

- Create `Src/Models/Services/PartialAudition.cs` — pure effective-switch logic (+ tests).
- Create `Tests/TestPartialAudition.cs`.
- Modify `Src/ViewModels/SNSPartialViewModel.cs` — transient `Solo`/`Mute` + `SetAuditionFlags`.
- Modify `Src/ViewModels/SNSynthToneEditorViewModel.cs` — the audition coordinator (`+ using System.Linq;`).
- Modify `Src/App.axaml` — `SnSoloBrush`, `SnMuteBrush`, `SnAuditionBannerBrush`.
- Modify `Src/Views/SNSynthToneEditorView.axaml` — S/M buttons, disable on/off during audition, banner.

---

## Task 1: `PartialAudition` (pure) + tests

**Files:**
- Create: `Src/Models/Services/PartialAudition.cs`
- Test: `Tests/TestPartialAudition.cs`

- [ ] **Step 1: Write the failing tests.** Create `Tests/TestPartialAudition.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPartialAudition
{
    private static readonly bool[] AllOn = { true, true, true };

    [Test]
    public void No_solo_no_mute_is_identity()
    {
        var saved = new[] { true, false, true };
        Assert.That(PartialAudition.Effective(saved, new[] { false, false, false }, new[] { false, false, false }),
            Is.EqualTo(saved));
    }

    [Test]
    public void Mute_silences_only_that_partial()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { false, false, false }, new[] { false, true, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void Muting_a_saved_off_partial_stays_off()
    {
        var eff = PartialAudition.Effective(new[] { true, false, true }, new[] { false, false, false }, new[] { false, true, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void Solo_isolates_regardless_of_saved_or_mute()
    {
        var eff = PartialAudition.Effective(new[] { true, true, true }, new[] { true, false, false }, new[] { false, false, true });
        Assert.That(eff, Is.EqualTo(new[] { true, false, false }));
    }

    [Test]
    public void Solo_a_saved_off_partial_sounds()
    {
        var eff = PartialAudition.Effective(new[] { false, false, false }, new[] { false, false, true }, new[] { false, false, false });
        Assert.That(eff, Is.EqualTo(new[] { false, false, true }));
    }

    [Test]
    public void Solo_overrides_mute_on_same_partial()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { true, false, false }, new[] { true, false, false });
        Assert.That(eff[0], Is.True);
    }

    [Test]
    public void Multiple_solos_sound_together()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { true, false, true }, new[] { false, false, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void IsAuditioning_reflects_any_solo_or_mute()
    {
        Assert.That(PartialAudition.IsAuditioning(new[] { false, false, false }, new[] { false, false, false }), Is.False);
        Assert.That(PartialAudition.IsAuditioning(new[] { false, true, false }, new[] { false, false, false }), Is.True);
        Assert.That(PartialAudition.IsAuditioning(new[] { false, false, false }, new[] { true, false, false }), Is.True);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: FAIL — `PartialAudition` does not exist (compile error).

- [ ] **Step 3: Implement `PartialAudition`.** Create `Src/Models/Services/PartialAudition.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure logic for the SN-S partial Solo/Mute audition overlay. Computes the effective on/off of each
/// partial from the saved (real) switch states plus the transient solo/mute sets. Solo isolates: if
/// any partial is soloed, only soloed partials sound (overriding mute and saved-off); with no solo,
/// mutes silence saved-on partials. No UI or hardware dependency.
/// </summary>
public static class PartialAudition
{
    /// <summary>Effective on/off per partial. All three lists must be the same length as <paramref name="saved"/>.</summary>
    public static bool[] Effective(IReadOnlyList<bool> saved, IReadOnlyList<bool> solo, IReadOnlyList<bool> mute)
    {
        var anySolo = solo.Any(s => s);
        var result = new bool[saved.Count];
        for (var i = 0; i < saved.Count; i++)
            result[i] = anySolo ? solo[i] : (saved[i] && !mute[i]);
        return result;
    }

    /// <summary>True when any solo or any mute is engaged (an audition is active).</summary>
    public static bool IsAuditioning(IReadOnlyList<bool> solo, IReadOnlyList<bool> mute)
        => solo.Any(s => s) || mute.Any(m => m);
}
```

- [ ] **Step 4: Run the tests to verify they pass.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: PASS — all `TestPartialAudition` tests green, existing 172 still green.

- [ ] **Step 5: Commit.**

```bash
git add Src/Models/Services/PartialAudition.cs Tests/TestPartialAudition.cs
git commit -m "feat(sns): pure PartialAudition effective-switch logic (+ tests)"
```

---

## Task 2: VM wiring — partial Solo/Mute + editor coordinator

**Files:**
- Modify: `Src/ViewModels/SNSPartialViewModel.cs`
- Modify: `Src/ViewModels/SNSynthToneEditorViewModel.cs`

Both files change together (the `Solo`/`Mute` setters call `_parent.RecomputeAudition()`, which this task adds), so they compile as one unit.

- [ ] **Step 1: Add `Solo`/`Mute` + `SetAuditionFlags` to `SNSPartialViewModel`.** After the `public ParamBool IsOn { get; }` property declaration, insert:

```csharp

    // --- Audition (transient solo/mute; coordinated by the parent editor VM, not sent as params) ---
    private bool _solo;
    public bool Solo
    {
        get => _solo;
        set { if (_solo == value) return; this.RaiseAndSetIfChanged(ref _solo, value); _parent.RecomputeAudition(); }
    }

    private bool _mute;
    public bool Mute
    {
        get => _mute;
        set { if (_mute == value) return; this.RaiseAndSetIfChanged(ref _mute, value); _parent.RecomputeAudition(); }
    }

    /// <summary>Set solo/mute without triggering a parent recompute (used for bulk clear).</summary>
    internal void SetAuditionFlags(bool solo, bool mute)
    {
        this.RaiseAndSetIfChanged(ref _solo, solo, nameof(Solo));
        this.RaiseAndSetIfChanged(ref _mute, mute, nameof(Mute));
    }
```

(`using ReactiveUI;` is already present.)

- [ ] **Step 2: Add `using System.Linq;` to `SNSynthToneEditorViewModel.cs`.** Change the using block so it includes `System.Linq` — insert after `using System.Collections.ObjectModel;`:

```csharp
using System.Linq;
```

- [ ] **Step 3: Add the coordinator members to `SNSynthToneEditorViewModel`.** Immediately after the line `[ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("SN-S-COMMON", null);`, insert:

```csharp

    // --- Partial Solo/Mute audition ---
    private readonly bool[] _savedSwitches = new bool[Constants.NO_OF_PARTIALS_SN_SYNTH_TONE];
    private bool _auditing;
    private bool _suppressRecompute;

    private bool _isAuditioning;
    /// <summary>True while any partial is soloed or muted (drives the banner + disables card on/off).</summary>
    public bool IsAuditioning
    {
        get => _isAuditioning;
        private set => this.RaiseAndSetIfChanged(ref _isAuditioning, value);
    }

    /// <summary>Recompute effective partial on/off from the solo/mute flags. Snapshots the real
    /// switches when an audition begins and restores them when it ends (safe audition).</summary>
    public void RecomputeAudition()
    {
        if (_suppressRecompute) return;

        var solo = Partials.Select(p => p.Solo).ToList();
        var mute = Partials.Select(p => p.Mute).ToList();
        var active = PartialAudition.IsAuditioning(solo, mute);

        if (active && !_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) _savedSwitches[i] = Partials[i].IsOn.Value;
            _auditing = true;
        }

        if (active)
        {
            var saved = new bool[Partials.Count];
            for (var i = 0; i < Partials.Count; i++) saved[i] = _savedSwitches[i];
            var eff = PartialAudition.Effective(saved, solo, mute);
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = eff[i];
        }
        else if (_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = _savedSwitches[i];
            _auditing = false;
        }

        IsAuditioning = active;
    }

    /// <summary>Clear all solo/mute and restore the saved switches (single recompute).</summary>
    [ReactiveCommand]
    public void ClearAudition()
    {
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        _suppressRecompute = false;
        RecomputeAudition();
    }
```

- [ ] **Step 4: Build to verify it compiles.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. (Ignore `MSB3027`/`MSB3021` exe-lock messages from a running app — only `error CS/AVLN/XAMLIL` count.) `[ReactiveCommand]` generates `ClearAuditionCommand`.

- [ ] **Step 5: Commit.**

```bash
git add Src/ViewModels/SNSPartialViewModel.cs Src/ViewModels/SNSynthToneEditorViewModel.cs
git commit -m "feat(sns): partial solo/mute flags + editor audition coordinator"
```

---

## Task 3: View — S/M buttons, disabled on/off, banner (+ brushes)

**Files:**
- Modify: `Src/App.axaml`
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

- [ ] **Step 1: Add brushes to `App.axaml`.** Near the other `Sn*` `SolidColorBrush` resources, add:

```xml
    <SolidColorBrush x:Key="SnSoloBrush" Color="#4FB06A"/>
    <SolidColorBrush x:Key="SnMuteBrush" Color="#C25A4F"/>
    <SolidColorBrush x:Key="SnAuditionBannerBrush" Color="#5A4A1E"/>
```

- [ ] **Step 2: Add the S/M `:checked` styles** to the editor view. In `Src/Views/SNSynthToneEditorView.axaml`, inside the existing `<UserControl.Styles>` block (which already has the `sliderLabel` style), add:

```xml
        <Style Selector="ToggleButton.solo:checked">
            <Setter Property="Background" Value="{StaticResource SnSoloBrush}"/>
        </Style>
        <Style Selector="ToggleButton.mute:checked">
            <Setter Property="Background" Value="{StaticResource SnMuteBrush}"/>
        </Style>
```

- [ ] **Step 3: Add S/M buttons and disable the on/off toggle in the rack card.** Replace this exact block:

```xml
                                <DockPanel>
                                    <TextBlock Text="{Binding Title}" FontWeight="Bold"/>
                                    <ToggleSwitch DockPanel.Dock="Right" OnContent="" OffContent=""
                                                  IsChecked="{Binding IsOn.Value, Mode=TwoWay}"
                                                  ToolTip.Tip="Partial on/off"/>
                                </DockPanel>
```

with:

```xml
                                <DockPanel>
                                    <TextBlock Text="{Binding Title}" FontWeight="Bold" VerticalAlignment="Center"/>
                                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="4"
                                                VerticalAlignment="Center">
                                        <ToggleButton Classes="solo" Content="S" MinWidth="26" Width="26" Padding="0"
                                                      FontSize="11" IsChecked="{Binding Solo, Mode=TwoWay}"
                                                      ToolTip.Tip="Solo (audition)"/>
                                        <ToggleButton Classes="mute" Content="M" MinWidth="26" Width="26" Padding="0"
                                                      FontSize="11" IsChecked="{Binding Mute, Mode=TwoWay}"
                                                      ToolTip.Tip="Mute (audition)"/>
                                        <ToggleSwitch OnContent="" OffContent=""
                                                      IsChecked="{Binding IsOn.Value, Mode=TwoWay}"
                                                      IsEnabled="{Binding !#Root.((vm:SNSynthToneEditorViewModel)DataContext).IsAuditioning}"
                                                      ToolTip.Tip="Partial on/off (disabled while auditioning)"/>
                                    </StackPanel>
                                </DockPanel>
```

- [ ] **Step 4: Wrap the rack in a Grid with the audition banner.** Replace this exact opening of the rack `ListBox`:

```xml
            <ListBox Grid.Column="0" Margin="0,0,8,0"
                     ItemsSource="{Binding Partials}"
                     SelectedItem="{Binding SelectedPartial, Mode=TwoWay}">
```

with:

```xml
            <Grid Grid.Column="0" Margin="0,0,8,0" RowDefinitions="Auto,*">
              <Border Grid.Row="0" IsVisible="{Binding IsAuditioning}" Margin="0,0,0,8"
                      Background="{StaticResource SnAuditionBannerBrush}" CornerRadius="6" Padding="8">
                  <StackPanel Spacing="6">
                      <TextBlock Text="Auditioning (solo/mute) — restore before saving"
                                 TextWrapping="Wrap" FontSize="12"/>
                      <Button Content="Restore" HorizontalAlignment="Left" Command="{Binding ClearAuditionCommand}"/>
                  </StackPanel>
              </Border>
              <ListBox Grid.Row="1"
                     ItemsSource="{Binding Partials}"
                     SelectedItem="{Binding SelectedPartial, Mode=TwoWay}">
```

Then find the matching closing `</ListBox>` for this rack ListBox (the one right before `<!-- right column -->` / the `Grid.Column="1"` content; it is the ListBox whose `ItemsSource="{Binding Partials}"`) and add a `</Grid>` immediately AFTER that `</ListBox>` to close the new wrapper Grid.

- [ ] **Step 5: Build to verify XAML compiles.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. Fix any `AVLN`/`XAMLIL` errors. `#Root` is the view root `x:Name`; `IsAuditioning`/`ClearAuditionCommand` resolve on the root `SNSynthToneEditorViewModel` DataContext; no inline hex colors.

- [ ] **Step 6: Commit.**

```bash
git add Src/App.axaml Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): rack S/M buttons + audition banner (Restore)"
```

---

## Task 4: Verification + smoke checklist

**Files:** none (verification only).

- [ ] **Step 1: Full build.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Full test run.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: 172 prior + the new `TestPartialAudition` tests pass. If the app is running and the build can't copy the exe, close it or use `--no-build` against the last good build.

- [ ] **Step 3: Hand the hardware smoke checklist (spec §10) to the user** (they run it on the Integra-7):
  1. SN-S part → Editor. Note the three cards' on/off states.
  2. **S** on Partial 2 → only Partial 2 sounds; cards 1 & 3 show off + disabled; banner appears.
  3. **S** on Partial 1 too → Partials 1 & 2 sound (multi-solo).
  4. **Restore** → all three return to their original on/off; banner clears; toggles re-enable.
  5. No solo: **M** on Partial 1 → Partial 1 silenced, others unchanged; Restore returns it.
  6. Solo a partial that was off → it sounds; Restore → it's off again.
  7. After Restore, confirm the saved on/off matches step 1 (e.g. raw Common tab Partial Switches).

- [ ] **Step 4: Finish the branch** — once the user is satisfied, use superpowers:finishing-a-development-branch to merge `sns-solo-mute` to `main` (Option 1, local).

---

## Self-Review notes (author)
- Spec coverage: Task 1 = §4a + §8; Task 2 = §4b + §4c + §5; Task 3 = §4d + §4e; Task 4 = §9 verify + §10 smoke.
- Compile order: Task 2 changes both VMs together because the `Solo`/`Mute` setters call `_parent.RecomputeAudition()` defined in the same task; `System.Linq` added for `Select`/`ToList`.
- `IsOn.Value` setter is idempotent (no write when unchanged), so recompute only emits SysEx for real changes; snapshot/restore guarantees the saved on/off returns.
- New brushes are `SolidColorBrush` resources in `App.axaml` (no inline hex); S/M active states use `ToggleButton.solo:checked` / `.mute:checked` selectors.
