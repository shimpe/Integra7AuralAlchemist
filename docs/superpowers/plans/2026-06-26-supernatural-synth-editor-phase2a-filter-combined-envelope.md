# SN-S Editor Phase 2a — Filter + Combined Amp/Filter Envelope — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Filter section and a single graph that overlays the Amp and Filter envelopes on one shared X (time) / Y (0–127) axis, color-coded and both editable, so their sonic interaction is visible.

**Architecture:** Reuse Phase-1 infrastructure (param wrappers `ParamInt/ParamString/ParamBool`, per-(domain,path) throttled writer, live FQP binding, `SnsEnvelopeMapping` math). Add a pure nearest-handle helper, app-level envelope color resources, a brush retrofit of `AdsrEnvelopeControl`, a new `DualAdsrEnvelopeControl`, the VM filter params, and the view reorg.

**Tech Stack:** Avalonia 12, ReactiveUI (+ SourceGenerators), NUnit 3. Build/test from PowerShell at repo root with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sns-editor`.

**Spec:** `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-phase2-design.md`.

**Key facts (verified, from memory `sn-s-tone-parameters` / Phase-1 code):**
- Lookup path = `ParSpec.Path` = `<prefix>/<name>`; part/partial from the domain instance. Wrappers: `ParamInt(domain, fqp, writer, min, max)`, `ParamString(domain, fqp, writer, options?)` (`.Value`, `.Options`), `ParamBool(domain, fqp, writer, on="ON", off="OFF")`. All in namespace `Integra7AuralAlchemist.ViewModels`, implement `IParam`/`IDisposable`.
- Filter partial params (`SuperNATURAL Synth Tone Partial/…`): `Filter Mode` (enum FILTER_MODE), `Filter Slope` (display `-12`/`-24`), `Filter Cutoff` (0–127), `Filter Resonance` (0–127), `Filter Cutoff Keyfollow` (−100..100), `HPF Cutoff` (0–127), `Filter Env Velocity Sens` (−63..63), `Filter Env Attack Time`/`Filter Env Decay Time`/`Filter Env Sustain Level`/`Filter Env Release Time` (0–127), `Filter Env Depth` (−63..63).
- `SnsEnvelopeMapping` (pure, `Src/Models/Services/`) already has: `Min=0,Max=127`, `record struct Point(double X,double Y)`, `record struct EnvPoints(Point Start,Peak,SustainStart,SustainEnd,End)`, `Clamp`, `SegmentMax(w,sustainW)`, `TimeToWidth`, `TimeFromWidth`, `LevelToY`, `LevelFromY`, `ComputePoints(a,d,s,r,w,h,sustainW)`, `AttackFromX`, `DecayFromX`, `ReleaseFromX`.
- `AdsrEnvelopeControl` (`Src/Controls/`) currently uses private static brushes/pens and a private `DrawAxes`. Card previews use `Preview=true`.

---

## Task 1: Pure helpers — `NearestHandle` + `SnsFilterRules.Abbrev`

**Files:**
- Modify: `Src/Models/Services/SnsEnvelopeMapping.cs`
- Create: `Src/Models/Services/SnsFilterRules.cs`
- Test: `Tests/TestDualEnvelopeHitTest.cs`, `Tests/TestSnsFilterRules.cs`

- [ ] **Step 1: Write the failing tests**

`Tests/TestDualEnvelopeHitTest.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class DualEnvelopeHitTestTests
{
    // Amp peak at (10,0); filter peak at (200,0). Pointer near amp peak -> amp attack handle.
    private static SnsEnvelopeMapping.EnvPoints Env(double peakX) => new(
        new SnsEnvelopeMapping.Point(0, 100),
        new SnsEnvelopeMapping.Point(peakX, 0),
        new SnsEnvelopeMapping.Point(peakX + 10, 50),
        new SnsEnvelopeMapping.Point(peakX + 50, 50),
        new SnsEnvelopeMapping.Point(peakX + 90, 100));

    [Test]
    public void Picks_nearest_handle_of_either_envelope()
    {
        var hit = SnsEnvelopeMapping.NearestHandle(12, 2, Env(10), Env(200), activeEnv: 0, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(0));
        Assert.That(hit.Handle, Is.EqualTo(0)); // Attack
    }

    [Test]
    public void Returns_none_when_outside_radius()
    {
        var hit = SnsEnvelopeMapping.NearestHandle(500, 500, Env(10), Env(200), activeEnv: 0, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(-1));
        Assert.That(hit.Handle, Is.EqualTo(-1));
    }

    [Test]
    public void Active_envelope_wins_ties()
    {
        // Both envelopes have a handle at exactly (100,0); pointer exactly there. Active=1 -> filter.
        var hit = SnsEnvelopeMapping.NearestHandle(100, 0, Env(100), Env(100), activeEnv: 1, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(1));
    }
}
```

`Tests/TestSnsFilterRules.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsFilterRulesTests
{
    [TestCase("Bypass", "BYP")]
    [TestCase("Low pass", "LPF")]
    [TestCase("High pass", "HPF")]
    [TestCase("Band pass", "BPF")]
    [TestCase("Peaking", "PEAK")]
    [TestCase("Low pass 2", "LPF2")]
    [TestCase("Low pass 3", "LPF3")]
    [TestCase("Low pass 4", "LPF4")]
    [TestCase("something else", "something else")]
    public void Abbreviates_filter_mode(string mode, string expected)
        => Assert.That(SnsFilterRules.Abbrev(mode), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Run to confirm FAIL**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter "DualEnvelopeHitTestTests|SnsFilterRulesTests"`
Expected: FAIL — members don't exist.

- [ ] **Step 3: Add `NearestHandle` to `SnsEnvelopeMapping`**

Append these members inside the `SnsEnvelopeMapping` class (before the closing brace) in `Src/Models/Services/SnsEnvelopeMapping.cs`:
```csharp
    /// <summary>Result of a dual-envelope hit test. Env 0 = amp, 1 = filter; Handle 0/1/2 =
    /// Attack/Decay/Release; (-1,-1) = nothing within radius.</summary>
    public readonly record struct HandleHit(int Env, int Handle);

    /// <summary>Nearest editable handle across two envelopes. The active envelope wins ties
    /// (it is tested first, and later candidates must be strictly closer to replace it).</summary>
    public static HandleHit NearestHandle(double px, double py, EnvPoints amp, EnvPoints filter,
        int activeEnv, double radius)
    {
        var best = new HandleHit(-1, -1);
        var bestD = radius * radius;
        var order = activeEnv == 1 ? new[] { 1, 0 } : new[] { 0, 1 };
        foreach (var e in order)
        {
            var pts = e == 0 ? amp : filter;
            Consider(e, 0, pts.Peak);
            Consider(e, 1, pts.SustainStart);
            Consider(e, 2, pts.End);
        }
        return best;

        void Consider(int e, int handle, Point pt)
        {
            var dx = px - pt.X; var dy = py - pt.Y; var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = new HandleHit(e, handle); }
        }
    }
```

- [ ] **Step 4: Create `Src/Models/Services/SnsFilterRules.cs`**
```csharp
namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Small pure helpers for the friendly filter UI.</summary>
public static class SnsFilterRules
{
    /// <summary>Short label for a filter mode (for the partial card summary).</summary>
    public static string Abbrev(string mode) => mode switch
    {
        "Bypass" => "BYP",
        "Low pass" => "LPF",
        "High pass" => "HPF",
        "Band pass" => "BPF",
        "Peaking" => "PEAK",
        "Low pass 2" => "LPF2",
        "Low pass 3" => "LPF3",
        "Low pass 4" => "LPF4",
        _ => mode
    };
}
```

- [ ] **Step 5: Run to confirm PASS**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter "DualEnvelopeHitTestTests|SnsFilterRulesTests"`
Expected: all pass.

- [ ] **Step 6: Commit**
```
git add Src/Models/Services/SnsEnvelopeMapping.cs Src/Models/Services/SnsFilterRules.cs Tests/TestDualEnvelopeHitTest.cs Tests/TestSnsFilterRules.cs
git commit -m "feat(sns): dual-envelope nearest-handle + filter-mode abbrev helpers with tests"
```

---

## Task 2: Envelope color resources (`App.axaml`)

**Files:**
- Modify: `Src/App.axaml`

- [ ] **Step 1: Add envelope brushes**

In `Src/App.axaml`, inside the existing `<ResourceDictionary>` in `Application.Resources` (after the `SnCardSelectedBorderBrush` line), add:
```xml
            <!-- Envelope graph palette (Amp = blue, Filter = amber). -->
            <SolidColorBrush x:Key="SnEnvelopeBackgroundBrush" Color="#1B1F22" />
            <SolidColorBrush x:Key="SnEnvelopeGridBrush" Color="#22FFFFFF" />
            <SolidColorBrush x:Key="SnEnvelopeAxisBrush" Color="#55FFFFFF" />
            <SolidColorBrush x:Key="SnEnvelopeHandleBrush" Color="White" />
            <SolidColorBrush x:Key="SnEnvelopeFocusBrush" Color="Orange" />
            <SolidColorBrush x:Key="SnAmpEnvelopeBrush" Color="#7FB6E0" />
            <SolidColorBrush x:Key="SnAmpEnvelopeFillBrush" Color="#553D7EAA" />
            <SolidColorBrush x:Key="SnFilterEnvelopeBrush" Color="#E0A23D" />
            <SolidColorBrush x:Key="SnFilterEnvelopeFillBrush" Color="#55B07A1E" />
```

- [ ] **Step 2: Build to confirm the XAML parses**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)` (ignore exe copy-lock if the app is running; only `error CS/AVLN/XAMLIL` matter).

- [ ] **Step 3: Commit**
```
git add Src/App.axaml
git commit -m "feat(sns): add envelope graph color resources (amp blue, filter amber)"
```

---

## Task 3: Shared `EnvelopeAxes` + retrofit `AdsrEnvelopeControl` with brush properties

**Files:**
- Create: `Src/Controls/EnvelopeAxes.cs`
- Modify: `Src/Controls/AdsrEnvelopeControl.cs`

This factors the axis drawing into a reusable helper (the dual control will reuse it) and makes the line/fill colors bindable so the card filter preview can be amber.

- [ ] **Step 1: Create `Src/Controls/EnvelopeAxes.cs`**
```csharp
using Avalonia;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Shared X (time) / Y (level) axis + tick drawing for envelope graphs (ticks every 16
/// of the 0..127 range). Used by both the single and dual ADSR controls so they look identical.</summary>
public static class EnvelopeAxes
{
    public const int TickStep = 16;

    public static void Draw(DrawingContext ctx, double w, double h, double sustainWidth, IPen gridPen, IPen axisPen)
    {
        for (var level = 0; level <= SnsEnvelopeMapping.Max; level += TickStep)
        {
            var y = SnsEnvelopeMapping.LevelToY(level, h);
            ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            ctx.DrawLine(axisPen, new Point(0, y), new Point(6, y));
        }

        var step = SnsEnvelopeMapping.TimeToWidth(TickStep, SnsEnvelopeMapping.SegmentMax(w, sustainWidth));
        if (step > 2)
            for (var x = step; x < w - 1; x += step)
                ctx.DrawLine(axisPen, new Point(x, h - 6), new Point(x, h));

        ctx.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        ctx.DrawLine(axisPen, new Point(0, h), new Point(w, h));
    }
}
```

- [ ] **Step 2: Retrofit `AdsrEnvelopeControl`**

In `Src/Controls/AdsrEnvelopeControl.cs`:

(a) Add two bindable brush properties. After the `PreviewProperty` registration block (the lines registering `PreviewProperty`), add:
```csharp
    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, IBrush>(nameof(FillBrush),
            new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
```
And after the `public bool Preview { … }` accessor add:
```csharp
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
```

(b) Replace the static `FillBrush` / `LinePen` / `PreviewFillBrush` / `PreviewLinePen` fields. Delete these four lines:
```csharp
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa));
```
```csharp
    private static readonly IPen LinePen = new Pen(new SolidColorBrush(Color.Parse("#7fb6e0")), 2);
