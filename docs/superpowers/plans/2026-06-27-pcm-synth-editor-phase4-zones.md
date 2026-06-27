# PCM Synth Tone Editor — Phase 4 (Zones / PMT) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the Zones tab — a 2-D **key × velocity** map where each of the 4 partials is a draggable / resizable rectangle (with an in-rectangle label showing its velocity and key range), a numeric grid beneath for precise values, and the tone-level Structure/Booster (per pair) + PMT Velocity-Control above.

**Architecture:** A pure `PmtZoneMapping` (key↔X, velocity↔Y, rectangle geometry, edge/body hit-testing) is unit-tested with no Avalonia dependency. A new custom-render `PmtZoneEditorControl` renders the four rectangles and handles drag-move / edge-resize, delegating geometry to the mapping (same control architecture as `MultiStageEnvelopeControl`), with 20 two-way `int`/`bool` styled properties. A per-zone `PcmPmtZoneViewModel` wraps each zone's params; `PcmPmtPanelViewModel` (tone-level, like the MFX panel) owns the four zones + Structure/Booster/Vel-Control. `PcmPmtPanelView` hosts the control, a numeric `ItemsControl` grid, and the structure controls. The Zones tab renders `Pmt` via `ContentControl` (ViewLocator resolves by name).

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if a running app holds the Debug exe lock.

---

## File Structure

- **Create** `Src/Models/Services/PmtZoneMapping.cs` — pure geometry/hit-test.
- **Create** `Tests/TestPmtZoneMapping.cs` — unit tests.
- **Create** `Src/Controls/PmtZoneEditorControl.cs` — the draggable 4-rectangle control.
- **Create** `Src/ViewModels/PcmPmtZoneViewModel.cs` — one zone's params.
- **Create** `Src/ViewModels/PcmPmtPanelViewModel.cs` — PMT panel (4 zones + structure/booster/vel-control).
- **Create** `Src/Views/PcmPmtPanelView.axaml` (+ `.axaml.cs`) — map + numeric grid + structure controls.
- **Modify** `Src/ViewModels/PCMSynthToneEditorViewModel.cs` — add a `Pmt` property.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — Zones tab shows `Pmt`.
- **Modify** `App.axaml` — add four `PmtZone{1..4}Brush` resources (no hardcoded colors).

---

## Task 1: PmtZoneMapping (pure geometry + tests)

**Files:**
- Create: `Src/Models/Services/PmtZoneMapping.cs`
- Create: `Tests/TestPmtZoneMapping.cs`

- [ ] **Step 1: Write the tests** — `Tests/TestPmtZoneMapping.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPmtZoneMapping
{
    [Test]
    public void Key_maps_across_width()
    {
        Assert.That(PmtZoneMapping.KeyToX(0, 254), Is.EqualTo(0).Within(1e-9));
        Assert.That(PmtZoneMapping.KeyToX(127, 254), Is.EqualTo(254).Within(1e-9));
        Assert.That(PmtZoneMapping.XToKey(0, 254), Is.EqualTo(0));
        Assert.That(PmtZoneMapping.XToKey(254, 254), Is.EqualTo(127));
    }

    [Test]
    public void Velocity_is_inverted_on_Y()
    {
        Assert.That(PmtZoneMapping.VelToY(127, 254), Is.EqualTo(0).Within(1e-9));   // loud = top
        Assert.That(PmtZoneMapping.VelToY(0, 254), Is.EqualTo(254).Within(1e-9));   // soft = bottom
        Assert.That(PmtZoneMapping.YToVel(0, 254), Is.EqualTo(127));
        Assert.That(PmtZoneMapping.YToVel(254, 254), Is.EqualTo(0));
    }

    [Test]
    public void ToRect_spans_the_key_and_velocity_range()
    {
        var r = PmtZoneMapping.ToRect(0, 127, 0, 127, 254, 254);
        Assert.That(r.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(r.W, Is.EqualTo(254).Within(1e-9));
        Assert.That(r.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(r.H, Is.EqualTo(254).Within(1e-9));
    }

    [Test]
    public void HitRect_detects_edges_then_body_then_outside()
    {
        var r = new PmtZoneMapping.Rect(100, 100, 80, 80); // x,y,w,h
        Assert.That(PmtZoneMapping.HitRect(101, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Left));
        Assert.That(PmtZoneMapping.HitRect(179, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Right));
        Assert.That(PmtZoneMapping.HitRect(140, 101, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Top));
        Assert.That(PmtZoneMapping.HitRect(140, 179, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Bottom));
        Assert.That(PmtZoneMapping.HitRect(140, 140, r, 6), Is.EqualTo(PmtZoneMapping.Handle.Body));
        Assert.That(PmtZoneMapping.HitRect(10, 10, r, 6), Is.EqualTo(PmtZoneMapping.Handle.None));
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (no `PmtZoneMapping`).

- [ ] **Step 3: Implement** — `Src/Models/Services/PmtZoneMapping.cs`:

```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the PMT key×velocity zone editor. X is the
/// MIDI key (0..127, left→right), Y is velocity (0..127, loud at top). No Avalonia dependency.</summary>
public static class PmtZoneMapping
{
    public const int Min = 0, Max = 127;

