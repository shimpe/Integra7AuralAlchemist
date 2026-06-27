# PCM Synth Tone Editor — Phase 2b (Sound tab view) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the Sound tab body — a **Wave picker** (bank + searchable wave field + stereo + gain/FXM), a **Filter (TVF)** section (type + interactive `FilterCurveControl` + cutoff/resonance), an **Amp (TVA)** section (level/pan/bias/sends/tune), and **numeric envelope rows** for the pitch/TVF/TVA rate-level envelopes. The graphical multi-stage envelope control is Phase 2c (it will sit above these numeric rows).

**Architecture:** Bind the Sound tab to the `SelectedPartial` (a `PCMPartialViewModel`, which already exposes all Sound params from Phase 2a). The filter graph reuses the existing `FilterCurveControl`; PCM's `TVF_FILTER_TYPE` is mapped to the control's `Mode`/`Steep` via a pure helper on `PcmTvfRules` and two computed VM properties. The wave field uses Avalonia's `AutoCompleteBox` for substring filtering over the ~1083-entry wave list.

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. A running app may hold the Debug exe lock (use Release). A real XAML/binding error (AVLN:) is a true failure.

---

## File Structure

- **Modify** `Src/Models/Services/PcmTvfRules.cs` — add `CurveMode(type)` + `CurveSteep(type)` (TVF type → `FilterCurveControl` mode/steep).
- **Modify** `Tests/TestPcmTvfRules.cs` — cover the new mapping.
- **Modify** `Src/ViewModels/PCMPartialViewModel.cs` — add computed `FilterCurveMode`/`FilterCurveSteep` and raise them when the filter type changes.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — replace the Sound tab stub with the Wave/Pitch/Filter/Amp/Envelopes sections.

---

## Task 1: Filter-type → curve mapping (helper + VM props)

**Files:**
- Modify: `Src/Models/Services/PcmTvfRules.cs`
- Modify: `Tests/TestPcmTvfRules.cs`
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`

`FilterCurve.Sample` accepts modes `"Low pass"`, `"High pass"`, `"Band pass"`, `"Peaking"`, `"Bypass"` plus a `steep` flag. Map the PCM TVF types onto them.

- [ ] **Step 1: Add the mapping + tests (TDD).** Append to `Tests/TestPcmTvfRules.cs` inside the class:

```csharp
    [TestCase("Off", "Bypass")]
    [TestCase("Low-pass filter", "Low pass")]
    [TestCase("Band-pass filter", "Band pass")]
    [TestCase("High-pass filter", "High pass")]
    [TestCase("Peaking filter", "Peaking")]
    [TestCase("Low-pass filter 2", "Low pass")]
    [TestCase("Low-pass filter 3", "Low pass")]
    public void CurveMode_maps_to_FilterCurve_modes(string input, string expected)
    {
        Assert.That(PcmTvfRules.CurveMode(input), Is.EqualTo(expected));
    }

    [TestCase("Low-pass filter", false)]
    [TestCase("Low-pass filter 2", true)]
    [TestCase("Low-pass filter 3", true)]
    [TestCase("Off", false)]
    public void CurveSteep_flags_the_steeper_lowpass_variants(string input, bool expected)
    {
        Assert.That(PcmTvfRules.CurveSteep(input), Is.EqualTo(expected));
    }
```

- [ ] **Step 2: Run to verify FAIL** (`CurveMode`/`CurveSteep` don't exist):

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

- [ ] **Step 3: Implement.** Add to `Src/Models/Services/PcmTvfRules.cs` inside the `PcmTvfRules` class (after `Abbrev`):

```csharp
    private static readonly Dictionary<string, string> CurveModes = new()
    {
        ["Off"] = "Bypass",
        ["Low-pass filter"] = "Low pass",
        ["Band-pass filter"] = "Band pass",
        ["High-pass filter"] = "High pass",
        ["Peaking filter"] = "Peaking",
        ["Low-pass filter 2"] = "Low pass",
        ["Low-pass filter 3"] = "Low pass",
    };

    /// <summary>Maps a TVF filter-type display string to a <c>FilterCurve</c> mode; unknown → "Low pass".</summary>
    public static string CurveMode(string? filterType)
    {
        if (filterType is not null && CurveModes.TryGetValue(filterType, out var m)) return m;
        return "Low pass";
    }

    /// <summary>True for the steeper low-pass variants (LPF2 / LPF3), which roll off faster.</summary>
    public static bool CurveSteep(string? filterType) =>
        filterType is "Low-pass filter 2" or "Low-pass filter 3";
