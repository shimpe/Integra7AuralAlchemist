# PCM Synth Tone Editor — Phase 1 (Skeleton) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the friendly PCM Synth Tone editor shell — header (Common/Common 2), a 4-partial rack with Solo/Mute audition, an empty tab shell, a working FX tab, and full advanced-tab wiring — mirroring the SN-S/SN-A editors.

**Architecture:** A new `PCMSynthToneEditorViewModel` (built per-part by `PartViewModel`, bound in `MainWindow` to a friendly "Editor" tab tagged `PCMS`) composes a `PCMSynthToneHeaderViewModel`, an `ObservableCollection<PCMPartialViewModel>` rack, and the reused `MfxPanelViewModel`. The partial Solo/Mute overlay reuses the engine-agnostic `PartialAudition` helper. The raw PCM parameter tabs are renamed "Advanced — …" and re-tagged `PCM-SYN-*`, reached via "Advanced…" navigation callbacks (the established SN-S pattern). The Sound/Motion/Zones/Response tab bodies are stubs filled in later phases.

**Tech Stack:** Avalonia 12 + ReactiveUI (`[Reactive]`/`[ReactiveCommand]` source generators) + .NET 10, NUnit 3. Build with the user-local SDK.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`

> Note: an MSB3027/MSB3021 file-lock error means a previous app instance holds the exe — it is NOT a compile error. PowerShell CWD defaults to `Tools/ParameterBlobGenerator`; use the full `.sln` path above.

---

## File Structure

- **Create** `Src/ViewModels/PCMSynthToneHeaderViewModel.cs` — tone-wide header wrappers over `PCM Synth Tone Common/` + `PCM Synth Tone Common 2/`. One responsibility: header parameter state.
- **Create** `Src/ViewModels/PCMPartialViewModel.cs` — per-partial state (skeleton: identity, on/off via PMT switch, Solo/Mute, empty copy/paste buffer). Grows in Phases 2–3.
- **Create** `Src/ViewModels/PCMSynthToneEditorViewModel.cs` — composes header + 4 partials + MFX; owns audition orchestration, partial Copy/Paste/Init, and Advanced-navigation commands. Mutually dependent with `PCMPartialViewModel` (built/committed together in Task 3).
- **Create** `Src/Views/PCMSynthToneEditorView.axaml` (+ `.axaml.cs`) — header + rack + tab shell.
- **Modify** `Src/ViewModels/PartViewModel.cs` — add a `[Reactive] PCMSynthToneEditorViewModel?` field (near line 200) and construct it in `ResyncPartAsync` after the PCM caches are populated (after line 1358).
- **Modify** `Src/Views/MainWindow.axaml` — add the friendly "Editor" tab (`Tag="PCMS"`), re-tag the four raw PCM tabs to "Advanced — …" (`PCM-SYN-*`), and bind the Advanced — Partials inner `TabControl` `SelectedIndex` to `AdvancedPartialIndex`.
- **Modify** `Tests/TestPartialAudition.cs` — add 4-partial coverage the PCM editor relies on.

No `ViewLocator` change: `MainWindow` places the view explicitly via `<local:PCMSynthToneEditorView .../>` (same as SN-S/SN-A). No new colors: the view reuses the existing `Sn*` brushes from `App.axaml` (per the no-hardcoded-colors rule).

---

## Task 1: Guard the 4-partial audition contract

**Files:**
- Test: `Tests/TestPartialAudition.cs`

`PartialAudition.Effective`/`IsAuditioning` are length-generic, but every existing test uses 3-element arrays. PCM has 4 partials, so add explicit 4-partial coverage before relying on it.

- [ ] **Step 1: Add the failing-safe coverage tests**

Append these two tests inside the `TestPartialAudition` class (before the closing brace) in `Tests/TestPartialAudition.cs`:

```csharp
    [Test]
    public void Mute_silences_only_that_partial_with_four()
    {
        var saved = new[] { true, true, false, true };
        var solo = new[] { false, false, false, false };
        var mute = new[] { false, true, false, false };
        Assert.That(PartialAudition.Effective(saved, solo, mute),
            Is.EqualTo(new[] { true, false, false, true }));
    }

    [Test]
    public void Solo_isolates_among_four_partials()
    {
        var saved = new[] { true, true, true, true };
        var solo = new[] { false, false, true, false };
        var mute = new[] { false, false, false, false };
        Assert.That(PartialAudition.Effective(saved, solo, mute),
            Is.EqualTo(new[] { false, false, true, false }));
    }
