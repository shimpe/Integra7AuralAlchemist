# Step LFO editor — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** a friendly editor for the PCM Synth partial's step LFO — `LFO Step Type` and `LFO Step 1..16` —
closing the last gap in friendly-editor coverage.

**Architecture:** a tested geometry class does the arithmetic, a Canvas-drawn control does the drawing and
the pointer, and a panel view model holds the seventeen `ParamInt`/`ParamString` wrappers. The panel sits
below LFO 1 and LFO 2 in the partial's Motion tab.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI, NUnit 3.

**Spec:** `docs/superpowers/specs/2026-07-28-step-lfo-editor-design.md`. Read it first.

---

## Conventions for every task

**Build and test with the user-local SDK** — the system `dotnet` is 8/9 and too old. `Src/bin` is
routinely locked by the user's own running application or Rider's Avalonia previewer; **never kill
either**, redirect instead. The four-deep path and the junction are both load-bearing, because several
tests find `Src\Assets\parameters.bin` by walking `..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **872 passed, 0 failed**.

**XAML rules that fail the build:** never hardcode a colour — use `{StaticResource ...}`; an em dash in
prose must be the character `—`, because a literal `--` in an XML comment is illegal; compiled bindings
are checked at build time and a wrong member name is `AVLN2000`.

**A control's C# defaults may name colours** — every existing control does, and the XAML then overrides
them with resources. The rule is about XAML, not about a fallback in code.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/step-lfo-editor`. Stage explicit paths only — never `git add -A`, and never stage
`Src/Assets/new-icon-orig.svg`, the user's own untracked file. Never `--no-verify`. Do not merge or push.

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `Src/Controls/StepLfoGeometry.cs` | Bar rectangles, step-from-x, value-from-y. Pure, tested. |
| Create `Src/Controls/StepLfoControl.cs` | Draws the bars, handles the drag, one undo step per stroke |
| Create `Src/ViewModels/StepLfoPanelViewModel.cs` | The seventeen parameter wrappers |
| Create `Src/Views/StepLfoPanelView.axaml` (+ `.axaml.cs`) | The panel |
| Modify `Tools/ParameterBlobGenerator/ParameterDefinitions.cs` | Names for `LFO Step Type` |
| Modify `Src/ViewModels/PCMPartialViewModel.cs` | Owns the panel; its parameters join the partial's editable set |
| Modify `Src/Views/PCMSynthToneEditorView.axaml:540` | Shows the panel under the two LFOs |
| Create `Tests/TestStepLfoGeometry.cs` | The geometry's tests |
| Create `Tests/TestStepLfoParameters.cs` | That the database really names the step type |

---

### Task 1: Name the step type

**Files:**
- Modify: `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`
- Test: `Tests/TestStepLfoParameters.cs`

The parameter database is generated at build time from `ParameterDefinitions.cs`. Editing that file and
building regenerates `Src/Assets/parameters.bin`, which is git-ignored — never commit it, and never
hand-edit `Src/Models/Data/Integra7Parameters.cs`.

- [ ] **Step 1: Write the failing test**

Create `Tests/TestStepLfoParameters.cs`:

