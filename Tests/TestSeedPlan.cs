using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Turning a selection into an ordered list of work.</summary>
public class SeedPlanTests
{
    private static Integra7Preset Preset(string name, string type = "SN-A", string bank = "PRST",
        string usage = "INT", int pc = 1) =>
        new(0, usage, type, bank, pc, name, 89, 64, pc, "Ac.Piano");

    private static SeedSelection Everything(params string[] banks) =>
        new(["SN-A", "SN-S", "PCMS", "PCMD", "SN-D"], banks.Length == 0 ? ["PRST"] : banks);

    [Test]
    public void A_selected_preset_becomes_one_item()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
        Assert.That(work.Rounds[0].Items[0].FileName, Is.EqualTo("Full Grand 1 [89-64-1].json"));
    }

    /// <summary>The address is in the file name because the name alone is not unique and the library will
    /// not overwrite: 405 of the 6,022 catalogue rows share a name with another row -- three Harps, three
    /// Shakuhachis, three Snare Menu 1s -- and <c>SnapshotLibrary.Create</c> answers a collision with
    /// " (2)". A sweep that let it would write ~208 files under names its own planner never predicts, so
    /// every re-run would capture them again and the folder would grow by 208 files each time while the
    /// resume looked like it was working. Unique by construction is the only version of this that stays
    /// true after the second run.</summary>
    [Test]
    public void Two_presets_with_one_name_get_two_file_names()
    {
        var work = SeedPlan.Build(
            [Preset("Harp", bank: "PRST", pc: 12), Preset("Harp", bank: "SRX07", pc: 40)],
            Everything("PRST", "SRX07"), [], []);

        var names = work.Rounds.SelectMany(round => round.Items).Select(item => item.FileName).ToList();
        Assert.That(names, Is.Unique);
    }

    [Test]
    public void An_engine_that_was_not_ticked_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Pad", type: "SN-S")],
            new SeedSelection(["SN-A"], ["PRST"]), [], []);

        Assert.That(work.Count, Is.EqualTo(0));
        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.NotSelected));
    }

    [Test]
    public void A_bank_that_was_not_ticked_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Pad", bank: "SRX07")], Everything("PRST"), [], []);

        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.NotSelected));
    }

    /// <summary>The resume, and it costs nothing: a file already in the folder is not read, not compared,
    /// just not swept again. Matched on the file name because that is what the sweep would write and what
    /// the folder can be asked for cheaply -- the alternative, opening every snapshot to compare its
    /// address, is a folder read to save a folder read.</summary>
    [Test]
    public void A_preset_already_in_the_library_is_skipped()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(),
            ["Full Grand 1 [89-64-1].json"], []);

        Assert.That(work.Count, Is.EqualTo(0));
        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.AlreadyInLibrary));
    }

    /// <summary>Case-insensitively, because the folder is on Windows and "full grand 1.json" is the same
    /// file. A sweep that captured it again would write a second file the folder cannot hold.</summary>
    [Test]
    public void An_existing_file_matches_whatever_its_case()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(),
            ["FULL GRAND 1 [89-64-1].JSON"], []);

        Assert.That(work.Skipped.Single().Why, Is.EqualTo(SeedSkip.AlreadyInLibrary));
    }

    /// <summary>An untouched user slot. The instrument names them "INIT TONE", "INIT KIT" and so on, and
    /// there are up to 1,120 slots -- so this is the difference between a sweep of the user's own sounds and
    /// a sweep of nine hundred copies of the same empty patch.</summary>
    [Test]
    public void An_empty_user_slot_is_skipped()
    {
        var work = SeedPlan.Build([Preset("INIT TONE", usage: "USR"), Preset("INIT KIT", usage: "USR"),
            Preset("Mine", usage: "USR")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
        Assert.That(work.Rounds[0].Items[0].Preset.Name, Is.EqualTo("Mine"));
        Assert.That(work.Skipped.Select(s => s.Why),
            Is.EqualTo(new[] { SeedSkip.EmptySlot, SeedSkip.EmptySlot }));
    }

    /// <summary>Only a user slot. A factory preset legitimately called "Init Tone" is a sound somebody
    /// designed, and the instrument ships one -- dropping it because of its name would be this feature
    /// deciding it knows better than the tone list.</summary>
    [Test]
    public void A_factory_preset_named_init_is_not_an_empty_slot()
    {
        var work = SeedPlan.Build([Preset("INIT TONE")], Everything(), [], []);

        Assert.That(work.Count, Is.EqualTo(1));
    }

    [Test]
    public void The_two_sides_can_be_asked_for_separately()
    {
        Integra7Preset[] presets = [Preset("Factory"), Preset("Mine", usage: "USR")];

        var userOnly = SeedPlan.Build(presets,
            new SeedSelection(["SN-A"], ["PRST"], IncludeInternal: false), [], []);
        var factoryOnly = SeedPlan.Build(presets,
            new SeedSelection(["SN-A"], ["PRST"], IncludeUser: false), [], []);

        Assert.That(userOnly.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Mine"));
        Assert.That(factoryOnly.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Factory"));
    }

    /// <summary>Presets needing no board come first and in one round, so a sweep starts producing files
    /// immediately instead of spending 23 seconds loading a board before the first capture.</summary>
    [Test]
    public void The_boardless_presets_are_one_round_and_come_first()
    {
        var work = SeedPlan.Build(
            [Preset("On a board", bank: "SRX07"), Preset("Built in", bank: "PRST")],
            Everything("PRST", "SRX07"), [], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(2));
        Assert.That(work.Rounds[0].Boards, Is.Null);
        Assert.That(work.Rounds[0].Items.Single().Preset.Name, Is.EqualTo("Built in"));
        Assert.That(work.Rounds[1].Boards, Is.EqualTo(new[] { 7, 0, 0, 0 }));
    }

    /// <summary>Four boards to a round, because the instrument has four slots -- so eight selected boards
    /// are two loads, not eight.</summary>
    [Test]
    public void Up_to_four_boards_share_a_round()
    {
        var presets = new[] { "SRX01", "SRX02", "SRX03", "SRX04", "SRX05" }
            .Select(bank => Preset($"On {bank}", bank: bank)).ToArray();

        var work = SeedPlan.Build(presets, Everything("SRX01", "SRX02", "SRX03", "SRX04", "SRX05"), [], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(2));
        Assert.That(work.Rounds[0].Items, Has.Count.EqualTo(4));
        Assert.That(work.Rounds[1].Items, Has.Count.EqualTo(1));
    }

    /// <summary>A round whose every patch is already in the library is not a round: loading four boards to
    /// capture nothing is 23 seconds spent on an empty answer, and an interrupted sweep resumed near its end
    /// would otherwise spend minutes reloading boards before reaching the work that is left.</summary>
    [Test]
    public void A_round_with_nothing_left_to_do_is_dropped()
    {
        var work = SeedPlan.Build(
            [Preset("Built in"), Preset("On a board", bank: "SRX07")],
            Everything("PRST", "SRX07"), ["On a board [89-64-1].json"], []);

        Assert.That(work.Rounds, Has.Count.EqualTo(1));
        Assert.That(work.Rounds[0].Boards, Is.Null);
    }

    /// <summary>The boards already loaded do not need loading again, which is the difference between a
    /// one-board sweep that starts now and one that starts in 23 seconds.</summary>
    [Test]
    public void A_board_that_is_already_loaded_costs_no_round_of_its_own()
    {
        var work = SeedPlan.Build([Preset("On a board", bank: "SRX07")],
            Everything("SRX07"), [], [7, 0, 0, 0]);

        Assert.That(work.Rounds, Has.Count.EqualTo(1));
        Assert.That(work.Rounds[0].Boards, Is.Null);
    }

    /// <summary>The estimate is built from times measured on the instrument, so a drum kit counts for what
    /// it costs -- 6 s against 116 ms for an SN-A tone. An estimate that averaged them would promise ten
    /// minutes for a sweep that takes an hour.</summary>
    [Test]
    public void The_estimate_charges_each_engine_what_it_measured()
    {
        var synth = SeedPlan.Build([Preset("Tone", type: "SN-A")], Everything(), [], []);
        var kit = SeedPlan.Build([Preset("Kit", type: "PCMD")], Everything(), [], []);

        Assert.That(kit.Estimate, Is.GreaterThan(synth.Estimate * 10));
    }

    /// <summary>Loading boards is most of a small sweep's time and none of its captures, so it is in the
    /// estimate. Two rounds of one board each cost two loads.</summary>
    [Test]
    public void The_estimate_includes_the_board_loads()
    {
        var withoutBoards = SeedPlan.Build([Preset("A")], Everything(), [], []);
        var withBoards = SeedPlan.Build(
            [Preset("A"), Preset("B", bank: "SRX07")], Everything("PRST", "SRX07"), [], []);

        Assert.That(withBoards.Estimate - withoutBoards.Estimate, Is.GreaterThan(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public void Nothing_selected_is_no_work_and_no_failure()
    {
        var work = SeedPlan.Build([Preset("A")], new SeedSelection([], []), [], []);

        Assert.That(work.Rounds, Is.Empty);
        Assert.That(work.Estimate, Is.EqualTo(TimeSpan.Zero));
    }

    /// <summary>The category comes from the table, which is the instrument's own vocabulary and the same one
    /// the library's category filter offers -- a sweep that invented its own would put 6,000 snapshots
    /// outside every filter the browser has.</summary>
    [Test]
    public void A_swept_snapshot_carries_the_presets_category()
    {
        var work = SeedPlan.Build([Preset("Full Grand 1")], Everything(), [], []);

        Assert.That(work.Rounds[0].Items[0].Metadata.Category, Is.EqualTo("Ac.Piano"));
    }

    /// <summary>Two tags: where it came from, and which side it came from. The bank tag is how a user finds
    /// the SRX07 sounds again; the factory/user tag is how they find the ones that are theirs among six
    /// thousand that are not, which is the whole reason a sweep is survivable.</summary>
    [Test]
    public void A_swept_snapshot_is_tagged_with_its_bank_and_its_side()
    {
        var factory = SeedPlan.Build([Preset("A", bank: "SRX07")], Everything("SRX07"), [], []);
        var mine = SeedPlan.Build([Preset("B", usage: "USR")], Everything(), [], []);

        Assert.That(factory.Rounds[0].Items[0].Metadata.TagList,
            Is.EquivalentTo(new[] { "SRX07", "factory" }));
        Assert.That(mine.Rounds[0].Items[0].Metadata.TagList, Is.EquivalentTo(new[] { "PRST", "user" }));
    }
}