```

- [ ] **Step 2: Run the tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: PASS (the helper is already length-generic; these guard the 4-partial case the PCM editor depends on). Total test count increases by 2.

- [ ] **Step 3: Commit**

```bash
git add Tests/TestPartialAudition.cs
git commit -m "test: cover 4-partial audition for the PCM editor"
```

---

## Task 2: PCMSynthToneHeaderViewModel

**Files:**
- Create: `Src/ViewModels/PCMSynthToneHeaderViewModel.cs`

Header wrappers over `PCM Synth Tone Common/` (level, pan, voice, glide, tune, octave, bend, character offsets) and `PCM Synth Tone Common 2/` (category, phrase, TFX). Ranges below are the verified display (`omin..omax`) ranges from `ParameterDefinitions.cs` — note PCM offsets are −63..63 (not −64) and pitch-bend goes to 48.

- [ ] **Step 1: Create the file**

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

/// <summary>Tone-wide (Common + Common 2) controls shown in the PCM Synth editor header.</summary>
public sealed class PCMSynthToneHeaderViewModel : ViewModelBase, IDisposable
{
    private const string CP = "PCM Synth Tone Common/";
    private const string C2 = "PCM Synth Tone Common 2/";

    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _toneName;

    public ParamInt ToneLevel { get; }
    public ParamInt Pan { get; }
    public ParamString MonoPoly { get; }     // Mono / Poly (enum)
    public ParamBool Legato { get; }
    public ParamBool Portamento { get; }
    public ParamInt PortamentoTime { get; }
    public ParamInt AnalogFeel { get; }
    public ParamInt OctaveShift { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamInt PitchBendUp { get; }
    public ParamInt PitchBendDown { get; }

    // Character (tone-wide offsets)
    public ParamInt CutoffOffset { get; }
    public ParamInt ResonanceOffset { get; }
    public ParamInt AttackOffset { get; }
    public ParamInt ReleaseOffset { get; }

    // Common 2
    public ParamString Category { get; }
    public ParamString PhraseNumber { get; }
    public ParamInt PhraseOctaveShift { get; }
    public ParamBool TfxSwitch { get; }

    public PCMSynthToneHeaderViewModel(DomainBase common,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath,
        DomainBase common2, IReadOnlyDictionary<string, FullyQualifiedParameter> by2,
        ThrottledParameterWriter writer)
    {
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], writer, min, max));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], writer));
        ParamString PS(string n) => Track(new ParamString(common, byPath[CP + n], writer));
        ParamInt PI2(string n, int min, int max) => Track(new ParamInt(common2, by2[C2 + n], writer, min, max));
        ParamBool PB2(string n) => Track(new ParamBool(common2, by2[C2 + n], writer));
        ParamString PS2(string n) => Track(new ParamString(common2, by2[C2 + n], writer));

        ToneLevel = PI("PCM Synth Tone Level", 0, 127);
        Pan = PI("PCM Synth Tone Pan", -64, 63);
        MonoPoly = PS("Mono-Poly");
        Legato = PB("Legato Switch");
        Portamento = PB("Portamento Switch");
        PortamentoTime = PI("Portamento Time", 0, 127);
        AnalogFeel = PI("Analog Feel", 0, 127);
        OctaveShift = PI("Octave Shift", -3, 3);
        CoarseTune = PI("PCM Synth Tone Coarse Tune", -48, 48);
        FineTune = PI("PCM Synth Tone Fine Tune", -50, 50);
        PitchBendUp = PI("Pitch Bend Range Up", 0, 48);
        PitchBendDown = PI("Pitch Bend Range Down", 0, 48);

        CutoffOffset = PI("Cutoff Offset", -63, 63);
        ResonanceOffset = PI("Resonance Offset", -63, 63);
        AttackOffset = PI("Attack Time Offset", -63, 63);
        ReleaseOffset = PI("Release Time Offset", -63, 63);

        Category = PS2("Tone Category");
        PhraseNumber = PS2("Phrase Number");
        PhraseOctaveShift = PI2("Phrase Octave Shift", -3, 3);
        TfxSwitch = PB2("TFX Switch");

        _toneName = byPath[CP + "PCM Synth Tone Name"];
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

- [ ] **Step 2: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: Build succeeded (the type compiles; not yet referenced).

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PCMSynthToneHeaderViewModel.cs
git commit -m "feat: PCM Synth editor header view-model"
```

