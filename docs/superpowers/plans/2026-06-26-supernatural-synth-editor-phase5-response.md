# SuperNATURAL Synth (SN-S) Editor — Phase 5 (Response/Touch panel) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-partial **Response** sub-tab — a 3×2 matrix of expression sources (Velocity / Aftertouch / Keyboard) against destinations (Brightness / Loudness) — adding the two missing Aftertouch sens params and moving the four existing velocity/keyfollow sliders out of the Sound tab.

**Architecture:** Two new `ParamInt` wrappers on `SNSPartialViewModel` (reusing the existing `PI` helper, `_editable`, and `InitDefaults`); the four already-wrapped velocity/keyfollow params stay in the VM and only their *view* moves. The Response tab is added to the selected-partial editor `TabControl` (peer to Sound/Motion/FX), bound to `SelectedPartial`.

**Tech Stack:** Avalonia 12 (compiled bindings), ReactiveUI, .NET 10, NUnit 3. Build/test with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-response` (already created; spec committed).

Reference: spec `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase5-response-design.md`. Memory: `parameter-domain-infrastructure`, `sn-s-tone-parameters`, `no-hardcoded-colors-in-xaml`.

---

## File Structure

- Modify `Src/ViewModels/SNSPartialViewModel.cs` — add the two Aftertouch wrappers (+ `_editable`, `InitDefaults`).
- Modify `Src/Views/SNSynthToneEditorView.axaml` — remove four sliders from the Sound tab; add the "Response" `TabItem`.

No new files, no new pure logic (verified by build + existing 172 tests + hardware smoke).

---

## Task 1: Add the two Aftertouch params to `SNSPartialViewModel`

**Files:**
- Modify: `Src/ViewModels/SNSPartialViewModel.cs`

- [ ] **Step 1: Declare the two properties.** After the `AMP` properties block (the line `public ParamInt AmpKeyfollow { get; }`), insert:

```csharp

    // --- Aftertouch response (cutoff + level) ---
    public ParamInt CutoffAftertouchSens { get; }
    public ParamInt LevelAftertouchSens { get; }
```

- [ ] **Step 2: Construct them.** Immediately after the line `FilterEnvVeloSens = PI("Filter Env Velocity Sens", -63, 63);`, insert:

```csharp

        CutoffAftertouchSens = PI("Cutoff Aftertouch Sens", -63, 63);
        LevelAftertouchSens = PI("Level Aftertouch Sens", -63, 63);
```

- [ ] **Step 3: Add them to `_editable`.** In the `_editable` initializer, change the last data line from:

```csharp
            PitchEnvAttack, PitchEnvDecay, PitchEnvDepth
        }
```

to:

```csharp
            PitchEnvAttack, PitchEnvDecay, PitchEnvDepth,
            CutoffAftertouchSens, LevelAftertouchSens
        }
```

- [ ] **Step 4: Add init defaults.** In the `InitDefaults` dictionary, after the line `[PP + "Filter Env Velocity Sens"] = "0",`, insert:

```csharp
        [PP + "Cutoff Aftertouch Sens"] = "0",
        [PP + "Level Aftertouch Sens"] = "0",
```

- [ ] **Step 5: Build to verify it compiles.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. (Ignore `MSB3027`/`MSB3021` exe-lock messages from a running app — only `error CS/AVLN/XAMLIL` count.)

- [ ] **Step 6: Commit.**

```bash
git add Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat(sns): add Cutoff/Level Aftertouch Sens wrappers to the partial VM"
```

---

## Task 2: Move the four sliders into a new Response tab

**Files:**
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

- [ ] **Step 1: Remove the Filter "Cutoff Keyfollow" slider** from the Sound tab. Delete this exact block:

```xml
                            <StackPanel Spacing="2" Width="180">
                                <TextBlock Text="Cutoff Keyfollow" Classes="sliderLabel" ToolTip.Tip="Filter Cutoff Keyfollow"/>
                                <Slider Minimum="-100" Maximum="100" Value="{Binding FilterCutoffKeyfollow.Value, Mode=TwoWay}"/>
                            </StackPanel>
```

- [ ] **Step 2: Remove the Filter "Env Velocity Sens" slider.** Delete this exact block:

```xml
                            <StackPanel Spacing="2" Width="180">
                                <TextBlock Text="Env Velocity Sens" Classes="sliderLabel" ToolTip.Tip="Filter Env Velocity Sens"/>
                                <Slider Minimum="-63" Maximum="63" Value="{Binding FilterEnvVeloSens.Value, Mode=TwoWay}"/>
                            </StackPanel>
```

(The "HPF Cutoff" StackPanel between them stays; the Filter row keeps only HPF Cutoff.)

- [ ] **Step 3: Remove the Amp "Velocity" slider.** Delete this exact block:

```xml
                            <StackPanel Spacing="2" Width="170">
                                <TextBlock Text="Velocity (Consistent → Dynamic)" Classes="sliderLabel"
                                           ToolTip.Tip="AMP Level Velocity Sens"/>
                                <Slider Minimum="-63" Maximum="63" Value="{Binding AmpVeloSens.Value, Mode=TwoWay}"/>
                            </StackPanel>
```

- [ ] **Step 4: Remove the Amp "Keyfollow" slider.** Delete this exact block:

```xml
                            <StackPanel Spacing="2" Width="180">
                                <TextBlock Text="Keyfollow (Lower → Higher notes louder)" Classes="sliderLabel"
                                           ToolTip.Tip="AMP Level Keyfollow"/>
                                <Slider Minimum="-100" Maximum="100" Value="{Binding AmpKeyfollow.Value, Mode=TwoWay}"/>
                            </StackPanel>
