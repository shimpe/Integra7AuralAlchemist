# SuperNATURAL Synth (SN-S) Editor — Phase 4 (MFX panel) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a friendly, tone-wide **Multi-Effect (MFX)** panel as a third "FX" sub-tab of the SN-S editor: a categorized effect-type picker, bypass, Chorus/Reverb send faders, and the current effect's parameters rendered dynamically from the parameter DB with cleaned-up labels, plus a link to the raw Advanced — MFX tab.

**Architecture:** A pure `MfxCatalog` (families + label cleanup, unit-tested) drives an `MfxPanelViewModel` that wraps the `SuperNATURAL Synth Tone Common MFX` domain. The picker and sends use the existing `ParamString`/`ParamInt` throttled wrappers; the per-type controls are derived by filtering the domain's MFX params on the `ParentCtrlDispValue` discriminator and rendered with the existing `DataTemplateProvider.ParameterValueTemplate`. The panel is hosted via `ContentControl` + `ViewLocator` in a new "FX" `TabItem`.

**Tech Stack:** Avalonia 12, ReactiveUI (`WhenAnyValue`, `ReactiveCommand`, `RaiseAndSetIfChanged`), .NET 10, NUnit 3. Build/test with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-mfx` (already created; spec committed).

Reference: spec `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase4-mfx-design.md`. Memory: `parameter-domain-infrastructure`, `visual-editor-pattern`, `sn-s-tone-parameters`, `no-hardcoded-colors-in-xaml`.

---

## File Structure

- Create `Src/Models/Services/MfxCatalog.cs` — pure families table + label cleanup (no Avalonia/domain deps).
- Create `Tests/TestMfxCatalog.cs` — NUnit tests for `MfxCatalog`.
- Create `Src/ViewModels/MfxPanelViewModel.cs` — panel VM + `MfxParamDisplay` record.
- Create `Src/Views/MfxPanelView.axaml` + `Src/Views/MfxPanelView.axaml.cs` — the panel view.
- Modify `Src/ViewModels/SNSynthToneEditorViewModel.cs` — build/expose/dispose `Mfx`.
- Modify `Src/Views/SNSynthToneEditorView.axaml` — add the "FX" `TabItem`.

---

## Task 1: `MfxCatalog` (pure) + tests

**Files:**
- Create: `Src/Models/Services/MfxCatalog.cs`
- Test: `Tests/TestMfxCatalog.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestMfxCatalog.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestMfxCatalog
{
    [Test]
    public void Families_cover_every_type_0_to_67_exactly_once()
    {
        var all = MfxCatalog.Families.SelectMany(f => f.TypeIndices).ToList();
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 68), all);
        Assert.That(all.Count, Is.EqualTo(68), "no duplicates");
    }

    [Test]
    public void FamilyOf_and_TypesIn_round_trip()
    {
        for (var i = 0; i < 68; i++)
        {
            var fam = MfxCatalog.FamilyOf(i);
            Assert.That(MfxCatalog.TypesIn(fam), Does.Contain(i), $"type {i} should be in family {fam}");
        }
    }

    [Test]
    public void FamilyOf_out_of_range_returns_empty()
    {
        Assert.That(MfxCatalog.FamilyOf(-1), Is.EqualTo(""));
        Assert.That(MfxCatalog.FamilyOf(68), Is.EqualTo(""));
    }

    [Test]
    public void FriendlyParamNames_strips_effect_prefix_for_multi_param_type()
    {
        var r = MfxCatalog.FriendlyParamNames("Equalizer",
            new[] { "Equalizer Low Freq", "Equalizer Low Gain", "Equalizer High Freq" });
        Assert.That(r, Is.EqualTo(new[] { "Low Freq", "Low Gain", "High Freq" }));
    }

    [Test]
    public void FriendlyParamNames_strips_dotted_effect_name()
    {
        var r = MfxCatalog.FriendlyParamNames("Time Ctrl. Delay",
            new[] { "Time Ctrl. Delay Time (ms-note)", "Time Ctrl. Delay Feedback" });
        Assert.That(r, Is.EqualTo(new[] { "Time (ms-note)", "Feedback" }));
    }

    [Test]
    public void FriendlyParamNames_strips_combo_arrow_effect_name()
    {
        var r = MfxCatalog.FriendlyParamNames("Overdrive->Chorus",
            new[] { "Overdrive->Chorus Overdrive Drive", "Overdrive->Chorus Chorus Rate" });
        Assert.That(r, Is.EqualTo(new[] { "Overdrive Drive", "Chorus Rate" }));
    }

    [Test]
    public void FriendlyParamNames_punctuation_mismatch_still_trims_and_never_empty()
    {
        // MFX_TYPE name uses '/', the leaf names use '-' — typed strip fails, common-prefix path wins.
        var r = MfxCatalog.FriendlyParamNames("Overdrive/Distortion->TouchWah",
            new[] { "Overdrive-Distortion->TouchWah Drive Switch", "Overdrive-Distortion->TouchWah Type" });
        Assert.That(r, Is.EqualTo(new[] { "Drive Switch", "Type" }));
    }

    [Test]
    public void FriendlyParamName_single_is_never_empty()
    {
        Assert.That(MfxCatalog.FriendlyParamName("Equalizer", "Equalizer Low Freq"), Is.EqualTo("Low Freq"));
        // Degenerate: leaf equals the effect name -> fall back to the raw leaf, not empty.
        Assert.That(MfxCatalog.FriendlyParamName("Chorus", "Chorus"), Is.EqualTo("Chorus"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: FAIL — `MfxCatalog` does not exist (compile error).

- [ ] **Step 3: Implement `MfxCatalog`**

Create `Src/Models/Services/MfxCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure, UI-free catalog for the Integra-7 MFX: groups the 68 effect types (0 = Thru) into
/// musical families for a two-step picker, and cleans up per-type parameter leaf names by stripping
/// the redundant effect-name prefix (e.g. "Equalizer Low Freq" -> "Low Freq").
/// Type indices match the MFX_TYPE enum order (= ParamString.Options order for the MFX Type param).
/// </summary>
public static class MfxCatalog
{
    /// <summary>One picker family: a display name and the MFX type indices it contains.</summary>
    public sealed record Family(string Name, int[] TypeIndices);

    /// <summary>Families in display order; the union of TypeIndices covers 0..67 exactly once.</summary>
    public static IReadOnlyList<Family> Families { get; } = new List<Family>
    {
        new("Off (Thru)",       new[] { 0 }),
        new("EQ & Filter",      new[] { 1, 2, 3, 4, 5 }),
        new("Wah & Voice",      new[] { 6, 7 }),
        new("Phaser",           new[] { 9, 10, 11, 12, 13, 14 }),
        new("Chorus & Mod",     new[] { 22, 23, 24, 25, 26, 27 }),
        new("Tremolo & Pan",    new[] { 15, 16, 17, 18 }),
        new("Rotary",           new[] { 19, 20, 21 }),
        new("Amp & Distortion", new[] { 8, 28, 29, 30 }),
        new("Dynamics",         new[] { 31, 32, 33 }),
        new("Delay",            new[] { 34, 35, 36, 37, 38, 39, 40 }),
        new("Lo-Fi & Pitch",    new[] { 41, 42, 43, 44 }),
        new("Combos",           Enumerable.Range(45, 23).ToArray()), // 45..67
    };

    /// <summary>Family name containing the given type index, or "" if out of range.</summary>
    public static string FamilyOf(int typeIndex)
        => Families.FirstOrDefault(f => f.TypeIndices.Contains(typeIndex))?.Name ?? "";

    /// <summary>Type indices in a family (empty if the family name is unknown).</summary>
    public static IReadOnlyList<int> TypesIn(string familyName)
        => Families.FirstOrDefault(f => f.Name == familyName)?.TypeIndices ?? Array.Empty<int>();

    /// <summary>Friendly label for a single per-type parameter leaf name.</summary>
    public static string FriendlyParamName(string effectTypeName, string leafName)
        => StripTyped(effectTypeName, leafName) ?? leafName;

    /// <summary>
    /// Friendly labels for all of one effect type's parameter leaf names. Prefers stripping
    /// "&lt;effectTypeName&gt; "; for names that don't match (e.g. punctuation differences in combo
    /// types) falls back to the longest common word-boundary prefix shared by the set. Never empty.
    /// </summary>
    public static IReadOnlyList<string> FriendlyParamNames(string effectTypeName, IReadOnlyList<string> leafNames)
    {
        if (leafNames.Count == 0) return leafNames;
        var common = CommonWordPrefix(leafNames);
        return leafNames.Select(n =>
        {
            var r = StripTyped(effectTypeName, n)
                    ?? (common.Length > 0 && n.StartsWith(common, StringComparison.Ordinal)
                            ? n[common.Length..]
                            : n);
            r = r.Trim();
            return r.Length == 0 ? n : r;
        }).ToList();
    }

    // Strips "<effectTypeName> " from the leaf if present; returns null if it doesn't match.
    private static string? StripTyped(string effectTypeName, string leafName)
    {
        var typed = effectTypeName + " ";
        return leafName.StartsWith(typed, StringComparison.Ordinal) ? leafName[typed.Length..] : null;
    }

    // Longest common prefix across all names, trimmed back to a whole-word boundary (incl. trailing space).
    private static string CommonWordPrefix(IReadOnlyList<string> names)
    {
        var p = names[0];
        foreach (var n in names.Skip(1))
        {
            var k = 0;
            while (k < p.Length && k < n.Length && p[k] == n[k]) k++;
            p = p[..k];
            if (p.Length == 0) break;
        }
        var sp = p.LastIndexOf(' ');
        return sp < 0 ? "" : p[..(sp + 1)];
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: PASS — all `TestMfxCatalog` tests green, existing 164 still green.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/MfxCatalog.cs Tests/TestMfxCatalog.cs
git commit -m "feat(sns): MFX catalog — type families + per-type label cleanup (+ tests)"
```

---

## Task 2: `MfxPanelViewModel` + `MfxParamDisplay`

**Files:**
- Create: `Src/ViewModels/MfxPanelViewModel.cs`

This VM has no pure unit test (it depends on the live domain); it is verified by build here and by the hardware smoke test in Task 5. Keep all testable logic in `MfxCatalog` (Task 1).

- [ ] **Step 1: Implement the VM**

Create `Src/ViewModels/MfxPanelViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One per-type MFX parameter for the dynamic grid: the live FQP plus its cleaned label.</summary>
public sealed record MfxParamDisplay(FullyQualifiedParameter Param, string Label);

/// <summary>
/// Friendly, tone-wide Multi-Effect panel. Two-combo (family -> type) picker, bypass (Thru),
/// Chorus/Reverb send faders, and the current effect type's parameters rendered dynamically from the
/// MFX domain (filtered by the MFX Type discriminator) using DataTemplateProvider.ParameterValueTemplate.
/// </summary>
public sealed class MfxPanelViewModel : ViewModelBase, IDisposable
{
    private const string PFX = "SuperNATURAL Synth Tone Common MFX/";
    private const string Thru = "Thru";

    private readonly DomainBase _mfxDomain;
    private readonly List<FullyQualifiedParameter> _allMfxParams; // all variants, context-independent
    private readonly IDisposable _typeSub;

    private bool _syncing;            // true while syncing the picker from Type (suppress write-back)
    private string _lastEffectType = "Equalizer";

    public ParamString Type { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    public IReadOnlyList<string> Families { get; } = MfxCatalog.Families.Select(f => f.Name).ToList();
    public ObservableCollection<MfxParamDisplay> TypeParameters { get; } = [];
    public bool HasTypeParameters => TypeParameters.Count > 0;

    public ReactiveCommand<Unit, Unit> AdvancedMfxCommand { get; }

    public MfxPanelViewModel(DomainBase mfxDomain, ThrottledParameterWriter writer, Action navigateToAdvanced)
    {
        _mfxDomain = mfxDomain;
        _allMfxParams = mfxDomain.GetRelevantParameters(true, true);
        var byPath = _allMfxParams.ToDictionary(p => p.ParSpec.Path);

        Type = new ParamString(mfxDomain, byPath[PFX + "MFX Type"], writer);
        ChorusSend = new ParamInt(mfxDomain, byPath[PFX + "MFX Chorus Send Level"], writer, 0, 127);
        ReverbSend = new ParamInt(mfxDomain, byPath[PFX + "MFX Reverb Send Level"], writer, 0, 127);

        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Fires immediately with the current type, then on every change (picker / hardware / raw tab).
        _typeSub = Type.WhenAnyValue(t => t.Value).Subscribe(OnTypeChanged);
    }

    // ---- Picker projection -------------------------------------------------

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            TypesInFamily = MfxCatalog.TypesIn(value).Select(i => Type.Options[i]).ToList();
            this.RaisePropertyChanged(nameof(TypesInFamily));
            if (!_syncing && TypesInFamily.Count > 0 && !TypesInFamily.Contains(SelectedType))
                SelectedType = TypesInFamily[0]; // user switched family -> pick its first effect
        }
    }

    private IReadOnlyList<string> _typesInFamily = [];
    public IReadOnlyList<string> TypesInFamily
    {
        get => _typesInFamily;
        private set => _typesInFamily = value;
    }

    private string _selectedType = "";
    public string SelectedType
    {
        get => _selectedType;
        set
        {
            if (value is null || _selectedType == value) return;
            this.RaiseAndSetIfChanged(ref _selectedType, value);
            if (!_syncing) Type.Value = value; // write to hardware (throttled) -> OnTypeChanged
        }
    }

    // ---- Bypass (a view over Type) ----------------------------------------

    public bool Bypass
    {
        get => Type.Value == Thru;
        set
        {
            if (value == Bypass) return;
            if (value)
            {
                if (Type.Value != Thru) _lastEffectType = Type.Value;
                Type.Value = Thru;
            }
            else
            {
                Type.Value = _lastEffectType;
            }
            // Type.Value change -> OnTypeChanged raises Bypass + re-syncs picker + grid.
        }
    }

    // ---- Reaction to any MFX Type change ----------------------------------

    private void OnTypeChanged(string typeName)
    {
        _syncing = true;
        try
        {
            var idx = Type.Options is { Count: > 0 } ? IndexOfType(typeName) : -1;
            if (idx >= 0)
            {
                SelectedFamily = MfxCatalog.FamilyOf(idx);
                if (!TypesInFamily.Contains(typeName))
                {
                    // Family unchanged but list not yet built (e.g. initial) — rebuild for safety.
                    TypesInFamily = MfxCatalog.TypesIn(SelectedFamily).Select(i => Type.Options[i]).ToList();
                    this.RaisePropertyChanged(nameof(TypesInFamily));
                }
                SelectedType = typeName;
            }
        }
        finally { _syncing = false; }

        RecomputeTypeParameters(typeName);
        this.RaisePropertyChanged(nameof(Bypass));
    }

    private int IndexOfType(string typeName)
    {
        for (var i = 0; i < Type.Options.Count; i++)
            if (Type.Options[i] == typeName) return i;
        return -1;
    }

    private void RecomputeTypeParameters(string typeName)
    {
        var relevant = _allMfxParams
            .Where(p => !p.ParSpec.Reserved
                        && p.ParSpec.Path.Contains("/MFX Parameter ")
                        && p.ParSpec.ParentCtrlDispValue == typeName)
            .OrderBy(p => p.ParSpec.AddressInt)
            .ToList();
        var labels = MfxCatalog.FriendlyParamNames(typeName, relevant.Select(p => p.ParSpec.Name).ToList());

        TypeParameters.Clear();
        for (var i = 0; i < relevant.Count; i++)
            TypeParameters.Add(new MfxParamDisplay(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasTypeParameters));
    }

    public void Dispose()
    {
        _typeSub.Dispose();
        AdvancedMfxCommand.Dispose();
        Type.Dispose();
        ChorusSend.Dispose();
        ReverbSend.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. (Ignore MSB3027/MSB3021 exe-lock warnings if the app is running — only `error CS/AVLN/XAMLIL` count.)

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/MfxPanelViewModel.cs
git commit -m "feat(sns): MfxPanelViewModel — picker, bypass, sends, dynamic per-type params"
```

---

## Task 3: `MfxPanelView`

**Files:**
- Create: `Src/Views/MfxPanelView.axaml`
- Create: `Src/Views/MfxPanelView.axaml.cs`

- [ ] **Step 1: Implement the code-behind**

Create `Src/Views/MfxPanelView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class MfxPanelView : UserControl
{
    public MfxPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
```

- [ ] **Step 2: Implement the view**

Create `Src/Views/MfxPanelView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:dataTemplates="using:Integra7AuralAlchemist.DataTemplates"
             x:DataType="vm:MfxPanelViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.MfxPanelView">
    <UserControl.Styles>
        <Style Selector="TextBlock.sliderLabel">
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="MinHeight" Value="40"/>
        </Style>
    </UserControl.Styles>

    <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
        <StackPanel Spacing="10">

            <!-- Title + bypass -->
            <Grid ColumnDefinitions="*,Auto">
                <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                    <TextBlock Text="Multi-Effect" FontWeight="Bold"/>
                    <TextBlock Text="— applies to the whole tone" Opacity="0.6" VerticalAlignment="Bottom"/>
                </StackPanel>
                <StackPanel Grid.Column="1" Spacing="2">
                    <TextBlock Text="Bypass" ToolTip.Tip="Sets the effect to Thru (no effect); flips back to your last effect."/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding Bypass, Mode=TwoWay}"/>
                </StackPanel>
            </Grid>

            <!-- Effect picker: family -> type -->
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <TextBlock Text="Effect family"/>
                    <ComboBox MinWidth="150" ItemsSource="{Binding Families}"
                              SelectedItem="{Binding SelectedFamily, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Type"/>
                    <ComboBox MinWidth="200" ItemsSource="{Binding TypesInFamily}"
                              SelectedItem="{Binding SelectedType, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <!-- Sends -->
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="200">
                    <TextBlock Classes="sliderLabel" Text="Chorus send" ToolTip.Tip="MFX Chorus Send Level"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding ChorusSend.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="200">
                    <TextBlock Classes="sliderLabel" Text="Reverb send" ToolTip.Tip="MFX Reverb Send Level"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding ReverbSend.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <Border Height="1" Background="{StaticResource SnEnvelopeAxisBrush}"/>

            <!-- Dynamic per-type parameters -->
            <TextBlock Text="{Binding SelectedType, StringFormat='{}{0} — parameters'}" FontWeight="Bold"/>
            <TextBlock Text="This effect has no extra parameters." Opacity="0.6"
                       IsVisible="{Binding !HasTypeParameters}"/>
            <ItemsControl ItemsSource="{Binding TypeParameters}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel Orientation="Horizontal"/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:MfxParamDisplay">
                        <StackPanel Width="240" Spacing="2" Margin="0,0,12,10">
                            <TextBlock Classes="sliderLabel" Text="{Binding Label}"/>
                            <ContentControl Content="{Binding Param}"
                                            ContentTemplate="{x:Static dataTemplates:DataTemplateProvider.ParameterValueTemplate}"/>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <Button HorizontalAlignment="Left" Content="Advanced MFX parameters…"
                    Command="{Binding AdvancedMfxCommand}"/>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. If you hit `AVLN`/`XAMLIL` errors, fix them before committing. (`SnPanelBackgroundBrush` and `SnEnvelopeAxisBrush` already exist in `App.axaml`.)

- [ ] **Step 4: Commit**

```bash
git add Src/Views/MfxPanelView.axaml Src/Views/MfxPanelView.axaml.cs
git commit -m "feat(sns): MfxPanelView — picker, bypass, sends, dynamic params grid"
```

---

## Task 4: Wire the panel into the editor

**Files:**
- Modify: `Src/ViewModels/SNSynthToneEditorViewModel.cs`
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

- [ ] **Step 1: Build the `Mfx` panel in the editor VM**

In `Src/ViewModels/SNSynthToneEditorViewModel.cs`, add a property and construct it in the constructor.

Add the property near `Header`/`Partials`:

```csharp
    public MfxPanelViewModel Mfx { get; }
```

In the constructor, after the `Partials` loop and `_selectedPartial = Partials[0];`, add:

```csharp
        Mfx = new MfxPanelViewModel(domain.SNSynthToneCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("SN-S-MFX", null));
```

In `Dispose()`, add `Mfx.Dispose();` (before `_writer.Dispose();`):

```csharp
    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
```

- [ ] **Step 2: Add the "FX" tab to the editor view**

In `Src/Views/SNSynthToneEditorView.axaml`, find the `TabControl` that holds the "Sound" and "Motion" `TabItem`s (the selected-partial editor). After the "Motion" `TabItem`, add:

```xml
                <TabItem Header="FX">
                    <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                        <ContentControl Content="{Binding Mfx}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
```

(`ContentControl` + `ViewLocator` resolves `MfxPanelViewModel` → `MfxPanelView`. Do not change the Copy/Paste/Init row; MFX is tone-wide and stays out of partial copy/paste per the spec.)

- [ ] **Step 3: Build to verify it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/SNSynthToneEditorViewModel.cs Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): host the MFX panel as a third FX sub-tab"
```

---

## Task 5: Verification + smoke checklist

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Full test run**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: all tests pass (164 prior + the new `TestMfxCatalog` tests). If the app is running and the build can't copy the exe, close the app or use `--no-build` against the last good build.

- [ ] **Step 3: Hand the hardware smoke checklist to the user** (they run it on the Integra-7):
  1. Select an SN-S part → Editor → select a partial → click the **FX** sub-tab.
  2. Confirm the panel reads "Multi-Effect — applies to the whole tone" with Bypass, Family/Type pickers, Chorus/Reverb sends, and a parameter grid.
  3. Change **Family** → the Type list repopulates and the first effect is selected; the effect audibly changes.
  4. Change **Type** within a family → the parameter grid updates to that effect's controls (cleaned labels) and the sound changes.
  5. Move a couple of per-type controls (slider/combo/toggle) → hear the effect change; confirm bipolar/scaled controls (e.g. EQ gain in dB) behave.
  6. Move **Chorus/Reverb send** → confirm send level changes (audible with chorus/reverb active in the Studio Set).
  7. Toggle **Bypass** on → effect = Thru (no effect), grid shows the empty-state note; toggle off → returns to the previous effect.
  8. On the hardware, change the MFX type from the front panel → the Family/Type pickers and the grid update to match (round-trip).
  9. Click **Advanced MFX parameters…** → switches to the raw "Advanced — MFX" tab; clicking it again still works (repeat navigation).
  10. Edit a per-type value in the friendly grid, then open the raw MFX tab → the same parameter shows the new value (shared FQP consistency).

- [ ] **Step 4: Finish the branch** — once the user is satisfied, use superpowers:finishing-a-development-branch to merge `sns-mfx` to `main` (Option 1, local) as in prior phases.

---

## Self-Review notes (author)
- Spec coverage: Task 1 = §4a; Task 2 = §4b + §5 + §6 (MFX excluded from partial copy/paste — no `Mfx.Params` wiring into the clipboard); Task 3 = §4c; Task 4 = §4d + §4e; Task 5 = §8/§9 verification + smoke.
- Type/family bridging uses `Type.Options` (the `Repr` names in index order) so `MfxCatalog` stays index-based; the `Combos` family entry must close with `)` not `}` (noted inline).
- Dynamic grid filters `_allMfxParams` on `ParentCtrlDispValue == typeName` (single discriminator confirmed in the DB), so it updates instantly without depending on the throttled FQP write.
