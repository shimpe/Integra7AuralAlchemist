# SN-S Editor Phase 2b — Pitch Envelope + Tone-wide Pitch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a graphical bipolar **Pitch envelope** (Attack/Decay + Depth around a center line) to the SN-S editor, plus the tone-wide **Octave Shift** and **Pitch Bend Up/Down** controls.

**Architecture:** Reuse the Phase 1/2a infrastructure (param wrappers, throttled writer, `SnsEnvelopeMapping`, color resources, the `Preview` mode pattern). Add bipolar mapping helpers, a new `PitchEnvelopeControl`, the partial-VM pitch-envelope params, the header-VM octave/bend params, and the view's Pitch section.

**Tech Stack:** Avalonia 12, ReactiveUI (+ SourceGenerators), NUnit 3. Build/test from PowerShell at repo root with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-editor`.

**Spec:** `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase2-design.md` (this is the remaining Phase-2 scope; the filter-response curve was delivered in Phase 2a).

**Key facts (verified, from memory `sn-s-tone-parameters` / existing code):**
- Pitch envelope partial params (`SuperNATURAL Synth Tone Partial/…`): `OSC Pitch Env Attack Time` (0–127), `OSC Pitch Env Decay` (0–127), `OSC Pitch Env Depth` (−63..63).
- Tone-wide common params (`SuperNATURAL Synth Tone Common/…`): `Octave Shift` (−3..+3), `Pitch Bend Range Up` (0–24), `Pitch Bend Range Down` (0–24).
- Wrappers (namespace `Integra7AuralAlchemist.ViewModels`): `ParamInt(domain, fqp, writer, min, max)` (`.Value`). `SNSPartialViewModel` has local `PI(name,min,max)` + `Track(...)` helpers, an `_editable` `IParam[]`, a static `InitDefaults` dict, `PP = "SuperNATURAL Synth Tone Partial/"`.
- `SnsEnvelopeMapping` (pure) has `record struct Point(double X,double Y)`, `Clamp(int)` (0..127), `TimeToWidth(int,double)`, `TimeFromWidth(double,double)`. Controls live in `Src/Controls/`. The `Preview`-mode + brush-StyledProperty pattern is established (see `DualAdsrEnvelopeControl`, `FilterCurveControl`).
- Editor view right column is a `Grid` (`RowDefinitions="Auto,Auto,Auto,*,Auto"`): Oscillator(0), Filter(1), Amp(2), Combined envelope(3,*), buttons(4), inside a `ScrollViewer Name="RightScroll"`. The grid `DataContext="{Binding SelectedPartial}"`. Editor-VM members are reached via `{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).X}`.

**Layout decision (deviation from spec, noted):** Octave Shift + Pitch Bend Up/Down are tone-wide; to avoid overflowing the already-full header, they live in the **Pitch section** (clearly labelled "whole tone"), bound to the header VM via the `#Root` editor-VM path. Pitch is inserted as a new right-column row after Oscillator.

---

## Task 1: Bipolar pitch mapping helpers (`SnsEnvelopeMapping`)

**Files:**
- Modify: `Src/Models/Services/SnsEnvelopeMapping.cs`
- Test: `Tests/TestPitchEnvelopeMapping.cs`

- [ ] **Step 1: Write the failing test** — `Tests/TestPitchEnvelopeMapping.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class PitchEnvelopeMappingTests
{
    [TestCase(0, 200.0, 100.0)]
    [TestCase(63, 200.0, 0.0)]
    [TestCase(-63, 200.0, 200.0)]
    public void BipolarToY_centers_zero_and_inverts(int value, double h, double expected)
        => Assert.That(SnsEnvelopeMapping.BipolarToY(value, h), Is.EqualTo(expected).Within(1e-9));

    [TestCase(-63)]
    [TestCase(-20)]
    [TestCase(0)]
    [TestCase(31)]
    [TestCase(63)]
    public void Bipolar_y_roundtrip(int value)
    {
        var y = SnsEnvelopeMapping.BipolarToY(value, 200.0);
        Assert.That(SnsEnvelopeMapping.BipolarFromY(y, 200.0), Is.EqualTo(value));
    }

    [TestCase(200, 63)]
    [TestCase(-200, -63)]
    [TestCase(40, 40)]
    public void ClampBipolar_limits_to_63(int value, int expected)
        => Assert.That(SnsEnvelopeMapping.ClampBipolar(value), Is.EqualTo(expected));

    [Test]
    public void ComputePitchPoints_places_center_peak_center()
    {
        // w=200 -> seg=100; attack 127 -> aW=100; decay 127 -> dW=100; depth 63 -> peak at top.
        var p = SnsEnvelopeMapping.ComputePitchPoints(127, 127, 63, 200, 200);
        Assert.That(p.Start.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.Start.Y, Is.EqualTo(100).Within(1e-9));   // center
        Assert.That(p.Peak.X, Is.EqualTo(100).Within(1e-9));
        Assert.That(p.Peak.Y, Is.EqualTo(0).Within(1e-9));      // +63 -> top
        Assert.That(p.End.X, Is.EqualTo(200).Within(1e-9));
        Assert.That(p.End.Y, Is.EqualTo(100).Within(1e-9));     // back to center
    }
}
```

