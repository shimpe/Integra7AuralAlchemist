# PCM Synth Tone Editor — Phase 2c (Graphical multi-stage envelope) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a draggable graphical editor for PCM's 4-segment rate/level envelopes (TVP/TVF/TVA), shown above the existing numeric Time/Level rows in the Sound tab — bringing PCM's envelopes to visual parity with SN-S.

**Architecture:** A pure `PcmEnvelopeMapping` (value↔pixel geometry for 5 level points L0..L4 joined by 4 time segments T1..T4) mirrors the conventions of the existing `SnsEnvelopeMapping`; it is unit-tested with no Avalonia dependency. A new `MultiStageEnvelopeControl : Control` renders and drags one such envelope, delegating all math to the mapping (exactly as `DualAdsrEnvelopeControl` delegates to `SnsEnvelopeMapping`). The Sound tab hosts three instances (Pitch=bipolar, Filter, Amp=fixed endpoints).

**Tech Stack:** Avalonia 12, .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if the Debug exe is locked by a running app.

---

## File Structure

- **Create** `Src/Models/Services/PcmEnvelopeMapping.cs` — pure geometry (points, inverse, hit-test).
- **Create** `Tests/TestPcmEnvelopeMapping.cs` — unit tests for the mapping.
- **Create** `Src/Controls/MultiStageEnvelopeControl.cs` — the draggable control.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — add a control above each of the three numeric envelope grids.

---

## Task 1: PcmEnvelopeMapping (pure geometry + tests)

**Files:**
- Create: `Src/Models/Services/PcmEnvelopeMapping.cs`
- Create: `Tests/TestPcmEnvelopeMapping.cs`

- [ ] **Step 1: Write the tests** — create `Tests/TestPcmEnvelopeMapping.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPcmEnvelopeMapping
{
    [Test]
    public void SegmentMax_is_quarter_width()
    {
        Assert.That(PcmEnvelopeMapping.SegmentMax(400), Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.SegmentMax(0), Is.EqualTo(0));
    }

    [Test]
    public void Time_round_trips_through_width()
    {
        var seg = PcmEnvelopeMapping.SegmentMax(400);
        var w = PcmEnvelopeMapping.TimeToWidth(127, seg);
        Assert.That(w, Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.TimeFromWidth(w, seg), Is.EqualTo(127));
        Assert.That(PcmEnvelopeMapping.TimeFromWidth(0, seg), Is.EqualTo(0));
    }

    [Test]
    public void Unipolar_level_maps_top_and_bottom()
    {
        Assert.That(PcmEnvelopeMapping.LevelToY(127, 200, false), Is.EqualTo(0).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(0, 200, false), Is.EqualTo(200).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelFromY(0, 200, false), Is.EqualTo(127));
        Assert.That(PcmEnvelopeMapping.LevelFromY(200, 200, false), Is.EqualTo(0));
    }

    [Test]
    public void Bipolar_level_centers_at_zero()
    {
        Assert.That(PcmEnvelopeMapping.LevelToY(0, 200, true), Is.EqualTo(100).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(63, 200, true), Is.EqualTo(0).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelToY(-63, 200, true), Is.EqualTo(200).Within(1e-9));
        Assert.That(PcmEnvelopeMapping.LevelFromY(100, 200, true), Is.EqualTo(0));
    }

    [Test]
    public void ComputePoints_accumulates_segment_x()
    {
        // all times 127 → each segment = full segMax (width/4 = 100)
        var pts = PcmEnvelopeMapping.ComputePoints(127, 127, 127, 127, 0, 127, 127, 127, 0, 400, 200, false);
        Assert.That(pts.Length, Is.EqualTo(5));
        Assert.That(pts[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(pts[1].X, Is.EqualTo(100).Within(1e-9));
        Assert.That(pts[2].X, Is.EqualTo(200).Within(1e-9));
        Assert.That(pts[3].X, Is.EqualTo(300).Within(1e-9));
        Assert.That(pts[4].X, Is.EqualTo(400).Within(1e-9));
        Assert.That(pts[0].Y, Is.EqualTo(200).Within(1e-9)); // L0=0 → bottom
        Assert.That(pts[1].Y, Is.EqualTo(0).Within(1e-9));   // L1=127 → top
    }

    [Test]
    public void NearestHandle_finds_point_within_radius_else_minus1()
    {
        var pts = PcmEnvelopeMapping.ComputePoints(64, 64, 64, 64, 0, 100, 100, 100, 0, 400, 200, false);
        Assert.That(PcmEnvelopeMapping.NearestHandle(pts[2].X, pts[2].Y, pts, 10), Is.EqualTo(2));
        Assert.That(PcmEnvelopeMapping.NearestHandle(-50, -50, pts, 10), Is.EqualTo(-1));
    }

    [Test]
    public void TimeFromX_is_relative_to_previous_point()
    {
        var seg = PcmEnvelopeMapping.SegmentMax(400); // 100
        // a point dragged to x=350 whose previous point sits at x=300 → segment width 50 → ~64
        Assert.That(PcmEnvelopeMapping.TimeFromX(350, 300, seg), Is.EqualTo(64).Within(1));
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (no `PcmEnvelopeMapping`):

`& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