```
```csharp
    private static readonly IBrush PreviewFillBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x3d, 0x7e, 0xaa));
    private static readonly IPen PreviewLinePen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x7f, 0xb6, 0xe0)), 1);
```
(Keep `Bg`, `HandleBrush`, `GridPen`, `AxisPen`, `FocusPen`, `PreviewBg`, `TickStep`.)

(c) Add `LineBrushProperty, FillBrushProperty` to `AffectsRender`. Change:
```csharp
        AffectsRender<AdsrEnvelopeControl>(AttackProperty, DecayProperty, SustainProperty, ReleaseProperty,
            PreviewProperty);
```
to:
```csharp
        AffectsRender<AdsrEnvelopeControl>(AttackProperty, DecayProperty, SustainProperty, ReleaseProperty,
            PreviewProperty, LineBrushProperty, FillBrushProperty);
```

(d) In `Render`, use the brush properties + the shared axes. Replace the axis block:
```csharp
        if (preview)
            context.DrawLine(GridPen, new Point(0, h - 1), new Point(w, h - 1));
        else
            DrawAxes(context, w, h);
```
with:
```csharp
        if (preview)
            context.DrawLine(GridPen, new Point(0, h - 1), new Point(w, h - 1));
        else
            EnvelopeAxes.Draw(context, w, h, SustainWidth, GridPen, AxisPen);