    public readonly record struct Rect(double X, double Y, double W, double H);
    public enum Handle { None, Body, Left, Right, Top, Bottom }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double KeyToX(int key, double w) => Clamp(key) / 127.0 * w;
    public static int XToKey(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    public static double VelToY(int vel, double h) => (1.0 - Clamp(vel) / 127.0) * h;
    public static int YToVel(double y, double h)
        => h <= 0 ? Min : Clamp((int)Math.Round((1.0 - y / h) * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Pixel rectangle for a zone. Tolerates lo/hi being swapped.</summary>
    public static Rect ToRect(int keyLo, int keyHi, int velLo, int velHi, double w, double h)
    {
        var x = KeyToX(Math.Min(keyLo, keyHi), w);
        var x2 = KeyToX(Math.Max(keyLo, keyHi), w);
        var yTop = VelToY(Math.Max(velLo, velHi), h);
        var yBot = VelToY(Math.Min(velLo, velHi), h);
        return new Rect(x, yTop, x2 - x, yBot - yTop);
    }

    /// <summary>Which part of one rectangle is under (px,py): an edge within <paramref name="margin"/>,
    /// else the body if inside, else None. Edges win over body.</summary>
    public static Handle HitRect(double px, double py, Rect r, double margin)
    {
        var inX = px >= r.X - margin && px <= r.X + r.W + margin;
        var inY = py >= r.Y - margin && py <= r.Y + r.H + margin;
        if (!inX || !inY) return Handle.None;
        if (Math.Abs(px - r.X) <= margin) return Handle.Left;
        if (Math.Abs(px - (r.X + r.W)) <= margin) return Handle.Right;
        if (Math.Abs(py - r.Y) <= margin) return Handle.Top;
        if (Math.Abs(py - (r.Y + r.H)) <= margin) return Handle.Bottom;
        if (px > r.X && px < r.X + r.W && py > r.Y && py < r.Y + r.H) return Handle.Body;
        return Handle.None;
    }
}
```

- [ ] **Step 4: Run to verify PASS** + full suite. Expected: PASS, +4 tests (222 → 226).

- [ ] **Step 5: Commit**

```
git add Src/Models/Services/PmtZoneMapping.cs Tests/TestPmtZoneMapping.cs
git commit -m "feat: PmtZoneMapping geometry + hit-test for the zone editor"
```

---

## Task 2: PmtZoneEditorControl

**Files:**
- Create: `Src/Controls/PmtZoneEditorControl.cs`

A custom-render `Control` (architecture identical to `Src/Controls/MultiStageEnvelopeControl.cs` — read it first) showing four key×velocity rectangles, draggable to move and edge-resizable, delegating geometry to `PmtZoneMapping` (Task 1).

**Public StyledProperties:**
- For each zone n in 1..4: `int Key{n}Lo, Key{n}Hi, Vel{n}Lo, Vel{n}Hi` (`defaultBindingMode: TwoWay`, default 0) and `bool Partial{n}On` (default false). (20 properties total.)
- `bool Preview` (non-interactive).
- Brushes (use the `B(name, def)` helper as in `MultiStageEnvelopeControl`): `Zone1Brush, Zone2Brush, Zone3Brush, Zone4Brush` (defaults: distinct translucent-friendly solid colors — `#6b8dff`, `#ff9e6b`, `#7ad19a`, `#d18ad1`), `BackgroundBrush` (`#1B1F22`), `GridBrush` (`#22ffffff`), `AxisBrush` (`#55ffffff`), `LabelBrush` (`Brushes.White`).

**Behavior** (mirror `MultiStageEnvelopeControl`'s ctor/Render/pointer structure):
- `static` ctor: `AffectsRender` over ALL 20 value props + `Preview` + all brushes; `FocusableProperty.OverrideDefaultValue(true)`.
- Helpers: `private (int lo,int hi,int vlo,int vhi,bool on, IBrush brush) Zone(int i)` returning the four ints + on + brush for zone i (1..4); `private PmtZoneMapping.Rect Rect(int i, double w, double h)` → `PmtZoneMapping.ToRect(...)`.
- `Render`:
  - fill `BackgroundBrush`.
  - grid: vertical lines every 12 keys (octave boundaries) via `KeyToX`, horizontal lines every ~16 velocity via `VelToY`, with `GridBrush`; left+bottom axes with `AxisBrush`.
  - for each zone i = 1..4 that is **On**: compute `Rect(i,w,h)`; fill with the zone brush at ~0.22 opacity (`using (context.PushOpacity(0.22)) context.FillRectangle(brush, rect)`); stroke the border with `new Pen(brush, 2)`; draw a label at the rect's top-left (inset ~4px) using `LabelBrush`, text `"P{i}  vel {vlo}-{vhi}  key {lo}-{hi}"` via a `FormattedText` (size ~11). Skip labels if the rect is too small (`W < 40 || H < 18`). Off zones are not drawn (or drawn faint — your call; not drawing is fine).
  - To draw text, use: `var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush); context.DrawText(ft, new Point(rect.X + 4, rect.Y + 3));`
- `OnPointerPressed` (skip if `Preview`): `Focus()`. Hit-test zones **front-to-back** — iterate i = 4,3,2,1 (so later zones win); for the first On zone whose `PmtZoneMapping.HitRect(pos.X,pos.Y, Rect(i,w,h), 6)` ≠ None, record `_dragZone = i`, `_dragHandle = that handle`, capture pointer, remember `_lastPos = pos`, `e.Handled = true`, `InvalidateVisual()`.
- `OnPointerMoved` (while `_dragZone >= 1`): `var pos = e.GetPosition(this);` per `_dragHandle`:
  - `Left`: set `Key{z}Lo = Min(XToKey(pos.X,w), Key{z}Hi)`.
  - `Right`: set `Key{z}Hi = Max(XToKey(pos.X,w), Key{z}Lo)`.
  - `Top`: set `Vel{z}Hi = Max(YToVel(pos.Y,h), Vel{z}Lo)`.
  - `Bottom`: set `Vel{z}Lo = Min(YToVel(pos.Y,h), Vel{z}Hi)`.
  - `Body`: compute `dKey = XToKey(pos.X,w) - XToKey(_lastPos.X,w)` and `dVel = YToVel(pos.Y,h) - YToVel(_lastPos.Y,h)`; shift all four bounds by the deltas, clamped to 0..127 **without changing the width/height** (i.e., if a shift would push a bound past 0 or 127, clamp the delta so the whole zone stays in range). Update `_lastPos = pos`.
  - Use a `SetKeyLo(z,v)/SetKeyHi/SetVelLo/SetVelHi(z,v)` switch helper to route to the right property (mirror `MultiStageEnvelopeControl`'s `SetLevel/SetTime`). `e.Handled = true`.
- `OnPointerReleased`: release capture, `_dragZone = -1`.
- Fields: `private const double HandleMargin = 6;` `private int _dragZone = -1;` `private PmtZoneMapping.Handle _dragHandle;` `private Point _lastPos;`

**Verify:** build must succeed. The geometry/hit-test is already covered by Task 1's tests. Self-review the drag routing: dragging an edge changes exactly one bound (clamped against its partner); body-drag shifts all four bounds equally and never resizes; off zones are not interactive.

- [ ] **Step 1: Read `Src/Controls/MultiStageEnvelopeControl.cs` and `Src/Models/Services/PmtZoneMapping.cs`, then implement `Src/Controls/PmtZoneEditorControl.cs` per the spec.**
- [ ] **Step 2: Build** — Expected: succeeded.
- [ ] **Step 3: Run tests** — Expected: PASS (226).
- [ ] **Step 4: Commit**

```
git add Src/Controls/PmtZoneEditorControl.cs
git commit -m "feat: PmtZoneEditorControl (draggable key/velocity zone map)"
```

---

## Task 3: Zone + panel view-models, wired into the editor

**Files:**
- Create: `Src/ViewModels/PcmPmtZoneViewModel.cs`
- Create: `Src/ViewModels/PcmPmtPanelViewModel.cs`
- Modify: `Src/ViewModels/PCMSynthToneEditorViewModel.cs`

- [ ] **Step 1: Create `Src/ViewModels/PcmPmtZoneViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One PMT zone (partial 1..4): its on/off plus key &amp; velocity range and fade widths.</summary>
public sealed class PcmPmtZoneViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }                 // 1..4
    public string Title => $"P{Index}";

    public ParamBool On { get; }
    public ParamInt KeyLo { get; }
    public ParamInt KeyHi { get; }
    public ParamInt KeyFadeLo { get; }
    public ParamInt KeyFadeHi { get; }
    public ParamInt VelLo { get; }
    public ParamInt VelHi { get; }
    public ParamInt VelWidthLo { get; }
    public ParamInt VelWidthHi { get; }

    public PcmPmtZoneViewModel(DomainBase pmtDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var p = $"PMT {index} ";
        ParamInt PI(string n) => Track(new ParamInt(pmtDomain, byPath[PMT + p + n], writer, 0, 127));
        ParamBool PB(string n) => Track(new ParamBool(pmtDomain, byPath[PMT + p + n], writer));

        On = PB("Partial Switch");
        KeyLo = PI("Keyboard Range Lower");
        KeyHi = PI("Keyboard Range Upper");
        KeyFadeLo = PI("Keyboard Fade Width Lower");
        KeyFadeHi = PI("Keyboard Fade Width Upper");
        VelLo = PI("Velocity Range Lower");
        VelHi = PI("Velocity Range Upper");
        VelWidthLo = PI("Velocity Width Lower");
        VelWidthHi = PI("Velocity Width Upper");
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Create `Src/ViewModels/PcmPmtPanelViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Tone-level Partial Mix Table panel: the four key/velocity zones plus the per-pair
/// Structure/Booster and the PMT Velocity-Control switch.</summary>
public sealed class PcmPmtPanelViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";
    private readonly List<IDisposable> _wrappers = [];

    public PcmPmtZoneViewModel Zone1 { get; }
    public PcmPmtZoneViewModel Zone2 { get; }
    public PcmPmtZoneViewModel Zone3 { get; }
    public PcmPmtZoneViewModel Zone4 { get; }
    public IReadOnlyList<PcmPmtZoneViewModel> Zones { get; }

    public ParamString VelocityControl { get; }   // Off / On / Random / Cycle
    public ParamInt Structure12 { get; }
    public ParamString Booster12 { get; }
    public ParamInt Structure34 { get; }
    public ParamString Booster34 { get; }

    public PcmPmtPanelViewModel(DomainBase pmtDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        Zone1 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 1));
        Zone2 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 2));
        Zone3 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 3));
        Zone4 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 4));
        Zones = new[] { Zone1, Zone2, Zone3, Zone4 };

        VelocityControl = Track(new ParamString(pmtDomain, byPath[PMT + "PMT Velocity Control"], writer));
        Structure12 = Track(new ParamInt(pmtDomain, byPath[PMT + "Structure Type 1 & 2"], writer, 1, 10));
        Booster12 = Track(new ParamString(pmtDomain, byPath[PMT + "Booster 1 & 2"], writer, new[] { "0", "6", "12", "18" }));
        Structure34 = Track(new ParamInt(pmtDomain, byPath[PMT + "Structure Type 3 & 4"], writer, 1, 10));
        Booster34 = Track(new ParamString(pmtDomain, byPath[PMT + "Booster 3 & 4"], writer, new[] { "0", "6", "12", "18" }));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 3: Wire `Pmt` into `Src/ViewModels/PCMSynthToneEditorViewModel.cs`.**