---

## Task 3: PCMSynthToneEditorViewModel + PCMPartialViewModel

**Files:**
- Create: `Src/ViewModels/PCMPartialViewModel.cs`
- Create: `Src/ViewModels/PCMSynthToneEditorViewModel.cs`

These two types reference each other (the partial holds its parent editor; the editor holds a collection of partials), so they are written and built together. The partial is a skeleton: identity, on/off (the PMT per-partial switch), Solo/Mute, and an empty copy buffer that later phases populate.

- [ ] **Step 1: Create `Src/ViewModels/PCMPartialViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly editor state for one PCM Synth partial. Phase 1 skeleton: identity, on/off
/// (the PMT per-partial switch), and the Solo/Mute audition flags. Sound/Motion params are added
/// in later phases; <see cref="_editable"/> (copy/paste/init buffer) grows with them.</summary>
public sealed class PCMPartialViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";

    private readonly PCMSynthToneEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }            // 0..3
    public string Title => $"Partial {Index + 1}";

    /// <summary>Card on/off — the PMT per-partial switch (audition saves/restores this).</summary>
    public ParamBool IsOn { get; }

    // Params copied/pasted/initialised (empty until later phases add Sound/Motion controls).
    private readonly IReadOnlyList<IParam> _editable = Array.Empty<IParam>();

    public PCMPartialViewModel(PCMSynthToneEditorViewModel parent, DomainBase partialDomain,
        DomainBase pmtDomain, IReadOnlyDictionary<string, FullyQualifiedParameter> pmtByPath,
        int index, ThrottledParameterWriter writer)
    {
        _parent = parent;
        Index = index;
        IsOn = Track(new ParamBool(pmtDomain, pmtByPath[PMT + $"PMT {index + 1} Partial Switch"], writer));
    }

    private T Track<T>(T wrapper) where T : IDisposable { _wrappers.Add(wrapper); return wrapper; }

    // --- Audition (transient solo/mute; coordinated by the parent editor VM, not sent as params) ---
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

    // --- Copy / Paste / Init (edit-buffer only; no save). No-op until _editable is populated. ---
    public void Copy() => _parent.PartialClipboard = SnsPartialClipboard.Snapshot(_editable);
    public void Paste() { if (_parent.PartialClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    public void Init() { }

    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
    }
}
```

