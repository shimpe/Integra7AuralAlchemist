# PCM Drums Editor — Phase 4 (Pitch + Filter tabs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the per-key editor's **Pitch** and **Filter** sub-tabs — the pitch envelope, and the TVF filter (type, cutoff/resonance, an interactive filter-curve preview, the TVF envelope, and velocity response).

**Architecture:** Extend `PCMDrumNoteEditorViewModel` with the partial's Pitch-Env + TVF params (mirroring `PCMPartialViewModel`), plus derived `FilterCurveMode`/`FilterCurveSteep` (via the existing `PcmTvfRules`) for the `FilterCurveControl`. The two tabs reuse `MultiStageEnvelopeControl` (Pitch = 5-level/4-time **bipolar**, TVF = 5-level/4-time unipolar) and `FilterCurveControl`.

**Tech Stack:** Avalonia 12 + ReactiveUI, `ParamInt/String`, `MultiStageEnvelopeControl`, `FilterCurveControl`, `PcmTvfRules`, `ValueSlider`, `NumericUpDown`. Build/test with the user-local SDK in Release. Never use `--no-verify`.

**Parameter facts (verified against `ParameterDefinitions.cs`).** All paths `PCM Drum Kit Partial/`. `ParamInt(min,max)` = the param's `omin/omax`; non-null-repr params are `ParamString` combos.

Pitch (all `repr:null` except none; envelope is bipolar — Levels are −63..63):
| Param | Type | Range |
|---|---|---|
| `Pitch Env Depth` | ParamInt | -12..12 |
| `Pitch Env Velocity Sens` | ParamInt | -63..63 |
| `Pitch Env Time 1 Velocity Sens` | ParamInt | -63..63 |
| `Pitch Env Time 4 Velocity Sens` | ParamInt | -63..63 |
| `Pitch Env Time 1..4` | ParamInt | 0..127 |
| `Pitch Env Level 0..4` | ParamInt | -63..63 |

Filter (TVF):
| Param | Type | Range / source |
|---|---|---|
| `TVF Filter Type` | ParamString | TVF_FILTER_TYPE (auto options) |
| `TVF Cutoff Frequency` | ParamInt | 0..127 |
| `TVF Cutoff Velocity Curve` | ParamString | TVF_VELOCITY_CURVE |
| `TVF Cutoff Velocity Sens` | ParamInt | -63..63 |
| `TVF Resonance` | ParamInt | 0..127 |
| `TVF Resonance Velocity Sens` | ParamInt | -63..63 |
| `TVF Env Depth` | ParamInt | -63..63 |
| `TVF Env Velocity Curve` | ParamString | TVF_VELOCITY_CURVE |
| `TVF Env Velocity Sens` | ParamInt | -63..63 |
| `TVF Env Time 1 Velocity Sens` | ParamInt | -63..63 |
| `TVF Env Time 4 Velocity Sens` | ParamInt | -63..63 |
| `TVF Env Time 1..4` | ParamInt | 0..127 |
| `TVF Env Level 0..4` | ParamInt | 0..127 |

`TVF Filter Type` shares the `TVF_FILTER_TYPE` repr with PCM Synth, so `PcmTvfRules.CurveMode/CurveSteep` map it to the `FilterCurveControl`'s `Mode`/`Steep` exactly as in `PCMPartialViewModel`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: Add Pitch + TVF params to the per-key editor VM

**Files:**
- Modify: `Src/ViewModels/PCMDrumNoteEditorViewModel.cs`

Six edits. (Testing: VM/view glue — verify by Release build + the 244-test suite staying green.)

- [ ] **Step 1: Add the `System.ComponentModel` using**

Find:
```csharp
using System.Collections.Generic;
```
Add immediately after it:
```csharp
using System.ComponentModel;
```

- [ ] **Step 2: Add the Pitch + Filter property declarations**

