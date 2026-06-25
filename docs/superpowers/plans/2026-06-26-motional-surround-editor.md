# Motional Surround Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a spatial, musical Motional Surround editor — a 2D "room map" where the 16 internal parts and the external part are dragged into place, with prominent global room/ambience controls, fast per-part fine-tuning, an overview list, and UI-level presets.

**Architecture:** A new top-level "Motional Surround" tab hosts `MotionalSurroundView` bound to `MotionalSurroundViewModel`. The VM binds to the **live, shared `FullyQualifiedParameter` (FQP)** objects already held by the `Studio Set Common Motional Surround` domain (globals + external) and each `Studio Set Part {n}` domain (per-part L-R/F-B/Width/Ambience). Reads/state-sync come free via `INotifyPropertyChanged`. Writes go through `DomainBase.WriteToIntegraAsync(path, value)` behind a **per-key debounce** (Rx `GroupBy` + `Throttle(Constants.THROTTLE)`) so a 2-axis puck drag flushes both L-R and F-B without the global `ui2hw` debounce dropping a sibling axis.

**Tech Stack:** C# / .NET 10, Avalonia 12 (MVVM), ReactiveUI (`ReactiveObject`, `RaiseAndSetIfChanged`), System.Reactive, NUnit (pure-service tests). No new dependencies.

---

## Build & Test Commands

The system `dotnet` is only 8/9; use the user-local .NET 10 SDK.

- Build app: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj -c Debug`
- Run tests: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -c Debug`
- Run a single test: append `--filter FullyQualifiedName~MotionalSurroundMapping`

(Use the PowerShell tool; the call operator `&` runs the exe.)

---

## File Structure

**New files**
- `Src/Models/Services/MotionalSurroundMapping.cs` — pure value↔coordinate math, clamping, channel validation. No Avalonia deps.
- `Src/ViewModels/MotionalSurroundPartViewModel.cs` — one part's MS values (internal → a `StudioSetPart` domain; external → the common domain). Holds FQP refs, suppress/echo handling, reactive props, canvas coords.
- `Src/ViewModels/MotionalSurroundViewModel.cs` — orchestrates globals, the parts collection, selection, the per-key debounced write pipeline, and presets.
- `Src/Views/MotionalSurroundView.axaml` (+ `.axaml.cs`) — Option-A layout + interactive room-map canvas (pointer drag, keyboard nudge).
- `Tests/TestMotionalSurroundMapping.cs` — NUnit tests for the mapping helper.

**Modified files**
- `Src/ViewModels/MainWindowViewModel.cs` — construct/expose `MotionalSurroundVm` after the parts loop; clear it on disconnect.
- `Src/Views/MainWindow.axaml` — add the top-level "Motional Surround" `TabItem` + not-connected placeholder.

---

## Parameter reference (exact paths)

Common domain — `Integra7Domain.StudioSetCommonMotionalSurround`, prefix `Studio Set Common Motional Surround/`:
- `Motional Surround Switch` (repr OFF_ON → "OFF"/"ON")
- `Room Type` (repr → "Room1"/"Room2"/"Hall1"/"Hall2")
- `Room Size` (repr → "Small"/"Medium"/"Large")
- `Motional Surround Depth` (0–100)
- `Ambience Level` (0–127), `Ambience Time` (0–100), `Ambience Density` (0–100), `Ambience HF Damp` (0–100)
- `Ext Part L-R` (−64..63), `Ext Part F-B` (−64..63), `Ext Part Width` (0..32), `Ext Part Ambience Send Level` (0..127), `Ext Part Control Channel` (repr → "1".."16","OFF")

Per-part domain — `Integra7Domain.StudioSetPart(zeroBasedPartNo)`, prefix `Studio Set Part/`:
- `Motional Surround L-R` (−64..63), `Motional Surround F-B` (−64..63), `Motional Surround Width` (0..32), `Motional Surround Ambience Send Level` (0..127)

Display values round-trip through `DomainBase.WriteToIntegraAsync(path, displayValue)` → `DisplayValueToRawValueConverter`. Numeric params take an integer string (InvariantCulture). Repr params take the exact display string above.

---

## Task 1: Pure mapping helper (TDD)

**Files:**
- Create: `Src/Models/Services/MotionalSurroundMapping.cs`
- Test: `Tests/TestMotionalSurroundMapping.cs`

- [ ] **Step 1: Write the failing tests**

`Tests/TestMotionalSurroundMapping.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class MotionalSurroundMappingTests
{
    // Clamp keeps values inside the inclusive range.
    [TestCase(-100, -64, 63, -64)]
    [TestCase(100, -64, 63, 63)]
    [TestCase(0, -64, 63, 0)]
    [TestCase(40, 0, 32, 32)]
    [TestCase(-1, 0, 127, 0)]
    public void TestClamp(int v, int min, int max, int expected)
    {
        Assert.That(MotionalSurroundMapping.Clamp(v, min, max), Is.EqualTo(expected));
    }

    // value -> normalized 0..1 across an inclusive integer range.
    [TestCase(-64, -64, 63, 0.0)]
    [TestCase(63, -64, 63, 1.0)]
    [TestCase(0, 0, 32, 0.0)]
    [TestCase(32, 0, 32, 1.0)]
    [TestCase(16, 0, 32, 0.5)]
    public void TestToNormalized(int value, int min, int max, double expected)
    {
        Assert.That(MotionalSurroundMapping.ToNormalized(value, min, max), Is.EqualTo(expected).Within(1e-9));
    }

    // normalized 0..1 -> nearest integer in range; edges and clamping covered.
    [TestCase(0.0, -64, 63, -64)]
    [TestCase(1.0, -64, 63, 63)]
    [TestCase(0.5, -64, 63, 0)]   // midpoint of -64..63 rounds to 0 (-0.5 -> round to 0)
    [TestCase(-0.2, -64, 63, -64)]
    [TestCase(1.2, -64, 63, 63)]
    [TestCase(0.5, 0, 32, 16)]
    public void TestFromNormalized(double n, int min, int max, int expected)
    {
        Assert.That(MotionalSurroundMapping.FromNormalized(n, min, max), Is.EqualTo(expected));
    }

    // Round-trip: a value -> normalized -> value is stable for representative values.
    [TestCase(-64)]
    [TestCase(-32)]
    [TestCase(0)]
    [TestCase(31)]
    [TestCase(63)]
    public void TestLrFbRoundTrip(int value)
    {
        var n = MotionalSurroundMapping.ToNormalized(value, -64, 63);
        Assert.That(MotionalSurroundMapping.FromNormalized(n, -64, 63), Is.EqualTo(value));
    }

    [TestCase("OFF", true)]
    [TestCase("1", true)]
    [TestCase("16", true)]
    [TestCase("0", false)]
    [TestCase("17", false)]
    [TestCase("", false)]
    [TestCase("x", false)]
    public void TestControlChannelValidation(string display, bool expected)
    {
        Assert.That(MotionalSurroundMapping.IsValidControlChannel(display), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -c Debug --filter FullyQualifiedName~MotionalSurroundMapping`