- [ ] **Step 2: Create `Src/ViewModels/PCMSynthToneEditorViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly PCM Synth editor for ONE part: header (Common/Common 2), a 4-partial rack with
/// Solo/Mute audition, and the shared MFX panel. Sound/Motion/Zones/Response tab bodies are filled
/// in later phases.</summary>
public sealed partial class PCMSynthToneEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;

    public PCMSynthToneHeaderViewModel Header { get; }
    public ObservableCollection<PCMPartialViewModel> Partials { get; } = [];
    public MfxPanelViewModel Mfx { get; }

    /// <summary>Shared partial copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? PartialClipboard { get; set; }

    public PCMSynthToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var common = domain.PCMSynthToneCommon(partNo);
        var commonByPath = ToDict(common);
        var common2 = domain.PCMSynthToneCommon2(partNo);
        var common2ByPath = ToDict(common2);
        var pmt = domain.PCMSynthTonePMT(partNo);
        var pmtByPath = ToDict(pmt);

        Header = new PCMSynthToneHeaderViewModel(common, commonByPath, common2, common2ByPath, _writer);

        for (var i = 0; i < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; i++)
            Partials.Add(new PCMPartialViewModel(this, domain.PCMSynthTonePartial(partNo, i),
                pmt, pmtByPath, i, _writer));

        _selectedPartial = Partials[0];

        Mfx = new MfxPanelViewModel(domain.PCMSynthToneCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCM-SYN-MFX", null));
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private PCMPartialViewModel _selectedPartial;
    public PCMPartialViewModel SelectedPartial
    {
        get => _selectedPartial;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedPartial)) return;
            this.RaiseAndSetIfChanged(ref _selectedPartial, value);
        }
    }

    [ReactiveCommand] public void CopyPartial() => SelectedPartial.Copy();
    [ReactiveCommand] public void PastePartial() => SelectedPartial.Paste();
    [ReactiveCommand] public void InitPartial() => SelectedPartial.Init();

    // Open the raw "Advanced — …" tabs (the friendly Editor tab owns Tag "PCMS").
    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("PCM-SYN-COMMON", null);
    [ReactiveCommand] public void AdvancedPartial() => _navigateToRawTab?.Invoke("PCM-SYN-PARTIALS", SelectedPartial.Index);

    // --- Partial Solo/Mute audition (reuses the engine-agnostic PartialAudition helper) ---
    private readonly bool[] _savedSwitches = new bool[Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE];
    private bool _auditing;
    private bool _suppressRecompute;

    private bool _isAuditioning;
    /// <summary>True while any partial is soloed or muted (drives the banner + disables card on/off).</summary>
    public bool IsAuditioning
    {
        get => _isAuditioning;
        private set => this.RaiseAndSetIfChanged(ref _isAuditioning, value);
    }

    /// <summary>Recompute effective partial on/off from the solo/mute flags. Snapshots the real
    /// switches when an audition begins and restores them when it ends (safe audition).</summary>
    public void RecomputeAudition()
    {
        if (_suppressRecompute) return;

        var solo = Partials.Select(p => p.Solo).ToList();
        var mute = Partials.Select(p => p.Mute).ToList();
        var active = PartialAudition.IsAuditioning(solo, mute);

        if (active && !_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) _savedSwitches[i] = Partials[i].IsOn.Value;
            _auditing = true;
        }

        if (active)
        {
            var saved = new bool[Partials.Count];
            for (var i = 0; i < Partials.Count; i++) saved[i] = _savedSwitches[i];
            var eff = PartialAudition.Effective(saved, solo, mute);
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = eff[i];
        }
        else if (_auditing)
        {
            for (var i = 0; i < Partials.Count; i++) Partials[i].IsOn.Value = _savedSwitches[i];
            _auditing = false;
        }

        IsAuditioning = active;
    }

    /// <summary>Clear all solo/mute and restore the saved switches (single recompute).</summary>
    [ReactiveCommand]
    public void ClearAudition()
    {
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        _suppressRecompute = false;
        RecomputeAudition();
    }

    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
}
```

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: Build succeeded. (Source generators produce `CopyPartialCommand`, `ClearAuditionCommand`, `AdvancedCommonCommand`, etc.)

