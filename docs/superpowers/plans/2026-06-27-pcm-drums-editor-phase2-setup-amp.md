# PCM Drums Editor — Phase 2 (per-key editor: Setup + Amp) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each PCM drum key a per-key editor under the Drum tab — a sub-tabbed panel (Wave · Pitch · Filter · Amp · Setup) where Phase 2 fills the **Setup** (identity & routing) and **Amp** (TVA: level, velocity, envelope) tabs and stubs the other three, plus per-key Copy/Paste/Init. Selecting a rail key rebuilds this editor.

**Architecture:** Mirrors the merged SN-Drums per-note editor and the PCM Synth TVA envelope. A new `PCMDrumNoteEditorViewModel` wraps the relevant `PCM Drum Kit Partial/` parameters with `ParamInt/String/Bool`; the kit VM gains `SelectedDrumEditor` + `RebuildDrumEditor()` + a shared `DrumClipboard`, rebuilding the editor on selection. The view organizes the params into a sub-`TabControl`; the TVA envelope reuses `MultiStageEnvelopeControl`. Copy/Paste/Init reuse the engine-agnostic `SnsPartialClipboard`.

**Tech Stack:** Avalonia 12 + ReactiveUI, `ParamInt/String/Bool` over `ThrottledParameterWriter`, `MultiStageEnvelopeControl`, `ValueSlider`, `SnsPartialClipboard`, `SnPanelBackgroundBrush`.

**Parameter facts (verified against `ParameterDefinitions.cs`).** All paths are prefixed `PCM Drum Kit Partial/`. A `ParamInt` slider's `(min,max)` equals the param's display range (`omin`,`omax`); params with a non-null `repr` are enums → use `ParamString` (combo) or `ParamBool` (OFF/ON):

Setup:
| Param | Type | Range / enum |
|---|---|---|
| `Partial Name` | ParamString (no options) | ASC text |
| `Assign Type` | ParamString combo | MULTI/SINGLE |
| `Mute Group` | ParamString combo | OFF,1..31 |
| `Partial Env Mode` | ParamString combo | NO-SUS/SUS |
| `One Shot Mode` | ParamBool | OFF/ON |
| `Partial Pitch Bend Range` | ParamInt | 0..48 |
| `Partial Receive Expression` | ParamBool | OFF/ON |
| `Partial Receive Sustain` | ParamBool | OFF/ON |
| `Partial Pan` | ParamInt | -64..63 |
| `Partial Coarse Tune` | ParamInt | 0..127 |
| `Partial Fine Tune` | ParamInt | -50..50 |
| `Partial Random Pitch Depth` | ParamString combo | RND_PITCH_DEPTH |
| `Partial Random Pan Depth` | ParamInt | 0..63 |
| `Partial Alternate Pan Depth` | ParamInt | -63..63 |
| `Partial Output Assign` | ParamString combo | PARTIAL_OUTPUT_ASSIGN |
| `Partial Output Level` | ParamInt | 0..127 |
| `Partial Chorus Send Level` | ParamInt | 0..127 |
| `Partial Reverb Send Level` | ParamInt | 0..127 |

Amp (TVA):
| Param | Type | Range / enum |
|---|---|---|
| `Partial Level` | ParamInt | 0..127 |
| `TVA Velocity Curve` | ParamString combo | TVF_VELOCITY_CURVE |
| `TVA Velocity Sens` | ParamInt | -63..63 |
| `TVA Env Time 1 Velocity Sens` | ParamInt | -63..63 |
| `TVA Env Time 4 Velocity Sens` | ParamInt | -63..63 |
| `TVA Env Time 1..4` | ParamInt | 0..127 |
| `TVA Env Level 1..3` | ParamInt | 0..127 |

The TVA envelope is 3-level/4-time releasing to silence: `MultiStageEnvelopeControl FixedEndpoints="True"`, `Level0="0"`, `Level1..3` bound, `Level4` left at its 0 default (pinned by FixedEndpoints), `Time1..4` bound (mirrors the PCM Synth TVA envelope exactly).