```csharp
using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Tests;

/// <summary>The step LFO's parameters, as the database really carries them. The editor is built on these
/// exact ranges, and a silent change to one would move every bar it draws.</summary>
public class StepLfoParametersTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    /// <summary>It had no name table at all, so it displayed as 0 or 1 -- in the raw grid, in snapshot
    /// files and in comparisons. The names are the instrument's own two types, with what each does.</summary>
    [Test]
    public void The_step_type_is_named_rather_than_numbered()
    {
        var spec = _parameters.Lookup("PCM Synth Tone Partial/LFO Step Type");

        Assert.That(spec.Repr, Is.Not.Null);
        Assert.That(spec.Repr![0], Is.EqualTo("Type 1 (stepped)"));
        Assert.That(spec.Repr[1], Is.EqualTo("Type 2 (smoothed)"));
    }

    /// <summary>Sixteen steps, each raw 28..100 shown as -36..+36. The geometry is built from the
    /// displayed range, so this is what pins it.</summary>
    [Test]
    public void There_are_sixteen_steps_over_a_bipolar_range()
    {
        var steps = Enumerable.Range(1, 16)
            .Select(n => _parameters.Lookup($"PCM Synth Tone Partial/LFO Step {n}"))
            .ToList();

        Assert.That(steps.Select(s => s.OMin), Is.All.EqualTo(-36));
        Assert.That(steps.Select(s => s.OMax), Is.All.EqualTo(36));
        Assert.That(steps.Select(s => s.IMin), Is.All.EqualTo(28));
        Assert.That(steps.Select(s => s.IMax), Is.All.EqualTo(100));
    }
}
```

- [ ] **Step 2: Run it and watch the first test fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter StepLfoParametersTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: `The_step_type_is_named_rather_than_numbered` fails because `Repr` is null;
`There_are_sixteen_steps_over_a_bipolar_range` passes already.

- [ ] **Step 3: Add the name table**

In `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`, beside the other repr tables (search for
`LFO_FADEMODE`, which is next to `LFO_WAVEFORM` around line 1031):

```csharp
    // What the two step types do, not just what they are called. The instrument's own documentation names
    // them TYPE1 and TYPE2 and nothing else, which is why this parameter sat with no table at all and
    // displayed as 0 or 1 everywhere -- the raw grid, snapshot files, comparisons. The descriptions were
    // confirmed on the hardware rather than taken from this repository, which carries no Roland reference.
    public readonly IDictionary<int, string> LFO_STEP_TYPE = new Dictionary<int, string>
    {
        [0] = "Type 1 (stepped)",
        [1] = "Type 2 (smoothed)"
    };
```

Then point the parameter at it. The line currently ends `unit:"", repr:null`:

```csharp
            new(type:NUM, path:"PCM Synth Tone Partial/LFO Step Type", offs:[0x01, 0x09], imin:0, imax:1, omin:0, omax:1, bytes:1, res:USED, nib:false, unit:"", repr:LFO_STEP_TYPE),
```

- [ ] **Step 4: Run until green, then the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter StepLfoParametersTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

The build regenerates the blob, so the first run picks the new table up with no other step.

- [ ] **Step 5: Commit**

```bash
git add Tools/ParameterBlobGenerator/ParameterDefinitions.cs Tests/TestStepLfoParameters.cs
git commit -m "feat: name the two LFO step types"
```

---

### Task 2: The geometry

**Files:**
- Create: `Src/Controls/StepLfoGeometry.cs`
- Test: `Tests/TestStepLfoGeometry.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestStepLfoGeometry.cs`:

```csharp
using System.Linq;
using Integra7AuralAlchemist.Controls;

namespace Tests;

/// <summary>The step editor's arithmetic. It lives apart from the control for the reason every other
/// visual editor here does: a control cannot be unit-tested, and this is the part that can be wrong.
///
/// The fixture is 160 x 100 over sixteen steps of -36..+36, so a bar is 10 wide and the centre line is at
/// 50 -- numbers chosen so the expectations below are readable rather than arithmetic of their own.</summary>
public class StepLfoGeometryTests
{
    private static StepLfoGeometry Geometry() => new(width: 160, height: 100, steps: 16, minValue: -36, maxValue: 36);

    [Test]
    public void The_first_and_last_bars_answer_at_the_edges()
    {
        var g = Geometry();

        Assert.That(g.StepAt(0), Is.EqualTo(0));
        Assert.That(g.StepAt(159.9), Is.EqualTo(15));
    }

    [Test]
    public void A_pointer_outside_the_bars_is_over_no_step()
    {
        var g = Geometry();

        Assert.That(g.StepAt(-1), Is.Null);
        Assert.That(g.StepAt(160), Is.Null);
    }

    /// <summary>Every step is reachable and none is reachable twice: the failure this catches is an
    /// off-by-one in the division that makes one bar unclickable or two bars share an x.</summary>
    [Test]
    public void The_width_divides_evenly_over_the_steps()
    {
        var g = Geometry();

        var hit = Enumerable.Range(0, 160).Select(x => g.StepAt(x + 0.5)).ToList();

        Assert.That(hit, Has.None.Null);
        Assert.That(hit.Distinct().Count(), Is.EqualTo(16));
        Assert.That(hit.Select(h => h!.Value).Distinct().OrderBy(h => h), Is.EqualTo(Enumerable.Range(0, 16)));
    }

    [Test]
    public void The_top_is_the_maximum_the_bottom_the_minimum_and_the_middle_zero()
    {
        var g = Geometry();

        Assert.That(g.ValueAt(0), Is.EqualTo(36));
        Assert.That(g.ValueAt(100), Is.EqualTo(-36));
        Assert.That(g.ValueAt(50), Is.EqualTo(0));
    }

    /// <summary>A drag does not stop at the control's edge, so a pointer above or below it must clamp
    /// rather than ask for a value the parameter cannot hold.</summary>
    [Test]
    public void A_pointer_dragged_past_either_edge_clamps()
    {
        var g = Geometry();

        Assert.That(g.ValueAt(-500), Is.EqualTo(36));
        Assert.That(g.ValueAt(500), Is.EqualTo(-36));
    }

    [Test]
    public void A_positive_bar_stands_above_the_centre_and_a_negative_one_below()
    {
        var g = Geometry();

        var up = g.BarFor(0, 36);
        var down = g.BarFor(0, -36);

        Assert.That(up.Bottom, Is.EqualTo(g.CentreY).Within(0.001));
        Assert.That(up.Top, Is.LessThan(g.CentreY));
        Assert.That(down.Top, Is.EqualTo(g.CentreY).Within(0.001));
        Assert.That(down.Bottom, Is.GreaterThan(g.CentreY));
    }

    /// <summary>A step at rest is the state the editor opens in, sixteen times over. A zero-height bar
    /// would be an invisible control that cannot be aimed at.</summary>
    [Test]
    public void A_step_at_zero_still_draws_something_to_aim_at()
    {
        var g = Geometry();

        var bar = g.BarFor(0, 0);

        Assert.That(bar.Height, Is.GreaterThan(0));
        Assert.That(bar.Top, Is.LessThanOrEqualTo(g.CentreY));
        Assert.That(bar.Bottom, Is.GreaterThanOrEqualTo(g.CentreY));
    }

    /// <summary>The two halves have to agree, or a bar is drawn in one place and clicked in another.</summary>
    [Test]
    public void A_bar_contains_the_x_that_maps_back_to_it()
    {
        var g = Geometry();

        for (var step = 0; step < 16; step++)
        {
            var bar = g.BarFor(step, 20);
            Assert.That(g.StepAt(bar.Center.X), Is.EqualTo(step), $"step {step}");
        }
    }

    /// <summary>A control is measured before it is laid out, so the first call can arrive at zero size.
    /// Answering is better than dividing by zero.</summary>
    [Test]
    public void A_control_with_no_size_yet_answers_rather_than_throwing()
    {
        var g = new StepLfoGeometry(width: 0, height: 0, steps: 16, minValue: -36, maxValue: 36);

        Assert.That(g.StepAt(0), Is.Null);
        Assert.That(g.ValueAt(0), Is.InRange(-36, 36));
        Assert.That(g.BarFor(0, 0).Width, Is.GreaterThanOrEqualTo(0));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter StepLfoGeometryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error — `StepLfoGeometry` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Controls/StepLfoGeometry.cs`:

