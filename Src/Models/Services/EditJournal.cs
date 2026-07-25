using System;
using System.Collections.Generic;
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
