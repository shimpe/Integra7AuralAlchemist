using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A real INTEGRA-7 behind <see cref="ISeedInstrument"/>: select a preset on a part, read back what
/// the part then holds, move the four expansion slots, and take and replace a Studio Set.
///
/// <b>The adapter, and the one piece of this feature with no tests.</b> That is deliberate rather than an
/// omission: everything a sweep can get quietly wrong was pushed above it -- which board a bank needs into
/// <see cref="SeedBoards"/>, what to capture and what to call it into <see cref="SeedPlan"/>, what happens
/// when one patch goes wrong into <see cref="SeedRun"/>, and which name wins into <see cref="SeedNaming"/>,
/// all four of them testable without a device. What is left here is the part that cannot be tested without
/// one, and it is kept thin so that stays true.
///
/// <b>Nothing in here waits for a tone to load.</b> The device withholds the read reply until the patch has
/// finished loading rather than answering with the outgoing one, so the capture is itself the settle check:
/// forty captures started with zero delay after the bank and program writes came back byte-identical to
/// captures taken 1.5 seconds later, on all five engines. Nor is the name polled to find out whether the new
/// patch has arrived -- the table disagrees with the device about roughly 2% of names, and each of those
/// would burn a full reply deadline and then be reported as a failed capture of a patch that loaded
/// perfectly.
///
/// <b>And nothing is retried.</b> Reads against a loaded engine's area did not flake once in ~17,000
/// requests. Every silence measured was a patch this unit genuinely does not expose, and those are
/// deterministic: retried three times they were silent three times. A retry is a reply deadline spent being
/// told the same thing again, and twenty minutes of it over a full sweep.</summary>
/// <param name="domain">The live parameter tree, for the part write and every block read.</param>
/// <param name="api">The wire, for leases and for the two SRX messages that have no domain of their own.</param>
public sealed class SeedInstrument(Integra7Domain domain, IIntegra7Api api) : ISeedInstrument
{
    /// <summary>How often the four slots are asked what they hold while a loadout settles.
    ///
    /// <b>One interval carries two decisions.</b> A board load takes 5 seconds, three of them 14.6, and the
    /// <c>HQ Pcm</c> loadout 18.7 -- so polling faster than this buys nothing but traffic on a wire the sweep
    /// wants for captures. And two intervals is longer than the 2.5 seconds an unload was measured to take,
    /// which matters for the one case where the settled reading and the mid-flight reading are the same three
    /// bytes: see <see cref="LoadBoardsAsync"/> on the all-Off loadout.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>When to stop believing a loadout is still on its way. Four times the slowest convergence ever
    /// measured (<c>HQ Pcm</c>, stable at 23.3 seconds), because the cost of being wrong in the two directions
    /// is not symmetric: waiting too long delays a round, and giving up too early sweeps a whole board's worth
    /// of patches against slots that do not hold it yet and records every one of them as unavailable.</summary>
    private static readonly TimeSpan SettleCeiling = TimeSpan.FromSeconds(90);

    /// <summary>The four slot values the instrument reports right now.</summary>
    public async Task<int[]> LoadedBoardsAsync()
    {
        var (slot1, slot2, slot3, slot4) = await api.GetLoadedSrxAsync();
        return [slot1, slot2, slot3, slot4];
    }

    /// <summary>Send a loadout and wait for the instrument to settle on one.
    ///
    /// <b>Never compared against what was sent.</b> The device rewrites a loadout it is given -- sending
    /// (19,0,0,0) reads back as (19,20,21,22), because that board occupies all four slots -- so a poll that
    /// waited for the request to appear would wait for something that is never going to, become an accidental
    /// fixed wait, and then sweep a round against whatever the slots happened to hold. That trap was walked
    /// into once already, by the person who had just written it down. What is waited for instead is the
    /// reading holding still: two consecutive answers that agree.
    ///
    /// <b>All zeros is the device working, not the device finished</b> -- it reports (0,0,0,0) while a load is
    /// in flight, so a run of identical zeros is exactly what a load in progress looks like and must not be
    /// mistaken for one that has completed. The single exception is a loadout that asks for no board at all,
    /// which is what the restore sends for a user who had nothing loaded when the sweep started: there all
    /// zeros is the answer, there is no other reading it could converge on, and the wait for two of them one
    /// <see cref="PollInterval"/> apart is what keeps the Studio Set restore from starting into an instrument
    /// that is still emptying its slots.</summary>
    public async Task LoadBoardsAsync(int[] boards, CancellationToken token)
    {
        await api.SendLoadSrxAsync((byte)boards[0], (byte)boards[1], (byte)boards[2], (byte)boards[3]);

        var asksForABoard = boards.Any(slot => slot != 0);
        var clock = Stopwatch.StartNew();
        int[]? previous = null;

        while (clock.Elapsed < SettleCeiling)
        {
            await Task.Delay(PollInterval, token);
            var now = await LoadedBoardsAsync();

            if ((!asksForABoard || now.Any(slot => slot != 0))
                && previous is not null && now.SequenceEqual(previous))
            {
                Log.Information("The expansion slots settled on {Boards} after {Seconds:0.0} s.",
                    string.Join(", ", now), clock.Elapsed.TotalSeconds);
                return;
            }

            previous = now;
        }

        // Named, because the message is read by somebody looking at an instrument and the loadout is the
        // only part of this they can act on -- and because the sweep puts it in the outcome verbatim rather
        // than stopping with an exception nobody kept. A TimeoutException rather than one of this
        // application's own: nothing is malformed here, the instrument simply never answered the same thing
        // twice.
        throw new TimeoutException(
            $"The instrument did not settle on the expansion boards {string.Join(", ", boards)} within "
            + $"{SettleCeiling.TotalSeconds:0} seconds.");
    }

