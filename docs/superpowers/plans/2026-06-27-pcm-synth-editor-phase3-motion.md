# PCM Synth Tone Editor — Phase 3 (Motion / LFOs) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the Motion tab — **LFO 1** and **LFO 2** panels (waveform + preview, rate, fade, key-trigger, and the four bipolar depths: Vibrato/Wah/Tremolo/Auto-pan), mirroring the SN-S LFO panel's look.

**Architecture:** PCM's LFOs differ from SN-S's (no tempo-sync; `Rate` is a value table; `Waveform`/`TVF Depth`/`TVA Depth` leaf names), so rather than overload `LfoPanelViewModel`, add a parallel **`PcmLfoPanelViewModel`** + **`PcmLfoPanelView`** that mirror the SN-S panel and reuse the existing `LfoWaveformControl` preview. `PCMPartialViewModel` gains `Lfo1`/`Lfo2`; the Motion tab renders them via `ContentControl` (ViewLocator resolves `PcmLfoPanelViewModel`→`PcmLfoPanelView` by name). The pure `LfoWaveform` sampler gains the `Saw Up`/`Saw Down` shapes PCM uses.

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if a running app holds the Debug exe lock.

---

## File Structure

- **Modify** `Src/Models/Services/LfoWaveform.cs` — add `Saw Up` / `Saw Down` cases.
- **Modify** `Tests/TestLfoWaveform.cs` — cover the two new shapes.
- **Create** `Src/ViewModels/PcmLfoPanelViewModel.cs` — one LFO's friendly params, prefix-parameterized (`LFO1 `/`LFO2 `).
- **Modify** `Src/ViewModels/PCMPartialViewModel.cs` — add `Lfo1`/`Lfo2`, include their params in `_editable`.
- **Create** `Src/Views/PcmLfoPanelView.axaml` (+ `.axaml.cs`) — mirrors `LfoPanelView`.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — Motion tab shows `Lfo1` + `Lfo2`.

---

## Task 1: LfoWaveform — Saw Up / Saw Down shapes

**Files:**
- Modify: `Src/Models/Services/LfoWaveform.cs`
- Modify: `Tests/TestLfoWaveform.cs`

PCM's `LFO_WAVEFORM` uses `Saw Up` / `Saw Down` (SN-S used `Sawtooth`). The preview's `YAt` currently has a `Sawtooth` case; add the two PCM names so the preview is correct (other PCM-only shapes fall back to the triangle default, which is acceptable for a decorative preview).

- [ ] **Step 1: Write the failing tests** — append inside the `TestLfoWaveform` class in `Tests/TestLfoWaveform.cs`:

```csharp
    [Test]
    public void SawUp_ramps_up()
    {
        var pts = LfoWaveform.Sample("Saw Up", 5);
        Assert.That(pts[0].Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void SawDown_ramps_down()
    {
        var pts = LfoWaveform.Sample("Saw Down", 5);
        Assert.That(pts[0].Y, Is.EqualTo(1).Within(1e-9));
        Assert.That(pts[^1].Y, Is.EqualTo(0).Within(1e-9));
    }
```

