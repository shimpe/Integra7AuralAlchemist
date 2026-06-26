# SN-S Editor Phase 3 — Motion / LFO — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the two per-partial LFOs to the SN-S editor as musical "Motion" cards (Automatic Motion + Mod Wheel Motion) under a Sound/Motion sub-tab split.

**Architecture:** Reuse Phase 1/2 infrastructure (param wrappers, throttled writer, live FQP binding, color resources, custom-control pattern). One reusable `LfoPanelViewModel` + `LfoPanelView` renders both LFOs (prefix-parameterized). A small `LfoWaveformControl` previews the shape. The selected-partial editor gains a `TabControl` (Sound | Motion) with Copy/Paste/Init shared below.

**Tech Stack:** Avalonia 12, ReactiveUI (+ SourceGenerators), NUnit 3. Build/test from PowerShell at repo root with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-motion`.

**Spec:** `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase3-design.md`.

**Key facts (verified):**
- LFO partial params (`SuperNATURAL Synth Tone Partial/<name>`): Always-On uses `LFO Shape` (enum), `LFO Rate` (0–127), `LFO Tempo Sync Switch` (OFF/ON), `LFO Tempo Sync Note` (enum), `LFO Fade Time` (0–127), `LFO Key Trigger` (OFF/ON), `LFO Pitch Depth`/`LFO Filter Depth`/`LFO AMP Depth`/`LFO Pan Depth` (−63..63). Mod Wheel uses the `Modulation LFO ` prefix for the same suffixes plus `Modulation LFO Rate Control` (−63..63), and has no Fade/Key-Trigger.
- Wrappers (namespace `Integra7AuralAlchemist.ViewModels`): `ParamInt(domain, fqp, writer, min, max)` (`.Value`), `ParamString(domain, fqp, writer, options?)` (`.Value`, `.Options`), `ParamBool(domain, fqp, writer)` (`.Value`); all implement `IParam`/`IDisposable`. `ViewModelBase : ReactiveObject`.
- `SNSPartialViewModel` builds `var byPath = ToDict(partialDomain)` (path→FQP), has local `PI`/`PS`/helpers, a `Track<T>(T) where T:IDisposable` helper (adds to `_wrappers`), an `_editable` `IReadOnlyList<IParam>` assigned `new IParam[] { … }` ending with `PitchEnvDepth`, a static `InitDefaults` dict, `PP = "SuperNATURAL Synth Tone Partial/"`, and a `Dispose()` that disposes `_wrappers`.
- The editor view's right column is `<ScrollViewer Grid.Column="1" Name="RightScroll" …>` → `<Grid DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel" MinHeight="{Binding #RightScroll.Bounds.Height}" RowDefinitions="Auto,Auto,Auto,Auto,*,Auto">` with rows 0 Oscillator, 1 Pitch, 2 Filter, 3 Amp, 4 Combined-envelope (`*`), 5 Copy/Paste/Init StackPanel. The root UserControl is `x:Name="Root"`; editor-VM commands are bound `{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).XCommand}`.
- `ViewLocator` (app-level) maps any `ViewModelBase` → View by name (`LfoPanelViewModel` → `LfoPanelView`), so `<ContentControl Content="{Binding ...AutomaticMotion}"/>` renders `LfoPanelView`.

---

## Task 1: `LfoWaveform` (pure shape sampler)

**Files:**
- Create: `Src/Models/Services/LfoWaveform.cs`
- Test: `Tests/TestLfoWaveform.cs`

- [ ] **Step 1: Write the failing test** — `Tests/TestLfoWaveform.cs`:
```csharp
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class LfoWaveformTests
{
    [Test]
    public void Returns_count_points_spanning_x()
    {
        var pts = LfoWaveform.Sample("Triangle", 5);
        Assert.That(pts.Count, Is.EqualTo(5));
        Assert.That(pts.First().X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts.Last().X, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Sine_crosses_center_and_peaks()
    {
        var p = LfoWaveform.Sample("Sine", 5); // x = 0,.25,.5,.75,1
        Assert.That(p[0].Y, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(p[1].Y, Is.EqualTo(1.0).Within(1e-6));
        Assert.That(p[2].Y, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(p[3].Y, Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void Sawtooth_is_monotonic_rising()
    {
        var ys = LfoWaveform.Sample("Sawtooth", 8).Select(p => p.Y).ToList();
        for (var i = 1; i < ys.Count; i++) Assert.That(ys[i], Is.GreaterThanOrEqualTo(ys[i - 1]));
    }

    [Test]
    public void Square_has_two_levels()
    {
        var distinct = LfoWaveform.Sample("Square", 9).Select(p => p.Y).Distinct().ToList();
        Assert.That(distinct.Count, Is.EqualTo(2));
    }

    [Test]
    public void Triangle_peaks_in_the_middle()
    {
        var p = LfoWaveform.Sample("Triangle", 5);
        Assert.That(p[2].Y, Is.EqualTo(1.0).Within(1e-6)); // x=0.5 is the peak
        Assert.That(p[0].Y, Is.EqualTo(0.0).Within(1e-6));
        Assert.That(p[4].Y, Is.EqualTo(0.0).Within(1e-6));
    }

    [TestCase("Sample&Hold")]
    [TestCase("Random")]
    public void Stepped_shapes_are_deterministic_and_in_range(string shape)
    {
        var a = LfoWaveform.Sample(shape, 32);
        var b = LfoWaveform.Sample(shape, 32);
        Assert.That(a.Select(p => p.Y), Is.EqualTo(b.Select(p => p.Y))); // deterministic
        Assert.That(a.All(p => p.Y is >= 0 and <= 1), Is.True);
    }
}
```

- [ ] **Step 2: Run to confirm FAIL** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter LfoWaveformTests` → FAIL (type missing).

- [ ] **Step 3: Create `Src/Models/Services/LfoWaveform.cs`:**
```csharp
using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure, deterministic one-cycle waveform sampler for the LFO shape preview. Returns normalized
/// points: X 0..1 across one cycle, Y 0..1 with 0.5 = center. No RNG so it renders/tests stably.
/// </summary>
public static class LfoWaveform
{
    private static readonly double[] SampleHoldLevels = { 0.85, 0.30, 0.60, 0.10, 0.95, 0.45 };
    private static readonly double[] RandomLevels = { 0.50, 0.90, 0.20, 0.70, 0.35, 0.62, 0.15, 0.80 };

    public static IReadOnlyList<(double X, double Y)> Sample(string shape, int count)
    {
        var pts = new List<(double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            var x = count <= 1 ? 0.0 : i / (double)(count - 1);
            pts.Add((x, Clamp01(YAt(shape, x))));
        }
        return pts;
    }

    private static double YAt(string shape, double x)
    {
        switch (shape)
        {
            case "Sine": return 0.5 + 0.5 * Math.Sin(2 * Math.PI * x);
            case "Sawtooth": return x;
            case "Square": return x < 0.5 ? 1.0 : 0.0;
            case "Sample&Hold": return SampleHoldLevels[StepIndex(x, SampleHoldLevels.Length)];
            case "Random": return RandomLevels[StepIndex(x, RandomLevels.Length)];
            default: return x < 0.5 ? x * 2.0 : 2.0 - x * 2.0; // Triangle
        }
    }

    private static int StepIndex(double x, int len)
    {
        var i = (int)(x * len);
        return i < 0 ? 0 : i >= len ? len - 1 : i;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
```
(Note: `StepIndex` clamps `x==1.0` to the last level — a stable last step rather than wrapping.)

- [ ] **Step 4: Run to confirm PASS** — same filter → all pass.

- [ ] **Step 5: Commit**
```
git add Src/Models/Services/LfoWaveform.cs Tests/TestLfoWaveform.cs
git commit -m "feat(sns): pure LFO waveform sampler with tests"
```

---

## Task 2: `LfoWaveformControl` + brush resource

**Files:**
- Modify: `Src/App.axaml`
- Create: `Src/Controls/LfoWaveformControl.cs`

- [ ] **Step 1: Add a brush** to `Src/App.axaml` — after `SnPitchEnvelopeFillBrush`, inside the `<ResourceDictionary>`:
```xml
            <SolidColorBrush x:Key="SnLfoWaveformBrush" Color="#4FB0A0" />
```

- [ ] **Step 2: Create `Src/Controls/LfoWaveformControl.cs`:**
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Non-interactive one-cycle preview of an LFO shape. Shape geometry from <see cref="LfoWaveform"/>.</summary>
public class LfoWaveformControl : Control
{
    private const int Samples = 48;

    public static readonly StyledProperty<string> ShapeProperty =
        AvaloniaProperty.Register<LfoWaveformControl, string>(nameof(Shape), "Triangle");

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<LfoWaveformControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#4FB0A0")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public string Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    static LfoWaveformControl()
    {
        AffectsRender<LfoWaveformControl>(ShapeProperty, LineBrushProperty, BackgroundBrushProperty, AxisBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        context.DrawLine(new Pen(AxisBrush), new Point(0, h / 2), new Point(w, h / 2));

        var pts = LfoWaveform.Sample(Shape, Samples);
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            var first = true;
            foreach (var p in pts)
            {
                var pt = new Point(p.X * w, (1 - p.Y) * h);
                if (first) { c.BeginFigure(pt, false); first = false; } else c.LineTo(pt);
            }
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), geo);
    }
}
```

- [ ] **Step 3: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)` (only `error CS/AVLN/XAMLIL` count; exe copy-lock is fine). Fix any Avalonia-12 API mismatch minimally and report.

- [ ] **Step 4: Commit**
```
git add Src/App.axaml Src/Controls/LfoWaveformControl.cs
git commit -m "feat(sns): LfoWaveformControl shape preview + brush resource"
```

---

## Task 3: `LfoPanelViewModel` (reusable LFO view model)

**Files:**
- Create: `Src/ViewModels/LfoPanelViewModel.cs`

- [ ] **Step 1: Create `Src/ViewModels/LfoPanelViewModel.cs`:**
```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>
/// One LFO ("Automatic Motion" or "Mod Wheel Motion"). Prefix-parameterized so the same panel
/// builds both: prefix "LFO " or "Modulation LFO ". Always-On has Fade + Key Trigger; Mod Wheel
/// has Rate Control. Bipolar destination depths are Vibrato/Wah/Tremolo/Auto-pan.
/// </summary>
public sealed class LfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public string Title { get; }
    public bool IsModWheel { get; }
    public bool HasFade => !IsModWheel;
    public bool HasKeyTrigger => !IsModWheel;
    public bool HasRateControl => IsModWheel;

    public ParamString Shape { get; }
    public ParamInt Rate { get; }
    public ParamBool TempoSync { get; }
    public ParamString TempoSyncNote { get; }
    public ParamInt PitchDepth { get; }   // Vibrato
    public ParamInt FilterDepth { get; }  // Wah
    public ParamInt AmpDepth { get; }     // Tremolo
    public ParamInt PanDepth { get; }     // Auto-pan
    public ParamInt? FadeTime { get; }
    public ParamBool? KeyTrigger { get; }
    public ParamInt? RateControl { get; }

    public IReadOnlyList<IParam> Params { get; }

    public LfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer,
        string prefix, string title, bool isModWheel)
    {
        Title = title;
        IsModWheel = isModWheel;

        ParamInt PI(string suffix, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + prefix + suffix], writer, min, max));
        ParamBool PB(string suffix) => Track(new ParamBool(partialDomain, byPath[PP + prefix + suffix], writer));
        ParamString PS(string suffix) => Track(new ParamString(partialDomain, byPath[PP + prefix + suffix], writer));

        Shape = PS("Shape");
        Rate = PI("Rate", 0, 127);
        TempoSync = PB("Tempo Sync Switch");
        TempoSyncNote = PS("Tempo Sync Note");
        PitchDepth = PI("Pitch Depth", -63, 63);
        FilterDepth = PI("Filter Depth", -63, 63);
        AmpDepth = PI("AMP Depth", -63, 63);
        PanDepth = PI("Pan Depth", -63, 63);

        var ps = new List<IParam> { Shape, Rate, TempoSync, TempoSyncNote, PitchDepth, FilterDepth, AmpDepth, PanDepth };
        if (isModWheel)
        {
            RateControl = PI("Rate Control", -63, 63);
            ps.Add(RateControl);
        }
        else
        {
            FadeTime = PI("Fade Time", 0, 127);
            KeyTrigger = PB("Key Trigger");
            ps.Add(FadeTime);
            ps.Add(KeyTrigger);
        }
        Params = ps;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)`.

- [ ] **Step 3: Commit**
```
git add Src/ViewModels/LfoPanelViewModel.cs
git commit -m "feat(sns): reusable LfoPanelViewModel (always-on + mod-wheel)"
```

---

## Task 4: `LfoPanelView` (reusable LFO card view)

**Files:**
- Create: `Src/Views/LfoPanelView.axaml`
- Create: `Src/Views/LfoPanelView.axaml.cs`

- [ ] **Step 1: Create the code-behind** — `Src/Views/LfoPanelView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class LfoPanelView : UserControl
{
    public LfoPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Create the view** — `Src/Views/LfoPanelView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:LfoPanelViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.LfoPanelView">
    <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
        <StackPanel Spacing="8">
            <TextBlock Text="{Binding Title}" FontWeight="Bold"/>

            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <TextBlock Text="Shape" ToolTip.Tip="LFO Shape"/>
                    <ComboBox ItemsSource="{Binding Shape.Options}" SelectedItem="{Binding Shape.Value, Mode=TwoWay}"/>
                </StackPanel>
                <controls:LfoWaveformControl Width="90" Height="44" VerticalAlignment="Bottom"
                    Shape="{Binding Shape.Value}"
                    LineBrush="{StaticResource SnLfoWaveformBrush}"
                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                <StackPanel Spacing="2">
                    <TextBlock Text="Tempo Sync" ToolTip.Tip="LFO Tempo Sync Switch"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding TempoSync.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="160" IsVisible="{Binding !TempoSync.Value}">
                    <TextBlock Text="Rate" ToolTip.Tip="LFO Rate"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Rate.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" IsVisible="{Binding TempoSync.Value}">
                    <TextBlock Text="Note" ToolTip.Tip="LFO Tempo Sync Note"/>
                    <ComboBox ItemsSource="{Binding TempoSyncNote.Options}" SelectedItem="{Binding TempoSyncNote.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <!-- Automatic Motion only -->
            <StackPanel Orientation="Horizontal" Spacing="16" IsVisible="{Binding HasFade}">
                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Fade-in" ToolTip.Tip="LFO Fade Time"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding FadeTime.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Restart on note" ToolTip.Tip="LFO Key Trigger"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding KeyTrigger.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>

            <!-- Mod Wheel Motion only -->
            <StackPanel Spacing="2" Width="220" IsVisible="{Binding HasRateControl}">
                <TextBlock Text="Wheel changes speed" ToolTip.Tip="Modulation LFO Rate Control"/>
                <Slider Minimum="-63" Maximum="63" Value="{Binding RateControl.Value, Mode=TwoWay}"/>
            </StackPanel>

            <!-- Destination depths -->
            <TextBlock Text="Motion amount (bipolar)" Opacity="0.7"/>
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Vibrato (pitch)" ToolTip.Tip="LFO Pitch Depth"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding PitchDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Wah (filter)" ToolTip.Tip="LFO Filter Depth"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding FilterDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Tremolo (amp)" ToolTip.Tip="LFO AMP Depth"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding AmpDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Auto-pan (pan)" ToolTip.Tip="LFO Pan Depth"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding PanDepth.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)`.
  - Likely risk: the compiled-binding negation `{Binding !TempoSync.Value}`. Avalonia 12 compiled bindings support the leading-`!` boolean negation on a path. If it errors, the minimal fix is to bind the *Rate* panel's `IsVisible` to a new VM bool (add `public bool RateVisible => !TempoSync.Value;` raised when `TempoSync.Value` changes) — but try `!` first. The nullable members (`FadeTime`/`KeyTrigger`/`RateControl`) are only bound inside panels gated by `HasFade`/`HasRateControl`, so their `.Value` is never read when null; compiled bindings null-guard the path. If you cannot resolve a real error, report BLOCKED with the exact text.

- [ ] **Step 4: Commit**
```
git add Src/Views/LfoPanelView.axaml Src/Views/LfoPanelView.axaml.cs
git commit -m "feat(sns): reusable LfoPanelView (shape preview, sync rate/note, depths)"
```

---

## Task 5: `SNSPartialViewModel` — two Motion panels

**Files:**
- Modify: `Src/ViewModels/SNSPartialViewModel.cs`

**Read the file first** to anchor edits.

- [ ] **Step 1: Add `using System.Linq;`** at the top of `Src/ViewModels/SNSPartialViewModel.cs` if not already present (needed for `.Concat(...).ToArray()` below).

- [ ] **Step 2: Add the panel properties** (e.g. after the pitch-envelope properties `PitchEnvDepth`):
```csharp
    // --- Motion (two LFOs) ---
    public LfoPanelViewModel AutomaticMotion { get; }
    public LfoPanelViewModel ModWheelMotion { get; }
```

- [ ] **Step 3: Construct the panels** in the constructor, immediately BEFORE the `_editable = new IParam[] { … };` statement (so they exist when the editable set is built). `partialDomain`, `byPath`, and `writer` are the constructor's parameter names already used by `PI`/`PS`:
```csharp
        AutomaticMotion = Track(new LfoPanelViewModel(partialDomain, byPath, writer, "LFO ", "Automatic Motion", false));
        ModWheelMotion = Track(new LfoPanelViewModel(partialDomain, byPath, writer, "Modulation LFO ", "Mod Wheel Motion", true));
```
(`Track` accepts any `IDisposable`; `LfoPanelViewModel` is `IDisposable`, so both panels are disposed with the partial.)

- [ ] **Step 4: Fold the LFO params into `_editable`.** Find the end of the `_editable` initializer (it ends with `PitchEnvDepth` then `};`):
```csharp
            PitchEnvAttack, PitchEnvDecay, PitchEnvDepth
        };
```
Replace with (append both panels' params):
```csharp
            PitchEnvAttack, PitchEnvDecay, PitchEnvDepth
        }
        .Concat(AutomaticMotion.Params).Concat(ModWheelMotion.Params).ToArray();
```

- [ ] **Step 5: Add LFO defaults** to the static `InitDefaults` dictionary (append, valid comma placement):
```csharp
        ,[PP + "LFO Shape"] = "Triangle",
        [PP + "LFO Rate"] = "64",
        [PP + "LFO Tempo Sync Switch"] = "OFF",
        [PP + "LFO Tempo Sync Note"] = "1/4",
        [PP + "LFO Fade Time"] = "0",
        [PP + "LFO Key Trigger"] = "OFF",
        [PP + "LFO Pitch Depth"] = "0",
        [PP + "LFO Filter Depth"] = "0",
        [PP + "LFO AMP Depth"] = "0",
        [PP + "LFO Pan Depth"] = "0",
        [PP + "Modulation LFO Shape"] = "Triangle",
        [PP + "Modulation LFO Rate"] = "64",
        [PP + "Modulation LFO Tempo Sync Switch"] = "OFF",
        [PP + "Modulation LFO Tempo Sync Note"] = "1/4",
        [PP + "Modulation LFO Pitch Depth"] = "0",
        [PP + "Modulation LFO Filter Depth"] = "0",
        [PP + "Modulation LFO AMP Depth"] = "0",
        [PP + "Modulation LFO Pan Depth"] = "0",
        [PP + "Modulation LFO Rate Control"] = "0"
```

- [ ] **Step 6: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)`. Fix any comma/initializer error minimally; report BLOCKED if unresolved.

- [ ] **Step 7: Commit**
```
git add Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat(sns): partial VM Automatic + Mod-Wheel motion panels (+ copy/paste/init)"
```

---

## Task 6: View — Sound / Motion sub-tabs

**Files:**
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

**Read the file first.** Goal: wrap the right column's content in a `TabControl` (Sound | Motion), with Copy/Paste/Init moved out to a shared row below the tabs.

- [ ] **Step 1: Replace the right-column ScrollViewer+Grid opening** so the existing sections become the Sound tab. Find:
```xml
            <!-- Right column: sections stack; the Combined Envelope row (*) grows to fill the window. -->
            <ScrollViewer Grid.Column="1" Name="RightScroll"
                          VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <!-- MinHeight = viewport so the envelope row (*) fills a tall window, while a short
                     window scrolls instead of overlapping the buttons. -->
                <Grid DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                      MinHeight="{Binding #RightScroll.Bounds.Height}"
                      RowDefinitions="Auto,Auto,Auto,Auto,*,Auto">
```
Replace with:
```xml
            <!-- Right column: Sound / Motion sub-tabs; Copy/Paste/Init shared below. -->
            <Grid Grid.Column="1" RowDefinitions="*,Auto">
                <TabControl Grid.Row="0">
                    <TabItem Header="Sound">
                        <ScrollViewer Name="SoundScroll"
                                      VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                            <!-- MinHeight = viewport so the envelope row (*) fills a tall window. -->
                            <Grid DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                                  MinHeight="{Binding #SoundScroll.Bounds.Height}"
                                  RowDefinitions="Auto,Auto,Auto,Auto,*">
```
(Note: the row list drops the trailing `,Auto` because the buttons move out; rows are now 0 Oscillator … 4 Combined-envelope.)

- [ ] **Step 2: Remove the Copy/Paste/Init StackPanel from inside the Sound grid.** Find and DELETE this block (it currently sits at `Grid.Row="5"` inside that grid):
```xml
                <!-- Partial utilities -->
                <StackPanel Grid.Row="5" Orientation="Horizontal" Spacing="8" Margin="0,12,0,0">
                    <Button Content="Copy partial"
                            Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).CopyPartialCommand}"/>
                    <Button Content="Paste partial"
                            Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).PastePartialCommand}"/>
                    <Button Content="Init partial"
                            Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).InitPartialCommand}"/>
                </StackPanel>