- [ ] **Step 4: Run tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: PASS (full suite green).

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/PCMPartialViewModel.cs Src/ViewModels/PCMSynthToneEditorViewModel.cs
git commit -m "feat: PCM Synth editor + partial view-models (skeleton)"
```

---

## Task 4: PCMSynthToneEditorView

**Files:**
- Create: `Src/Views/PCMSynthToneEditorView.axaml`
- Create: `Src/Views/PCMSynthToneEditorView.axaml.cs`

Header (mirrors SN-S/SN-A), a 4-card partial rack with audition banner, and the right-pane `TabControl` (`Sound · Motion · Zones · FX · Response`). FX shows the reused MFX panel; the other bodies are labelled stubs for later phases. Reuses the `Sn*` brushes — no hardcoded colors.

- [ ] **Step 1: Create `Src/Views/PCMSynthToneEditorView.axaml.cs`**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class PCMSynthToneEditorView : UserControl
{
    public PCMSynthToneEditorView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Create `Src/Views/PCMSynthToneEditorView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             x:DataType="vm:PCMSynthToneEditorViewModel"
             x:CompileBindings="True"
             x:Name="Root"
             x:Class="Integra7AuralAlchemist.Views.PCMSynthToneEditorView">

    <UserControl.Styles>
        <Style Selector="TextBlock.sliderLabel">
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="MinHeight" Value="40"/>
        </Style>
        <Style Selector="ToggleButton.solo:checked">
            <Setter Property="Background" Value="{StaticResource SnSoloBrush}"/>
        </Style>
        <Style Selector="ToggleButton.mute:checked">
            <Setter Property="Background" Value="{StaticResource SnMuteBrush}"/>
        </Style>
    </UserControl.Styles>

    <Grid RowDefinitions="Auto,*" Margin="8">

        <!-- ===== Tone Header ===== -->
        <Border Grid.Row="0" Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10" Margin="0,0,0,8">
            <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
            <StackPanel Spacing="8">
              <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <Border Background="{StaticResource SnAccentBrush}" CornerRadius="4" Padding="6,2" HorizontalAlignment="Left">
                        <TextBlock Text="PCM Synth" Foreground="{StaticResource SnBadgeForegroundBrush}" FontSize="11"/>
                    </Border>
                    <TextBlock Text="{Binding Header.ToneName}" FontSize="16" FontWeight="Bold"/>
                </StackPanel>

                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Tone Level"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.ToneLevel.Value, Mode=TwoWay}"/>
                </StackPanel>

                <StackPanel Spacing="2">
                    <TextBlock Text="Voice" ToolTip.Tip="Mono-Poly"/>
                    <ComboBox ItemsSource="{Binding Header.MonoPoly.Options}"
                              SelectedItem="{Binding Header.MonoPoly.Value, Mode=TwoWay}"/>
                    <ToggleSwitch OnContent="Legato" OffContent="Legato"
                                  IsChecked="{Binding Header.Legato.Value, Mode=TwoWay}"/>
                </StackPanel>

                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Glide" ToolTip.Tip="Portamento"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding Header.Portamento.Value, Mode=TwoWay}"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.PortamentoTime.Value, Mode=TwoWay}"
                            IsEnabled="{Binding Header.Portamento.Value}"/>
                </StackPanel>

                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Vintage Drift" ToolTip.Tip="Analog Feel"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding Header.AnalogFeel.Value, Mode=TwoWay}"/>
                </StackPanel>

                <StackPanel Spacing="2">
                    <TextBlock Text="Octave" ToolTip.Tip="Octave Shift"/>
                    <NumericUpDown Width="100" Minimum="-3" Maximum="3" Increment="1" FormatString="0"
                                   Value="{Binding Header.OctaveShift.Value, Mode=TwoWay}"/>
                </StackPanel>

                <Button VerticalAlignment="Bottom" Content="Advanced common parameters…"
                        Command="{Binding AdvancedCommonCommand}"/>
              </StackPanel>

              <!-- Character (tone-wide offsets) -->
              <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Brightness (Cutoff)" Classes="sliderLabel" ToolTip.Tip="Cutoff Offset"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding Header.CutoffOffset.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Resonance" Classes="sliderLabel" ToolTip.Tip="Resonance Offset"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding Header.ResonanceOffset.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Attack" Classes="sliderLabel" ToolTip.Tip="Attack Time Offset"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding Header.AttackOffset.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="150">
                    <TextBlock Text="Release" Classes="sliderLabel" ToolTip.Tip="Release Time Offset"/>
                    <Slider Minimum="-63" Maximum="63" Value="{Binding Header.ReleaseOffset.Value, Mode=TwoWay}"/>
                </StackPanel>
              </StackPanel>

              <!-- Phrase (arpeggio) on its own row -->
              <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2" Width="260">
                    <TextBlock Text="Phrase (arpeggio)" ToolTip.Tip="Phrase Number"/>
                    <ComboBox MaxDropDownHeight="300" HorizontalAlignment="Stretch"
                              ItemsSource="{Binding Header.PhraseNumber.Options}"
                              SelectedItem="{Binding Header.PhraseNumber.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Phrase Octave" ToolTip.Tip="Phrase Octave Shift"/>
                    <NumericUpDown Width="110" Minimum="-3" Maximum="3" Increment="1" FormatString="0"
                                   Value="{Binding Header.PhraseOctaveShift.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Category" ToolTip.Tip="Tone Category"/>
                    <ComboBox MaxDropDownHeight="300"
                              ItemsSource="{Binding Header.Category.Options}"
                              SelectedItem="{Binding Header.Category.Value, Mode=TwoWay}"/>
                </StackPanel>
              </StackPanel>
            </StackPanel>
            </ScrollViewer>
        </Border>

        <!-- ===== Rack + Selected partial ===== -->
        <Grid Grid.Row="1" ColumnDefinitions="240,*">

            <Grid Grid.Column="0" Margin="0,0,8,0" RowDefinitions="Auto,*">
              <Border Grid.Row="0" IsVisible="{Binding IsAuditioning}" Margin="0,0,0,8"
                      Background="{StaticResource SnAuditionBannerBrush}" CornerRadius="6" Padding="8">
                  <StackPanel Spacing="6">
                      <TextBlock Text="Auditioning (solo/mute) — restore before saving"
                                 TextWrapping="Wrap" FontSize="12"/>
                      <Button Content="Restore" HorizontalAlignment="Left" Command="{Binding ClearAuditionCommand}"/>
                  </StackPanel>
              </Border>
              <ListBox Grid.Row="1"
                     ItemsSource="{Binding Partials}"
                     SelectedItem="{Binding SelectedPartial, Mode=TwoWay}">
                <ListBox.Styles>
                    <Style Selector="ListBoxItem">
                        <Setter Property="Padding" Value="2"/>
                    </Style>
                    <Style Selector="ListBoxItem:selected /template/ ContentPresenter">
                        <Setter Property="Background" Value="Transparent"/>
                    </Style>
                    <Style Selector="ListBoxItem:pointerover /template/ ContentPresenter">
                        <Setter Property="Background" Value="Transparent"/>
                    </Style>
                    <Style Selector="ListBoxItem Border#CardRoot">
                        <Setter Property="Background" Value="{StaticResource SnCardBackgroundBrush}"/>
                        <Setter Property="BorderThickness" Value="2"/>
                        <Setter Property="BorderBrush" Value="Transparent"/>
                    </Style>
                    <Style Selector="ListBoxItem:pointerover Border#CardRoot">
                        <Setter Property="Background" Value="{StaticResource SnCardHoverBackgroundBrush}"/>
                    </Style>
                    <Style Selector="ListBoxItem:selected Border#CardRoot">
                        <Setter Property="Background" Value="{StaticResource SnCardSelectedBackgroundBrush}"/>
                        <Setter Property="BorderBrush" Value="{StaticResource SnCardSelectedBorderBrush}"/>
                    </Style>
                </ListBox.Styles>
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="vm:PCMPartialViewModel">
                        <Border x:Name="CardRoot" Padding="8" CornerRadius="6">
                            <StackPanel Spacing="4">
                                <DockPanel>
                                    <TextBlock Text="{Binding Title}" FontWeight="Bold" VerticalAlignment="Center"/>
                                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="4"
                                                VerticalAlignment="Center">
                                        <ToggleButton Classes="solo" Content="S" MinWidth="26" Width="26" Padding="0"
                                                      FontSize="11" IsChecked="{Binding Solo, Mode=TwoWay}"
                                                      ToolTip.Tip="Solo (audition)"/>
                                        <ToggleButton Classes="mute" Content="M" MinWidth="26" Width="26" Padding="0"
                                                      FontSize="11" IsChecked="{Binding Mute, Mode=TwoWay}"
                                                      ToolTip.Tip="Mute (audition)"/>
                                        <ToggleSwitch OnContent="" OffContent=""
                                                      IsChecked="{Binding IsOn.Value, Mode=TwoWay}"
                                                      IsEnabled="{Binding !#Root.((vm:PCMSynthToneEditorViewModel)DataContext).IsAuditioning}"
                                                      ToolTip.Tip="Partial on/off (disabled while auditioning)"/>
                                    </StackPanel>
                                </DockPanel>
                                <TextBlock Text="(sound controls in a later phase)" Opacity="0.5" FontSize="11"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            </Grid>

            <!-- Right column: tab shell + Copy/Paste/Init -->
            <Grid Grid.Column="1" RowDefinitions="*,Auto">
                <TabControl Grid.Row="0">
                    <TabItem Header="Sound">
                        <TextBlock Margin="12" Opacity="0.6" Text="Sound — wave, filter, amp, envelopes (Phase 2)."/>
                    </TabItem>
                    <TabItem Header="Motion">
                        <TextBlock Margin="12" Opacity="0.6" Text="Motion — LFO 1 + LFO 2 (Phase 3)."/>
                    </TabItem>
                    <TabItem Header="Zones">
                        <TextBlock Margin="12" Opacity="0.6" Text="Zones — PMT key/velocity map (Phase 4)."/>
                    </TabItem>
                    <TabItem Header="FX">
                        <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                            <ContentControl Content="{Binding Mfx}" Margin="0,8,0,0"/>
                        </ScrollViewer>
                    </TabItem>
                    <TabItem Header="Response">
                        <TextBlock Margin="12" Opacity="0.6" Text="Response — velocity / aftertouch / keyfollow (Phase 5)."/>
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

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: Build succeeded (XAML compiles against the VM; view not yet shown anywhere).

