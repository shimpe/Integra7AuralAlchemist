# PCM Synth Tone Editor — Phase 5 (Response) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the Response tab — how the selected partial reacts to playing: velocity sensitivity, velocity curves, and keyfollow, laid out as a grid (rows: Velocity / Velocity curve / Key position; columns: Brightness (filter) / Loudness (amp) / Pitch). Completes the PCM Synth editor.

**Architecture:** PCM partials have no per-partial aftertouch-sens params (unlike SN-S), so the grid uses Velocity / Velocity-curve / Key-position rows. Add the response params to `PCMPartialViewModel` (also folded into Copy/Paste), then fill the Response tab. Amp-vs-key response is set by Bias (already on the Amp/Sound tab) — noted in the UI.

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if a running app holds the Debug exe lock.

---

## File Structure

- **Modify** `Src/ViewModels/PCMPartialViewModel.cs` — add the seven response params + include them in `_editable`.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — fill the Response tab.

---

## Task 1: Response params on PCMPartialViewModel

**Files:**
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`

Verified display ranges/reprs: velocity-sens are −63..63; the velocity curves use `TVF_VELOCITY_CURVE` (FIXED/1..7); keyfollows are −200..200.

- [ ] **Step 1: Add the property declarations.** Find the Bias section:

```csharp
    // --- Bias ---
    public ParamInt BiasLevel { get; }
    public ParamInt BiasPosition { get; }
    public ParamString BiasDirection { get; }
```

Add immediately after it:

```csharp

    // --- Response (velocity / keyfollow) ---
    public ParamInt TvfCutoffVeloSens { get; }
    public ParamInt TvaVeloSens { get; }
    public ParamInt PitchEnvVeloSens { get; }
    public ParamString TvfCutoffVelCurve { get; }
    public ParamString TvaVelCurve { get; }
    public ParamInt TvfCutoffKeyfollow { get; }
    public ParamInt WavePitchKeyfollow { get; }
```

- [ ] **Step 2: Add the constructor assignments.** Find the Bias ctor block:

```csharp
        BiasLevel = PI("Bias Level", -100, 100);
        BiasPosition = PI("Bias Position", 0, 127);
        BiasDirection = PS("Bias Direction");
```

Add immediately after it:

```csharp

        TvfCutoffVeloSens = PI("TVF Cutoff Velocity Sens", -63, 63);
        TvaVeloSens = PI("TVA Velocity Sens", -63, 63);
        PitchEnvVeloSens = PI("Pitch Env Velocity Sens", -63, 63);
        TvfCutoffVelCurve = PS("TVF Cutoff Velocity Curve");
        TvaVelCurve = PS("TVA Velocity Curve");
        TvfCutoffKeyfollow = PI("TVF Cutoff Keyfollow", -200, 200);
        WavePitchKeyfollow = PI("Wave Pitch Keyfollow", -200, 200);
```

- [ ] **Step 3: Add them to the editable set.** Find the end of the `_editable` initializer:

```csharp
            BiasLevel, BiasPosition, BiasDirection,
        }
        .Concat(Lfo1.Params).Concat(Lfo2.Params).ToArray();
```

Replace it with:

```csharp
            BiasLevel, BiasPosition, BiasDirection,
            TvfCutoffVeloSens, TvaVeloSens, PitchEnvVeloSens, TvfCutoffVelCurve, TvaVelCurve,
            TvfCutoffKeyfollow, WavePitchKeyfollow,
        }
        .Concat(Lfo1.Params).Concat(Lfo2.Params).ToArray();
```

- [ ] **Step 4: Build** — `… build … --configuration Release`. Expected: succeeded.
- [ ] **Step 5: Run tests** — Expected: PASS (237).
- [ ] **Step 6: Commit**

```
git add Src/ViewModels/PCMPartialViewModel.cs
git commit -m "feat: PCM partial response params (velocity sens / curve / keyfollow)"
```

---

## Task 2: Response tab view

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

- [ ] **Step 1: Replace the Response tab stub.** Find:

```xml
                    <TabItem Header="Response">
                        <TextBlock Margin="12" Opacity="0.6" Text="Response — velocity / aftertouch / keyfollow (Phase 5)."/>
                    </TabItem>