- [ ] **Step 2: Run to confirm FAIL** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter PitchEnvelopeMappingTests` → FAIL (members don't exist).

- [ ] **Step 3: Add the members** inside the `SnsEnvelopeMapping` class (before its closing brace) in `Src/Models/Services/SnsEnvelopeMapping.cs`:
```csharp
    public const int BipolarMax = 63;

    public static int ClampBipolar(int v) => v < -BipolarMax ? -BipolarMax : v > BipolarMax ? BipolarMax : v;

    /// <summary>Bipolar value (−63..+63) → Y pixel. 0 = center, +63 = top (0), −63 = bottom (height).</summary>
    public static double BipolarToY(int value, double height) =>
        height / 2.0 - ClampBipolar(value) / (double)BipolarMax * (height / 2.0);

    public static int BipolarFromY(double y, double height)
    {
        if (height <= 0) return 0;
        var v = (height / 2.0 - y) / (height / 2.0) * BipolarMax;
        return ClampBipolar((int)Math.Round(v, MidpointRounding.AwayFromZero));
    }

    /// <summary>Two time segments (attack, decay) share the width.</summary>
    public static double PitchSegmentMax(double width) => width / 2.0;

    public readonly record struct PitchPoints(Point Start, Point Peak, Point End);

    /// <summary>Bipolar AD pitch envelope: center → depth target (attack) → back to center (decay).</summary>
    public static PitchPoints ComputePitchPoints(int attack, int decay, int depth, double width, double height)
    {
        var seg = PitchSegmentMax(width);
        var aW = TimeToWidth(attack, seg);
        var dW = TimeToWidth(decay, seg);
        var cy = height / 2.0;
        return new PitchPoints(new Point(0, cy), new Point(aW, BipolarToY(depth, height)), new Point(aW + dW, cy));
    }

    public static int PitchAttackFromX(double x, double segMax) => TimeFromWidth(x, segMax);
    public static int PitchDecayFromX(double x, double attackWidth, double segMax) => TimeFromWidth(x - attackWidth, segMax);
```
(`System` is already used by the class for `Math.Round`.)

- [ ] **Step 4: Run to confirm PASS** — same filter command → all pass.

- [ ] **Step 5: Commit**
```
git add Src/Models/Services/SnsEnvelopeMapping.cs Tests/TestPitchEnvelopeMapping.cs
git commit -m "feat(sns): bipolar pitch-envelope mapping helpers with tests"
```

---

## Task 2: `PitchEnvelopeControl`

**Files:**
- Create: `Src/Controls/PitchEnvelopeControl.cs`

- [ ] **Step 1: Create the control** — `Src/Controls/PitchEnvelopeControl.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Bipolar Attack/Decay pitch envelope. Center line = no pitch change; up = sharp, down = flat.
/// Drag the peak handle (X = Attack, Y = Depth) or the decay handle (X = Decay). Arrow keys + a
/// non-interactive Preview mode. Math is in <see cref="SnsEnvelopeMapping"/>.
/// </summary>
public class PitchEnvelopeControl : Control
{
    private const double HandleRadius = 7;