- [ ] **Step 3: Implement** — create `Src/Models/Services/PcmEnvelopeMapping.cs`:

```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping for a 4-segment rate/level envelope (PCM TVP/TVF/TVA):
/// 5 level points L0..L4 joined by 4 time segments T1..T4. No Avalonia dependency (unit-testable).
/// Levels are 0..127 (unipolar) or −63..+63 (bipolar, centered). The 4 segments share the width
/// equally as their max extent at value 127. Mirrors the conventions of SnsEnvelopeMapping.</summary>
public static class PcmEnvelopeMapping
{
    public const int Min = 0, Max = 127, BipolarMax = 63;

    public readonly record struct Point(double X, double Y);

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;
    public static int ClampBipolar(int v) => v < -BipolarMax ? -BipolarMax : v > BipolarMax ? BipolarMax : v;

    /// <summary>Max pixel width of one of the 4 time segments at full (127) value.</summary>
    public static double SegmentMax(double width) => width <= 0 ? 0 : width / 4.0;

    public static double TimeToWidth(int value, double segMax) => Clamp(value) / 127.0 * segMax;

    public static int TimeFromWidth(double w, double segMax)
    {
        if (segMax <= 0) return Min;
        if (w < 0) w = 0;
        return Clamp((int)Math.Round(w / segMax * 127.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Y pixel for a level. Unipolar: 0→bottom(height), 127→top(0). Bipolar: 0→center,
    /// +63→top, −63→bottom.</summary>
    public static double LevelToY(int level, double height, bool bipolar) => bipolar
        ? height / 2.0 - ClampBipolar(level) / (double)BipolarMax * (height / 2.0)
        : height - Clamp(level) / 127.0 * height;

    public static int LevelFromY(double y, double height, bool bipolar)
    {
        if (height <= 0) return bipolar ? 0 : Min;
        if (bipolar)
        {
            var v = (height / 2.0 - y) / (height / 2.0) * BipolarMax;
            return ClampBipolar((int)Math.Round(v, MidpointRounding.AwayFromZero));
        }
        var lvl = (1.0 - y / height) * 127.0;
        return Clamp((int)Math.Round(lvl, MidpointRounding.AwayFromZero));
    }

    /// <summary>The 5 envelope points (L0..L4) at the given times/levels.</summary>
    public static Point[] ComputePoints(int t1, int t2, int t3, int t4,
        int l0, int l1, int l2, int l3, int l4, double width, double height, bool bipolar)
    {
        var seg = SegmentMax(width);
        var x0 = 0.0;
        var x1 = x0 + TimeToWidth(t1, seg);
        var x2 = x1 + TimeToWidth(t2, seg);
        var x3 = x2 + TimeToWidth(t3, seg);
        var x4 = x3 + TimeToWidth(t4, seg);
        return new[]
        {
            new Point(x0, LevelToY(l0, height, bipolar)),
            new Point(x1, LevelToY(l1, height, bipolar)),
            new Point(x2, LevelToY(l2, height, bipolar)),
            new Point(x3, LevelToY(l3, height, bipolar)),
            new Point(x4, LevelToY(l4, height, bipolar)),
        };
    }

    /// <summary>Time value (0..127) for the segment ending at a dragged point, given the dragged X
    /// and the previous point's X.</summary>
    public static int TimeFromX(double x, double prevX, double segMax) => TimeFromWidth(x - prevX, segMax);

    /// <summary>Index (0..4) of the nearest point within <paramref name="radius"/>, else −1.</summary>
    public static int NearestHandle(double px, double py, Point[] pts, double radius)
    {
        var best = -1;
        var bestD = radius * radius;
        for (var i = 0; i < pts.Length; i++)
        {
            var dx = px - pts[i].X;
            var dy = py - pts[i].Y;
            var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
```