Expected: FAIL — `MotionalSurroundMapping` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

`Src/Models/Services/MotionalSurroundMapping.cs`:
```csharp
using System.Globalization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure value/coordinate helpers for the Motional Surround editor. No UI dependencies.
///
/// Axis mapping on the 2D room map (documented so it is unambiguous):
///   L-R: left edge = -64, center = 0, right edge = +63   (normalized 0..1 left->right)
///   F-B: top edge  = -64 (Front), center = 0, bottom = +63 (Back) (normalized 0..1 top->bottom)
/// Both L-R and F-B share the same inclusive integer range [-64, +63].
/// Width is [0, 32]; Ambience send is [0, 127].
/// </summary>
public static class MotionalSurroundMapping
{
    public const int LrFbMin = -64;
    public const int LrFbMax = 63;
    public const int WidthMin = 0;
    public const int WidthMax = 32;
    public const int AmbienceMin = 0;
    public const int AmbienceMax = 127;

    public static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    /// <summary>value in [min,max] -> fraction in [0,1] (clamped).</summary>
    public static double ToNormalized(int value, int min, int max)
    {
        if (max == min) return 0.0;
        var n = (double)(value - min) / (max - min);
        return n < 0.0 ? 0.0 : n > 1.0 ? 1.0 : n;
    }

    /// <summary>fraction in [0,1] -> nearest integer in [min,max] (clamped).</summary>
    public static int FromNormalized(double normalized, int min, int max)
    {
        if (normalized < 0.0) normalized = 0.0;
        if (normalized > 1.0) normalized = 1.0;
        var v = (int)System.Math.Round(min + normalized * (max - min),
            System.MidpointRounding.AwayFromZero);
        return Clamp(v, min, max);
    }

    /// <summary>Ext Part Control Channel: valid display values are "1".."16" or "OFF".</summary>
    public static bool IsValidControlChannel(string display)
    {
        if (display == "OFF") return true;
        return int.TryParse(display, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ch)
               && ch is >= 1 and <= 16;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -c Debug --filter FullyQualifiedName~MotionalSurroundMapping`
Expected: PASS (all cases). Note `TestFromNormalized(0.5, -64, 63, 0)`: `-64 + 0.5*127 = -0.5`, `Round(AwayFromZero) = -1`? Verify: AwayFromZero rounds -0.5 to -1. **Adjust expected to -1** if the run shows -1, OR switch midpoint test to `0.504` → 0. Use the actual computed value; do not fight the math. (Recommended: change that single case to `[TestCase(0.5039, -64, 63, 0)]` and `[TestCase(0.5, -64, 63, -1)]` to document the exact midpoint behavior.)

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/MotionalSurroundMapping.cs Tests/TestMotionalSurroundMapping.cs
git commit -m "Add pure MotionalSurroundMapping helper with tests"
```

---

## Task 2: Per-part view model

**Files:**
- Create: `Src/ViewModels/MotionalSurroundPartViewModel.cs`

Holds the FQP refs for one part and exposes reactive properties. Internal parts use a `StudioSetPart` domain with `Motional Surround …` paths; the external part uses the common domain with `Ext Part …` paths and adds a control-channel property. Echo-suppression mirrors `DataTemplateProvider` (`_suppress`): updates coming **from** the model never re-enqueue a write.

- [ ] **Step 1: Write the class**

`Src/ViewModels/MotionalSurroundPartViewModel.cs`:
```csharp
using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

public class MotionalSurroundPartViewModel : ViewModelBase
{
    private readonly MotionalSurroundViewModel _parent;
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _lrParam;
    private readonly FullyQualifiedParameter _fbParam;
    private readonly FullyQualifiedParameter _widthParam;
    private readonly FullyQualifiedParameter _ambParam;
    private readonly FullyQualifiedParameter? _channelParam; // external only

    private bool _suppress;
    private int _lr, _fb, _width, _amb;
    private string _channel = "OFF";
    private bool _isSelected;

    public bool IsExternal { get; }
    /// <summary>Zero-based internal part index, or -1 for the external part.</summary>
    public int PartIndex { get; }
    public string Key => IsExternal ? "ext" : $"p{PartIndex}";
    public string Label => IsExternal ? "Ext" : $"P{PartIndex + 1}";
    public DomainBase Domain => _domain;
    public string LrPath => _lrParam.ParSpec.Path;
    public string FbPath => _fbParam.ParSpec.Path;

    public MotionalSurroundPartViewModel(MotionalSurroundViewModel parent, DomainBase domain,
        int partIndex, bool isExternal,
        FullyQualifiedParameter lr, FullyQualifiedParameter fb,
        FullyQualifiedParameter width, FullyQualifiedParameter amb,
        FullyQualifiedParameter? channel = null)
    {
        _parent = parent;
        _domain = domain;
        PartIndex = partIndex;
        IsExternal = isExternal;
        _lrParam = lr; _fbParam = fb; _widthParam = width; _ambParam = amb; _channelParam = channel;

        // Initialize from the live model with writes suppressed.
        ApplyFromModel();

        // Track hardware/preset/raw-tab changes on the shared FQP objects.
        lr.PropertyChanged += OnParamChanged;
        fb.PropertyChanged += OnParamChanged;
        width.PropertyChanged += OnParamChanged;
        amb.PropertyChanged += OnParamChanged;
        if (channel != null) channel.PropertyChanged += OnParamChanged;
    }