    public static readonly StyledProperty<int> AttackProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Attack), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DecayProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Decay), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Depth), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, bool>(nameof(Preview));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<PitchEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#9C7BE0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty = B(nameof(FillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x6a, 0x4f, 0xb0)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> FocusBrushProperty = B(nameof(FocusBrush), Brushes.Orange);

    public int Attack { get => GetValue(AttackProperty); set => SetValue(AttackProperty, value); }
    public int Decay { get => GetValue(DecayProperty); set => SetValue(DecayProperty, value); }
    public int Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public IBrush FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    private int _drag = -1, _focused = 0; // 0 = peak (attack/depth), 1 = decay

    static PitchEnvelopeControl()
    {
        AffectsRender<PitchEnvelopeControl>(AttackProperty, DecayProperty, DepthProperty, PreviewProperty,
            LineBrushProperty, FillBrushProperty, BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty,
            HandleBrushProperty, FocusBrushProperty);
        FocusableProperty.OverrideDefaultValue<PitchEnvelopeControl>(true);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var cy = h / 2.0;
        context.DrawLine(gridPen, new Point(0, h * 0.25), new Point(w, h * 0.25));
        context.DrawLine(gridPen, new Point(0, h * 0.75), new Point(w, h * 0.75));
        context.DrawLine(axisPen, new Point(0, cy), new Point(w, cy)); // center = no pitch change
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));

        var p = SnsEnvelopeMapping.ComputePitchPoints(Attack, Decay, Depth, w, h);

        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(p.Start.X, cy), true);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.End.X, cy));
            c.EndFigure(true);
        }
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.End.X, p.End.Y));
            c.LineTo(new Point(w, cy));
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), line);

        if (Preview) return;
        DrawHandle(context, p.Peak, _focused == 0);
        DrawHandle(context, p.End, _focused == 1);
    }

    private void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? new Pen(FocusBrush, 2) : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return;
        Focus();
        var pos = e.GetPosition(this);
        _drag = Nearest(pos, SnsEnvelopeMapping.ComputePitchPoints(Attack, Decay, Depth, Bounds.Width, Bounds.Height));
        if (_drag < 0) return;
        _focused = _drag;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.PitchSegmentMax(Bounds.Width);
        if (_drag == 0)
        {
            Attack = SnsEnvelopeMapping.PitchAttackFromX(pos.X, seg);
            Depth = SnsEnvelopeMapping.BipolarFromY(pos.Y, Bounds.Height);
        }
        else
        {
            var aW = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
            Decay = SnsEnvelopeMapping.PitchDecayFromX(pos.X, aW, seg);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        e.Pointer.Capture(null);
        _drag = -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Preview) return;
        switch (e.Key)
        {
            case Key.Tab:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _focused == 1) break;
                _focused = 1; e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        if (_focused == 0)
        {
            if (vertical) Depth = SnsEnvelopeMapping.ClampBipolar(Depth + delta);
            else Attack = SnsEnvelopeMapping.Clamp(Attack + delta);
        }
        else if (!vertical)
        {
            Decay = SnsEnvelopeMapping.Clamp(Decay + delta);
        }
    }

    private static int Nearest(Point pos, SnsEnvelopeMapping.PitchPoints p)
    {
        var best = -1;
        var bestD = HandleRadius * HandleRadius * 4;
        Check(0, p.Peak);
        Check(1, p.End);
        return best;

        void Check(int i, SnsEnvelopeMapping.Point pt)
        {
            var dx = pos.X - pt.X; var dy = pos.Y - pt.Y; var d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
    }
}
```

- [ ] **Step 2: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)` (only `error CS/AVLN/XAMLIL` count; exe copy-lock is fine). Fix any Avalonia-12 API mismatch minimally and report.

- [ ] **Step 3: Commit**
```
git add Src/Controls/PitchEnvelopeControl.cs
git commit -m "feat(sns): bipolar PitchEnvelopeControl (drag attack/decay/depth)"
```

---

## Task 3: `SNSPartialViewModel` — pitch-envelope params

**Files:**
- Modify: `Src/ViewModels/SNSPartialViewModel.cs`

- [ ] **Step 1: Add the properties** (next to the filter-envelope properties):
```csharp
    // --- Pitch envelope (bipolar AD) ---
    public ParamInt PitchEnvAttack { get; }
    public ParamInt PitchEnvDecay { get; }
    public ParamInt PitchEnvDepth { get; }
```