```csharp
using System;
using Avalonia;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Where the step LFO's bars go, which step a pointer is over, and what height means what value.
///
/// Apart from the control for the reason every other visual editor here keeps its arithmetic apart: a
/// control cannot be unit-tested, and this is the part that can be wrong. See <c>LayerMapGeometry</c> and
/// <c>KnobGeometry</c> for the same split.
///
/// Values are the <em>displayed</em> ones, -36..+36 rather than the raw 28..100 the device stores. The
/// wrappers the control writes through speak displayed values, so converting here would convert
/// twice.</summary>
public sealed class StepLfoGeometry(double width, double height, int steps, int minValue, int maxValue)
{
    /// <summary>The gap between two bars, so sixteen of them read as sixteen rather than as one block.
    /// Taken out of the bar's width, not added to it, so the bars still tile the full width and the x a
    /// user clicks in the gap still belongs to the bar beside it.</summary>
    private const double Gap = 2;

    /// <summary>How tall a bar at zero is drawn. The editor opens with all sixteen steps at rest, and a
    /// bar of no height is a control that cannot be seen or aimed at.</summary>
    private const double RestingHeight = 2;

    private double BarWidth => steps <= 0 ? 0 : width / steps;

    /// <summary>Where the value zero sits. Not simply the middle: the range is only symmetrical because
    /// this parameter happens to be, and a caller with an asymmetric one would get a centre line in the
    /// wrong place.</summary>
    public double CentreY => YFor(0);

    /// <summary>Which step the pointer is over, or null when it is outside the bars entirely -- a press
    /// there should do nothing rather than move the nearest step.</summary>
    public int? StepAt(double x)
    {
        if (width <= 0 || steps <= 0 || x < 0 || x >= width) return null;

        // Clamped as well as bounded: floating point can put x/BarWidth a hair past the last index for an
        // x a hair inside the right edge.
        return Math.Clamp((int)(x / BarWidth), 0, steps - 1);
    }

    /// <summary>The value a pointer at this height means, clamped to the parameter's own range. Clamping
    /// rather than refusing, because a drag does not stop at the control's edge and a user pulling well
    /// above it means "as high as it goes".</summary>
    public int ValueAt(double y)
    {
        if (height <= 0) return 0;

        var fraction = Math.Clamp(y / height, 0, 1);
        return (int)Math.Round(maxValue - fraction * (maxValue - minValue));
    }

    /// <summary>The bar for one step: from the centre line up for a positive value, down for a negative
    /// one, and a sliver on the centre for zero.</summary>
    public Rect BarFor(int step, int value)
    {
        var left = step * BarWidth + Gap / 2;
        var barWidth = Math.Max(0, BarWidth - Gap);

        if (value == 0)
            return new Rect(left, CentreY - RestingHeight / 2, barWidth, RestingHeight);

        var valueY = YFor(value);
        return value > 0
            ? new Rect(left, valueY, barWidth, CentreY - valueY)
            : new Rect(left, CentreY, barWidth, valueY - CentreY);
    }

    private double YFor(int value)
    {
        if (maxValue == minValue) return 0;

        return (maxValue - (double)value) / (maxValue - minValue) * height;
    }
}
```

- [ ] **Step 4: Run until green, then the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter StepLfoGeometryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

- [ ] **Step 5: Commit**

```bash
git add Src/Controls/StepLfoGeometry.cs Tests/TestStepLfoGeometry.cs
git commit -m "feat: work out where a step LFO's bars go"
```

---

### Task 3: The control

**Files:**
- Create: `Src/Controls/StepLfoControl.cs`

No unit tests: controls are not unit-tested in this repository, which is why Task 2 exists. Verification is
that the solution builds and the hand checks at the end.

- [ ] **Step 1: Write the control**

