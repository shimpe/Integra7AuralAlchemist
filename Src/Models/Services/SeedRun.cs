using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a sweep needs of an instrument, so that the loop above it can be tested without one.
///
/// <b>Every method is one conversation.</b> The three parameter writes and the capture are a single
/// operation from the run's point of view, and they must share one lease -- they are three DT1 messages plus
/// a read, and an abort between them leaves the part on a mixed bank. So the interface exposes the pair
/// rather than the pieces.</summary>
public interface ISeedInstrument
{
    /// <summary>The four slot values the instrument reports right now.</summary>
    Task<int[]> LoadedBoardsAsync();

    /// <summary>Load these four and wait until the instrument settles.
    ///
    /// <b>Settling is not "what was sent".</b> The device rewrites a loadout -- sending (19,0,0,0) reads
    /// back as (19,20,21,22) -- and reports (0,0,0,0) while it works. An implementation polls until the
    /// reported set stops changing, never until it matches the request.</summary>
    Task LoadBoardsAsync(int[] boards, CancellationToken token);

    /// <summary>Select this preset on the part and capture what the part then holds. Null when the
    /// instrument exposed no tone for it -- an unloaded board, or a bank this unit does not answer for
    /// (every GM2 and ExPCM row on the measured unit).</summary>
    Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int zeroBasedPartNo, CancellationToken token);

    /// <summary>Everything the sweep is about to overwrite, so it can be put back.</summary>
    Task<Integra7Snapshot> CaptureStudioSetAsync();

    Task RestoreStudioSetAsync(Integra7Snapshot studioSet);
}

/// <summary>How far a sweep has got.
///
/// <b>Deliberately clock-free.</b> It carries counts and nothing else; elapsed and remaining are the
/// screen's, computed from its own stopwatch and <see cref="SeedWork.Estimate"/>. A run that read a clock
/// would be a run whose progress no test could pin, and it would have bought nothing -- the panel is holding
/// a stopwatch either way, because it wants to keep counting between reports.</summary>
/// <param name="Done">Patches attempted, including the ones that answered nothing.</param>
/// <param name="Total">Patches in the whole plan.</param>
/// <param name="Current">The one just attempted.</param>
public sealed record SeedProgress(int Done, int Total, SeedItem Current);

/// <summary>What a sweep did, patch by patch.</summary>
/// <param name="Written">File paths, in the order they were written.</param>
/// <param name="Unavailable">Presets the instrument exposed no tone for.</param>
/// <param name="Failed">Presets whose capture or write threw, with the message.</param>
/// <param name="Cancelled">Whether the run stopped early because it was asked to.</param>
/// <param name="RestoreWarning">Null when the instrument came back to where it started. Otherwise what
/// disagreed -- the spec asks for the restore to be verified by reading back rather than assumed, and a
/// user whose boards did not come back needs to be told rather than left to find out when a part goes
/// silent. Carried rather than thrown: it is discovered in a finally, where throwing would replace whatever
/// the run was already reporting.</param>
public sealed record SeedOutcome(
    IReadOnlyList<string> Written,
    IReadOnlyList<Integra7Preset> Unavailable,
    IReadOnlyList<(Integra7Preset Preset, string Why)> Failed,
    bool Cancelled,
    string? RestoreWarning = null);