Find this exact pair of lines:
```csharp
    public ParamInt TvaEnvLevel3 { get; }

    private readonly IReadOnlyList<IParam> _editable;
```
Replace with:
```csharp
    public ParamInt TvaEnvLevel3 { get; }

    // --- Pitch envelope (5-level / 4-time, bipolar) ---
    public ParamInt PitchEnvDepth { get; }
    public ParamInt PitchEnvVeloSens { get; }
    public ParamInt PitchEnvTime1VeloSens { get; }
    public ParamInt PitchEnvTime4VeloSens { get; }
    public ParamInt PitchEnvTime1 { get; }
    public ParamInt PitchEnvTime2 { get; }
    public ParamInt PitchEnvTime3 { get; }
    public ParamInt PitchEnvTime4 { get; }
    public ParamInt PitchEnvLevel0 { get; }
    public ParamInt PitchEnvLevel1 { get; }
    public ParamInt PitchEnvLevel2 { get; }
    public ParamInt PitchEnvLevel3 { get; }
    public ParamInt PitchEnvLevel4 { get; }

    // --- Filter (TVF) ---
    public ParamString TvfFilterType { get; }
    public ParamInt TvfCutoff { get; }
    public ParamString TvfCutoffVeloCurve { get; }
    public ParamInt TvfCutoffVeloSens { get; }
    public ParamInt TvfResonance { get; }
    public ParamInt TvfResonanceVeloSens { get; }
    public ParamInt TvfEnvDepth { get; }
    public ParamString TvfEnvVeloCurve { get; }
    public ParamInt TvfEnvVeloSens { get; }
    public ParamInt TvfEnvTime1VeloSens { get; }
    public ParamInt TvfEnvTime4VeloSens { get; }
    public ParamInt TvfEnvTime1 { get; }
    public ParamInt TvfEnvTime2 { get; }
    public ParamInt TvfEnvTime3 { get; }
    public ParamInt TvfEnvTime4 { get; }
    public ParamInt TvfEnvLevel0 { get; }
    public ParamInt TvfEnvLevel1 { get; }
    public ParamInt TvfEnvLevel2 { get; }
    public ParamInt TvfEnvLevel3 { get; }
    public ParamInt TvfEnvLevel4 { get; }

    private readonly IReadOnlyList<IParam> _editable;
```

- [ ] **Step 3: Construct the Pitch + TVF params in the ctor**

Find this exact line:
```csharp
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);
```
Add immediately AFTER it:
```csharp

        // Pitch envelope (5-level / 4-time, bipolar).
        PitchEnvDepth = PI("Pitch Env Depth", -12, 12);
        PitchEnvVeloSens = PI("Pitch Env Velocity Sens", -63, 63);
        PitchEnvTime1VeloSens = PI("Pitch Env Time 1 Velocity Sens", -63, 63);
        PitchEnvTime4VeloSens = PI("Pitch Env Time 4 Velocity Sens", -63, 63);
        PitchEnvTime1 = PI("Pitch Env Time 1", 0, 127);
        PitchEnvTime2 = PI("Pitch Env Time 2", 0, 127);
        PitchEnvTime3 = PI("Pitch Env Time 3", 0, 127);
        PitchEnvTime4 = PI("Pitch Env Time 4", 0, 127);
        PitchEnvLevel0 = PI("Pitch Env Level 0", -63, 63);
        PitchEnvLevel1 = PI("Pitch Env Level 1", -63, 63);
        PitchEnvLevel2 = PI("Pitch Env Level 2", -63, 63);
        PitchEnvLevel3 = PI("Pitch Env Level 3", -63, 63);
        PitchEnvLevel4 = PI("Pitch Env Level 4", -63, 63);

        // Filter (TVF): cutoff/resonance + 5-level / 4-time envelope.
        TvfFilterType = PS("TVF Filter Type");
        TvfCutoff = PI("TVF Cutoff Frequency", 0, 127);
        TvfCutoffVeloCurve = PS("TVF Cutoff Velocity Curve");
        TvfCutoffVeloSens = PI("TVF Cutoff Velocity Sens", -63, 63);
        TvfResonance = PI("TVF Resonance", 0, 127);
        TvfResonanceVeloSens = PI("TVF Resonance Velocity Sens", -63, 63);
        TvfEnvDepth = PI("TVF Env Depth", -63, 63);
        TvfEnvVeloCurve = PS("TVF Env Velocity Curve");
        TvfEnvVeloSens = PI("TVF Env Velocity Sens", -63, 63);
        TvfEnvTime1VeloSens = PI("TVF Env Time 1 Velocity Sens", -63, 63);
        TvfEnvTime4VeloSens = PI("TVF Env Time 4 Velocity Sens", -63, 63);
        TvfEnvTime1 = PI("TVF Env Time 1", 0, 127);
        TvfEnvTime2 = PI("TVF Env Time 2", 0, 127);
        TvfEnvTime3 = PI("TVF Env Time 3", 0, 127);
        TvfEnvTime4 = PI("TVF Env Time 4", 0, 127);
        TvfEnvLevel0 = PI("TVF Env Level 0", 0, 127);
        TvfEnvLevel1 = PI("TVF Env Level 1", 0, 127);
        TvfEnvLevel2 = PI("TVF Env Level 2", 0, 127);
        TvfEnvLevel3 = PI("TVF Env Level 3", 0, 127);
        TvfEnvLevel4 = PI("TVF Env Level 4", 0, 127);
        TvfFilterType.PropertyChanged += OnFilterTypeChanged;
```