    private void OnParamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        // FQP changes may arrive on a MIDI background thread; marshal to UI.
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private static int ParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void ApplyFromModel()
    {
        _suppress = true;
        try
        {
            Lr = MotionalSurroundMapping.Clamp(ParseInt(_lrParam.StringValue),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            Fb = MotionalSurroundMapping.Clamp(ParseInt(_fbParam.StringValue),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            Width = MotionalSurroundMapping.Clamp(ParseInt(_widthParam.StringValue),
                MotionalSurroundMapping.WidthMin, MotionalSurroundMapping.WidthMax);
            Ambience = MotionalSurroundMapping.Clamp(ParseInt(_ambParam.StringValue),
                MotionalSurroundMapping.AmbienceMin, MotionalSurroundMapping.AmbienceMax);
            if (_channelParam != null) Channel = _channelParam.StringValue;
        }
        finally { _suppress = false; }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public int Lr
    {
        get => _lr;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            if (_lr == value) return;
            this.RaiseAndSetIfChanged(ref _lr, value);
            this.RaisePropertyChanged(nameof(CanvasX));
            this.RaisePropertyChanged(nameof(PositionLabel));
            if (!_suppress) _parent.EnqueuePositionWrite(this);
        }
    }

    public int Fb
    {
        get => _fb;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            if (_fb == value) return;
            this.RaiseAndSetIfChanged(ref _fb, value);
            this.RaisePropertyChanged(nameof(CanvasY));
            this.RaisePropertyChanged(nameof(PositionLabel));
            if (!_suppress) _parent.EnqueuePositionWrite(this);
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.WidthMin, MotionalSurroundMapping.WidthMax);
            if (_width == value) return;
            this.RaiseAndSetIfChanged(ref _width, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _widthParam.ParSpec.Path, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public int Ambience
    {
        get => _amb;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.AmbienceMin, MotionalSurroundMapping.AmbienceMax);
            if (_amb == value) return;
            this.RaiseAndSetIfChanged(ref _amb, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _ambParam.ParSpec.Path, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>External part only. Valid values: "1".."16" or "OFF".</summary>
    public string Channel
    {
        get => _channel;
        set
        {
            if (_channelParam == null) return;
            if (!MotionalSurroundMapping.IsValidControlChannel(value)) return;
            if (_channel == value) return;
            this.RaiseAndSetIfChanged(ref _channel, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _channelParam.ParSpec.Path, value);
        }
    }

    // --- Canvas coordinates (set by the parent when the stage is measured) ---
    public double CanvasX => MotionalSurroundMapping.ToNormalized(_lr,
        MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax) * _parent.StageWidth;
    public double CanvasY => MotionalSurroundMapping.ToNormalized(_fb,
        MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax) * _parent.StageHeight;

    public string PositionLabel
    {
        get
        {
            string lr = _lr == 0 ? "C" : _lr < 0 ? $"L{-_lr}" : $"R{_lr}";
            string fb = _fb == 0 ? "C" : _fb < 0 ? $"F{-_fb}" : $"B{_fb}";
            return $"{lr} / {fb}";
        }
    }

    public void RaiseCanvasChanged()
    {
        this.RaisePropertyChanged(nameof(CanvasX));
        this.RaisePropertyChanged(nameof(CanvasY));
    }

    /// <summary>Set position from the model side (no write enqueued). Used by presets before a direct write.</summary>
    public void SetPositionSuppressed(int lr, int fb)
    {
        _suppress = true;
        try { Lr = lr; Fb = fb; }
        finally { _suppress = false; }
    }

    public void SetWidthAmbienceSuppressed(int width, int ambience)
    {
        _suppress = true;
        try { Width = width; Ambience = ambience; }
        finally { _suppress = false; }
    }

    public async System.Threading.Tasks.Task WritePositionAsync()
    {
        await _domain.WriteToIntegraAsync(_lrParam.ParSpec.Path, _lr.ToString(CultureInfo.InvariantCulture));
        await _domain.WriteToIntegraAsync(_fbParam.ParSpec.Path, _fb.ToString(CultureInfo.InvariantCulture));
    }

    public async System.Threading.Tasks.Task WriteWidthAmbienceAsync()
    {
        await _domain.WriteToIntegraAsync(_widthParam.ParSpec.Path, _width.ToString(CultureInfo.InvariantCulture));
        await _domain.WriteToIntegraAsync(_ambParam.ParSpec.Path, _amb.ToString(CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Build to verify it compiles** (it references `MotionalSurroundViewModel`, created next — expect compile errors until Task 3; skip the build here and build at the end of Task 3).

- [ ] **Step 3: Commit (after Task 3 builds).** Combined commit at end of Task 3.

---

## Task 3: Orchestrator view model

**Files:**
- Create: `Src/ViewModels/MotionalSurroundViewModel.cs`

Owns: the common-domain global FQPs (as reactive props with suppress/echo handling), the 16 internal + 1 external `MotionalSurroundPartViewModel`s, `SelectedPart`, the per-key debounced write pipeline, stage size, and presets.

- [ ] **Step 1: Write the class**

`Src/ViewModels/MotionalSurroundViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Integra7AuralAlchemist.ViewModels;

public partial class MotionalSurroundViewModel : ViewModelBase, IDisposable
{
    private const string Prefix = "Studio Set Common Motional Surround/";

    private static readonly string[] RoomTypes = ["Room1", "Room2", "Hall1", "Hall2"];
    private static readonly string[] RoomSizes = ["Small", "Medium", "Large"];

    private readonly DomainBase _common;
    private readonly Dictionary<string, FullyQualifiedParameter> _commonByPath;

    private readonly Subject<MsWrite> _writes = new();
    private readonly IDisposable _writeSub;

    private bool _suppress;

    private sealed record MsWrite(string Key, Func<Task> WriteAsync);

    public ObservableCollection<MotionalSurroundPartViewModel> InternalParts { get; } = [];
    public MotionalSurroundPartViewModel ExternalPart { get; }
    public IReadOnlyList<MotionalSurroundPartViewModel> AllParts { get; }
    public string[] RoomTypeOptions => RoomTypes;
    public string[] RoomSizeOptions => RoomSizes;
    public string[] ChannelOptions { get; } =
        Enumerable.Range(1, 16).Select(i => i.ToString(CultureInfo.InvariantCulture)).Append("OFF").ToArray();

    public MotionalSurroundViewModel(Integra7Domain communicator)
    {
        _common = communicator.StudioSetCommonMotionalSurround;
        _commonByPath = _common.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path);

        // Per-key debounce: each key (a part position or a single parameter path) is throttled
        // independently, so a diagonal puck drag flushes BOTH axes (unlike the global ui2hw stream).
        _writeSub = _writes
            .GroupBy(w => w.Key)
            .SelectMany(g => g.Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE)))
            .Subscribe(async w =>
            {
                try { await w.WriteAsync(); }
                catch (Exception ex) { Log.Error(ex, "Motional Surround write failed for key {Key}", w.Key); }
            });

        // 16 internal parts, each from its own Studio Set Part domain.
        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            var d = communicator.StudioSetPart(i);
            var byPath = d.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path);
            var vm = new MotionalSurroundPartViewModel(this, d, i, false,
                byPath["Studio Set Part/Motional Surround L-R"],
                byPath["Studio Set Part/Motional Surround F-B"],
                byPath["Studio Set Part/Motional Surround Width"],
                byPath["Studio Set Part/Motional Surround Ambience Send Level"]);
            InternalParts.Add(vm);
        }

        ExternalPart = new MotionalSurroundPartViewModel(this, _common, -1, true,
            C("Ext Part L-R"), C("Ext Part F-B"), C("Ext Part Width"),
            C("Ext Part Ambience Send Level"), C("Ext Part Control Channel"));

        AllParts = InternalParts.Append(ExternalPart).ToList();
        _selectedPart = InternalParts[0];
        _selectedPart.IsSelected = true;

        InitGlobalsFromModel();
        SubscribeGlobals();
    }

    private FullyQualifiedParameter C(string shortName) => _commonByPath[Prefix + shortName];

    // ---- Write pipeline entry points (called by part VMs and global setters) ----
    public void EnqueuePositionWrite(MotionalSurroundPartViewModel part)
        => _writes.OnNext(new MsWrite($"pos:{part.Key}", part.WritePositionAsync));

    public void EnqueueValueWrite(DomainBase domain, string path, string displayValue)
        => _writes.OnNext(new MsWrite($"val:{path}", () => domain.WriteToIntegraAsync(path, displayValue)));

    private void EnqueueCommonWrite(string shortName, string displayValue)
        => EnqueueValueWrite(_common, Prefix + shortName, displayValue);

    // ---- Selection ----
    private MotionalSurroundPartViewModel _selectedPart;
    public MotionalSurroundPartViewModel SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (ReferenceEquals(_selectedPart, value) || value is null) return;
            _selectedPart.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedPart, value);
            _selectedPart.IsSelected = true;
        }
    }

    // ---- Stage size (pushed by the view; recompute every puck's canvas coords) ----
    private double _stageWidth = 1, _stageHeight = 1;
    public double StageWidth
    {
        get => _stageWidth;
        set { if (value > 0 && Math.Abs(_stageWidth - value) > 0.5) { _stageWidth = value; RaiseAllCanvas(); } }
    }
    public double StageHeight
    {
        get => _stageHeight;
        set { if (value > 0 && Math.Abs(_stageHeight - value) > 0.5) { _stageHeight = value; RaiseAllCanvas(); } }
    }
    private void RaiseAllCanvas() { foreach (var p in AllParts) p.RaiseCanvasChanged(); }

    // ---- Global reactive properties (bound to the common-domain FQPs) ----
    private bool _on;
    public bool MotionalSurroundOn
    {
        get => _on;
        set { if (_on == value) return; this.RaiseAndSetIfChanged(ref _on, value);
              if (!_suppress) EnqueueCommonWrite("Motional Surround Switch", value ? "ON" : "OFF"); }
    }

    private int _roomType;
    public int RoomTypeIndex
    {
        get => _roomType;
        set { value = MotionalSurroundMapping.Clamp(value, 0, RoomTypes.Length - 1);
              if (_roomType == value) return; this.RaiseAndSetIfChanged(ref _roomType, value);
              if (!_suppress) EnqueueCommonWrite("Room Type", RoomTypes[value]); }
    }

    private int _roomSize;
    public int RoomSizeIndex
    {
        get => _roomSize;
        set { value = MotionalSurroundMapping.Clamp(value, 0, RoomSizes.Length - 1);
              if (_roomSize == value) return; this.RaiseAndSetIfChanged(ref _roomSize, value);
              if (!_suppress) EnqueueCommonWrite("Room Size", RoomSizes[value]); }
    }

    private int _depth;
    public int Depth
    {
        get => _depth;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_depth == value) return; this.RaiseAndSetIfChanged(ref _depth, value);
              if (!_suppress) EnqueueCommonWrite("Motional Surround Depth", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambLevel;
    public int AmbienceLevel
    {
        get => _ambLevel;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 127);
              if (_ambLevel == value) return; this.RaiseAndSetIfChanged(ref _ambLevel, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Level", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambTime;
    public int AmbienceTime
    {
        get => _ambTime;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambTime == value) return; this.RaiseAndSetIfChanged(ref _ambTime, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Time", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambDensity;
    public int AmbienceDensity
    {
        get => _ambDensity;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambDensity == value) return; this.RaiseAndSetIfChanged(ref _ambDensity, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Density", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambHfDamp;
    public int AmbienceHfDamp
    {
        get => _ambHfDamp;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambHfDamp == value) return; this.RaiseAndSetIfChanged(ref _ambHfDamp, value);
              if (!_suppress) EnqueueCommonWrite("Ambience HF Damp", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private static int ParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void InitGlobalsFromModel()
    {
        _suppress = true;
        try
        {
            MotionalSurroundOn = C("Motional Surround Switch").StringValue == "ON";
            RoomTypeIndex = Math.Max(0, Array.IndexOf(RoomTypes, C("Room Type").StringValue));
            RoomSizeIndex = Math.Max(0, Array.IndexOf(RoomSizes, C("Room Size").StringValue));
            Depth = ParseInt(C("Motional Surround Depth").StringValue);
            AmbienceLevel = ParseInt(C("Ambience Level").StringValue);
            AmbienceTime = ParseInt(C("Ambience Time").StringValue);
            AmbienceDensity = ParseInt(C("Ambience Density").StringValue);
            AmbienceHfDamp = ParseInt(C("Ambience HF Damp").StringValue);
        }
        finally { _suppress = false; }
    }

    private void SubscribeGlobals()
    {
        void Sub(string shortName) => C(shortName).PropertyChanged += OnCommonChanged;
        Sub("Motional Surround Switch"); Sub("Room Type"); Sub("Room Size");
        Sub("Motional Surround Depth"); Sub("Ambience Level"); Sub("Ambience Time");
        Sub("Ambience Density"); Sub("Ambience HF Damp");
    }

    private void OnCommonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(InitGlobalsFromModel);
    }

    // ---- Presets (batch updates; suppress UI writes then write each changed value once) ----
    [ReactiveCommand]
    public async Task CenterAll()
    {
        foreach (var p in AllParts) { p.SetPositionSuppressed(0, 0); await p.WritePositionAsync(); }
    }

    [ReactiveCommand]
    public async Task WideStereoSpread()
    {
        // Spread internal parts evenly across L-R at center depth; external stays centered.
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            InternalParts[i].SetPositionSuppressed(lr, 0);
            await InternalParts[i].WritePositionAsync();
        }
    }

    [ReactiveCommand]
    public async Task FrontBandLayout()
    {
        // A row of parts near the front (F-B = -48), spread across L-R.
        const int front = -48;
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            InternalParts[i].SetPositionSuppressed(lr, front);
            await InternalParts[i].WritePositionAsync();
        }
    }

    [ReactiveCommand]
    public async Task AmbientHallLayout()
    {
        // Big, lush room + parts pushed slightly back with healthy ambience send.
        RoomTypeIndex = 3;            // Hall2
        RoomSizeIndex = 2;            // Large
        Depth = 80; AmbienceLevel = 100; AmbienceTime = 70; AmbienceDensity = 60; AmbienceHfDamp = 40;
        foreach (var p in InternalParts)
        {
            p.SetPositionSuppressed(p.Lr, 24);          // nudge back, keep L-R
            p.SetWidthAmbienceSuppressed(20, 90);
            await p.WritePositionAsync();
            await p.WriteWidthAmbienceAsync();
        }
    }

    [ReactiveCommand]
    public async Task ResetMotionalSurround()
    {
        // Opinionated neutral defaults (UI-level reset, not a factory dump).
        MotionalSurroundOn = true;
        RoomTypeIndex = 0; RoomSizeIndex = 1;
        Depth = 50; AmbienceLevel = 64; AmbienceTime = 50; AmbienceDensity = 50; AmbienceHfDamp = 50;
        foreach (var p in AllParts)
        {
            p.SetPositionSuppressed(0, 0);
            p.SetWidthAmbienceSuppressed(16, 0);
            await p.WritePositionAsync();
            await p.WriteWidthAmbienceAsync();
        }
        ExternalPart.Channel = "OFF";
    }

    public void Dispose()
    {
        _writeSub.Dispose();
        _writes.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify VM + part VM compile**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj -c Debug`
Expected: BUILD SUCCEEDED (no view yet; the VM and part VM compile together).

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/MotionalSurroundPartViewModel.cs Src/ViewModels/MotionalSurroundViewModel.cs
git commit -m "Add Motional Surround view models (per-part + orchestrator, debounced writes, presets)"
```

---

## Task 4: The view (layout + interactive room map)

**Files:**
- Create: `Src/Views/MotionalSurroundView.axaml`
- Create: `Src/Views/MotionalSurroundView.axaml.cs`

Layout = Option A: top ribbon (globals) · row of [parts list · room map · detail]. The room map is a `Canvas` inside a `Border`; pucks are an `ItemsControl` over a `Canvas` panel with `Canvas.Left`/`Top` bound to each part's `CanvasX`/`CanvasY` (offset by puck radius in code-behind). Drag + keyboard handled in code-behind.

- [ ] **Step 1: Write the XAML**

`Src/Views/MotionalSurroundView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             mc:Ignorable="d" d:DesignWidth="1200" d:DesignHeight="760"
             x:DataType="vm:MotionalSurroundViewModel"
             x:Class="Integra7AuralAlchemist.Views.MotionalSurroundView">
  <UserControl.Styles>
    <Style Selector="TextBlock.help">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Foreground" Value="#9aa0a3"/>
      <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style Selector="TextBlock.colhead">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Foreground" Value="#8a9092"/>
      <Setter Property="Margin" Value="0,0,0,4"/>
    </Style>
  </UserControl.Styles>

  <Grid RowDefinitions="Auto,*,Auto" Margin="10">
    <!-- GLOBAL RIBBON -->
    <Border Grid.Row="0" Background="#34383a" CornerRadius="6" Padding="10" Margin="0,0,0,8">
      <StackPanel Orientation="Vertical" Spacing="6">
        <StackPanel Orientation="Horizontal" Spacing="18" VerticalAlignment="Center">
          <ToggleSwitch IsChecked="{Binding MotionalSurroundOn}" OnContent="Motional Surround ON" OffContent="Motional Surround OFF"/>
          <StackPanel Orientation="Vertical">
            <TextBlock Text="Room Type"/>
            <ListBox ItemsSource="{Binding RoomTypeOptions}" SelectedIndex="{Binding RoomTypeIndex}"
                     SelectionMode="Single">
              <ListBox.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate></ListBox.ItemsPanel>
            </ListBox>
          </StackPanel>
          <StackPanel Orientation="Vertical">
            <TextBlock Text="Room Size"/>
            <ListBox ItemsSource="{Binding RoomSizeOptions}" SelectedIndex="{Binding RoomSizeIndex}"
                     SelectionMode="Single">
              <ListBox.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate></ListBox.ItemsPanel>
            </ListBox>
          </StackPanel>
          <StackPanel Orientation="Vertical" Width="220">
            <TextBlock Text="Depth"/>
            <StackPanel Orientation="Horizontal" Spacing="6">
              <Slider Minimum="0" Maximum="100" Width="150" Value="{Binding Depth}"/>
              <NumericUpDown Minimum="0" Maximum="100" Increment="1" Width="90" Value="{Binding Depth}"/>
            </StackPanel>
            <TextBlock Classes="help" Text="How strongly parts are placed in the surround field."/>
          </StackPanel>
        </StackPanel>

        <WrapPanel Orientation="Horizontal">
          <StackPanel Orientation="Vertical" Width="240" Margin="0,0,16,0">
            <TextBlock Text="Ambience Level"/>
            <StackPanel Orientation="Horizontal" Spacing="6">
              <Slider Minimum="0" Maximum="127" Width="150" Value="{Binding AmbienceLevel}"/>
              <NumericUpDown Minimum="0" Maximum="127" Increment="1" Width="90" Value="{Binding AmbienceLevel}"/>
            </StackPanel>
            <TextBlock Classes="help" Text="Overall room/reverb presence."/>
          </StackPanel>
          <StackPanel Orientation="Vertical" Width="220" Margin="0,0,16,0">
            <TextBlock Text="Ambience Time"/>
            <StackPanel Orientation="Horizontal" Spacing="6">
              <Slider Minimum="0" Maximum="100" Width="130" Value="{Binding AmbienceTime}"/>
              <NumericUpDown Minimum="0" Maximum="100" Increment="1" Width="90" Value="{Binding AmbienceTime}"/>
            </StackPanel>
          </StackPanel>
          <StackPanel Orientation="Vertical" Width="220" Margin="0,0,16,0">
            <TextBlock Text="Ambience Density"/>
            <StackPanel Orientation="Horizontal" Spacing="6">
              <Slider Minimum="0" Maximum="100" Width="130" Value="{Binding AmbienceDensity}"/>
              <NumericUpDown Minimum="0" Maximum="100" Increment="1" Width="90" Value="{Binding AmbienceDensity}"/>
            </StackPanel>
          </StackPanel>
          <StackPanel Orientation="Vertical" Width="240">
            <TextBlock Text="Ambience HF Damp"/>
            <StackPanel Orientation="Horizontal" Spacing="6">
              <Slider Minimum="0" Maximum="100" Width="150" Value="{Binding AmbienceHfDamp}"/>
              <NumericUpDown Minimum="0" Maximum="100" Increment="1" Width="90" Value="{Binding AmbienceHfDamp}"/>
            </StackPanel>
            <TextBlock Classes="help" Text="Higher values darken the ambience."/>
          </StackPanel>
        </WrapPanel>
      </StackPanel>
    </Border>

    <!-- MAIN ROW: parts list | room map | detail -->
    <Grid Grid.Row="1" ColumnDefinitions="220,*,300" >
      <!-- PARTS OVERVIEW -->
      <Border Grid.Column="0" Background="#2f3335" CornerRadius="6" Padding="6" Margin="0,0,8,0">
        <DockPanel>
          <TextBlock DockPanel.Dock="Top" Classes="colhead" Text="PARTS"/>
          <ListBox ItemsSource="{Binding AllParts}" SelectedItem="{Binding SelectedPart}">
            <ListBox.ItemTemplate>
              <DataTemplate x:DataType="vm:MotionalSurroundPartViewModel">
                <Grid ColumnDefinitions="40,*,Auto">
                  <TextBlock Grid.Column="0" Text="{Binding Label}" FontWeight="SemiBold"/>
                  <TextBlock Grid.Column="1" Text="{Binding PositionLabel}" Foreground="#cfd3d5"/>
                  <TextBlock Grid.Column="2" Foreground="#8a9092"
                             Text="{Binding Ambience, StringFormat='A{0}'}"/>
                </Grid>
              </DataTemplate>
            </ListBox.ItemTemplate>
          </ListBox>
        </DockPanel>
      </Border>

      <!-- ROOM MAP -->
      <Border Grid.Column="1" Background="#2b2f31" BorderBrush="#5a6063" BorderThickness="1" CornerRadius="8"
              Margin="0,0,8,0" ClipToBounds="True">
        <Grid>
          <!-- axis guide lines + labels -->
          <Canvas>
            <Line StartPoint="0,0" EndPoint="0,0" Name="CrossH" Stroke="#44494b" StrokeDashArray="4,4"/>
            <Line StartPoint="0,0" EndPoint="0,0" Name="CrossV" Stroke="#44494b" StrokeDashArray="4,4"/>
          </Canvas>
          <TextBlock Text="FRONT" Classes="help" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,4"/>
          <TextBlock Text="BACK" Classes="help" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,4"/>
          <TextBlock Text="L" Classes="help" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="4,0"/>
          <TextBlock Text="R" Classes="help" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="4,0"/>

          <!-- pucks -->
          <ItemsControl ItemsSource="{Binding AllParts}" Name="PuckHost">
            <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:MotionalSurroundPartViewModel">
                <Border Width="28" Height="28" CornerRadius="14" Focusable="True"
                        Background="{Binding IsExternal, Converter={x:Static vm:MsConverters.ExternalBrush}}"
                        BorderBrush="White"
                        BorderThickness="{Binding IsSelected, Converter={x:Static vm:MsConverters.SelectedThickness}}"
                        Tag="{Binding}">
                  <TextBlock Text="{Binding Label}" Foreground="White" FontSize="10"
                             HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </Grid>
      </Border>

      <!-- DETAIL -->
      <Border Grid.Column="2" Background="#34383a" CornerRadius="6" Padding="10">
        <StackPanel Orientation="Vertical" Spacing="8" DataContext="{Binding SelectedPart}"
                    x:DataType="vm:MotionalSurroundPartViewModel">
          <TextBlock Text="{Binding Label, StringFormat='Part {0}'}" FontWeight="Bold" FontSize="16"/>
          <TextBlock Text="L–R  (Left · Center · Right)"/>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <Slider Minimum="-64" Maximum="63" Width="180" Value="{Binding Lr}"/>
            <NumericUpDown Minimum="-64" Maximum="63" Increment="1" Width="100" Value="{Binding Lr}"/>
          </StackPanel>
          <TextBlock Text="F–B  (Front · Center · Back)"/>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <Slider Minimum="-64" Maximum="63" Width="180" Value="{Binding Fb}"/>
            <NumericUpDown Minimum="-64" Maximum="63" Increment="1" Width="100" Value="{Binding Fb}"/>
          </StackPanel>
          <TextBlock Text="Width  (Narrow → Wide)"/>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <Slider Minimum="0" Maximum="32" Width="180" Value="{Binding Width}"/>
            <NumericUpDown Minimum="0" Maximum="32" Increment="1" Width="100" Value="{Binding Width}"/>
          </StackPanel>
          <TextBlock Text="Ambience Send  (Dry → Ambient)"/>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <Slider Minimum="0" Maximum="127" Width="180" Value="{Binding Ambience}"/>
            <NumericUpDown Minimum="0" Maximum="127" Increment="1" Width="100" Value="{Binding Ambience}"/>
          </StackPanel>
          <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{Binding IsExternal}">
            <TextBlock Text="Control Channel" VerticalAlignment="Center"/>
            <ComboBox ItemsSource="{Binding $parent[ItemsControl].DataContext}" /> <!-- replaced in code-behind binding below -->
          </StackPanel>
          <Button Content="Center position"
                  Command="{Binding $parent[UserControl].((vm:MotionalSurroundViewModel)DataContext).CenterSelected}"/>
        </StackPanel>
      </Border>
    </Grid>

    <!-- PRESETS -->
    <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
      <TextBlock Text="Presets:" VerticalAlignment="Center"/>
      <Button Content="Center all" Command="{Binding CenterAll}"/>
      <Button Content="Wide stereo spread" Command="{Binding WideStereoSpread}"/>
      <Button Content="Front band layout" Command="{Binding FrontBandLayout}"/>
      <Button Content="Ambient hall layout" Command="{Binding AmbientHallLayout}"/>
      <Button Content="Reset Motional Surround" Command="{Binding ResetMotionalSurround}"/>
    </StackPanel>
  </Grid>
</UserControl>
```

> Note: two bindings above are placeholders that the code-behind finalizes — the external Control Channel `ComboBox` (bind `ItemsSource` to `ChannelOptions`, `SelectedItem` to `Channel`) and the "Center position" button. To keep the XAML clean, **replace** the Control Channel `ComboBox` line with:
> ```xml
> <ComboBox ItemsSource="{Binding $parent[UserControl].((vm:MotionalSurroundViewModel)DataContext).ChannelOptions}"
>           SelectedItem="{Binding Channel}"/>
> ```
> and **replace** the Center button `Command` with a code-behind click handler `CenterSelected_Click` (simpler than a compiled-binding path):
> ```xml
> <Button Content="Center position" Click="CenterSelected_Click"/>
> ```

- [ ] **Step 2: Add the value converters used by the puck template**

Append to `Src/ViewModels/MotionalSurroundViewModel.cs` (same namespace) a small static converter holder:
```csharp
public static class MsConverters
{
    public static readonly Avalonia.Data.Converters.IValueConverter ExternalBrush =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(
            isExt => new Avalonia.Media.SolidColorBrush(
                isExt ? Avalonia.Media.Color.Parse("#7a4fb0") : Avalonia.Media.Color.Parse("#3d7eaa")));

    public static readonly Avalonia.Data.Converters.IValueConverter SelectedThickness =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Thickness>(
            sel => sel ? new Avalonia.Thickness(3) : new Avalonia.Thickness(0));
}
```

- [ ] **Step 3: Write the code-behind (drag, keyboard, positioning, stage size)**

`Src/Views/MotionalSurroundView.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class MotionalSurroundView : UserControl
{
    private const double PuckRadius = 14;
    private Canvas? _puckCanvas;
    private Border? _dragging;
    private MotionalSurroundPartViewModel? _dragVm;

    public MotionalSurroundView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MotionalSurroundViewModel? Vm => DataContext as MotionalSurroundViewModel;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
    }

    // The room-map Border hosts a Grid; find the pucks' Canvas and wire size + pointer handlers.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var host = this.FindControl<ItemsControl>("PuckHost");
        _puckCanvas = host?.GetVisualDescendants() is { } ? FindCanvas(host) : null;
        if (host != null)
        {
            host.AddHandler(PointerPressedEvent, OnPuckPointerPressed, RoutingStrategies.Tunnel);
            host.AddHandler(PointerMovedEvent, OnPuckPointerMoved, RoutingStrategies.Tunnel);
            host.AddHandler(PointerReleasedEvent, OnPuckPointerReleased, RoutingStrategies.Tunnel);
            host.AddHandler(KeyDownEvent, OnPuckKeyDown, RoutingStrategies.Bubble);
            host.LayoutUpdated += (_, _) => UpdateStageAndPucks(host);
        }
    }

    private static Canvas? FindCanvas(Control root)
    {
        foreach (var d in root.GetVisualDescendants())
            if (d is Canvas c) return c;
        return null;
    }

    private void UpdateStageAndPucks(ItemsControl host)
    {
        if (Vm is null) return;
        var w = host.Bounds.Width;
        var h = host.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Reserve a puck-radius margin so pucks stay fully inside the map.
        Vm.StageWidth = w - 2 * PuckRadius;
        Vm.StageHeight = h - 2 * PuckRadius;
        foreach (var item in host.GetRealizedContainers())
            if (item is ContentPresenter cp && cp.Child is Border b && b.DataContext is MotionalSurroundPartViewModel p)
            {
                Canvas.SetLeft(b, p.CanvasX);
                Canvas.SetTop(b, p.CanvasY);
            }
    }

    private void OnPuckPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual v && FindPuck(v) is { } b && b.DataContext is MotionalSurroundPartViewModel p)
        {
            _dragging = b; _dragVm = p;
            if (Vm != null) Vm.SelectedPart = p;
            e.Pointer.Capture(b);
            b.Focus();
        }
    }

    private void OnPuckPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || _dragVm is null || Vm is null) return;
        var host = (Control)sender!;
        var pos = e.GetPosition(host);
        var nx = (pos.X - PuckRadius) / Vm.StageWidth;
        var ny = (pos.Y - PuckRadius) / Vm.StageHeight;
        // L-R: left->right ; F-B: top(Front,-64)->bottom(Back,+63)
        _dragVm.Lr = MotionalSurroundMapping.FromNormalized(nx,
            MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
        _dragVm.Fb = MotionalSurroundMapping.FromNormalized(ny,
            MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
    }

    private void OnPuckPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null; _dragVm = null;
        e.Pointer.Capture(null);
    }

    private void OnPuckKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Visual v && FindPuck(v) is { } b && b.DataContext is MotionalSurroundPartViewModel p)
        {
            switch (e.Key)
            {
                case Key.Left:  p.Lr -= 1; e.Handled = true; break;
                case Key.Right: p.Lr += 1; e.Handled = true; break;
                case Key.Up:    p.Fb -= 1; e.Handled = true; break;   // up = toward Front (-)
                case Key.Down:  p.Fb += 1; e.Handled = true; break;
            }
        }
    }

    private static Border? FindPuck(Visual from)
    {
        var cur = from;
        while (cur != null)
        {
            if (cur is Border b && b.DataContext is MotionalSurroundPartViewModel) return b;
            cur = cur.GetVisualParent();
        }
        return null;
    }

    private void CenterSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is { } p) { p.Lr = 0; p.Fb = 0; }
    }
}
```

> The exact realized-container access (`GetRealizedContainers`, `ContentPresenter.Child`) may need a small tweak to match Avalonia 12's `ItemsControl` realization API — if `GetRealizedContainers()` returns the `Border` directly (no `ContentPresenter`), position the `Control` itself. Verify during the build/run step and adjust the cast accordingly.

- [ ] **Step 4: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj -c Debug`
Expected: BUILD SUCCEEDED. Fix any Avalonia-12 API mismatches surfaced (binding paths, realized-container API) until it compiles.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/MotionalSurroundView.axaml Src/Views/MotionalSurroundView.axaml.cs Src/ViewModels/MotionalSurroundViewModel.cs
git commit -m "Add MotionalSurroundView: spatial room map, detail panel, presets"
```

---

## Task 5: Wire into the app

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/Views/MainWindow.axaml`