Create `Src/Controls/StepLfoControl.cs`:

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The PCM Synth partial's sixteen LFO steps, as bars either side of a centre line.
///
/// <b>One stroke draws a sequence.</b> Pressing sets the bar under the pointer and dragging sets every bar
/// it passes, including ones a fast movement skipped over -- which is how a step sequencer behaves
/// everywhere else, and the only reason to build a control rather than put sixteen knobs in a row.
///
/// Sixteen separate styled properties rather than a list: it is what every other editor here does (see
/// <c>PmtZoneEditorControl</c>, which has thirty-two), the bindings are compiled and therefore checked at
/// build time, and a list would need change notification per element to be two-way at all.
///
/// The arithmetic is in <see cref="StepLfoGeometry"/>.</summary>
public class StepLfoControl : Control
{
    private const int Steps = 16;
    private const int MinValue = -36;
    private const int MaxValue = 36;

    private static StyledProperty<int> S(string name) =>
        AvaloniaProperty.Register<StepLfoControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Step1Property = S(nameof(Step1));
    public static readonly StyledProperty<int> Step2Property = S(nameof(Step2));
    public static readonly StyledProperty<int> Step3Property = S(nameof(Step3));
    public static readonly StyledProperty<int> Step4Property = S(nameof(Step4));
    public static readonly StyledProperty<int> Step5Property = S(nameof(Step5));
    public static readonly StyledProperty<int> Step6Property = S(nameof(Step6));
    public static readonly StyledProperty<int> Step7Property = S(nameof(Step7));
    public static readonly StyledProperty<int> Step8Property = S(nameof(Step8));
    public static readonly StyledProperty<int> Step9Property = S(nameof(Step9));
    public static readonly StyledProperty<int> Step10Property = S(nameof(Step10));
    public static readonly StyledProperty<int> Step11Property = S(nameof(Step11));
    public static readonly StyledProperty<int> Step12Property = S(nameof(Step12));
    public static readonly StyledProperty<int> Step13Property = S(nameof(Step13));
    public static readonly StyledProperty<int> Step14Property = S(nameof(Step14));
    public static readonly StyledProperty<int> Step15Property = S(nameof(Step15));
    public static readonly StyledProperty<int> Step16Property = S(nameof(Step16));

    /// <summary>The sixteen in order, so the pointer handlers can address a step by index instead of a
    /// sixteen-armed switch.</summary>
    private static readonly StyledProperty<int>[] StepProperties =
    [
        Step1Property, Step2Property, Step3Property, Step4Property,
        Step5Property, Step6Property, Step7Property, Step8Property,
        Step9Property, Step10Property, Step11Property, Step12Property,
        Step13Property, Step14Property, Step15Property, Step16Property,
    ];