```

- [ ] **Step 3: Replace the closing of the old ScrollViewer+Grid** with the close of the Sound grid/tab + the Motion tab + the shared buttons. Find the closing tags that currently end the right column (the inner `</Grid>` that closed the sections grid, then `</ScrollViewer>`):
```xml
                </Grid>
            </ScrollViewer>
        </Grid>
    </Grid>
</UserControl>
```
Replace with:
```xml
                            </Grid>
                        </ScrollViewer>
                    </TabItem>
                    <TabItem Header="Motion">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                            <StackPanel DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                                        Spacing="12" Margin="0,0,0,8">
                                <ContentControl Content="{Binding AutomaticMotion}"/>
                                <ContentControl Content="{Binding ModWheelMotion}"/>
                            </StackPanel>
                        </ScrollViewer>
                    </TabItem>
                </TabControl>
                <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
                    <Button Content="Copy partial" Command="{Binding CopyPartialCommand}"/>
                    <Button Content="Paste partial" Command="{Binding PastePartialCommand}"/>
                    <Button Content="Init partial" Command="{Binding InitPartialCommand}"/>
                </StackPanel>
            </Grid>
        </Grid>
    </Grid>
</UserControl>
```
(The shared buttons are now in the editor-VM DataContext scope — the outer `Grid Grid.Row="1" ColumnDefinitions="240,*"` inherits the root editor VM — so they bind directly to `CopyPartialCommand` etc. without the `#Root` cast. The `ContentControl`s render `LfoPanelView` via the app `ViewLocator`.)