```
Replace the fill draw:
```csharp
        context.DrawGeometry(preview ? PreviewFillBrush : FillBrush, null, fill);
```
with:
```csharp
        context.DrawGeometry(FillBrush, null, fill);
```
Replace the line draw:
```csharp
        context.DrawGeometry(null, preview ? PreviewLinePen : LinePen, line);
```
with:
```csharp
        context.DrawGeometry(null, new Pen(LineBrush, preview ? 1 : 2), line);
```

(e) Delete the now-unused private `DrawAxes` method (the whole `private static void DrawAxes(DrawingContext ctx, double w, double h) { … }` block) since `EnvelopeAxes.Draw` replaces it.

- [ ] **Step 3: Build to confirm it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)` (only `error CS/AVLN/XAMLIL` matter; exe copy-lock is fine).

- [ ] **Step 4: Commit**
```
git add Src/Controls/EnvelopeAxes.cs Src/Controls/AdsrEnvelopeControl.cs
git commit -m "refactor(sns): shared EnvelopeAxes + bindable Line/Fill brushes on AdsrEnvelopeControl"
```

---

## Task 4: `DualAdsrEnvelopeControl` (shared-axis Amp+Filter overlay)

**Files:**
- Create: `Src/Controls/DualAdsrEnvelopeControl.cs`

- [ ] **Step 1: Create the control**

