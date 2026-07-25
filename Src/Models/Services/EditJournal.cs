using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Integra7AuralAlchemist.Models.Data;

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
///
/// Record is called from two different threads: the friendly editors call it from <c>SynthParam</c>'s
/// setters on the UI thread, while the raw grid's path
/// (<c>MainWindowViewModel.UpdateIntegraFromUiAsync</c>) is reached through
/// <c>MessageBus.Current.Listen&lt;UpdateMessageSpec&gt;("ui2hw").Throttle(...)</c>, and <c>Throttle</c>
/// with no scheduler runs on the thread pool. <see cref="_gate"/> below serializes all mutation for
/// that reason. <see cref="Changed"/> is raised outside the lock and therefore fires from whichever of
/// those two threads made the change -- a UI listener must marshal back to the UI thread itself.
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

    // Guards _undo, _redo, _lastRecordedAt and _isApplying. Record runs on the UI thread and on the
    // thread pool (see the class comment); List<T> is not thread-safe, so without this two concurrent
    // Record calls can corrupt the lists outright (reproduced in
    // TestEditJournal.Concurrent_records_from_many_threads_do_not_corrupt_the_history). The lists are
    // tiny and uncontended, so a plain lock costs nothing.
    private readonly object _gate = new();

    private readonly List<EditStep> _undo = [];
    private readonly List<EditStep> _redo = [];
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _lastRecordedAt;
    private bool _isApplying;

    public EditJournal(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>True while a step is being applied. The write that undo performs comes back through
    /// the same setters that record, so without this an undo would record itself as a new edit and
    /// the history would never empty.</summary>
    public bool IsApplying { get { lock (_gate) return _isApplying; } }

    public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }

    /// <summary>Raised whenever <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed. Fires
    /// from whichever thread made the change -- see the class remarks.</summary>
    public event Action? Changed;

    public void Record(EditStep step)
    {
        lock (_gate)
        {
            if (_isApplying) return;

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
        }
        Changed?.Invoke();
    }

    public bool TryUndo([MaybeNullWhen(false)] out PendingEdit pending)
    {
        EditStep step;
        lock (_gate)
        {
            if (_undo.Count == 0)
            {
                pending = default;
                return false;
            }

            step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(step);
            // The step now on top of _undo (if any) was not the one Record last touched, so its
            // recorded time must not be trusted as "still the same gesture" by the next Record call --
            // see the regression this guards in TestEditJournal.
            _lastRecordedAt = default;
        }
        pending = new PendingEdit(step, step.OldValue);
        Changed?.Invoke();
        return true;
    }

    public bool TryRedo([MaybeNullWhen(false)] out PendingEdit pending)
    {
        EditStep step;
        lock (_gate)
        {
            if (_redo.Count == 0)
            {
                pending = default;
                return false;
            }

            step = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(step);
            // Same reasoning as TryUndo: whatever Record does next must start a fresh step, not assume
            // it is continuing the gesture that originally produced the step redo just reinstated.
            _lastRecordedAt = default;
        }
        pending = new PendingEdit(step, step.NewValue);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Run <paramref name="apply"/> with recording switched off. Restores the previous
    /// suppression state on exit rather than clearing it, so a nested call does not unsuppress a call
    /// still in progress further up the stack.
    ///
    /// The suppression window spans real device I/O, including waiting on a MIDI lease, so a genuine
    /// user edit made while an undo/redo is in flight is silently dropped from the history (it arrives
    /// while <see cref="IsApplying"/> is true and <see cref="Record"/> discards it). Callers should
    /// keep the awaited region as small as they can.</summary>
    public async System.Threading.Tasks.Task ApplyAsync(Func<System.Threading.Tasks.Task> apply)
    {
        bool was;
        lock (_gate) { was = _isApplying; _isApplying = true; }
        try { await apply(); }
        finally { lock (_gate) { _isApplying = was; } }
    }

    /// <summary>Forget everything. Used when the instrument's state stops being the one the history
    /// describes -- a Studio Set change, a preset change, a snapshot restore.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
        }
        Changed?.Invoke();
    }

    private static bool IsSameTarget(EditStep a, EditStep b) =>
        a.Start == b.Start && a.Offset == b.Offset && a.Offset2 == b.Offset2 && a.Path == b.Path;

    /// <summary>The one journal the application records into. Ambient like
    /// <c>LoadedSrxState.Default</c> and <c>WaveformBanks.Default</c>: the alternative is threading it
    /// through the constructor of every parameter wrapper in fifteen editor view models.</summary>
    public static EditJournal Default { get; } = new();
}
