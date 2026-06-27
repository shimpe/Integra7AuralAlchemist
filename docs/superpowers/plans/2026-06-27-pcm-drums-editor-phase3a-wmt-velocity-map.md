# PCM Drums Editor — Phase 3a (WMT velocity-map infrastructure) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the foundation for the Wave/WMT tab — make the drum WMT waves show real wave *names*, and build the reusable velocity-map control (a pure, unit-tested geometry helper + an Avalonia render control). No UI wiring yet; that is Phase 3b.

**Architecture:** (1) A one-line-per-param parameter-DB change assigns the existing `PARTIAL_WAVEFORMS` repr to the drum WMT wave-number params so their runtime `StringValue` resolves to a wave name (the INTEGRA-7 shares one waveform ROM; the repr is reference-deduped, so zero blob bloat). (2) `WmtVelocityMapping` is a pure, Avalonia-free geometry/hit-test helper (mirrors `PmtZoneMapping`). (3) `WmtVelocityMapControl` is a `Control` that renders four velocity lanes (WMT1..4) with draggable range edges, mirroring `PmtZoneEditorControl`.

**Tech Stack:** .NET 10, NUnit, Avalonia 12 custom `Control` with `StyledProperty` + `Render`. Build/test with the user-local SDK in Release (Debug exe is file-locked; MSB3027/MSB3021 = lock, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Run one test class: append `--filter "FullyQualifiedName~TestWmtVelocityMapping"` to the Test command.

---

### Task 1: Give the drum WMT waves real names (parameter-DB repr)

**Files:**
- Modify: `Tools/ParameterBlobGenerator/ParameterDefinitions.cs` (8 lines: WMT1–4 Wave Number L + R)

The drum WMT wave-number params currently have `repr:null`, so they read back as raw numbers. The PCM Synth tone wave param uses `repr:PARTIAL_WAVEFORMS` (number→name). The INTEGRA-7 PCM waveform ROM is shared, so the same repr applies. Assigning it makes the WMT wave `StringValue` resolve to a name, which the friendly editor's `ParamString` turns into a searchable list (Phase 3b), and incidentally names the waves in the raw Advanced tab too. The blob is reference-deduped — no size cost. Editing `ParameterDefinitions.cs` (the generator *source*, not the generated `parameters.bin`) is the sanctioned way to change the DB; the `GenerateParameterBlob` build target regenerates the git-ignored blob automatically.

- [ ] **Step 1: Edit the 8 WMT wave-number param definitions**

In `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`, each of these 8 lines ends with `repr:null),`. Change ONLY the trailing `repr:null` to `repr:PARTIAL_WAVEFORMS` on each. The lines (identified by their path) are:

```
"PCM Drum Kit Partial/WMT1 Wave Number L (Mono)"
"PCM Drum Kit Partial/WMT1 Wave Number R"
"PCM Drum Kit Partial/WMT2 Wave Number L (Mono)"
"PCM Drum Kit Partial/WMT2 Wave Number R"
"PCM Drum Kit Partial/WMT3 Wave Number L (Mono)"
"PCM Drum Kit Partial/WMT3 Wave Number R"
"PCM Drum Kit Partial/WMT4 Wave Number L (Mono)"
"PCM Drum Kit Partial/WMT4 Wave Number R"
```

For example, the first one changes from:
```csharp
            new(type:NUM, path:"PCM Drum Kit Partial/WMT1 Wave Number L (Mono)", offs:[0x00, 0x27], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:null),
```
to (only the final field changes):
```csharp
            new(type:NUM, path:"PCM Drum Kit Partial/WMT1 Wave Number L (Mono)", offs:[0x00, 0x27], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:PARTIAL_WAVEFORMS),
```
Do the same `repr:null` → `repr:PARTIAL_WAVEFORMS` change on all 8 lines listed above. Change nothing else on those lines, and touch no other parameters (other params also end in `repr:null` — only the 8 WMT wave-number lines above must change).

- [ ] **Step 2: Build (regenerates the blob)**

Run the Build command. Expected: `Build succeeded.` 0 errors. The build runs `GenerateParameterBlob` (you should see a `GenerateParameterBlob: producing …parameters.bin` message), regenerating `Src/Assets/parameters.bin` from the edited source.

- [ ] **Step 3: Verify the names resolved**

Run a quick check that WMT wave numbers now carry names in the blob (PowerShell):

```
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release
```
Expected: `Passed! - Failed: 0, Passed: 237`. (No test asserts the new names directly; this confirms the regenerated blob still loads and nothing regressed. A red build/test here means the edit broke the generator — STOP and report.)

- [ ] **Step 4: Commit**

```bash
git add Tools/ParameterBlobGenerator/ParameterDefinitions.cs
git commit -m "feat: name PCM-Drums WMT waves via PARTIAL_WAVEFORMS repr"
```

---

### Task 2: `WmtVelocityMapping` pure geometry helper (TDD)

**Files:**
- Create: `Tests/TestWmtVelocityMapping.cs`
- Create: `Src/Models/Services/WmtVelocityMapping.cs`

A pure, Avalonia-free helper (four horizontal lanes; X is velocity 0..127) mirroring `PmtZoneMapping`. Write the tests first.

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestWmtVelocityMapping.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWmtVelocityMapping
{
    [Test]
    public void Clamp_BoundsTo0_127()
    {
        Assert.That(WmtVelocityMapping.Clamp(-5), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.Clamp(200), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.Clamp(64), Is.EqualTo(64));
    }

    [Test]
    public void VelToX_Endpoints()
    {
        Assert.That(WmtVelocityMapping.VelToX(0, 254), Is.EqualTo(0).Within(1e-9));
        Assert.That(WmtVelocityMapping.VelToX(127, 254), Is.EqualTo(254).Within(1e-9));
        Assert.That(WmtVelocityMapping.VelToX(64, 127), Is.EqualTo(64).Within(1e-9));
    }

    [Test]
    public void XToVel_RoundTripsAndClamps()
    {
        Assert.That(WmtVelocityMapping.XToVel(0, 127), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.XToVel(127, 127), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.XToVel(-10, 127), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.XToVel(999, 127), Is.EqualTo(127));
        Assert.That(WmtVelocityMapping.XToVel(0, 0), Is.EqualTo(0)); // zero width guard
    }

    [Test]
    public void LaneRect_PartitionsHeightIntoFour()
    {
        var l0 = WmtVelocityMapping.LaneRect(0, 200, 400, 0);
        var l3 = WmtVelocityMapping.LaneRect(3, 200, 400, 0);
        Assert.That(l0.W, Is.EqualTo(200).Within(1e-9));
        Assert.That(l0.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(l0.H, Is.EqualTo(100).Within(1e-9));
        Assert.That(l3.Y, Is.EqualTo(300).Within(1e-9));
    }

    [Test]
    public void LaneAt_MapsYToLaneIndex()
    {
        Assert.That(WmtVelocityMapping.LaneAt(10, 400), Is.EqualTo(0));
        Assert.That(WmtVelocityMapping.LaneAt(150, 400), Is.EqualTo(1));
        Assert.That(WmtVelocityMapping.LaneAt(399, 400), Is.EqualTo(3));
        Assert.That(WmtVelocityMapping.LaneAt(-1, 400), Is.EqualTo(-1));
        Assert.That(WmtVelocityMapping.LaneAt(401, 400), Is.EqualTo(-1));
    }

    [Test]
    public void BandRect_ToleratesSwappedLoHi()
    {
        var a = WmtVelocityMapping.BandRect(20, 100, 0, 127, 400, 0);
        var b = WmtVelocityMapping.BandRect(100, 20, 0, 127, 400, 0);
        Assert.That(a.X, Is.EqualTo(20).Within(1e-9));
        Assert.That(a.W, Is.EqualTo(80).Within(1e-9));
        Assert.That(b.X, Is.EqualTo(a.X).Within(1e-9));
        Assert.That(b.W, Is.EqualTo(a.W).Within(1e-9));
    }

    [Test]
    public void HitBand_DetectsEdgesBodyAndMiss()
    {
        var band = new WmtVelocityMapping.Rect(50, 100, 40, 20); // x 50..90, y 100..120
        Assert.That(WmtVelocityMapping.HitBand(50, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Left));
        Assert.That(WmtVelocityMapping.HitBand(90, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Right));
        Assert.That(WmtVelocityMapping.HitBand(70, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.Body));
        Assert.That(WmtVelocityMapping.HitBand(10, 110, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
        Assert.That(WmtVelocityMapping.HitBand(70, 10, band, 6), Is.EqualTo(WmtVelocityMapping.Handle.None));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestWmtVelocityMapping"`.
Expected: FAIL — `WmtVelocityMapping` does not exist (compile error / type not found).

- [ ] **Step 3: Implement the helper**

Create `Src/Models/Services/WmtVelocityMapping.cs`:

```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the WMT velocity map: four horizontal lanes
/// (WMT1..4, top→bottom); X is velocity 0..127 left→right. No Avalonia dependency.</summary>
public static class WmtVelocityMapping
{
    public const int Min = 0, Max = 127;
    public const int LaneCount = 4;

    public readonly record struct Rect(double X, double Y, double W, double H);
    public enum Handle { None, Body, Left, Right }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double VelToX(int vel, double w) => Clamp(vel) / 127.0 * w;

    public static int XToVel(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Full-width pixel strip for lane <paramref name="index"/> (0..3), with vertical padding.</summary>
    public static Rect LaneRect(int index, double w, double h, double pad = 3)
    {
        var laneH = h / LaneCount;
        var y = index * laneH + pad;
        return new Rect(0, y, w, Math.Max(0, laneH - 2 * pad));
    }

    /// <summary>Lane index (0..3) containing pixel y, or -1 if out of range.</summary>
    public static int LaneAt(double y, double h)
    {
        if (h <= 0 || y < 0 || y > h) return -1;
        var i = (int)(y / (h / LaneCount));
        return i < 0 ? 0 : i >= LaneCount ? LaneCount - 1 : i;
    }

    /// <summary>Velocity band rect [lo..hi] within lane <paramref name="index"/>. Tolerates lo/hi swapped.</summary>
    public static Rect BandRect(int lo, int hi, int index, double w, double h, double pad = 3)
    {
        var lane = LaneRect(index, w, h, pad);
        var x = VelToX(Math.Min(lo, hi), w);
        var x2 = VelToX(Math.Max(lo, hi), w);
        return new Rect(x, lane.Y, x2 - x, lane.H);
    }

    /// <summary>Hit a band: Left/Right edge within <paramref name="margin"/> wins over Body; else None.</summary>
    public static Handle HitBand(double px, double py, Rect band, double margin)
    {
        var inY = py >= band.Y - margin && py <= band.Y + band.H + margin;
        if (!inY) return Handle.None;
        var inX = px >= band.X - margin && px <= band.X + band.W + margin;
        if (!inX) return Handle.None;
        if (Math.Abs(px - band.X) <= margin) return Handle.Left;
        if (Math.Abs(px - (band.X + band.W)) <= margin) return Handle.Right;
        if (px > band.X && px < band.X + band.W) return Handle.Body;
        return Handle.None;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the Test command with `--filter "FullyQualifiedName~TestWmtVelocityMapping"`.
Expected: PASS (7 tests).

- [ ] **Step 5: Run the full suite**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244` (237 prior + 7 new).

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/WmtVelocityMapping.cs Tests/TestWmtVelocityMapping.cs
git commit -m "feat: WmtVelocityMapping pure geometry helper + tests"
```

---

### Task 3: `WmtVelocityMapControl` render control

**Files:**
- Create: `Src/Controls/WmtVelocityMapControl.cs`

A `Control` that renders four velocity lanes (WMT1 top → WMT4 bottom). Each active lane draws its velocity range [lo..hi] as a trapezoid with fade-in/out ramps (using fade widths), dims when off, and is outlined when selected. Dragging a band's left/right edge sets its range; dragging the body moves both (cumulative from press, like `PmtZoneEditorControl`); clicking a lane sets `SelectedIndex`. Brush defaults live in C# (the accepted exception); the Phase-3b view binds them to resources.

- [ ] **Step 1: Create the control**

```csharp
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Four WMT velocity lanes (WMT1..4, top→bottom). X is velocity 0..127. Each active lane shows
/// its range [lo..hi] with fade ramps; drag an edge to resize, the body to move, or click a lane to
/// select it (TwoWay <see cref="SelectedIndex"/>). Geometry is delegated to <see cref="WmtVelocityMapping"/>.</summary>
public class WmtVelocityMapControl : Control
{
    private const double HandleMargin = 6;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<WmtVelocityMapControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Wmt1LoProperty = I(nameof(Wmt1Lo));
    public static readonly StyledProperty<int> Wmt1HiProperty = I(nameof(Wmt1Hi));
    public static readonly StyledProperty<int> Wmt1FadeLoProperty = I(nameof(Wmt1FadeLo));
    public static readonly StyledProperty<int> Wmt1FadeHiProperty = I(nameof(Wmt1FadeHi));
    public static readonly StyledProperty<int> Wmt2LoProperty = I(nameof(Wmt2Lo));
    public static readonly StyledProperty<int> Wmt2HiProperty = I(nameof(Wmt2Hi));
    public static readonly StyledProperty<int> Wmt2FadeLoProperty = I(nameof(Wmt2FadeLo));
    public static readonly StyledProperty<int> Wmt2FadeHiProperty = I(nameof(Wmt2FadeHi));
    public static readonly StyledProperty<int> Wmt3LoProperty = I(nameof(Wmt3Lo));
    public static readonly StyledProperty<int> Wmt3HiProperty = I(nameof(Wmt3Hi));
    public static readonly StyledProperty<int> Wmt3FadeLoProperty = I(nameof(Wmt3FadeLo));
    public static readonly StyledProperty<int> Wmt3FadeHiProperty = I(nameof(Wmt3FadeHi));
    public static readonly StyledProperty<int> Wmt4LoProperty = I(nameof(Wmt4Lo));
    public static readonly StyledProperty<int> Wmt4HiProperty = I(nameof(Wmt4Hi));
    public static readonly StyledProperty<int> Wmt4FadeLoProperty = I(nameof(Wmt4FadeLo));
    public static readonly StyledProperty<int> Wmt4FadeHiProperty = I(nameof(Wmt4FadeHi));

    public static readonly StyledProperty<bool> Wmt1OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt1On));
    public static readonly StyledProperty<bool> Wmt2OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt2On));
    public static readonly StyledProperty<bool> Wmt3OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt3On));
    public static readonly StyledProperty<bool> Wmt4OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt4On));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WmtVelocityMapControl, int>(nameof(SelectedIndex), 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<WmtVelocityMapControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> Lane1BrushProperty = B(nameof(Lane1Brush), new SolidColorBrush(Color.Parse("#6b8dff")));
    public static readonly StyledProperty<IBrush> Lane2BrushProperty = B(nameof(Lane2Brush), new SolidColorBrush(Color.Parse("#ff9e6b")));
    public static readonly StyledProperty<IBrush> Lane3BrushProperty = B(nameof(Lane3Brush), new SolidColorBrush(Color.Parse("#7ad19a")));
    public static readonly StyledProperty<IBrush> Lane4BrushProperty = B(nameof(Lane4Brush), new SolidColorBrush(Color.Parse("#d18ad1")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> LabelBrushProperty = B(nameof(LabelBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> HighlightBrushProperty = B(nameof(HighlightBrush), Brushes.Orange);

    public int Wmt1Lo { get => GetValue(Wmt1LoProperty); set => SetValue(Wmt1LoProperty, value); }
    public int Wmt1Hi { get => GetValue(Wmt1HiProperty); set => SetValue(Wmt1HiProperty, value); }
    public int Wmt1FadeLo { get => GetValue(Wmt1FadeLoProperty); set => SetValue(Wmt1FadeLoProperty, value); }
    public int Wmt1FadeHi { get => GetValue(Wmt1FadeHiProperty); set => SetValue(Wmt1FadeHiProperty, value); }
    public int Wmt2Lo { get => GetValue(Wmt2LoProperty); set => SetValue(Wmt2LoProperty, value); }
    public int Wmt2Hi { get => GetValue(Wmt2HiProperty); set => SetValue(Wmt2HiProperty, value); }
    public int Wmt2FadeLo { get => GetValue(Wmt2FadeLoProperty); set => SetValue(Wmt2FadeLoProperty, value); }
    public int Wmt2FadeHi { get => GetValue(Wmt2FadeHiProperty); set => SetValue(Wmt2FadeHiProperty, value); }
    public int Wmt3Lo { get => GetValue(Wmt3LoProperty); set => SetValue(Wmt3LoProperty, value); }
    public int Wmt3Hi { get => GetValue(Wmt3HiProperty); set => SetValue(Wmt3HiProperty, value); }
    public int Wmt3FadeLo { get => GetValue(Wmt3FadeLoProperty); set => SetValue(Wmt3FadeLoProperty, value); }
    public int Wmt3FadeHi { get => GetValue(Wmt3FadeHiProperty); set => SetValue(Wmt3FadeHiProperty, value); }
    public int Wmt4Lo { get => GetValue(Wmt4LoProperty); set => SetValue(Wmt4LoProperty, value); }
    public int Wmt4Hi { get => GetValue(Wmt4HiProperty); set => SetValue(Wmt4HiProperty, value); }
    public int Wmt4FadeLo { get => GetValue(Wmt4FadeLoProperty); set => SetValue(Wmt4FadeLoProperty, value); }
    public int Wmt4FadeHi { get => GetValue(Wmt4FadeHiProperty); set => SetValue(Wmt4FadeHiProperty, value); }
    public bool Wmt1On { get => GetValue(Wmt1OnProperty); set => SetValue(Wmt1OnProperty, value); }
    public bool Wmt2On { get => GetValue(Wmt2OnProperty); set => SetValue(Wmt2OnProperty, value); }
    public bool Wmt3On { get => GetValue(Wmt3OnProperty); set => SetValue(Wmt3OnProperty, value); }
    public bool Wmt4On { get => GetValue(Wmt4OnProperty); set => SetValue(Wmt4OnProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public IBrush Lane1Brush { get => GetValue(Lane1BrushProperty); set => SetValue(Lane1BrushProperty, value); }
    public IBrush Lane2Brush { get => GetValue(Lane2BrushProperty); set => SetValue(Lane2BrushProperty, value); }
    public IBrush Lane3Brush { get => GetValue(Lane3BrushProperty); set => SetValue(Lane3BrushProperty, value); }
    public IBrush Lane4Brush { get => GetValue(Lane4BrushProperty); set => SetValue(Lane4BrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    private int _dragLane = -1;
    private WmtVelocityMapping.Handle _dragHandle;
    private double _dragOrigX;
    private int _origLo, _origHi;

    static WmtVelocityMapControl()
    {
        AffectsRender<WmtVelocityMapControl>(
            Wmt1LoProperty, Wmt1HiProperty, Wmt1FadeLoProperty, Wmt1FadeHiProperty,
            Wmt2LoProperty, Wmt2HiProperty, Wmt2FadeLoProperty, Wmt2FadeHiProperty,
            Wmt3LoProperty, Wmt3HiProperty, Wmt3FadeLoProperty, Wmt3FadeHiProperty,
            Wmt4LoProperty, Wmt4HiProperty, Wmt4FadeLoProperty, Wmt4FadeHiProperty,
            Wmt1OnProperty, Wmt2OnProperty, Wmt3OnProperty, Wmt4OnProperty, SelectedIndexProperty,
            Lane1BrushProperty, Lane2BrushProperty, Lane3BrushProperty, Lane4BrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty, LabelBrushProperty, HighlightBrushProperty);
        FocusableProperty.OverrideDefaultValue<WmtVelocityMapControl>(true);
    }

    private (int lo, int hi, int fadeLo, int fadeHi, bool on, IBrush brush) Lane(int i) => i switch
    {
        0 => (Wmt1Lo, Wmt1Hi, Wmt1FadeLo, Wmt1FadeHi, Wmt1On, Lane1Brush),
        1 => (Wmt2Lo, Wmt2Hi, Wmt2FadeLo, Wmt2FadeHi, Wmt2On, Lane2Brush),
        2 => (Wmt3Lo, Wmt3Hi, Wmt3FadeLo, Wmt3FadeHi, Wmt3On, Lane3Brush),
        3 => (Wmt4Lo, Wmt4Hi, Wmt4FadeLo, Wmt4FadeHi, Wmt4On, Lane4Brush),
        _ => (0, 0, 0, 0, false, Lane1Brush),
    };

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var culture = CultureInfo.CurrentCulture;

        // Vertical velocity grid + value ticks every 16.
        for (var v = 0; v <= 127; v += 16)
        {
            var x = WmtVelocityMapping.VelToX(v, w);
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
            var vt = new FormattedText(v.ToString(culture), culture, FlowDirection.LeftToRight, Typeface.Default, 9, AxisBrush);
            context.DrawText(vt, new Point(x + 2, h - 12));
        }

        for (var i = 0; i < WmtVelocityMapping.LaneCount; i++)
        {
            var z = Lane(i);
            var lane = WmtVelocityMapping.LaneRect(i, w, h);
            // Lane separator line.
            context.DrawLine(axisPen, new Point(0, lane.Y + lane.H + 3), new Point(w, lane.Y + lane.H + 3));

            var opacity = z.on ? 1.0 : 0.25;
            using (context.PushOpacity(opacity))
            {
                if (z.on)
                {
                    var loX = WmtVelocityMapping.VelToX(Math.Min(z.lo, z.hi), w);
                    var hiX = WmtVelocityMapping.VelToX(Math.Max(z.lo, z.hi), w);
                    var fadeLoX = WmtVelocityMapping.VelToX(Math.Max(0, Math.Min(z.lo, z.hi) - z.fadeLo), w);
                    var fadeHiX = WmtVelocityMapping.VelToX(Math.Min(127, Math.Max(z.lo, z.hi) + z.fadeHi), w);
                    var top = lane.Y;
                    var bot = lane.Y + lane.H;

                    // Trapezoid: fade-in ramp up to lo, flat top to hi, fade-out ramp down to hi+fade.
                    var geo = new StreamGeometry();
                    using (var c = geo.Open())
                    {
                        c.BeginFigure(new Point(fadeLoX, bot), true);
                        c.LineTo(new Point(loX, top));
                        c.LineTo(new Point(hiX, top));
                        c.LineTo(new Point(fadeHiX, bot));
                        c.EndFigure(true);
                    }
                    using (context.PushOpacity(0.30)) context.DrawGeometry(z.brush, null, geo);
                    context.DrawGeometry(null, new Pen(z.brush, 2), geo);

                    var label = $"WMT{i + 1}  vel {Math.Min(z.lo, z.hi)}-{Math.Max(z.lo, z.hi)}";
                    var ft = new FormattedText(label, culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                    context.DrawText(ft, new Point(loX + 4, top + 2));
                }
                else
                {
                    var ft = new FormattedText($"WMT{i + 1}  (off)", culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                    context.DrawText(ft, new Point(4, lane.Y + 2));
                }
            }

            // Selected-lane highlight outline.
            if (i == SelectedIndex)
                context.DrawRectangle(null, new Pen(HighlightBrush, 1.5), new Rect(lane.X, lane.Y, lane.W, lane.H));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        var lane = WmtVelocityMapping.LaneAt(pos.Y, h);
        if (lane < 0) return;

        // Selecting the lane under the pointer.
        SelectedIndex = lane;

        var z = Lane(lane);
        if (z.on)
        {
            var band = WmtVelocityMapping.BandRect(z.lo, z.hi, lane, w, h);
            var hit = WmtVelocityMapping.HitBand(pos.X, pos.Y, band, HandleMargin);
            if (hit != WmtVelocityMapping.Handle.None)
            {
                _dragLane = lane;
                _dragHandle = hit;
                _dragOrigX = pos.X;
                _origLo = z.lo; _origHi = z.hi;
                e.Pointer.Capture(this);
            }
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragLane < 0) return;
        var pos = e.GetPosition(this);
        double w = Bounds.Width;
        var z = Lane(_dragLane);

        switch (_dragHandle)
        {
            case WmtVelocityMapping.Handle.Left:
                SetLo(_dragLane, Math.Min(WmtVelocityMapping.XToVel(pos.X, w), z.hi));
                break;
            case WmtVelocityMapping.Handle.Right:
                SetHi(_dragLane, Math.Max(WmtVelocityMapping.XToVel(pos.X, w), z.lo));
                break;
            case WmtVelocityMapping.Handle.Body:
                var d = (int)Math.Round((pos.X - _dragOrigX) / w * 127.0, MidpointRounding.AwayFromZero);
                if (_origLo + d < 0) d = -_origLo;
                if (_origHi + d > 127) d = 127 - _origHi;
                SetLo(_dragLane, _origLo + d); SetHi(_dragLane, _origHi + d);
                break;
        }
        e.Handled = true;

        void SetLo(int i, int v) { switch (i) { case 0: Wmt1Lo = v; break; case 1: Wmt2Lo = v; break; case 2: Wmt3Lo = v; break; case 3: Wmt4Lo = v; break; } }
        void SetHi(int i, int v) { switch (i) { case 0: Wmt1Hi = v; break; case 1: Wmt2Hi = v; break; case 2: Wmt3Hi = v; break; case 3: Wmt4Hi = v; break; } }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragLane < 0) return;
        e.Pointer.Capture(null);
        _dragLane = -1;
        e.Handled = true;
    }
}
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (The control is unused until Phase 3b.)

- [ ] **Step 3: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 4: Commit**

```bash
git add Src/Controls/WmtVelocityMapControl.cs
git commit -m "feat: WmtVelocityMapControl (four draggable velocity lanes)"
```

---

## Done criteria

The drum WMT wave-number params resolve to wave names in the regenerated blob (verifiable later in the friendly picker and the raw Advanced tab); `WmtVelocityMapping` is implemented and unit-tested (244 tests green); `WmtVelocityMapControl` compiles and renders four draggable velocity lanes. No UI uses them yet. Phase 3b adds the WMT layer view-models and the Wave tab that wires this control + the searchable wave picker.
