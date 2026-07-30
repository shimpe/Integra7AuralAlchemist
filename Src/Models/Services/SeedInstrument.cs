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
    /// A board load takes 5 seconds, three of them about 13, and the <c>HQ Pcm</c> loadout 18.7 -- so polling
    /// faster than this buys nothing but traffic on a wire the sweep wants for captures. It is also the reply
    /// deadline, which is what a poll costs while the instrument is loading: it answers nothing at all until
    /// it has finished.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>How quickly a reading has to arrive to have come from the instrument.
    ///
    /// <b>This is how "still loading" is recognised, and it is a measurement rather than a guess.</b> On the
    /// user's unit on 2026-07-30 an idle instrument answered the slot query in 2 to 5 milliseconds, every
    /// time, whatever the slots held -- all four Off included. While it was loading boards it answered
    /// nothing, and the read came back at 1,504 to 1,514 milliseconds having run out its deadline, which
    /// <see cref="Integra7Api.GetLoadedSrxAsync"/> reports as (0,0,0,0) because there is nothing else it
    /// could say. So the two states are three hundred times apart and this threshold sits between them with
    /// two orders of magnitude of room on either side; it is written as half the deadline in
    /// <c>AsyncMidiInputWrapper</c> so that raising that deadline cannot quietly turn every timed-out read
    /// into an answer.</summary>
    private static readonly TimeSpan AnsweredWithin = TimeSpan.FromMilliseconds(750);

    /// <summary>When to stop waiting for the slots to hold still. Four times the slowest convergence ever
    /// measured (<c>HQ Pcm</c>, stable at 23.3 seconds), because the cost of being wrong in the two directions
    /// is not symmetric: waiting too long delays a round, and giving up too early sweeps a whole board's worth
    /// of patches against slots that do not hold it yet and records every one of them as unavailable.</summary>
    private static readonly TimeSpan SettleCeiling = TimeSpan.FromSeconds(90);

    /// <summary>The four slot values the instrument reports right now.</summary>
    public async Task<int[]> LoadedBoardsAsync() => (await ReadSlotsAsync()).Slots;

    /// <summary>One reading of the four slots, and whether it came from the instrument at all: see
    /// <see cref="AnsweredWithin"/>. A read that ran out its deadline reads as (0,0,0,0), which is also what
    /// an idle instrument with empty slots says, and telling those two apart is the whole of knowing whether
    /// a load is still in flight.</summary>
    private async Task<(int[] Slots, bool Answered)> ReadSlotsAsync()
    {
        var clock = Stopwatch.StartNew();
        var (slot1, slot2, slot3, slot4) = await api.GetLoadedSrxAsync();
        return ([slot1, slot2, slot3, slot4], clock.Elapsed < AnsweredWithin);
    }

    /// <summary>Send a loadout and wait for the instrument to settle on one.
    ///
    /// <b>Nothing is sent to the slots while the instrument is moving them, and this is the whole point of
    /// the method.</b> A loadout that arrives during a load is discarded in silence -- measured on the user's
    /// unit, and the reason a sweep cancelled 5.7 seconds into a board round put its restore into the void
    /// and left three of their boards evicted. There is no acknowledgement to check and no error to catch, so
    /// the only defence is not to send: the wait below happens twice, once to find an instrument that is not
    /// busy and once to see the request through.
    ///
    /// <b>Which means a load, once sent, is finished.</b> <paramref name="token"/> is honoured while waiting
    /// for the instrument to become free -- nothing has been sent yet, so stopping there stops nothing -- and
    /// not for a moment after that. A cancel pressed during a board round therefore takes up to about half a
    /// minute to be acted on, which is the price of the sweep's own board load being over before its restore
    /// begins. Abandoning the wait is what the old code did, and it is exactly how the restore came to be
    /// sent into a busy instrument.
    ///
    /// <b>What "settled" means is <see cref="SeedSettling"/>'s, and deliberately not decided here.</b> It is
    /// the one piece of this adapter that could be got wrong without a device on the desk, so it is not in an
    /// adapter that has no tests. In short: three readings the instrument actually answered, all agreeing --
    /// never a comparison against what was sent, because the device rewrites a loadout it is given.
    ///
    /// <b>And it says in the log what it saw, not merely what it concluded.</b> A conclusion about a board
    /// having finished loading is the thing other findings are built on -- whether a bank is capturable on
    /// this unit is decided by reading its tones once the boards are supposedly in place -- and the last
    /// version of this convinced itself with a rule that could not tell a finished load from one that had
    /// not started. So the line it writes carries the evidence: what the slots held before the request, how
    /// many polls the instrument spent answering nothing, what it settled on, and how long all of that took.
    /// A load during which the instrument never once went quiet did not load anything, and a reader of the
    /// log can see that for themselves rather than take this method's word for it.</summary>
    public async Task LoadBoardsAsync(int[] boards, CancellationToken token)
    {
        var before = await SettleAsync("before sending a loadout", token);
        await api.SendLoadSrxAsync((byte)boards[0], (byte)boards[1], (byte)boards[2], (byte)boards[3]);
        var after = await SettleAsync($"after asking for {Slots(boards)}", CancellationToken.None);

        // "Quiet" rather than "loading", because that is the observation; the sentence after it is the
        // reading of the observation and is kept separate from it on purpose.
        Log.Information(
            "The expansion slots settled on {Now} {Seconds:0.0} s after the instrument was asked for "
            + "{Asked}. They held {Before} beforehand, and it answered nothing for {Quiet} of the {Polls} "
            + "polls in between -- {Verdict}.",
            Slots(after.Slots), after.Took.TotalSeconds, Slots(boards), Slots(before.Slots), after.Quiet,
            after.Polls, Verdict(before.Slots, after));
    }

    /// <summary>What the evidence in the log amounts to, in one clause, and it is worth being careful here
    /// because the interesting case looks like success.
    ///
    /// An instrument that never stopped answering never loaded anything -- verified by sending the user's own
    /// (2,13,6,0) back to them, which produced not a single quiet poll. That is the right outcome when the
    /// loadout was already in the slots, and it is a load that did not happen when it was not. The two are
    /// told apart by whether the slots changed, which is only a legitimate comparison because both sides of
    /// it are settled readings taken either side of the request rather than a comparison against the request
    /// itself.</summary>
    private static string Verdict(int[] before, Settled after) => after.Quiet switch
    {
        0 when before.SequenceEqual(after.Slots) =>
            "it never went quiet and the slots did not change, so it already held what it was asked for",
        0 => "it never went quiet, so the slots changed without a load, which nothing here can explain",
        _ when before.SequenceEqual(after.Slots) =>
            "it went quiet and came back holding what it started with",
        _ => "it went quiet, then came back holding a different loadout, which is a load that has finished",
    };

    /// <summary>What a wait for the slots to hold still saw.</summary>
    /// <param name="Slots">The reading it settled on.</param>
    /// <param name="Quiet">How many polls the instrument answered nothing at all for -- the only direct
    /// evidence there is that it was working, since a load is neither acknowledged nor reported.</param>
    /// <param name="Polls">How many readings were taken altogether.</param>
    /// <param name="Took">How long the wait was.</param>
    private readonly record struct Settled(int[] Slots, int Quiet, int Polls, TimeSpan Took);

    /// <summary>Poll until the slots hold still, and answer what was seen on the way.</summary>
    /// <param name="what">Which of the two waits this is, for the message a user would have to act on.</param>
    /// <param name="token">Honoured only by the wait that has sent nothing.</param>
    private async Task<Settled> SettleAsync(string what, CancellationToken token)
    {
        var settling = new SeedSettling();
        var clock = Stopwatch.StartNew();
        int quiet = 0, polls = 0;

        while (clock.Elapsed < SettleCeiling)
        {
            // The wait comes first, and that ordering is load-bearing rather than incidental. For the first
            // few tens of milliseconds after a loadout is sent the instrument answers the slot query with
            // the loadout it is *leaving* -- measured on five consecutive loads, every one of them, between
            // 3 and 29 ms, including answering (19,20,21,22) when it had just been asked for (11,0,0,0).
            // Reading before waiting therefore starts the settling rule on a reading of the wrong thing.
            // A scratch harness written to measure this feature did exactly that, believed the stale
            // answer, sent a Studio Set restore into an instrument that was still loading, and left a part
            // holding a tone its owner had not chosen. Anyone moving this read ahead of the wait is
            // reintroducing that.
            await Task.Delay(PollInterval, token);
            var (slots, answered) = await ReadSlotsAsync();
            polls++;
            if (!answered) quiet++;

            Log.Debug("Expansion slots, {What}, t+{Seconds:0.0} s: the instrument {Answer}.", what,
                clock.Elapsed.TotalSeconds,
                answered ? $"answered {Slots(slots)}" : "answered nothing within its reply deadline");

            if (settling.Settled(answered ? slots : null))
                return new Settled(slots, quiet, polls, clock.Elapsed);
        }

        // Named, because the message is read by somebody looking at an instrument and the loadout is the
        // only part of this they can act on -- and because the sweep puts it in the outcome verbatim rather
        // than stopping with an exception nobody kept. A TimeoutException rather than one of this
        // application's own: nothing is malformed here, the instrument simply never stopped moving.
        throw new TimeoutException(
            $"The instrument's expansion slots did not hold still {what}, within "
            + $"{SettleCeiling.TotalSeconds:0} seconds.");
    }

    /// <summary>All four values as the device reports them, zeros included: these strings are read by
    /// somebody looking at an instrument, and which slot went empty is the useful half of it.</summary>
    private static string Slots(int[] slots) => string.Join(", ", slots);

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
            // difference between "these are not available on your instrument" and "these failed" -- one of
            // which is true and the other of which has a user hunting a fault that is not there. A patch this
            // unit does not expose answers nothing from the very first block: the Studio Set Part stores the
            // bank and program quite happily and then all five engines' temporary areas stay silent, which is
            // what a board that is not in a slot looks like from here, and what every GM2 and ExPCM row looks
            // like. A tone that answered and then stopped partway is a real failure and has to reach the
            // sweep as one.
            //
            // Which it was is asked of the device rather than read out of the exception's wording. Matching
            // on the message would make this depend on a sentence in another file that nobody would think to
            // check when they reworded it, and the way it would break is silent: every unavailable row
            // reported as a failure. The cost is one extra read on the failing path only -- 1.5 s apiece, and
            // nothing at all on the patches that capture, which is what keeps SeedPlan's measured estimate
            // honest now that a sweep's silent rows are the exception. It is not a retry of the capture: the
            // capture is already lost, and this only decides which of the two lists it belongs in.
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
