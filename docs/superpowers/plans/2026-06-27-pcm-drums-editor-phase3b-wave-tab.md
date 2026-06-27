# PCM Drums Editor — Phase 3b (Wave/WMT tab wiring) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the per-key editor's **Wave** tab — four velocity-switched WMT layers shown on the `WmtVelocityMapControl` (from Phase 3a), a WMT Velocity Control selector, a WMT 1–4 selector, and a per-layer detail panel with the searchable wave picker + tweaks.

**Architecture:** A new `PCMDrumWmtLayerViewModel` wraps one WMT layer's params (mirrors `PCMPartialViewModel`'s wave handling). `PCMDrumNoteEditorViewModel` gains the four layers (`Wmt1..4` + `Wmts`), the shared `WmtVelocityControl`, and `SelectedWmtIndex`/`SelectedWmt`; all WMT params join `_editable` so Copy captures them. The Wave tab binds the velocity map to the four layers and hosts the selected layer's detail view (`PCMDrumWmtLayerView`) via the ViewLocator.

**Tech Stack:** Avalonia 12 + ReactiveUI, `ParamInt/String/Bool`, `WmtVelocityMapControl` (Phase 3a), `AutoCompleteBox` searchable picker + browse arrow (mirrors `PCMSynthToneEditorView`). Build/test with the user-local SDK in Release. Never use `--no-verify`.

**Parameter facts (verified).** All paths `PCM Drum Kit Partial/`. Shared: `WMT Velocity Control` (repr OFF_ON_RANDOM → ParamString). Per layer n (1..4), prefix `WMT{n} `:
| Leaf | Type | Range / source |
|---|---|---|
| `Wave Switch` | ParamBool | OFF/ON |
| `Wave Group Type` | ParamString | INT_SRX_RES (auto options) |
| `Wave Number L (Mono)` | ParamString | PARTIAL_WAVEFORMS names (auto, from Phase 3a) |
| `Wave Number R` | ParamString | PARTIAL_WAVEFORMS names (auto) |
| `Wave Gain` | ParamString | explicit options `["-6","0","6","12"]` |
| `Wave Level` | ParamInt | 0..127 |
| `Wave Pan` | ParamInt | -64..63 |
| `Wave Coarse Tune` | ParamInt | -48..48 |
| `Wave Fine Tune` | ParamInt | -50..50 |
| `Wave FXM Switch` | ParamBool | OFF/ON |
| `Wave FXM Color` | ParamInt | 1..4 |
| `Wave FXM Depth` | ParamInt | 0..16 |
| `Wave Tempo Sync` | ParamBool | OFF/ON |
| `Random Pan Switch` | ParamBool | OFF/ON |
| `Alternate Pan Switch` | ParamString | OFF_ON_REVERSE (auto options) |
| `Velocity Range Lower` | ParamInt | 0..127 |
| `Velocity Range Upper` | ParamInt | 0..127 |
| `Velocity Fade Width Lower` | ParamInt | 0..127 |
| `Velocity Fade Width Upper` | ParamInt | 0..127 |

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: WMT layer view-model