    public int Step1 { get => GetValue(Step1Property); set => SetValue(Step1Property, value); }
    public int Step2 { get => GetValue(Step2Property); set => SetValue(Step2Property, value); }
    public int Step3 { get => GetValue(Step3Property); set => SetValue(Step3Property, value); }
    public int Step4 { get => GetValue(Step4Property); set => SetValue(Step4Property, value); }
    public int Step5 { get => GetValue(Step5Property); set => SetValue(Step5Property, value); }
    public int Step6 { get => GetValue(Step6Property); set => SetValue(Step6Property, value); }
    public int Step7 { get => GetValue(Step7Property); set => SetValue(Step7Property, value); }
    public int Step8 { get => GetValue(Step8Property); set => SetValue(Step8Property, value); }
    public int Step9 { get => GetValue(Step9Property); set => SetValue(Step9Property, value); }
    public int Step10 { get => GetValue(Step10Property); set => SetValue(Step10Property, value); }
    public int Step11 { get => GetValue(Step11Property); set => SetValue(Step11Property, value); }
    public int Step12 { get => GetValue(Step12Property); set => SetValue(Step12Property, value); }
    public int Step13 { get => GetValue(Step13Property); set => SetValue(Step13Property, value); }
    public int Step14 { get => GetValue(Step14Property); set => SetValue(Step14Property, value); }
    public int Step15 { get => GetValue(Step15Property); set => SetValue(Step15Property, value); }
    public int Step16 { get => GetValue(Step16Property); set => SetValue(Step16Property, value); }

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<StepLfoControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> BarBrushProperty =
        B(nameof(BarBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> AxisBrushProperty =
        B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    /// <summary>The step the pointer last set, so a fast drag can fill in the ones between rather than
    /// leaving gaps. -1 when no drag is in progress.</summary>
    private int _lastStep = -1;

    // The whole stroke is one undo step, however slowly it is drawn.
    private readonly EditGesture _gesture = new();
    private readonly PointerGesture _pointer = new();

    static StepLfoControl()
    {
        AffectsRender<StepLfoControl>(
            Step1Property, Step2Property, Step3Property, Step4Property,
            Step5Property, Step6Property, Step7Property, Step8Property,
            Step9Property, Step10Property, Step11Property, Step12Property,
            Step13Property, Step14Property, Step15Property, Step16Property,
            BarBrushProperty, BackgroundBrushProperty, AxisBrushProperty);
    }

    private StepLfoGeometry Geometry() =>
        new(Bounds.Width, Bounds.Height, Steps, MinValue, MaxValue);

    public override void Render(DrawingContext context)
    {
        var g = Geometry();
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // The centre line first, so a resting bar sits on top of it rather than being lost in it.
        context.DrawLine(new Pen(AxisBrush), new Point(0, g.CentreY), new Point(Bounds.Width, g.CentreY));

        for (var step = 0; step < Steps; step++)
            context.FillRectangle(BarBrush, g.BarFor(step, GetValue(StepProperties[step])));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var position = e.GetPosition(this);
        if (Geometry().StepAt(position.X) is not { } step) return;

        _gesture.Begin();
        // Ends the gesture on capture lost as well as on release: PointerCaptureLost is a direct event
        // that reaches only the element holding capture, which is what PointerGesture exists to get right.
        _pointer.Begin(this, EndStroke);
        e.Pointer.Capture(this);

        _lastStep = step;
        SetStep(step, Geometry().ValueAt(position.Y));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastStep < 0) return;

        var position = e.GetPosition(this);
        var g = Geometry();
        var value = g.ValueAt(position.Y);

        // A pointer that has left the control sideways keeps drawing at the end it left by, which is what
        // a user dragging past the edge means; one that is still inside sets the bar it is over.
        var step = g.StepAt(position.X) ?? (position.X < 0 ? 0 : Steps - 1);

        // Every bar between the last one and this one, so a fast drag leaves no gaps behind it.
        var direction = step >= _lastStep ? 1 : -1;
        for (var s = _lastStep; s != step + direction; s += direction) SetStep(s, value);

        _lastStep = step;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pointer.End();
    }

    private void EndStroke()
    {
        _lastStep = -1;
        _gesture.End();
    }

    private void SetStep(int step, int value)
    {
        if (step < 0 || step >= Steps) return;

        SetValue(StepProperties[step], value);
    }
}
```

- [ ] **Step 2: Check `PointerGesture`'s real shape before relying on it**

```powershell
Get-Content Src/Controls/PointerGesture.cs | Select-String -Pattern "public" -Context 0,3
```

`Begin(Interactive captureTarget, Action? onEnd = null)` and `End()` are what this plan assumes. If the
signature differs, follow the file rather than this plan, and say so in your report.

- [ ] **Step 3: Build**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: build succeeds. The control is not reachable from any view yet — Task 4 adds the panel.

- [ ] **Step 4: Commit**

```bash
git add Src/Controls/StepLfoControl.cs
git commit -m "feat: draw and drag the sixteen LFO steps"
```

---

### Task 4: The panel, and its place in the editor

**Files:**
- Create: `Src/ViewModels/StepLfoPanelViewModel.cs`
- Create: `Src/Views/StepLfoPanelView.axaml`, `Src/Views/StepLfoPanelView.axaml.cs`
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`
- Modify: `Src/Views/PCMSynthToneEditorView.axaml:540`

- [ ] **Step 1: Write the view model**

Create `Src/ViewModels/StepLfoPanelViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The PCM Synth partial's step LFO: a type, and sixteen steps of -36..+36.
///
/// <b>Shared by both LFOs.</b> These parameter paths carry no LFO number, unlike "LFO1 Rate" and
/// "LFO2 Rate", so a partial has one step sequence and whichever of its two LFOs is set to the Step
/// waveform plays it. That is the instrument's design; the panel says so on screen, because a tone using
/// Step on both LFOs otherwise looks like a bug.
///
/// Sixteen named properties as well as the list: the list is what the partial tracks and disposes, and
/// the named ones are what the view binds, because a control's styled properties are bound one at a time
/// (see StepLfoControl for why it is built that way).</summary>
public sealed class StepLfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public ParamString StepType { get; }

    public IReadOnlyList<ParamInt> Steps { get; }

    public ParamInt Step1 => Steps[0];
    public ParamInt Step2 => Steps[1];
    public ParamInt Step3 => Steps[2];
    public ParamInt Step4 => Steps[3];
    public ParamInt Step5 => Steps[4];
    public ParamInt Step6 => Steps[5];
    public ParamInt Step7 => Steps[6];
    public ParamInt Step8 => Steps[7];
    public ParamInt Step9 => Steps[8];
    public ParamInt Step10 => Steps[9];
    public ParamInt Step11 => Steps[10];
    public ParamInt Step12 => Steps[11];
    public ParamInt Step13 => Steps[12];
    public ParamInt Step14 => Steps[13];
    public ParamInt Step15 => Steps[14];
    public ParamInt Step16 => Steps[15];

    public IReadOnlyList<IParam> Params { get; }

    public StepLfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        StepType = Track(new ParamString(partialDomain, byPath[PP + "LFO Step Type"], writer));

        // -36..+36 is the displayed range; the device stores 28..100 and the wrapper converts.
        Steps =
        [
            .. Enumerable.Range(1, 16)
                .Select(n => Track(new ParamInt(partialDomain, byPath[PP + $"LFO Step {n}"], writer, -36, 36))),
        ];

        Params = [StepType, .. Steps];
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Write the view**

Create `Src/Views/StepLfoPanelView.axaml`. Open `Src/Views/PcmLfoPanelView.axaml` first and copy its outer
chrome — the `Border`, its brushes and its header layout — so this panel sits beside the other two rather
than beneath its own invented styling. The content is:

```xml
        <StackPanel Orientation="Vertical" Spacing="6">
            <TextBlock Text="Step LFO" FontWeight="Bold" />
            <!-- Said on screen because it surprises people: the sixteen steps belong to the partial, not to
                 an LFO, so a tone with both LFOs set to Step plays the same sequence twice. -->
            <TextBlock Text="Shared by LFO 1 and LFO 2 — plays on whichever has its waveform set to Step."
                       Foreground="{StaticResource SnMutedTextBrush}" TextWrapping="Wrap" />

            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="Type" VerticalAlignment="Center" />
                <ComboBox ItemsSource="{Binding StepType.Options}"
                          SelectedItem="{Binding StepType.Value, Mode=TwoWay}"
                          MinWidth="180" />
            </StackPanel>