- [ ] **Step 2: Construct them** in the constructor (after the filter-envelope `FilterEnv*` assignments):
```csharp
        PitchEnvAttack = PI("OSC Pitch Env Attack Time", 0, 127);
        PitchEnvDecay = PI("OSC Pitch Env Decay", 0, 127);
        PitchEnvDepth = PI("OSC Pitch Env Depth", -63, 63);
```

- [ ] **Step 3: Add to the `_editable` array** (append after the filter-env entries):
```csharp
            ,PitchEnvAttack, PitchEnvDecay, PitchEnvDepth
```

- [ ] **Step 4: Add to `InitDefaults`** (append, valid comma placement):
```csharp
        ,[PP + "OSC Pitch Env Attack Time"] = "0",
        [PP + "OSC Pitch Env Decay"] = "64",
        [PP + "OSC Pitch Env Depth"] = "0"
```

- [ ] **Step 5: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)`.

- [ ] **Step 6: Commit**
```
git add Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat(sns): partial VM pitch-envelope params"
```

---

## Task 4: `SNSynthToneHeaderViewModel` — octave + pitch bend

**Files:**
- Modify: `Src/ViewModels/SNSynthToneHeaderViewModel.cs`

- [ ] **Step 1: Add the properties** (after `Ring`):
```csharp
    public ParamInt OctaveShift { get; }
    public ParamInt PitchBendUp { get; }
    public ParamInt PitchBendDown { get; }
```

- [ ] **Step 2: Construct them** in the constructor (after `Ring = PS("Ring Switch");`):
```csharp
        OctaveShift = PI("Octave Shift", -3, 3);
        PitchBendUp = PI("Pitch Bend Range Up", 0, 24);
        PitchBendDown = PI("Pitch Bend Range Down", 0, 24);
```

- [ ] **Step 3: Build** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo` → `0 Error(s)`.

- [ ] **Step 4: Commit**
```
git add Src/ViewModels/SNSynthToneHeaderViewModel.cs
git commit -m "feat(sns): header VM octave shift + pitch bend up/down"
```

---

## Task 5: View — Pitch section (pitch envelope + tone-wide octave/bend)

**Files:**
- Modify: `Src/App.axaml` (pitch brushes)
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

- [ ] **Step 1: Add pitch color resources** to `Src/App.axaml` — after the `SnFilterEnvelopeFillBrush` line, inside the `<ResourceDictionary>`:
```xml
            <SolidColorBrush x:Key="SnPitchEnvelopeBrush" Color="#9C7BE0" />
            <SolidColorBrush x:Key="SnPitchEnvelopeFillBrush" Color="#556A4FB0" />
```

- [ ] **Step 2: Add a row for the Pitch section** in the right-column grid. Find:
```xml
                <Grid DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                      MinHeight="{Binding #RightScroll.Bounds.Height}"
                      RowDefinitions="Auto,Auto,Auto,*,Auto">
```
Change `RowDefinitions` to `"Auto,Auto,Auto,Auto,*,Auto"`. Then renumber the existing borders' `Grid.Row`: the **Filter** border `Grid.Row="1"` → `Grid.Row="2"`; the **Amp** border `Grid.Row="2"` → `Grid.Row="3"`; the **Combined envelope** border `Grid.Row="3"` → `Grid.Row="4"`; the **Copy/Paste/Init** StackPanel `Grid.Row="4"` → `Grid.Row="5"`. (Oscillator stays `Grid.Row="0"`.)