- [ ] **Step 1: Expose `MotionalSurroundVm` on `MainWindowViewModel`**

In `Src/ViewModels/MainWindowViewModel.cs`, add the field/property near the other reactive state (after `PartViewModels`):
```csharp
[Reactive] private MotionalSurroundViewModel? _motionalSurroundVm;
```
(The `[Reactive]` source generator creates the `MotionalSurroundVm` property.)

- [ ] **Step 2: Construct it after the parts loop**

In `UpdateConnectedAsync`, immediately after
```csharp
                PartViewModels = new ReadOnlyObservableCollection<PartViewModel>(pvm);
                this.RaisePropertyChanged(nameof(PartViewModels));
```
add:
```csharp
                // All Studio Set Part + common Motional Surround domains have now been read,
                // so the editor can bind to live values.
                MotionalSurroundVm?.Dispose();
                MotionalSurroundVm = new MotionalSurroundViewModel(_integra7Communicator);
```
And in the `else` branch (failed connect) and at the start of `RescanMidiDevicesAsync`, clear it so a stale editor isn't shown:
```csharp
                MotionalSurroundVm?.Dispose();
                MotionalSurroundVm = null;
```
(Place the clearing line in the `else` branch of `UpdateConnectedAsync` where `MidiDevices = "Could not find ..."` is set.)