- [ ] **Step 2: Run to verify FAIL** (Saw Up/Down currently fall to the triangle default, so `pts[0].Y`/`pts[^1].Y` won't match):

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

- [ ] **Step 3: Implement** — in `Src/Models/Services/LfoWaveform.cs`, change the `Sawtooth` case and add the two PCM cases. Replace:

```csharp
            case "Sawtooth": return x;
```

with:

```csharp
            case "Sawtooth": case "Saw Up": return x;
            case "Saw Down": return 1.0 - x;
```

- [ ] **Step 4: Run to verify PASS** + full suite. Expected: PASS, +2 tests (214 → 216).

- [ ] **Step 5: Commit**

```
git add Src/Models/Services/LfoWaveform.cs Tests/TestLfoWaveform.cs
git commit -m "feat: LfoWaveform Saw Up / Saw Down shapes for the PCM LFO preview"
```

---

## Task 2: PcmLfoPanelViewModel + wire into the partial

**Files:**
- Create: `Src/ViewModels/PcmLfoPanelViewModel.cs`
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`

- [ ] **Step 1: Create `Src/ViewModels/PcmLfoPanelViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One PCM Synth LFO (LFO 1 or LFO 2) — the friendly subset: waveform, rate, fade-in,
/// key-trigger, and the four bipolar destination depths (Vibrato/Wah/Tremolo/Auto-pan).
/// Prefix-parameterized: "LFO1 " or "LFO2 ". Mirrors the SN-S LfoPanelViewModel.</summary>
public sealed class PcmLfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public string Title { get; }
    /// <summary>Roland leaf-name prefix ("LFO1 "/"LFO2 ") — drives tooltips.</summary>
    public string Prefix { get; }

    public ParamString Waveform { get; }
    public ParamString Rate { get; }       // value table (PARTIAL_DELAY_TIME) → combo
    public ParamInt FadeTime { get; }
    public ParamBool KeyTrigger { get; }
    public ParamInt PitchDepth { get; }    // Vibrato
    public ParamInt TvfDepth { get; }      // Wah
    public ParamInt TvaDepth { get; }      // Tremolo
    public ParamInt PanDepth { get; }      // Auto-pan

    public IReadOnlyList<IParam> Params { get; }

    public PcmLfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer,
        string prefix, string title)
    {
        Prefix = prefix;
        Title = title;

        ParamInt PI(string s, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + prefix + s], writer, min, max));
        ParamBool PB(string s) => Track(new ParamBool(partialDomain, byPath[PP + prefix + s], writer));
        ParamString PS(string s) => Track(new ParamString(partialDomain, byPath[PP + prefix + s], writer));

        Waveform = PS("Waveform");
        Rate = PS("Rate");
        FadeTime = PI("Fade Time", 0, 127);
        KeyTrigger = PB("Key Trigger");
        PitchDepth = PI("Pitch Depth", -63, 63);
        TvfDepth = PI("TVF Depth", -63, 63);
        TvaDepth = PI("TVA Depth", -63, 63);
        PanDepth = PI("Pan Depth", -63, 63);

        Params = new IParam[] { Waveform, Rate, FadeTime, KeyTrigger, PitchDepth, TvfDepth, TvaDepth, PanDepth };
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Wire `Lfo1`/`Lfo2` into `Src/ViewModels/PCMPartialViewModel.cs`.**

(a) Re-add the LINQ using (the editable set now concatenates the LFO params). Change the top usings to include:

```csharp
using System.Linq;
```

(immediately after `using System.Collections.Generic;`).

(b) Add the two properties, after the existing `public ParamBool IsOn { get; }` line:

```csharp
    // --- Motion (two LFOs) ---
    public PcmLfoPanelViewModel Lfo1 { get; }
    public PcmLfoPanelViewModel Lfo2 { get; }
```

(c) Construct them in the ctor, immediately AFTER the `IsOn = Track(...)` line and BEFORE the `_editable = new IParam[]` assignment:

```csharp
        Lfo1 = Track(new PcmLfoPanelViewModel(partialDomain, byPath, writer, "LFO1 ", "LFO 1"));
        Lfo2 = Track(new PcmLfoPanelViewModel(partialDomain, byPath, writer, "LFO2 ", "LFO 2"));
```

(d) Include the LFO params in the editable set. Change the end of the `_editable` initializer from:

```csharp
            BiasLevel, BiasPosition, BiasDirection,
        };
```

to:

```csharp
            BiasLevel, BiasPosition, BiasDirection,
        }
        .Concat(Lfo1.Params).Concat(Lfo2.Params).ToArray();
```

- [ ] **Step 3: Build**

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: Build succeeded. (`Track` already accepts any `IDisposable`, so it disposes the LFO panels too.)

- [ ] **Step 4: Run tests** — Expected: PASS (216).

- [ ] **Step 5: Commit**

```
git add Src/ViewModels/PcmLfoPanelViewModel.cs Src/ViewModels/PCMPartialViewModel.cs
git commit -m "feat: PCM LFO panel view-model + two LFOs per partial"
```