- [ ] **Step 3: Insert the Pitch border** at `Grid.Row="1"` (between the Oscillator border and the Filter border):
```xml
                <!-- Pitch -->
                <Border Grid.Row="1" Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10" Margin="0,0,0,12">
                    <StackPanel Spacing="8">
                        <TextBlock Text="Pitch" FontWeight="Bold"/>
                        <DockPanel>
                            <!-- Tone-wide pitch controls (apply to the whole tone, not just this partial) -->
                            <StackPanel DockPanel.Dock="Right" Spacing="2" Margin="16,0,0,0">
                                <TextBlock Text="Octave / Bend (whole tone)" Opacity="0.7"/>
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Octave" ToolTip.Tip="Octave Shift"/>
                                        <NumericUpDown Width="100" Minimum="-3" Maximum="3" Increment="1" FormatString="0"
                                                       Value="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).Header.OctaveShift.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Bend Up" ToolTip.Tip="Pitch Bend Range Up"/>
                                        <NumericUpDown Width="100" Minimum="0" Maximum="24" Increment="1" FormatString="0"
                                                       Value="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).Header.PitchBendUp.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Bend Down" ToolTip.Tip="Pitch Bend Range Down"/>
                                        <NumericUpDown Width="100" Minimum="0" Maximum="24" Increment="1" FormatString="0"
                                                       Value="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).Header.PitchBendDown.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </StackPanel>
                            </StackPanel>
                            <!-- Per-partial pitch envelope (bipolar: up = sharp, down = flat) -->
                            <StackPanel Spacing="4">
                                <TextBlock Text="Pitch envelope (up = sharp, down = flat)"/>
                                <controls:PitchEnvelopeControl Height="120"
                                    Attack="{Binding PitchEnvAttack.Value, Mode=TwoWay}"
                                    Decay="{Binding PitchEnvDecay.Value, Mode=TwoWay}"
                                    Depth="{Binding PitchEnvDepth.Value, Mode=TwoWay}"
                                    LineBrush="{StaticResource SnPitchEnvelopeBrush}"
                                    FillBrush="{StaticResource SnPitchEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                    HandleBrush="{StaticResource SnEnvelopeHandleBrush}"
                                    FocusBrush="{StaticResource SnEnvelopeFocusBrush}"/>
                                <StackPanel Orientation="Horizontal" Spacing="12">
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Attack" ToolTip.Tip="OSC Pitch Env Attack Time"/>
                                        <NumericUpDown Width="118" Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                       Value="{Binding PitchEnvAttack.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Decay" ToolTip.Tip="OSC Pitch Env Decay"/>
                                        <NumericUpDown Width="118" Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                       Value="{Binding PitchEnvDecay.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                    <StackPanel Spacing="2">
                                        <TextBlock Text="Depth" ToolTip.Tip="OSC Pitch Env Depth"/>
                                        <NumericUpDown Width="118" Minimum="-63" Maximum="63" Increment="1" FormatString="0"
                                                       Value="{Binding PitchEnvDepth.Value, Mode=TwoWay}"/>
                                    </StackPanel>
                                </StackPanel>
                            </StackPanel>
                        </DockPanel>
                    </StackPanel>
                </Border>
```

- [ ] **Step 4: Build the whole solution** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo` → `0 Error(s)`. Fix any real binding/namespace error minimally (the `controls:` and `vm:` xmlns and the `#Root` editor-VM cast pattern are already used in this file). If unresolved, report BLOCKED with the exact error.

- [ ] **Step 5: Commit**
```
git add Src/App.axaml Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): editor Pitch section (bipolar pitch envelope + tone-wide octave/bend)"
```

---

## Task 6: Full verification + smoke

**Files:** none.

- [ ] **Step 1: Run the full test suite** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --nologo` → all pass (incl. the new `PitchEnvelopeMappingTests`).
- [ ] **Step 2: Build the whole solution** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo` → `0 Error(s)`.
- [ ] **Step 3: Manual smoke (requires the connected Integra-7; pause for the user).** Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj`. Confirm:
  1. The new **Pitch** section shows a bipolar graph (center = no change); dragging the peak handle changes Attack (X) and Depth (Y, up = sharp / down = flat); the decay handle changes Decay; arrow keys + numeric boxes work; values send.
  2. **Octave** and **Bend Up/Down** edit and send (tone-wide).
  3. Hardware edits update the new controls.
  4. No layout regressions (scrolling still works; rows still align).
- [ ] **Step 4: Verify against the Phase-2 spec's pitch/octave/bend acceptance items; note any deviations.**

---

## Self-review notes (for the executor)
- `OSC Pitch` / `OSC Detune` already live in the Oscillator section (Phase 1) — the Pitch section adds only the pitch *envelope* + the tone-wide octave/bend, no duplication.
- Octave/Bend are tone-wide (header VM) but placed in the Pitch section to avoid header overflow; they bind via the `#Root` editor-VM path (same pattern the Advanced/utility buttons use).
- Pitch color is purple (`SnPitchEnvelopeBrush`) to distinguish from amp (blue) and filter (amber); all brushes come from `App.axaml` resources, none hardcoded in XAML.
- The control reuses the established `Preview`/brush-StyledProperty pattern; pitch is bipolar so it is its own control (not the shared-axis dual graph).