```

Replace with:

```xml
                    <TabItem Header="Response">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                            <Border DataContext="{Binding SelectedPartial}" x:DataType="vm:PCMPartialViewModel"
                                    Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6"
                                    Padding="10" Margin="0,8,0,0" VerticalAlignment="Top">
                                <StackPanel Spacing="12">
                                    <StackPanel Orientation="Horizontal" Spacing="6">
                                        <TextBlock Text="Response" FontWeight="Bold"/>
                                        <TextBlock Text="— how this partial reacts to playing"
                                                   Opacity="0.6" VerticalAlignment="Bottom"/>
                                    </StackPanel>
                                    <Grid ColumnDefinitions="130,200,200,200" RowDefinitions="Auto,Auto,Auto,Auto"
                                          ColumnSpacing="16" RowSpacing="10">
                                        <TextBlock Grid.Row="0" Grid.Column="1" Text="→ Brightness (filter)" FontWeight="Bold"/>
                                        <TextBlock Grid.Row="0" Grid.Column="2" Text="→ Loudness (amp)" FontWeight="Bold"/>
                                        <TextBlock Grid.Row="0" Grid.Column="3" Text="→ Pitch" FontWeight="Bold"/>

                                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Velocity" VerticalAlignment="Center"
                                                   ToolTip.Tip="How hard you play"/>
                                        <Slider Grid.Row="1" Grid.Column="1" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="TVF Cutoff Velocity Sens"
                                                Value="{Binding TvfCutoffVeloSens.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="1" Grid.Column="2" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="TVA Velocity Sens"
                                                Value="{Binding TvaVeloSens.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="1" Grid.Column="3" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="Pitch Env Velocity Sens"
                                                Value="{Binding PitchEnvVeloSens.Value, Mode=TwoWay}"/>

                                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Velocity curve" VerticalAlignment="Center"
                                                   ToolTip.Tip="Shape of the velocity response"/>
                                        <ComboBox Grid.Row="2" Grid.Column="1" HorizontalAlignment="Stretch"
                                                  ToolTip.Tip="TVF Cutoff Velocity Curve"
                                                  ItemsSource="{Binding TvfCutoffVelCurve.Options}"
                                                  SelectedItem="{Binding TvfCutoffVelCurve.Value, Mode=TwoWay}"/>
                                        <ComboBox Grid.Row="2" Grid.Column="2" HorizontalAlignment="Stretch"
                                                  ToolTip.Tip="TVA Velocity Curve"
                                                  ItemsSource="{Binding TvaVelCurve.Options}"
                                                  SelectedItem="{Binding TvaVelCurve.Value, Mode=TwoWay}"/>

                                        <TextBlock Grid.Row="3" Grid.Column="0" Text="Key position" VerticalAlignment="Center"
                                                   ToolTip.Tip="Higher vs lower notes (keyfollow)"/>
                                        <Slider Grid.Row="3" Grid.Column="1" Minimum="-200" Maximum="200"
                                                ToolTip.Tip="TVF Cutoff Keyfollow"
                                                Value="{Binding TvfCutoffKeyfollow.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="3" Grid.Column="3" Minimum="-200" Maximum="200"
                                                ToolTip.Tip="Wave Pitch Keyfollow"
                                                Value="{Binding WavePitchKeyfollow.Value, Mode=TwoWay}"/>
                                    </Grid>
                                    <TextBlock Text="Loudness vs key position is set by Bias (on the Sound tab’s Amp section)."
                                               Opacity="0.5" FontSize="11"/>
                                    <Button HorizontalAlignment="Left" Content="Advanced partial parameters…"
                                            Command="{Binding #Root.((vm:PCMSynthToneEditorViewModel)DataContext).AdvancedPartialCommand}"/>
                                </StackPanel>
                            </Border>
                        </ScrollViewer>
                    </TabItem>
```

- [ ] **Step 2: Build** — Expected: succeeded. (All bound properties exist on `PCMPartialViewModel` after Task 1; `AdvancedPartialCommand` is on the editor VM; compiled bindings validate them.)
- [ ] **Step 3: Run tests** — Expected: PASS (237).
- [ ] **Step 4: Commit**

```
git add Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: PCM Response tab (velocity sens / curve / keyfollow)"
```

---

## Done criteria

- Full suite green (237).
- The Response tab shows a Velocity / Velocity-curve / Key-position grid across Brightness/Loudness/Pitch; edits write to the partial (and Copy/Paste carries them). "Advanced partial parameters…" opens the raw partial tab.
- **This completes the PCM Synth Tone friendly editor** (Header, rack + audition, Sound, Motion, Zones, FX, Response, and Advanced navigation).
