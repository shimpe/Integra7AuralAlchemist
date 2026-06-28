# Group Advanced Parameter Tabs Under One "Advanced" Tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** De-crowd the per-part tab strip by moving all "Advanced —…" raw-parameter tabs under a single top-level "Advanced" tab (sub-tabs, prefix dropped), while keeping the friendly editors' "Advanced —…" navigation working.

**Architecture:** Make the existing `SelectTabByTag` attached behavior recurse (select a tab by `Tag` at any nesting depth, plus its ancestor TabItems) and repair stale hidden inner selections on preset-type change. Then nest the "Advanced —…" TabItems inside one "Advanced" TabItem's inner `TabControl` in `MainWindow.axaml`. The single `ToneTabKey` binding and the view-models are unchanged.

**Tech Stack:** Avalonia 12 (XAML + an attached behavior); C# / .NET 10. No view-model changes; no headless-UI test harness (UI verification is build + manual).

**Build command** (user-local .NET 10 SDK; the system `dotnet` is too old):
- `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Full tests (no new tests in this feature, but confirm no regressions): `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release` → expect `Passed! - Failed: 0, Passed: 281`.

**Standing constraints:** never `git --no-verify`; this work is on branch `advanced-tab-grouping` (already created off `main`). Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Transient `Permission denied` on `.git/objects` (Windows AV) — retry the commit once. There is an unrelated uncommitted change to `Src/Models/Services/AsyncMidiInputWrapper.cs` — **do NOT stage or commit it**; use explicit file paths in every `git add`.

---

## File Structure

- **Modify** `Src/Behaviors/TabControlBehaviors.cs` — recursive tag-select + hidden-selection repair (one responsibility: drive tab selection from a tag key).
- **Modify** `Src/Views/MainWindow.axaml` — nest the "Advanced —…" tabs under one "Advanced" TabItem; drop the header prefix.

No view-model, parameter-grid, or navigation-contract changes.

---

## Task 1: Recursive `SelectTabByTag` + hidden-selection repair

Done first so it is backward-compatible on the still-flat XAML: with no nesting, the recursion finds tags at depth 0 exactly like the old single-level scan, and the repair is a no-op (nothing hidden-and-selected). So the app behaves identically until Task 2 introduces the nesting.

**Files:**
- Modify: `Src/Behaviors/TabControlBehaviors.cs`

> **Note on testing:** this is an Avalonia attached behavior operating on `TabControl`/`TabItem`; the project has no headless-UI test harness and the existing behavior has no unit test. Verification is build green + (after Task 2) manual. No new unit test.

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `Src/Behaviors/TabControlBehaviors.cs` with:

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Integra7AuralAlchemist.Behaviors;

/// <summary>
/// Attached behaviour to select a <see cref="TabControl"/> tab by a stable <see cref="TabItem.Tag"/>
/// value instead of a brittle, layout-order-dependent index. Bind <c>SelectTabByTag</c> to a key
/// (e.g. the current tone type or an "Advanced" sub-tab tag); the tab whose <c>Tag</c> equals that
/// value is selected — at any nesting depth, opening each containing tab on the way. It is a no-op
/// when the value is null/empty or no tab matches.
///
/// After selecting, it also repairs any nested <see cref="TabControl"/> whose currently-selected tab
/// has become hidden (e.g. an "Advanced" sub-tab left selected when the preset's engine changed), so
/// a hidden tab's stale content can never be shown (Avalonia #16879).
/// </summary>
public sealed class TabControlBehaviors
{
    private TabControlBehaviors()
    {
    }

    public static readonly AttachedProperty<string?> SelectTabByTagProperty =
        AvaloniaProperty.RegisterAttached<TabControlBehaviors, TabControl, string?>("SelectTabByTag");

    public static void SetSelectTabByTag(TabControl control, string? value) =>
        control.SetValue(SelectTabByTagProperty, value);

    public static string? GetSelectTabByTag(TabControl control) =>
        control.GetValue(SelectTabByTagProperty);

    static TabControlBehaviors()
    {
        SelectTabByTagProperty.Changed.AddClassHandler<TabControl>(OnSelectTabByTagChanged);
    }

    private static void OnSelectTabByTagChanged(TabControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string tag || string.IsNullOrEmpty(tag))
            return;

        SelectByTag(control, tag);

        // Reselect a visible tab in any nested control whose selected sub-tab is now hidden (e.g. the
        // "Advanced" sub-tabs after an engine change). Deferred so the per-engine IsVisible bindings
        // have settled before we read them.
        Dispatcher.UIThread.Post(() => RepairHiddenSelections(control));
    }

    /// <summary>Select the TabItem whose Tag matches, searching nested TabControls, and select each
    /// ancestor TabItem so the containing ("Advanced") tab is opened too. Returns true if found.</summary>
    private static bool SelectByTag(TabControl control, string tag)
    {
        foreach (var item in control.Items)
        {
            if (item is not TabItem tab)
                continue;

            if (string.Equals(tab.Tag as string, tag, StringComparison.Ordinal))
            {
                control.SelectedItem = tab;
                return true;
            }

            if (tab.Content is TabControl nested && SelectByTag(nested, tag))
            {
                control.SelectedItem = tab; // open the ancestor that contains the match
                return true;
            }
        }

        return false;
    }

    /// <summary>For this control and every nested TabControl, if the selected TabItem is hidden,
    /// reselect the first visible TabItem (or null if none).</summary>
    private static void RepairHiddenSelections(TabControl control)
    {
        if (control.SelectedItem is TabItem { IsVisible: false })
            control.SelectedItem = control.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);

        foreach (var item in control.Items)
            if (item is TabItem { Content: TabControl nested })
                RepairHiddenSelections(nested);
    }
}
```