/// <summary>Walking a <see cref="SeedWork"/> across an instrument: the boards each round needs, the patches
/// capturable under them, the file that comes out of each one, and putting the instrument back afterwards.
///
/// <b>Nothing that goes wrong with one patch may end the sweep.</b> A factory sweep is ~6,000 patches and
/// about 54 minutes, and 796 of those rows -- every GM2 and every ExPCM one -- expose no temporary tone at
/// all on the unit this was measured against. So 13% of a full run takes the "there was nothing to capture"
/// path; that is a normal outcome rather than an error, and neither it nor a capture that throws is allowed
/// to cost the other 87%. Each patch is therefore its own try, and what went wrong is recorded against the
/// preset it went wrong for.
///
/// <b>There is no delay anywhere in this loop, and adding one would be a mistake.</b> The device withholds
/// the read reply until the tone has finished loading, so the capture is itself the settle check: forty
/// captures started with zero delay after the bank and program writes came back byte-identical to captures
/// taken 1.5 s later, on all five engines. Nor is a silent patch retried. Every silence measured was a patch
/// this unit genuinely does not expose, and retried three times it was silent three times -- so a retry is
/// 1.5 s spent to be told the same thing again, twenty minutes of it over a full sweep.
///
/// <b>The instrument is put back in a finally.</b> A sweep overwrites a part once per patch and evicts
/// whatever was in the four board slots, and a run that threw and left the user with neither their Studio
/// Set nor their boards is the worst thing this feature can do -- worse than not running at all, because
/// they did not choose it.</summary>
public static class SeedRun
{
    /// <summary>Sweep <paramref name="work"/> into the library, one patch at a time.</summary>
    /// <param name="work">What to capture, in the order <see cref="SeedPlan"/> put it.</param>
    /// <param name="selection">Read for the part to borrow; the rest of it shaped the plan already.</param>
    /// <param name="instrument">The device, or a fake standing in for one.</param>
    /// <param name="write">Where a captured snapshot goes, answering the path it was written to. A callback
    /// rather than a folder, so that this knows nothing about files and a test can count what was written
    /// without touching a disk -- and so that each snapshot is written the moment it is captured, which is
    /// what makes an interrupted sweep resumable rather than a lost hour.</param>
    /// <param name="progress">Told about every patch attempted, including the ones that answered nothing.</param>
    /// <param name="token">Honoured between patches, never inside one.</param>
    public static async Task<SeedOutcome> RunAsync(SeedWork work, SeedSelection selection,
        ISeedInstrument instrument, Func<SeedItem, Integra7Snapshot, string> write,
        IProgress<SeedProgress>? progress, CancellationToken token)
    {
        // Before anything is overwritten, and outside the try on purpose: if the Studio Set cannot be
        // captured then it cannot be put back either, and the right answer to that is to fail here with the
        // instrument untouched rather than to start a sweep that has nothing to restore at the end of it.
        var studioSet = await instrument.CaptureStudioSetAsync();

        var boardsBefore = await instrument.LoadedBoardsAsync();

        List<string> written = [];
        List<Integra7Preset> unavailable = [];
        List<(Integra7Preset Preset, string Why)> failed = [];
        var cancelled = false;
        var loadedBoards = false;
        var done = 0;
        string? restoreWarning;

        try
        {
            foreach (var round in work.Rounds)
            {
                // Between rounds as well as between patches, because the first thing a round does is a board
                // load that takes 23 seconds, and a cancel that arrived before it should not have to sit
                // through it to be noticed.
                cancelled |= token.IsCancellationRequested;
                if (cancelled) break;

                if (round.Boards is not null)
                {
                    // Noted before the load rather than after it. A load that threw part-way may still have
                    // evicted what was in the slots, and the run where the boards most need putting back is
                    // exactly the run where the loading went wrong.
                    loadedBoards = true;
                    await instrument.LoadBoardsAsync(round.Boards, token);
                }

                foreach (var item in round.Items)
                {
                    // Never inside a patch: the three parameter writes and the capture share one lease, and
                    // stopping between them leaves the part holding one patch's bank and another's program.
                    cancelled |= token.IsCancellationRequested;
                    if (cancelled) break;

                    try
                    {
                        var snapshot = await instrument.CaptureAsync(item, selection.ZeroBasedPartNo, token);
                        // Null is not a failure and must not be reported as one. It is the instrument saying
                        // it holds no tone for this row -- an unloaded board, or one of the 796 GM2 and ExPCM
                        // rows the measured unit answers nothing for -- and "796 of these were not available
                        // on your instrument" and "796 of these failed" are different sentences, only one of
                        // which is true. The second would have a user hunting a fault that is not there.
                        if (snapshot is null) unavailable.Add(item.Preset);
                        else written.Add(write(item, snapshot));
                    }
                    catch (Exception e)
                    {
                        // The write is inside this for the same reason the capture is: a folder that filled
                        // up or a name the filesystem refused is this patch's problem, and making it the
                        // sweep's would throw away the fifty minutes on either side of it.
                        failed.Add((item.Preset, e.Message));
                    }

                    // Per attempt rather than per success, so the number on screen is the number of patches
                    // gone past. Progress that only moved when something was written would sit still for
                    // minutes at a time through a bank this unit does not answer for, which is exactly what
                    // a hang looks like from the outside.
                    progress?.Report(new SeedProgress(++done, work.Count, item));
                }
            }
        }
        finally
        {
            restoreWarning = await PutBackAsync(instrument, studioSet, boardsBefore, loadedBoards);
        }

        return new SeedOutcome(written, unavailable, failed, cancelled, restoreWarning);
    }

