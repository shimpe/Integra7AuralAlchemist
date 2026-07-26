# Undo, Redo and Compare Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user take back a parameter edit, and A/B the part they are editing against the sound it had when they opened it.

**Architecture:** Every edit in this application leaves through one of two doors — the friendly editors' `ParamInt`/`ParamString`/`ParamBool` wrappers, or the raw grid's `"ui2hw"` message bus. A journal records `(address, path, old display value, new display value)` at both doors; undo replays a step backwards through the ordinary write path, which is also what makes the UI follow. Compare reuses the tone snapshot machinery: capture the part's tone when it loads, and swap between that and the edited state.

**Tech Stack:** .NET 10, ReactiveUI, NUnit. No new dependencies.

---

## Background the implementer needs

**The two write doors, and why only these two.**

- `Src/ViewModels/SynthParam.cs` — `ParamInt`, `ParamString`, `ParamBool` each hold one `FullyQualifiedParameter` and a `ThrottledParameterWriter`. Their `Value` setters enqueue a write **only when `_suppress` is false**. `_suppress` is set while `ApplyFromModel` runs, which is how a value arriving *from* the device avoids being echoed back to it. That flag is exactly the "this is a user edit" signal the journal needs.
- `Src/ViewModels/MainWindowViewModel.cs`, `UpdateIntegraFromUiAsync` — the raw parameter grid's path. `DataTemplateProvider` posts an `UpdateMessageSpec` on the `"ui2hw"` bus for every control change.

Inbound device changes arrive on a **different** path — `"hw2ui"` → `DomainBase.ModifySingleParameterDisplayedValue` — and must never enter the journal. If they did, undo would fight the instrument's front panel.

**Why display strings.** `IParam.Snapshot()` already returns each wrapper's value in exactly the form its write sends: the integer for `ParamInt`, the on/off word for `ParamBool`, the string for `ParamString`. Applying a step is then `DomainBase.WriteToIntegraAsync(path, displayValue, lease)`, the same call the wrappers make. This is also the form the snapshot files use, so the two features agree about what a value *is*.

**Why undo does not need to touch the UI.** A wrapper subscribes to its `FullyQualifiedParameter`'s `PropertyChanged` and re-reads through `ApplyFromModel` with `_suppress` set. So writing the old value through the domain updates the model, the model notifies, the control moves, and nothing is echoed back. The raw grid's controls do the same via `DataTemplateProvider.BindToModel`. **Do not** add UI refresh calls; if a control does not move, the bug is elsewhere.

**Addressing.** A `DomainBase` is identified by three address names (`StartAddressName`, `OffsetAddressName`, `Offset2AddressName`) and `Integra7Domain.GetDomain(start, offset, offset2)` resolves them. Note `GetDomain` does **not** throw on an unknown triple — it logs and returns an unrelated domain (`Integra7Domain.cs:438-449`). The journal only ever stores triples it read off a live domain, so it cannot produce an unknown one; do not add a lookup that could.

---

## File structure

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/EditJournal.cs` (create) | `EditStep`, `EditJournal`: record, coalesce, undo/redo stacks, suppression, bound. Pure — no Avalonia, no MIDI. |
| `Tests/TestEditJournal.cs` (create) | The whole of the above. |
| `Src/ViewModels/SynthParam.cs` (modify) | Record from the three wrappers. |
| `Src/ViewModels/MainWindowViewModel.cs` (modify) | Record from the `"ui2hw"` path; `UndoAsync`/`RedoAsync` commands; Compare. |
| `Src/Views/MainWindow.axaml` (modify) | Undo/Redo buttons, Ctrl+Z / Ctrl+Y key bindings, Compare toggle. |

---

# Phase 1 — Undo and redo

## Task 1: The journal

**Files:**
- Create: `Src/Models/Services/EditJournal.cs`
- Test: `Tests/TestEditJournal.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class EditJournalTests
{
    private static DateTimeOffset _now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static EditJournal NewJournal() => new(() => _now);

    private static EditStep Step(string path, string oldValue, string newValue) =>
        new("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 1", path, oldValue, newValue);

    [Test]
    public void Undo_returns_the_step_reversed_and_redo_returns_it_forward()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));

        Assert.That(journal.CanUndo, Is.True);
        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(undo.ValueToApply, Is.EqualTo("100"), "undo applies the value from before the edit");

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True);
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo.ValueToApply, Is.EqualTo("110"), "redo applies the value the edit set");
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~EditJournalTests"`

