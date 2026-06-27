# SuperNATURAL Acoustic (SN-A) friendly editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A friendly SN-Acoustic editor (Header + Tone/FX tabs) with a family→instrument picker, a dynamic per-instrument "modify params" grid, Character offsets + Vibrato, and the generalized MFX panel — built on a new reusable `DiscriminatedParamSection` component.

**Architecture:** Extract the MFX picker+grid pattern into a delegate-parameterized `DiscriminatedParamSectionViewModel`/`View` (discriminator picker + dynamic named-param grid + label cleanup). Add `InstrumentCatalog` (instrument families) and `ConditionalParamLabels` (the existing generic label cleanup, neutral home). SN-A's editor composes: header/character wrappers + the shared section (Instrument) + the generalized `MfxPanelViewModel`. The existing MFX panel is left untouched until an optional final de-dup task.

**Tech Stack:** Avalonia 12 (compiled bindings, `Dispatcher.UIThread`), ReactiveUI, .NET 10, NUnit 3. Build/test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `sna-editor` (created; spec committed).

Reference: spec `docs/superpowers/specs/2026-06-26-supernatural-acoustic-editor-design.md`. Mirror existing patterns: `SNSynthToneHeaderViewModel`, `SNSPartialViewModel` (the `PI`/`PS`/`PB` helper + `_wrappers`/`Track`/`Dispose`), `MfxPanelViewModel` (the picker+grid being extracted), `SNSynthToneEditorView.axaml` (Header + tabs), `MainWindow.axaml` SN-S Editor tab + `PartViewModel` SN-S editor wiring. Memory: `conditional-parameters-and-write-races`, `parameter-domain-infrastructure`, `no-hardcoded-colors-in-xaml`.

---

## File Structure

- Create `Src/Models/Services/ConditionalParamLabels.cs` — generic per-type label cleanup (moved from `MfxCatalog`).
- Create `Src/Models/Services/InstrumentCatalog.cs` — SN-A instrument families.
- Create `Tests/TestInstrumentCatalog.cs`, `Tests/TestConditionalParamLabels.cs`.
- Modify `Src/Models/Services/MfxCatalog.cs` — forward `FriendlyParamNames`/`FriendlyParamName` to `ConditionalParamLabels`.
- Create `Src/ViewModels/DiscriminatedParamSectionViewModel.cs` — shared picker+grid (+ `record DisplayParam`).
- Create `Src/Views/DiscriminatedParamSectionView.axaml(.cs)`.
- Create `Src/ViewModels/SNAcousticToneEditorViewModel.cs`.
- Create `Src/Views/SNAcousticToneEditorView.axaml(.cs)`.
- Modify `Src/ViewModels/PartViewModel.cs` — build/expose/dispose `SNAcousticToneEditor`.
- Modify `Src/Views/MainWindow.axaml` — add SN-A friendly "Editor" tab.
- (Optional, Task 9) Modify `Src/ViewModels/MfxPanelViewModel.cs` + `Src/Views/MfxPanelView.axaml` — delegate to the shared section.

---

## Task 1: `ConditionalParamLabels` (move generic label cleanup) + tests

**Files:** Create `Src/Models/Services/ConditionalParamLabels.cs`, `Tests/TestConditionalParamLabels.cs`; Modify `Src/Models/Services/MfxCatalog.cs`.

- [ ] **Step 1: Create `ConditionalParamLabels`** with the label logic currently in `MfxCatalog` (move `FriendlyParamNames`, `FriendlyParamName`, and the private `StripTyped`/`CommonWordPrefix`):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Generic friendly-label cleanup for "discriminated" parameters whose leaf name is prefixed with a
/// discriminator value (e.g. MFX "Equalizer Low Freq" or SN-A "ConcertGrand String Resonance").
/// Strips "&lt;valueName&gt; " when present; otherwise strips the longest common word-boundary prefix
/// shared by the set (handles cases where the param prefix differs from the display name, e.g.
/// "ConcertGrand" vs "INT 001: Concert Grand"). Never returns empty.
/// </summary>
public static class ConditionalParamLabels
{
    public static string FriendlyName(string valueName, string leafName)
        => StripTyped(valueName, leafName) ?? leafName;

