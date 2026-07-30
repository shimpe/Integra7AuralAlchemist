using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Walking a plan across an instrument: what happens to the sweep when one patch goes wrong, and
/// what the instrument looks like when the sweep is over.</summary>
public class SeedRunTests
{
    private static Integra7Preset Preset(string name, string type = "SN-A", string bank = "PRST",
        string usage = "INT", int pc = 1) =>
        new(0, usage, type, bank, pc, name, 89, 64, pc, "Ac.Piano");

    /// <summary>Every engine and every bank these presets use, so that the plan below is about the patches
    /// a test names and not about what a selection left out -- that is <c>SeedPlanTests</c>' subject.</summary>
    private static SeedSelection Selection(IReadOnlyList<Integra7Preset> presets) =>
        new(["SN-A", "SN-S", "PCMS", "PCMD", "SN-D"],
            presets.Select(preset => preset.ToneBankStr).Distinct().ToList());

    /// <summary>Plan these presets and sweep them, exactly as the panel will.
    ///
    /// The work goes through <see cref="SeedPlan.Build"/> rather than being assembled by hand, so that these
    /// tests run over the rounds, file names and metadata the planner really produces. A hand-built
    /// <see cref="SeedItem"/> would be a second opinion about the shape of the work, and the two would
    /// eventually disagree without either being wrong on its own.</summary>
    private static Task<SeedOutcome> Sweep(FakeInstrument instrument, IReadOnlyList<Integra7Preset> presets,
        Func<SeedItem, Integra7Snapshot, string>? write = null, IProgress<SeedProgress>? progress = null,
        CancellationToken token = default)
    {
        var selection = Selection(presets);
        var work = SeedPlan.Build(presets, selection, [], instrument.Boards);
        return SeedRun.RunAsync(work, selection, instrument, write ?? ((item, _) => item.FileName),
            progress, token);
    }

    /// <summary>A fake instrument, so the loop's rules -- isolation, restore, cancellation -- are tested
    /// without hardware and without waiting an hour.</summary>
    private sealed class FakeInstrument : ISeedInstrument
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> Silent { get; } = [];      // preset names that expose no tone
        public HashSet<string> Throws { get; } = [];      // preset names whose capture throws
        public HashSet<string> LoadIgnores { get; } = []; // loadouts it accepts and then does not hold
        public HashSet<string> LoadThrows { get; } = [];  // loadouts it refuses outright
        public int[] Boards { get; set; } = [0, 0, 0, 0];

        public Task<int[]> LoadedBoardsAsync() => Task.FromResult(Boards);

        public Task LoadBoardsAsync(int[] boards, CancellationToken token)
        {
            var loadout = string.Join(',', boards);
            Calls.Add($"load {loadout}");
            if (LoadThrows.Contains(loadout)) throw new SnapshotFormatException("the slots are stuck");
            if (!LoadIgnores.Contains(loadout)) Boards = boards;
            return Task.CompletedTask;
        }