(a) Add the property near `Mfx`:

```csharp
    public PcmPmtPanelViewModel Pmt { get; }
```

(b) In the ctor, the `pmt`/`pmtByPath` locals already exist (used for the partials). After the `Mfx = new MfxPanelViewModel(...)` line, add:

```csharp
        Pmt = new PcmPmtPanelViewModel(pmt, pmtByPath, _writer);
```

(c) In `Dispose()`, add `Pmt.Dispose();` (next to `Mfx.Dispose();`).

- [ ] **Step 4: Build** — Expected: succeeded.
- [ ] **Step 5: Run tests** — Expected: PASS (226).
- [ ] **Step 6: Commit**

```
git add Src/ViewModels/PcmPmtZoneViewModel.cs Src/ViewModels/PcmPmtPanelViewModel.cs Src/ViewModels/PCMSynthToneEditorViewModel.cs
git commit -m "feat: PCM PMT zone + panel view-models"
```

---

## Task 4: PmtZone brushes, PcmPmtPanelView, and the Zones tab

**Files:**
- Modify: `App.axaml`
- Create: `Src/Views/PcmPmtPanelView.axaml` (+ `.axaml.cs`)
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

- [ ] **Step 1: Add the zone brushes to `App.axaml`.** Find the other `Sn*Brush` `SolidColorBrush` resources and add alongside them (inside the same `<Application.Resources>` / resource dictionary):

