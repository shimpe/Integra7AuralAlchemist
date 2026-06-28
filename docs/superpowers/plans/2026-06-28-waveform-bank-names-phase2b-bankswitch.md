# Waveform Bank Names — Phase 2b (bank-switch UX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the bank-switch UX on top of the Phase-2a display flip: (1) the advanced raw grid's wave combo rebuilds its item list when the bank changes mid-edit, (2) switching a wave group's Type/ID resets an out-of-range wave number to the new bank's first wave (both surfaces), and (3) the friendly editors gain an SRX-board (Group ID) selector so users can switch banks there.

**Architecture:** Both surfaces already re-read on a Group-Type/ID change (`IsParent`, since Phase 1) — friendly via `SynthParam`, advanced via `MainWindowViewModel.UpdateIntegraFromUiAsync`. Phase 2b adds: a combo-item rebuild in `DataTemplateProvider`'s apply callback; a shared `WaveOutOfRangeReset` helper called from both write paths' `IsParent` branch (which writes the corrected raw wave number directly via the single-arg `WriteToIntegraAsync`); and a `WaveGroupID` wrapper + a conditionally-visible SRX combo in the two friendly wave views.

**Tech Stack:** Avalonia 12 + ReactiveUI; the Phase-1/2a `WaveformBanks`/`WaveBankRegistry`/`EffectiveRepr`. Build/test with the user-local SDK in Release (Debug exe file-lock = MSB3027/MSB3021, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: Advanced wave combo rebuilds items on bank change

**Files:**
- Modify: `Src/DataTemplates/DataTemplateProvider.cs` (the combo `BindToModel` apply callback)

The combo's apply callback (run on each `StringValue` change, i.e. after a re-read) currently only re-sets `SelectedItem`. Make it also rebuild `Items` from the current effective repr **when the effective repr's contents differ** from what's shown — so a wave param's combo reflects the newly-selected bank. Guarded so non-wave combos (stable repr) don't churn.

- [ ] **Step 1: Update the combo's `BindToModel` apply callback**

In `Src/DataTemplates/DataTemplateProvider.cs`, find the combo case (inside `if (repr != null)`, the `else` branch):

```csharp
            else
            {
                ComboBox c = new();
                foreach (var el in repr) c.Items.Add(el.Value);
                c.SelectedItem = p.StringValue;
                c.SelectionChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
                };
                BindToModel(c, p, () => c.SelectedItem = p.StringValue, () => suppressPush, v => suppressPush = v);
                return c;
            }
```

Replace it with (the apply callback now rebuilds items from the live effective repr if it changed):

```csharp
            else
            {
                ComboBox c = new();
                foreach (var el in repr) c.Items.Add(el.Value);
                c.SelectedItem = p.StringValue;
                c.SelectionChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
                };
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
                return c;
            }
```

(The `c.Items.Count != cur.Count` guard rebuilds only when the bank actually changes size — cheap for the common case and correct for bank switches, which always change the entry count between INT/SRX boards. A same-size bank switch is not possible here since each bank has a distinct entry count.)

- [ ] **Step 2: Build + test**

Run the Build command (expect 0 errors) and the Test command (expect `Passed! - Failed: 0, Passed: 257`).

- [ ] **Step 3: Commit**

```bash
git add Src/DataTemplates/DataTemplateProvider.cs
git commit -m "feat: advanced wave combo rebuilds items when its bank changes"
```

---

### Task 2: Out-of-range reset on bank switch (both surfaces)

**Files:**
- Create: `Src/Models/Services/WaveOutOfRangeReset.cs`
- Create: `Tests/TestWaveOutOfRangeReset.cs`
- Modify: `Src/ViewModels/SynthParam.cs` (friendly write path — `ParamString.Value` + `ParamInt.Enqueue`)
- Modify: `Src/ViewModels/MainWindowViewModel.cs` (advanced write path — `UpdateIntegraFromUiAsync`)

When a wave group's Type/ID is written and a governed wave number is no longer valid in the new bank, set it to the bank's first wave and write it. Triggered only on a discriminator edit (never on load), from both write paths' existing `IsParent` branch.

- [ ] **Step 1: Write the failing test for the pure decision**

Create `Tests/TestWaveOutOfRangeReset.cs`:

```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveOutOfRangeReset
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "A", [2] = "B" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "K1", [1] = "K2" }, // only 0,1 valid
    });

    [Test]
    public void NeedsReset_TrueWhenNumberNotInNewBank()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 1, 2), Is.True); // SRX1 has no #2

    [Test]
    public void NeedsReset_FalseWhenNumberInBank()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 1, 1), Is.False);

    [Test]
    public void NeedsReset_FalseWhenBankMissing()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 9, 0), Is.False); // SRX9 absent → no reset

    [Test]
    public void IsWaveGroupDiscriminator_RecognizesTypeAndId()
    {
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Synth Tone Partial/Wave Group Type"), Is.True);
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Drum Kit Partial/WMT2 Wave Group ID"), Is.True);
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Synth Tone Partial/Wave Number L (Mono)"), Is.False);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestWaveOutOfRangeReset"`. Expected: FAIL (type does not exist).

- [ ] **Step 3: Implement the helper**

Create `Src/Models/Services/WaveOutOfRangeReset.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>On a wave-group Type/ID change, resets any governed wave number that is out of range for the
/// newly-selected bank to that bank's first wave. Edit-triggered only (called from the write paths'
/// IsParent branch), so loads never auto-correct a stored value.</summary>
public static class WaveOutOfRangeReset
{
    /// <summary>True if <paramref name="path"/> is one of the wave-group discriminators (a Type or ID).</summary>
    public static bool IsWaveGroupDiscriminator(string path)
    {
        foreach (var sib in WaveBankRegistry.Entries.Values)
            if (sib.TypePath == path || sib.IdPath == path) return true;
        return false;
    }

    /// <summary>True if the selected bank exists and does NOT contain <paramref name="number"/>.</summary>
    public static bool NeedsReset(WaveformBanks banks, string groupType, int groupId, int number)
    {
        var bank = banks.Bank(groupType, groupId);
        return bank != null && !bank.ContainsKey(number);
    }

    /// <summary>If <paramref name="edited"/> is a wave-group discriminator, reset each governed wave whose
    /// number is out of range for the new bank to the bank's first wave (writing it to hardware).</summary>
    public static async Task ApplyAsync(DomainBase domain, FullyQualifiedParameter edited, WaveformBanks banks)
    {
        if (!IsWaveGroupDiscriminator(edited.ParSpec.Path)) return;

        var byPath = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in domain.GetRelevantParameters(true, true)) byPath[p.ParSpec.Path] = p;

        foreach (var (wavePath, sib) in WaveBankRegistry.Entries)
        {
            if (sib.TypePath != edited.ParSpec.Path && sib.IdPath != edited.ParSpec.Path) continue;
            if (!byPath.TryGetValue(wavePath, out var wave)
                || !byPath.TryGetValue(sib.TypePath, out var type)
                || !byPath.TryGetValue(sib.IdPath, out var id))
                continue;

            var groupId = (int)id.RawNumericValue;
            var number = (int)wave.RawNumericValue;
            if (!NeedsReset(banks, type.StringValue, groupId, number)) continue;

            var bank = banks.Bank(type.StringValue, groupId)!;
            var first = banks.FirstWave(type.StringValue, groupId);
            wave.RawNumericValue = first;
            wave.StringValue = bank.TryGetValue(first, out var name)
                ? name : first.ToString(CultureInfo.InvariantCulture);
            wave.EffectiveRepr = bank;
            await domain.WriteToIntegraAsync(wave.ParSpec.Path); // single-arg: writes the FQP's current raw value
        }
    }
}
```

- [ ] **Step 4: Run the filtered tests to verify they pass**

Run the Test command with `--filter "FullyQualifiedName~TestWaveOutOfRangeReset"`. Expected: PASS (4 tests). If `domain.GetRelevantParameters(true, true)` or the single-arg `WriteToIntegraAsync(string)` doesn't exist with that signature, STOP and report BLOCKED with the actual signatures.

- [ ] **Step 5: Wire into the friendly write path**

In `Src/ViewModels/SynthParam.cs`, in `ParamString`'s `Value` setter, find:

```csharp
            if (!_suppress) _writer.Enqueue(_key, async () =>
            {
                await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value);
                if (_p.ParSpec.IsParent) await _domain.ReadFromIntegraAsync(); // resync dependents
            });
```

Replace with:

```csharp
            if (!_suppress) _writer.Enqueue(_key, async () =>
            {
                await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value);
                if (_p.ParSpec.IsParent)
                {
                    await WaveOutOfRangeReset.ApplyAsync(_domain, _p, WaveformBanks.Default);
                    await _domain.ReadFromIntegraAsync(); // resync dependents
                }
            });
```

And in `ParamInt`'s `Enqueue`, find:

```csharp
    private void Enqueue() => _writer.Enqueue(_key, async () =>
    {
        await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(CultureInfo.InvariantCulture));
        // A parent param reinterprets dependent slots on the hardware; re-read so dependent controls
        // show correct values (mirrors the advanced view's IsParent resync). See memory
        // conditional-parameters-and-write-races.
        if (_p.ParSpec.IsParent) await _domain.ReadFromIntegraAsync();
    });
```

Replace the final `if` line with:

```csharp
        if (_p.ParSpec.IsParent)
        {
            await WaveOutOfRangeReset.ApplyAsync(_domain, _p, WaveformBanks.Default);
            await _domain.ReadFromIntegraAsync();
        }
```

(`SynthParam.cs` already has `using Integra7AuralAlchemist.Models.Services;` for `ThrottledParameterWriter`; if not, add it.)

- [ ] **Step 6: Wire into the advanced write path**

In `Src/ViewModels/MainWindowViewModel.cs`, find the `IsParent` branch in `UpdateIntegraFromUiAsync`:

```csharp
        if (p.ParSpec.IsParent)
        {
            await _integra7Communicator?.GetDomain(p).ReadFromIntegraAsync();
            ForceUiRefresh(p);
        }
```

Replace with:

```csharp
        if (p.ParSpec.IsParent)
        {
            var domain = _integra7Communicator?.GetDomain(p);
            if (domain != null)
            {
                await Integra7AuralAlchemist.Models.Services.WaveOutOfRangeReset.ApplyAsync(
                    domain, p, Integra7AuralAlchemist.Models.Services.WaveformBanks.Default);
                await domain.ReadFromIntegraAsync();
            }
            ForceUiRefresh(p);
        }
```

(If `MainWindowViewModel.cs` already has `using Integra7AuralAlchemist.Models.Services;`, use the short names instead of the fully-qualified ones. Verify the exact shape of the existing `IsParent` block first; if it differs — e.g. no null-conditional — adapt the replacement to match, keeping the same null-safety as the original.)

- [ ] **Step 7: Build + test**

Run the Build command (expect 0 errors) and the Test command (expect `Passed! - Failed: 0, Passed: 261` — 257 + 4 new).

- [ ] **Step 8: Commit**

```bash
git add Src/Models/Services/WaveOutOfRangeReset.cs Tests/TestWaveOutOfRangeReset.cs Src/ViewModels/SynthParam.cs Src/ViewModels/MainWindowViewModel.cs
git commit -m "feat: reset out-of-range wave number to first-in-bank on bank switch (both surfaces)"
```

---

### Task 3: Friendly SRX-board (Group ID) selector

**Files:**
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`
- Modify: `Src/ViewModels/PCMDrumWmtLayerViewModel.cs`
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`
- Modify: `Src/Views/PCMDrumWmtLayerView.axaml`

Add a `WaveGroupID` wrapper (SRX board 1..12) + an `IsSrx` flag, and an SRX-board combo shown only when the bank is SRX. (Internal keeps Group ID 0 implicitly.)

- [ ] **Step 1: `PCMPartialViewModel` — expose `WaveGroupID` + `IsSrx`**

In `Src/ViewModels/PCMPartialViewModel.cs`, after the `WaveGroupType` property declaration add:

```csharp
    public ParamString WaveGroupID { get; }   // SRX board 1..12 (Internal = 0)
```

In the constructor, immediately after `WaveGroupType = PS("Wave Group Type");`, add:

```csharp
        WaveGroupID = PS("Wave Group ID", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" });
        WaveGroupType.PropertyChanged += OnSummaryChanged;
```

Then add an `IsSrx` computed property next to the other computed properties (e.g. near `FilterCurveMode`):

```csharp
    /// <summary>True when the wave bank is an SRX board (so the SRX-board selector is shown).</summary>
    public bool IsSrx => WaveGroupType.Value == "SRX";
```

And in `OnSummaryChanged` (the handler that raises the derived properties), add:

```csharp
        this.RaisePropertyChanged(nameof(IsSrx));
```

Add `WaveGroupID` to the `_editable` array (wherever the wave params are listed) so copy/paste captures it.

- [ ] **Step 2: `PCMDrumWmtLayerViewModel` — same additions**

In `Src/ViewModels/PCMDrumWmtLayerViewModel.cs`, after `WaveGroupType` add the `WaveGroupID` property; in the constructor after `WaveGroupType = PS("Wave Group Type");` add:

```csharp
        WaveGroupID = PS("Wave Group ID", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" });
        WaveGroupType.PropertyChanged += OnLayerSummaryChanged;
```

Add an `IsSrx` property:

```csharp
    public bool IsSrx => WaveGroupType.Value == "SRX";
```

In `OnLayerSummaryChanged`, also raise it:

```csharp
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(IsSrx)));
```

(Place the `RaisePropertyChanged(nameof(IsSrx))` alongside the existing `Summary` raise; keep it on the UI thread as the existing handler does.) Add `WaveGroupID` to this layer's `Params` list so copy/paste captures it.

- [ ] **Step 3: `PCMSynthToneEditorView` — add the SRX combo**

In `Src/Views/PCMSynthToneEditorView.axaml`, find the Bank combo:

```xml
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="Bank" Classes="sliderLabel" ToolTip.Tip="Wave Group Type"/>
                                                <ComboBox ItemsSource="{Binding WaveGroupType.Options}"
                                                          SelectedItem="{Binding WaveGroupType.Value, Mode=TwoWay}"/>
                                            </StackPanel>
```

Add immediately after that `</StackPanel>`:

```xml
                                            <StackPanel Spacing="2" IsVisible="{Binding IsSrx}">
                                                <TextBlock Text="SRX" Classes="sliderLabel" ToolTip.Tip="Wave Group ID (SRX board)"/>
                                                <ComboBox MaxDropDownHeight="300" ItemsSource="{Binding WaveGroupID.Options}"
                                                          SelectedItem="{Binding WaveGroupID.Value, Mode=TwoWay}"/>
                                            </StackPanel>
```

- [ ] **Step 4: `PCMDrumWmtLayerView` — add the SRX combo**

In `Src/Views/PCMDrumWmtLayerView.axaml`, find the Bank combo:

```xml
                    <StackPanel Spacing="2" Width="150" Margin="0,0,16,8">
                        <TextBlock Text="Bank" Classes="sliderLabel" ToolTip.Tip="Wave Group Type"/>
                        <ComboBox HorizontalAlignment="Stretch" ItemsSource="{Binding WaveGroupType.Options}"
                                  SelectedItem="{Binding WaveGroupType.Value, Mode=TwoWay}"/>
                    </StackPanel>
```

Add immediately after that `</StackPanel>`:

```xml
                    <StackPanel Spacing="2" Width="150" Margin="0,0,16,8" IsVisible="{Binding IsSrx}">
                        <TextBlock Text="SRX" Classes="sliderLabel" ToolTip.Tip="Wave Group ID (SRX board)"/>
                        <ComboBox MaxDropDownHeight="300" HorizontalAlignment="Stretch" ItemsSource="{Binding WaveGroupID.Options}"
                                  SelectedItem="{Binding WaveGroupID.Value, Mode=TwoWay}"/>
                    </StackPanel>
```

- [ ] **Step 5: Build + test**

Run the Build command (expect 0 errors; compiled XAML validates `WaveGroupID`/`IsSrx`) and the Test command (expect `Passed! - Failed: 0, Passed: 261`).

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/PCMPartialViewModel.cs Src/ViewModels/PCMDrumWmtLayerViewModel.cs Src/Views/PCMSynthToneEditorView.axaml Src/Views/PCMDrumWmtLayerView.axaml
git commit -m "feat: friendly SRX-board (Wave Group ID) selector, shown when bank is SRX"
```

---

## Done criteria

Switching a wave group's bank (Type/ID) in either surface refreshes the wave list to the new bank and, if the stored wave number is out of range, resets it to that bank's first wave. The advanced raw grid's wave combo rebuilds its items on the change. The friendly editors show an SRX-board selector when the bank is SRX, so users can pick any INT/SRX wave end-to-end. Build green, 261 tests passing. **This completes the Group-Type/ID-aware waveform names feature** (Phases 1 + 2a + 2b).