---

## Task 3: PcmLfoPanelView + Motion tab

**Files:**
- Create: `Src/Views/PcmLfoPanelView.axaml`
- Create: `Src/Views/PcmLfoPanelView.axaml.cs`
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

- [ ] **Step 1: Create `Src/Views/PcmLfoPanelView.axaml.cs`:**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class PcmLfoPanelView : UserControl
{
    public PcmLfoPanelView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Create `Src/Views/PcmLfoPanelView.axaml`** (mirrors `LfoPanelView`, swapping tempo-sync for the Rate combo and using PCM's leaf names):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:PcmLfoPanelViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.PcmLfoPanelView">
    <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
        <StackPanel Spacing="8">
            <TextBlock Text="{Binding Title}" FontWeight="Bold"/>

            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <TextBlock Text="Waveform" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Waveform'}"/>
                    <ComboBox ItemsSource="{Binding Waveform.Options}" SelectedItem="{Binding Waveform.Value, Mode=TwoWay}"/>
                </StackPanel>
                <controls:LfoWaveformControl Width="90" Height="44" VerticalAlignment="Bottom"
                    Shape="{Binding Waveform.Value}"
                    LineBrush="{StaticResource SnLfoWaveformBrush}"
                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                <StackPanel Spacing="2" Width="180">
                    <TextBlock Text="Rate" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Rate'}"/>
                    <ComboBox MaxDropDownHeight="320" HorizontalAlignment="Stretch"
                              ItemsSource="{Binding Rate.Options}" SelectedItem="{Binding Rate.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Fade-in" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Fade Time'}"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding FadeTime.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Restart on note" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Key Trigger'}"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding KeyTrigger.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <TextBlock Text="Motion amount (bipolar)" Opacity="0.7"/>
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Vibrato (pitch)" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Pitch Depth'}"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding PitchDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Wah (filter)" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}TVF Depth'}"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding TvfDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Tremolo (amp)" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}TVA Depth'}"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding TvaDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Auto-pan (pan)" ToolTip.Tip="{Binding Prefix, StringFormat='{}{0}Pan Depth'}"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding PanDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Replace the Motion tab body** in `Src/Views/PCMSynthToneEditorView.axaml`. Find:

```xml
                    <TabItem Header="Motion">
                        <TextBlock Margin="12" Opacity="0.6" Text="Motion — LFO 1 + LFO 2 (Phase 3)."/>
                    </TabItem>
```

Replace with:

```xml
                    <TabItem Header="Motion">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                            <StackPanel DataContext="{Binding SelectedPartial}" x:DataType="vm:PCMPartialViewModel"
                                        Spacing="12" Margin="0,0,0,8">
                                <ContentControl Content="{Binding Lfo1}"/>
                                <ContentControl Content="{Binding Lfo2}"/>
                            </StackPanel>
                        </ScrollViewer>
                    </TabItem>
```

- [ ] **Step 4: Build** — `… build … --configuration Release`. Expected: Build succeeded. (ViewLocator resolves `PcmLfoPanelViewModel`→`PcmLfoPanelView` by name; the `SnLfoWaveformBrush`/`SnEnvelope*Brush` resources already exist.)

- [ ] **Step 5: Run tests** — Expected: PASS (216).

- [ ] **Step 6: Commit**

```
git add Src/Views/PcmLfoPanelView.axaml Src/Views/PcmLfoPanelView.axaml.cs Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: PCM Motion tab (LFO 1 + LFO 2 panels)"
```

---

## Done criteria

- Full suite green (Phase 2c 214 + 2 new = 216).
- The Motion tab shows LFO 1 and LFO 2, each with a waveform combo + live shape preview, a rate combo, fade-in, restart-on-note, and the four bipolar depth sliders. Copy/Paste partial now also carries the LFO settings.
- Remaining engine phases: **Zones** (PMT 2-D map), **Response** (velocity/aftertouch/keyfollow). Step LFO + Offset/Rate-Detune/Delay/Fade-Mode stay on the Advanced — Partials tab.