What changed vs the original: the flat `foreach` is now the recursive `SelectByTag` (descends into a TabItem whose content is a `TabControl`, and selects ancestors on the way back up); a deferred `RepairHiddenSelections` pass is added. Items that are not `TabItem`s (e.g. the per-partial `TabControl`'s `ItemsSource` of view-models) are skipped by the `is TabItem` checks, so those controls are walked harmlessly.

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Behaviors/TabControlBehaviors.cs
git commit -m "feat: recursive SelectTabByTag + hidden-selection repair for nested tabs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Nest the "Advanced —…" tabs under one "Advanced" tab

**Files:**
- Modify: `Src/Views/MainWindow.axaml`

> **Note on testing:** XAML layout change; verification is build green + manual (see the manual checklist at the end). No unit test.

- [ ] **Step 1: Replace the per-part `TabControl` block**

First **Read** the relevant region of `Src/Views/MainWindow.axaml` (around lines 221–455) so you have the exact current text — the per-part `TabControl` to replace starts at the line `<TabControl Grid.Column="1"` carrying `behaviors:TabControlBehaviors.SelectTabByTag="{Binding ToneTabKey}"` and ends at its matching `</TabControl>` (just before the `</Grid>` that closes the part template). It contains `Midi`, `Set Part`, `Set EQ`, the interleaved `Editor`/`Advanced —…` tabs, and the four `Advanced — Partials` tabs. Use that exact text as the `old_string` for a single Edit, replacing the whole element with exactly this (matching the file's existing indentation):

```xml
                                        <TabControl Grid.Column="1"
                                                    Margin="8,0,0,0"
                                                    TabStripPlacement="Top"
                                                    VerticalAlignment="Stretch"
                                                    VerticalContentAlignment="Stretch"
                                                    behaviors:TabControlBehaviors.SelectTabByTag="{Binding ToneTabKey}">
                                            <TabItem Header="Midi">
                                                <local:ParameterCollection
                                                    Parameters="{Binding StudioSetMidiParameters}"
                                                    SearchText="{Binding SearchTextStudioSetMidi, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Set Part">
                                                <local:ParameterCollection
                                                    Parameters="{Binding StudioSetPartParameters}"
                                                    SearchText="{Binding SearchTextStudioSetPart, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Set EQ">
                                                <local:ParameterCollection
                                                    Parameters="{Binding StudioSetPartEQParameters}"
                                                    SearchText="{Binding SearchTextStudioSetPartEQ, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Editor"
                                                     Tag="PCMS"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:PCMSynthToneEditorView DataContext="{Binding PcmSynthToneEditor}" />
                                            </TabItem>
                                            <TabItem Header="Editor"
                                                     Tag="PCMD"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:PCMDrumKitEditorView DataContext="{Binding PcmDrumKitEditor}" />
                                            </TabItem>
                                            <TabItem Header="Editor"
                                                     Tag="SN-S"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:SNSynthToneEditorView DataContext="{Binding SNSynthToneEditor}" />
                                            </TabItem>
                                            <TabItem Header="Editor"
                                                     Tag="SN-A"
                                                     IsVisible="{Binding SelectedPresetIsSNAcousticTone}">
                                                <local:SNAcousticToneEditorView DataContext="{Binding SNAcousticToneEditor}" />
                                            </TabItem>
                                            <TabItem Header="Editor"
                                                     Tag="SN-D"
                                                     IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <local:SNDrumKitEditorView DataContext="{Binding SNDrumKitEditor}" />
                                            </TabItem>
                                            <TabItem Header="Advanced">
                                                <TabControl TabStripPlacement="Top"
                                                            VerticalAlignment="Stretch"
                                                            VerticalContentAlignment="Stretch">
                                                    <TabItem Header="Common"
                                                             Tag="PCM-SYN-COMMON"
                                                             IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMSynthToneCommonParameters}"
                                                            SearchText="{Binding SearchTextPCMSynthToneCommon, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Common 2"
                                                             Tag="PCM-SYN-COMMON2"
                                                             IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMSynthToneCommon2Parameters}"
                                                            SearchText="{Binding SearchTextPCMSynthToneCommon2, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="MFX"
                                                             Tag="PCM-SYN-MFX"
                                                             IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMSynthToneCommonMFXParameters}"
                                                            SearchText="{Binding SearchTextPCMSynthToneCommonMFX, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="PMT"
                                                             Tag="PCM-SYN-PMT"
                                                             IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMSynthTonePMTParameters}"
                                                            SearchText="{Binding SearchTextPCMSynthTonePMT, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Kit"
                                                             Tag="PCMD-KIT"
                                                             IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMDrumKitCommonParameters}"
                                                            SearchText="{Binding SearchTextPCMDrumKitCommon, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Kit extra"
                                                             Tag="PCMD-KIT2"
                                                             IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMDrumKitCommon2Parameters}"
                                                            SearchText="{Binding SearchTextPCMDrumKitCommon2, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="MFX"
                                                             Tag="PCMD-MFX"
                                                             IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMDrumKitCommonMFXParameters}"
                                                            SearchText="{Binding SearchTextPCMDrumKitCommonMFX, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Comp-EQ"
                                                             Tag="PCMD-COMPEQ"
                                                             IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding PCMDrumKitCompEQParameters}"
                                                            SearchText="{Binding SearchTextPCMDrumKitCompEQ, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Common"
                                                             Tag="SN-S-COMMON"
                                                             IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNSynthToneCommonParameters}"
                                                            SearchText="{Binding SearchTextSNSynthToneCommon, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="MFX"
                                                             Tag="SN-S-MFX"
                                                             IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNSynthToneCommonMFXParameters}"
                                                            SearchText="{Binding SearchTextSNSynthToneCommonMFX, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Tone"
                                                             Tag="SN-A-TONE"
                                                             IsVisible="{Binding SelectedPresetIsSNAcousticTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNAcousticToneCommonParameters}"
                                                            SearchText="{Binding SearchTextSNAcousticToneCommon, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="MFX"
                                                             Tag="SN-A-MFX"
                                                             IsVisible="{Binding SelectedPresetIsSNAcousticTone}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNAcousticToneCommonMFXParameters}"
                                                            SearchText="{Binding SearchTextSNAcousticToneCommonMFX, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Kit"
                                                             Tag="SN-D-KIT"
                                                             IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNDrumKitCommonParameters}"
                                                            SearchText="{Binding SearchTextSNDrumKitCommon, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="MFX"
                                                             Tag="SN-D-MFX"
                                                             IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNDrumKitCommonMFXParameters}"
                                                            SearchText="{Binding SearchTextSNDrumKitCommonMFX, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Comp-EQ"
                                                             Tag="SN-D-COMPEQ"
                                                             IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                        <local:ParameterCollection
                                                            Parameters="{Binding SNDrumKitCompEQParameters}"
                                                            SearchText="{Binding SearchTextSNDrumKitCompEQ, Mode=TwoWay}" />
                                                    </TabItem>
                                                    <TabItem Header="Partials"
                                                             Tag="PCM-SYN-PARTIALS"
                                                             IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                        <TabControl
                                                            SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                            ItemsSource="{Binding PcmSynthTonePartialViewModels}"
                                                            TabStripPlacement="Left">
                                                            <TabControl.ItemTemplate>
                                                                <DataTemplate>
                                                                    <TextBlock Text="{Binding Header}" />
                                                                </DataTemplate>
                                                            </TabControl.ItemTemplate>
                                                            <TabControl.ContentTemplate>
                                                                <DataTemplate DataType="vm:PartialViewModel">
                                                                    <local:ParameterCollection
                                                                        Parameters="{Binding PartialParameters}"
                                                                        SearchText="{Binding SearchTextPartial, Mode=TwoWay}" />
                                                                </DataTemplate>
                                                            </TabControl.ContentTemplate>
                                                        </TabControl>
                                                    </TabItem>
                                                    <TabItem Header="Partials"
                                                             Tag="PCMD-PARTIALS"
                                                             IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                        <TabControl
                                                            SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                            ItemsSource="{Binding PcmDrumKitPartialViewModels}"
                                                            TabStripPlacement="Left">
                                                            <TabControl.ItemTemplate>
                                                                <DataTemplate>
                                                                    <TextBlock Text="{Binding Header}" />
                                                                </DataTemplate>
                                                            </TabControl.ItemTemplate>
                                                            <TabControl.ContentTemplate>
                                                                <DataTemplate DataType="vm:PartialViewModel">
                                                                    <local:ParameterCollection
                                                                        Parameters="{Binding PartialParameters}"
                                                                        SearchText="{Binding SearchTextPartial, Mode=TwoWay}" />
                                                                </DataTemplate>
                                                            </TabControl.ContentTemplate>
                                                        </TabControl>
                                                    </TabItem>
                                                    <TabItem Header="Partials"
                                                             Tag="SN-S-PARTIALS"
                                                             IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                        <TabControl
                                                            ItemsSource="{Binding SNSynthTonePartialViewModels}"
                                                            SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                            TabStripPlacement="Left">
                                                            <TabControl.ItemTemplate>
                                                                <DataTemplate>
                                                                    <TextBlock Text="{Binding Header}" />
                                                                </DataTemplate>
                                                            </TabControl.ItemTemplate>
                                                            <TabControl.ContentTemplate>
                                                                <DataTemplate DataType="vm:PartialViewModel">
                                                                    <local:ParameterCollection
                                                                        Parameters="{Binding PartialParameters}"
                                                                        SearchText="{Binding SearchTextPartial, Mode=TwoWay}" />
                                                                </DataTemplate>
                                                            </TabControl.ContentTemplate>
                                                        </TabControl>
                                                    </TabItem>
                                                    <!-- SNAcoustic doesn't have partials -->
                                                    <TabItem Header="Partials" Tag="SN-D-PARTIALS" IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                        <TabControl
                                                            SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                            ItemsSource="{Binding SNDrumKitPartialViewModels}"
                                                            TabStripPlacement="Left">
                                                            <TabControl.ItemTemplate>
                                                                <DataTemplate>
                                                                    <TextBlock Text="{Binding Header}" />
                                                                </DataTemplate>
                                                            </TabControl.ItemTemplate>
                                                            <TabControl.ContentTemplate>
                                                                <DataTemplate DataType="vm:PartialViewModel">
                                                                    <local:ParameterCollection
                                                                        Parameters="{Binding PartialParameters}"
                                                                        SearchText="{Binding SearchTextPartial, Mode=TwoWay}" />
                                                                </DataTemplate>
                                                            </TabControl.ContentTemplate>
                                                        </TabControl>
                                                    </TabItem>
                                                </TabControl>
                                            </TabItem>
                                        </TabControl>
```

This keeps every `Tag`, `IsVisible` binding, `Parameters`/`SearchText` binding, and the four per-partial `TabControl`s exactly as before; it only (a) groups the five `Editor` tabs ahead of one new `Advanced` TabItem, (b) moves all the former `Advanced —…` tabs into the `Advanced` TabItem's inner `TabControl`, and (c) drops the `"Advanced — "` header prefix.

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`. (Avalonia compiles XAML at build time, so a malformed tree fails here.)

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Views/MainWindow.axaml
git commit -m "feat: group advanced parameter tabs under one Advanced tab

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with the app running):**
- Top strip per preset shows only `Midi`, `Set Part`, `Set EQ`, `Editor`, `Advanced`.
- `Advanced` contains the current engine's sub-tabs with the prefix dropped (e.g. PCM Synth → `Common`, `Common 2`, `MFX`, `PMT`, `Partials`).
- From each friendly editor, the "Advanced —…" buttons open `Advanced` and select the right sub-tab (and the right partial for the Partials buttons).
- Switching to a different-engine preset defaults to `Editor`; opening `Advanced` afterwards shows the new engine's sub-tabs with a valid (non-stale) selection.
- Switching between same-engine presets leaves the current tab/sub-tab alone.

---

## Final verification

- [ ] Full suite green (`Passed: 281`); `git log --oneline main..HEAD` shows the spec, Task 1, and Task 2 commits on `advanced-tab-grouping`; `git status` shows only the unrelated `AsyncMidiInputWrapper.cs` change uncommitted.

After both tasks: dispatch a final review, then use superpowers:finishing-a-development-branch.

---

## Spec coverage (self-review)

- **§1 Tab structure** → Task 2 (outer keeps studio-set + 5 `Editor` + one `Advanced`; inner holds the prefix-dropped advanced sub-tabs with unchanged `Tag`/`IsVisible`; per-partial TabControls moved verbatim).
- **§2a Recursive select** → Task 1 (`SelectByTag` recursion + ancestor selection).
- **§2b Repair hidden inner selections** → Task 1 (`RepairHiddenSelections`, deferred via `Dispatcher.UIThread.Post`).
- **§3 Unchanged** → no VM/binding-contract changes (only the two files above).
- **Edge cases** (lazy inner realization; same-engine leaves tab alone via unchanged `ToneTabKey`; non-`TabItem` items in per-partial controls skipped) → handled by the recursion's `is TabItem` checks + the unchanged `ToneTabKey` semantics.
- **Testing** → build + manual (no headless-UI harness), as the spec states.
- **Out of scope** (SRX Loader / Motional Surround tabs, parameter grids, view-models) → untouched.