**Files:**
- Create: `Src/ViewModels/PCMDrumWmtLayerViewModel.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One WMT layer (1..4) of a PCM drum key: a velocity-switched wave plus its tweaks and
/// velocity range. Mirrors the PCM Synth partial's wave handling.</summary>
public sealed class PCMDrumWmtLayerViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }                 // 1..4
    public string Title => $"WMT{Index}";

    public ParamBool WaveSwitch { get; }
    public ParamString WaveGroupType { get; }
    public ParamString WaveNumberL { get; }
    public ParamString WaveNumberR { get; }
    public ParamString WaveGain { get; }
    public ParamInt Level { get; }
    public ParamInt Pan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamBool FxmSwitch { get; }
    public ParamInt FxmColor { get; }
    public ParamInt FxmDepth { get; }
    public ParamBool TempoSync { get; }
    public ParamBool RandomPanSwitch { get; }
    public ParamString AlternatePanSwitch { get; }
    public ParamInt RangeLower { get; }
    public ParamInt RangeUpper { get; }
    public ParamInt FadeLower { get; }
    public ParamInt FadeUpper { get; }

    /// <summary>All wrapped params, for the parent's copy/paste set.</summary>
    public IReadOnlyList<IParam> Params { get; }

    public PCMDrumWmtLayerViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var pre = $"WMT{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[PP + pre + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[PP + pre + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[PP + pre + n], writer));

        WaveSwitch = PB("Wave Switch");
        WaveGroupType = PS("Wave Group Type");
        WaveNumberL = PS("Wave Number L (Mono)");
        WaveNumberR = PS("Wave Number R");
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        Level = PI("Wave Level", 0, 127);
        Pan = PI("Wave Pan", -64, 63);
        CoarseTune = PI("Wave Coarse Tune", -48, 48);
        FineTune = PI("Wave Fine Tune", -50, 50);
        FxmSwitch = PB("Wave FXM Switch");
        FxmColor = PI("Wave FXM Color", 1, 4);
        FxmDepth = PI("Wave FXM Depth", 0, 16);
        TempoSync = PB("Wave Tempo Sync");
        RandomPanSwitch = PB("Random Pan Switch");
        AlternatePanSwitch = PS("Alternate Pan Switch");
        RangeLower = PI("Velocity Range Lower", 0, 127);
        RangeUpper = PI("Velocity Range Upper", 0, 127);
        FadeLower = PI("Velocity Fade Width Lower", 0, 127);
        FadeUpper = PI("Velocity Fade Width Upper", 0, 127);

        Params = new IParam[]
        {
            WaveSwitch, WaveGroupType, WaveNumberL, WaveNumberR, WaveGain, Level, Pan, CoarseTune,
            FineTune, FxmSwitch, FxmColor, FxmDepth, TempoSync, RandomPanSwitch, AlternatePanSwitch,
            RangeLower, RangeUpper, FadeLower, FadeUpper,
        };

        WaveSwitch.PropertyChanged += OnLayerSummaryChanged;
        WaveNumberL.PropertyChanged += OnLayerSummaryChanged;
    }

    /// <summary>Short label for the WMT selector (e.g. "WMT1: 808 Kick" / "WMT1: off").</summary>
    public string Summary => WaveSwitch.Value ? $"{Title}: {WaveNumberL.Value}" : $"{Title}: off";

    private void OnLayerSummaryChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ParamBool.Value) or nameof(ParamString.Value))) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(Summary)));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        WaveSwitch.PropertyChanged -= OnLayerSummaryChanged;
        WaveNumberL.PropertyChanged -= OnLayerSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (Unused until Task 2.) `ParamBool.Value`/`ParamString.Value` are public properties so the `nameof` references resolve. If a param path mismatches and fails the build, STOP and report BLOCKED with the exact error.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PCMDrumWmtLayerViewModel.cs
git commit -m "feat: PCM-Drums WMT layer view-model"
```

---

### Task 2: Add the WMT layers to the per-key editor VM

**Files:**
- Modify: `Src/ViewModels/PCMDrumNoteEditorViewModel.cs`

Add the four layers + `WmtVelocityControl` + `SelectedWmtIndex`/`SelectedWmt`, and fold all WMT params into `_editable`. The layers are `Track`ed, so the existing `Dispose()` loop disposes them.

- [ ] **Step 1: Add the Wave (WMT) properties**

Find this line (the Amp region header comment, near the top of the class):

```csharp
    // --- Amp (TVA) ---
```

Immediately BEFORE it, insert:

```csharp
    // --- Wave (WMT) ---
    public ParamString WmtVelocityControl { get; }
    public PCMDrumWmtLayerViewModel Wmt1 { get; }
    public PCMDrumWmtLayerViewModel Wmt2 { get; }
    public PCMDrumWmtLayerViewModel Wmt3 { get; }
    public PCMDrumWmtLayerViewModel Wmt4 { get; }
    public IReadOnlyList<PCMDrumWmtLayerViewModel> Wmts { get; }

```

- [ ] **Step 2: Add the SelectedWmt members**

Find this line near the end of the class (the Init defaults field declaration):

```csharp
    // Neutral reset of the continuous tweaks (leaves the envelope, enums, name, velocity sens).
```

Immediately BEFORE it, insert:

```csharp
    private int _selectedWmtIndex;
    /// <summary>The WMT layer (0..3) shown in the detail panel; also driven by the velocity map.</summary>
    public int SelectedWmtIndex
    {
        get => _selectedWmtIndex;
        set
        {
            var clamped = value < 0 ? 0 : value > 3 ? 3 : value;
            if (clamped == _selectedWmtIndex) return;
            this.RaiseAndSetIfChanged(ref _selectedWmtIndex, clamped);
            this.RaisePropertyChanged(nameof(SelectedWmt));
        }
    }
    public PCMDrumWmtLayerViewModel SelectedWmt => Wmts[_selectedWmtIndex];

```

- [ ] **Step 3: Construct the layers in the ctor**

Find this line (end of the Amp construction block in the ctor):

```csharp
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);
```

Immediately AFTER it, insert:

```csharp

        // Wave (WMT): four velocity-switched layers + the shared velocity-control mode.
        WmtVelocityControl = PS("WMT Velocity Control");
        var wmts = new PCMDrumWmtLayerViewModel[4];
        for (var i = 0; i < 4; i++) wmts[i] = Track(new PCMDrumWmtLayerViewModel(partialDomain, byPath, writer, i + 1));
        Wmts = wmts;
        Wmt1 = wmts[0]; Wmt2 = wmts[1]; Wmt3 = wmts[2]; Wmt4 = wmts[3];
```

- [ ] **Step 4: Fold the WMT params into `_editable`**

Find this exact block in the ctor:

```csharp
        _editable = new IParam[]
        {
            PartialName, AssignType, MuteGroup, EnvMode, OneShot, PitchBendRange,
            ReceiveExpression, ReceiveSustain, Pan, CoarseTune, FineTune, RandomPitchDepth,
            RandomPanDepth, AlternatePanDepth, OutputAssign, OutputLevel, ChorusSend, ReverbSend,
            Level, TvaVeloCurve, TvaVeloSens, TvaEnvTime1VeloSens, TvaEnvTime4VeloSens,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4, TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
        };
```

Replace it with:

```csharp
        var editable = new List<IParam>
        {
            PartialName, AssignType, MuteGroup, EnvMode, OneShot, PitchBendRange,
            ReceiveExpression, ReceiveSustain, Pan, CoarseTune, FineTune, RandomPitchDepth,
            RandomPanDepth, AlternatePanDepth, OutputAssign, OutputLevel, ChorusSend, ReverbSend,
            Level, TvaVeloCurve, TvaVeloSens, TvaEnvTime1VeloSens, TvaEnvTime4VeloSens,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4, TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
            WmtVelocityControl,
        };
        foreach (var wmt in wmts) editable.AddRange(wmt.Params);
        _editable = editable;
```

- [ ] **Step 5: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (`System.Collections.Generic` is already imported. `Wmts` is assigned before any access to `SelectedWmt`.)

- [ ] **Step 6: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/PCMDrumNoteEditorViewModel.cs
git commit -m "feat: add four WMT layers + selection to PCM-Drums per-key VM"
```

---

### Task 3: WMT layer detail view

**Files:**
- Create: `Src/Views/PCMDrumWmtLayerView.axaml`
- Create: `Src/Views/PCMDrumWmtLayerView.axaml.cs`

The selected layer's detail: wave picker (Bank + searchable Wave L/R + Gain) and tweaks + velocity range. Browse arrows mirror `PCMSynthToneEditorView`.

- [ ] **Step 1: Create `PCMDrumWmtLayerView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Integra7AuralAlchemist.Views;