```

- [ ] **Step 4: Add the computed VM properties.** In `Src/ViewModels/PCMPartialViewModel.cs`, add these two properties next to the other card-summary properties (after `FilterSummary`):

```csharp
    // FilterCurveControl reads a mode string + steep flag; derive them from the TVF filter type.
    public string FilterCurveMode => PcmTvfRules.CurveMode(TvfFilterType.Value);
    public bool FilterCurveSteep => PcmTvfRules.CurveSteep(TvfFilterType.Value);
```

And in the existing `OnSummaryChanged` method, add two more raises so the graph redraws when the filter type changes:

```csharp
    private void OnSummaryChanged(object? s, PropertyChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(WaveSummary));
        this.RaisePropertyChanged(nameof(LevelLabel));
        this.RaisePropertyChanged(nameof(PanLabel));
        this.RaisePropertyChanged(nameof(FilterSummary));
        this.RaisePropertyChanged(nameof(FilterCurveMode));
        this.RaisePropertyChanged(nameof(FilterCurveSteep));
    }
```

(`TvfFilterType.PropertyChanged += OnSummaryChanged;` is already wired in the ctor, so these raise on type change.)

- [ ] **Step 5: Run to verify PASS** + full suite:

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: PASS. Test count +11 (7 CurveMode + 4 CurveSteep rows).

- [ ] **Step 6: Commit**

```
git add Src/Models/Services/PcmTvfRules.cs Tests/TestPcmTvfRules.cs Src/ViewModels/PCMPartialViewModel.cs
git commit -m "feat: PCM TVF filter-type to filter-curve mapping"
```

---

## Task 2: Sound tab view

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

Replace the Sound `TabItem`'s stub body with the five sections. Binds to `SelectedPartial`. Reuses the `controls:` namespace for `FilterCurveControl` and the `Sn*` brushes.

- [ ] **Step 1: Add the controls namespace.** In the root `<UserControl …>` open tag, add this xmlns alongside the others:

```xml
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
```

- [ ] **Step 2: Replace the Sound tab body.** Find:

```xml
                    <TabItem Header="Sound">
                        <TextBlock Margin="12" Opacity="0.6" Text="Sound — wave, filter, amp, envelopes (Phase 2)."/>
                    </TabItem>