- [ ] **Step 3: Add the top-level tab + placeholder**

In `Src/Views/MainWindow.axaml`, inside the outer `<TabControl Grid.Column="0" Grid.Row="1">`, after the `</TabItem>` for "SRX Loader" (before `</TabControl>`), add:
```xml
            <TabItem Header="Motional Surround">
                <Panel>
                    <local:MotionalSurroundView DataContext="{Binding MotionalSurroundVm}"
                                                IsVisible="{Binding MotionalSurroundVm, Converter={x:Static ObjectConverters.IsNotNull}}"/>
                    <TextBlock Text="Connect to your Integra-7 to edit Motional Surround."
                               HorizontalAlignment="Center" VerticalAlignment="Center"
                               IsVisible="{Binding MotionalSurroundVm, Converter={x:Static ObjectConverters.IsNull}}"/>
                </Panel>
            </TabItem>
```

- [ ] **Step 4: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj -c Debug`
Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/Views/MainWindow.axaml
git commit -m "Wire Motional Surround editor into a top-level tab"
```

---

## Task 6: Full build, tests, and verification

- [ ] **Step 1: Run the whole test suite**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -c Debug`
Expected: all tests pass (including the new mapping tests).

- [ ] **Step 2: Build Release to confirm no Debug-only breakage**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj -c Release`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Manual verification (with hardware if available)**