    public static IReadOnlyList<string> FriendlyNames(string valueName, IReadOnlyList<string> leafNames)
    {
        if (leafNames.Count == 0) return leafNames;
        var common = CommonWordPrefix(leafNames);
        return leafNames.Select(n =>
        {
            var r = StripTyped(valueName, n)
                    ?? (common.Length > 0 && n.StartsWith(common, StringComparison.Ordinal)
                            ? n[common.Length..]
                            : n);
            r = r.Trim();
            return r.Length == 0 ? n : r;
        }).ToList();
    }

    private static string? StripTyped(string valueName, string leafName)
    {
        var typed = valueName + " ";
        return leafName.StartsWith(typed, StringComparison.Ordinal) ? leafName[typed.Length..] : null;
    }

    private static string CommonWordPrefix(IReadOnlyList<string> names)
    {
        var p = names[0];
        foreach (var n in names.Skip(1))
        {
            var k = 0;
            while (k < p.Length && k < n.Length && p[k] == n[k]) k++;
            p = p[..k];
            if (p.Length == 0) break;
        }
        var sp = p.LastIndexOf(' ');
        return sp < 0 ? "" : p[..(sp + 1)];
    }
}
```

- [ ] **Step 2: In `MfxCatalog.cs`, replace the bodies of `FriendlyParamNames`/`FriendlyParamName` with forwarders** (keep the public signatures so `MfxPanelViewModel` and `TestMfxCatalog` are unaffected), and delete the now-unused private `StripTyped`/`CommonWordPrefix`:

```csharp
    public static string FriendlyParamName(string effectTypeName, string leafName)
        => ConditionalParamLabels.FriendlyName(effectTypeName, leafName);

    public static IReadOnlyList<string> FriendlyParamNames(string effectTypeName, IReadOnlyList<string> leafNames)
        => ConditionalParamLabels.FriendlyNames(effectTypeName, leafNames);
```

- [ ] **Step 3: Write `Tests/TestConditionalParamLabels.cs`** — the existing MFX label cases plus the SN-A common-prefix case:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestConditionalParamLabels
{
    [Test]
    public void Strips_value_name_prefix()
    {
        var r = ConditionalParamLabels.FriendlyNames("Equalizer",
            new[] { "Equalizer Low Freq", "Equalizer Low Gain" });
        Assert.That(r, Is.EqualTo(new[] { "Low Freq", "Low Gain" }));
    }

    [Test]
    public void Common_prefix_when_value_name_differs_from_param_prefix()
    {
        // SN-A: display "INT 001: Concert Grand", but the param prefix is "ConcertGrand".
        var r = ConditionalParamLabels.FriendlyNames("INT 001: Concert Grand",
            new[] { "ConcertGrand String Resonance", "ConcertGrand Lid" });
        Assert.That(r, Is.EqualTo(new[] { "String Resonance", "Lid" }));
    }

    [Test]
    public void Single_is_never_empty()
    {
        Assert.That(ConditionalParamLabels.FriendlyName("Chorus", "Chorus"), Is.EqualTo("Chorus"));
    }
}
```

- [ ] **Step 4: Run tests** — `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`. Expected: new + existing `TestMfxCatalog` (forwarders) all green.

- [ ] **Step 5: Commit** — `git add Src/Models/Services/ConditionalParamLabels.cs Src/Models/Services/MfxCatalog.cs Tests/TestConditionalParamLabels.cs` then `git commit -m "refactor(sns): extract generic ConditionalParamLabels from MfxCatalog (+ tests)"`.

---

## Task 2: `InstrumentCatalog` + tests

**Files:** Create `Src/Models/Services/InstrumentCatalog.cs`, `Tests/TestInstrumentCatalog.cs`.

- [ ] **Step 1: Read the full `INSTRUMENT_VARIATIONS` list** in `Tools/ParameterBlobGenerator/ParameterDefinitions.cs` (starts ~line 1990). Note the total count and the family ordering (instruments are listed grouped by family).