- [ ] **Step 4: Commit**

```bash
git add Src/Views/PCMSynthToneEditorView.axaml Src/Views/PCMSynthToneEditorView.axaml.cs
git commit -m "feat: PCM Synth editor view (header + rack + tab shell)"
```

---

## Task 5: Wire the editor into PartViewModel

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs` (field near line 200; construction after line 1358)

- [ ] **Step 1: Add the reactive field**

In `Src/ViewModels/PartViewModel.cs`, find (≈ line 200):

```csharp
    [Reactive] private SNSynthToneEditorViewModel? _sNSynthToneEditor;
    [Reactive] private SNAcousticToneEditorViewModel? _sNAcousticToneEditor;
```

Add a third line directly below them:

```csharp
    [Reactive] private SNSynthToneEditorViewModel? _sNSynthToneEditor;
    [Reactive] private SNAcousticToneEditorViewModel? _sNAcousticToneEditor;
    [Reactive] private PCMSynthToneEditorViewModel? _pcmSynthToneEditor;
```

- [ ] **Step 2: Construct it where the PCM caches are populated**

In `ResyncPartAsync`, find the block that ends at line 1358:

```csharp
            List<FullyQualifiedParameter>
                p_pcmpmt = _i7domain.PCMSynthTonePMT(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMSynthTonePMTParameters.AddOrUpdate(p_pcmpmt);
```

Immediately after that line (before the `p_pcmdkc` PCM drum-kit block), insert:

```csharp

            // Friendly PCM Synth editor for this part. Binds to the same live PCM FQP instances
            // populated above, so it tracks preset/hardware changes for free. The navigation callback
            // selects the matching raw "Advanced" tab (clear-then-set so repeat navigations always fire
            // SelectTabByTag) and carries the selected partial for "Advanced — Partials".
            _pcmSynthToneEditor?.Dispose();
            PCMSynthToneEditor = new PCMSynthToneEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            });