`Src/Controls/DualAdsrEnvelopeControl.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Two ADSR envelopes (Amp + Filter) overlaid on ONE shared axis (same time/level mapping), so
/// their sonic interaction is visible. Both are draggable; the active envelope is drawn on top
/// and edited by the keyboard. A drag grabs the nearest handle of either envelope.
/// </summary>
public class DualAdsrEnvelopeControl : Control
{
    private const double HandleRadius = 7;
    private const double SustainWidth = 40;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> AmpAttackProperty = I(nameof(AmpAttack));
    public static readonly StyledProperty<int> AmpDecayProperty = I(nameof(AmpDecay));
    public static readonly StyledProperty<int> AmpSustainProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(AmpSustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> AmpReleaseProperty = I(nameof(AmpRelease));
    public static readonly StyledProperty<int> FilterAttackProperty = I(nameof(FilterAttack));
    public static readonly StyledProperty<int> FilterDecayProperty = I(nameof(FilterDecay));
    public static readonly StyledProperty<int> FilterSustainProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(FilterSustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> FilterReleaseProperty = I(nameof(FilterRelease));

    /// <summary>0 = Amp (front/keyboard), 1 = Filter.</summary>
    public static readonly StyledProperty<int> ActiveEnvelopeProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(ActiveEnvelope), 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> AmpLineBrushProperty = B(nameof(AmpLineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> AmpFillBrushProperty = B(nameof(AmpFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
    public static readonly StyledProperty<IBrush> FilterLineBrushProperty = B(nameof(FilterLineBrush), new SolidColorBrush(Color.Parse("#E0A23D")));
    public static readonly StyledProperty<IBrush> FilterFillBrushProperty = B(nameof(FilterFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xb0, 0x7a, 0x1e)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> FocusBrushProperty = B(nameof(FocusBrush), Brushes.Orange);

    public int AmpAttack { get => GetValue(AmpAttackProperty); set => SetValue(AmpAttackProperty, value); }
    public int AmpDecay { get => GetValue(AmpDecayProperty); set => SetValue(AmpDecayProperty, value); }
    public int AmpSustain { get => GetValue(AmpSustainProperty); set => SetValue(AmpSustainProperty, value); }
    public int AmpRelease { get => GetValue(AmpReleaseProperty); set => SetValue(AmpReleaseProperty, value); }
    public int FilterAttack { get => GetValue(FilterAttackProperty); set => SetValue(FilterAttackProperty, value); }
    public int FilterDecay { get => GetValue(FilterDecayProperty); set => SetValue(FilterDecayProperty, value); }
    public int FilterSustain { get => GetValue(FilterSustainProperty); set => SetValue(FilterSustainProperty, value); }
    public int FilterRelease { get => GetValue(FilterReleaseProperty); set => SetValue(FilterReleaseProperty, value); }
    public int ActiveEnvelope { get => GetValue(ActiveEnvelopeProperty); set => SetValue(ActiveEnvelopeProperty, value); }
    public IBrush AmpLineBrush { get => GetValue(AmpLineBrushProperty); set => SetValue(AmpLineBrushProperty, value); }
    public IBrush AmpFillBrush { get => GetValue(AmpFillBrushProperty); set => SetValue(AmpFillBrushProperty, value); }
    public IBrush FilterLineBrush { get => GetValue(FilterLineBrushProperty); set => SetValue(FilterLineBrushProperty, value); }
    public IBrush FilterFillBrush { get => GetValue(FilterFillBrushProperty); set => SetValue(FilterFillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public IBrush FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    // 0=Attack,1=Decay,2=Release. _dragEnv/_dragHandle while dragging; _focusedHandle for keyboard.
    private int _dragEnv = -1, _dragHandle = -1, _focusedHandle = 0;

    static DualAdsrEnvelopeControl()
    {
        AffectsRender<DualAdsrEnvelopeControl>(
            AmpAttackProperty, AmpDecayProperty, AmpSustainProperty, AmpReleaseProperty,
            FilterAttackProperty, FilterDecayProperty, FilterSustainProperty, FilterReleaseProperty,
            ActiveEnvelopeProperty, AmpLineBrushProperty, FilterLineBrushProperty);
        FocusableProperty.OverrideDefaultValue<DualAdsrEnvelopeControl>(true);
    }

    private SnsEnvelopeMapping.EnvPoints AmpPoints(double w, double h) =>
        SnsEnvelopeMapping.ComputePoints(AmpAttack, AmpDecay, AmpSustain, AmpRelease, w, h, SustainWidth);
    private SnsEnvelopeMapping.EnvPoints FilterPoints(double w, double h) =>
        SnsEnvelopeMapping.ComputePoints(FilterAttack, FilterDecay, FilterSustain, FilterRelease, w, h, SustainWidth);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        EnvelopeAxes.Draw(context, w, h, SustainWidth, new Pen(GridBrush), new Pen(AxisBrush));

        // Inactive first (dim), then active on top.
        if (ActiveEnvelope == 0)
        {
            DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h, active: false);
            DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h, active: true);
        }
        else
        {
            DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h, active: false);
            DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h, active: true);
        }
    }

    private void DrawEnvelope(DrawingContext context, SnsEnvelopeMapping.EnvPoints p, IBrush line, IBrush fill,
        double h, bool active)
    {
        using (context.PushOpacity(active ? 1.0 : 0.4))
        {
            var fillGeo = new StreamGeometry();
            using (var c = fillGeo.Open())
            {
                c.BeginFigure(new Point(p.Start.X, p.Start.Y), true);
                c.LineTo(new Point(p.Peak.X, p.Peak.Y));
                c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
                c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
                c.LineTo(new Point(p.End.X, p.End.Y));
                c.LineTo(new Point(p.End.X, h));
                c.LineTo(new Point(p.Start.X, h));
                c.EndFigure(true);
            }
            context.DrawGeometry(fill, null, fillGeo);

            var lineGeo = new StreamGeometry();
            using (var c = lineGeo.Open())
            {
                c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
                c.LineTo(new Point(p.Peak.X, p.Peak.Y));
                c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
                c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
                c.LineTo(new Point(p.End.X, p.End.Y));
                c.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(line, active ? 2 : 1.5), lineGeo);

            if (!active) return;
            DrawHandle(context, p.Peak, _focusedHandle == 0);
            DrawHandle(context, p.SustainStart, _focusedHandle == 1);
            DrawHandle(context, p.End, _focusedHandle == 2);
        }
    }

    private void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? new Pen(FocusBrush, 2) : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var hit = SnsEnvelopeMapping.NearestHandle(pos.X, pos.Y, AmpPoints(Bounds.Width, Bounds.Height),
            FilterPoints(Bounds.Width, Bounds.Height), ActiveEnvelope, HandleRadius * 2);
        if (hit.Env < 0) return;
        if (hit.Env != ActiveEnvelope) ActiveEnvelope = hit.Env; // grabbing the other env activates it
        _dragEnv = hit.Env;
        _dragHandle = hit.Handle;
        _focusedHandle = hit.Handle;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragEnv < 0) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.SegmentMax(Bounds.Width, SustainWidth);
        var (a, d) = _dragEnv == 0 ? (AmpAttack, AmpDecay) : (FilterAttack, FilterDecay);
        switch (_dragHandle)
        {
            case 0: SetVal(0, SnsEnvelopeMapping.AttackFromX(pos.X, seg)); break;
            case 1:
                var aW = SnsEnvelopeMapping.TimeToWidth(a, seg);
                SetVal(1, SnsEnvelopeMapping.DecayFromX(pos.X, aW, seg));
                SetVal(2, SnsEnvelopeMapping.LevelFromY(pos.Y, Bounds.Height));
                break;
            case 2: // Release handle
                var aW2 = SnsEnvelopeMapping.TimeToWidth(a, seg);
                var dW2 = SnsEnvelopeMapping.TimeToWidth(d, seg);
                SetVal(3, SnsEnvelopeMapping.ReleaseFromX(pos.X, aW2, dW2, SustainWidth, seg));
                break;
        }
        e.Handled = true;

        // map slot (0=Attack,1=Decay,2=Sustain,3=Release) to the active drag-env's setters
        void SetVal(int slot, int value)
        {
            if (_dragEnv == 0)
                switch (slot) { case 0: AmpAttack = value; break; case 1: AmpDecay = value; break; case 2: AmpSustain = value; break; case 3: AmpRelease = value; break; }
            else
                switch (slot) { case 0: FilterAttack = value; break; case 1: FilterDecay = value; break; case 2: FilterSustain = value; break; case 3: FilterRelease = value; break; }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragEnv < 0) return;
        e.Pointer.Capture(null);
        _dragEnv = -1; _dragHandle = -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Tab:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _focusedHandle == 2) break;
                _focusedHandle++; e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        var amp = ActiveEnvelope == 0;
        switch (_focusedHandle)
        {
            case 0:
                if (!vertical) { if (amp) AmpAttack = SnsEnvelopeMapping.Clamp(AmpAttack + delta); else FilterAttack = SnsEnvelopeMapping.Clamp(FilterAttack + delta); }
                break;
            case 1:
                if (vertical) { if (amp) AmpSustain = SnsEnvelopeMapping.Clamp(AmpSustain + delta); else FilterSustain = SnsEnvelopeMapping.Clamp(FilterSustain + delta); }
                else { if (amp) AmpDecay = SnsEnvelopeMapping.Clamp(AmpDecay + delta); else FilterDecay = SnsEnvelopeMapping.Clamp(FilterDecay + delta); }
                break;
            case 2:
                if (!vertical) { if (amp) AmpRelease = SnsEnvelopeMapping.Clamp(AmpRelease + delta); else FilterRelease = SnsEnvelopeMapping.Clamp(FilterRelease + delta); }
                break;
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`. Fix any Avalonia 12 API mismatch (`PushOpacity`, `new Pen(IBrush, double)`, `DrawEllipse`) minimally and report.