    /// <summary>Select <paramref name="item"/>'s preset on the part and capture what the part then holds.
    ///
    /// <b>One lease for the whole patch.</b> The three parameter writes are three separate DT1 messages and
    /// the capture is a run of reads after them; anything else granted the port in between would leave the
    /// part holding one patch's bank and another's program, and be captured that way.
    ///
    /// <b>The program number is written one lower than the table says.</b> The parameter is 0-based and
    /// <c>Presets.csv</c> is 1-based -- confirmed by read-back during the spike, and by
    /// <c>PartViewModel.PreSelectConfiguredPreset</c>, which has always compared against <c>Pc - 1</c> to
    /// recognise the patch a part is holding.
    ///
    /// <b>The engine comes from the preset row.</b> It decides which blocks a tone even consists of, so
    /// getting it wrong is a reply deadline per block and then a failure.
    ///
    /// <paramref name="token"/> is deliberately not honoured inside this method, only between calls to it:
    /// see <see cref="ISeedInstrument"/>. Stopping between the bank write and the program write is the one
    /// thing a cancel must not do.</summary>
    public async Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int zeroBasedPartNo,
        CancellationToken token)
    {
        var preset = item.Preset;
        await using var lease = await api.BeginConversationAsync($"seed {preset.Name}");

        var part = domain.StudioSetPart(zeroBasedPartNo);
        await part.WriteToIntegraAsync("Studio Set Part/Tone Bank Select MSB", $"{preset.Msb}", lease);
        await part.WriteToIntegraAsync("Studio Set Part/Tone Bank Select LSB", $"{preset.Lsb}", lease);
        await part.WriteToIntegraAsync("Studio Set Part/Tone Bank Program Number (PC)", $"{preset.Pc - 1}",
            lease);

        try
        {
            // Straight into the capture. There is nothing to poll and nothing to wait for; see the class
            // remarks.
            return await StudioSetSnapshotService.CaptureToneAsync(domain, zeroBasedPartNo,
                preset.ToneTypeStr, preset.Name, lease);
        }
        catch (SnapshotFormatException)
        {
            // Two very different things arrive here as the same exception, and telling them apart is the
            // difference between "796 of these are not available on your instrument" and "796 of these
            // failed" -- one of which is true and the other of which has a user hunting a fault that is not
            // there. A patch this unit does not expose answers nothing from the very first block: the Studio
            // Set Part stores the bank and program quite happily and then all five engines' temporary areas
            // stay silent, which is every GM2 and every ExPCM row, 13% of a factory sweep. A tone that
            // answered and then stopped partway is a real failure and has to reach the sweep as one.
            //
            // Which it was is asked of the device rather than read out of the exception's wording. Matching
            // on the message would make this depend on a sentence in another file that nobody would think to
            // check when they reworded it, and the way it would break is silent: every unavailable row
            // reported as a failure. The cost is one extra read on the failing path only -- roughly 24
            // seconds spread over a full sweep, and nothing at all on the 87% that succeed, which is what
            // keeps SeedPlan's measured estimate honest. It is not a retry of the capture: the capture is
            // already lost, and this only decides which of the two lists it belongs in.
            var (start, offset, offset2) = ToneDomainNames.For(preset.ToneTypeStr, zeroBasedPartNo)[0];
            if (await domain.GetDomain(start, offset, offset2).ReadFromIntegraAsync(lease)) throw;

            Log.Debug("This unit exposes no tone for {Preset} ({Bank} {Msb}-{Lsb}-{Pc}).",
                preset.Name, preset.ToneBankStr, preset.Msb, preset.Lsb, preset.Pc);
            return null;
        }
    }

    /// <summary>Everything the sweep is about to overwrite. Its own conversation, because the sweep does not
    /// hold the port between patches and has no business holding it across the hour in between.</summary>
    public async Task<Integra7Snapshot> CaptureStudioSetAsync()
    {
        await using var lease = await api.BeginConversationAsync("capture the Studio Set before a sweep");
        return await StudioSetSnapshotService.CaptureAsync(domain, "before the library sweep", lease);
    }

    /// <summary>And putting it back. No token anywhere in this: it runs on the cancel path as well, and a
    /// cancelled sweep must still leave the user's instrument as it found it.</summary>
    public async Task RestoreStudioSetAsync(Integra7Snapshot studioSet)
    {
        await using var lease = await api.BeginConversationAsync("restore the Studio Set after a sweep");
        await StudioSetSnapshotService.RestoreAsync(domain, studioSet, lease);
    }
}
