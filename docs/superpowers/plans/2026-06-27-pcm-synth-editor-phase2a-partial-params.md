# PCM Synth Tone Editor — Phase 2a (Partial Sound params + rich cards) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give `PCMPartialViewModel` the full friendly Sound parameter set (wave, output/amp, filter, three envelopes, bias), make the rack cards show real wave/level/pan/filter summaries, and make Copy/Paste/Init operate on the partial's editable params — without yet building the Sound tab view (that is Phase 2b) or the graphical envelope (Phase 2c).

**Architecture:** Extend the existing `PCMPartialViewModel` (Phase 1 skeleton) by mirroring `SNSPartialViewModel`'s parameter-wrapper + card-summary + clipboard pattern, with PCM partial param names/ranges. Add a pure `PcmTvfRules` helper (filter-type abbreviation for the card), tested in isolation. Re-add the `partialDomain` constructor parameter (dropped in Phase 1 as YAGNI) since the partial now wraps partial-domain params.

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`

> Use `--configuration Release` if a running app holds the Debug exe lock (MSB3027/MSB3021 is a lock, not a compile error). Do NOT use `--no-verify` on commits.

---

## File Structure

- **Create** `Src/Models/Services/PcmTvfRules.cs` — pure: `Abbrev(string filterType)` → short filter label for the card (e.g. "Low-pass filter" → "LPF"). One responsibility: card label formatting.
- **Create** `Tests/TestPcmTvfRules.cs` — unit tests for `PcmTvfRules.Abbrev`.
- **Modify** `Src/ViewModels/PCMPartialViewModel.cs` — add the Sound param wrappers, card-summary properties, `_editable` set + real `Copy/Paste/Init`, and re-add the `partialDomain` ctor param.
- **Modify** `Src/ViewModels/PCMSynthToneEditorViewModel.cs` — pass `domain.PCMSynthTonePartial(partNo, i)` to the partial ctor again (call-site update for the re-added param).
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — replace the partial card's "(sound controls in a later phase)" stub with wave/level/pan/filter summary rows (mirroring the SN-S card).

---

## Task 1: PcmTvfRules filter-abbreviation helper

**Files:**
- Create: `Src/Models/Services/PcmTvfRules.cs`
- Test: `Tests/TestPcmTvfRules.cs`

Mirrors the existing `SnsFilterRules.Abbrev` (used by the SN-S card's `FilterSummary`). The PCM TVF type strings are: `Off`, `Low-pass filter`, `Band-pass filter`, `High-pass filter`, `Peaking filter`, `Low-pass filter 2`, `Low-pass filter 3`.

- [ ] **Step 1: Write the failing test** — create `Tests/TestPcmTvfRules.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPcmTvfRules
{
    [TestCase("Off", "Off")]
    [TestCase("Low-pass filter", "LPF")]
    [TestCase("Band-pass filter", "BPF")]
    [TestCase("High-pass filter", "HPF")]
    [TestCase("Peaking filter", "PKG")]
    [TestCase("Low-pass filter 2", "LPF2")]
    [TestCase("Low-pass filter 3", "LPF3")]
    public void Abbrev_maps_known_types(string input, string expected)
    {
        Assert.That(PcmTvfRules.Abbrev(input), Is.EqualTo(expected));
    }

    [Test]
    public void Abbrev_passes_through_unknown()
    {
        Assert.That(PcmTvfRules.Abbrev("Something Else"), Is.EqualTo("Something Else"));
    }

    [Test]
    public void Abbrev_handles_null_or_empty()
    {
        Assert.That(PcmTvfRules.Abbrev(null), Is.EqualTo(""));
        Assert.That(PcmTvfRules.Abbrev(""), Is.EqualTo(""));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: FAIL to compile / test fails — `PcmTvfRules` does not exist yet.

- [ ] **Step 3: Write the implementation** — create `Src/Models/Services/PcmTvfRules.cs`:

```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure formatting helpers for the PCM Synth TVF (filter) — short labels for the rack card.</summary>
public static class PcmTvfRules
{
    private static readonly Dictionary<string, string> Abbrevs = new()
    {
        ["Off"] = "Off",
        ["Low-pass filter"] = "LPF",
        ["Band-pass filter"] = "BPF",
        ["High-pass filter"] = "HPF",
        ["Peaking filter"] = "PKG",
        ["Low-pass filter 2"] = "LPF2",
        ["Low-pass filter 3"] = "LPF3",
    };

    /// <summary>Short label for a TVF filter-type display string; passes unknown values through;
    /// null/empty → "".</summary>
    public static string Abbrev(string? filterType)
    {
        if (string.IsNullOrEmpty(filterType)) return "";
        return Abbrevs.TryGetValue(filterType, out var a) ? a : filterType;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: PASS. Test count increases by 9 (3 TestCases for the 7 mapped + 2 standalone = actually 7 TestCase rows + 2 tests = 9).

- [ ] **Step 5: Commit**

```
git add Src/Models/Services/PcmTvfRules.cs Tests/TestPcmTvfRules.cs
git commit -m "feat: PcmTvfRules filter-abbreviation helper for the rack card"
```

---

## Task 2: Full Sound parameter set on PCMPartialViewModel

**Files:**
- Modify: `Src/ViewModels/PCMPartialViewModel.cs` (replace whole file)
- Modify: `Src/ViewModels/PCMSynthToneEditorViewModel.cs` (call-site: re-add `partialDomain` argument)

Add every friendly Sound parameter (wave, output/amp, filter, pitch/TVF/TVA envelopes, bias), the card-summary properties, the `_editable` set feeding Copy/Paste/Init, and a neutral `Init`. Re-add the `partialDomain` ctor parameter (the partial now reads partial-domain params; `IsOn` stays on the PMT domain). Ranges are the verified display ranges from `ParameterDefinitions.cs`.

- [ ] **Step 1: Replace `Src/ViewModels/PCMPartialViewModel.cs` with EXACTLY:**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one PCM Synth partial: wave selection, output/amp, filter (TVF),
/// the three rate/level envelopes (pitch/TVF/TVA), and bias. On/off is the PMT per-partial switch.
/// Solo/Mute audition is coordinated by the parent editor VM.</summary>
public sealed class PCMPartialViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";                 // partial path prefix
    private const string PMT = "PCM Synth Tone Partial Mix Table/";      // pmt path prefix

    private readonly PCMSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..3
    public string Title => $"Partial {Index + 1}";

    // --- Wave ---
    public ParamString WaveGroupType { get; }   // Internal / SRX
    public ParamString WaveNumberL { get; }     // wave name (mono / left)
    public ParamString WaveNumberR { get; }     // wave name (right, stereo)
    public ParamString WaveGain { get; }        // -6 / 0 / 6 / 12 dB
    public ParamBool WaveFxmSwitch { get; }
    public ParamInt WaveFxmColor { get; }
    public ParamInt WaveFxmDepth { get; }

    // --- Output / Amp ---
    public ParamInt PartialLevel { get; }
    public ParamInt PartialPan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    // --- Filter (TVF) ---
    public ParamString TvfFilterType { get; }
    public ParamInt TvfCutoff { get; }
    public ParamInt TvfResonance { get; }
    public ParamInt TvfEnvDepth { get; }

    // --- Pitch envelope (TVP): 4 times, 5 bipolar levels + depth ---
    public ParamInt PitchEnvDepth { get; }
    public ParamInt PitchEnvTime1 { get; }
    public ParamInt PitchEnvTime2 { get; }
    public ParamInt PitchEnvTime3 { get; }
    public ParamInt PitchEnvTime4 { get; }
    public ParamInt PitchEnvLevel0 { get; }
    public ParamInt PitchEnvLevel1 { get; }
    public ParamInt PitchEnvLevel2 { get; }
    public ParamInt PitchEnvLevel3 { get; }
    public ParamInt PitchEnvLevel4 { get; }

    // --- Filter envelope (TVF): 4 times, 5 levels ---
    public ParamInt TvfEnvTime1 { get; }
    public ParamInt TvfEnvTime2 { get; }
    public ParamInt TvfEnvTime3 { get; }
    public ParamInt TvfEnvTime4 { get; }
    public ParamInt TvfEnvLevel0 { get; }
    public ParamInt TvfEnvLevel1 { get; }
    public ParamInt TvfEnvLevel2 { get; }
    public ParamInt TvfEnvLevel3 { get; }
    public ParamInt TvfEnvLevel4 { get; }

    // --- Amp envelope (TVA): 4 times, 3 levels (start/end implicitly 0) ---
    public ParamInt TvaEnvTime1 { get; }
    public ParamInt TvaEnvTime2 { get; }
    public ParamInt TvaEnvTime3 { get; }
    public ParamInt TvaEnvTime4 { get; }
    public ParamInt TvaEnvLevel1 { get; }
    public ParamInt TvaEnvLevel2 { get; }
    public ParamInt TvaEnvLevel3 { get; }

    // --- Bias ---
    public ParamInt BiasLevel { get; }
    public ParamInt BiasPosition { get; }
    public ParamString BiasDirection { get; }

    /// <summary>Card on/off — the PMT per-partial switch (audition saves/restores this).</summary>
    public ParamBool IsOn { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public PCMPartialViewModel(PCMSynthToneEditorViewModel parent, DomainBase partialDomain,
        DomainBase pmtDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> pmtByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        var byPath = ToDict(partialDomain);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(partialDomain, byPath[PP + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(partialDomain, byPath[PP + n], writer));

        WaveGroupType = PS("Wave Group Type");
        WaveNumberL = PS("Wave Number L (Mono)");
        WaveNumberR = PS("Wave Number R");
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        WaveFxmSwitch = PB("Wave FXM Switch");
        WaveFxmColor = PI("Wave FXM Color", 1, 4);
        WaveFxmDepth = PI("Wave FXM Depth", 0, 16);

        PartialLevel = PI("Partial Level", 0, 127);
        PartialPan = PI("Partial Pan", -64, 63);
        CoarseTune = PI("Partial Coarse Tune", -48, 48);
        FineTune = PI("Partial Fine Tune", -50, 50);
        ChorusSend = PI("Partial Chorus Send Level", 0, 127);
        ReverbSend = PI("Partial Reverb Send Level", 0, 127);

        TvfFilterType = PS("TVF Filter Type");
        TvfCutoff = PI("TVF Cutoff Frequency", 0, 127);
        TvfResonance = PI("TVF Resonance", 0, 127);
        TvfEnvDepth = PI("TVF Env Depth", -63, 63);

        PitchEnvDepth = PI("Pitch Env Depth", -12, 12);
        PitchEnvTime1 = PI("Pitch Env Time 1", 0, 127);
        PitchEnvTime2 = PI("Pitch Env Time 2", 0, 127);
        PitchEnvTime3 = PI("Pitch Env Time 3", 0, 127);
        PitchEnvTime4 = PI("Pitch Env Time 4", 0, 127);
        PitchEnvLevel0 = PI("Pitch Env Level 0", -63, 63);
        PitchEnvLevel1 = PI("Pitch Env Level 1", -63, 63);
        PitchEnvLevel2 = PI("Pitch Env Level 2", -63, 63);
        PitchEnvLevel3 = PI("Pitch Env Level 3", -63, 63);
        PitchEnvLevel4 = PI("Pitch Env Level 4", -63, 63);

        TvfEnvTime1 = PI("TVF Env Time 1", 0, 127);
        TvfEnvTime2 = PI("TVF Env Time 2", 0, 127);
        TvfEnvTime3 = PI("TVF Env Time 3", 0, 127);
        TvfEnvTime4 = PI("TVF Env Time 4", 0, 127);
        TvfEnvLevel0 = PI("TVF Env Level 0", 0, 127);
        TvfEnvLevel1 = PI("TVF Env Level 1", 0, 127);
        TvfEnvLevel2 = PI("TVF Env Level 2", 0, 127);
        TvfEnvLevel3 = PI("TVF Env Level 3", 0, 127);
        TvfEnvLevel4 = PI("TVF Env Level 4", 0, 127);

        TvaEnvTime1 = PI("TVA Env Time 1", 0, 127);
        TvaEnvTime2 = PI("TVA Env Time 2", 0, 127);
        TvaEnvTime3 = PI("TVA Env Time 3", 0, 127);
        TvaEnvTime4 = PI("TVA Env Time 4", 0, 127);
        TvaEnvLevel1 = PI("TVA Env Level 1", 0, 127);
        TvaEnvLevel2 = PI("TVA Env Level 2", 0, 127);
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);

        BiasLevel = PI("Bias Level", -100, 100);
        BiasPosition = PI("Bias Position", 0, 127);
        BiasDirection = PS("Bias Direction");

        IsOn = Track(new ParamBool(pmtDomain, pmtByPath[PMT + $"PMT {index + 1} Partial Switch"], writer));

        _editable = new IParam[]
        {
            WaveGroupType, WaveNumberL, WaveNumberR, WaveGain, WaveFxmSwitch, WaveFxmColor, WaveFxmDepth,
            PartialLevel, PartialPan, CoarseTune, FineTune, ChorusSend, ReverbSend,
            TvfFilterType, TvfCutoff, TvfResonance, TvfEnvDepth,
            PitchEnvDepth, PitchEnvTime1, PitchEnvTime2, PitchEnvTime3, PitchEnvTime4,
            PitchEnvLevel0, PitchEnvLevel1, PitchEnvLevel2, PitchEnvLevel3, PitchEnvLevel4,
            TvfEnvTime1, TvfEnvTime2, TvfEnvTime3, TvfEnvTime4,
            TvfEnvLevel0, TvfEnvLevel1, TvfEnvLevel2, TvfEnvLevel3, TvfEnvLevel4,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4,
            TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
            BiasLevel, BiasPosition, BiasDirection,
        };

        // Card summaries follow the wave / level / pan / filter the user actually sees on the card.
        WaveNumberL.PropertyChanged += OnSummaryChanged;
        PartialLevel.PropertyChanged += OnSummaryChanged;
        PartialPan.PropertyChanged += OnSummaryChanged;
        TvfFilterType.PropertyChanged += OnSummaryChanged;
        TvfCutoff.PropertyChanged += OnSummaryChanged;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T wrapper) where T : IDisposable { _wrappers.Add(wrapper); return wrapper; }

    // --- Card summary ---
    public string WaveSummary => WaveNumberL.Value;
    public string LevelLabel => PartialLevel.Value.ToString();
    public string PanLabel => PartialPan.Value == 0 ? "C" : PartialPan.Value < 0 ? $"L{-PartialPan.Value}" : $"R{PartialPan.Value}";
    public string FilterSummary => $"{PcmTvfRules.Abbrev(TvfFilterType.Value)} {TvfCutoff.Value}";

    private void OnSummaryChanged(object? s, PropertyChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(WaveSummary));
        this.RaisePropertyChanged(nameof(LevelLabel));
        this.RaisePropertyChanged(nameof(PanLabel));
        this.RaisePropertyChanged(nameof(FilterSummary));
    }

    // --- Audition (transient solo/mute; coordinated by the parent editor VM) ---
    private bool _solo;
    public bool Solo
    {
        get => _solo;
        set { if (_solo == value) return; this.RaiseAndSetIfChanged(ref _solo, value); _parent.RecomputeAudition(); }
    }

    private bool _mute;
    public bool Mute
    {
        get => _mute;
        set { if (_mute == value) return; this.RaiseAndSetIfChanged(ref _mute, value); _parent.RecomputeAudition(); }
    }

    /// <summary>Set solo/mute without triggering a parent recompute (used for bulk clear).</summary>
    internal void SetAuditionFlags(bool solo, bool mute)
    {
        this.RaiseAndSetIfChanged(ref _solo, solo, nameof(Solo));
        this.RaiseAndSetIfChanged(ref _mute, mute, nameof(Mute));
    }

    // --- Copy / Paste / Init (edit-buffer only; no save) ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);
    public void Paste() { if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    public void Init() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset. Leaves the wave selection untouched (the wave is the instrument); resets
    // output/filter/envelopes to a simple, audible shape.
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Wave Gain"] = "0",
        [PP + "Wave FXM Switch"] = "OFF",
        [PP + "Partial Level"] = "127",
        [PP + "Partial Pan"] = "0",
        [PP + "Partial Coarse Tune"] = "0",
        [PP + "Partial Fine Tune"] = "0",
        [PP + "TVF Filter Type"] = "Low-pass filter",
        [PP + "TVF Cutoff Frequency"] = "127",
        [PP + "TVF Resonance"] = "0",
        [PP + "TVF Env Depth"] = "0",
        [PP + "Pitch Env Depth"] = "0",
        [PP + "Pitch Env Time 1"] = "0",
        [PP + "Pitch Env Time 2"] = "0",
        [PP + "Pitch Env Time 3"] = "0",
        [PP + "Pitch Env Time 4"] = "0",
        [PP + "Pitch Env Level 0"] = "0",
        [PP + "Pitch Env Level 1"] = "0",
        [PP + "Pitch Env Level 2"] = "0",
        [PP + "Pitch Env Level 3"] = "0",
        [PP + "Pitch Env Level 4"] = "0",
        [PP + "TVF Env Time 1"] = "0",
        [PP + "TVF Env Time 2"] = "64",
        [PP + "TVF Env Time 3"] = "64",
        [PP + "TVF Env Time 4"] = "30",
        [PP + "TVF Env Level 0"] = "127",
        [PP + "TVF Env Level 1"] = "127",
        [PP + "TVF Env Level 2"] = "127",
        [PP + "TVF Env Level 3"] = "127",
        [PP + "TVF Env Level 4"] = "127",
        [PP + "TVA Env Time 1"] = "0",
        [PP + "TVA Env Time 2"] = "64",
        [PP + "TVA Env Time 3"] = "64",
        [PP + "TVA Env Time 4"] = "30",
        [PP + "TVA Env Level 1"] = "127",
        [PP + "TVA Env Level 2"] = "127",
        [PP + "TVA Env Level 3"] = "110",
        [PP + "Bias Level"] = "0",
        [PP + "Bias Position"] = "0",
    };

    public void Dispose()
    {
        WaveNumberL.PropertyChanged -= OnSummaryChanged;
        PartialLevel.PropertyChanged -= OnSummaryChanged;
        PartialPan.PropertyChanged -= OnSummaryChanged;
        TvfFilterType.PropertyChanged -= OnSummaryChanged;
        TvfCutoff.PropertyChanged -= OnSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
```

- [ ] **Step 2: Update the call site in `Src/ViewModels/PCMSynthToneEditorViewModel.cs`**

Find:

```csharp
        for (var i = 0; i < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; i++)
            Partials.Add(new PCMPartialViewModel(this, pmt, pmtByPath, i, _writer));
```

Replace with (re-add the partial domain as the 2nd argument):

```csharp
        for (var i = 0; i < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; i++)
            Partials.Add(new PCMPartialViewModel(this, domain.PCMSynthTonePartial(partNo, i),
                pmt, pmtByPath, i, _writer));
```

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: Build succeeded.

- [ ] **Step 4: Run tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: PASS (full suite green).

- [ ] **Step 5: Commit**

```
git add Src/ViewModels/PCMPartialViewModel.cs Src/ViewModels/PCMSynthToneEditorViewModel.cs
git commit -m "feat: PCM partial Sound params + card summaries + copy/paste/init"
```

---

## Task 3: Show wave/level/pan/filter on the rack cards

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml` (partial card `DataTemplate`)

Replace the Phase-1 stub line with the same summary rows the SN-S card shows.

- [ ] **Step 1: Replace the stub** — find this line inside the `DataTemplate x:DataType="vm:PCMPartialViewModel"` card (within the StackPanel, after the DockPanel):

```xml
                                <TextBlock Text="(sound controls in a later phase)" Opacity="0.5" FontSize="11"/>
```

Replace it with:

```xml
                                <TextBlock Text="{Binding WaveSummary}" Opacity="0.8" TextTrimming="CharacterEllipsis"/>
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Lvl" Opacity="0.6"/>
                                    <TextBlock Text="{Binding LevelLabel}"/>
                                    <TextBlock Text="Pan" Opacity="0.6"/>
                                    <TextBlock Text="{Binding PanLabel}"/>
                                </StackPanel>
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Filter" Opacity="0.6"/>
                                    <TextBlock Text="{Binding FilterSummary}"/>
                                </StackPanel>
```

- [ ] **Step 2: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: Build succeeded (compiled bindings resolve `WaveSummary`/`LevelLabel`/`PanLabel`/`FilterSummary` on `PCMPartialViewModel`).

- [ ] **Step 3: Run tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: PASS.

- [ ] **Step 4: Commit**

```
git add Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: rich PCM partial cards (wave / level / pan / filter)"
```

---

## Done criteria

- Full test suite green (Phase-1 187 + 9 new = 196).
- The PCM rack cards show the wave name, level, pan, and filter summary; they update live as the wave/level/pan/filter change.
- Copy partial → Paste partial round-trips all Sound params; Init partial resets filter/amp/envelopes to neutral (leaving the wave selection).
- The Sound *tab* body is still the Phase-1 stub — built in Phase 2b (wave picker, filter curve, amp, numeric envelopes), with the graphical multi-stage envelope in Phase 2c.