public partial class PCMDrumWmtLayerView : UserControl
{
    public PCMDrumWmtLayerView()
    {
        InitializeComponent();
    }

    // The wave fields are searchable AutoCompleteBoxes; the browse arrow opens the full list.
    // Clearing Text first drops the filter; ParamString ignores a null/empty assignment.
    private void BrowseWaveL(object? sender, RoutedEventArgs e) => OpenForBrowse(WaveLBox);
    private void BrowseWaveR(object? sender, RoutedEventArgs e) => OpenForBrowse(WaveRBox);

    private static void OpenForBrowse(AutoCompleteBox box)
    {
        box.Text = string.Empty;
        box.Focus();
        box.IsDropDownOpen = true;
    }
}
```

- [ ] **Step 2: Create `PCMDrumWmtLayerView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:PCMDrumWmtLayerViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.PCMDrumWmtLayerView">
    <UserControl.Styles>
        <Style Selector="TextBlock.sliderLabel">
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="MinHeight" Value="34"/>
        </Style>
    </UserControl.Styles>

    <StackPanel Spacing="12" Margin="0,8,0,8">

        <!-- Wave -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <DockPanel>
                    <TextBlock Text="{Binding Title}" FontWeight="Bold"/>
                    <ToggleSwitch DockPanel.Dock="Right" OnContent="On" OffContent="Off"
                                  IsChecked="{Binding WaveSwitch.Value, Mode=TwoWay}"/>
                </DockPanel>
                <WrapPanel Orientation="Horizontal">
                    <StackPanel Spacing="2" Width="150" Margin="0,0,16,8">
                        <TextBlock Text="Bank" Classes="sliderLabel" ToolTip.Tip="Wave Group Type"/>
                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding WaveGroupType.Options}"
                                  SelectedItem="{Binding WaveGroupType.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Margin="0,0,16,8">
                        <TextBlock Text="Wave (type to find)" Classes="sliderLabel" ToolTip.Tip="Wave Number L (Mono)"/>
                        <StackPanel Orientation="Horizontal" Spacing="2">
                            <AutoCompleteBox x:Name="WaveLBox" Width="240" MaxDropDownHeight="320"
                                             MinimumPrefixLength="0" FilterMode="Contains"
                                             ItemsSource="{Binding WaveNumberL.Options}"
                                             SelectedItem="{Binding WaveNumberL.Value, Mode=TwoWay}"/>
                            <Button Content="▾" Padding="8,4" VerticalAlignment="Center"
                                    Click="BrowseWaveL" ToolTip.Tip="Browse all waves"/>
                        </StackPanel>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="150" Margin="0,0,16,8">
                        <TextBlock Text="Gain (dB)" Classes="sliderLabel" ToolTip.Tip="Wave Gain"/>
                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding WaveGain.Options}"
                                  SelectedItem="{Binding WaveGain.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Margin="0,0,16,8">
                        <TextBlock Text="Wave R (type to find)" Classes="sliderLabel" ToolTip.Tip="Wave Number R"/>
                        <StackPanel Orientation="Horizontal" Spacing="2">
                            <AutoCompleteBox x:Name="WaveRBox" Width="240" MaxDropDownHeight="320"
                                             MinimumPrefixLength="0" FilterMode="Contains"
                                             ItemsSource="{Binding WaveNumberR.Options}"
                                             SelectedItem="{Binding WaveNumberR.Value, Mode=TwoWay}"/>
                            <Button Content="▾" Padding="8,4" VerticalAlignment="Center"
                                    Click="BrowseWaveR" ToolTip.Tip="Browse all waves"/>
                        </StackPanel>
                    </StackPanel>
                </WrapPanel>
            </StackPanel>
        </Border>

        <!-- Tweaks -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <TextBlock Text="Tweaks" FontWeight="Bold"/>
                <WrapPanel Orientation="Horizontal">
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Level" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding Level.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Pan (L / C / R)" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="-64" Maximum="63" Value="{Binding Pan.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Coarse Tune" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="-48" Maximum="48" Value="{Binding CoarseTune.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Fine Tune" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="-50" Maximum="50" Value="{Binding FineTune.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Margin="0,0,16,8">
                        <TextBlock Text="Metallic (FXM)" ToolTip.Tip="Wave FXM Switch"/>
                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding FxmSwitch.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="FXM Color" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="1" Maximum="4" Value="{Binding FxmColor.Value, Mode=TwoWay}"
                                              IsEnabled="{Binding FxmSwitch.Value}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="FXM Depth" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="16" Value="{Binding FxmDepth.Value, Mode=TwoWay}"
                                              IsEnabled="{Binding FxmSwitch.Value}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Margin="0,0,16,8">
                        <TextBlock Text="Tempo Sync" ToolTip.Tip="Wave Tempo Sync"/>
                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding TempoSync.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Margin="0,0,16,8">
                        <TextBlock Text="Random Pan" ToolTip.Tip="Random Pan Switch"/>
                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding RandomPanSwitch.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Alternate Pan" Classes="sliderLabel" ToolTip.Tip="Alternate Pan Switch"/>
                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding AlternatePanSwitch.Options}"
                                  SelectedItem="{Binding AlternatePanSwitch.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </WrapPanel>
            </StackPanel>
        </Border>

        <!-- Velocity -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <TextBlock Text="Velocity range" FontWeight="Bold"/>
                <WrapPanel Orientation="Horizontal">
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Range Lower" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding RangeLower.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Range Upper" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding RangeUpper.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Fade Lower" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding FadeLower.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Fade Upper" Classes="sliderLabel"/>
                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding FadeUpper.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </WrapPanel>
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors (compiled XAML validates every binding against `PCMDrumWmtLayerViewModel`). A genuine AVLN/binding error IS a true failure — report it.

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/PCMDrumWmtLayerView.axaml Src/Views/PCMDrumWmtLayerView.axaml.cs
git commit -m "feat: PCM-Drums WMT layer detail view (wave picker + tweaks)"
```

---

### Task 4: Wire the Wave tab (velocity map + selector + detail)

**Files:**
- Modify: `Src/Views/PCMDrumNoteEditorView.axaml` (the Wave `TabItem`)

Replace the Wave placeholder with the velocity map (bound to the four layers), the WMT Velocity Control selector, a WMT 1–4 selector, and the selected-layer detail. The map brushes bind to the existing palette/envelope resources.

- [ ] **Step 1: Replace the Wave placeholder**

In `Src/Views/PCMDrumNoteEditorView.axaml`, find this exact placeholder:

```xml
            <TabItem Header="Wave">
                <TextBlock Margin="12" Opacity="0.6" Text="Wave (WMT) — velocity-layered waves (Phase 3)."/>
            </TabItem>
