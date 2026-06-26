# SuperNATURAL Synth Editor — Phase 1 (Vertical Slice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable, friendly SuperNATURAL Synth (SN-S) editor for the Integra-7: Tone Header + 3-card Partial Rack + a selected partial's Oscillator/Amp controls + a draggable graphical Amp Envelope, all live-synced and throttled, integrated as the default tab for SN-S tones.

**Architecture:** Mirror the proven **Motional Surround** editor. New code binds to the *shared live* `FullyQualifiedParameter` (FQP) instances returned by `Integra7Domain.SNSynthTone*(part[,partial])`, so hardware/preset echoes sync for free. User edits go through a per-key throttled write pipeline (`ThrottledParameterWriter`) calling `DomainBase.WriteToIntegraAsync(path, displayValue)`. The envelope is a custom Avalonia `Control` with a `Render` override; all value↔geometry math is in a pure, unit-tested mapping class.

**Tech Stack:** Avalonia 12, ReactiveUI (+ SourceGenerators `[Reactive]`), System.Reactive, NUnit 3. Build with the user-local SDK: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`.

**Spec:** `docs/superpowers/specs/2026-06-26-supernatural-synth-editor-design.md`. **Branch:** `sns-editor` (already created).

**Key facts the engineer must know (verified against source):**
- Lookup **path = `ParSpec.Path`** = `<prefix>/<name>` (e.g. `SuperNATURAL Synth Tone Partial/OSC Wave`). Part/partial context comes from *which domain instance* you call, not the path. Build dicts via `domain.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path)`.
- Domain accessors (zero-based): `Integra7Domain.SNSynthToneCommon(part)`, `SNSynthToneCommonMFX(part)`, `SNSynthTonePartial(part, partial)` — all return the cached, live `DomainBase`.
- Write one value: `await domain.WriteToIntegraAsync(path, displayValue)`. `displayValue` is the display-space string (e.g. `"-63"`, `"Saw"`, `"ON"`).
- Inbound sync: subscribe to `FullyQualifiedParameter.StringValue` `PropertyChanged`; marshal to UI with `Dispatcher.UIThread.Post`. Guard model→VM writes with a `_suppress` flag.
- Enum option lists: `p.ParSpec.Repr` is `IDictionary<int,string>?` (public). `Repr.OrderBy(kv => kv.Key).Select(kv => kv.Value)`.
- `Constants.THROTTLE` = 250 (global namespace, no `using` needed).
- Test framework: NUnit 3, namespace `Tests`, global `using NUnit.Framework;`, `[Test]`/`[TestCase]`, `Assert.That(actual, Is.EqualTo(expected))`. Run: `dotnet test`.

**Exact SN-S parameter paths used (prefix + name):**
- Partial (`SuperNATURAL Synth Tone Partial/…`): `OSC Wave`, `OSC Wave Variation`, `OSC Pitch`, `OSC Detune`, `OSC Pulse Width`, `OSC Pulse Width shift`, `OSC Pulse Width Mod Depth`, `Super Saw Detune`, `Wave Gain`, `Wave Number`, `AMP Level`, `AMP Pan`, `AMP Level Velocity Sens`, `AMP Level Keyfollow`, `AMP Env Attack Time`, `AMP Env Decay Time`, `AMP Env Sustain Level`, `AMP Env Release Time`.
- Common (`SuperNATURAL Synth Tone Common/…`): `Tone Name`, `Tone Level`, `Mono Switch`, `Legato Switch`, `Portamento Switch`, `Portamento Time`, `Unison Switch`, `Unison Size`, `Analog Feel`, `Ring Switch`, `Partial1 Switch`, `Partial2 Switch`, `Partial3 Switch`.

**Display ranges:** `OSC Pitch` −24..24; `OSC Detune` −50..50; `AMP Pan` −64..63; `*Velocity Sens` −63..63; `AMP Level Keyfollow` −100..100; everything else listed as a time/level is 0..127. Enums send the exact string.

---

## Task 1: `SnsEnvelopeMapping` (pure value↔geometry mapping)

**Files:**
- Create: `Src/Models/Services/SnsEnvelopeMapping.cs`
- Test: `Tests/TestSnsEnvelopeMapping.cs`

- [ ] **Step 1: Write the failing test**

`Tests/TestSnsEnvelopeMapping.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsEnvelopeMappingTests
{
    [TestCase(-5, 0)]
    [TestCase(200, 127)]
    [TestCase(64, 64)]
    public void Clamp_keeps_in_range(int v, int expected)
        => Assert.That(SnsEnvelopeMapping.Clamp(v), Is.EqualTo(expected));

    [TestCase(0, 0.0)]
    [TestCase(127, 100.0)]
    [TestCase(64, 50.39370078740157)]
    public void TimeToWidth_scales_value_over_segment(int value, double expected)
        => Assert.That(SnsEnvelopeMapping.TimeToWidth(value, 100.0), Is.EqualTo(expected).Within(1e-9));

    [TestCase(0)]
    [TestCase(40)]
    [TestCase(127)]
    public void Time_width_roundtrip(int value)
    {
        var w = SnsEnvelopeMapping.TimeToWidth(value, 100.0);
        Assert.That(SnsEnvelopeMapping.TimeFromWidth(w, 100.0), Is.EqualTo(value));
    }

    [TestCase(0, 200.0, 200.0)]   // level 0 -> bottom (y == height)
    [TestCase(127, 200.0, 0.0)]   // level 127 -> top
    public void LevelToY_maps_level_to_pixel(int level, double height, double expectedY)
        => Assert.That(SnsEnvelopeMapping.LevelToY(level, height), Is.EqualTo(expectedY).Within(1e-9));

    [TestCase(0)]
    [TestCase(63)]
    [TestCase(127)]
    public void Level_y_roundtrip(int level)
    {
        var y = SnsEnvelopeMapping.LevelToY(level, 200.0);
        Assert.That(SnsEnvelopeMapping.LevelFromY(y, 200.0), Is.EqualTo(level));
    }

    [Test]
    public void ComputePoints_places_the_five_breakpoints()
    {
        // width 340, sustainWidth 40 -> segMax = (340-40)/3 = 100
        var p = SnsEnvelopeMapping.ComputePoints(127, 0, 127, 0, 340, 200, 40);
        Assert.That(p.Start.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.Start.Y, Is.EqualTo(200).Within(1e-9));
        Assert.That(p.Peak.X, Is.EqualTo(100).Within(1e-9));   // full attack
        Assert.That(p.Peak.Y, Is.EqualTo(0).Within(1e-9));
        Assert.That(p.SustainStart.X, Is.EqualTo(100).Within(1e-9)); // decay 0
        Assert.That(p.SustainStart.Y, Is.EqualTo(0).Within(1e-9));   // sustain 127 -> top
        Assert.That(p.SustainEnd.X, Is.EqualTo(140).Within(1e-9));   // + sustainWidth 40
        Assert.That(p.End.X, Is.EqualTo(140).Within(1e-9));          // release 0
        Assert.That(p.End.Y, Is.EqualTo(200).Within(1e-9));
    }

    [Test]
    public void DecayFromX_subtracts_attack_offset()
    {
        // segMax 100, attackWidth 100, pointer x 150 -> decay width 50 -> ~64
        Assert.That(SnsEnvelopeMapping.DecayFromX(150, 100, 100), Is.EqualTo(64));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsEnvelopeMappingTests`
Expected: FAIL — `SnsEnvelopeMapping` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`Src/Models/Services/SnsEnvelopeMapping.cs`:
```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure value↔geometry mapping for the ADSR envelope graph (no Avalonia dependency, so it is
/// unit-testable). The graph reserves a fixed-width sustain plateau; the three time segments
/// (attack, decay, release) share the remaining width equally as their max extent at value 127.
/// </summary>
public static class SnsEnvelopeMapping
{
    public const int Min = 0;
    public const int Max = 127;

    public readonly record struct Point(double X, double Y);

    public readonly record struct EnvPoints(
        Point Start, Point Peak, Point SustainStart, Point SustainEnd, Point End);

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    /// <summary>Max pixel width of one time segment at full (127) value.</summary>
    public static double SegmentMax(double width, double sustainWidth)
    {
        var usable = width - sustainWidth;
        return usable < 0 ? 0 : usable / 3.0;
    }

    public static double TimeToWidth(int value, double segMax) => Clamp(value) / 127.0 * segMax;

    public static int TimeFromWidth(double w, double segMax)
    {
        if (segMax <= 0) return Min;
        return Clamp((int)Math.Round(w / segMax * 127.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Y pixel for a 0..127 level (0 at the bottom = height, 127 at the top = 0).</summary>
    public static double LevelToY(int level, double height) => height - Clamp(level) / 127.0 * height;

    public static int LevelFromY(double y, double height)
    {
        if (height <= 0) return Min;
        var lvl = (1.0 - y / height) * 127.0;
        return Clamp((int)Math.Round(lvl, MidpointRounding.AwayFromZero));
    }

    public static EnvPoints ComputePoints(int a, int d, int s, int r,
        double width, double height, double sustainWidth)
    {
        var seg = SegmentMax(width, sustainWidth);
        var aW = TimeToWidth(a, seg);
        var dW = TimeToWidth(d, seg);
        var rW = TimeToWidth(r, seg);
        var sy = LevelToY(s, height);
        var x1 = aW;
        var x2 = aW + dW;
        var x3 = aW + dW + sustainWidth;
        var x4 = aW + dW + sustainWidth + rW;
        return new EnvPoints(
            new Point(0, height),
            new Point(x1, 0),
            new Point(x2, sy),
            new Point(x3, sy),
            new Point(x4, height));
    }

    public static int AttackFromX(double x, double segMax) => TimeFromWidth(x, segMax);

    public static int DecayFromX(double x, double attackWidth, double segMax)
        => TimeFromWidth(x - attackWidth, segMax);

    public static int ReleaseFromX(double x, double attackWidth, double decayWidth, double sustainWidth, double segMax)
        => TimeFromWidth(x - attackWidth - decayWidth - sustainWidth, segMax);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsEnvelopeMappingTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnsEnvelopeMapping.cs Tests/TestSnsEnvelopeMapping.cs
git commit -m "feat(sns): pure ADSR envelope value<->geometry mapping with tests"
```

---

## Task 2: `ThrottledParameterWriter` (per-key throttled write pipeline)

**Files:**
- Create: `Src/Models/Services/ThrottledParameterWriter.cs`
- Modify: `Tests/Tests.csproj` (add `Microsoft.Reactive.Testing`)
- Test: `Tests/TestThrottledParameterWriter.cs`

- [ ] **Step 1: Add the test scheduler package**

In `Tests/Tests.csproj`, inside the existing `<ItemGroup>` that has the NUnit `PackageReference`s, add:
```xml
        <PackageReference Include="Microsoft.Reactive.Testing" Version="6.0.1"/>
```

- [ ] **Step 2: Write the failing test**

`Tests/TestThrottledParameterWriter.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using Microsoft.Reactive.Testing;

namespace Tests;

public class ThrottledParameterWriterTests
{
    [Test]
    public void Same_key_collapses_to_last_value()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<int>();
        w.Enqueue("k", () => { sent.Add(1); return Task.CompletedTask; });
        w.Enqueue("k", () => { sent.Add(2); return Task.CompletedTask; });
        w.Enqueue("k", () => { sent.Add(3); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(251).Ticks);
        Assert.That(sent, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Different_keys_are_independent()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<string>();
        w.Enqueue("a", () => { sent.Add("a"); return Task.CompletedTask; });
        w.Enqueue("b", () => { sent.Add("b"); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(251).Ticks);
        Assert.That(sent, Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void Nothing_sent_before_the_throttle_window()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<int>();
        w.Enqueue("k", () => { sent.Add(1); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.That(sent, Is.Empty);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter ThrottledParameterWriterTests`
Expected: FAIL — `ThrottledParameterWriter` does not exist.

- [ ] **Step 4: Write minimal implementation**

`Src/Models/Services/ThrottledParameterWriter.cs`:
```csharp
using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Per-key throttled write pipeline. Rapid writes sharing a key collapse to the last value
/// (THROTTLE-ms window); different keys are independent, so e.g. two envelope handles dragged
/// together both flush. Mirrors the Motional Surround editor's local write Subject.
/// </summary>
public sealed class ThrottledParameterWriter : IDisposable
{
    private sealed record Req(string Key, Func<Task> WriteAsync);

    private readonly Subject<Req> _subject = new();
    private readonly IDisposable _sub;

    public ThrottledParameterWriter(int throttleMs = Constants.THROTTLE, IScheduler? scheduler = null)
    {
        scheduler ??= Scheduler.Default;
        _sub = _subject
            .GroupBy(r => r.Key)
            .SelectMany(g => g.Throttle(TimeSpan.FromMilliseconds(throttleMs), scheduler))
            .Subscribe(async r =>
            {
                try { await r.WriteAsync(); }
                catch (Exception ex) { Log.Error(ex, "Throttled write failed for {Key}", r.Key); }
            });
    }

    public void Enqueue(string key, Func<Task> writeAsync) => _subject.OnNext(new Req(key, writeAsync));

    public void Dispose()
    {
        _sub.Dispose();
        _subject.Dispose();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter ThrottledParameterWriterTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/ThrottledParameterWriter.cs Tests/TestThrottledParameterWriter.cs Tests/Tests.csproj
git commit -m "feat(sns): per-key throttled parameter writer with TestScheduler tests"
```

---

## Task 3: `SnsOscillatorRules` (context-aware control visibility)

**Files:**
- Create: `Src/Models/Services/SnsOscillatorRules.cs`
- Test: `Tests/TestSnsOscillatorRules.cs`

- [ ] **Step 1: Write the failing test**

`Tests/TestSnsOscillatorRules.cs`:
```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsOscillatorRulesTests
{
    [TestCase("Pulse Width Mod. Square", true)]
    [TestCase("Saw", false)]
    [TestCase("SuperSaw", false)]
    public void Pulse_width_controls_only_for_pwm_square(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsPulseWidth(wave), Is.EqualTo(expected));

    [TestCase("SuperSaw", true)]
    [TestCase("Saw", false)]
    public void Super_saw_detune_only_for_super_saw(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsSuperSawDetune(wave), Is.EqualTo(expected));

    [TestCase("Pcm", true)]
    [TestCase("Sine", false)]
    public void Pcm_controls_only_for_pcm(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsPcm(wave), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsOscillatorRulesTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write minimal implementation**

`Src/Models/Services/SnsOscillatorRules.cs`:
```csharp
namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which oscillator sub-controls are relevant for a given OSC Wave (display string).</summary>
public static class SnsOscillatorRules
{
    public const string WaveSaw = "Saw";
    public const string WavePulseSquare = "Pulse Width Mod. Square";
    public const string WaveSuperSaw = "SuperSaw";
    public const string WavePcm = "Pcm";

    public static bool ShowsPulseWidth(string wave) => wave == WavePulseSquare;
    public static bool ShowsSuperSawDetune(string wave) => wave == WaveSuperSaw;
    public static bool ShowsPcm(string wave) => wave == WavePcm;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsOscillatorRulesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnsOscillatorRules.cs Tests/TestSnsOscillatorRules.cs
git commit -m "feat(sns): oscillator control-visibility rules with tests"
```

---

## Task 4: Parameter wrappers (`IParam`, `ParamInt`, `ParamString`, `ParamBool`) + clipboard

**Files:**
- Create: `Src/ViewModels/SynthParam.cs` (interface + three wrappers + `SnsPartialClipboard`)
- Test: `Tests/TestSnsPartialClipboard.cs`

These wrappers each bind one FQP: clamp/convert, reflect hardware echoes, and enqueue throttled writes. They make the VMs DRY. The clipboard logic is pure (operates on `IParam`), so it is unit-tested with a fake.

- [ ] **Step 1: Write the failing test**

`Tests/TestSnsPartialClipboard.cs`:
```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.ViewModels;

namespace Tests;

public class SnsPartialClipboardTests
{
    private sealed class FakeParam : IParam
    {
        public FakeParam(string path, string value) { Path = path; _value = value; }
        private string _value;
        public string Path { get; }
        public string Snapshot() => _value;
        public void ApplyDisplay(string display) => _value = display;
    }

    [Test]
    public void Snapshot_captures_path_to_value()
    {
        var ps = new IParam[] { new FakeParam("a", "1"), new FakeParam("b", "Saw") };
        var snap = SnsPartialClipboard.Snapshot(ps);
        Assert.That(snap["a"], Is.EqualTo("1"));
        Assert.That(snap["b"], Is.EqualTo("Saw"));
    }

    [Test]
    public void Apply_writes_matching_paths_only()
    {
        var a = new FakeParam("a", "1");
        var b = new FakeParam("b", "Saw");
        SnsPartialClipboard.Apply(new IParam[] { a, b },
            new Dictionary<string, string> { ["a"] = "9", ["zzz"] = "ignored" });
        Assert.That(a.Snapshot(), Is.EqualTo("9"));
        Assert.That(b.Snapshot(), Is.EqualTo("Saw")); // untouched
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsPartialClipboardTests`
Expected: FAIL — `IParam`/`SnsPartialClipboard` do not exist.

- [ ] **Step 3: Write minimal implementation**

`Src/ViewModels/SynthParam.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>A copy/paste-able synth parameter: its lookup path plus get/set of the display value.</summary>
public interface IParam
{
    string Path { get; }
    string Snapshot();
    void ApplyDisplay(string display);
}

/// <summary>Pure copy/paste over <see cref="IParam"/> (testable without hardware).</summary>
public static class SnsPartialClipboard
{
    public static Dictionary<string, string> Snapshot(IEnumerable<IParam> ps)
        => ps.ToDictionary(p => p.Path, p => p.Snapshot());

    public static void Apply(IEnumerable<IParam> ps, IReadOnlyDictionary<string, string> data)
    {
        foreach (var p in ps)
            if (data.TryGetValue(p.Path, out var v))
                p.ApplyDisplay(v);
    }
}

/// <summary>Two-way bindable wrapper over one numeric FQP: clamps, reflects echoes, throttled writes.</summary>
public sealed class ParamInt : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private readonly int _min, _max;
    private bool _suppress;
    private int _value;

    public ParamInt(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer, int min, int max)
    {
        _domain = domain; _p = p; _writer = writer; _min = min; _max = max;
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _min, _max);
            if (_value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) Enqueue();
        }
    }

    private void Enqueue() => _writer.Enqueue(_p.ParSpec.Path,
        () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(CultureInfo.InvariantCulture)));

    public string Snapshot() => _value.ToString(CultureInfo.InvariantCulture);

    public void ApplyDisplay(string display)
    {
        if (int.TryParse(display, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            Value = v; // setter clamps + enqueues
    }

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try
        {
            if (int.TryParse(_p.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                Value = Math.Clamp(v, _min, _max);
        }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}

/// <summary>Two-way bindable wrapper over one enum/string FQP, exposing its allowed Options.</summary>
public sealed class ParamString : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private bool _suppress;
    private string _value = "";

    public ParamString(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer,
        IReadOnlyList<string>? options = null)
    {
        _domain = domain; _p = p; _writer = writer;
        Options = options
            ?? (p.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                ?? new List<string>());
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;
    public IReadOnlyList<string> Options { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (value is null || _value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) _writer.Enqueue(_p.ParSpec.Path, () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value));
        }
    }

    public string Snapshot() => _value;
    public void ApplyDisplay(string display) => Value = display;

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try { Value = _p.StringValue; }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}

/// <summary>Two-way bindable bool over an OFF/ON-style FQP.</summary>
public sealed class ParamBool : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private readonly string _on, _off;
    private bool _suppress;
    private bool _value;

    public ParamBool(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer,
        string onValue = "ON", string offValue = "OFF")
    {
        _domain = domain; _p = p; _writer = writer; _on = onValue; _off = offValue;
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) _writer.Enqueue(_p.ParSpec.Path,
                () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, value ? _on : _off));
        }
    }

    public string Snapshot() => _value ? _on : _off;
    public void ApplyDisplay(string display) => Value = string.Equals(display, _on, StringComparison.OrdinalIgnoreCase);

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try { Value = string.Equals(_p.StringValue, _on, StringComparison.OrdinalIgnoreCase); }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --filter SnsPartialClipboardTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/SynthParam.cs Tests/TestSnsPartialClipboard.cs
git commit -m "feat(sns): bindable param wrappers + pure partial clipboard with tests"
```

---

## Task 5: `AdsrEnvelopeControl` (custom draggable envelope control)

**Files:**
- Create: `Src/Controls/AdsrEnvelopeControl.cs`

No unit test (UI/render); its math is covered by Task 1. Verified by build now and smoke later.

- [ ] **Step 1: Write the control**

`Src/Controls/AdsrEnvelopeControl.cs`:
```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Draggable ADSR envelope graph. A/D/S/R are 0..127 StyledProperties (TwoWay by default) so a VM
/// can bind them to throttled parameter wrappers. Pointer drag and arrow-key edits adjust the
/// focused handle; the View also provides numeric inputs for full keyboard accessibility.
/// All value↔pixel math lives in <see cref="SnsEnvelopeMapping"/>.
/// </summary>
public class AdsrEnvelopeControl : Control
{
    private const double HandleRadius = 7;
    private const double SustainWidth = 40;

    public static readonly StyledProperty<int> AttackProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Attack), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DecayProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Decay), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> SustainProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Sustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> ReleaseProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Release), 0, defaultBindingMode: BindingMode.TwoWay);

    public int Attack { get => GetValue(AttackProperty); set => SetValue(AttackProperty, value); }
    public int Decay { get => GetValue(DecayProperty); set => SetValue(DecayProperty, value); }
    public int Sustain { get => GetValue(SustainProperty); set => SetValue(SustainProperty, value); }
    public int Release { get => GetValue(ReleaseProperty); set => SetValue(ReleaseProperty, value); }

    private enum Handle { None, Attack, Decay, Release }
    private Handle _active = Handle.None;
    private Handle _focused = Handle.Attack;

    private static readonly IBrush Bg = new SolidColorBrush(Color.Parse("#1b1f22"));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa));
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly IPen LinePen = new Pen(new SolidColorBrush(Color.Parse("#7fb6e0")), 2);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)), 1);
    private static readonly IPen FocusPen = new Pen(Brushes.Orange, 2);

    static AdsrEnvelopeControl()
    {
        AffectsRender<AdsrEnvelopeControl>(AttackProperty, DecayProperty, SustainProperty, ReleaseProperty);
        FocusableProperty.OverrideDefaultValue<AdsrEnvelopeControl>(true);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(Bg, new Rect(0, 0, w, h));
        context.DrawLine(GridPen, new Point(0, h - 1), new Point(w, h - 1));
        context.DrawLine(GridPen, new Point(0, h / 2), new Point(w, h / 2));

        var p = SnsEnvelopeMapping.ComputePoints(Attack, Decay, Sustain, Release, w, h, SustainWidth);

        var fill = new StreamGeometry();
        using (var c = fill.Open())
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
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
            c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
            c.LineTo(new Point(p.End.X, p.End.Y));
            c.EndFigure(false);
        }
        context.DrawGeometry(null, LinePen, line);

        DrawHandle(context, p.Peak, _focused == Handle.Attack);
        DrawHandle(context, p.SustainStart, _focused == Handle.Decay);
        DrawHandle(context, p.End, _focused == Handle.Release);
    }

    private static void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? FocusPen : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var p = SnsEnvelopeMapping.ComputePoints(Attack, Decay, Sustain, Release, Bounds.Width, Bounds.Height, SustainWidth);
        _active = Nearest(pos, p);
        if (_active != Handle.None)
        {
            _focused = _active;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_active == Handle.None) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.SegmentMax(Bounds.Width, SustainWidth);
        switch (_active)
        {
            case Handle.Attack:
                Attack = SnsEnvelopeMapping.AttackFromX(pos.X, seg);
                break;
            case Handle.Decay:
                var aW = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
                Decay = SnsEnvelopeMapping.DecayFromX(pos.X, aW, seg);
                Sustain = SnsEnvelopeMapping.LevelFromY(pos.Y, Bounds.Height);
                break;
            case Handle.Release:
                var aW2 = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
                var dW2 = SnsEnvelopeMapping.TimeToWidth(Decay, seg);
                Release = SnsEnvelopeMapping.ReleaseFromX(pos.X, aW2, dW2, SustainWidth, seg);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_active == Handle.None) return;
        e.Pointer.Capture(null);
        _active = Handle.None;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Tab:
                _focused = _focused switch
                {
                    Handle.Attack => Handle.Decay,
                    Handle.Decay => Handle.Release,
                    _ => Handle.Attack
                };
                e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        switch (_focused)
        {
            case Handle.Attack: if (!vertical) Attack = SnsEnvelopeMapping.Clamp(Attack + delta); break;
            case Handle.Decay:
                if (vertical) Sustain = SnsEnvelopeMapping.Clamp(Sustain + delta);
                else Decay = SnsEnvelopeMapping.Clamp(Decay + delta);
                break;
            case Handle.Release: if (!vertical) Release = SnsEnvelopeMapping.Clamp(Release + delta); break;
        }
    }

    private static Handle Nearest(Point pos, SnsEnvelopeMapping.EnvPoints p)
    {
        var best = Handle.None;
        var bestD = HandleRadius * HandleRadius * 4; // generous hit area
        Check(Handle.Attack, p.Peak);
        Check(Handle.Decay, p.SustainStart);
        Check(Handle.Release, p.End);
        return best;

        void Check(Handle h, SnsEnvelopeMapping.Point pt)
        {
            var dx = pos.X - pt.X; var dy = pos.Y - pt.Y; var d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = h; }
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`. (Pre-existing nullable warnings are unrelated.)

- [ ] **Step 3: Commit**

```bash
git add Src/Controls/AdsrEnvelopeControl.cs
git commit -m "feat(sns): draggable + keyboard ADSR envelope custom control"
```

---

## Task 6: `SNSPartialViewModel` (one partial: oscillator + amp + amp envelope)

**Files:**
- Create: `Src/ViewModels/SNSPartialViewModel.cs`

Verified by build now; behaviour verified in the smoke test (Task 11).

- [ ] **Step 1: Write the view model**

`Src/ViewModels/SNSPartialViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one SN-S partial: oscillator, amp, and amp envelope.</summary>
public sealed class SNSPartialViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Synth Tone Partial/"; // partial path prefix
    private const string CP = "SuperNATURAL Synth Tone Common/";  // common path prefix

    private readonly SNSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..2
    public string Title => $"Partial {Index + 1}";

    // --- Oscillator ---
    public ParamString OscWave { get; }
    public ParamString OscWaveVariation { get; }
    public ParamInt OscPitch { get; }
    public ParamInt OscDetune { get; }
    public ParamInt OscPulseWidth { get; }
    public ParamInt OscPulseWidthShift { get; }
    public ParamInt OscPwmDepth { get; }
    public ParamInt SuperSawDetune { get; }
    public ParamString WaveGain { get; }
    public ParamString WaveNumber { get; }

    // --- Amp ---
    public ParamInt AmpLevel { get; }
    public ParamInt AmpPan { get; }
    public ParamInt AmpVeloSens { get; }
    public ParamInt AmpKeyfollow { get; }

    // --- Amp envelope ---
    public ParamInt AmpEnvAttack { get; }
    public ParamInt AmpEnvDecay { get; }
    public ParamInt AmpEnvSustain { get; }
    public ParamInt AmpEnvRelease { get; }

    // --- Card on/off (Common Partial{n} Switch) ---
    public ParamBool IsOn { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

    // Params copied/pasted/initialised (oscillator + amp + amp env, NOT the common on/off switch).
    private readonly IReadOnlyList<IParam> _editable;

    public SNSPartialViewModel(SNSynthToneEditorViewModel parent, DomainBase partialDomain,
        DomainBase commonDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> commonByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        var byPath = ToDict(partialDomain);

        ParamInt PI(string name, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + name], writer, min, max));
        ParamString PS(string name, IReadOnlyList<string>? opts = null) => Track(new ParamString(partialDomain, byPath[PP + name], writer, opts));

        OscWave = PS("OSC Wave");
        OscWaveVariation = PS("OSC Wave Variation");
        OscPitch = PI("OSC Pitch", -24, 24);
        OscDetune = PI("OSC Detune", -50, 50);
        OscPulseWidth = PI("OSC Pulse Width", 0, 127);
        OscPulseWidthShift = PI("OSC Pulse Width shift", 0, 127);
        OscPwmDepth = PI("OSC Pulse Width Mod Depth", 0, 127);
        SuperSawDetune = PI("Super Saw Detune", 0, 127);
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        WaveNumber = PS("Wave Number");

        AmpLevel = PI("AMP Level", 0, 127);
        AmpPan = PI("AMP Pan", -64, 63);
        AmpVeloSens = PI("AMP Level Velocity Sens", -63, 63);
        AmpKeyfollow = PI("AMP Level Keyfollow", -100, 100);

        AmpEnvAttack = PI("AMP Env Attack Time", 0, 127);
        AmpEnvDecay = PI("AMP Env Decay Time", 0, 127);
        AmpEnvSustain = PI("AMP Env Sustain Level", 0, 127);
        AmpEnvRelease = PI("AMP Env Release Time", 0, 127);

        IsOn = Track(new ParamBool(commonDomain, commonByPath[CP + $"Partial{index + 1} Switch"], writer));

        _editable = new IParam[]
        {
            OscWave, OscWaveVariation, OscPitch, OscDetune, OscPulseWidth, OscPulseWidthShift,
            OscPwmDepth, SuperSawDetune, WaveGain, WaveNumber,
            AmpLevel, AmpPan, AmpVeloSens, AmpKeyfollow,
            AmpEnvAttack, AmpEnvDecay, AmpEnvSustain, AmpEnvRelease
        };

        // Re-raise the conditional-visibility flags and card summary when the wave / level / pan change.
        OscWave.PropertyChanged += OnOscWaveChanged;
        AmpLevel.PropertyChanged += OnSummaryChanged;
        AmpPan.PropertyChanged += OnSummaryChanged;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T wrapper) where T : IDisposable
    {
        _wrappers.Add(wrapper);
        return wrapper;
    }

    // --- Conditional oscillator controls ---
    public bool ShowsPulseWidth => SnsOscillatorRules.ShowsPulseWidth(OscWave.Value);
    public bool ShowsSuperSawDetune => SnsOscillatorRules.ShowsSuperSawDetune(OscWave.Value);
    public bool ShowsPcm => SnsOscillatorRules.ShowsPcm(OscWave.Value);

    private void OnOscWaveChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ParamString.Value)) return;
        this.RaisePropertyChanged(nameof(ShowsPulseWidth));
        this.RaisePropertyChanged(nameof(ShowsSuperSawDetune));
        this.RaisePropertyChanged(nameof(ShowsPcm));
        this.RaisePropertyChanged(nameof(WaveSummary));
    }

    private void OnSummaryChanged(object? s, PropertyChangedEventArgs e)
        => this.RaisePropertyChanged(nameof(PanLabel));

    // --- Card summary ---
    public string WaveSummary => OscWave.Value;
    public string PanLabel => AmpPan.Value == 0 ? "C" : AmpPan.Value < 0 ? $"L{-AmpPan.Value}" : $"R{AmpPan.Value}";

    // --- Utilities (edit-buffer only; no save) ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);

    public void Paste()
    {
        if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data);
    }

    public void Init() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "OSC Wave"] = "Saw",
        [PP + "OSC Wave Variation"] = "A",
        [PP + "OSC Pitch"] = "0",
        [PP + "OSC Detune"] = "0",
        [PP + "OSC Pulse Width"] = "0",
        [PP + "OSC Pulse Width shift"] = "0",
        [PP + "OSC Pulse Width Mod Depth"] = "0",
        [PP + "Super Saw Detune"] = "0",
        [PP + "Wave Gain"] = "0",
        [PP + "AMP Level"] = "100",
        [PP + "AMP Pan"] = "0",
        [PP + "AMP Level Velocity Sens"] = "0",
        [PP + "AMP Level Keyfollow"] = "0",
        [PP + "AMP Env Attack Time"] = "0",
        [PP + "AMP Env Decay Time"] = "64",
        [PP + "AMP Env Sustain Level"] = "110",
        [PP + "AMP Env Release Time"] = "30"
    };

    public void Dispose()
    {
        OscWave.PropertyChanged -= OnOscWaveChanged;
        AmpLevel.PropertyChanged -= OnSummaryChanged;
        AmpPan.PropertyChanged -= OnSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
```

- [ ] **Step 2: Note the build-ordering dependency**

`SNSPartialViewModel` and `SNSynthToneEditorViewModel` are mutually referential (child holds a
`_parent`; parent creates the children). Neither compiles alone, so build verification for this
file happens in **Task 8, Step 2** (after the editor shell exists). Do not run a standalone build here.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat(sns): partial view model (oscillator, amp, amp envelope, copy/paste/init)"
```

---

## Task 7: `SNSynthToneHeaderViewModel` (Tone Header common controls)

**Files:**
- Create: `Src/ViewModels/SNSynthToneHeaderViewModel.cs`

- [ ] **Step 1: Write the view model**

`Src/ViewModels/SNSynthToneHeaderViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Tone-wide (Common) controls shown in the editor header.</summary>
public sealed class SNSynthToneHeaderViewModel : ViewModelBase, IDisposable
{
    private const string CP = "SuperNATURAL Synth Tone Common/";

    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _toneName;

    public ParamInt ToneLevel { get; }
    public ParamBool IsMono { get; }       // ON = Mono, OFF = Poly
    public ParamBool Legato { get; }
    public ParamBool Portamento { get; }
    public ParamInt PortamentoTime { get; }
    public ParamBool Unison { get; }
    public ParamString UnisonSize { get; }
    public ParamInt AnalogFeel { get; }
    public ParamString Ring { get; }       // Off/Reserved/On (combo for the slice)

    public SNSynthToneHeaderViewModel(DomainBase common,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], writer, min, max));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], writer));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(common, byPath[CP + n], writer, o));

        ToneLevel = PI("Tone Level", 0, 127);
        IsMono = PB("Mono Switch");
        Legato = PB("Legato Switch");
        Portamento = PB("Portamento Switch");
        PortamentoTime = PI("Portamento Time", 0, 127);
        Unison = PB("Unison Switch");
        UnisonSize = PS("Unison Size", new[] { "2", "4", "6", "8" });
        AnalogFeel = PI("Analog Feel", 0, 127);
        Ring = PS("Ring Switch");

        _toneName = byPath[CP + "Tone Name"];
        _toneName.PropertyChanged += OnToneNameChanged;
    }

    public string ToneName => _toneName.StringValue;

    private void OnToneNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(ToneName)));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        _toneName.PropertyChanged -= OnToneNameChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
```

- [ ] **Step 2: Commit** (build deferred to Task 8)

```bash
git add Src/ViewModels/SNSynthToneHeaderViewModel.cs
git commit -m "feat(sns): tone header view model (level, mono, legato, glide, unison, drift, ring)"
```

---

## Task 8: `SNSynthToneEditorViewModel` (editor shell) + build

**Files:**
- Create: `Src/ViewModels/SNSynthToneEditorViewModel.cs`

- [ ] **Step 1: Write the view model**

`Src/ViewModels/SNSynthToneEditorViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly SuperNATURAL Synth editor for ONE part: header + 3 partials.</summary>
public sealed partial class SNSynthToneEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string>? _navigateToRawTab;

    public SNSynthToneHeaderViewModel Header { get; }
    public ObservableCollection<SNSPartialViewModel> Partials { get; } = [];

    /// <summary>Shared partial copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? PartialClipboard { get; set; }

    public SNSynthToneEditorViewModel(Integra7Domain domain, int partNo, Action<string>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var common = domain.SNSynthToneCommon(partNo);
        var commonByPath = ToDict(common);
        Header = new SNSynthToneHeaderViewModel(common, commonByPath, _writer);

        for (var i = 0; i < Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; i++)
            Partials.Add(new SNSPartialViewModel(this, domain.SNSynthTonePartial(partNo, i),
                common, commonByPath, i, _writer));

        _selectedPartial = Partials[0];
        _selectedPartial.IsSelected = true;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private SNSPartialViewModel _selectedPartial;
    public SNSPartialViewModel SelectedPartial
    {
        get => _selectedPartial;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedPartial)) return;
            _selectedPartial.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedPartial, value);
            _selectedPartial.IsSelected = true;
        }
    }

    [ReactiveCommand] public void CopyPartial() => SelectedPartial.Copy();
    [ReactiveCommand] public void PastePartial() => SelectedPartial.Paste();
    [ReactiveCommand] public void InitPartial() => SelectedPartial.Init();

    [ReactiveCommand] public void AdvancedOscillator() => _navigateToRawTab?.Invoke("SN-S-PARTIALS");
    [ReactiveCommand] public void AdvancedAmp() => _navigateToRawTab?.Invoke("SN-S-PARTIALS");
    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("SN-S-COMMON");

    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        _writer.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify Tasks 4–8 compile**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/SNSynthToneEditorViewModel.cs
git commit -m "feat(sns): editor shell view model (header, 3 partials, utilities, advanced nav)"
```

---

## Task 9: `SNSynthToneEditorView` (the editor UI)

**Files:**
- Create: `Src/Views/SNSynthToneEditorView.axaml`
- Create: `Src/Views/SNSynthToneEditorView.axaml.cs`

- [ ] **Step 1: Write the code-behind**

`Src/Views/SNSynthToneEditorView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class SNSynthToneEditorView : UserControl
{
    public SNSynthToneEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Write the view**

`Src/Views/SNSynthToneEditorView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:SNSynthToneEditorViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.SNSynthToneEditorView">

    <Grid RowDefinitions="Auto,*" Margin="8">

        <!-- ===== Tone Header ===== -->
        <Border Grid.Row="0" Background="#22000000" CornerRadius="6" Padding="10" Margin="0,0,0,8">
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <Border Background="#3d7eaa" CornerRadius="4" Padding="6,2" HorizontalAlignment="Left">
                        <TextBlock Text="SuperNATURAL Synth" Foreground="White" FontSize="11"/>
                    </Border>
                    <TextBlock Text="{Binding Header.ToneName}" FontSize="16" FontWeight="Bold"/>
                </StackPanel>

                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Tone Level" />
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.ToneLevel.Value, Mode=TwoWay}"/>
                </StackPanel>

                <StackPanel Spacing="2">
                    <TextBlock Text="Voice" ToolTip.Tip="Mono Switch"/>
                    <ToggleSwitch OnContent="Mono" OffContent="Poly" IsChecked="{Binding Header.IsMono.Value, Mode=TwoWay}"/>
                    <ToggleSwitch OnContent="Legato" OffContent="Legato"
                                  IsChecked="{Binding Header.Legato.Value, Mode=TwoWay}"
                                  IsEnabled="{Binding Header.IsMono.Value}"
                                  ToolTip.Tip="Legato only applies in Mono mode."/>
                </StackPanel>

                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Glide" ToolTip.Tip="Portamento"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding Header.Portamento.Value, Mode=TwoWay}"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.PortamentoTime.Value, Mode=TwoWay}"
                            IsEnabled="{Binding Header.Portamento.Value}"
                            ToolTip.Tip="Glide time (active only when Glide is on)."/>
                </StackPanel>

                <StackPanel Spacing="2">
                    <TextBlock Text="Unison"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding Header.Unison.Value, Mode=TwoWay}"/>
                    <ComboBox ItemsSource="{Binding Header.UnisonSize.Options}"
                              SelectedItem="{Binding Header.UnisonSize.Value, Mode=TwoWay}"
                              IsEnabled="{Binding Header.Unison.Value}"
                              ToolTip.Tip="Unison voice count (active only when Unison is on)."/>
                </StackPanel>

                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Vintage Drift" ToolTip.Tip="Analog Feel"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.AnalogFeel.Value, Mode=TwoWay}"/>
                </StackPanel>

                <StackPanel Spacing="2">
                    <TextBlock Text="Metallic Ring Mod" ToolTip.Tip="Ring Switch"/>
                    <ComboBox ItemsSource="{Binding Header.Ring.Options}"
                              SelectedItem="{Binding Header.Ring.Value, Mode=TwoWay}"/>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- ===== Rack + Selected partial ===== -->
        <Grid Grid.Row="1" ColumnDefinitions="240,*">

            <!-- Partial Rack -->
            <ListBox Grid.Column="0" Margin="0,0,8,0"
                     ItemsSource="{Binding Partials}"
                     SelectedItem="{Binding SelectedPartial, Mode=TwoWay}">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="vm:SNSPartialViewModel">
                        <Border Padding="8" CornerRadius="6">
                            <StackPanel Spacing="4">
                                <DockPanel>
                                    <TextBlock Text="{Binding Title}" FontWeight="Bold"/>
                                    <ToggleSwitch DockPanel.Dock="Right" OnContent="" OffContent=""
                                                  IsChecked="{Binding IsOn.Value, Mode=TwoWay}"
                                                  ToolTip.Tip="Partial on/off"/>
                                </DockPanel>
                                <TextBlock Text="{Binding WaveSummary}" Opacity="0.8"/>
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Lvl" Opacity="0.6"/>
                                    <TextBlock Text="{Binding AmpLevel.Value}"/>
                                    <TextBlock Text="Pan" Opacity="0.6"/>
                                    <TextBlock Text="{Binding PanLabel}"/>
                                </StackPanel>
                                <controls:AdsrEnvelopeControl Height="48" IsHitTestVisible="False"
                                    Attack="{Binding AmpEnvAttack.Value}"
                                    Decay="{Binding AmpEnvDecay.Value}"
                                    Sustain="{Binding AmpEnvSustain.Value}"
                                    Release="{Binding AmpEnvRelease.Value}"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Selected Partial Editor -->
            <ScrollViewer Grid.Column="1">
                <StackPanel Spacing="12" DataContext="{Binding SelectedPartial}" x:DataType="vm:SNSPartialViewModel">

                    <!-- Oscillator -->
                    <Border Background="#22000000" CornerRadius="6" Padding="10">
                        <StackPanel Spacing="8">
                            <TextBlock Text="Oscillator" FontWeight="Bold"/>
                            <StackPanel Orientation="Horizontal" Spacing="16">
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Waveform" ToolTip.Tip="OSC Wave"/>
                                    <ComboBox ItemsSource="{Binding OscWave.Options}"
                                              SelectedItem="{Binding OscWave.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Variation"/>
                                    <ComboBox ItemsSource="{Binding OscWaveVariation.Options}"
                                              SelectedItem="{Binding OscWaveVariation.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Pitch (semitones)" ToolTip.Tip="OSC Pitch"/>
                                    <Slider Minimum="-24" Maximum="24" Value="{Binding OscPitch.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Detune" ToolTip.Tip="OSC Detune (Flat / Center / Sharp)"/>
                                    <Slider Minimum="-50" Maximum="50" Value="{Binding OscDetune.Value, Mode=TwoWay}"/>
                                </StackPanel>
                            </StackPanel>

                            <!-- Pulse Width (PWM Square only) -->
                            <StackPanel Orientation="Horizontal" Spacing="16" IsVisible="{Binding ShowsPulseWidth}">
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Pulse Width" ToolTip.Tip="OSC Pulse Width"/>
                                    <Slider Minimum="0" Maximum="127" Value="{Binding OscPulseWidth.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="PW Shift" ToolTip.Tip="OSC Pulse Width shift"/>
                                    <Slider Minimum="0" Maximum="127" Value="{Binding OscPulseWidthShift.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="PWM Depth" ToolTip.Tip="OSC Pulse Width Mod Depth"/>
                                    <Slider Minimum="0" Maximum="127" Value="{Binding OscPwmDepth.Value, Mode=TwoWay}"/>
                                </StackPanel>
                            </StackPanel>

                            <!-- Super Saw -->
                            <StackPanel Spacing="2" Width="200" IsVisible="{Binding ShowsSuperSawDetune}">
                                <TextBlock Text="Super Saw Detune" ToolTip.Tip="Super Saw Detune"/>
                                <Slider Minimum="0" Maximum="127" Value="{Binding SuperSawDetune.Value, Mode=TwoWay}"/>
                            </StackPanel>

                            <!-- PCM -->
                            <StackPanel Orientation="Horizontal" Spacing="16" IsVisible="{Binding ShowsPcm}">
                                <StackPanel Spacing="2">
                                    <TextBlock Text="PCM Wave" ToolTip.Tip="Wave Number"/>
                                    <ComboBox MaxDropDownHeight="300" Width="220"
                                              ItemsSource="{Binding WaveNumber.Options}"
                                              SelectedItem="{Binding WaveNumber.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2">
                                    <TextBlock Text="PCM Gain (dB)" ToolTip.Tip="Wave Gain"/>
                                    <ComboBox ItemsSource="{Binding WaveGain.Options}"
                                              SelectedItem="{Binding WaveGain.Value, Mode=TwoWay}"/>
                                </StackPanel>
                            </StackPanel>

                            <Button Content="Advanced oscillator parameters…"
                                    Command="{Binding $parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext).AdvancedOscillatorCommand}"/>
                        </StackPanel>
                    </Border>

                    <!-- Amp -->
                    <Border Background="#22000000" CornerRadius="6" Padding="10">
                        <StackPanel Spacing="8">
                            <TextBlock Text="Amp" FontWeight="Bold"/>
                            <StackPanel Orientation="Horizontal" Spacing="16">
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Level" ToolTip.Tip="AMP Level"/>
                                    <Slider Minimum="0" Maximum="127" Value="{Binding AmpLevel.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Pan (L / C / R)" ToolTip.Tip="AMP Pan"/>
                                    <Slider Minimum="-64" Maximum="63" Value="{Binding AmpPan.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="160">
                                    <TextBlock Text="Velocity (Consistent → Dynamic)" ToolTip.Tip="AMP Level Velocity Sens"/>
                                    <Slider Minimum="-63" Maximum="63" Value="{Binding AmpVeloSens.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2" Width="180">
                                    <TextBlock Text="Keyfollow (Low → High louder)" ToolTip.Tip="AMP Level Keyfollow"/>
                                    <Slider Minimum="-100" Maximum="100" Value="{Binding AmpKeyfollow.Value, Mode=TwoWay}"/>
                                </StackPanel>
                            </StackPanel>
                            <Button Content="Advanced amp parameters…"
                                    Command="{Binding $parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext).AdvancedAmpCommand}"/>
                        </StackPanel>
                    </Border>

                    <!-- Amp Envelope -->
                    <Border Background="#22000000" CornerRadius="6" Padding="10">
                        <StackPanel Spacing="8">
                            <TextBlock Text="Amp Envelope (Volume shape)" FontWeight="Bold"/>
                            <controls:AdsrEnvelopeControl Height="180"
                                Attack="{Binding AmpEnvAttack.Value, Mode=TwoWay}"
                                Decay="{Binding AmpEnvDecay.Value, Mode=TwoWay}"
                                Sustain="{Binding AmpEnvSustain.Value, Mode=TwoWay}"
                                Release="{Binding AmpEnvRelease.Value, Mode=TwoWay}"/>
                            <StackPanel Orientation="Horizontal" Spacing="12">
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Attack" ToolTip.Tip="How quickly the sound starts."/>
                                    <NumericUpDown Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                   Value="{Binding AmpEnvAttack.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Decay" ToolTip.Tip="How quickly it falls after the start."/>
                                    <NumericUpDown Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                   Value="{Binding AmpEnvDecay.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Sustain" ToolTip.Tip="Level held while the key is down."/>
                                    <NumericUpDown Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                   Value="{Binding AmpEnvSustain.Value, Mode=TwoWay}"/>
                                </StackPanel>
                                <StackPanel Spacing="2">
                                    <TextBlock Text="Release" ToolTip.Tip="How long it fades after release."/>
                                    <NumericUpDown Minimum="0" Maximum="127" Increment="1" FormatString="0"
                                                   Value="{Binding AmpEnvRelease.Value, Mode=TwoWay}"/>
                                </StackPanel>
                            </StackPanel>
                        </StackPanel>
                    </Border>

                    <!-- Partial utilities -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Content="Copy partial"
                                Command="{Binding $parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext).CopyPartialCommand}"/>
                        <Button Content="Paste partial"
                                Command="{Binding $parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext).PastePartialCommand}"/>
                        <Button Content="Init partial"
                                Command="{Binding $parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext).InitPartialCommand}"/>
                    </StackPanel>

                </StackPanel>
            </ScrollViewer>
        </Grid>
    </Grid>
</UserControl>
```

> Note: the inner editor `StackPanel` sets `DataContext` to `SelectedPartial`, so partial controls bind directly; the editor-level commands (`*Command`) are reached via `$parent[ScrollViewer].((vm:SNSynthToneEditorViewModel)DataContext)`. If the `$parent` ancestor binding proves awkward at build time, the equivalent fallback is to give the root a `Name` (e.g. `x:Name="Root"`) and bind `Command="{Binding #Root.((vm:SNSynthToneEditorViewModel)DataContext).CopyPartialCommand}"`.

- [ ] **Step 3: Build to verify the view compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`. Fix any XAML binding-compilation errors (the `$parent`/`#Root` command bindings are the most likely; use the fallback noted above).

- [ ] **Step 4: Commit**

```bash
git add Src/Views/SNSynthToneEditorView.axaml Src/Views/SNSynthToneEditorView.axaml.cs
git commit -m "feat(sns): editor view (header, partial rack, oscillator/amp/amp-env panels)"
```

---

## Task 10: Integration — `PartViewModel` + `MainWindow.axaml`

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs` (add the editor VM property + creation + nav callback)
- Modify: `Src/Views/MainWindow.axaml` (add the editor tab; retag/relabel the raw SN-S tabs)

- [ ] **Step 1: Add the editor VM field to `PartViewModel`**

In `Src/ViewModels/PartViewModel.cs`, near the other `[Reactive]` fields (e.g. just after line 199 `private IDisposable? _cleanupSNSynthToneCommonParams;`), add:
```csharp
    [Reactive] private SNSynthToneEditorViewModel? _sNSynthToneEditor;
```
This generates the public `SNSynthToneEditor` property.

- [ ] **Step 2: Create the editor VM where the SN-S caches are populated**

In `Src/ViewModels/PartViewModel.cs`, in `InitializeParameterSourceCachesAsync`, immediately after line 1372 (`_sourceCacheSNSynthToneCommonMFXParameters.AddOrUpdate(p_snmfx);`) add:
```csharp

            // Friendly SuperNATURAL Synth editor for this part. Binds to the same live SN-S FQP
            // instances populated above, so it tracks preset/hardware changes for free. The
            // navigation callback points the inner tab control's SelectTabByTag binding at the
            // matching raw "Advanced" tab.
            _sNSynthToneEditor?.Dispose();
            SNSynthToneEditor = new SNSynthToneEditorViewModel(_i7domain, PartNo, tag => ToneTabKey = tag);
```

- [ ] **Step 3: Build to verify integration compiles so far**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj --nologo`
Expected: `0 Error(s)`.

- [ ] **Step 4: Retag the existing raw SN-S "Tone" tab and add the editor tab**

In `Src/Views/MainWindow.axaml`, replace the existing SN-S "Tone" tab (lines 285–291):
```xml
                                            <TabItem Header="Tone"
                                                     Tag="SN-S"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNSynthToneCommonParameters}"
                                                    SearchText="{Binding SearchTextSNSynthToneCommon, Mode=TwoWay}" />
                                            </TabItem>
```
with (new editor tab gets `Tag="SN-S"`; the raw common tab becomes `Tag="SN-S-COMMON"` and is relabeled):
```xml
                                            <TabItem Header="Editor"
                                                     Tag="SN-S"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:SNSynthToneEditorView DataContext="{Binding SNSynthToneEditor}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Common"
                                                     Tag="SN-S-COMMON"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNSynthToneCommonParameters}"
                                                    SearchText="{Binding SearchTextSNSynthToneCommon, Mode=TwoWay}" />
                                            </TabItem>
```

- [ ] **Step 5: Retag/relabel the raw SN-S "MFX" tab**

In `Src/Views/MainWindow.axaml`, replace the SN-S MFX tab (lines 292–297):
```xml
                                            <TabItem Header="MFX"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNSynthToneCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextSNSynthToneCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
```
with:
```xml
                                            <TabItem Header="Advanced — MFX"
                                                     Tag="SN-S-MFX"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNSynthToneCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextSNSynthToneCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
```

- [ ] **Step 6: Retag/relabel the raw SN-S "Partials" tab**

In `Src/Views/MainWindow.axaml`, on the SN-S Partials `TabItem` (lines 367–368) change the header and add a tag:
```xml
                                            <TabItem Header="Partials"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
```
to:
```xml
                                            <TabItem Header="Advanced — Partials"
                                                     Tag="SN-S-PARTIALS"
                                                     IsVisible="{Binding SelectedPresetIsSNSynthTone}">
```

- [ ] **Step 7: Build to verify the whole app compiles**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs Src/Views/MainWindow.axaml
git commit -m "feat(sns): integrate editor as default SN-S tab; retag raw grids as Advanced"
```

---

## Task 11: Full verification (tests + manual smoke) and final commit

**Files:** none (verification only).

- [ ] **Step 1: Run the full test suite**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test --nologo`
Expected: all tests PASS, including the new `SnsEnvelopeMappingTests`, `ThrottledParameterWriterTests`, `SnsOscillatorRulesTests`, `SnsPartialClipboardTests`, and the pre-existing suites.

- [ ] **Step 2: Manual smoke test (requires a connected Integra-7, or run to confirm UI loads)**

Run the app: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj`

Confirm:
1. Select a part, then select an **SN-S** preset → the per-part panel auto-selects the **"Editor"** tab showing the header + 3 partial cards + the selected partial's Oscillator/Amp/Amp-Envelope.
2. Selecting a non-SN-S preset shows the usual tabs (editor tab hidden).
3. Header: Tone Level slider, Mono/Poly toggle, Legato disabled until Mono, Glide time disabled until Glide on, Unison size disabled until Unison on, Vintage Drift, Ring — all send (watch the device / logs).
4. Click each partial card → it becomes selected; the on/off toggle flips `Partial{n} Switch`.
5. Oscillator: changing Waveform to *Pulse Width Mod. Square* / *SuperSaw* / *Pcm* reveals the matching extra controls; others hidden.
6. Drag the Amp Envelope handles → curve updates live, device receives throttled updates; the numeric inputs reflect and also drive the graph; arrow keys move the focused handle.
7. Copy partial → select another card → Paste partial copies the oscillator/amp/envelope; Init partial resets to defaults.
8. "Advanced …" buttons switch to the "Advanced — Partials"/"Advanced — Common" raw tabs.
9. Edit a value on the hardware → the editor UI updates.

- [ ] **Step 3: Verify against acceptance criteria**

Re-read §13 of the spec and confirm each criterion is met. Note any deviations.

- [ ] **Step 4: Final commit (if any fixes were needed during smoke)**

```bash
git add -A
git commit -m "fix(sns): address issues found during Phase 1 smoke test"
```

---

## Self-review notes (for the executor)

- The per-key throttle delivers the final dragged value as the last write in each key's window (proven by the Motional Surround editor); there is no separate release-flush.
- `WaveNumber` options come from `ParSpec.Repr` (≈450 PCM names) — the combo uses `MaxDropDownHeight`.
- If a partial's preset is not yet SN-S when the editor VM is built, its FQP values are defaults until an SN-S preset is selected (then `ResyncPartAsync` reads the SN-S domains and the wrappers update via their `StringValue` subscriptions). The editor tab is only visible for SN-S tones, so this is invisible to the user.
- No undo system exists; copy/paste/init act on the edit buffer only and are not persisted unless the user runs the existing Save User Tone flow.
- Solo/Mute is intentionally out of scope (deferred — see spec §8).
- Pan and bipolar (`*Velocity Sens`, `Keyfollow`, `AMP Pan`) are display-space passthrough: the wrappers send the display string and clamp to the display range; the raw↔display conversion is done by the existing `DisplayValueToRawValueConverter` in the domain layer. There is therefore no new bipolar-mapping function to unit-test (spec §12) — correctness is confirmed in the smoke test (Task 11, step 2.6/3).
