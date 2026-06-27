# SN-Drums Editor — Phase 3 (Comp-EQ) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Fill the **Comp-EQ** tab — the kit's 6-unit master compressor / 3-band EQ. A unit selector (1–6) shows the chosen unit's compressor (On, Threshold, Ratio, Attack, Release, Output Gain) and EQ (On, Low/Mid/High freq + gain, Mid Q). Completes the SN-Drums editor.

**Architecture:** `SNDrumCompEqUnitViewModel` wraps one unit's 14 params; `SNDrumCompEqPanelViewModel` owns the 6 units + a `SelectedUnit`. `SNDrumKitEditorViewModel` gains a `CompEq` panel (built once, like the MFX panel). The Comp-EQ tab hosts the panel via `ContentControl` (ViewLocator → `SNDrumCompEqPanelView`), which has the selector + the selected unit's comp + EQ sections (using the new `ValueSlider`).

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if the Debug exe is locked.

---

## File Structure

- **Create** `Src/ViewModels/SNDrumCompEqUnitViewModel.cs` — one comp/EQ unit's 14 params.
- **Create** `Src/ViewModels/SNDrumCompEqPanelViewModel.cs` — 6 units + `SelectedUnit`.
- **Modify** `Src/ViewModels/SNDrumKitEditorViewModel.cs` — add the `CompEq` panel.
- **Create** `Src/Views/SNDrumCompEqPanelView.axaml` (+ `.axaml.cs`).
- **Modify** `Src/Views/SNDrumKitEditorView.axaml` — Comp-EQ tab hosts `CompEq`.

---

## Task 1: Comp-EQ view-models + kit wiring

**Files:**
- Create: `Src/ViewModels/SNDrumCompEqUnitViewModel.cs`
- Create: `Src/ViewModels/SNDrumCompEqPanelViewModel.cs`
- Modify: `Src/ViewModels/SNDrumKitEditorViewModel.cs`

- [ ] **Step 1: Create `Src/ViewModels/SNDrumCompEqUnitViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One SN-Drums Comp-EQ unit (1..6): a compressor + a 3-band EQ over the shared Comp-EQ domain.</summary>
public sealed class SNDrumCompEqUnitViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Common Comp-EQ/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 1..6
    public string Title => $"Unit {Index}";

    // Compressor
    public ParamBool CompSwitch { get; }
    public ParamString CompAttack { get; }   // ms (enum)
    public ParamString CompRelease { get; }  // ms (enum)
    public ParamInt CompThreshold { get; }   // 0..127
    public ParamString CompRatio { get; }
    public ParamInt CompGain { get; }        // 0..24 dB

    // EQ
    public ParamBool EqSwitch { get; }
    public ParamString EqLowFreq { get; }    // 200 / 400 Hz
    public ParamInt EqLowGain { get; }       // -15..15 dB
    public ParamString EqMidFreq { get; }    // Hz (enum)
    public ParamInt EqMidGain { get; }       // -15..15 dB
    public ParamString EqMidQ { get; }
    public ParamString EqHighFreq { get; }   // Hz (enum)
    public ParamInt EqHighGain { get; }      // -15..15 dB

    public SNDrumCompEqUnitViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var c = $"Comp{index} ";
        var q = $"EQ{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[PP + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[PP + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[PP + n], writer));

        CompSwitch = PB(c + "Switch");
        CompAttack = PS(c + "Attack Time");
        CompRelease = PS(c + "Release Time");
        CompThreshold = PI(c + "Threshold", 0, 127);
        CompRatio = PS(c + "Ratio");
        CompGain = PI(c + "Output Gain", 0, 24);

        EqSwitch = PB(q + "Switch");
        EqLowFreq = PS(q + "Low Freq", new[] { "200", "400" });
        EqLowGain = PI(q + "Low Gain", -15, 15);
        EqMidFreq = PS(q + "Mid Freq");
        EqMidGain = PI(q + "Mid Gain", -15, 15);
        EqMidQ = PS(q + "Mid Q");
        EqHighFreq = PS(q + "High Freq");
        EqHighGain = PI(q + "High Gain", -15, 15);
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Create `Src/ViewModels/SNDrumCompEqPanelViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The kit's Comp-EQ section: six compressor/EQ units, one shown at a time.</summary>
public sealed class SNDrumCompEqPanelViewModel : ViewModelBase, IDisposable
{
    private const int UnitCount = 6;
    private readonly List<IDisposable> _wrappers = [];