Expected: FAIL — `EditJournal` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One reversible edit: which parameter, and what it displayed before and after. Values are
/// display strings because that is the form every write path already speaks -- see
/// <c>IParam.Snapshot()</c> and <c>DomainBase.WriteToIntegraAsync(path, displayValue, lease)</c>.</summary>
public sealed record EditStep(
    string Start, string Offset, string Offset2, string Path, string OldValue, string NewValue);

/// <summary>An <see cref="EditStep"/> together with the value the caller should now write.</summary>
public sealed record PendingEdit(EditStep Step, string ValueToApply)
{
    public string Path => Step.Path;
}

/// <summary>
/// The undo history. Records every edit the user makes, from either write path, and hands back the
/// value to write to take one back.
///
/// Pure -- no Avalonia, no MIDI -- so the whole of it is unit-tested. Applying a step is the caller's
/// job, because that needs a device and a lease.
/// </summary>
public sealed class EditJournal
{
    /// <summary>Consecutive edits to the same parameter within this window are one step. A knob drag
    /// is hundreds of setter calls; without this, undo would walk back through every intermediate
    /// value one at a time. Matches the write debounce, so a coalesced group is also one write.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(Constants.THROTTLE);

    /// <summary>How many steps to keep. A long session must not grow without bound; losing the
    /// oldest edits is the right thing to lose.</summary>
    public const int Capacity = 200;

    private readonly List<EditStep> _undo = [];
    private readonly List<EditStep> _redo = [];
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _lastRecordedAt;

    public EditJournal(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>True while a step is being applied. The write that undo performs comes back through
    /// the same setters that record, so without this an undo would record itself as a new edit and
    /// the history would never empty.</summary>
    public bool IsApplying { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised whenever <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    public void Record(EditStep step)
    {
        if (IsApplying) return;

        var at = _now();
        if (_undo.Count > 0 && IsSameTarget(_undo[^1], step) && at - _lastRecordedAt <= CoalesceWindow)
            // Same parameter, still the same gesture: keep the value it had before the gesture began.
            _undo[^1] = _undo[^1] with { NewValue = step.NewValue };
        else
            _undo.Add(step);

        _lastRecordedAt = at;
        if (_undo.Count > Capacity) _undo.RemoveAt(0);
        // A new edit makes the redo history unreachable -- it described a future that no longer follows.
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool TryUndo(out PendingEdit pending)
    {
        pending = null!;
        if (_undo.Count == 0) return false;

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        pending = new PendingEdit(step, step.OldValue);
        Changed?.Invoke();
        return true;
    }

    public bool TryRedo(out PendingEdit pending)
    {
        pending = null!;
        if (_redo.Count == 0) return false;

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        pending = new PendingEdit(step, step.NewValue);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Run <paramref name="apply"/> with recording switched off.</summary>
    public async System.Threading.Tasks.Task ApplyAsync(Func<System.Threading.Tasks.Task> apply)
    {
        IsApplying = true;
        try { await apply(); }
        finally { IsApplying = false; }
    }

    /// <summary>Forget everything. Used when the instrument's state stops being the one the history
    /// describes -- a Studio Set change, a preset change, a snapshot restore.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private static bool IsSameTarget(EditStep a, EditStep b) =>
        a.Start == b.Start && a.Offset == b.Offset && a.Offset2 == b.Offset2 && a.Path == b.Path;

    /// <summary>The one journal the application records into. Ambient like
    /// <c>LoadedSrxState.Default</c> and <c>WaveformBanks.Default</c>: the alternative is threading it
    /// through the constructor of every parameter wrapper in fifteen editor view models.</summary>
    public static EditJournal Default { get; } = new();
}
```

Add `using Integra7AuralAlchemist.Models.Data;` for `Constants`.

- [ ] **Step 4: Run the test to see it pass**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~EditJournalTests"`

Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/EditJournal.cs Tests/TestEditJournal.cs
git commit -m "feat: an undo history for parameter edits"
```

## Task 2: Pin the behaviour that makes it usable

**Files:**
- Modify: `Tests/TestEditJournal.cs`

- [ ] **Step 1: Write the tests**

Add to `EditJournalTests`:

```csharp
    [Test]
    public void A_gesture_on_one_parameter_is_one_step()
    {
        // A knob drag is hundreds of setter calls. Undo must return the value from before the drag,
        // not walk back through every intermediate.
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        _now = _now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));
        _now = _now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "102", "103"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo.ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the three calls were one gesture");
    }

    [Test]
    public void A_pause_starts_a_new_step()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        _now = _now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first.ValueToApply, Is.EqualTo("101"));
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void Editing_a_different_parameter_starts_a_new_step()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        journal.Record(Step("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out var pan), Is.True);
        Assert.That(pan.Path, Is.EqualTo("Studio Set Part/Part Pan"));
        Assert.That(journal.TryUndo(out var level), Is.True);
        Assert.That(level.Path, Is.EqualTo("Studio Set Part/Part Level"));
    }

    [Test]
    public void The_same_path_in_a_different_part_is_a_different_parameter()
    {
        // Every part's parameters share a path; only the address tells them apart. Coalescing on the
        // path alone would merge an edit on part 1 with one on part 2 and undo the wrong part.
        var journal = NewJournal();
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", "Studio Set Part/Part Level", "100", "101"));
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 2", "Studio Set Part/Part Level", "50", "51"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 2"));
        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 1"));
    }

    [Test]
    public void A_new_edit_drops_the_redo_history()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);
        Assert.That(journal.CanRedo, Is.True);

        journal.Record(Step("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.CanRedo, Is.False, "the redone future no longer follows from this history");
    }

    [Test]
    public void Nothing_is_recorded_while_a_step_is_being_applied()
    {
        // The write undo performs comes back through the same setters that record. Without this the
        // history would never empty.
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.ApplyAsync(() =>
        {
            journal.Record(Step("Studio Set Part/Part Level", "110", "100"));
            return System.Threading.Tasks.Task.CompletedTask;
        }).GetAwaiter().GetResult();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True, "the redo the undo created must survive applying it");
    }

    [Test]
    public void The_history_is_bounded()
    {
        var journal = NewJournal();
        for (var i = 0; i < EditJournal.Capacity + 50; i++)
        {
            journal.Record(Step($"Studio Set Part/Parameter {i}", $"{i}", $"{i + 1}"));
            _now = _now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        }

        var undone = 0;
        while (journal.TryUndo(out _)) undone++;
        Assert.That(undone, Is.EqualTo(EditJournal.Capacity));
    }

    [Test]
    public void Clearing_forgets_both_directions()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.Clear();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.False);
    }