- [ ] **Step 4: Fold the Pitch + TVF params into `_editable`**

Find this exact pair of lines:
```csharp
        foreach (var wmt in wmts) editable.AddRange(wmt.Params);
        _editable = editable;
```
Replace with:
```csharp
        foreach (var wmt in wmts) editable.AddRange(wmt.Params);
        editable.AddRange(new IParam[]
        {
            PitchEnvDepth, PitchEnvVeloSens, PitchEnvTime1VeloSens, PitchEnvTime4VeloSens,
            PitchEnvTime1, PitchEnvTime2, PitchEnvTime3, PitchEnvTime4,
            PitchEnvLevel0, PitchEnvLevel1, PitchEnvLevel2, PitchEnvLevel3, PitchEnvLevel4,
            TvfFilterType, TvfCutoff, TvfCutoffVeloCurve, TvfCutoffVeloSens, TvfResonance, TvfResonanceVeloSens,
            TvfEnvDepth, TvfEnvVeloCurve, TvfEnvVeloSens, TvfEnvTime1VeloSens, TvfEnvTime4VeloSens,
            TvfEnvTime1, TvfEnvTime2, TvfEnvTime3, TvfEnvTime4,
            TvfEnvLevel0, TvfEnvLevel1, TvfEnvLevel2, TvfEnvLevel3, TvfEnvLevel4,
        });
        _editable = editable;
```

- [ ] **Step 5: Add the derived FilterCurve properties + the handler**

Find this exact line:
```csharp
    public PCMDrumWmtLayerViewModel SelectedWmt => Wmts[_selectedWmtIndex];
```
Add immediately AFTER it:
```csharp

    // FilterCurveControl reads a mode string + steep flag; derive them from the TVF filter type.
    public string FilterCurveMode => PcmTvfRules.CurveMode(TvfFilterType.Value);
    public bool FilterCurveSteep => PcmTvfRules.CurveSteep(TvfFilterType.Value);

    private void OnFilterTypeChanged(object? s, PropertyChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(FilterCurveMode));
        this.RaisePropertyChanged(nameof(FilterCurveSteep));
    }
```

- [ ] **Step 6: Unsubscribe in Dispose**

Find this exact line:
```csharp
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
```
Replace with:
```csharp
    public void Dispose()
    {
        TvfFilterType.PropertyChanged -= OnFilterTypeChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
```

- [ ] **Step 7: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (`PcmTvfRules` is in `Integra7AuralAlchemist.Models.Services`, already imported.) If a param path or `PcmTvfRules` member mismatches, STOP and report BLOCKED with the exact error.

- [ ] **Step 8: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 9: Commit**

```bash
git add Src/ViewModels/PCMDrumNoteEditorViewModel.cs
git commit -m "feat: add Pitch + TVF params to PCM-Drums per-key VM"
```