    public IReadOnlyList<SNDrumCompEqUnitViewModel> Units { get; }

    private SNDrumCompEqUnitViewModel _selectedUnit;
    public SNDrumCompEqUnitViewModel SelectedUnit
    {
        get => _selectedUnit;
        set { if (ReferenceEquals(value, _selectedUnit) || value is null) return; this.RaiseAndSetIfChanged(ref _selectedUnit, value); }
    }

    public SNDrumCompEqPanelViewModel(DomainBase compEqDomain, ThrottledParameterWriter writer)
    {
        var byPath = ToDict(compEqDomain);
        var units = new List<SNDrumCompEqUnitViewModel>(UnitCount);
        for (var i = 1; i <= UnitCount; i++) units.Add(Track(new SNDrumCompEqUnitViewModel(compEqDomain, byPath, writer, i)));
        Units = units;
        _selectedUnit = units[0];
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 3: Wire `CompEq` into `Src/ViewModels/SNDrumKitEditorViewModel.cs`.**

(a) Add the property next to `Mfx` (after `public MfxPanelViewModel Mfx { get; }`):

```csharp
    public SNDrumCompEqPanelViewModel CompEq { get; }
```

(b) In the constructor, after the `Mfx = new MfxPanelViewModel(...)` statement (it ends with `() => _navigateToRawTab?.Invoke("SN-D-MFX", null));`), add:

```csharp

        CompEq = new SNDrumCompEqPanelViewModel(domain.SNDrumKitCompEQ(partNo), _writer);
```

(c) In `Dispose()`, after `Mfx.Dispose();`, add:

```csharp
        CompEq.Dispose();
```

- [ ] **Step 4: Build** — Expected: succeeded.
- [ ] **Step 5: Run tests** — Expected: PASS (237).
- [ ] **Step 6: Commit**

```
git add Src/ViewModels/SNDrumCompEqUnitViewModel.cs Src/ViewModels/SNDrumCompEqPanelViewModel.cs Src/ViewModels/SNDrumKitEditorViewModel.cs
git commit -m "feat: SN-Drums Comp-EQ view-models (6 units) + kit wiring"
```

---

## Task 2: SNDrumCompEqPanelView + Comp-EQ tab

**Files:**
- Create: `Src/Views/SNDrumCompEqPanelView.axaml` (+ `.axaml.cs`)
- Modify: `Src/Views/SNDrumKitEditorView.axaml`

- [ ] **Step 1: Create `Src/Views/SNDrumCompEqPanelView.axaml.cs`:**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class SNDrumCompEqPanelView : UserControl
{
    public SNDrumCompEqPanelView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Create `Src/Views/SNDrumCompEqPanelView.axaml`:**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:SNDrumCompEqPanelViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.SNDrumCompEqPanelView">
    <StackPanel Spacing="12" Margin="0,0,0,8">

        <!-- Unit selector -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="Comp / EQ unit" VerticalAlignment="Center"/>
                <ComboBox ItemsSource="{Binding Units}" SelectedItem="{Binding SelectedUnit, Mode=TwoWay}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:SNDrumCompEqUnitViewModel">
                            <TextBlock Text="{Binding Title}"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
            </StackPanel>
        </Border>

        <!-- Compressor (bound to the selected unit) -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10"
                DataContext="{Binding SelectedUnit}" x:DataType="vm:SNDrumCompEqUnitViewModel">
            <StackPanel Spacing="8">
                <DockPanel>
                    <TextBlock Text="Compressor" FontWeight="Bold"/>
                    <ToggleSwitch DockPanel.Dock="Right" OnContent="On" OffContent="Off"
                                  IsChecked="{Binding CompSwitch.Value, Mode=TwoWay}"/>
                </DockPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="Ratio" ToolTip.Tip="Compressor Ratio"/>
                        <ComboBox ItemsSource="{Binding CompRatio.Options}" SelectedItem="{Binding CompRatio.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Attack" ToolTip.Tip="Attack Time"/>
                        <ComboBox ItemsSource="{Binding CompAttack.Options}" SelectedItem="{Binding CompAttack.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Release" ToolTip.Tip="Release Time"/>
                        <ComboBox ItemsSource="{Binding CompRelease.Options}" SelectedItem="{Binding CompRelease.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2" Width="200">
                        <TextBlock Text="Threshold"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding CompThreshold.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="200">
                        <TextBlock Text="Output Gain"/>
                        <controls:ValueSlider Minimum="0" Maximum="24" Unit="dB" Value="{Binding CompGain.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- EQ (bound to the selected unit) -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10"
                DataContext="{Binding SelectedUnit}" x:DataType="vm:SNDrumCompEqUnitViewModel">
            <StackPanel Spacing="8">
                <DockPanel>
                    <TextBlock Text="EQ (3-band)" FontWeight="Bold"/>
                    <ToggleSwitch DockPanel.Dock="Right" OnContent="On" OffContent="Off"
                                  IsChecked="{Binding EqSwitch.Value, Mode=TwoWay}"/>
                </DockPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="Low Freq (Hz)"/>
                        <ComboBox ItemsSource="{Binding EqLowFreq.Options}" SelectedItem="{Binding EqLowFreq.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="200">
                        <TextBlock Text="Low Gain"/>
                        <controls:ValueSlider Minimum="-15" Maximum="15" Unit="dB" Value="{Binding EqLowGain.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="Mid Freq (Hz)"/>
                        <ComboBox ItemsSource="{Binding EqMidFreq.Options}" SelectedItem="{Binding EqMidFreq.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Mid Q"/>
                        <ComboBox ItemsSource="{Binding EqMidQ.Options}" SelectedItem="{Binding EqMidQ.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="200">
                        <TextBlock Text="Mid Gain"/>
                        <controls:ValueSlider Minimum="-15" Maximum="15" Unit="dB" Value="{Binding EqMidGain.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="High Freq (Hz)"/>
                        <ComboBox ItemsSource="{Binding EqHighFreq.Options}" SelectedItem="{Binding EqHighFreq.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="200">
                        <TextBlock Text="High Gain"/>
                        <controls:ValueSlider Minimum="-15" Maximum="15" Unit="dB" Value="{Binding EqHighGain.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Host it in the Comp-EQ tab.** In `Src/Views/SNDrumKitEditorView.axaml`, find:

```xml
                <TabItem Header="Comp-EQ">
                    <TextBlock Margin="12" Opacity="0.6" Text="Comp-EQ — 6-unit compressor / EQ (Phase 3)."/>
                </TabItem>
```

Replace with:

```xml
                <TabItem Header="Comp-EQ">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                        <ContentControl Content="{Binding CompEq}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
```

- [ ] **Step 4: Build** — Expected: succeeded. (ViewLocator resolves `SNDrumCompEqPanelViewModel`→`SNDrumCompEqPanelView`; `ValueSlider` + `SnPanelBackgroundBrush` resolve.)
- [ ] **Step 5: Run tests** — Expected: PASS (237).
- [ ] **Step 6: Commit**

```
git add Src/Views/SNDrumCompEqPanelView.axaml Src/Views/SNDrumCompEqPanelView.axaml.cs Src/Views/SNDrumKitEditorView.axaml
git commit -m "feat: SN-Drums Comp-EQ tab (6-unit comp + 3-band EQ)"
```

---

## Done criteria

- Full suite green (237).
- The Comp-EQ tab shows a unit selector (1–6); picking a unit shows its compressor (On/Ratio/Attack/Release/Threshold/Output Gain) and 3-band EQ (On + Low/Mid/High freq + gain + Mid Q). Edits write to the kit.
- **This completes the SuperNATURAL Drums friendly editor** (Header, note rail with click-to-play, Drum, Comp-EQ, FX, Advanced).