```

- [ ] **Step 2: Run them**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~EditJournalTests"`

Expected: PASS, 9 tests. If `The_same_path_in_a_different_part_is_a_different_parameter` fails, `IsSameTarget` is comparing the path only — fix the implementation, not the test.

- [ ] **Step 3: Commit**

```bash
git add Tests/TestEditJournal.cs
git commit -m "test: pin coalescing, redo invalidation and the history bound"
```

## Task 3: Record from the friendly editors

**Files:**
- Modify: `Src/ViewModels/SynthParam.cs`

Each wrapper records inside the `if (!_suppress)` branch it already has — that branch *is* "this came from the user, not from the device".

- [ ] **Step 1: Record from `ParamInt`**

In `ParamInt.Value`'s setter, replace:

```csharp
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) Enqueue();
```

with:

```csharp
            var before = Snapshot();
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress)
            {
                // Inside the !_suppress branch on purpose: that is what distinguishes an edit the user
                // made from one ApplyFromModel is echoing back from the device. Undoing the latter
                // would fight the instrument's front panel.
                EditJournal.Default.Record(new EditStep(_domain.StartAddressName, _domain.OffsetAddressName,
                    _domain.Offset2AddressName, _p.ParSpec.Path, before, Snapshot()));
                Enqueue();
            }
```

Add `using Integra7AuralAlchemist.Models.Services;` if it is not already there.

- [ ] **Step 2: Record from `ParamString` and `ParamBool`**

Both have the same shape — a `this.RaiseAndSetIfChanged` followed by `if (!_suppress) _writer.Enqueue(...)`. Capture `Snapshot()` before the raise and record inside the branch, exactly as above. Do not restructure their write lambdas.

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj`

Expected: Build succeeded. If `Src\bin` is locked, build with `-p:OutputPath=<scratch>/`.

- [ ] **Step 4: Run the whole suite**

Expected: unchanged from the baseline. Nothing existing asserts on journal contents, so a change here means a real regression.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/SynthParam.cs
git commit -m "feat: record friendly-editor edits in the undo history"
```

## Task 4: Record from the raw parameter grid

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Record before the value is overwritten**

In `UpdateIntegraFromUiAsync`, the old value must be read *before* the assignment on the following line:

```csharp
        var p = s.Par;
        UserActionLog.Action($"edit parameter '{p.ParSpec.Path}' -> '{s.DisplayValue}'");
        // Before the assignment below: after it, the value it replaced is gone.
        EditJournal.Default.Record(new EditStep(p.Start, p.Offset, p.Offset2, p.ParSpec.Path,
            p.StringValue, s.DisplayValue));
        p.StringValue = s.DisplayValue;
```