```

(The Amp row keeps Level + Pan.)

- [ ] **Step 5: Add the Response tab.** Find the "FX" `TabItem` (it ends with `</ScrollViewer>` then `</TabItem>` followed by `</TabControl>`). Insert the following Response `TabItem` immediately AFTER the FX `TabItem`'s closing `</TabItem>` and BEFORE `</TabControl>`:

```xml
                    <TabItem Header="Response">
                        <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                            <Border DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                                    Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6"
                                    Padding="10" Margin="0,8,0,0" VerticalAlignment="Top">
                                <StackPanel Spacing="12">
                                    <StackPanel Orientation="Horizontal" Spacing="6">
                                        <TextBlock Text="Response" FontWeight="Bold"/>
                                        <TextBlock Text="— how this partial reacts to playing"
                                                   Opacity="0.6" VerticalAlignment="Bottom"/>
                                    </StackPanel>
                                    <Grid ColumnDefinitions="130,210,210" RowDefinitions="Auto,Auto,Auto,Auto"
                                          ColumnSpacing="16" RowSpacing="10">
                                        <TextBlock Grid.Row="0" Grid.Column="1" Text="→ Brightness (filter)" FontWeight="Bold"/>
                                        <TextBlock Grid.Row="0" Grid.Column="2" Text="→ Loudness (amp)" FontWeight="Bold"/>

                                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Velocity" VerticalAlignment="Center"
                                                   ToolTip.Tip="How hard you play"/>
                                        <Slider Grid.Row="1" Grid.Column="1" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="Filter Env Velocity Sens"
                                                Value="{Binding FilterEnvVeloSens.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="1" Grid.Column="2" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="AMP Level Velocity Sens"
                                                Value="{Binding AmpVeloSens.Value, Mode=TwoWay}"/>

                                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Aftertouch" VerticalAlignment="Center"
                                                   ToolTip.Tip="Pressure after the key is down"/>
                                        <Slider Grid.Row="2" Grid.Column="1" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="Cutoff Aftertouch Sens"
                                                Value="{Binding CutoffAftertouchSens.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="2" Grid.Column="2" Minimum="-63" Maximum="63"
                                                ToolTip.Tip="Level Aftertouch Sens"
                                                Value="{Binding LevelAftertouchSens.Value, Mode=TwoWay}"/>

                                        <TextBlock Grid.Row="3" Grid.Column="0" Text="Key position" VerticalAlignment="Center"
                                                   ToolTip.Tip="Higher vs lower notes (keyfollow)"/>
                                        <Slider Grid.Row="3" Grid.Column="1" Minimum="-100" Maximum="100"
                                                ToolTip.Tip="Filter Cutoff Keyfollow"
                                                Value="{Binding FilterCutoffKeyfollow.Value, Mode=TwoWay}"/>
                                        <Slider Grid.Row="3" Grid.Column="2" Minimum="-100" Maximum="100"
                                                ToolTip.Tip="AMP Level Keyfollow"
                                                Value="{Binding AmpKeyfollow.Value, Mode=TwoWay}"/>
                                    </Grid>
                                    <Button HorizontalAlignment="Left" Content="Advanced partial parameters…"
                                            Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).AdvancedOscillatorCommand}"/>
                                </StackPanel>
                            </Border>
                        </ScrollViewer>
                    </TabItem>
```

- [ ] **Step 6: Build to verify XAML compiles.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. Fix any `AVLN`/`XAMLIL` errors before committing. (`SnPanelBackgroundBrush` already exists in `App.axaml`; `#Root` is the view's `x:Name`. No inline colors.)

- [ ] **Step 7: Commit.**

```bash
git add Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): Response sub-tab — move velocity/keyfollow + add aftertouch matrix"
```

---

## Task 3: Verification + smoke checklist

**Files:** none (verification only).

- [ ] **Step 1: Full build.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Full test run.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: all 172 tests pass (no new tests this phase). If the app is running and the build can't copy the exe, close it or use `--no-build` against the last good build.

- [ ] **Step 3: Hand the hardware smoke checklist (spec §9) to the user** (they run it on the Integra-7):
  1. SN-S part → Editor → select a partial → confirm a **Response** sub-tab next to FX.
  2. Confirm the Sound tab’s Filter/Amp sections no longer show Velocity Sens / Keyfollow.
  3. Response: move **Velocity → Brightness/Loudness**; play soft vs hard → filter/level respond.
  4. Move **Aftertouch → Brightness/Loudness**; apply channel aftertouch → filter/level respond.
  5. Move **Key position → Brightness/Loudness**; play low vs high → filter/level track the keyboard.
  6. Edit on the hardware front panel → the matrix sliders update (round-trip).
  7. Copy a partial → Paste onto another: response settings (incl. aftertouch) carry over; Init → 0.
  8. "Advanced partial parameters…" opens the raw Partials tab on the same partial.

- [ ] **Step 4: Finish the branch** — once the user is satisfied, use superpowers:finishing-a-development-branch to merge `sns-response` to `main` (Option 1, local).

---

## Self-Review notes (author)
- Spec coverage: Task 1 = §4a; Task 2 = §4b (remove four + add Response tab); Task 3 = §7/§8 verification + §9 smoke.
- The four moved bindings (`FilterCutoffKeyfollow`, `FilterEnvVeloSens`, `AmpVeloSens`, `AmpKeyfollow`) are unchanged wrappers — only their XAML location moves, so compiled bindings still resolve in their new home.
- New params use the existing `PI` helper and `InitDefaults` "0"; no new write path, no new pure logic, hence no new unit tests (build + 172 existing tests + smoke).