- [ ] **Step 3: Commit**
```
git add Src/Controls/DualAdsrEnvelopeControl.cs
git commit -m "feat(sns): DualAdsrEnvelopeControl — shared-axis Amp+Filter overlay"
```

---

## Task 5: `SNSPartialViewModel` — filter + filter-envelope params

**Files:**
- Modify: `Src/ViewModels/SNSPartialViewModel.cs`

- [ ] **Step 1: Add the wrappers**

In `SNSPartialViewModel`, add these public properties alongside the Amp envelope properties (after `AmpEnvRelease`):
```csharp
    // --- Filter ---
    public ParamString FilterMode { get; }
    public ParamBool FilterSlopeSteep { get; }   // on = -24 dB, off = -12 dB
    public ParamInt FilterCutoff { get; }
    public ParamInt FilterResonance { get; }
    public ParamInt FilterCutoffKeyfollow { get; }
    public ParamInt HpfCutoff { get; }
    public ParamInt FilterEnvVeloSens { get; }

    // --- Filter envelope ---
    public ParamInt FilterEnvAttack { get; }
    public ParamInt FilterEnvDecay { get; }
    public ParamInt FilterEnvSustain { get; }
    public ParamInt FilterEnvRelease { get; }
    public ParamInt FilterEnvDepth { get; }

    private int _activeEnvelope; // 0 = Amp, 1 = Filter (bound to the dual control toggle)
    public int ActiveEnvelope { get => _activeEnvelope; set => this.RaiseAndSetIfChanged(ref _activeEnvelope, value); }
```