`EditJournal.Record` ignores the call while a step is being applied, so undo writing through this path cannot record itself.

- [ ] **Step 2: Build and run the suite**

Expected: baseline, unchanged.

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs
git commit -m "feat: record raw-grid edits in the undo history"
```

## Task 5: Apply a step

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add the commands**

Beside `SaveStudioSetAsync`:

```csharp
    [Reactive] private bool _canUndo;
    [Reactive] private bool _canRedo;

    [ReactiveCommand]
    public async Task UndoAsync()
    {
        if (!EditJournal.Default.TryUndo(out var pending)) return;
        UserActionLog.Action($"undo '{pending.Path}' -> '{pending.ValueToApply}'");
        await ApplyEditAsync(pending);
    }

    [ReactiveCommand]
    public async Task RedoAsync()
    {
        if (!EditJournal.Default.TryRedo(out var pending)) return;
        UserActionLog.Action($"redo '{pending.Path}' -> '{pending.ValueToApply}'");
        await ApplyEditAsync(pending);
    }

    /// <summary>Write one journal step back to the instrument. Deliberately the ordinary write path:
    /// the parameter wrappers and the raw grid's controls both follow their FullyQualifiedParameter
    /// through INotifyPropertyChanged, so the screen catches up on its own and no refresh is needed.
    /// </summary>
    private async Task ApplyEditAsync(PendingEdit pending)
    {
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        try
        {
            var domain = communicator.GetDomain(pending.Step.Start, pending.Step.Offset, pending.Step.Offset2);
            await EditJournal.Default.ApplyAsync(async () =>
            {
                await using var lease = await api.BeginConversationAsync($"undo {pending.Path}");
                await domain.WriteToIntegraAsync(pending.Path, pending.ValueToApply, lease);
            });
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"apply '{pending.Path}'", e.ToString());
        }
    }
```

- [ ] **Step 2: Keep the button states current**

In the constructor, next to the other subscriptions:

```csharp
        // The journal is ambient, so nothing else tells the buttons when it changes.
        EditJournal.Default.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            CanUndo = EditJournal.Default.CanUndo;
            CanRedo = EditJournal.Default.CanRedo;
        });
```

`Changed` fires from whichever thread recorded, and a knob drag records from the UI thread while a device echo may not — hence the `Post`.

- [ ] **Step 3: Forget the history when it stops describing the instrument**

A Studio Set change, a snapshot restore or a preset change replaces state the history refers to; undoing across one would write a value into a patch that never had it. Call `EditJournal.Default.Clear()` at each of:

- `UpdateUiFromIntegraAsync`, in the `StudioSetSelectors.Contains(...)` branch, beside the existing `ResyncAllPartsAsync`.
- `LoadStudioSetAsync` and `LoadToneAsync`, after a successful restore.
- `ChangePresetAndReloadAsync` in `PartViewModel`, after the reload.

Each gets a one-line comment saying which of those it is.

- [ ] **Step 4: Build and run the suite**

Expected: baseline, unchanged.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/ViewModels/PartViewModel.cs
git commit -m "feat: undo and redo a parameter edit"
```

## Task 6: The buttons and the shortcuts

**Files:**
- Modify: `Src/Views/MainWindow.axaml`

- [ ] **Step 1: Add the buttons**

In the toolbar `StackPanel`, before `Save User Tone`:

```xml
                        <Button Command="{Binding UndoAsync}" IsEnabled="{Binding CanUndo}"
                                ToolTip.Tip="Take back the last parameter edit (Ctrl+Z)">
                            Undo
                        </Button>
                        <Button Command="{Binding RedoAsync}" IsEnabled="{Binding CanRedo}"
                                ToolTip.Tip="Reapply the edit that was taken back (Ctrl+Y)">
                            Redo
                        </Button>
```

These bind `CanUndo`/`CanRedo` rather than `Connected`/`!IsSyncing` like the others: an empty history is the reason to disable them, and the journal is empty until something has been edited, which cannot happen while disconnected.

- [ ] **Step 2: Add the key bindings**

The window has none today. Add to `MainWindow.axaml`, inside `<Window>` before `<Window.Styles>`:

```xml
    <Window.KeyBindings>
        <KeyBinding Gesture="Ctrl+Z" Command="{Binding UndoAsync}" />
        <KeyBinding Gesture="Ctrl+Y" Command="{Binding RedoAsync}" />
        <KeyBinding Gesture="Ctrl+Shift+Z" Command="{Binding RedoAsync}" />
    </Window.KeyBindings>
```