```

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: Build succeeded. The source generator exposes the `PCMSynthToneEditor` property from the `_pcmSynthToneEditor` field.

- [ ] **Step 4: Run tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: PASS (full suite green).

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "feat: build the friendly PCM Synth editor per part"
```

---

## Task 6: MainWindow tabs — friendly Editor + re-tagged Advanced tabs

**Files:**
- Modify: `Src/Views/MainWindow.axaml` (PCM Synth tab group, ≈ lines 242–266 and the "Partials" tab ≈ line 349)

The raw PCM "Tone" tab currently owns `Tag="PCMS"` — the value `ToneTabKey` is set to on preset load (`ToneTypeStr == "PCMS"`), so it is the default tab. Move that tag to the new friendly Editor tab (making it the default) and re-tag the raw tabs `PCM-SYN-*`, matching the navigation strings used by the editor VM.

- [ ] **Step 1: Add the friendly Editor tab and re-tag the four raw tabs**

Replace this block (≈ lines 242–266):

```xml
                                            <TabItem Header="Tone"
                                                     Tag="PCMS"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommonParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommon, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Tone extra"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommon2Parameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommon2, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="MFX"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="PMT"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthTonePMTParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthTonePMT, Mode=TwoWay}" />
                                            </TabItem>
```