- [ ] **Step 2: Construct them in the constructor**

In the constructor, after the `AmpEnv*` assignments and before `IsOn = …`, add:
```csharp
        FilterMode = PS("Filter Mode");
        FilterSlopeSteep = Track(new ParamBool(partialDomain, byPath[PP + "Filter Slope"], writer, "-24", "-12"));
        FilterCutoff = PI("Filter Cutoff", 0, 127);
        FilterResonance = PI("Filter Resonance", 0, 127);
        FilterCutoffKeyfollow = PI("Filter Cutoff Keyfollow", -100, 100);
        HpfCutoff = PI("HPF Cutoff", 0, 127);
        FilterEnvVeloSens = PI("Filter Env Velocity Sens", -63, 63);

        FilterEnvAttack = PI("Filter Env Attack Time", 0, 127);
        FilterEnvDecay = PI("Filter Env Decay Time", 0, 127);
        FilterEnvSustain = PI("Filter Env Sustain Level", 0, 127);
        FilterEnvRelease = PI("Filter Env Release Time", 0, 127);
        FilterEnvDepth = PI("Filter Env Depth", -63, 63);
```

- [ ] **Step 3: Extend the editable list, init defaults, and add the card summary**

In the constructor, replace the `_editable = new IParam[] { … }` initializer to append the filter params. Add these entries before the closing `}` of the array:
```csharp
            ,FilterMode, FilterSlopeSteep, FilterCutoff, FilterResonance, FilterCutoffKeyfollow, HpfCutoff, FilterEnvVeloSens,
            FilterEnvAttack, FilterEnvDecay, FilterEnvSustain, FilterEnvRelease, FilterEnvDepth
```
In `InitDefaults`, add entries (before the closing `}`):
```csharp
        ,[PP + "Filter Mode"] = "Low pass",
        [PP + "Filter Slope"] = "-24",
        [PP + "Filter Cutoff"] = "127",
        [PP + "Filter Resonance"] = "0",
        [PP + "Filter Cutoff Keyfollow"] = "0",
        [PP + "HPF Cutoff"] = "0",
        [PP + "Filter Env Velocity Sens"] = "0",
        [PP + "Filter Env Attack Time"] = "0",
        [PP + "Filter Env Decay Time"] = "64",
        [PP + "Filter Env Sustain Level"] = "127",
        [PP + "Filter Env Release Time"] = "30",
        [PP + "Filter Env Depth"] = "0"
```
Add a `using Integra7AuralAlchemist.Models.Services;` at the top if not present (it is). Add the card summary property (near `WaveSummary`/`PanLabel`):
```csharp
    public string FilterSummary => $"{SnsFilterRules.Abbrev(FilterMode.Value)} {FilterCutoff.Value}";
```
And raise it when the mode/cutoff change — in the constructor, after the existing `OscWave.PropertyChanged += …` subscriptions, add:
```csharp
        FilterMode.PropertyChanged += OnFilterSummaryChanged;
        FilterCutoff.PropertyChanged += OnFilterSummaryChanged;
```
Add the handler (near `OnSummaryChanged`):
```csharp
    private void OnFilterSummaryChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => this.RaisePropertyChanged(nameof(FilterSummary));
```
And in `Dispose()`, before `foreach (var w in _wrappers)`, add:
```csharp
        FilterMode.PropertyChanged -= OnFilterSummaryChanged;
        FilterCutoff.PropertyChanged -= OnFilterSummaryChanged;
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`.

- [ ] **Step 5: Commit**
```
git add Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat(sns): partial VM filter + filter-envelope params, active-envelope, card summary"
```

---

## Task 6: View — Filter section + combined envelope + card filter preview

**Files:**
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

- [ ] **Step 1: Add a Filter section and switch the right-column grid to 5 rows**