Both redo gestures, because both are conventional and neither is used elsewhere here.

- [ ] **Step 3: Build**

Expected: Build succeeded, no `AVLN2000`.

- [ ] **Step 4: Check the encoding**

`MainWindow.axaml` is UTF-8 and holds `…` and `—`. Use the Edit tool, never PowerShell `Get-Content -Raw`/`Set-Content`, which default to ANSI in PS 5.1 and corrupt them.

Run: `grep -c "â€" Src/Views/MainWindow.axaml` — expected `0`.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/MainWindow.axaml
git commit -m "feat: Undo and Redo buttons and keyboard shortcuts"
```

## Task 7: Hardware verification

**Files:** none — a manual pass.

- [ ] **Step 1: One edit** — move a knob, Ctrl+Z. The knob returns and the instrument follows. Ctrl+Y puts it back.
- [ ] **Step 2: A drag is one step** — drag a knob slowly across its range, then Ctrl+Z once. It returns to where the drag started, not one step back.
- [ ] **Step 3: Both doors** — edit in a friendly editor, then the same parameter in its Advanced grid, then undo twice. Both come back, in order.
- [ ] **Step 4: The right part** — edit Part 1's level, then Part 2's, then undo twice. Each undo moves the part it belongs to.
- [ ] **Step 5: The history clears** — make an edit, change the Studio Set on the front panel, and check Undo is disabled. Undoing across that would write into a patch that never had the value.
- [ ] **Step 6: Nothing records from the panel** — change a value on the instrument's front panel and check Undo stays disabled.

---

# Phase 2 — Compare

> **This phase is a sketch, not an executable plan.** Tasks 1–7 above give exact code for every step;
> tasks 8–10 give intent and constraints only. Do not hand them to an implementer as they stand —
> write the task-level plan once Phase 1 is merged, when the journal's real shape is known and it is
> clear how much of `RestoreToneAsync` the toggle can reuse. It is recorded here so the design
> decisions taken while they were fresh are not lost.

Compare is a small feature sitting on tone snapshots, and keeping it out of the undo commits keeps both reviewable.

## Task 8: Hold the tone the part opened with

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs`

- [ ] **Step 1: Capture on load**

`StudioSetSnapshotService.CaptureToneAsync(domain, partNo, toneType, name, lease)` already exists and is hardware-verified. At the end of `InitializeDeferredPartStateAsync`, once the tone domains have been read, capture the part's tone into a field — the reads have just happened, so pass the lease that is already open if there is one, and note in a comment that this costs no extra round trip if so.

Guard it: no capture for a part with no resolved preset or an unknown tone type (`ToneDomainNames.IsKnownToneType`).

- [ ] **Step 2: Build, run the suite, commit**

```bash
git commit -m "feat: remember the tone a part opened with"
```

## Task 9: The toggle

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs`, `Src/Views/MainWindow.axaml`

- [ ] **Step 1: Swap between the two states**

A `Compare` toggle that, when switched on, captures the *current* (edited) tone, restores the opening one, and marks itself on; when switched off, restores the edited one. Both restores go through `StudioSetSnapshotService.RestoreToneAsync` with the part's own tone type, and both call `EditJournal.Default.Clear()` — the history describes edits to a state that is no longer loaded.

Show which is being heard: the toggle's content reads "Comparing — original" while on.

- [ ] **Step 2: Refuse when there is nothing to compare with** — no captured tone, or the part's tone type has changed since (the user selected a different preset). Disable rather than fail.

- [ ] **Step 3: Build, run the suite, commit**

## Task 10: Hardware verification for Compare

- [ ] Edit a tone, toggle Compare on — the original sound returns. Toggle off — the edits return.
- [ ] Toggle Compare, then change preset, and check Compare disables itself rather than restoring a tone into the wrong patch.

---

## Known limitations to state in the commit messages

- **Undo is a single global history**, not per part. Ctrl+Z takes back the last edit made anywhere. Per-part histories would need the journal keyed by part and the buttons bound to the selected one; nothing here prevents that later.
- **Undo does not cross a patch change.** The history is cleared by a Studio Set change, a preset change and a snapshot restore, because a step refers to state that is no longer loaded.
- **Coalescing is time-based**, so two deliberate edits to the same parameter inside 250 ms merge into one step. The alternative — pointer capture boundaries — would only work for the knobs and not for the raw grid's text boxes.