**Testing note:** Phase 2 is view-model/view glue over live FQP instances (no new pure helper to unit-test — TDD-style tests resume in the `WmtVelocityMapping` phase). Each task verifies by **Release build succeeding** and the **full 237-test suite staying green**. Build/test use the user-local .NET 10 SDK in Release (Debug exe is file-locked; MSB3027/MSB3021 = lock, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: Per-key editor view-model

**Files:**
- Create: `Src/ViewModels/PCMDrumNoteEditorViewModel.cs`

Wraps the Setup + Amp params, exposes Copy/Paste/Init over the parent's shared clipboard. Wave/Pitch/Filter params are added in later phases.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Editor for ONE PCM drum key (the Drum tab): Setup (identity/routing) + Amp (TVA).
/// Built fresh for the selected rail note. Wave/Pitch/Filter tabs are filled in later phases.</summary>
public sealed partial class PCMDrumNoteEditorViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly PCMDrumKitEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    // --- Setup (identity & routing) ---
    public ParamString PartialName { get; }
    public ParamString AssignType { get; }
    public ParamString MuteGroup { get; }
    public ParamString EnvMode { get; }
    public ParamBool OneShot { get; }
    public ParamInt PitchBendRange { get; }
    public ParamBool ReceiveExpression { get; }
    public ParamBool ReceiveSustain { get; }
    public ParamInt Pan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamString RandomPitchDepth { get; }
    public ParamInt RandomPanDepth { get; }
    public ParamInt AlternatePanDepth { get; }
    public ParamString OutputAssign { get; }
    public ParamInt OutputLevel { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    // --- Amp (TVA) ---
    public ParamInt Level { get; }
    public ParamString TvaVeloCurve { get; }
    public ParamInt TvaVeloSens { get; }
    public ParamInt TvaEnvTime1VeloSens { get; }
    public ParamInt TvaEnvTime4VeloSens { get; }
    public ParamInt TvaEnvTime1 { get; }
    public ParamInt TvaEnvTime2 { get; }
    public ParamInt TvaEnvTime3 { get; }
    public ParamInt TvaEnvTime4 { get; }
    public ParamInt TvaEnvLevel1 { get; }
    public ParamInt TvaEnvLevel2 { get; }
    public ParamInt TvaEnvLevel3 { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public PCMDrumNoteEditorViewModel(PCMDrumKitEditorViewModel parent, DomainBase partialDomain,
        ThrottledParameterWriter writer)
    {
        _parent = parent;
        var byPath = ToDict(partialDomain);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n) => Track(new ParamString(partialDomain, byPath[PP + n], writer));
        ParamBool PB(string n) => Track(new ParamBool(partialDomain, byPath[PP + n], writer));

        // Setup
        PartialName = PS("Partial Name");
        AssignType = PS("Assign Type");
        MuteGroup = PS("Mute Group");
        EnvMode = PS("Partial Env Mode");
        OneShot = PB("One Shot Mode");
        PitchBendRange = PI("Partial Pitch Bend Range", 0, 48);
        ReceiveExpression = PB("Partial Receive Expression");
        ReceiveSustain = PB("Partial Receive Sustain");
        Pan = PI("Partial Pan", -64, 63);
        CoarseTune = PI("Partial Coarse Tune", 0, 127);
        FineTune = PI("Partial Fine Tune", -50, 50);
        RandomPitchDepth = PS("Partial Random Pitch Depth");
        RandomPanDepth = PI("Partial Random Pan Depth", 0, 63);
        AlternatePanDepth = PI("Partial Alternate Pan Depth", -63, 63);
        OutputAssign = PS("Partial Output Assign");
        OutputLevel = PI("Partial Output Level", 0, 127);
        ChorusSend = PI("Partial Chorus Send Level", 0, 127);
        ReverbSend = PI("Partial Reverb Send Level", 0, 127);

        // Amp (TVA)
        Level = PI("Partial Level", 0, 127);
        TvaVeloCurve = PS("TVA Velocity Curve");
        TvaVeloSens = PI("TVA Velocity Sens", -63, 63);
        TvaEnvTime1VeloSens = PI("TVA Env Time 1 Velocity Sens", -63, 63);
        TvaEnvTime4VeloSens = PI("TVA Env Time 4 Velocity Sens", -63, 63);
        TvaEnvTime1 = PI("TVA Env Time 1", 0, 127);
        TvaEnvTime2 = PI("TVA Env Time 2", 0, 127);
        TvaEnvTime3 = PI("TVA Env Time 3", 0, 127);
        TvaEnvTime4 = PI("TVA Env Time 4", 0, 127);
        TvaEnvLevel1 = PI("TVA Env Level 1", 0, 127);
        TvaEnvLevel2 = PI("TVA Env Level 2", 0, 127);
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);

        _editable = new IParam[]
        {
            PartialName, AssignType, MuteGroup, EnvMode, OneShot, PitchBendRange,
            ReceiveExpression, ReceiveSustain, Pan, CoarseTune, FineTune, RandomPitchDepth,
            RandomPanDepth, AlternatePanDepth, OutputAssign, OutputLevel, ChorusSend, ReverbSend,
            Level, TvaVeloCurve, TvaVeloSens, TvaEnvTime1VeloSens, TvaEnvTime4VeloSens,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4, TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
        };
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    // Copy / Paste / Init (shared clipboard lives on the parent kit editor).
    [ReactiveCommand] public void CopyDrum() => _parent.DrumClipboard = SnsPartialClipboard.Snapshot(_editable);
    [ReactiveCommand] public void PasteDrum() { if (_parent.DrumClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    [ReactiveCommand] public void InitDrum() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset of the continuous tweaks (leaves the envelope, enums, name, velocity sens).
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Partial Level"] = "100",
        [PP + "Partial Pan"] = "0",
        [PP + "Partial Coarse Tune"] = "64",
        [PP + "Partial Fine Tune"] = "0",
        [PP + "Partial Output Level"] = "127",
        [PP + "Partial Chorus Send Level"] = "0",
        [PP + "Partial Reverb Send Level"] = "0",
        [PP + "Partial Alternate Pan Depth"] = "0",
        [PP + "Partial Random Pan Depth"] = "0",
    };

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (`[ReactiveCommand]` generates `CopyDrumCommand`/`PasteDrumCommand`/`InitDrumCommand`. The file is unused until Task 3 references it.) If a `ParamInt`/`ParamString`/`ParamBool` constructor or a parameter path mismatches and the build fails, STOP and report BLOCKED with the exact error rather than altering the design.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PCMDrumNoteEditorViewModel.cs
git commit -m "feat: PCM-Drums per-key editor view-model (Setup + Amp)"
```

---

### Task 2: Per-key editor view (sub-tabs)

**Files:**
- Create: `Src/Views/PCMDrumNoteEditorView.axaml`
- Create: `Src/Views/PCMDrumNoteEditorView.axaml.cs`

A Copy/Paste/Init button row, then a sub-`TabControl` (Wave · Pitch · Filter · Amp · Setup). Wave/Pitch/Filter are placeholders for Phases 3–4. All sliders are `ValueSlider`; the TVA envelope is `MultiStageEnvelopeControl` plus `NumericUpDown` precise-entry spinboxes (mirroring the PCM Synth editor). No hardcoded colors.

- [ ] **Step 1: Create `PCMDrumNoteEditorView.axaml.cs`**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class PCMDrumNoteEditorView : UserControl
{
    public PCMDrumNoteEditorView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Create `PCMDrumNoteEditorView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:PCMDrumNoteEditorViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.PCMDrumNoteEditorView">
    <UserControl.Styles>
        <Style Selector="TextBlock.sliderLabel">
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="MinHeight" Value="34"/>
        </Style>
    </UserControl.Styles>

    <StackPanel Spacing="10" Margin="0,0,0,8">

        <!-- Per-key copy/paste/init (applies to the whole drum key) -->
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Copy drum" Command="{Binding CopyDrumCommand}"/>
            <Button Content="Paste drum" Command="{Binding PasteDrumCommand}"/>
            <Button Content="Init drum" Command="{Binding InitDrumCommand}"/>
        </StackPanel>

        <TabControl>
            <!-- Wave / Pitch / Filter: filled in Phases 3–4 -->
            <TabItem Header="Wave">
                <TextBlock Margin="12" Opacity="0.6" Text="Wave (WMT) — velocity-layered waves (Phase 3)."/>
            </TabItem>
            <TabItem Header="Pitch">
                <TextBlock Margin="12" Opacity="0.6" Text="Pitch envelope (Phase 4)."/>
            </TabItem>
            <TabItem Header="Filter">
                <TextBlock Margin="12" Opacity="0.6" Text="Filter / TVF (Phase 4)."/>
            </TabItem>

            <!-- Amp (TVA) -->
            <TabItem Header="Amp">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <StackPanel Spacing="12" Margin="0,8,0,8">
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Amplifier (TVA)" FontWeight="Bold"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Level" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding Level.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvaVeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Velocity Curve" Classes="sliderLabel"/>
                                        <ComboBox HorizontalAlignment="Stretch"
                                                  ItemsSource="{Binding TvaVeloCurve.Options}"
                                                  SelectedItem="{Binding TvaVeloCurve.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>

                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Amp envelope (TVA)" FontWeight="Bold"/>
                                <controls:MultiStageEnvelopeControl Height="120" FixedEndpoints="True"
                                    Time1="{Binding TvaEnvTime1.Value, Mode=TwoWay}"
                                    Time2="{Binding TvaEnvTime2.Value, Mode=TwoWay}"
                                    Time3="{Binding TvaEnvTime3.Value, Mode=TwoWay}"
                                    Time4="{Binding TvaEnvTime4.Value, Mode=TwoWay}"
                                    Level0="0"
                                    Level1="{Binding TvaEnvLevel1.Value, Mode=TwoWay}"
                                    Level2="{Binding TvaEnvLevel2.Value, Mode=TwoWay}"
                                    Level3="{Binding TvaEnvLevel3.Value, Mode=TwoWay}"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Times (T1–T4)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime3.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime4.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Levels (L1–L3)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel3.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="200" Margin="0,0,16,8">
                                        <TextBlock Text="T1 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvaEnvTime1VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="200" Margin="0,0,16,8">
                                        <TextBlock Text="T4 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvaEnvTime4VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <!-- Setup (identity & routing) -->
            <TabItem Header="Setup">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <StackPanel Spacing="12" Margin="0,8,0,8">
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Identity" FontWeight="Bold"/>
                                <StackPanel Spacing="2" Width="320">
                                    <TextBlock Text="Name" ToolTip.Tip="Partial Name"/>
                                    <TextBox Text="{Binding PartialName.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Assign Type"/>
                                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding AssignType.Options}" SelectedItem="{Binding AssignType.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Mute Group"/>
                                        <ComboBox MaxDropDownHeight="300" HorizontalAlignment="Stretch" ItemsSource="{Binding MuteGroup.Options}" SelectedItem="{Binding MuteGroup.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Env Mode" ToolTip.Tip="Partial Env Mode"/>
                                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding EnvMode.Options}" SelectedItem="{Binding EnvMode.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,16,8">
                                        <TextBlock Text="One Shot" ToolTip.Tip="One Shot Mode"/>
                                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding OneShot.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,16,8">
                                        <TextBlock Text="Rx Expression" ToolTip.Tip="Partial Receive Expression"/>
                                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding ReceiveExpression.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,16,8">
                                        <TextBlock Text="Rx Sustain" ToolTip.Tip="Partial Receive Sustain"/>
                                        <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding ReceiveSustain.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>

                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Pitch &amp; pan" FontWeight="Bold"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Coarse Tune" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding CoarseTune.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Fine Tune" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-50" Maximum="50" Value="{Binding FineTune.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Pitch Bend Range" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="48" Value="{Binding PitchBendRange.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="200" Margin="0,0,16,8">
                                        <TextBlock Text="Random Pitch Depth" Classes="sliderLabel"/>
                                        <ComboBox MaxDropDownHeight="300" HorizontalAlignment="Stretch" ItemsSource="{Binding RandomPitchDepth.Options}" SelectedItem="{Binding RandomPitchDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Pan (L / C / R)" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-64" Maximum="63" Value="{Binding Pan.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Random Pan Depth" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="63" Value="{Binding RandomPanDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Alternate Pan Depth" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding AlternatePanDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>

                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Output" FontWeight="Bold"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="200" Margin="0,0,16,8">
                                        <TextBlock Text="Output Assign" Classes="sliderLabel"/>
                                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding OutputAssign.Options}" SelectedItem="{Binding OutputAssign.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Output Level" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding OutputLevel.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Chorus Send" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding ChorusSend.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Reverb Send" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding ReverbSend.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
        </TabControl>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (Compiled XAML validates every binding path against `PCMDrumNoteEditorViewModel`; a wrong path fails the build. A genuine AVLN/XAML error IS a real failure — report it.)

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/PCMDrumNoteEditorView.axaml Src/Views/PCMDrumNoteEditorView.axaml.cs
git commit -m "feat: PCM-Drums per-key editor view (Setup + Amp tabs, sub-tab shell)"
```

---

### Task 3: Wire the per-key editor into the kit VM + Drum tab

**Files:**
- Modify: `Src/ViewModels/PCMDrumKitEditorViewModel.cs`
- Modify: `Src/Views/PCMDrumKitEditorView.axaml` (Drum tab body)

Add `SelectedDrumEditor` + `RebuildDrumEditor()` + the shared `DrumClipboard`; rebuild on selection and at construction; dispose properly. Point the Drum tab at the editor.

- [ ] **Step 1: Add the `SelectedDrumEditor` / `DrumClipboard` members and rebuild logic**

In `Src/ViewModels/PCMDrumKitEditorViewModel.cs`, find the `SelectedNote` setter:

```csharp
    private PCMDrumNoteViewModel? _selectedNote;
    public PCMDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(value, _selectedNote)) return;
            this.RaiseAndSetIfChanged(ref _selectedNote, value);
        }
    }
```

Replace it with (adds the rebuild call + the editor/clipboard members directly after):

```csharp
    private PCMDrumNoteViewModel? _selectedNote;
    public PCMDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(value, _selectedNote)) return;
            this.RaiseAndSetIfChanged(ref _selectedNote, value);
            RebuildDrumEditor();
        }
    }

    private PCMDrumNoteEditorViewModel? _selectedDrumEditor;
    /// <summary>The Drum-tab editor for the selected note (rebuilt on each selection change).</summary>
    public PCMDrumNoteEditorViewModel? SelectedDrumEditor
    {
        get => _selectedDrumEditor;
        private set => this.RaiseAndSetIfChanged(ref _selectedDrumEditor, value);
    }

    /// <summary>Shared drum copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? DrumClipboard { get; set; }

    private void RebuildDrumEditor()
    {
        _selectedDrumEditor?.Dispose();
        SelectedDrumEditor = _selectedNote is { } n
            ? new PCMDrumNoteEditorViewModel(this, n.PartialDomain, _writer)
            : null;
    }