```

Replace with:

```xml
                    <TabItem Header="Sound">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                            <StackPanel DataContext="{Binding SelectedPartial}" x:DataType="vm:PCMPartialViewModel"
                                        Spacing="12" Margin="0,0,0,8">

                                <!-- Wave -->
                                <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                                    <StackPanel Spacing="8">
                                        <TextBlock Text="Wave" FontWeight="Bold"/>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Bank" Classes="sliderLabel" ToolTip.Tip="Wave Group Type"/>
                                                <ComboBox ItemsSource="{Binding WaveGroupType.Options}"
                                                          SelectedItem="{Binding WaveGroupType.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Wave (type to find)" Classes="sliderLabel" ToolTip.Tip="Wave Number L (Mono)"/>
                                                <AutoCompleteBox Width="240" MaxDropDownHeight="320"
                                                                 FilterMode="Contains"
                                                                 ItemsSource="{Binding WaveNumberL.Options}"
                                                                 SelectedItem="{Binding WaveNumberL.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Gain (dB)" Classes="sliderLabel" ToolTip.Tip="Wave Gain"/>
                                                <ComboBox ItemsSource="{Binding WaveGain.Options}"
                                                          SelectedItem="{Binding WaveGain.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Stereo"/>
                                                <ToggleSwitch x:Name="StereoToggle" OnContent="On" OffContent="Off"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" IsVisible="{Binding #StereoToggle.IsChecked}">
                                                <TextBlock Text="Wave R (type to find)" Classes="sliderLabel" ToolTip.Tip="Wave Number R"/>
                                                <AutoCompleteBox Width="240" MaxDropDownHeight="320"
                                                                 FilterMode="Contains"
                                                                 ItemsSource="{Binding WaveNumberR.Options}"
                                                                 SelectedItem="{Binding WaveNumberR.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Metallic (FXM)" ToolTip.Tip="Wave FXM Switch"/>
                                                <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding WaveFxmSwitch.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="FXM Depth" Classes="sliderLabel" ToolTip.Tip="Wave FXM Depth"/>
                                                <Slider Minimum="0" Maximum="16" Value="{Binding WaveFxmDepth.Value, Mode=TwoWay}"
                                                        IsEnabled="{Binding WaveFxmSwitch.Value}"/>
                                            </StackPanel>
                                        </StackPanel>
                                    </StackPanel>
                                </Border>

                                <!-- Pitch -->
                                <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                                    <StackPanel Spacing="8">
                                        <TextBlock Text="Pitch" FontWeight="Bold"/>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Coarse (semi)" ToolTip.Tip="Partial Coarse Tune"/>
                                                <NumericUpDown Width="110" Minimum="-48" Maximum="48" Increment="1" FormatString="0"
                                                               Value="{Binding CoarseTune.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Fine (cent)" ToolTip.Tip="Partial Fine Tune"/>
                                                <NumericUpDown Width="110" Minimum="-50" Maximum="50" Increment="1" FormatString="0"
                                                               Value="{Binding FineTune.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Env Depth" ToolTip.Tip="Pitch Env Depth"/>
                                                <NumericUpDown Width="110" Minimum="-12" Maximum="12" Increment="1" FormatString="0"
                                                               Value="{Binding PitchEnvDepth.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        <TextBlock Text="Pitch envelope — times / levels (graphical editor in a later phase)" Opacity="0.6" FontSize="11"/>
                                        <Grid ColumnDefinitions="60,*" RowDefinitions="Auto,Auto" ColumnSpacing="8" RowSpacing="4">
                                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Times" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime3.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding PitchEnvTime4.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Levels" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="74" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel0.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel3.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="-63" Maximum="63" Increment="1" FormatString="0" Value="{Binding PitchEnvLevel4.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </Grid>
                                    </StackPanel>
                                </Border>

                                <!-- Filter (TVF) -->
                                <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                                    <StackPanel Spacing="8">
                                        <TextBlock Text="Filter (TVF)" FontWeight="Bold"/>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
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
                                        <StackPanel Orientation="Horizontal" Spacing="12">
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Cutoff" ToolTip.Tip="TVF Cutoff Frequency"/>
                                                <NumericUpDown Width="118" Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                               Value="{Binding TvfCutoff.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Resonance" ToolTip.Tip="TVF Resonance"/>
                                                <NumericUpDown Width="118" Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                               Value="{Binding TvfResonance.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Env Depth" ToolTip.Tip="TVF Env Depth"/>
                                                <NumericUpDown Width="118" Minimum="-63" Maximum="63" Increment="1" FormatString="0"
                                                               Value="{Binding TvfEnvDepth.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        <TextBlock Text="Filter envelope — times / levels (graphical editor in a later phase)" Opacity="0.6" FontSize="11"/>
                                        <Grid ColumnDefinitions="60,*" RowDefinitions="Auto,Auto" ColumnSpacing="8" RowSpacing="4">
                                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Times" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime3.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvTime4.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Levels" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="74" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel0.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel3.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="74" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvfEnvLevel4.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </Grid>
                                    </StackPanel>
                                </Border>

                                <!-- Amp (TVA) -->
                                <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                                    <StackPanel Spacing="8">
                                        <TextBlock Text="Amp (TVA)" FontWeight="Bold"/>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Level" Classes="sliderLabel" ToolTip.Tip="Partial Level"/>
                                                <Slider Minimum="0" Maximum="127" Value="{Binding PartialLevel.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Pan (L / C / R)" Classes="sliderLabel" ToolTip.Tip="Partial Pan"/>
                                                <Slider Minimum="-64" Maximum="63" Value="{Binding PartialPan.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Chorus Send" Classes="sliderLabel" ToolTip.Tip="Partial Chorus Send Level"/>
                                                <Slider Minimum="0" Maximum="127" Value="{Binding ChorusSend.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Reverb Send" Classes="sliderLabel" ToolTip.Tip="Partial Reverb Send Level"/>
                                                <Slider Minimum="0" Maximum="127" Value="{Binding ReverbSend.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        <TextBlock Text="Amp envelope — times / levels (graphical editor in a later phase)" Opacity="0.6" FontSize="11"/>
                                        <Grid ColumnDefinitions="60,*" RowDefinitions="Auto,Auto" ColumnSpacing="8" RowSpacing="4">
                                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Times" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime3.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvTime4.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Levels" VerticalAlignment="Center" Opacity="0.7"/>
                                            <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel1.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel2.Value, Mode=TwoWay}"/>
                                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding TvaEnvLevel3.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </Grid>
                                        <StackPanel Orientation="Horizontal" Spacing="16">
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Bias Level" Classes="sliderLabel" ToolTip.Tip="Bias Level"/>
                                                <Slider Minimum="-100" Maximum="100" Value="{Binding BiasLevel.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2" Width="150">
                                                <TextBlock Text="Bias Position" Classes="sliderLabel" ToolTip.Tip="Bias Position"/>
                                                <Slider Minimum="0" Maximum="127" Value="{Binding BiasPosition.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Bias Dir" ToolTip.Tip="Bias Direction"/>
                                                <ComboBox ItemsSource="{Binding BiasDirection.Options}"
                                                          SelectedItem="{Binding BiasDirection.Value, Mode=TwoWay}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        <Button HorizontalAlignment="Left" Content="Advanced partial parameters…"
                                                Command="{Binding #Root.((vm:PCMSynthToneEditorViewModel)DataContext).AdvancedPartialCommand}"/>
                                    </StackPanel>
                                </Border>
                            </StackPanel>
                        </ScrollViewer>
                    </TabItem>