Checklist (note any that can't be verified without hardware):
- Tab "Motional Surround" appears; shows placeholder before connect, editor after.
- Toggle ON/OFF, pick Room Type/Size, move global sliders → values hold and (with hw) sound changes.
- Drag a puck diagonally → both L-R and F-B update in list + detail; the value is sent once on pause (not flooded).
- Select a part via list and via puck → highlight syncs in both.
- Per-part Width / Ambience sliders + numeric boxes work; external part shows Control Channel (1–16/OFF).
- Keyboard: focus a puck, arrow keys nudge; numeric boxes accept typed values.
- Presets: Center all / Wide / Front band / Ambient hall / Reset behave as described.
- Change a Motional Surround value on the hardware (or via the raw "Motional Surround" tab) → the puck/sliders here update.

- [ ] **Step 4: Final commit (if any fixes were needed)**

```bash
git add -A
git commit -m "Motional Surround editor: verification fixes"
```

---

## Self-Review (spec coverage)

- 2D stage, draggable pucks, external distinct → Task 4 (puck template + drag).
- Drag updates L-R/F-B; selecting shows detail → Tasks 2/4.
- Global controls prominent (toggle, room cards, sliders, helper text) → Task 4 ribbon.
- Per-part detail (labeled sliders, numeric, reset center) → Task 4 detail + `CenterSelected_Click`.
- Overview list, click-to-select, highlight both places → Task 4 list + `IsSelected`/`SelectedPart`.
- Presets via existing param API → Task 3 commands.
- Value mapping correct + clamped, no off-by-one → Task 1 (+ tests).
- Reuses existing SysEx/param infra → domains + `WriteToIntegraAsync`; no new SysEx.
- Throttling on drag → Task 3 per-key debounce.
- State sync from hardware → FQP `PropertyChanged` subscriptions (Tasks 2/3).
- Accessibility/keyboard/numeric → Task 4 (focusable pucks, arrow keys, `NumericUpDown`).
- Validation/clamping/channel → Tasks 1/2/3 setters.
- Tests added → Task 1.