```

- [ ] **Step 2: Build the editor for the initial selection**

In the constructor, find this line:

```csharp
        _selectedNote = Notes.Count > 0 ? Notes[0] : null;
```

Add immediately after it:

```csharp
        RebuildDrumEditor();
```

- [ ] **Step 3: Dispose the per-key editor**

In `Dispose()`, find:

```csharp
    public void Dispose()
    {
        _kitName.PropertyChanged -= OnKitNameChanged;
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
```

Replace with (adds `_selectedDrumEditor` disposal before the wrappers):

```csharp
    public void Dispose()
    {
        _kitName.PropertyChanged -= OnKitNameChanged;
        _selectedDrumEditor?.Dispose();
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
```

- [ ] **Step 4: Point the Drum tab at the editor**

In `Src/Views/PCMDrumKitEditorView.axaml`, find the Drum `TabItem`:

```xml
                <TabItem Header="Drum">
                    <TextBlock Margin="12" Opacity="0.6"
                               Text="{Binding SelectedNote.NoteName, StringFormat='Per-key editor for {0} — coming in Phase 2.'}"/>
                </TabItem>
```

Replace it with:

```xml
                <TabItem Header="Drum">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                        <ContentControl Content="{Binding SelectedDrumEditor}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
```

(The ViewLocator resolves `PCMDrumNoteEditorViewModel` → `PCMDrumNoteEditorView` by name.)

- [ ] **Step 5: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors.

- [ ] **Step 6: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/PCMDrumKitEditorViewModel.cs Src/Views/PCMDrumKitEditorView.axaml
git commit -m "feat: wire PCM-Drums per-key editor into the Drum tab"
```

---

## Done criteria

Selecting a PCM drum key shows a per-key editor under the Drum tab with five sub-tabs: **Wave/Pitch/Filter** placeholders, a working **Amp** tab (Level, Velocity Sens/Curve, a draggable 3-level/4-time TVA envelope with precise spinboxes + T1/T4 velocity sens), and a working **Setup** tab (Name, Assign Type, Mute Group, Env Mode, One-Shot, Rx Expression/Sustain, tune/pan group, output group). Copy/Paste/Init buttons copy a key's Phase-2 params to another key. Switching keys rebuilds the editor; the old one is disposed. Build green, 237 tests passing. Phase 3 adds the Wave/WMT tab.
```