```

- [ ] **Step 3: Build**

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: Build succeeded. If `AutoCompleteBox` fails to resolve a member or binding, see the fallback note below.

> **AutoCompleteBox fallback:** `AutoCompleteBox` is a standard Avalonia control but is not used elsewhere in this app. If `FilterMode="Contains"` or the `SelectedItem`/`Text` two-way binding does not compile or behave, replace each `AutoCompleteBox` with a `ComboBox MaxDropDownHeight="320" ItemsSource="{Binding WaveNumberL.Options}" SelectedItem="{Binding WaveNumberL.Value, Mode=TwoWay}"` (proven by the SN-S editor's PCM wave combo). Note the change in your report.

- [ ] **Step 4: Run tests**

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: PCM Sound tab (wave picker, filter, amp, numeric envelopes)"
```

---

## Done criteria

- Full suite green (Phase 2a 196 + 11 new = 207).
- The Sound tab shows Wave (bank + searchable wave field + stereo + gain/FXM), Pitch (coarse/fine + env numerics), Filter (type + interactive curve + cutoff/res + env numerics), and Amp (level/pan/sends + env numerics + bias). Editing the filter type reshapes the curve; dragging the curve sets cutoff/resonance.
- "Advanced partial parameters…" opens the raw **Advanced — Partials** tab on the selected partial.
- The pitch/TVF/TVA envelopes are numeric for now; Phase 2c adds the graphical multi-stage editor above each numeric row.