with:

```xml
                                            <TabItem Header="Editor"
                                                     Tag="PCMS"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:PCMSynthToneEditorView DataContext="{Binding PCMSynthToneEditor}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Common"
                                                     Tag="PCM-SYN-COMMON"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommonParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommon, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Common 2"
                                                     Tag="PCM-SYN-COMMON2"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommon2Parameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommon2, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — MFX"
                                                     Tag="PCM-SYN-MFX"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthToneCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthToneCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — PMT"
                                                     Tag="PCM-SYN-PMT"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMSynthTonePMTParameters}"
                                                    SearchText="{Binding SearchTextPCMSynthTonePMT, Mode=TwoWay}" />
                                            </TabItem>
```

- [ ] **Step 2: Tag the PCM "Partials" tab and bind its selected partial**

Find (≈ line 349):

```xml
                                            <TabItem Header="Partials"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <TabControl
                                                    ItemsSource="{Binding PcmSynthTonePartialViewModels}"
```

Change the `TabItem` header/tag and add `SelectedIndex` to its inner `TabControl`:

```xml
                                            <TabItem Header="Advanced — Partials"
                                                     Tag="PCM-SYN-PARTIALS"
                                                     IsVisible="{Binding SelectedPresetIsPCMSynthTone}">
                                                <TabControl
                                                    SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                    ItemsSource="{Binding PcmSynthTonePartialViewModels}"
```

(Leave the rest of that `TabControl` — `ItemTemplate`, `ContentTemplate`, etc. — unchanged.)

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Run tests**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo`
Expected: PASS (full suite green).

- [ ] **Step 5: Manual verification (hardware/app)**

Launch the app, select a PCM Synth Tone preset, and confirm:
- The **Editor** tab is shown by default (not a raw tab).
- The header shows the tone name/level/voice/glide/character/phrase, and "Advanced common parameters…" switches to the **Advanced — Common** tab.
- The rack lists **Partial 1–4**; toggling Solo/Mute shows the audition banner and "Restore" clears it.
- The **FX** tab shows the MFX panel.

- [ ] **Step 6: Commit**

```bash
git add Src/Views/MainWindow.axaml
git commit -m "feat: friendly PCM Synth Editor tab + Advanced — tabs"
```

---

## Done criteria

- Full test suite green (185 existing + 2 new = 187).
- Selecting a PCM Synth preset opens the friendly Editor by default; header, 4-partial rack with audition, FX panel, and Advanced-tab navigation all work.
- Sound/Motion/Zones/Response tab bodies are labelled stubs (filled in Phases 2–5).