```

Replace it with:

```xml
            <TabItem Header="Wave">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <StackPanel Spacing="12" Margin="0,8,0,8">
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <DockPanel>
                                    <TextBlock Text="Velocity map" FontWeight="Bold"/>
                                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="6">
                                        <TextBlock Text="Vel control" VerticalAlignment="Center" ToolTip.Tip="WMT Velocity Control"/>
                                        <ComboBox ItemsSource="{Binding WmtVelocityControl.Options}"
                                                  SelectedItem="{Binding WmtVelocityControl.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </DockPanel>
                                <controls:WmtVelocityMapControl Height="180"
                                    SelectedIndex="{Binding SelectedWmtIndex, Mode=TwoWay}"
                                    Wmt1On="{Binding Wmt1.WaveSwitch.Value}" Wmt1Lo="{Binding Wmt1.RangeLower.Value, Mode=TwoWay}" Wmt1Hi="{Binding Wmt1.RangeUpper.Value, Mode=TwoWay}" Wmt1FadeLo="{Binding Wmt1.FadeLower.Value}" Wmt1FadeHi="{Binding Wmt1.FadeUpper.Value}"
                                    Wmt2On="{Binding Wmt2.WaveSwitch.Value}" Wmt2Lo="{Binding Wmt2.RangeLower.Value, Mode=TwoWay}" Wmt2Hi="{Binding Wmt2.RangeUpper.Value, Mode=TwoWay}" Wmt2FadeLo="{Binding Wmt2.FadeLower.Value}" Wmt2FadeHi="{Binding Wmt2.FadeUpper.Value}"
                                    Wmt3On="{Binding Wmt3.WaveSwitch.Value}" Wmt3Lo="{Binding Wmt3.RangeLower.Value, Mode=TwoWay}" Wmt3Hi="{Binding Wmt3.RangeUpper.Value, Mode=TwoWay}" Wmt3FadeLo="{Binding Wmt3.FadeLower.Value}" Wmt3FadeHi="{Binding Wmt3.FadeUpper.Value}"
                                    Wmt4On="{Binding Wmt4.WaveSwitch.Value}" Wmt4Lo="{Binding Wmt4.RangeLower.Value, Mode=TwoWay}" Wmt4Hi="{Binding Wmt4.RangeUpper.Value, Mode=TwoWay}" Wmt4FadeLo="{Binding Wmt4.FadeLower.Value}" Wmt4FadeHi="{Binding Wmt4.FadeUpper.Value}"
                                    Lane1Brush="{StaticResource PmtZone1Brush}" Lane2Brush="{StaticResource PmtZone2Brush}"
                                    Lane3Brush="{StaticResource PmtZone3Brush}" Lane4Brush="{StaticResource PmtZone4Brush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                    LabelBrush="{StaticResource SnEnvelopeHandleBrush}"
                                    HighlightBrush="{StaticResource SnEnvelopeFocusBrush}"/>
                                <StackPanel Orientation="Horizontal" Spacing="6">
                                    <TextBlock Text="Edit layer" VerticalAlignment="Center"/>
                                    <ComboBox ItemsSource="{Binding Wmts}" SelectedIndex="{Binding SelectedWmtIndex, Mode=TwoWay}">
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate x:DataType="vm:PCMDrumWmtLayerViewModel">
                                                <TextBlock Text="{Binding Summary}"/>
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
                                    </ComboBox>
                                </StackPanel>
                            </StackPanel>
                        </Border>
                        <ContentControl Content="{Binding SelectedWmt}"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. Compiled XAML resolves `WmtVelocityMapControl`, the four `Wmt{n}` layer bindings, `SelectedWmtIndex`, `SelectedWmt`, `Wmts`, and every `{StaticResource}` (`PmtZone1..4Brush`, `SnEnvelope*Brush`). If any resource key is missing, the build fails — report the exact missing key.

- [ ] **Step 3: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 4: Commit**

```bash
git add Src/Views/PCMDrumNoteEditorView.axaml
git commit -m "feat: PCM-Drums Wave tab (velocity map + WMT selector + detail)"
```

---

## Done criteria

The per-key editor's **Wave** tab shows the four WMT layers on a velocity map (drag a band edge to set its velocity range; click a lane to select it), a **WMT Velocity Control** selector, an **Edit layer** selector, and the selected layer's detail — a searchable, **named** wave picker (Bank + Wave L/R + Gain) plus tweaks (level, pan, tune, FXM, tempo sync, pan switches) and velocity range/fade. Copy/Paste now captures the WMT params too. Build green, 244 tests passing. This completes Phase 3 (Wave/WMT). Phase 4 adds the Pitch + Filter tabs.