In `Src/Views/SNSynthToneEditorView.axaml`, find the right-column grid opening:
```xml
            <!-- Right column: sections stack; the Amp Envelope row (*) grows to fill the window. -->
            <Grid Grid.Column="1" DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel"
                  RowDefinitions="Auto,Auto,*,Auto">
```
Change `RowDefinitions` to `"Auto,Auto,Auto,*,Auto"`. Then change the **Amp** border's `Grid.Row="1"` to `Grid.Row="2"` (leave Oscillator at `Grid.Row="0"`), insert a new Filter border at `Grid.Row="1"` (between Oscillator and Amp):
```xml
                <!-- Filter -->
                <Border Grid.Row="1" Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10" Margin="0,0,0,12">
                    <StackPanel Spacing="8">
                        <TextBlock Text="Filter" FontWeight="Bold"/>
                        <StackPanel Orientation="Horizontal" Spacing="16">
                            <StackPanel Spacing="2">
                                <TextBlock Text="Mode" ToolTip.Tip="Filter Mode"/>
                                <ComboBox ItemsSource="{Binding FilterMode.Options}"
                                          SelectedItem="{Binding FilterMode.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <StackPanel Spacing="2">
                                <TextBlock Text="Slope" ToolTip.Tip="Filter Slope"/>
                                <ToggleSwitch OnContent="Steep 24 dB" OffContent="Gentle 12 dB"
                                              IsChecked="{Binding FilterSlopeSteep.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <StackPanel Spacing="2" Width="150">
                                <TextBlock Text="Cutoff (Brightness)" TextWrapping="Wrap" ToolTip.Tip="Filter Cutoff"/>
                                <Slider Minimum="0" Maximum="127" Value="{Binding FilterCutoff.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <StackPanel Spacing="2" Width="150">
                                <TextBlock Text="Resonance" TextWrapping="Wrap" ToolTip.Tip="Filter Resonance"/>
                                <Slider Minimum="0" Maximum="127" Value="{Binding FilterResonance.Value, Mode=TwoWay}"/>
                            </StackPanel>
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" Spacing="16">
                            <StackPanel Spacing="2" Width="180">
                                <TextBlock Text="Cutoff Keyfollow" TextWrapping="Wrap" ToolTip.Tip="Filter Cutoff Keyfollow"/>
                                <Slider Minimum="-100" Maximum="100" Value="{Binding FilterCutoffKeyfollow.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <StackPanel Spacing="2" Width="150">
                                <TextBlock Text="HPF Cutoff" TextWrapping="Wrap" ToolTip.Tip="HPF Cutoff"/>
                                <Slider Minimum="0" Maximum="127" Value="{Binding HpfCutoff.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <StackPanel Spacing="2" Width="180">
                                <TextBlock Text="Env Velocity Sens" TextWrapping="Wrap" ToolTip.Tip="Filter Env Velocity Sens"/>
                                <Slider Minimum="-63" Maximum="63" Value="{Binding FilterEnvVeloSens.Value, Mode=TwoWay}"/>
                            </StackPanel>
                        </StackPanel>
                        <Button HorizontalAlignment="Left" Content="Advanced filter parameters…"
                                Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).AdvancedOscillatorCommand}"/>
                    </StackPanel>
                </Border>
```

- [ ] **Step 2: Replace the Amp Envelope panel with the combined Amp+Filter envelope**

Find the existing Amp Envelope border (it begins `<!-- Amp Envelope (fills remaining height) -->` with `Grid.Row="2"`). Replace that entire `<Border Grid.Row="2" …> … </Border>` block with the combined-envelope panel at `Grid.Row="3"`:
```xml
                <!-- Combined Amp + Filter envelope (shared axes), fills remaining height -->
                <Border Grid.Row="3" Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
                    <Grid RowDefinitions="Auto,*,Auto">
                        <DockPanel Grid.Row="0">
                            <TextBlock Text="Envelopes — Amp + Filter (shared time / level)" FontWeight="Bold"/>
                            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="10">
                                <TextBlock Text="Filter Depth" VerticalAlignment="Center" ToolTip.Tip="Filter Env Depth"/>
                                <Slider Width="120" Minimum="-63" Maximum="63"
                                        Value="{Binding FilterEnvDepth.Value, Mode=TwoWay}" VerticalAlignment="Center"/>
                                <ComboBox SelectedIndex="{Binding ActiveEnvelope, Mode=TwoWay}" VerticalAlignment="Center"
                                          ToolTip.Tip="Which envelope the keyboard edits">
                                    <ComboBoxItem>Amp</ComboBoxItem>
                                    <ComboBoxItem>Filter</ComboBoxItem>
                                </ComboBox>
                            </StackPanel>
                        </DockPanel>
                        <controls:DualAdsrEnvelopeControl Grid.Row="1" MinHeight="180" Margin="0,8"
                            ActiveEnvelope="{Binding ActiveEnvelope, Mode=TwoWay}"
                            AmpAttack="{Binding AmpEnvAttack.Value, Mode=TwoWay}"
                            AmpDecay="{Binding AmpEnvDecay.Value, Mode=TwoWay}"
                            AmpSustain="{Binding AmpEnvSustain.Value, Mode=TwoWay}"
                            AmpRelease="{Binding AmpEnvRelease.Value, Mode=TwoWay}"
                            FilterAttack="{Binding FilterEnvAttack.Value, Mode=TwoWay}"
                            FilterDecay="{Binding FilterEnvDecay.Value, Mode=TwoWay}"
                            FilterSustain="{Binding FilterEnvSustain.Value, Mode=TwoWay}"
                            FilterRelease="{Binding FilterEnvRelease.Value, Mode=TwoWay}"
                            AmpLineBrush="{StaticResource SnAmpEnvelopeBrush}"
                            AmpFillBrush="{StaticResource SnAmpEnvelopeFillBrush}"
                            FilterLineBrush="{StaticResource SnFilterEnvelopeBrush}"
                            FilterFillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                            BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                            GridBrush="{StaticResource SnEnvelopeGridBrush}"
                            AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                            HandleBrush="{StaticResource SnEnvelopeHandleBrush}"
                            FocusBrush="{StaticResource SnEnvelopeFocusBrush}"/>
                        <Grid Grid.Row="2" ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto">
                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Amp A/D/S/R" Foreground="{StaticResource SnAmpEnvelopeBrush}"
                                       VerticalAlignment="Center" Margin="0,0,8,2"/>
                            <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="8" Margin="0,0,0,2">
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding AmpEnvAttack.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding AmpEnvDecay.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding AmpEnvSustain.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding AmpEnvRelease.Value, Mode=TwoWay}"/>
                            </StackPanel>
                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Filter A/D/S/R" Foreground="{StaticResource SnFilterEnvelopeBrush}"
                                       VerticalAlignment="Center" Margin="0,0,8,0"/>
                            <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Spacing="8">
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding FilterEnvAttack.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding FilterEnvDecay.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding FilterEnvSustain.Value, Mode=TwoWay}"/>
                                <NumericUpDown Width="90" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding FilterEnvRelease.Value, Mode=TwoWay}"/>
                            </StackPanel>
                        </Grid>
                    </Grid>
                </Border>
```