- [ ] **Step 4: Run to verify PASS** + full suite. Expected: PASS, +7 tests (207 → 214).

- [ ] **Step 5: Commit**

```
git add Src/Models/Services/PcmEnvelopeMapping.cs Tests/TestPcmEnvelopeMapping.cs
git commit -m "feat: PcmEnvelopeMapping geometry for the multi-stage envelope"
```

---

## Task 2: MultiStageEnvelopeControl

**Files:**
- Create: `Src/Controls/MultiStageEnvelopeControl.cs`

Implement a draggable single-envelope `Control` modeled closely on `Src/Controls/DualAdsrEnvelopeControl.cs`, but for the 5-point/4-segment shape, delegating all geometry to `PcmEnvelopeMapping` (Task 1). **Read `DualAdsrEnvelopeControl.cs` and `PcmEnvelopeMapping.cs` before starting** — match their structure and conventions exactly.

**Required public API (all `StyledProperty`):**
- `int Time1, Time2, Time3, Time4` — `defaultBindingMode: TwoWay`, default 0.
- `int Level0, Level1, Level2, Level3, Level4` — `TwoWay`, default 0.
- `bool Bipolar` — when true, levels are −63..+63 and the graph centers on a mid line (no fill); draw a faint center line.
- `bool FixedEndpoints` — when true, `Level0` and `Level4` are pinned to the baseline and NOT draggable in Y (used by TVA, whose endpoints are implicitly 0). Their handles may still move in X only where applicable (point 0 has no time; point 4 sets Time4).
- `bool Preview` — non-interactive (no handles, no input), for thumbnails.
- Brushes: `LineBrush, FillBrush, BackgroundBrush, GridBrush, AxisBrush, HandleBrush, FocusBrush` — same defaults/pattern as `DualAdsrEnvelopeControl` (use a `B(name, default)` helper).