        public Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int part, CancellationToken token)
        {
            Calls.Add($"capture {item.Preset.Name}");
            if (Throws.Contains(item.Preset.Name)) throw new SnapshotFormatException("no answer");
            return Task.FromResult<Integra7Snapshot?>(Silent.Contains(item.Preset.Name)
                ? null
                : new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, item.Preset.Name, [],
                    SnapshotKinds.Tone, item.Preset.ToneTypeStr));
        }

        public Task<Integra7Snapshot> CaptureStudioSetAsync()
        {
            Calls.Add("capture studio set");
            return Task.FromResult(new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "before", [],
                SnapshotKinds.StudioSet, null));
        }

        public Task RestoreStudioSetAsync(Integra7Snapshot studioSet)
        {
            Calls.Add("restore studio set");
            return Task.CompletedTask;
        }
    }

    /// <summary>Progress collected as it is reported. Not <see cref="Progress{T}"/>, which posts to a
    /// synchronization context and would turn every assertion about it into a race.</summary>
    private sealed class Reports(Action<SeedProgress>? each = null) : IProgress<SeedProgress>
    {
        public List<SeedProgress> Seen { get; } = [];

        public void Report(SeedProgress value)
        {
            Seen.Add(value);
            each?.Invoke(value);
        }
    }

    /// <summary>Before the first patch, because the first patch overwrites part of it. A Studio Set that
    /// could not be captured cannot be put back, and the sweep is about to spend an hour changing it -- so
    /// this is the one thing that has to succeed before anything else is allowed to start.</summary>
    [Test]
    public async Task The_studio_set_is_captured_before_the_first_patch()
    {
        var instrument = new FakeInstrument();

        await Sweep(instrument, [Preset("Full Grand 1")]);

        Assert.That(instrument.Calls[0], Is.EqualTo("capture studio set"));
    }

    /// <summary>And put back at the end. The user did not ask for their Studio Set to be replaced by
    /// whatever the last patch of the sweep happened to be; they asked for their library to be filled.
    /// </summary>
    [Test]
    public async Task The_studio_set_is_restored_at_the_end()
    {
        var instrument = new FakeInstrument();

        await Sweep(instrument, [Preset("Full Grand 1")]);

        Assert.That(instrument.Calls[^1], Is.EqualTo("restore studio set"));
    }

    /// <summary>Including when a patch went wrong on the way. A sweep that stopped tidying up because one
    /// capture threw would leave the instrument in a state the user never chose, which is worse than the
    /// failed capture it was reacting to.</summary>
    [Test]
    public async Task The_studio_set_is_restored_when_a_capture_throws()
    {
        var instrument = new FakeInstrument();
        instrument.Throws.Add("Broken");

        await Sweep(instrument, [Preset("Full Grand 1"), Preset("Broken")]);

        Assert.That(instrument.Calls[^1], Is.EqualTo("restore studio set"));
    }

    /// <summary>The boards go back before the Studio Set does, and that order is load-bearing: a Studio Set
    /// names a tone per part, some of those tones live on the boards, and restoring it while the slots still
    /// hold the sweep's last loadout would land parts on banks that are not there.</summary>
    [Test]
    public async Task The_boards_are_put_back()
    {
        var instrument = new FakeInstrument { Boards = [7, 0, 0, 0] };

        var outcome = await Sweep(instrument, [Preset("On another board", bank: "SRX08")]);

        Assert.That(instrument.Calls[^2], Is.EqualTo("load 7,0,0,0"));
        Assert.That(instrument.Calls[^1], Is.EqualTo("restore studio set"));
        Assert.That(outcome.RestoreWarning, Is.Null, "the boards went back, so there is nothing to warn about");
    }

    /// <summary>Nothing is loaded when nothing needed loading -- not for the round, and not for the restore
    /// either. A board load converges in about 23 seconds whether or not it changes anything, and a sweep of
    /// the built-in banks is otherwise the quickest run this feature has.</summary>
    [Test]
    public async Task A_boardless_round_loads_nothing()
    {
        var instrument = new FakeInstrument();

        await Sweep(instrument, [Preset("Built in")]);

        Assert.That(instrument.Calls, Is.EqualTo(new[]
        {
            "capture studio set", "capture Built in", "restore studio set",
        }));
    }

    /// <summary>A patch the instrument exposes no tone for is recorded as unavailable and the sweep carries
    /// on. 796 of the 6,023 factory rows answer nothing on the measured unit -- every GM2 and every ExPCM
    /// one -- so this is 13% of a full sweep rather than an exceptional case, and it is emphatically not a
    /// failure: "your instrument does not expose these" and "these failed" are different sentences, and only
    /// the first is true.</summary>
    [Test]
    public async Task A_silent_preset_is_recorded_and_the_sweep_goes_on()
    {
        var instrument = new FakeInstrument();
        instrument.Silent.Add("Not on this unit");

        var outcome = await Sweep(instrument,
            [Preset("Not on this unit"), Preset("The next one"), Preset("And the one after")]);

        Assert.That(outcome.Unavailable.Select(preset => preset.Name),
            Is.EqualTo(new[] { "Not on this unit" }));
        Assert.That(outcome.Failed, Is.Empty, "unavailable is not failed");
        Assert.That(outcome.Written, Is.EqualTo(new[]
        {
            "The next one [89-64-1].json", "And the one after [89-64-1].json",
        }));
    }

    /// <summary>A capture that threw is recorded against the preset it threw for, with what it said, and the
    /// sweep carries on. A run is 6,000 patches and 54 minutes; losing all of it to one of them -- and
    /// losing it 50 minutes in, which is when this is most likely to happen -- is the failure this whole
    /// feature is shaped around.</summary>
    [Test]
    public async Task A_throwing_preset_is_recorded_and_the_sweep_goes_on()
    {
        var instrument = new FakeInstrument();
        instrument.Throws.Add("Broken");

        var outcome = await Sweep(instrument, [Preset("Broken"), Preset("The next one")]);

        Assert.That(outcome.Failed.Single().Preset.Name, Is.EqualTo("Broken"));
        Assert.That(outcome.Failed.Single().Why, Is.EqualTo("no answer"));
        Assert.That(outcome.Unavailable, Is.Empty, "failed is not unavailable");
        Assert.That(outcome.Written, Is.EqualTo(new[] { "The next one [89-64-1].json" }));
    }

    /// <summary>Cancel stops the sweep between patches and never inside one: the three parameter writes and
    /// the capture share a lease, and stopping between them leaves the part holding one patch's bank and
    /// another's program. And the instrument still goes back -- a cancel is the user changing their mind,
    /// not the user asking to keep whatever the sweep had got to.</summary>
    [Test]
    public async Task Cancellation_stops_between_patches_and_still_restores()
    {
        var instrument = new FakeInstrument();
        var cancelling = new CancellationTokenSource();

        var outcome = await Sweep(instrument, [Preset("First"), Preset("Second"), Preset("Third")],
            progress: new Reports(_ => cancelling.Cancel()), token: cancelling.Token);

        Assert.That(outcome.Cancelled, Is.True);
        Assert.That(outcome.Written, Is.EqualTo(new[] { "First [89-64-1].json" }));
        Assert.That(instrument.Calls[^1], Is.EqualTo("restore studio set"));
    }

    /// <summary>Every capture that answered something is handed straight to the library, with the file name
    /// and the annotations the plan decided on. Written as it is captured rather than at the end, because
    /// that is what makes an interrupted sweep resumable: what is on disk when a run stops is what the next
    /// run will not have to do again.</summary>
    [Test]
    public async Task Every_written_snapshot_reaches_the_library()
    {
        var instrument = new FakeInstrument();
        instrument.Silent.Add("Not on this unit");
        List<(SeedItem Item, Integra7Snapshot Snapshot)> writes = [];

        await Sweep(instrument, [Preset("Full Grand 1"), Preset("Not on this unit")],
            write: (item, snapshot) =>
            {
                writes.Add((item, snapshot));
                return item.FileName;
            });

        Assert.That(writes, Has.Count.EqualTo(1), "and nothing at all for the one that answered nothing");
        Assert.That(writes[0].Item.FileName, Is.EqualTo("Full Grand 1 [89-64-1].json"));
        Assert.That(writes[0].Item.Metadata.Category, Is.EqualTo("Ac.Piano"));
        Assert.That(writes[0].Item.Metadata.TagList, Is.EquivalentTo(new[] { "PRST", "factory" }));
        Assert.That(writes[0].Snapshot.Name, Is.EqualTo("Full Grand 1"));
    }

    /// <summary>A round's boards are in the slots before any of its patches is asked for. Capturing a
    /// board's tones before its board has arrived is how a whole round becomes 200 rows of "unavailable"
    /// that were available all along.</summary>
    [Test]
    public async Task A_round_loads_its_boards_before_capturing_any_of_its_items()
    {
        var instrument = new FakeInstrument();

        await Sweep(instrument, [Preset("Built in"), Preset("On a board", bank: "SRX07")]);

        Assert.That(instrument.Calls.IndexOf("load 7,0,0,0"),
            Is.LessThan(instrument.Calls.IndexOf("capture On a board")));
        Assert.That(instrument.Calls.IndexOf("capture Built in"),
            Is.LessThan(instrument.Calls.IndexOf("load 7,0,0,0")),
            "and the boardless round is swept first, so files appear before the first 23-second load");
    }

    /// <summary>The restore is verified by reading the slots back, not assumed from having sent the load.
    /// A user whose boards did not come back finds out when a part goes silent in the middle of something
    /// else, which is both much later and much harder to connect to a sweep they ran this morning.
    ///
    /// This is the one comparison against a read-back that is legitimate here: what was sent is the set the
    /// device had already settled on before the sweep started, so this compares convergence with
    /// convergence rather than convergence with a request -- which is the trap the spike walked into.
    /// </summary>
    [Test]
    public async Task A_restore_that_did_not_take_is_reported_rather_than_assumed()
    {
        var instrument = new FakeInstrument { Boards = [7, 0, 0, 0] };
        instrument.LoadIgnores.Add("7,0,0,0");

        var outcome = await Sweep(instrument, [Preset("On another board", bank: "SRX08")]);

        Assert.That(outcome.RestoreWarning, Is.Not.Null);
        Assert.That(outcome.RestoreWarning, Does.Contain("7, 0, 0, 0"), "what the slots held before");
        Assert.That(outcome.RestoreWarning, Does.Contain("8, 0, 0, 0"), "and what they hold now");
    }

    /// <summary>Progress counts attempts, not successes. On a full sweep the unavailable rows arrive in
    /// runs of hundreds -- every GM2 row, then every ExPCM one -- so a counter that only moved when a file
    /// was written would sit still for minutes at a time, which from the outside is indistinguishable from
    /// a hang. It is also what the panel divides by to say how much longer this will take.</summary>
    [Test]
    public async Task Progress_counts_every_attempt_and_not_only_the_ones_that_wrote_a_file()
    {
        var instrument = new FakeInstrument();
        instrument.Silent.Add("Not on this unit");
        instrument.Throws.Add("Broken");
        var reports = new Reports();

        await Sweep(instrument, [Preset("Not on this unit"), Preset("Broken"), Preset("Fine")],
            progress: reports);

        Assert.That(reports.Seen.Select(report => report.Done), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(reports.Seen.Select(report => report.Current.Preset.Name),
            Is.EqualTo(new[] { "Not on this unit", "Broken", "Fine" }));
        Assert.That(reports.Seen[^1].Total, Is.EqualTo(3));
    }

    /// <summary>A board load that throws ends the sweep -- there is nothing useful to do with a round whose
    /// board never arrived, and an instrument that cannot move its slots will not capture the next round
    /// either -- but it does not end it with the user's instrument left as the sweep had it. This is the
    /// path the <c>finally</c> exists for: every per-patch failure above is caught and recorded, so a run
    /// that restored on its way out of the normal path only would look correct in every other test here and
    /// would abandon the instrument on this one.</summary>
    [Test]
    public void A_board_that_will_not_load_still_puts_the_instrument_back()
    {
        var instrument = new FakeInstrument();
        instrument.LoadThrows.Add("7,0,0,0");

        Assert.That(async () => await Sweep(instrument, [Preset("On a board", bank: "SRX07")]),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(instrument.Calls, Is.EqualTo(new[]
        {
            "capture studio set", "load 7,0,0,0", "load 0,0,0,0", "restore studio set",
        }), "and the slots are put back even though it was the load that failed: it may have emptied them");
    }
}