    /// <summary>Put the instrument back where the sweep found it, and say so when it did not go.
    ///
    /// <b>Boards first, then the Studio Set.</b> A Studio Set names a tone per part and some of those tones
    /// live on the boards, so restoring it while the slots still hold the sweep's last loadout would land
    /// parts on banks that are not there. Putting the boards back first is what makes the Studio Set restore
    /// arrive at an instrument able to hold it.
    ///
    /// <b>Boards only when the sweep moved them.</b> A load converges in about 23 seconds whether or not it
    /// changes anything, and a sweep of built-in banks alone never touched a slot -- so sending one anyway
    /// would be 23 seconds spent undoing nothing, on the runs that are otherwise the quickest.
    ///
    /// <b>Verified by reading back, and this is the one read-back comparison that is legitimate.</b> The
    /// device rewrites a loadout it is sent -- (19,0,0,0) comes back as (19,20,21,22) -- so checking a load
    /// against the request is the trap the spike walked into, and <see cref="ISeedInstrument.LoadBoardsAsync"/>
    /// says as much. What is sent here is not a request of that kind: it is exactly the set the device had
    /// already settled on when the sweep started, so comparing a fresh reading against it compares
    /// convergence with convergence. Compared as a set of the non-zero values, which is how
    /// <see cref="SeedPlan.Build"/> decides a board is available: which slot holds a board decides nothing,
    /// and a warning that fired because the device chose a different slot is a warning that would be ignored
    /// the second time it appeared.</summary>
    private static async Task<string?> PutBackAsync(ISeedInstrument instrument, Integra7Snapshot studioSet,
        int[] boardsBefore, bool loadedBoards)
    {
        // Explicitly uncancellable: this runs on the cancel path as well, and handing it the run's token
        // would abandon the restore at the one moment it is most needed. That is also why the Studio Set
        // restore below takes no token at all.
        if (loadedBoards) await instrument.LoadBoardsAsync(boardsBefore, CancellationToken.None);
        await instrument.RestoreStudioSetAsync(studioSet);

        if (!loadedBoards) return null;

        var now = await instrument.LoadedBoardsAsync();
        return Available(now).SetEquals(Available(boardsBefore))
            ? null
            : $"The expansion board slots did not come back. They held {Slots(boardsBefore)} before the "
              + $"sweep; the instrument now reports {Slots(now)}.";
    }

    /// <summary>The boards a set of slot values makes available. Off is 0, which is not a board -- the same
    /// reading <see cref="SeedPlan.Build"/> takes of the same four numbers.</summary>
    private static HashSet<int> Available(IEnumerable<int> slots) =>
        slots.Where(board => board != 0).ToHashSet();

    /// <summary>All four values as the device reports them, zeros included: the message is read by someone
    /// looking at an instrument, and which slot went empty is the useful half of it.</summary>
    private static string Slots(IEnumerable<int> slots) => string.Join(", ", slots);
}