- [ ] **Step 3: Move the partial-utilities buttons to row 4**

The Copy/Paste/Init `<StackPanel Grid.Row="3" …>` must move to row 4 now. Change its `Grid.Row="3"` to `Grid.Row="4"`.

- [ ] **Step 4: Add the filter summary + amber mini filter-env preview to the card**

In the card `DataTemplate` (the `ListBox.ItemTemplate`), find the amp preview block:
```xml
                                <!-- Preview thumbnail (dimmed, no handles): edit on the big graph at right. -->
                                <controls:AdsrEnvelopeControl Height="44" Preview="True" IsHitTestVisible="False"
                                    Opacity="0.85"
                                    Attack="{Binding AmpEnvAttack.Value}"
                                    Decay="{Binding AmpEnvDecay.Value}"
                                    Sustain="{Binding AmpEnvSustain.Value}"
                                    Release="{Binding AmpEnvRelease.Value}"/>
                                <TextBlock Text="amp envelope (preview)" FontSize="10" Opacity="0.45"
                                           HorizontalAlignment="Center"/>
```
Replace it with (adds a filter summary line + an amber filter-env preview using the retrofitted `LineBrush`/`FillBrush`):
```xml
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Filter" Opacity="0.6"/>
                                    <TextBlock Text="{Binding FilterSummary}"/>
                                </StackPanel>
                                <!-- Preview thumbnails (dimmed, no handles): edit on the big graph at right. -->
                                <controls:AdsrEnvelopeControl Height="40" Preview="True" IsHitTestVisible="False" Opacity="0.85"
                                    LineBrush="{StaticResource SnAmpEnvelopeBrush}" FillBrush="{StaticResource SnAmpEnvelopeFillBrush}"
                                    Attack="{Binding AmpEnvAttack.Value}" Decay="{Binding AmpEnvDecay.Value}"
                                    Sustain="{Binding AmpEnvSustain.Value}" Release="{Binding AmpEnvRelease.Value}"/>
                                <controls:AdsrEnvelopeControl Height="40" Preview="True" IsHitTestVisible="False" Opacity="0.85"
                                    LineBrush="{StaticResource SnFilterEnvelopeBrush}" FillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                                    Attack="{Binding FilterEnvAttack.Value}" Decay="{Binding FilterEnvDecay.Value}"
                                    Sustain="{Binding FilterEnvSustain.Value}" Release="{Binding FilterEnvRelease.Value}"/>
                                <TextBlock Text="amp (blue) + filter (amber) env — preview" FontSize="10" Opacity="0.45"
                                           HorizontalAlignment="Center"/>
```

- [ ] **Step 5: Build to confirm the view compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `0 Error(s)`. Fix any binding/namespace error minimally (the `controls:` xmlns already maps to `Integra7AuralAlchemist.Controls`; the editor-VM command bindings use the `#Root` form already in the file).

- [ ] **Step 6: Commit**
```
git add Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat(sns): editor Filter section + combined Amp/Filter envelope + card filter preview"
```

---

## Task 7: Full verification + smoke

**Files:** none.

- [ ] **Step 1: Run the full test suite**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --nologo`
Expected: all pass (Phase-1 suites + the new `DualEnvelopeHitTestTests`, `SnsFilterRulesTests`).

- [ ] **Step 2: Build the whole solution**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `0 Error(s)`.

- [ ] **Step 3: Manual smoke (requires the connected Integra-7; pause for the user)**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj`
Confirm:
1. The Filter section edits live (mode, slope toggle, cutoff, resonance, keyfollow, HPF, env velocity sens).
2. The combined envelope shows the Amp (blue) and Filter (amber) envelopes on one shared axis; both draggable; the Amp/Filter selector switches which the keyboard edits; numeric boxes drive both; Filter Depth slider sends.
3. Hardware edits to filter/filter-env params update the UI.
4. Cards show the filter summary + the amber filter-env preview; copy/paste/init carry the filter params.
5. Other tabs/layout still fill the window (no regressions from the new row).

- [ ] **Step 4: Verify against acceptance criteria (spec §9, filter/combined-envelope items) and note any deviations.**

---

## Self-review notes (for the executor)
- The dual control's drag handle indices are 0/1/2 (Attack/Decay/Release); Decay drag also sets Sustain. Task 4 Step 2 fixes the `OnPointerMoved` switch to use `case 2` for Release. Confirm by building.
- All envelope colors come from `App.axaml` resources; the controls only hold neutral defaults. No hex in XAML.
- Pitch envelope, the filter-response curve, and tone-wide octave/bend are **Plan 2b**, not here.
- `ActiveEnvelope` is bound both on the `DualAdsrEnvelopeControl` and the Amp/Filter `ComboBox` (two-way) via the VM's `ActiveEnvelope` int, so they stay in sync.
