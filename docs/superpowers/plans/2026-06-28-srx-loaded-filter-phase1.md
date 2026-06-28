# Phase 1 — Filter PCM Wave Group ID to Loaded SRX Boards — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the PCM sound editors, make the Wave Group ID (SRX board) selector list only the SRX boards currently loaded into the unit's 4 slots — in both the friendly editors and the advanced grid — keeping the patch's current board visible and refreshing live after "Load selected SRX!".

**Architecture:** One `EffectiveRepr` resolution pass (`SrxGroupIdResolution.Apply`) added to the single domain read path (`DomainBase.ReadFromIntegraAsync`) sets each Wave Group ID param's `EffectiveRepr` to the loaded-board list (∪ current board) when its sibling Wave Group Type is `SRX`. Both surfaces already read `EffectiveRepr ?? ParSpec.Repr`, so they filter with no UI-VM changes. A `LoadedSrxState` singleton (mirrors `WaveformBanks.Default`) carries the loaded set, updated from `SrxSlot1..4` at connect and after `LoadSrx`; `LoadSrx` then resyncs all parts so the pass re-runs.

**Tech Stack:** Avalonia 12 + ReactiveUI; C# / .NET 10; NUnit 3. The existing `WaveformBanks`/`WaveNameResolution`/`EffectiveRepr`/`WaveBankRegistry` infrastructure.

**Build/test commands** (user-local .NET 10 SDK; the system `dotnet` is too old):

- Build: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Full tests: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Filtered tests: append `--filter "FullyQualifiedName~ClassName"`

Baseline before starting: the suite passes with **264** tests (`Failed: 0, Passed: 264`). Run the full test command once to confirm this baseline on your branch.

**Standing constraints:** never `git --no-verify`; this work is on branch `srx-loaded-filter` (already created off `main`); commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Transient `Permission denied` on `.git/objects` (Windows AV) — retry the commit once.

---

## File Structure

- **Create** `Src/Models/Services/LoadedSrxState.cs` — holds the current loaded SRX board set; one responsibility: map the 4 raw slot values to the set of loaded board numbers (1..12).
- **Create** `Src/Models/Services/SrxGroupIdResolution.cs` — pure board-filtering logic (`VisibleBoards`, `BuildRepr`) + the domain glue (`Apply`) that sets `EffectiveRepr` on Wave Group ID params. Mirrors `WaveNameResolution`.
- **Create** `Tests/TestLoadedSrxState.cs`, `Tests/TestSrxGroupIdResolution.cs` — unit tests for the pure pieces.
- **Modify** `Src/Models/Domain/DomainBase.cs` — one line in `ReadFromIntegraAsync` to run the new pass.
- **Modify** `Src/ViewModels/MainWindowViewModel.cs` — set `LoadedSrxState` at connect + after `LoadSrx`; add `ResyncAllPartsAsync` and call it from `LoadSrx`.
- **Modify** `Src/DataTemplates/DataTemplateProvider.cs` — make the combo refresh rebuild on content change, not only count change.

**No change needed:** the friendly partial VMs (`PCMPartialViewModel`, `PCMDrumWmtLayerViewModel`) and the reverse converter already honor `EffectiveRepr`; the Wave Group Type params are already `isparent:true`, so a Type change already re-runs the read pass.

---

## Task 1: `LoadedSrxState` — the loaded SRX board set

**Files:**
- Create: `Src/Models/Services/LoadedSrxState.cs`
- Test: `Tests/TestLoadedSrxState.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestLoadedSrxState.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestLoadedSrxState
{
    [Test]
    public void SetFromSlots_KeepsOnlySrxBoards()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(2, 0, 6, 13); // 0 = Empty, 13 = ExSN1 -> dropped
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 2, 6 }));
    }

    [Test]
    public void SetFromSlots_Distinct()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(5, 5, 5, 5);
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 5 }));
    }

    [Test]
    public void SetFromSlots_AllEmpty_IsEmpty()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(0, 0, 0, 0);
        Assert.That(s.Boards, Is.Empty);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release --filter "FullyQualifiedName~TestLoadedSrxState"`