            <controls:StepLfoControl Height="140" MinWidth="520"
                                     BarBrush="{StaticResource KnobPitchBrush}"
                                     BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                     AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                     Step1="{Binding Step1.Value, Mode=TwoWay}"
                                     Step2="{Binding Step2.Value, Mode=TwoWay}"
                                     Step3="{Binding Step3.Value, Mode=TwoWay}"
                                     Step4="{Binding Step4.Value, Mode=TwoWay}"
                                     Step5="{Binding Step5.Value, Mode=TwoWay}"
                                     Step6="{Binding Step6.Value, Mode=TwoWay}"
                                     Step7="{Binding Step7.Value, Mode=TwoWay}"
                                     Step8="{Binding Step8.Value, Mode=TwoWay}"
                                     Step9="{Binding Step9.Value, Mode=TwoWay}"
                                     Step10="{Binding Step10.Value, Mode=TwoWay}"
                                     Step11="{Binding Step11.Value, Mode=TwoWay}"
                                     Step12="{Binding Step12.Value, Mode=TwoWay}"
                                     Step13="{Binding Step13.Value, Mode=TwoWay}"
                                     Step14="{Binding Step14.Value, Mode=TwoWay}"
                                     Step15="{Binding Step15.Value, Mode=TwoWay}"
                                     Step16="{Binding Step16.Value, Mode=TwoWay}" />
        </StackPanel>
```

The root element needs `x:DataType="vm:StepLfoPanelViewModel"` and the `controls:` namespace — copy both
declarations from `PcmLfoPanelView.axaml`, which already has the first and may not have the second
(`xmlns:controls="using:Integra7AuralAlchemist.Controls"`).

Create `Src/Views/StepLfoPanelView.axaml.cs` matching `PcmLfoPanelView.axaml.cs` exactly, with the class
renamed.

- [ ] **Step 3: Own the panel from the partial**

In `Src/ViewModels/PCMPartialViewModel.cs`, beside `Lfo1`/`Lfo2` (around line 110):

```csharp
    /// <summary>The sixteen shared steps. One per partial, not one per LFO — see StepLfoPanelViewModel.</summary>
    public StepLfoPanelViewModel StepLfo { get; }
```

and where they are constructed (around line 191):

```csharp
        StepLfo = Track(new StepLfoPanelViewModel(partialDomain, byPath, writer));
```

Add its parameters to the partial's editable set, so partial copy/paste and init carry the step sequence
like every other partial parameter. Find the `_editable = new IParam[] { ... }` initialiser and append:

```csharp
            StepLfo.StepType, .. StepLfo.Steps,
```

If that initialiser is a plain array initialiser that will not take a spread, convert it to a collection
expression (`_editable = [ ... ]`) — the surrounding code already uses them.

- [ ] **Step 4: Show it**

In `Src/Views/PCMSynthToneEditorView.axaml`, the Motion tab currently reads:

```xml
                                <ContentControl Content="{Binding Lfo1}"/>
                                <ContentControl Content="{Binding Lfo2}"/>
```

Add below them:

```xml
                                <ContentControl Content="{Binding StepLfo}"/>
```

- [ ] **Step 5: Build and run the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: build succeeds — this is where a missing `DataTemplate` for the new view model would show up as
the panel not rendering, so also check that `Src/DataTemplates/DataTemplateProvider.cs` resolves views by
convention; if it maps view models to views explicitly, add the new pair there and say so in your report.

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/StepLfoPanelViewModel.cs Src/Views/StepLfoPanelView.axaml Src/Views/StepLfoPanelView.axaml.cs Src/ViewModels/PCMPartialViewModel.cs Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: add the step LFO panel to the partial's Motion tab"
```

---

## Verification by hand (user)

- [ ] Drag across the bars in one stroke: every bar follows the pointer and none is skipped, including
  during a fast drag.
- [ ] One press of Undo takes the whole stroke back.
- [ ] Drag past the top and bottom edges: values stop at +36 and −36 rather than wrapping.
- [ ] Set an LFO's waveform to Step: the sequence is audible, and the values match the instrument's own
  display.
- [ ] Confirm the type labels are the right way round — "Type 1 (stepped)" should hold each value until
  the next step, "Type 2 (smoothed)" should glide. If reversed, it is one line in
  `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`.
- [ ] Edit a step, then compare the tone against a snapshot taken before: the step appears as a difference.
