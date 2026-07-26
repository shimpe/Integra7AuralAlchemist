using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter's before and after, and the address that resolves it. Values are display
/// strings because that is the form every write path already speaks -- see <c>IParam.Snapshot()</c> and
/// <c>DomainBase.WriteToIntegraAsync(path, displayValue, lease)</c>.</summary>
/// <param name="IsDiscriminator">Whether other parameters' values are interpreted through this one --
/// <c>ParSpec.IsParent</c>, captured at record time because that is where it is known. The journal needs
/// it for two things: to write a discriminator before anything that depends on it (see
/// <see cref="PendingEdit.Writes"/>), and to resync the dependents afterwards.</param>
public sealed record ParameterChange(
    string Start, string Offset, string Offset2, string Path, string OldValue, string NewValue,
    bool IsDiscriminator);

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
    /// <summary>Each change and the value it gets, ready to write in this order: <b>discriminators
    /// first</b>, everything else after them, each group keeping the order the gesture recorded it in.
    /// The order does not depend on the direction -- only the value written does.
    ///
    /// Dependency order is the rule, not recording order and not its reverse. A dependent's display value
    /// only converts to the right byte once the local context holds the discriminator value that dependent
    /// belongs to: <c>DomainBase.WriteToIntegraAsync</c> resolves the write through a
    /// <c>ParserContext</c> rebuilt from the block's <em>current</em> values, and
    /// <c>ModifySingleParameterDisplayedValue</c> skips a parameter outright when it is not
    /// <c>ValidInContext</c> there -- so the discriminator has to go first whichever direction we are
    /// moving in and whichever one the user touched first. Reversing on undo and recording order each get
    /// this right for one of the two touch orders and wrong for the other; asking the change itself gets
    /// it right for both. This is reachable, not hypothetical: pick a chorus type from its combo and move
    /// one of that type's knobs within 250 ms and both land in one group.
    ///
    /// It is not a general topological sort. Two discriminators in one group that depend on each other
    /// keep their recorded order relative to one another, which is only right if the gesture happened to
    /// touch the governing one first. What makes one level of "discriminators first" enough for the
    /// database as it stands is that a two-level chain is rewritten so the dependent names the
    /// <em>top-level</em> discriminator directly -- see
    /// <c>Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies</c>, which also asserts that no
    /// three-level chain exists -- so a dependent is never more than one hop from what governs it.</summary>
    public IReadOnlyList<(ParameterChange Change, string ValueToApply)> Writes { get; } =
        [.. Step.Changes
            // OrderBy is a stable sort, which is what keeps "everything else in recorded order" true.
            .OrderBy(c => c.IsDiscriminator ? 0 : 1)
            .Select(c => (c, Direction == EditDirection.Undo ? c.OldValue : c.NewValue))];

    /// <summary>What this step is about to write, for the action log.</summary>
    public string Description =>
        string.Join(", ", Writes.Select(w => $"'{w.Change.Path}' -> '{w.ValueToApply}'"));
}

