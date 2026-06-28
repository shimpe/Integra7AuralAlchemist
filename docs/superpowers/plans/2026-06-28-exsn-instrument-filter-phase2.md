# Phase 2 — Filter SN-Acoustic Instruments to Loaded ExSN — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the friendly SN-Acoustic editor, show only instruments from currently-loaded ExSN1–5 boards in the "Expansion" family (hide it when none loaded; keep the patch's current instrument visible, labelled "(not loaded)"; refresh live on Load SRX).

**Architecture:** Extend `LoadedSrxState` with loaded ExSN boards + a `Changed` event. Add a pure, unit-tested `ExSnInstrumentFilter`. Make the shared two-step picker VM (`DiscriminatedParamSectionViewModel`, used by SN-A and the friendly MFX panel) loaded-aware via **additive, optional, default-identity** hooks (a families supplier, a display-name transform + reverse, a generic keep-current-visible step, and a `Reproject()` method) — so the MFX panel is untouched. The SN-A editor supplies loaded-aware delegates and calls `Reproject()` on `LoadedSrxState.Changed`.

**Tech Stack:** Avalonia 12 + ReactiveUI; C# / .NET 10; NUnit 3. Reuses Phase 1's `LoadedSrxState` and `SrxGroupIdResolution.NotLoadedSuffix`.

**Build/test commands** (user-local .NET 10 SDK; the system `dotnet` is too old):

- Build: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Full tests: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Filtered tests: append `--filter "FullyQualifiedName~ClassName"`

Baseline before starting: the suite passes with **274** tests. Run the full test command once to confirm.

**Standing constraints:** never `git --no-verify`; this work is on branch `exsn-instrument-filter` (already created off `main`). Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Transient `Permission denied` on `.git/objects` (Windows AV) — retry the commit once. There is an unrelated uncommitted change to `Src/Models/Services/AsyncMidiInputWrapper.cs` in the working tree — **do NOT stage or commit it**; use explicit file paths in every `git add`.

---

## File Structure

- **Modify** `Src/Models/Services/LoadedSrxState.cs` — add `ExSnBoards` + a `Changed` event (one responsibility: map the 4 slots to loaded board sets and notify).
- **Create** `Src/Models/Services/ExSnInstrumentFilter.cs` — pure ExSN-instrument filtering logic.
- **Modify** `Src/ViewModels/DiscriminatedParamSectionViewModel.cs` — additive hooks: families supplier, display transforms, keep-current, `Reproject()`.
- **Modify** `Src/ViewModels/SNAcousticToneEditorViewModel.cs` — supply loaded-aware delegates + hooks; subscribe to `LoadedSrxState.Changed`.
- **Modify** `Tests/TestLoadedSrxState.cs`, **Create** `Tests/TestExSnInstrumentFilter.cs` — unit tests for the pure pieces.

**No change:** `MfxPanelViewModel` (the new picker-VM params are optional; MFX passes none → identity behavior).

---

## Task 1: `LoadedSrxState` — loaded ExSN boards + `Changed` event

**Files:**
- Modify: `Src/Models/Services/LoadedSrxState.cs`
- Test: `Tests/TestLoadedSrxState.cs`

- [ ] **Step 1: Add the failing tests**

Append these three tests inside the `TestLoadedSrxState` class in `Tests/TestLoadedSrxState.cs` (before the closing `}`):

```csharp
    [Test]
    public void ExSnBoards_MapsSlots13to18_ToBoards1to6()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(13, 17, 0, 2); // 13->ExSN1, 17->ExSN5; 0 empty, 2 is SRX (ignored here)
        Assert.That(s.ExSnBoards, Is.EquivalentTo(new[] { 1, 5 }));
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 2 })); // SRX side still correct
    }

    [Test]
    public void ExSnBoards_Distinct()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(14, 14, 14, 14);
        Assert.That(s.ExSnBoards, Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    public void Changed_FiresOnSetFromSlots()
    {
        var s = new LoadedSrxState();
        var fired = 0;
        s.Changed += () => fired++;
        s.SetFromSlots(13, 0, 0, 0);
        Assert.That(fired, Is.EqualTo(1));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release --filter "FullyQualifiedName~TestLoadedSrxState"`
Expected: FAIL — compile error (`ExSnBoards` and `Changed` do not exist).

- [ ] **Step 3: Implement**

