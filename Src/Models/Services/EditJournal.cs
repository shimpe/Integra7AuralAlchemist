using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter's before and after, and the address that resolves it. Values are display
/// strings because that is the form every write path already speaks -- see <c>IParam.Snapshot()</c> and
/// <c>DomainBase.WriteToIntegraAsync(path, displayValue, lease)</c>.</summary>
public sealed record ParameterChange(
    string Start, string Offset, string Offset2, string Path, string OldValue, string NewValue);

/// <summary>One undo step: everything a single gesture changed. A knob drag is one change; dragging an
/// envelope handle is two (a level from the pointer's Y, a time from its X -- see
/// <c>MultiStageEnvelopeControl.OnPointerMoved</c>), and undoing it has to put both back or the handle
/// does not return to where it was. Changes are in the order the gesture first touched each
/// parameter.</summary>
public sealed record EditStep(IReadOnlyList<ParameterChange> Changes);

/// <summary>Which way a step is being applied. Undo writes each change's <c>OldValue</c>, redo its
/// <c>NewValue</c>.</summary>
public enum EditDirection
{
    Undo,
    Redo
}

/// <summary>An <see cref="EditStep"/> together with the writes the caller should now perform, in the
/// order to perform them.</summary>
public sealed record PendingEdit(EditStep Step, EditDirection Direction)
{
    /// <summary>Each change and the value it gets, ready to write in this order: undo walks the step's
    /// changes backwards, redo forwards -- the usual rule for inverting a composition.
    ///
    /// Every change is an absolute display value written to a fixed address, so for changes that do not
    /// interpret one another the order makes no difference at all, and that is every gesture the editors
    /// actually produce (a level and a time on one envelope handle govern nothing). It becomes
    /// observable in exactly one case: a discriminator and one of its dependents landing in the same
    /// group. A dependent's display value only means what it meant while its discriminator held the
    /// value it held then -- <c>DomainBase.WriteToIntegraAsync</c> resolves the write through a
    /// <c>ParserContext</c> built from the discriminator's <em>current</em> value, and skips the
    /// parameter outright when it is not valid in that context -- so there the discriminator has to be
    /// written before its dependent, whichever direction is being applied. Reversing gets that right
    /// when the gesture touched the dependent first, and wrong when it touched the discriminator first;
    /// nothing here says which of two changes governs the other, because <c>IsParent</c> lives on the
    /// parameter spec and the journal only stores addresses and strings. No control writes a
    /// discriminator and its dependent inside one 250 ms window today; if one is ever written, this
    /// needs the dependency order rather than either fixed one.</summary>
    public IReadOnlyList<(ParameterChange Change, string ValueToApply)> Writes { get; } =
        Direction == EditDirection.Undo
            ? [.. Step.Changes.Reverse().Select(c => (c, c.OldValue))]
            : [.. Step.Changes.Select(c => (c, c.NewValue))];

    /// <summary>What this step is about to write, for the action log.</summary>
    public string Description =>
        string.Join(", ", Writes.Select(w => $"'{w.Change.Path}' -> '{w.ValueToApply}'"));
}

/// <summary>
/// The undo history. Records every parameter change the user makes, from either write path, groups the
/// changes of one gesture into a single step, and hands back the writes needed to take that step back.
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
    /// <summary>Changes that arrive within this window of one another are one step, <em>whatever they
    /// target</em>. A step is a gesture, not a parameter: a knob drag is hundreds of setter calls on one
    /// parameter, and a drag on any of the 2-D editors (envelopes, the EQ and filter curves, the PMT
    /// zone map) is hundreds of calls alternating between two parameters, because one pointer move sets
    /// a level from the pointer's Y and a time from its X. Coalescing on the target as well as the clock
    /// would merge neither of the latter -- every record would see the other parameter on top -- so one
    /// drag would fill the history and push everything before it out. Matches the write debounce, so a
    /// coalesced group is also one write per parameter.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(Constants.THROTTLE);

    /// <summary>How many steps -- gestures, not parameter changes -- to keep. A long session must not
    /// grow without bound; losing the oldest edits is the right thing to lose.</summary>
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

    public void Record(ParameterChange change)
    {
        lock (_gate)
        {
            if (_isApplying) return;

            var at = _now();
            if (_undo.Count > 0 && at - _lastRecordedAt <= CoalesceWindow)
                // Still the same gesture, whichever parameter this change names.
                _undo[^1] = Merge(_undo[^1], change);
            else
                _undo.Add(new EditStep([change]));

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
        pending = new PendingEdit(step, EditDirection.Undo);
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
        pending = new PendingEdit(step, EditDirection.Redo);
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

    /// <summary>Fold one more change into the open step. A parameter the gesture has already touched
    /// keeps the <c>OldValue</c> from the first time it did -- that is the value from before the gesture
    /// began, which is what undo has to put back -- and takes the latest <c>NewValue</c>. It also keeps
    /// its original position, so the step stays in first-touched order.</summary>
    private static EditStep Merge(EditStep step, ParameterChange change)
    {
        var changes = new List<ParameterChange>(step.Changes);
        var existing = changes.FindIndex(c => IsSameTarget(c, change));
        if (existing >= 0) changes[existing] = changes[existing] with { NewValue = change.NewValue };
        else changes.Add(change);
        return new EditStep(changes);
    }

    private static bool IsSameTarget(ParameterChange a, ParameterChange b) =>
        a.Start == b.Start && a.Offset == b.Offset && a.Offset2 == b.Offset2 && a.Path == b.Path;

    /// <summary>The one journal the application records into. Ambient like
    /// <c>LoadedSrxState.Default</c> and <c>WaveformBanks.Default</c>: the alternative is threading it
    /// through the constructor of every parameter wrapper in fifteen editor view models.</summary>
    public static EditJournal Default { get; } = new();
}
