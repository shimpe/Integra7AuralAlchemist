using System.Threading;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Counts the sync operations currently running, so the blocking overlay is visible while
/// any of them is and hidden once the last one finishes.
///
/// Thread-safe by design rather than by luck. The operations that drive it -- the throttled
/// MessageBus handlers -- run on thread-pool threads and are async void, so two can overlap and their
/// entries and exits can interleave arbitrarily. The counter this replaced was a plain int updated
/// with `_syncLevels = _syncLevels + 1`, where a lost decrement leaves the overlay on screen for the
/// rest of the session.</summary>
public sealed class SyncCounter
{
    private int _level;

    /// <summary>How many operations are running.</summary>
    public int Level => Volatile.Read(ref _level);

    /// <summary>True while any operation is running.</summary>
    public bool Visible => Level > 0;

    /// <summary>Report that an operation started. Returns the new level.</summary>
    public int Enter() => Interlocked.Increment(ref _level);

    /// <summary>Report that an operation finished. Returns the new level.
    ///
    /// An exit without a matching entry leaves the level at zero rather than taking it negative: the
    /// overlay starts out visible, so the first operation to finish after starting with no device
    /// attached exits a level it never entered. Letting that go negative would mean the next real
    /// operation could not bring the level back to zero.</summary>
    public int Exit()
    {
        while (true)
        {
            var current = Volatile.Read(ref _level);
            if (current == 0) return 0;
            if (Interlocked.CompareExchange(ref _level, current - 1, current) == current)
                return current - 1;
        }
    }
}