---

### Task 2: Fill the Pitch and Filter tabs

**Files:**
- Modify: `Src/Views/PCMDrumNoteEditorView.axaml`

Replace the two placeholders. All sliders are `ValueSlider`; envelopes are `MultiStageEnvelopeControl` with `NumericUpDown` precise entry; brushes bind to existing `Sn*` resources (matching the PCM Synth editor). No hardcoded colors.

- [ ] **Step 1: Replace the Pitch placeholder**

Find this exact placeholder:
```xml
            <TabItem Header="Pitch">
                <TextBlock Margin="12" Opacity="0.6" Text="Pitch envelope (Phase 4)."/>
            </TabItem>
```
Replace it with:
```xml
            <TabItem Header="Pitch">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <StackPanel Spacing="12" Margin="0,8,0,8">
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Pitch envelope" FontWeight="Bold"/>
                                <controls:MultiStageEnvelopeControl Height="120" Bipolar="True"
                                    Time1="{Binding PitchEnvTime1.Value, Mode=TwoWay}"
                                    Time2="{Binding PitchEnvTime2.Value, Mode=TwoWay}"
                                    Time3="{Binding PitchEnvTime3.Value, Mode=TwoWay}"
                                    Time4="{Binding PitchEnvTime4.Value, Mode=TwoWay}"
                                    Level0="{Binding PitchEnvLevel0.Value, Mode=TwoWay}"
                                    Level1="{Binding PitchEnvLevel1.Value, Mode=TwoWay}"
                                    Level2="{Binding PitchEnvLevel2.Value, Mode=TwoWay}"
                                    Level3="{Binding PitchEnvLevel3.Value, Mode=TwoWay}"
                                    Level4="{Binding PitchEnvLevel4.Value, Mode=TwoWay}"
                                    LineBrush="{StaticResource SnPitchEnvelopeBrush}"
                                    FillBrush="{StaticResource SnPitchEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                    HandleBrush="{StaticResource SnEnvelopeHandleBrush}"
                                    FocusBrush="{StaticResource SnEnvelopeFocusBrush}"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Times (T1–T4)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime3.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime4.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Levels (L0–L4)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel0.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel3.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel4.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Pitch amount &amp; velocity" FontWeight="Bold"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="170" Margin="0,0,16,8">
                                        <TextBlock Text="Env Depth (semitones)" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-12" Maximum="12" Value="{Binding PitchEnvDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="170" Margin="0,0,16,8">
                                        <TextBlock Text="Env Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding PitchEnvVeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="170" Margin="0,0,16,8">
                                        <TextBlock Text="T1 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding PitchEnvTime1VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="170" Margin="0,0,16,8">
                                        <TextBlock Text="T4 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding PitchEnvTime4VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
```

- [ ] **Step 2: Replace the Filter placeholder**