```xml
    <SolidColorBrush x:Key="PmtZone1Brush" Color="#6b8dff"/>
    <SolidColorBrush x:Key="PmtZone2Brush" Color="#ff9e6b"/>
    <SolidColorBrush x:Key="PmtZone3Brush" Color="#7ad19a"/>
    <SolidColorBrush x:Key="PmtZone4Brush" Color="#d18ad1"/>
```

- [ ] **Step 2: Create `Src/Views/PcmPmtPanelView.axaml.cs`:**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class PcmPmtPanelView : UserControl
{
    public PcmPmtPanelView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Create `Src/Views/PcmPmtPanelView.axaml`:**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:PcmPmtPanelViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.PcmPmtPanelView">
    <StackPanel Spacing="12">

        <!-- Structure / Booster / Velocity control -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <TextBlock Text="Structure" FontWeight="Bold"/>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="Velocity control" ToolTip.Tip="PMT Velocity Control"/>
                        <ComboBox ItemsSource="{Binding VelocityControl.Options}"
                                  SelectedItem="{Binding VelocityControl.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Structure 1&amp;2" ToolTip.Tip="Structure Type 1 &amp; 2"/>
                        <NumericUpDown Width="110" Minimum="1" Maximum="10" Increment="1" FormatString="0"
                                       Value="{Binding Structure12.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Booster 1&amp;2 (dB)" ToolTip.Tip="Booster 1 &amp; 2"/>
                        <ComboBox ItemsSource="{Binding Booster12.Options}"
                                  SelectedItem="{Binding Booster12.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Structure 3&amp;4" ToolTip.Tip="Structure Type 3 &amp; 4"/>
                        <NumericUpDown Width="110" Minimum="1" Maximum="10" Increment="1" FormatString="0"
                                       Value="{Binding Structure34.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Booster 3&amp;4 (dB)" ToolTip.Tip="Booster 3 &amp; 4"/>
                        <ComboBox ItemsSource="{Binding Booster34.Options}"
                                  SelectedItem="{Binding Booster34.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- 2-D zone map -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="6">
                <TextBlock Text="Zones — key (left→right) × velocity (loud at top). Drag to move, drag an edge to resize." Opacity="0.7" FontSize="11"/>
                <controls:PmtZoneEditorControl Height="240" Width="760" HorizontalAlignment="Left"
                    Partial1On="{Binding Zone1.On.Value}" Key1Lo="{Binding Zone1.KeyLo.Value, Mode=TwoWay}" Key1Hi="{Binding Zone1.KeyHi.Value, Mode=TwoWay}" Vel1Lo="{Binding Zone1.VelLo.Value, Mode=TwoWay}" Vel1Hi="{Binding Zone1.VelHi.Value, Mode=TwoWay}"
                    Partial2On="{Binding Zone2.On.Value}" Key2Lo="{Binding Zone2.KeyLo.Value, Mode=TwoWay}" Key2Hi="{Binding Zone2.KeyHi.Value, Mode=TwoWay}" Vel2Lo="{Binding Zone2.VelLo.Value, Mode=TwoWay}" Vel2Hi="{Binding Zone2.VelHi.Value, Mode=TwoWay}"
                    Partial3On="{Binding Zone3.On.Value}" Key3Lo="{Binding Zone3.KeyLo.Value, Mode=TwoWay}" Key3Hi="{Binding Zone3.KeyHi.Value, Mode=TwoWay}" Vel3Lo="{Binding Zone3.VelLo.Value, Mode=TwoWay}" Vel3Hi="{Binding Zone3.VelHi.Value, Mode=TwoWay}"
                    Partial4On="{Binding Zone4.On.Value}" Key4Lo="{Binding Zone4.KeyLo.Value, Mode=TwoWay}" Key4Hi="{Binding Zone4.KeyHi.Value, Mode=TwoWay}" Vel4Lo="{Binding Zone4.VelLo.Value, Mode=TwoWay}" Vel4Hi="{Binding Zone4.VelHi.Value, Mode=TwoWay}"
                    Zone1Brush="{StaticResource PmtZone1Brush}" Zone2Brush="{StaticResource PmtZone2Brush}"
                    Zone3Brush="{StaticResource PmtZone3Brush}" Zone4Brush="{StaticResource PmtZone4Brush}"
                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}"
                    GridBrush="{StaticResource SnEnvelopeGridBrush}"
                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
            </StackPanel>
        </Border>

        <!-- Numeric grid -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="6">
                <TextBlock Text="Precise values" FontWeight="Bold"/>
                <Grid ColumnDefinitions="40,60,110,110,110,110,110,110,110,110" ColumnSpacing="6">
                    <TextBlock Grid.Column="2" Text="Key Lo" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="3" Text="Key Hi" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="4" Text="Key Fade Lo" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="5" Text="Key Fade Hi" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="6" Text="Vel Lo" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="7" Text="Vel Hi" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="8" Text="Vel W Lo" Opacity="0.6" FontSize="11"/>
                    <TextBlock Grid.Column="9" Text="Vel W Hi" Opacity="0.6" FontSize="11"/>
                </Grid>
                <ItemsControl ItemsSource="{Binding Zones}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="vm:PcmPmtZoneViewModel">
                            <Grid ColumnDefinitions="40,60,110,110,110,110,110,110,110,110" ColumnSpacing="6" Margin="0,2">
                                <TextBlock Grid.Column="0" Text="{Binding Title}" VerticalAlignment="Center" FontWeight="SemiBold"/>
                                <ToggleSwitch Grid.Column="1" OnContent="" OffContent="" IsChecked="{Binding On.Value, Mode=TwoWay}" VerticalAlignment="Center"/>
                                <NumericUpDown Grid.Column="2" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding KeyLo.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="3" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding KeyHi.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="4" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding KeyFadeLo.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="5" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding KeyFadeHi.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="6" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding VelLo.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="7" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding VelHi.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="8" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding VelWidthLo.Value, Mode=TwoWay}"/>
                                <NumericUpDown Grid.Column="9" Minimum="0" Maximum="127" Increment="1" FormatString="0" Value="{Binding VelWidthHi.Value, Mode=TwoWay}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 4: Replace the Zones tab body** in `Src/Views/PCMSynthToneEditorView.axaml`. Find:

```xml
                    <TabItem Header="Zones">
                        <TextBlock Margin="12" Opacity="0.6" Text="Zones — PMT key/velocity map (Phase 4)."/>
                    </TabItem>
```

Replace with:

```xml
                    <TabItem Header="Zones">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
                            <ContentControl Content="{Binding Pmt}" Margin="0,8,0,0"/>
                        </ScrollViewer>
                    </TabItem>
```

- [ ] **Step 5: Build** — Expected: succeeded. (ViewLocator resolves `PcmPmtPanelViewModel`→`PcmPmtPanelView`; the `PmtZone*Brush` + `SnEnvelope*Brush` resources resolve.)
- [ ] **Step 6: Run tests** — Expected: PASS (226).
- [ ] **Step 7: Commit**

```
git add App.axaml Src/Views/PcmPmtPanelView.axaml Src/Views/PcmPmtPanelView.axaml.cs Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: PCM Zones tab (PMT 2-D key/velocity map + numeric grid)"
```

---

## Done criteria

- Full suite green (Phase 3 222 + 4 new = 226).
- The Zones tab shows the four partials as colored rectangles on a key×velocity map: drag a body to move a zone, drag an edge to resize it, and the in-rectangle label + the numeric grid update together (and the hardware). Structure/Booster/Velocity-Control sit above.
- Last remaining engine phase: **Response** (velocity / aftertouch / keyfollow grid).