Expected: FAIL — compile error `The type or namespace name 'LoadedSrxState' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/LoadedSrxState.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The set of SRX boards currently loaded into the unit's 4 slots. Slot values use the same
/// encoding as the SrxSelector combo (0 = Empty, 1..12 = SRX01..12, 13..18 = ExSN1..6, 19 = HQ); only
/// 1..12 (SRX) are relevant to PCM wave groups. Mirrors the <see cref="WaveformBanks"/>.Default pattern:
/// a shared mutable singleton the domain read path consults, updated by the main view model.</summary>
public sealed class LoadedSrxState
{
    private int[] _boards = Array.Empty<int>();

    /// <summary>The loaded SRX board numbers (1..12), distinct. Empty when none loaded.</summary>
    public IReadOnlyCollection<int> Boards => _boards;

    /// <summary>Recompute the loaded set from the 4 raw slot values (keeps only 1..12, deduped).</summary>
    public void SetFromSlots(int slot1, int slot2, int slot3, int slot4)
        => _boards = new[] { slot1, slot2, slot3, slot4 }
            .Where(v => v is >= 1 and <= 12)
            .Distinct()
            .ToArray();

    /// <summary>Shared instance consulted by the domain read path.</summary>
    public static LoadedSrxState Default { get; } = new();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run the filtered test command from Step 2.
Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/LoadedSrxState.cs Tests/TestLoadedSrxState.cs
git commit -m "feat: LoadedSrxState — current loaded SRX board set

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `SrxGroupIdResolution` pure core (`VisibleBoards` + `BuildRepr`)

**Files:**
- Create: `Src/Models/Services/SrxGroupIdResolution.cs` (pure methods now; `Apply` glue added in Task 3)
- Test: `Tests/TestSrxGroupIdResolution.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestSrxGroupIdResolution.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestSrxGroupIdResolution
{
    [Test]
    public void VisibleBoards_MergesCurrentIntoLoaded_Sorted()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 6, 2 }, 5), Is.EqualTo(new[] { 2, 5, 6 }));

    [Test]
    public void VisibleBoards_NoDuplicateWhenCurrentAlreadyLoaded()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 2, 6 }, 6), Is.EqualTo(new[] { 2, 6 }));

    [Test]
    public void VisibleBoards_CurrentOnlyWhenNoneLoaded()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new int[0], 5), Is.EqualTo(new[] { 5 }));

    [Test]
    public void VisibleBoards_DropsOutOfRange()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 0, 13, 19 }, 0), Is.Empty);

    [Test]
    public void BuildRepr_Srx_ReturnsFilteredBoardDict()
    {
        var repr = SrxGroupIdResolution.BuildRepr("SRX", new[] { 2, 6 }, 5);
        Assert.That(repr, Is.Not.Null);
        Assert.That(repr!.Keys, Is.EquivalentTo(new[] { 2, 5, 6 }));
        Assert.That(repr[5], Is.EqualTo("5"));
    }

    [Test]
    public void BuildRepr_NonSrx_ReturnsNull()
        => Assert.That(SrxGroupIdResolution.BuildRepr("Internal", new[] { 2, 6 }, 5), Is.Null);

    [Test]
    public void BuildRepr_KeepsUnloadedCurrentBoardVisible()
    {
        var repr = SrxGroupIdResolution.BuildRepr("SRX", new[] { 2, 6 }, 9); // SRX9 not loaded
        Assert.That(repr!.ContainsKey(9), Is.True);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release --filter "FullyQualifiedName~TestSrxGroupIdResolution"`
Expected: FAIL — compile error `The type or namespace name 'SrxGroupIdResolution' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/SrxGroupIdResolution.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Filters the PCM "Wave Group ID" (SRX board) option list to the currently-loaded SRX boards.
/// Pure <see cref="VisibleBoards"/>/<see cref="BuildRepr"/> are unit-tested; <see cref="Apply"/> (added
/// in the same file) is the domain glue that sets each Wave Group ID param's EffectiveRepr — mirrors
/// <see cref="WaveNameResolution"/>.</summary>
public static class SrxGroupIdResolution
{
    /// <summary>Board numbers to show: the loaded boards plus the patch's current board (so an unloaded
    /// current board stays selectable), clamped to 1..12, deduped, ascending.</summary>
    public static List<int> VisibleBoards(IReadOnlyCollection<int> loaded, int current)
    {
        var set = new SortedSet<int>();
        foreach (var b in loaded)
            if (b is >= 1 and <= 12) set.Add(b);
        if (current is >= 1 and <= 12) set.Add(current);
        return new List<int>(set);
    }

    /// <summary>The EffectiveRepr (board number -> its string) for a Wave Group ID whose sibling Wave
    /// Group Type is <paramref name="groupType"/>: the filtered board list when SRX, otherwise null
    /// (leaving the param as its plain numeric field, unchanged from today).</summary>
    public static IDictionary<int, string>? BuildRepr(string groupType, IReadOnlyCollection<int> loaded, int current)
    {
        if (groupType != WaveBankResolver.TypeSrx) return null;
        var dict = new Dictionary<int, string>();
        foreach (var b in VisibleBoards(loaded, current))
            dict[b] = b.ToString(CultureInfo.InvariantCulture);
        return dict;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run the filtered test command from Step 2.
Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SrxGroupIdResolution.cs Tests/TestSrxGroupIdResolution.cs
git commit -m "feat: SrxGroupIdResolution pure core (VisibleBoards + BuildRepr)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `SrxGroupIdResolution.Apply` glue + wire into the domain read path

This turns the filtering on: the pass sets each Wave Group ID param's `EffectiveRepr` after every read, which both surfaces already consume.

**Files:**
- Modify: `Src/Models/Services/SrxGroupIdResolution.cs` (add `Apply` + the `FullyQualifiedParameter` using)
- Modify: `Src/Models/Domain/DomainBase.cs:57` (call the pass)

> **Note on testing:** `Apply` is thin domain glue over the unit-tested `BuildRepr`, exactly like `WaveNameResolution.Apply` (which is also not unit-tested — building `FullyQualifiedParameter`/`Integra7ParameterSpec` instances in a test is not a pattern this codebase uses). Its SRX-vs-not decision and board filtering are already covered by the `BuildRepr` tests in Task 2. Verification here is: build succeeds and the full suite stays green (no regressions).

- [ ] **Step 1: Add the `Apply` method**

In `Src/Models/Services/SrxGroupIdResolution.cs`, add `using Integra7AuralAlchemist.Models.Data;` to the usings (so `FullyQualifiedParameter` resolves):

```csharp
using System.Collections.Generic;
using System.Globalization;
using Integra7AuralAlchemist.Models.Data;
```

Then add this method inside the `SrxGroupIdResolution` class, after `BuildRepr`:

```csharp
    /// <summary>For each distinct Wave Group ID param (paired with its Wave Group Type via
    /// <see cref="WaveBankRegistry"/>), set its EffectiveRepr from the loaded set + current board, then
    /// re-fire StringValue so the friendly ParamString.Options and the advanced-grid combo refresh
    /// (the read's StringValue notification fired before this pass set EffectiveRepr).</summary>
    public static void Apply(IReadOnlyList<FullyQualifiedParameter> ps, IReadOnlyCollection<int> loaded)
    {
        var byPath = new Dictionary<string, FullyQualifiedParameter>(ps.Count);
        foreach (var p in ps) byPath[p.ParSpec.Path] = p;

        var seen = new HashSet<string>();
        foreach (var sib in WaveBankRegistry.Entries.Values)
        {
            if (!seen.Add(sib.IdPath)) continue; // each (Type, Id) pair once
            if (!byPath.TryGetValue(sib.IdPath, out var id) || !byPath.TryGetValue(sib.TypePath, out var type))
                continue;

            id.EffectiveRepr = BuildRepr(type.StringValue, loaded, (int)id.RawNumericValue);
            var refire = id.StringValue;
            id.StringValue = refire; // notify listeners now that EffectiveRepr changed
        }
    }
```

- [ ] **Step 2: Wire the pass into the read path**

In `Src/Models/Domain/DomainBase.cs`, in `ReadFromIntegraAsync`, the existing line 57 is:

```csharp
        // Resolve bank-selected waveform names (no-op for domains without wave params).
        WaveNameResolution.Apply(_domainParameters, WaveformBanks.Default);
```

Add immediately after it:

```csharp
        // Filter the Wave Group ID (SRX board) options to the currently-loaded SRX boards.
        SrxGroupIdResolution.Apply(_domainParameters, LoadedSrxState.Default.Boards);
```

(`DomainBase.cs` already has `using Integra7AuralAlchemist.Models.Services;`, so both `SrxGroupIdResolution` and `LoadedSrxState` resolve.)

- [ ] **Step 3: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 274`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SrxGroupIdResolution.cs Src/Models/Domain/DomainBase.cs
git commit -m "feat: apply SRX-loaded Wave Group ID filter on every domain read

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Update `LoadedSrxState` at connect + after Load SRX, and resync all parts

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs` — `UpdateConnectedAsync` (after `GetLoadedSrxAsync`), `LoadSrx`, and a new `ResyncAllPartsAsync`.

> **Note on testing:** `MainWindowViewModel` wiring drives hardware MIDI and cannot be unit-tested here. Verification is build + full suite green; behavior is validated against hardware (see the manual check at the end of the task).

- [ ] **Step 1: Set the loaded state at connect**

In `Src/ViewModels/MainWindowViewModel.cs`, `UpdateConnectedAsync` currently has (around line 191):

```csharp
                (SrxSlot1, SrxSlot2, SrxSlot3, SrxSlot4) = await integra7Api.GetLoadedSrxAsync();
```

Add immediately after that line:

```csharp
                LoadedSrxState.Default.SetFromSlots(SrxSlot1, SrxSlot2, SrxSlot3, SrxSlot4);
```

- [ ] **Step 2: Update state + resync all parts in `LoadSrx`**

`LoadSrx` currently is (around line 152):

```csharp
    [ReactiveCommand]
    public async Task LoadSrx()
    {
        if (_connected)
            await Integra7?.SendLoadSrxAsync((byte)_srxSlot1, (byte)_srxSlot2, (byte)_srxSlot3, (byte)_srxSlot4);
    }
```

Replace it with:

```csharp
    [ReactiveCommand]
    public async Task LoadSrx()
    {
        if (_connected)
        {
            await Integra7?.SendLoadSrxAsync((byte)_srxSlot1, (byte)_srxSlot2, (byte)_srxSlot3, (byte)_srxSlot4);
            LoadedSrxState.Default.SetFromSlots(_srxSlot1, _srxSlot2, _srxSlot3, _srxSlot4);
            await ResyncAllPartsAsync(); // re-runs the read path -> refreshes Wave Group ID options
        }
    }
```

- [ ] **Step 3: Add `ResyncAllPartsAsync`**

Add this method next to the existing `ResyncPartAsync(byte part)` (which is around line 602). It mirrors that method but resyncs every part (each `pvm.ResyncPartAsync` early-returns unless its own `PartNo` matches, so we pass each part its own number):

```csharp
    private async Task ResyncAllPartsAsync()
    {
        try
        {
            SignalStartSync();
            if (PartViewModels != null)
                foreach (var pvm in PartViewModels)
                {
                    SyncInfo = $"Resync part {pvm.PartNo}";
                    await pvm.EnsurePreselectIsNotNullAsync();
                    await pvm.ResyncPartAsync((byte)pvm.PartNo);
                }
        }
        finally
        {
            SignalStopSync();
        }
    }
```

- [ ] **Step 4: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` with `0 Error(s)`. (If `(byte)pvm.PartNo` produces a redundant-cast warning because `PartNo` is already `byte`, that is harmless; leave it.)

- [ ] **Step 5: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 274`.

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs
git commit -m "feat: refresh SRX-loaded set on connect and resync all parts on Load SRX

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with hardware, after all tasks):** connect with at least one SRX board loaded; in a PCM Synth tone, set Bank = SRX — the SRX dropdown lists only loaded boards (friendly editor and the advanced "Wave Group ID" combo). Load a different SRX configuration via the SRX panel and press "Load selected SRX!" — the dropdowns update without reconnecting. A patch whose stored board is no longer loaded still shows that board.

---

## Task 5: Content-aware combo refresh in the advanced grid

Without this, the advanced grid combo only rebuilds its items when the option **count** changes (`DataTemplateProvider.cs:120`), so a same-size SRX swap (e.g. `{2,6,11}` → `{3,7,9}`) would leave stale board numbers. The friendly side already recomputes fully; this fixes the grid and closes the same latent gap for the wave-name combos.

**Files:**
- Modify: `Src/DataTemplates/DataTemplateProvider.cs` (the combo's `BindToModel` refresh lambda, around lines 117-126)

> **Note on testing:** this is Avalonia UI-control code (no headless UI test harness in this project). Verification is build + full suite green; behavior is validated by the manual check below.

- [ ] **Step 1: Replace the count-only refresh with a content-aware one**

In `Src/DataTemplates/DataTemplateProvider.cs`, find the combo's `BindToModel` call (the `else` branch that builds a `ComboBox c`), currently:

```csharp
                BindToModel(c, p, () =>
                {
                    var cur = p.EffectiveRepr ?? p.ParSpec.Repr;
                    if (cur != null && c.Items.Count != cur.Count)
                    {
                        c.Items.Clear();
                        foreach (var el in cur) c.Items.Add(el.Value);
                    }
                    c.SelectedItem = p.StringValue;
                }, () => suppressPush, v => suppressPush = v);
```

Replace it with (rebuild when the entries differ, preserving the existing iteration order so non-SRX combos are unaffected):

```csharp
                BindToModel(c, p, () =>
                {
                    var cur = p.EffectiveRepr ?? p.ParSpec.Repr;
                    if (cur != null)
                    {
                        var want = cur.Select(kv => kv.Value).ToList();
                        var have = c.Items.Cast<object?>().Select(o => o?.ToString()).ToList();
                        if (!have.SequenceEqual(want))
                        {
                            c.Items.Clear();
                            foreach (var v in want) c.Items.Add(v);
                        }
                    }
                    c.SelectedItem = p.StringValue;
                }, () => suppressPush, v => suppressPush = v);
```

(`System.Linq` is already imported in this file; no new using is needed.)

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 274`.

- [ ] **Step 4: Commit**

```bash
git add Src/DataTemplates/DataTemplateProvider.cs
git commit -m "fix: advanced-grid combo rebuilds on option content change, not only count

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with hardware):** load an SRX configuration, open the advanced PCM Synth Partial tab with Bank = SRX, then load a *different same-count* SRX configuration and press "Load selected SRX!" — the advanced Wave Group ID combo shows the new boards (not the old ones).

---

## Final verification

- [ ] Run the full suite once more: `Passed! - Failed: 0, Passed: 274`.
- [ ] Confirm `git log --oneline main..HEAD` shows the five feature commits (Tasks 1-5) on `srx-loaded-filter`.

After all tasks: dispatch a final code review of the branch diff, then use superpowers:finishing-a-development-branch.

---

## Spec coverage (self-review)

- **§1 Loaded-SRX state** → Task 1 (`LoadedSrxState`) + Task 4 (set at connect + after Load).
- **§2 Pure filter + pass** → Task 2 (`VisibleBoards`/`BuildRepr`, tested) + Task 3 (`Apply` glue, including the "keep current visible" via `VisibleBoards`, the SRX-only `EffectiveRepr`, and the StringValue re-fire).
- **§3 Wire into reads** → Task 3 (`DomainBase.ReadFromIntegraAsync`). Friendly VMs unchanged (already `EffectiveRepr`-aware), as the spec states.
- **§3a Content-aware grid refresh** → Task 5.
- **§3b Group Type `IsParent`** → no code; already `isparent:true` (noted in File Structure).
- **§4 Live refresh on Load SRX (resync all)** → Task 4 (`ResyncAllPartsAsync`).
- **Out of scope** (hide "SRX" type when none loaded; ExSN Phase 2) → not implemented, by design.
- **Testing** → `LoadedSrxState.SetFromSlots`, `VisibleBoards`, `BuildRepr` are covered. (The spec listed an `Apply`-over-FQPs test; replaced with `BuildRepr` tests since FQP construction isn't a pattern in this suite — the decision logic is identical and fully covered.)