- [ ] **Step 2: Write the failing test** `Tests/TestInstrumentCatalog.cs` (mirrors `TestMfxCatalog`; `N` = the total instrument count you found):

```csharp
using System.Linq;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestInstrumentCatalog
{
    [Test]
    public void Families_cover_every_instrument_index_exactly_once()
    {
        var all = InstrumentCatalog.Families.SelectMany(f => f.Indices).ToList();
        CollectionAssert.AreEquivalent(Enumerable.Range(0, InstrumentCatalog.Count), all);
        Assert.That(all.Count, Is.EqualTo(InstrumentCatalog.Count), "no duplicate indices");
    }

    [Test]
    public void FamilyOf_and_ValuesIn_round_trip()
    {
        for (var i = 0; i < InstrumentCatalog.Count; i++)
            Assert.That(InstrumentCatalog.ValuesIn(InstrumentCatalog.FamilyOf(i)), Does.Contain(i));
    }
}
```

- [ ] **Step 3: Implement `InstrumentCatalog`** (mirror `MfxCatalog`'s shape). Group ALL instrument indices into these families (in display order), using the family boundaries from the `INSTRUMENT_VARIATIONS` ordering you read in Step 1 — the test only enforces total coverage, so borderline instruments may go in the nearest sensible family:

  Families: **Pianos, E.Pianos, Clav, Mallets & Bells, Organ, Reeds & Accordion, Guitars, Basses, Strings & Orchestral, Choir, Brass, Sax, Woodwind, Ethnic, Expansion**.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Groups the SN-Acoustic INSTRUMENT_VARIATIONS into musical families for a two-step picker.
/// Indices are positions in the Instrument param's option list (= INSTRUMENT_VARIATIONS order).
/// </summary>
public static class InstrumentCatalog
{
    public sealed record Family(string Name, int[] Indices);

    // NOTE TO IMPLEMENTER: fill the index arrays from the actual INSTRUMENT_VARIATIONS ordering.
    // Use contiguous ranges per the list's grouping (e.g. Pianos = Enumerable.Range(0, 9).ToArray()).
    // The union MUST cover 0..Count-1 exactly once (enforced by the test).
    public static IReadOnlyList<Family> Families { get; } = new List<Family>
    {
        // e.g. new("Pianos", Enumerable.Range(0, 9).ToArray()), ...
    };

    public static int Count => Families.Sum(f => f.Indices.Length);

    public static string FamilyOf(int index)
        => Families.FirstOrDefault(f => f.Indices.Contains(index))?.Name ?? "";

    public static IReadOnlyList<int> ValuesIn(string family)
        => Families.FirstOrDefault(f => f.Name == family)?.Indices ?? Array.Empty<int>();
}
```

Cross-check `Count` against the instrument count and ensure the test passes (total coverage).

- [ ] **Step 4: Run tests** — all green. **Step 5: Commit** — `git add Src/Models/Services/InstrumentCatalog.cs Tests/TestInstrumentCatalog.cs` then `git commit -m "feat(sna): InstrumentCatalog — instrument families (+ tests)"`.

---

## Task 3: `DiscriminatedParamSectionViewModel` (shared picker + dynamic grid)

**Files:** Create `Src/ViewModels/DiscriminatedParamSectionViewModel.cs`.

This is the MFX picker+grid logic (`MfxPanelViewModel` lines for SelectedFamily/Type, OnTypeChanged, OnDiscriminatorChanged, RecomputeTypeParameters, the ctor discriminator wiring) generalized via delegates. Build-verified.

- [ ] **Step 1: Implement** (delegate-parameterized; no MFX/instrument specifics):

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One dynamic per-value parameter: the live FQP plus its cleaned label.</summary>
public sealed record DisplayParam(FullyQualifiedParameter Param, string Label);

/// <summary>
/// Reusable "discriminated parameter section": a family -> value two-combo picker over one discriminator
/// param, plus a grid of the value-specific named params (filtered by a path segment), rendered via
/// DataTemplateProvider.ParameterValueTemplate. Recomputes only on discriminator changes (UI-thread).
/// Used by the friendly MFX panel and the SN-A instrument detail. Catalog is supplied as delegates so
/// the engine catalogs (MfxCatalog / InstrumentCatalog) need no shared base type.
/// </summary>
public sealed class DiscriminatedParamSectionViewModel : ViewModelBase, IDisposable
{
    private readonly DomainBase _domain;
    private readonly List<FullyQualifiedParameter> _allParams;
    private readonly List<FullyQualifiedParameter> _discriminators;
    private readonly IDisposable _valueSub;
    private readonly string _gridPathSegment;
    private readonly IReadOnlyList<string> _families;
    private readonly Func<int, string> _familyOf;
    private readonly Func<string, IReadOnlyList<int>> _valuesIn;
    private readonly Func<string, IReadOnlyList<string>, IReadOnlyList<string>> _friendlyLabels;
    private bool _syncing;

    /// <summary>The discriminator wrapper (e.g. MFX Type / Instrument). Exposed for engine-specific extras.</summary>
    public ParamString Discriminator { get; }
    public IReadOnlyList<string> Families => _families;
    public ObservableCollection<DisplayParam> Params { get; } = [];
    public bool HasParams => Params.Count > 0;

    public DiscriminatedParamSectionViewModel(DomainBase domain, ThrottledParameterWriter writer,
        string discriminatorLeafName, string gridPathSegment,
        IReadOnlyList<string> families, Func<int, string> familyOf,
        Func<string, IReadOnlyList<int>> valuesIn,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>> friendlyLabels)
    {
        _domain = domain;
        _gridPathSegment = gridPathSegment;
        _families = families; _familyOf = familyOf; _valuesIn = valuesIn; _friendlyLabels = friendlyLabels;
        _allParams = domain.GetRelevantParameters(true, true);

        var disc = _allParams.First(p => p.ParSpec.Name == discriminatorLeafName);
        // Options from Repr (enum) or Discrete (e.g. Instrument list).
        var opts = disc.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                   ?? disc.ParSpec.Discrete?.Select(d => d.Item2).ToList()
                   ?? new List<string>();
        Discriminator = new ParamString(domain, disc, writer, opts);

        var parentPaths = _allParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        _discriminators = _allParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        _valueSub = Discriminator.WhenAnyValue(t => t.Value).Subscribe(SyncPickerFromValue);
        Recompute();
    }

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            ValuesInFamily = _valuesIn(value).Select(i => Discriminator.Options[i]).ToList();
            this.RaisePropertyChanged(nameof(ValuesInFamily));
            if (!_syncing && ValuesInFamily.Count > 0 && !ValuesInFamily.Contains(SelectedValue))
                SelectedValue = ValuesInFamily[0];
        }
    }

    private IReadOnlyList<string> _valuesInFamily = [];
    public IReadOnlyList<string> ValuesInFamily { get => _valuesInFamily; private set => _valuesInFamily = value; }

    private string _selectedValue = "";
    public string SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (value is null || _selectedValue == value) return;
            this.RaiseAndSetIfChanged(ref _selectedValue, value);
            if (!_syncing) Discriminator.Value = value;
        }
    }

    private void SyncPickerFromValue(string valueName)
    {
        _syncing = true;
        try
        {
            var idx = Discriminator.Options is { Count: > 0 } ? IndexOf(valueName) : -1;
            if (idx >= 0)
            {
                SelectedFamily = _familyOf(idx);
                if (!ValuesInFamily.Contains(valueName))
                {
                    ValuesInFamily = _valuesIn(SelectedFamily).Select(i => Discriminator.Options[i]).ToList();
                    this.RaisePropertyChanged(nameof(ValuesInFamily));
                }
                SelectedValue = valueName;
            }
        }
        finally { _syncing = false; }
    }

    private int IndexOf(string valueName)
    {
        for (var i = 0; i < Discriminator.Options.Count; i++)
            if (Discriminator.Options[i] == valueName) return i;
        return -1;
    }

    private void OnDiscriminatorChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(Recompute);
    }

    private void Recompute()
    {
        var valueName = Discriminator.Value;
        var relevant = _domain.GetRelevantParameters(false, false)
            .Where(p => p.ParSpec.Path.Contains(_gridPathSegment))
            .OrderBy(p => p.ParSpec.AddressInt).ToList();
        var labels = _friendlyLabels(valueName, relevant.Select(p => p.ParSpec.Name).ToList());
        Params.Clear();
        for (var i = 0; i < relevant.Count; i++) Params.Add(new DisplayParam(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasParams));
    }

    public void Dispose()
    {
        foreach (var d in _discriminators) d.PropertyChanged -= OnDiscriminatorChanged;
        _valueSub.Dispose();
        Discriminator.Dispose();
    }
}
```

- [ ] **Step 2: Build** — `Build succeeded. 0 Error(s)` (modulo exe-lock). **Step 3: Commit** — `git add Src/ViewModels/DiscriminatedParamSectionViewModel.cs` then `git commit -m "feat(sns): reusable DiscriminatedParamSectionViewModel (picker + dynamic grid)"`.

---

## Task 4: `DiscriminatedParamSectionView`

**Files:** Create `Src/Views/DiscriminatedParamSectionView.axaml(.cs)`.

- [ ] **Step 1:** Code-behind mirrors `MfxPanelView.axaml.cs` (`AvaloniaXamlLoader.Load(this)`).

- [ ] **Step 2:** View (`x:DataType="vm:DiscriminatedParamSectionViewModel"`, compiled bindings) — the family/value combos + the dynamic grid, mirroring the MFX grid markup:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:dataTemplates="using:Integra7AuralAlchemist.DataTemplates"
             x:DataType="vm:DiscriminatedParamSectionViewModel" x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.DiscriminatedParamSectionView">
    <StackPanel Spacing="10">
        <StackPanel Orientation="Horizontal" Spacing="16">
            <StackPanel Spacing="2">
                <TextBlock Text="Family"/>
                <ComboBox MinWidth="150" ItemsSource="{Binding Families}"
                          SelectedItem="{Binding SelectedFamily, Mode=TwoWay}"/>
            </StackPanel>
            <StackPanel Spacing="2">
                <TextBlock Text="Type"/>
                <ComboBox MinWidth="220" ItemsSource="{Binding ValuesInFamily}"
                          SelectedItem="{Binding SelectedValue, Mode=TwoWay}"/>
            </StackPanel>
        </StackPanel>
        <TextBlock Text="No extra parameters for this selection." Opacity="0.6"
                   IsVisible="{Binding !HasParams}"/>
        <ItemsControl ItemsSource="{Binding Params}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><WrapPanel Orientation="Horizontal"/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:DisplayParam">
                    <StackPanel Width="240" Spacing="2" Margin="0,0,12,10">
                        <TextBlock Text="{Binding Label}" TextWrapping="Wrap"/>
                        <ContentControl Content="{Binding Param}"
                                        ContentTemplate="{x:Static dataTemplates:DataTemplateProvider.ParameterValueTemplate}"/>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Build** clean. **Step 4: Commit** — `git add Src/Views/DiscriminatedParamSectionView.axaml Src/Views/DiscriminatedParamSectionView.axaml.cs` then `git commit -m "feat(sns): DiscriminatedParamSectionView"`.

---

## Task 5: `SNAcousticToneEditorViewModel`

**Files:** Create `Src/ViewModels/SNAcousticToneEditorViewModel.cs`.

Mirror `SNSPartialViewModel`'s wrapper idiom: a `PI(name,min,max)`/`PS(name[,opts])`/`PB(name)` helper over `byPath`, a `_wrappers` list + `Track`/`Dispose`. Build-verified.

- [ ] **Step 1: Implement.** Constructor `(Integra7Domain domain, int partNo, Action<string,int?>? navigateToRawTab = null)`:
  - `common = domain.SNAcousticToneCommon(partNo)`; `byPath = ToDict(common)` (copy `ToDict` from `SNSynthToneEditorViewModel`).
  - Build wrappers (exact names/ranges from the spec §2 / `ParameterDefinitions.cs`):
    - `ToneName = PS("Tone Name")` (display only), `ToneLevel = PI("Tone Level",0,127)`,
      `IsMono = PS("Mono-Poly")` (enum), `Portamento = PI("Portamento Time Offset",-64,63)` (confirm range in defs),
      `OctaveShift = PI("Octave Shift",-3,3)` (confirm), `Category = PS("Category")`.
    - Character: `CutoffOffset`,`ResonanceOffset`,`AttackOffset` (`"Attack Time Offset"`),`ReleaseOffset` (`"Release Time Offset"`) — confirm each `omin/omax` in defs and use them as `PI` min/max (bipolar). `VibratoRate`,`VibratoDepth` (confirm bipolar), `VibratoDelay`.
    - Phrase/TFX: `PhraseNumber = PS(...)` or `PI`, `PhraseOctaveShift = PI(...)`, `TfxSwitch = PB("TFX Switch")`.
    (For any param, READ its def line in `ParameterDefinitions.cs` to get the exact `omin/omax`/type; use `PS` for enum/discrete, `PI` for numeric, `PB` for OFF/ON.)
  - `Instrument = new DiscriminatedParamSectionViewModel(common, _writer, "Instrument", "/Modify Parameter ", InstrumentCatalog.Families.Select(f => f.Name).ToList(), InstrumentCatalog.FamilyOf, InstrumentCatalog.ValuesIn, ConditionalParamLabels.FriendlyNames)` — tracked for disposal.
  - `Mfx = new MfxPanelViewModel(domain.SNAcousticToneCommonMFX(partNo), _writer, () => navigateToRawTab?.Invoke("SN-A-MFX", null))` — tracked. (Use the existing raw SN-A MFX tab `Tag`; confirm/assign it in MainWindow.)
  - `[ReactiveCommand] AdvancedAcoustic()` is provided via the editor's nav delegate → `navigateToRawTab?.Invoke("SN-A", null)` (the raw SN-A Tone tab Tag). Expose as a `ReactiveCommand` or a delegate the view binds.
  - `Dispose()` disposes all tracked wrappers + `Instrument` + `Mfx` + `_writer`.

- [ ] **Step 2: Build** clean (resolve exact ranges/enums against `ParameterDefinitions.cs`). **Step 3: Commit** — `git add Src/ViewModels/SNAcousticToneEditorViewModel.cs` then `git commit -m "feat(sna): SNAcousticToneEditorViewModel (header/character + instrument section + MFX)"`.

---

## Task 6: `SNAcousticToneEditorView`

**Files:** Create `Src/Views/SNAcousticToneEditorView.axaml(.cs)`.

- [ ] **Step 1:** Mirror `SNSynthToneEditorView.axaml`: a `Grid RowDefinitions="Auto,*"`. Row 0 = Header `Border` (`SnPanelBackgroundBrush`) with Tone name/level/voice/octave/portamento/category. Row 1 = `TabControl`:
  - **Tone** tab → `ScrollViewer` → `StackPanel`: an **Instrument** `Border` hosting `<ContentControl Content="{Binding Instrument}"/>` (ViewLocator → `DiscriminatedParamSectionView`); a **Character** `Border` with bipolar sliders (the four offsets + Vibrato Rate/Depth/Delay), using the `sliderLabel` style; a **Phrase/TFX** `Border`; and an "Advanced acoustic parameters…" `Button` bound to the editor's advanced-nav command.
  - **FX** tab → `<ContentControl Content="{Binding Mfx}"/>`.
  - All colours via `{StaticResource}`; no inline hex.

- [ ] **Step 2: Build** clean (compiled bindings validate every path). **Step 3: Commit** — `git add Src/Views/SNAcousticToneEditorView.axaml Src/Views/SNAcousticToneEditorView.axaml.cs` then `git commit -m "feat(sna): SNAcousticToneEditorView (Header + Tone/FX tabs)"`.

---

## Task 7: Hosting (PartViewModel + MainWindow)

**Files:** Modify `Src/ViewModels/PartViewModel.cs`, `Src/Views/MainWindow.axaml`.

- [ ] **Step 1:** In `PartViewModel`, mirror the `SNSynthToneEditor` wiring: add `[Reactive] private SNAcousticToneEditorViewModel? _sNAcousticToneEditor;`, build it next to the SN-S one (`SNAcousticToneEditor = new SNAcousticToneEditorViewModel(_i7domain, PartNo, (tag, _) => { ToneTabKey = ""; ToneTabKey = tag; });` — reuse the repeat-click-safe pattern), and dispose/rebuild it the same way.

- [ ] **Step 2:** In `MainWindow.axaml`, add a friendly **"Editor"** `TabItem` for SN-A (mirroring the SN-S Editor tab), `IsVisible="{Binding SelectedPresetIsSNAcousticTone}"`, hosting `SNAcousticToneEditorView` bound to the part's `SNAcousticToneEditor`. Give the raw SN-A tabs stable `Tag`s if needed (`SN-A` for Tone, `SN-A-MFX` for MFX) so the "Advanced…" links resolve via `SelectTabByTag`.

- [ ] **Step 3: Build** clean. **Step 4: Commit** — `git add Src/ViewModels/PartViewModel.cs Src/Views/MainWindow.axaml` then `git commit -m "feat(sna): host the SN-Acoustic friendly editor"`.

---

## Task 8: Verification + smoke

- [ ] **Step 1:** Full build (`Build succeeded. 0 Error(s)`).
- [ ] **Step 2:** Full test run (existing + `TestInstrumentCatalog` + `TestConditionalParamLabels`; `TestMfxCatalog` still green). Use `--no-build` if the app is running.
- [ ] **Step 3: Hand the hardware smoke checklist to the user:**
  1. Select an SN-Acoustic preset → a friendly **Editor** tab with Header + **Tone**/**FX** sub-tabs.
  2. **Instrument** picker: choose a family then an instrument → the sound changes; the **instrument-detail grid** repopulates with that instrument's named params (cleaned labels).
  3. Edit a couple of detail params + the **Character** offsets (Brightness/Resonance/Attack/Release) + **Vibrato** → audible.
  4. **FX** tab: the MFX panel works against the SN-A MFX (pick an effect, edit params, sends).
  5. Change the instrument on the **hardware** → picker + grid follow; dragging a detail value doesn't reset the grid.
  6. "Advanced acoustic parameters…" opens the raw SN-A tab.
- [ ] **Step 4: Finish the branch** — once satisfied, superpowers:finishing-a-development-branch to merge `sna-editor` to `main`.

---

## Task 9 (OPTIONAL — confirm with user first): de-dup the MFX panel onto the shared section

Only after SN-A is validated. Refactor `MfxPanelViewModel` to embed a `DiscriminatedParamSectionViewModel` (`"MFX Type"`, `"/MFX Parameter "`, `MfxCatalog` delegates, `ConditionalParamLabels.FriendlyNames`) instead of its own copy of the picker+grid, keeping Bypass/sends/Advanced; update `MfxPanelView` to host `DiscriminatedParamSectionView`. **Regression:** the SN-S FX tab must behave identically (picker, nested delay ms/Note, bypass, sends, no mid-drag rebuild). Build + smoke. Commit separately.

---

## Self-Review notes (author)
- Spec coverage: Task 1 = §4a labels; Task 2 = §4a InstrumentCatalog; Tasks 3–4 = §4b; Tasks 5–6 = §4d/§4e; Task 7 = §4f; Task 8 = §6/§7; Task 9 = §4c (deferred/optional to protect the working MFX panel until SN-A proves the component).
- The shared section is delegate-parameterized (no `IDiscriminatorCatalog` base needed); `MfxCatalog`/`InstrumentCatalog` supply `Families`/`FamilyOf`/`ValuesIn` method groups; labels via `ConditionalParamLabels`.
- `ParamString` discriminator options come from `Repr` (MFX Type) or `Discrete` (Instrument).
- Recompute only on discriminator (parent-referenced) FQP changes, UI-thread marshalled — the hardened MFX behaviour (no mid-drag rebuild). Exact param ranges/enums for SN-A wrappers must be confirmed against `ParameterDefinitions.cs` during Task 5.