/// <summary>
/// The undo history. Records every parameter change the user makes, from either write path, groups the
/// changes of one gesture into a single step, and hands back the writes needed to take that step back.
///
/// What counts as one gesture is answered two ways. A draggable control says so outright, by holding a
/// <see cref="BeginGesture"/> scope open from the pointer press to the release; that is the only reliable
/// answer, because a control records only when its value actually changes and a slow, careful drag can be
/// seconds between changes. Everything else -- keystrokes in a text box, repeated picks from a combo --
/// has no gesture to delimit it and falls back to <see cref="CoalesceWindow"/>.
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
    /// <summary>Outside a gesture (see <see cref="BeginGesture"/>), changes that arrive within this
    /// window of one another are one step, <em>whatever they target</em>. A step is a gesture, not a
    /// parameter: a knob drag is hundreds of setter calls on one parameter, and a drag on any of the 2-D
    /// editors (envelopes, the EQ and filter curves, the PMT zone map) is hundreds of calls alternating
    /// between two parameters, because one pointer move sets a level from the pointer's Y and a time from
    /// its X. Coalescing on the target as well as the clock would merge neither of the latter -- every
    /// record would see the other parameter on top -- so one drag would fill the history and push
    /// everything before it out. Matches the write debounce, so a coalesced group is also one write per
    /// parameter.
    ///
    /// This is the rule for everything that is <em>not</em> a pointer gesture -- keystrokes in a knob's
    /// text box, repeated picks from a combo -- where nothing can tell us when the edit began and ended
    /// and the clock is all there is.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(Constants.THROTTLE);

    /// <summary>How long an open gesture may record nothing at all before its group is treated as
    /// finished anyway. Purely a containment measure for a gesture scope that was never disposed: the
    /// controls close theirs from both pointer-released and pointer-capture-lost, but a scope has to be
    /// held in a field across those handlers (a <c>using</c> cannot span two event callbacks), so a leak
    /// is possible in a way it is not for the rest of the journal. Without a bound, one leaked scope
    /// would fold every later edit of the whole session into a single step -- silently, and with no way
    /// back short of a resync.
    ///
    /// Deliberately far longer than <see cref="CoalesceWindow"/>: a real gesture <em>does</em> go quiet
    /// for seconds at a time (that is the whole reason the clock cannot delimit one), so this must not
    /// cut a slow, careful drag in half. Where it does fire mid-drag, the cost is one extra undo step --
    /// what the user got for every step before gestures existed -- against a history that otherwise
    /// stops working entirely.</summary>
    public static readonly TimeSpan StaleGestureWindow = CoalesceWindow * 40;

    /// <summary>How many steps -- gestures, not parameter changes -- to keep. A long session must not
    /// grow without bound; losing the oldest edits is the right thing to lose.</summary>
    public const int Capacity = 200;

    // Guards _undo, _redo, _compare, _isComparing, _truncated, _generation, _lastRecordedAt, _isApplying,
    // _gestureDepth and _gestureGroupOpen. Record runs on the UI thread and on the thread pool (see the
    // class comment); List<T> is not thread-safe,
    // so without this two concurrent Record calls can corrupt the lists outright (reproduced in
    // TestEditJournal.Concurrent_records_from_many_threads_do_not_corrupt_the_history). The lists are
    // tiny and uncontended, so a plain lock costs nothing. The gesture state is in here for the same
    // reason: a gesture is opened and closed on the UI thread but read by every Record call, including
    // the ones that arrive from the pool.
    private readonly object _gate = new();

    private readonly List<EditStep> _undo = [];
    private readonly List<EditStep> _redo = [];

    /// <summary>The whole history, held here while the instrument plays the sound from before it. Oldest
    /// step first, exactly as it sat on <see cref="_undo"/>, so coming back is that list written forward.
    /// Compare owns these steps rather than borrowing the undo stack because <see cref="Record"/> clears
    /// the redo side on every new edit -- parking them there would lose the user's edited sound the moment
    /// they touched a knob while comparing.</summary>
    private readonly List<EditStep> _compare = [];

    private bool _isComparing;

    /// <summary>Whether any step has been evicted since the last <see cref="Clear"/>. The oldest edits are
    /// the right ones to lose for the history's own purposes, but Compare derives "the sound before the
    /// edits" from the whole history, so an evicted step is a difference it cannot put back. Nothing here
    /// refuses because of it -- the comparison is still audible and still reversible -- but the caller has
    /// to be able to say so, which is the alternative to a wrong answer with nothing to mark it.</summary>
    private bool _truncated;

    /// <summary>How many times the history has changed shape. Stamped into a <see cref="CompareToggle"/>
    /// when one is computed and checked when it is committed, because the two are deliberately separated by
    /// the whole of the writes -- including waiting for the MIDI lease, which is outside the recording
    /// suppression. A toggle is a description of one particular history; anything that adds to, reorders or
    /// throws away that history in the meantime makes it a description of a state the journal is no longer
    /// in. Incremented by everything that mutates <see cref="_undo"/>, <see cref="_redo"/> or
    /// <see cref="_compare"/>.</summary>
    private int _generation;

    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _lastRecordedAt;
    private bool _isApplying;

    // How many gesture scopes are open (nesting is counted, not flagged, so an inner scope closing
    // cannot end an outer one), and whether the open gesture has already put a step on _undo to fold
    // into. The latter is what makes the gesture's first record start a fresh step and every later one
    // join it, however long the gesture takes.
    private int _gestureDepth;
    private bool _gestureGroupOpen;

    public EditJournal(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>True while a step is being applied. The write that undo performs comes back through
    /// the same setters that record, so without this an undo would record itself as a new edit and
    /// the history would never empty.</summary>
    public bool IsApplying { get { lock (_gate) return _isApplying; } }

    /// <summary>Whether there is a step to take back. False while comparing: the history is in Compare's
    /// buffer then, and a step applied on top of the original sound would leave the instrument playing
    /// neither it nor the edited one -- see <see cref="TryUndo"/>.</summary>
    public bool CanUndo { get { lock (_gate) return !_isComparing && _undo.Count > 0; } }

    /// <summary>Whether there is a step to put back. False while comparing, for the reason
    /// <see cref="CanUndo"/> gives -- and it is <em>not</em> incidentally false the way CanUndo would be:
    /// a step undone before the button was pressed is still on the redo side throughout.</summary>
    public bool CanRedo { get { lock (_gate) return !_isComparing && _redo.Count > 0; } }

    /// <summary>True while the instrument is playing the sound from before the recorded edits.</summary>
    public bool IsComparing { get { lock (_gate) return _isComparing; } }

    /// <summary>Whether the Compare button has anything to do. It stays true <em>while</em> comparing --
    /// the history is in the buffer then, so <see cref="CanUndo"/> is false, and a button disabled on that
    /// would leave no way back to the edited sound.</summary>
    public bool CanCompare { get { lock (_gate) return _isComparing || _undo.Count > 0; } }

    /// <summary>Whether the history has dropped steps it once held, so a comparison against it is missing
    /// those differences. See <see cref="_truncated"/>.</summary>
    public bool HistoryTruncated { get { lock (_gate) return _truncated; } }

    /// <summary>Raised whenever <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed. Fires
    /// from whichever thread made the change -- see the class remarks.</summary>
    public event Action? Changed;

    /// <summary>Open while a control is mid-gesture. Everything recorded inside one belongs to a single
    /// step no matter how long the gesture takes -- which timing alone cannot tell, because a knob only
    /// records when its snapped value changes and a careful drag can be seconds between steps.
    /// Re-entrant: a depth counter, so a control that nests scopes cannot close one early.
    ///
    /// Disposing the outermost scope ends the step: the next change recorded starts a new one, even if it
    /// arrives immediately, so releasing a knob and turning another is two undo steps and not one.
    /// Disposing a scope twice does nothing the second time.</summary>
    public IDisposable BeginGesture()
    {
        lock (_gate) _gestureDepth++;
        return new GestureScope(this);
    }

    /// <summary>One <see cref="BeginGesture"/> scope. Nulls its journal reference on the first dispose so
    /// a second one -- a control ending its drag from both pointer-released and pointer-capture-lost --
    /// cannot decrement the depth twice and close a gesture that is still in progress.</summary>
    private sealed class GestureScope(EditJournal journal) : IDisposable
    {
        private EditJournal? _journal = journal;

        public void Dispose() =>
            System.Threading.Interlocked.Exchange(ref _journal, null)?.EndGesture();
    }

    private void EndGesture()
    {
        lock (_gate)
        {
            if (_gestureDepth == 0) return;
            if (--_gestureDepth > 0) return;
            _gestureGroupOpen = false;
            // The gesture is over, so the next change is a new step whatever the clock says -- the same
            // reason TryUndo clears this.
            _lastRecordedAt = default;
        }
    }

    public void Record(ParameterChange change)
    {
        lock (_gate)
        {
            // Applying a step writes through the same setters that record (see IsApplying). Comparing is
            // the same problem for a different reason: an edit made while the original is playing is
            // overwritten the moment the user presses Compare again, because coming back writes every
            // buffered step forward over it -- so recording it would leave a step in the history whose
            // OldValue belongs to the original and whose NewValue the instrument does not keep. The edit
            // itself still reaches the device; it is only the history that ignores it.
            if (_isApplying || _isComparing) return;

            var at = _now();
            // A control mid-gesture has told us where the step ends, so the clock does not get a say;
            // with no gesture open the clock is all there is to go on.
            var stillTheSameStep = _gestureDepth > 0
                ? JoinsTheOpenGesture(at)
                : at - _lastRecordedAt <= CoalesceWindow;

            if (_undo.Count > 0 && stillTheSameStep)
                // The same gesture, whichever parameter this change names.
                _undo[^1] = Merge(_undo[^1], change);
            else
                _undo.Add(new EditStep([change]));

            // Whichever branch ran, _undo[^1] is now this gesture's step for the rest of it.
            if (_gestureDepth > 0) _gestureGroupOpen = true;
            _lastRecordedAt = at;
            if (_undo.Count > Capacity)
            {
                _undo.RemoveAt(0);
                _truncated = true;
            }
            // A new edit makes the redo history unreachable -- it described a future that no longer follows.
            _redo.Clear();
            _generation++;
        }
        Changed?.Invoke();
    }

    /// <summary>Whether a change recorded at <paramref name="at"/> folds into the step the open gesture
    /// already started. The elapsed time does not decide this -- that is the point of a gesture -- except
    /// as the containment bound described on <see cref="StaleGestureWindow"/>. Call under the lock.</summary>
    private bool JoinsTheOpenGesture(DateTimeOffset at) =>
        _gestureGroupOpen && at - _lastRecordedAt <= StaleGestureWindow;

    public bool TryUndo([MaybeNullWhen(false)] out PendingEdit pending)
    {
        EditStep step;
        lock (_gate)
        {
            // Nothing may move while a comparison is in progress. The instrument is playing the sound from
            // before the history, so a step applied now writes one edited value on top of it -- neither
            // sound -- and, worse, leaves the stack out of chronological order once the buffer goes back
            // under it, so the next undo takes back the older gesture first. The buttons are disabled
            // through CanUndo/CanRedo; this is the guard behind them.
            if (_isComparing || _undo.Count == 0)
            {
                pending = default;
                return false;
            }

            step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(step);
            _generation++;
            // The step now on top of _undo (if any) was not the one Record last touched, so neither its
            // recorded time nor an open gesture's claim on it must be trusted as "still the same gesture"
            // by the next Record call -- see the regression this guards in TestEditJournal.
            _lastRecordedAt = default;
            _gestureGroupOpen = false;
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
            // See TryUndo: the history does not move while the original is playing.
            if (_isComparing || _redo.Count == 0)
            {
                pending = default;
                return false;
            }

            step = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(step);
            _generation++;
            // Same reasoning as TryUndo: whatever Record does next must start a fresh step, not assume
            // it is continuing the gesture that originally produced the step redo just reinstated.
            _lastRecordedAt = default;
            _gestureGroupOpen = false;
        }
        pending = new PendingEdit(step, EditDirection.Redo);
        Changed?.Invoke();
        return true;
    }

    /// <summary>One press of Compare: the steps to write, in the order to write them, and which way the
    /// press is going. <paramref name="Entering"/> true means the writes take the instrument back to the
    /// sound from before the recorded edits; false means they put the edits back.
    ///
    /// Two-phase deliberately. <see cref="EditJournal.TryBeginCompareToggle"/> computes this and changes
    /// nothing, so a press whose writes do not all land simply is not committed: the journal still says it
    /// is on the side it was, and pressing Compare again retries the same direction. Every write is an
    /// absolute value, so the retry finishes the job rather than compounding a half-applied swap -- the
    /// same property that makes <c>StudioSetSnapshotService.RestoreAsync</c> safe to re-run.</summary>
    /// <param name="Generation">The history this toggle describes, as
    /// <see cref="EditJournal"/> counted it when the toggle was computed. See
    /// <c>EditJournal.CommitCompareToggle</c>, which refuses a toggle whose generation has moved on.</param>
    public sealed record CompareToggle(IReadOnlyList<PendingEdit> Steps, bool Entering, int Generation);

    /// <summary>Work out what one press of Compare has to write. Changes no state -- see
    /// <see cref="CompareToggle"/>. False when there is nothing to compare with, i.e. an empty history.
    /// </summary>
    public bool TryBeginCompareToggle([MaybeNullWhen(false)] out CompareToggle toggle)
    {
        lock (_gate)
        {
            if (_isComparing)
            {
                // Back to the edited sound: every buffered step forward, oldest first, so a parameter
                // edited more than once ends on the value the newest step gave it.
                toggle = new CompareToggle(
                    [.. _compare.Select(s => new PendingEdit(s, EditDirection.Redo))], false, _generation);
                return true;
            }

            if (_undo.Count == 0)
            {
                toggle = default;
                return false;
            }

            // Newest step first, which is undo's order repeated to the bottom of the history: a parameter
            // edited more than once has to end on the OldValue of the *oldest* step that touched it, which
            // is the value it held before any of this. Oldest-first would leave it on the newest step's
            // OldValue -- an intermediate value the user passed through, not the sound they started from.
            toggle = new CompareToggle(
                [.. Enumerable.Reverse(_undo).Select(s => new PendingEdit(s, EditDirection.Undo))], true,
                _generation);
            return true;
        }
    }

    /// <summary>Move the history to the side <paramref name="toggle"/> just wrote. Call only after every
    /// one of its writes has landed.
    ///
    /// The steps come from the toggle rather than from the live lists: they are the steps whose writes just
    /// went out, and coming back has to write exactly those forward again. The two cannot disagree today --
    /// recording is suppressed for the whole of the writes and the caller holds the sync overlay up over
    /// the press -- and taking them from the toggle means they still cannot if that changes.
    ///
    /// False when the toggle was refused, which is not the same as nothing happening: its writes have
    /// already gone out, so the instrument is somewhere between the two sounds while the journal still says
    /// it is on the side it started from. The caller has to say so rather than report the press as done --
    /// pressing Compare again recomputes from the history that is really there and converges, because every
    /// write is an absolute value. Returning void here would leave that indistinguishable from success.
    /// </summary>
    public bool CommitCompareToggle(CompareToggle toggle)
    {
        lock (_gate)
        {
            // The history has changed shape since this toggle was computed, so it describes writes for a
            // state the journal is no longer in: a Clear -- a preset change, or a Studio Set change
            // arriving from the front panel -- has thrown that history away, or an edit made while Compare
            // waited for the wire has added to it (and Merge replaced the step instance, so the removal
            // below would silently miss and leave one step in both lists). Refuse the whole press. Nothing
            // is consumed, the caller reports that it did not finish, and pressing Compare again
            // recomputes from the history that is really there.
            if (toggle.Generation != _generation) return false;

            if (toggle.Entering)
            {
                // Already comparing, so this toggle has been committed once already. Unreachable through
                // the generation check above, which a second commit of the same toggle also fails; here for
                // the same reason that check is worth having.
                if (_isComparing) return false;
                // Reversed back to oldest-first, the order _undo held them in and the order coming back
                // writes them in.
                var steps = Enumerable.Reverse(toggle.Steps.Select(s => s.Step)).ToList();
                _compare.AddRange(steps);
                foreach (var s in steps) _undo.Remove(s);
                _isComparing = true;
            }
            else
            {
                if (!_isComparing) return false;
                // In their original order, so the history after a Compare round trip is the history from
                // before it -- same steps, same order, undo still able to walk back through them.
                _undo.AddRange(toggle.Steps.Select(s => s.Step));
                _compare.Clear();
                _isComparing = false;
            }

            _generation++;
            // Whichever way it went, the next change recorded starts a new step rather than folding into
            // one from before the swap -- the same reason TryUndo and TryRedo clear these.
            _lastRecordedAt = default;
            _gestureGroupOpen = false;
        }

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
    /// describes -- a Studio Set change, a preset change, a snapshot restore.
    ///
    /// The step an open gesture was folding into has just been thrown away with the rest, so the gesture
    /// starts a new one if it records again. The depth itself is left alone: it belongs to the scopes the
    /// controls hold, and they are the only things that may close them.
    ///
    /// A comparison in progress ends here too, and its buffer goes with the history. That is right rather
    /// than lossy: everything that clears the journal has just replaced the sound the buffer described, so
    /// writing those steps forward afterwards would push values from a patch that is no longer loaded into
    /// the one that is.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
            _compare.Clear();
            _isComparing = false;
            _truncated = false;
            _generation++;
            _gestureGroupOpen = false;
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