In `Src/Models/Services/LoadedSrxState.cs`, replace the body of the class (the `_boards` field through the `SetFromSlots` method) — i.e. replace:

```csharp
    private int[] _boards = Array.Empty<int>();

    /// <summary>The loaded SRX board numbers (1..12), distinct. Empty when none loaded.</summary>
    public IReadOnlyCollection<int> Boards => _boards;

    /// <summary>Recompute the loaded set from the 4 raw slot values (keeps only 1..12, deduped).</summary>
    public void SetFromSlots(int slot1, int slot2, int slot3, int slot4)
        => _boards = new[] { slot1, slot2, slot3, slot4 }
            .Where(v => v is >= 1 and <= 12)
            .Distinct()
            .ToArray();
```

with:

```csharp
    private int[] _boards = Array.Empty<int>();
    private int[] _exSnBoards = Array.Empty<int>();

    /// <summary>The loaded SRX board numbers (1..12), distinct. Empty when none loaded.</summary>
    public IReadOnlyCollection<int> Boards => _boards;

    /// <summary>The loaded ExSN board numbers (1..6, from slot values 13..18), distinct. The SN-Acoustic
    /// catalog only references ExSN1..5.</summary>
    public IReadOnlyCollection<int> ExSnBoards => _exSnBoards;

    /// <summary>Raised after the loaded sets are recomputed, so loaded-aware UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>Recompute the loaded sets from the 4 raw slot values: SRX boards (1..12) and ExSN boards
    /// (slot 13..18 -> board 1..6), deduped; then raise <see cref="Changed"/>.</summary>
    public void SetFromSlots(int slot1, int slot2, int slot3, int slot4)
    {
        var slots = new[] { slot1, slot2, slot3, slot4 };
        _boards = slots.Where(v => v is >= 1 and <= 12).Distinct().ToArray();
        _exSnBoards = slots.Where(v => v is >= 13 and <= 18).Select(v => v - 12).Distinct().ToArray();
        Changed?.Invoke();
    }
```