**Behavior:**
- `static` ctor: `AffectsRender` over all value + brush + bool props; `FocusableProperty.OverrideDefaultValue(true)`.
- A `Points(w,h)` helper → `PcmEnvelopeMapping.ComputePoints(Time1..4, Level0..4, w, h, Bipolar)`.
- `Render`: fill background; draw horizontal level grid lines every 16 (via `LevelToY`) plus the left/bottom axis lines (you may inline this — do NOT reuse `EnvelopeAxes`, whose X-ticks assume the 3-segment ADSR model); if `Bipolar`, draw a center line at `h/2`. Draw the fill polygon ONLY when `!Bipolar` (baseline at `h`, up through the 5 points, back to baseline). Draw the connecting polyline through the 5 points. Unless `Preview`, draw a circular handle at each of the 5 points (radius 7); outline the focused handle with `FocusBrush`.
- `OnPointerPressed` (skip if `Preview`): `Focus()`; `NearestHandle` within `radius = HandleRadius*2`; if hit ≥ 0, set `_drag`/`_focused`, capture pointer, `e.Handled = true`.
- `OnPointerMoved` (only while `_drag ≥ 0`): let `i = _drag`, `pts = Points(w,h)`, `seg = SegmentMax(w)`:
  - Y → level: if NOT (`FixedEndpoints` && (i==0 || i==4)), set `Level{i} = LevelFromY(pos.Y, h, Bipolar)`.
  - X → time: for `i ∈ {1,2,3,4}`, set `Time{i} = TimeFromX(pos.X, pts[i-1].X, seg)`. (Point 0 has no preceding segment.)
  - Use a `SetLevel(int i, int v)` / `SetTime(int i, int v)` switch to route to the right property (mirror `DualAdsrEnvelopeControl`'s local `SetVal`). `e.Handled = true`.
- `OnPointerReleased`: release capture, `_drag = -1`.
- Keyboard handling is OPTIONAL for this phase; pointer drag is sufficient. (Note its absence in your report.)

**Verification:** the build must succeed (the control compiles). The geometry it relies on is already covered by Task 1's tests. After implementing, do a focused self-review of the drag routing (does dragging point _i_ change `Level{i}` and `Time{i}`, and are endpoints pinned when `FixedEndpoints`?).

- [ ] **Step 1: Read `Src/Controls/DualAdsrEnvelopeControl.cs` and `Src/Models/Services/PcmEnvelopeMapping.cs`, then implement `Src/Controls/MultiStageEnvelopeControl.cs` per the spec above.**

- [ ] **Step 2: Build** — `… build … --configuration Release`. Expected: Build succeeded.

- [ ] **Step 3: Run tests** — Expected: PASS (214, unchanged — no new tests here; the mapping is already covered).

- [ ] **Step 4: Commit**

```
git add Src/Controls/MultiStageEnvelopeControl.cs
git commit -m "feat: MultiStageEnvelopeControl (draggable rate/level envelope)"
```

---

## Task 3: Put the graphical envelopes in the Sound tab

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

Add a `MultiStageEnvelopeControl` above each of the three numeric env grids. The numeric rows stay (graphical + numeric, like SN-S).

- [ ] **Step 1: Pitch envelope (bipolar).** In the Pitch `Border`, immediately BEFORE the line `<TextBlock Text="Pitch envelope — times / levels (graphical editor in a later phase)" …/>`, insert:

```xml
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
```

And change that following `<TextBlock>`'s text from `(graphical editor in a later phase)` to `(drag the graph, or edit below)`.

- [ ] **Step 2: Filter envelope.** In the Filter `Border`, immediately BEFORE `<TextBlock Text="Filter envelope — times / levels (graphical editor in a later phase)" …/>`, insert the same control with the `Tvf` bindings and the filter brushes (NOT bipolar, NOT fixed endpoints):

```xml
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
```

And change that following `<TextBlock>`'s text to `(drag the graph, or edit below)`.

- [ ] **Step 3: Amp envelope (fixed endpoints).** In the Amp `Border`, immediately BEFORE `<TextBlock Text="Amp envelope — times / levels (graphical editor in a later phase)" …/>`, insert a control with `FixedEndpoints="True"`, `Level0`/`Level4` pinned to a literal `0`, the TVA level bindings on `Level1..3`, and the amp brushes:

```xml
                                        <controls:MultiStageEnvelopeControl Height="120" FixedEndpoints="True"
                                            Time1="{Binding TvaEnvTime1.Value, Mode=TwoWay}"
                                            Time2="{Binding TvaEnvTime2.Value, Mode=TwoWay}"
                                            Time3="{Binding TvaEnvTime3.Value, Mode=TwoWay}"
                                            Time4="{Binding TvaEnvTime4.Value, Mode=TwoWay}"
                                            Level0="0"
                                            Level1="{Binding TvaEnvLevel1.Value, Mode=TwoWay}"
                                            Level2="{Binding TvaEnvLevel2.Value, Mode=TwoWay}"
                                            Level3="{Binding TvaEnvLevel3.Value, Mode=TwoWay}"
                                            Level4="0"
                                            LineBrush="{StaticResource SnAmpEnvelopeBrush}"
                                            FillBrush="{StaticResource SnAmpEnvelopeFillBrush}"
                                            BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                                            GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                            AxisBrush="{StaticResource SnEnvelopeAxisBrush}"
                                            HandleBrush="{StaticResource SnEnvelopeHandleBrush}"
                                            FocusBrush="{StaticResource SnEnvelopeFocusBrush}"/>
```

And change that following `<TextBlock>`'s text to `(drag the graph, or edit below)`.

- [ ] **Step 4: Build** — `… build … --configuration Release`. Expected: Build succeeded (compiled bindings + the `controls:MultiStageEnvelopeControl` resolve; the `Sn*Envelope*Brush` resources are already defined for the SN-S editor).

- [ ] **Step 5: Run tests** — Expected: PASS (214).

- [ ] **Step 6: Commit**

```
git add Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: graphical multi-stage envelopes in the PCM Sound tab"
```

---

## Done criteria

- Full suite green (Phase 2b 207 + 7 new = 214).
- The Sound tab's Pitch, Filter, and Amp sections each show a draggable envelope graph above their numeric rows; dragging a point updates the numbers (and hardware) and vice-versa. Pitch is bipolar (centered); Amp pins its end levels to zero.
- This completes Phase 2 (the Sound tab). Remaining engine phases: Motion (LFOs), Zones (PMT), Response.