Find this exact placeholder:
```xml
            <TabItem Header="Filter">
                <TextBlock Margin="12" Opacity="0.6" Text="Filter / TVF (Phase 4)."/>
            </TabItem>
```
Replace it with:
```xml
            <TabItem Header="Filter">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <StackPanel Spacing="12" Margin="0,8,0,8">
                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <StackPanel Orientation="Horizontal" Spacing="16">
                                    <TextBlock Text="Filter (TVF)" FontWeight="Bold" VerticalAlignment="Center"/>
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Type" ToolTip.Tip="TVF Filter Type"/>
                                        <ComboBox ItemsSource="{Binding TvfFilterType.Options}"
                                                  SelectedItem="{Binding TvfFilterType.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </StackPanel>
                                <controls:FilterCurveControl Height="150"
                                    Mode="{Binding FilterCurveMode}"
                                    Steep="{Binding FilterCurveSteep}"
                                    Cutoff="{Binding TvfCutoff.Value, Mode=TwoWay}"
                                    Resonance="{Binding TvfResonance.Value, Mode=TwoWay}"
                                    LineBrush="{StaticResource SnFilterEnvelopeBrush}"
                                    FillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                    HandleBrush="{StaticResource SnEnvelopeHandleBrush}"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Cutoff" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding TvfCutoff.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Resonance" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding TvfResonance.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                                        <TextBlock Text="Env Depth" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfEnvDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>

                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Filter envelope (TVF)" FontWeight="Bold"/>
                                <controls:MultiStageEnvelopeControl Height="120"
                                    Time1="{Binding TvfEnvTime1.Value, Mode=TwoWay}"
                                    Time2="{Binding TvfEnvTime2.Value, Mode=TwoWay}"
                                    Time3="{Binding TvfEnvTime3.Value, Mode=TwoWay}"
                                    Time4="{Binding TvfEnvTime4.Value, Mode=TwoWay}"
                                    Level0="{Binding TvfEnvLevel0.Value, Mode=TwoWay}"
                                    Level1="{Binding TvfEnvLevel1.Value, Mode=TwoWay}"
                                    Level2="{Binding TvfEnvLevel2.Value, Mode=TwoWay}"
                                    Level3="{Binding TvfEnvLevel3.Value, Mode=TwoWay}"
                                    Level4="{Binding TvfEnvLevel4.Value, Mode=TwoWay}"
                                    LineBrush="{StaticResource SnFilterEnvelopeBrush}"
                                    FillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                    HandleBrush="{StaticResource SnEnvelopeHandleBrush}"
                                    FocusBrush="{StaticResource SnEnvelopeFocusBrush}"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Times (T1–T4)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime3.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime4.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Margin="0,0,12,4">
                                        <TextBlock Text="Levels (L0–L4)" Opacity="0.7"/>
                                        <StackPanel Orientation="Horizontal">
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel0.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel1.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel2.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel3.Value, Mode=TwoWay}"/>
                                            <NumericUpDown Width="116" Margin="0,0,6,4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel4.Value, Mode=TwoWay}"/>
                                        </StackPanel>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>

                        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                            <StackPanel Spacing="8">
                                <TextBlock Text="Velocity response" FontWeight="Bold"/>
                                <WrapPanel Orientation="Horizontal">
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Cutoff Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfCutoffVeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Cutoff Velocity Curve" Classes="sliderLabel"/>
                                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding TvfCutoffVeloCurve.Options}"
                                                  SelectedItem="{Binding TvfCutoffVeloCurve.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Resonance Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfResonanceVeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Env Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfEnvVeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Env Velocity Curve" Classes="sliderLabel"/>
                                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding TvfEnvVeloCurve.Options}"
                                                  SelectedItem="{Binding TvfEnvVeloCurve.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Env T1 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfEnvTime1VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2" Width="180" Margin="0,0,16,8">
                                        <TextBlock Text="Env T4 Velocity Sens" Classes="sliderLabel"/>
                                        <controls:ValueSlider Minimum="-63" Maximum="63" Value="{Binding TvfEnvTime4VeloSens.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </WrapPanel>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
```

- [ ] **Step 3: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. Compiled XAML resolves all `PitchEnv*`/`Tvf*`/`FilterCurveMode`/`FilterCurveSteep` bindings and every `{StaticResource}` (`SnPitchEnvelope*`, `SnFilterEnvelope*`, `SnEnvelope*`). A genuine AVLN/binding error IS a true failure — report the exact missing binding/key.

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/PCMDrumNoteEditorView.axaml
git commit -m "feat: PCM-Drums Pitch + Filter tabs (envelopes + filter curve)"
```

---

## Done criteria

The per-key editor's **Pitch** tab shows a draggable bipolar pitch envelope with precise spinboxes + depth/velocity controls; the **Filter** tab shows the filter type, an interactive `FilterCurveControl` (cutoff/resonance), the TVF envelope with spinboxes, and the velocity-response controls. All five per-key sub-tabs (Wave, Pitch, Filter, Amp, Setup) are now functional and Copy/Paste covers their params. Build green, 244 tests passing. This completes Phase 4. Phase 5 (Comp-EQ + FX) finishes the engine.