(`System` is already imported for `Array`/`Action`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run the filtered test command from Step 2.
Expected: `Passed! - Failed: 0, Passed: 6` (the 3 existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/LoadedSrxState.cs Tests/TestLoadedSrxState.cs
git commit -m "feat: LoadedSrxState exposes loaded ExSN boards + a Changed event

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `ExSnInstrumentFilter` — pure filtering logic

**Files:**
- Create: `Src/Models/Services/ExSnInstrumentFilter.cs`
- Test: `Tests/TestExSnInstrumentFilter.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestExSnInstrumentFilter.cs`:

```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestExSnInstrumentFilter
{
    [Test]
    public void ExSnBoardOf_ParsesPrefix()
    {
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("ExSN3 001: TC Guitar w/Fing"), Is.EqualTo(3));
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("ExSN5 011: Mute French Horn"), Is.EqualTo(5));
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("INT 001: Concert Grand"), Is.Null);
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("Strings"), Is.Null);
    }

    [Test]
    public void VisibleFamilies_DropsExpansionWhenNoneLoaded()
    {
        var all = new[] { "Pianos", "Expansion" };
        Assert.That(ExSnInstrumentFilter.VisibleFamilies(all, new int[0]), Is.EqualTo(new[] { "Pianos" }));
        Assert.That(ExSnInstrumentFilter.VisibleFamilies(all, new[] { 2 }), Is.EqualTo(new[] { "Pianos", "Expansion" }));
    }

    [Test]
    public void LoadedExpansionIndices_KeepsOnlyLoadedBoards()
    {
        var names = new List<string> { "ExSN1 001: A", "ExSN2 001: B", "ExSN3 001: C" };
        var expansion = new[] { 0, 1, 2 };
        Assert.That(ExSnInstrumentFilter.LoadedExpansionIndices(expansion, names, new[] { 3 }),
            Is.EqualTo(new[] { 2 }));
        Assert.That(ExSnInstrumentFilter.LoadedExpansionIndices(expansion, names, new[] { 1, 2 }),
            Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void DisplayName_LabelsUnloadedExSn()
    {
        Assert.That(ExSnInstrumentFilter.DisplayName("ExSN3 001: C", new[] { 2 }), Is.EqualTo("ExSN3 001: C (not loaded)"));
        Assert.That(ExSnInstrumentFilter.DisplayName("ExSN3 001: C", new[] { 3 }), Is.EqualTo("ExSN3 001: C"));
        Assert.That(ExSnInstrumentFilter.DisplayName("INT 001: Y", new int[0]), Is.EqualTo("INT 001: Y"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release --filter "FullyQualifiedName~TestExSnInstrumentFilter"`
Expected: FAIL — `ExSnInstrumentFilter` does not exist.

- [ ] **Step 3: Implement**

Create `Src/Models/Services/ExSnInstrumentFilter.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure filtering for the SN-Acoustic instrument picker's "Expansion" family: which ExSN
/// instruments are visible given the loaded ExSN boards, and how an unloaded one is labelled. The ExSN
/// board is implicit in the instrument name's "ExSN{k}" prefix (the hardware has no board parameter).</summary>
public static class ExSnInstrumentFilter
{
    public const string ExpansionFamily = "Expansion";

    /// <summary>The ExSN board number (1..6) named by an instrument's "ExSN{k} …" prefix, or null for a
    /// non-ExSN (INT) instrument.</summary>
    public static int? ExSnBoardOf(string instrumentName)
    {
        const string prefix = "ExSN";
        if (instrumentName.Length <= prefix.Length || !instrumentName.StartsWith(prefix)) return null;
        var digit = instrumentName[prefix.Length];
        return digit is >= '1' and <= '6' ? digit - '0' : null;
    }

    /// <summary>The family names with "Expansion" removed when no ExSN board is loaded; order preserved.</summary>
    public static List<string> VisibleFamilies(IReadOnlyList<string> allFamilyNames, IReadOnlyCollection<int> loadedExSn)
        => loadedExSn.Count > 0
            ? allFamilyNames.ToList()
            : allFamilyNames.Where(f => f != ExpansionFamily).ToList();

    /// <summary>The subset of <paramref name="expansionIndices"/> whose instrument name (looked up in
    /// <paramref name="names"/>) belongs to a loaded ExSN board.</summary>
    public static List<int> LoadedExpansionIndices(IReadOnlyList<int> expansionIndices,
        IReadOnlyList<string> names, IReadOnlyCollection<int> loadedExSn)
    {
        var loaded = new HashSet<int>(loadedExSn);
        return expansionIndices
            .Where(i => i >= 0 && i < names.Count && ExSnBoardOf(names[i]) is { } b && loaded.Contains(b))
            .ToList();
    }

    /// <summary>An instrument's display name: the name suffixed with "(not loaded)" when it is an ExSN
    /// instrument whose board isn't loaded; otherwise the name unchanged. Shares Phase 1's suffix.</summary>
    public static string DisplayName(string instrumentName, IReadOnlyCollection<int> loadedExSn)
    {
        var board = ExSnBoardOf(instrumentName);
        if (board is { } b && !loadedExSn.Contains(b))
            return instrumentName + SrxGroupIdResolution.NotLoadedSuffix;
        return instrumentName;
    }
}
```

(`CultureInfo` is imported for parity with sibling files even though this file does not format numbers; if a build warning about an unused using appears, remove the `System.Globalization` line.)

- [ ] **Step 4: Run the tests to verify they pass**

Run the filtered test command from Step 2.
Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ExSnInstrumentFilter.cs Tests/TestExSnInstrumentFilter.cs
git commit -m "feat: ExSnInstrumentFilter — loaded-ExSN instrument filtering/labelling

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `DiscriminatedParamSectionViewModel` — additive loaded-aware hooks

This makes the shared picker capable of dynamic families, display labelling with a reverse map, a generic "keep the current selection visible" step, and a public `Reproject()` — **all optional with identity defaults, so the MFX panel and (for now) the SN-A editor behave exactly as before.** No behavior changes until Task 4 wires SN-A.

**Files:**
- Modify: `Src/ViewModels/DiscriminatedParamSectionViewModel.cs`

> **Note on testing:** this VM requires a live `DomainBase` + discrete param to construct, which this suite does not build in unit tests (same as the existing picker/MFX VM). The pure decisions are covered by Task 2; here verification is build + full suite green (behavior unchanged at this step), and the final review confirms MFX is unaffected.

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `Src/ViewModels/DiscriminatedParamSectionViewModel.cs` with:

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
///
/// Optional loaded-aware hooks (all default to identity, so MFX is unaffected): a <c>familiesSupplier</c>
/// for a dynamic family list, a <c>displayName</c>/<c>toRealValue</c> pair so the instrument combo can
/// show a transformed label (e.g. "(not loaded)") while still writing the real discrete value, and a
/// public <see cref="Reproject"/> to refresh after the loaded set changes. The VM always keeps the
/// current selection's family and value visible so a filtered list can never blank the selection.
/// </summary>
public sealed class DiscriminatedParamSectionViewModel : ViewModelBase, IDisposable
{
    private readonly DomainBase _domain;
    private readonly List<FullyQualifiedParameter> _allParams;
    private readonly List<FullyQualifiedParameter> _discriminators;
    private readonly IDisposable _valueSub;
    private readonly string _gridPathSegment;
    private readonly Func<IReadOnlyList<string>> _familiesSupplier;
    private readonly Func<int, string> _familyOf;
    private readonly Func<string, IReadOnlyList<int>> _valuesIn;
    private readonly Func<string, IReadOnlyList<string>, IReadOnlyList<string>> _friendlyLabels;
    private readonly Func<int, string, string> _displayName;
    private readonly Func<string, string> _toRealValue;
    private bool _syncing;

    /// <summary>The discriminator wrapper (e.g. MFX Type / Instrument). Exposed for engine-specific extras.</summary>
    public ParamString Discriminator { get; }
    public ObservableCollection<DisplayParam> Params { get; } = [];
    public bool HasParams => Params.Count > 0;

    public DiscriminatedParamSectionViewModel(DomainBase domain, ThrottledParameterWriter writer,
        string discriminatorLeafName, string gridPathSegment,
        IReadOnlyList<string> families, Func<int, string> familyOf,
        Func<string, IReadOnlyList<int>> valuesIn,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>> friendlyLabels,
        Func<IReadOnlyList<string>>? familiesSupplier = null,
        Func<int, string, string>? displayName = null,
        Func<string, string>? toRealValue = null)
    {
        _domain = domain;
        _gridPathSegment = gridPathSegment;
        _familiesSupplier = familiesSupplier ?? (() => families);
        _familyOf = familyOf; _valuesIn = valuesIn; _friendlyLabels = friendlyLabels;
        _displayName = displayName ?? ((_, name) => name);
        _toRealValue = toRealValue ?? (d => d);
        _allParams = domain.GetRelevantParameters(true, true);

        var disc = _allParams.First(p => p.ParSpec.Name == discriminatorLeafName);
        // Options from Repr (enum) or Discrete (e.g. Instrument list).
        var opts = disc.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                   ?? disc.ParSpec.Discrete?.Select(d => d.Item2).ToList()
                   ?? new List<string>();
        Discriminator = new ParamString(domain, disc, writer, opts);

        _families = BuildFamilies();

        var parentPaths = _allParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        _discriminators = _allParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        _valueSub = Discriminator.WhenAnyValue(t => t.Value).Subscribe(SyncPickerFromValue);
        Recompute();
    }

    private IReadOnlyList<string> _families = [];
    /// <summary>Selectable families (loaded-aware via the supplier; always includes the current
    /// selection's family).</summary>
    public IReadOnlyList<string> Families { get => _families; private set => this.RaiseAndSetIfChanged(ref _families, value); }

    private string _selectedFamily = "";
    public string SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (_selectedFamily == value) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            ValuesInFamily = BuildValuesInFamily(value);
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
            if (!_syncing) Discriminator.Value = _toRealValue(value);
        }
    }

    /// <summary>Recompute the family list + current family's values from the live loaded set and
    /// discriminator value. Call after the loaded ExSN/SRX set changes.</summary>
    public void Reproject()
    {
        Families = BuildFamilies();
        SyncPickerFromValue(Discriminator.Value);
    }

    /// <summary>Families from the supplier, plus the current selection's family if the supplier dropped it
    /// (so an unloaded-board patch never blanks the family combo).</summary>
    private IReadOnlyList<string> BuildFamilies()
    {
        var fams = _familiesSupplier().ToList();
        var idx = IndexOf(Discriminator.Value);
        if (idx >= 0)
        {
            var cf = _familyOf(idx);
            if (!fams.Contains(cf)) fams.Add(cf);
        }
        return fams;
    }

    /// <summary>The (display-transformed) value names in a family, plus the current selection's display if
    /// the filter dropped it (so the value combo never blanks the current selection).</summary>
    private IReadOnlyList<string> BuildValuesInFamily(string family)
    {
        var list = _valuesIn(family).Select(i => _displayName(i, Discriminator.Options[i])).ToList();
        var idx = IndexOf(Discriminator.Value);
        if (idx >= 0 && _familyOf(idx) == family)
        {
            var cur = _displayName(idx, Discriminator.Options[idx]);
            if (!list.Contains(cur)) list.Add(cur);
        }
        return list;
    }

    private void SyncPickerFromValue(string valueName)
    {
        _syncing = true;
        try
        {
            var idx = Discriminator.Options is { Count: > 0 } ? IndexOf(valueName) : -1;
            if (idx >= 0)
            {
                var family = _familyOf(idx);
                if (_selectedFamily != family)
                {
                    _selectedFamily = family;
                    this.RaisePropertyChanged(nameof(SelectedFamily));
                }
                // Rebuild only when the (loaded-aware) list actually changed — keeps MFX churn-free and
                // makes Reproject() refresh when the loaded set changes the filtered/labelled list.
                var values = BuildValuesInFamily(family);
                if (!values.SequenceEqual(_valuesInFamily))
                {
                    ValuesInFamily = values;
                    this.RaisePropertyChanged(nameof(ValuesInFamily));
                }
                SelectedValue = _displayName(idx, Discriminator.Options[idx]);
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
        // Rebuild the grid when a parent changes. Dependent VALUES are resynced elsewhere — the param
        // wrappers re-read on an IsParent write, and ParameterValueTemplate's ui2hw path resyncs
        // sub-switches — so here we only need the structural rebuild.
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

Key changes vs the original: `Families` is a change-notifying property built via `BuildFamilies()`; `SelectedFamily`/`SyncPickerFromValue` use `BuildValuesInFamily()` (which applies `_displayName` and keeps the current value visible); `SelectedValue`'s setter maps back through `_toRealValue` before writing `Discriminator.Value`; new `Reproject()`. With all hooks defaulting to identity, every existing caller behaves exactly as before.

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`. (Both call sites — MFX and SN-A — still compile: the new ctor params are optional.)

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281` (274 baseline + 3 from Task 1 + 4 from Task 2; unchanged by this task).

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/DiscriminatedParamSectionViewModel.cs
git commit -m "feat: additive loaded-aware hooks on the shared discriminated picker VM

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Wire the SN-Acoustic editor to filter + live-refresh

**Files:**
- Modify: `Src/ViewModels/SNAcousticToneEditorViewModel.cs`

> **Note on testing:** hardware-bound VM wiring; verification is build + full suite green. Manual check (with hardware) at the end.

- [ ] **Step 1: Add the `Avalonia.Threading` using**

In `Src/ViewModels/SNAcousticToneEditorViewModel.cs`, the usings currently start:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
```

Add `using Avalonia.Threading;` after `using System.Linq;`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
```

- [ ] **Step 2: Add a field for the Changed handler**

The class has a fields block near the top:

```csharp
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly List<IDisposable> _wrappers = [];
```

Add a handler field after `_wrappers`:

```csharp
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly List<IDisposable> _wrappers = [];
    private readonly Action _onLoadedSrxChanged;
```

- [ ] **Step 3: Replace the `Instrument` construction with loaded-aware delegates + hooks**

The current construction is:

```csharp
        Instrument = Track(new DiscriminatedParamSectionViewModel(common, _writer,
            "Instrument", "/Modify Parameter ",
            InstrumentCatalog.Families.Select(f => f.Name).ToList(),
            InstrumentCatalog.FamilyOf, InstrumentCatalog.ValuesIn,
            ConditionalParamLabels.FriendlyNames));
```

Replace it with:

```csharp
        // The full instrument-name list (INSTRUMENT_VARIATIONS order) drives ExSN board parsing.
        // The Instrument param is discrete, so ParSpec.Discrete is non-null here.
        var instrumentNames = byPath[CP + "Instrument"].ParSpec.Discrete!.Select(d => d.Item2).ToList();
        var familyNames = InstrumentCatalog.Families.Select(f => f.Name).ToList();
        var notLoaded = SrxGroupIdResolution.NotLoadedSuffix;

        Instrument = Track(new DiscriminatedParamSectionViewModel(common, _writer,
            "Instrument", "/Modify Parameter ",
            familyNames,
            InstrumentCatalog.FamilyOf,
            family => family == ExSnInstrumentFilter.ExpansionFamily
                ? ExSnInstrumentFilter.LoadedExpansionIndices(
                    InstrumentCatalog.ValuesIn(ExSnInstrumentFilter.ExpansionFamily), instrumentNames,
                    LoadedSrxState.Default.ExSnBoards)
                : InstrumentCatalog.ValuesIn(family),
            ConditionalParamLabels.FriendlyNames,
            familiesSupplier: () => ExSnInstrumentFilter.VisibleFamilies(familyNames, LoadedSrxState.Default.ExSnBoards),
            displayName: (_, name) => ExSnInstrumentFilter.DisplayName(name, LoadedSrxState.Default.ExSnBoards),
            toRealValue: d => d.EndsWith(notLoaded) ? d[..^notLoaded.Length] : d));

        // Live-refresh the instrument picker when expansions are (re)loaded.
        _onLoadedSrxChanged = () => Dispatcher.UIThread.Post(Instrument.Reproject);
        LoadedSrxState.Default.Changed += _onLoadedSrxChanged;
```

Notes: `CP` is the existing common-path-prefix const (`"SuperNATURAL Acoustic Tone Common/"`); `byPath` is the existing local dictionary. `ParSpec.Discrete` is the `INSTRUMENT_VARIATIONS` tuple list (non-null for this discrete param). `LoadedSrxState`, `ExSnInstrumentFilter`, `SrxGroupIdResolution` are all in `Integra7AuralAlchemist.Models.Services`, already imported.

- [ ] **Step 4: Unsubscribe in `Dispose`**

The current `Dispose` is:

```csharp
    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
        NoteRail.Dispose();
        _writer.Dispose();
    }
```

Replace it with:

```csharp
    public void Dispose()
    {
        LoadedSrxState.Default.Changed -= _onLoadedSrxChanged;
        foreach (var w in _wrappers) w.Dispose();
        NoteRail.Dispose();
        _writer.Dispose();
    }
```

- [ ] **Step 5: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 6: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/SNAcousticToneEditorViewModel.cs
git commit -m "feat: filter SN-Acoustic Expansion instruments to loaded ExSN, live-refresh on Load SRX

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with hardware, after all tasks):**
- With at least one ExSN1–5 loaded: open an SN-Acoustic tone, open the instrument picker → the "Expansion" family lists only instruments from loaded ExSN boards.
- With no ExSN loaded: the "Expansion" family is absent (unless the current patch uses an ExSN instrument).
- A patch whose instrument is an unloaded ExSN: the picker shows that instrument under "Expansion", labelled "(not loaded)", selected (not blank); selecting it again writes the same instrument (no error).
- Change expansions via the SRX panel + "Load selected SRX!" → the picker updates without reconnecting.
- Open a friendly MFX panel (any engine): its Type picker behaves exactly as before (regression check).

---

## Final verification

- [ ] Full suite: `Passed! - Failed: 0, Passed: 281`.
- [ ] `git log --oneline main..HEAD` shows the Phase 2 commits (spec + Tasks 1–4) on `exsn-instrument-filter`, and `git status` shows only the unrelated `AsyncMidiInputWrapper.cs` change left uncommitted.

After all tasks: dispatch a final code review (explicitly checking the MFX panel is unaffected), then use superpowers:finishing-a-development-branch.

---

## Spec coverage (self-review)

- **§1 Loaded-ExSN source** → Task 1 (`ExSnBoards` + `Changed`).
- **§2 Pure filter** → Task 2 (`ExSnBoardOf`/`VisibleFamilies`/`LoadedExpansionIndices`/`DisplayName`, tested).
- **§3 Picker VM extension** (dynamic families, display hooks, keep-current, `Reproject`) → Task 3. *Refinement:* `families` stays a positional param with an added optional `familiesSupplier`, so the MFX call site is untouched (strictly less risk than the spec's "change the signature + update MFX"). The spec's listed `MfxPanelViewModel` modification is therefore not needed.
- **§4 SN-A wiring + live refresh** → Task 4 (loaded-aware delegates + hooks + `Changed` subscription, unsubscribe on `Dispose`).
- **Edge cases** (unloaded current instrument kept + labelled; no-ExSN + INT current; MFX unchanged) → handled by `BuildFamilies`/`BuildValuesInFamily` keep-current + identity defaults; covered by the manual checks.
- **Testing** → pure filter + `LoadedSrxState` covered by unit tests; VM glue by build + suite + review (consistent with how this VM is already tested).
- **Out of scope** (SN-Synth, SN-Drums, phrases, ExSN6, the loader) → untouched.