- [ ] **Step 4: Build the whole solution** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo` → `0 Error(s)`. The most likely issues: an unbalanced tag from the wrap (recount the closing tags), or the `CopyPartialCommand` scope. Fix minimally; if the buttons don't resolve, fall back to the `#Root.((vm:SNSynthToneEditorViewModel)DataContext).CopyPartialCommand` form used elsewhere. Report BLOCKED with exact text if unresolved.

- [ ] **Step 5: Commit**
```
git add Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): Sound/Motion sub-tabs with the two LFO cards; shared utilities row"
```

---

## Task 7: Full verification + smoke

**Files:** none.

- [ ] **Step 1: Run the full test suite** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --nologo` → all pass (incl. `LfoWaveformTests`).
- [ ] **Step 2: Build the whole solution** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo` → `0 Error(s)`.
- [ ] **Step 3: Manual smoke (requires the connected Integra-7; pause for the user).** Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj`. Confirm:
  1. The partial editor shows **Sound** and **Motion** tabs; Sound is unchanged; Copy/Paste/Init work from below both tabs.
  2. **Motion** shows two cards (Automatic Motion, Mod Wheel Motion). Shape combo updates the waveform preview; Tempo Sync toggles Rate↔Note; Automatic shows Fade-in + Restart-on-note; Mod Wheel shows Wheel-changes-speed; the four depth sliders (Vibrato/Wah/Tremolo/Auto-pan) are bipolar.
  3. Editing sends to the device; hardware edits update the controls.
  4. Init/Copy/Paste carry the LFO params.
  5. No layout regressions.
- [ ] **Step 4: Verify against the spec §8 acceptance criteria; note any deviations.**

---

## Self-review notes (for the executor)
- The two LFOs are the same panel, prefix-parameterized (`"LFO "` / `"Modulation LFO "`); only the extras differ (Fade+KeyTrigger vs RateControl), gated by `HasFade`/`HasRateControl`.
- `ContentControl` + the app `ViewLocator` renders `LfoPanelView` from `LfoPanelViewModel` — no `xmlns` for it needed in the editor view.
- Buttons moved out of the per-partial scroll into the editor-VM scope, so they bind directly to `CopyPartialCommand`/`PastePartialCommand`/`InitPartialCommand`.
- All new colors come from `App.axaml` (`SnLfoWaveformBrush`, reused envelope brushes); none hardcoded in XAML.
