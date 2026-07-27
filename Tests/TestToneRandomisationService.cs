using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Randomise reaches the instrument as one bulk write per block and reaches the history as one
/// undo step. Both halves matter: the write is what makes it fast enough to use on a whole tone, and the
/// single step is what makes a randomise you dislike one press away from gone.</summary>
public class ToneRandomisationServiceTests
{
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";

    private static IReadOnlyList<(string, string, string)> OnePartial() =>
        [("Temporary Tone Part 1", Offset, "Offset2/SuperNATURAL Synth Tone Partial 1")];

    private static RandomisationStrengths Everything() =>
        new(Enum.GetValues<ToneCategory>().ToDictionary(c => c, _ => 1.0));

    [SetUp]
    public void ClearHistory() => EditJournal.Default.Clear();

    [Test]
    public async Task Records_one_undo_step_for_the_whole_operation()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var changed = await ToneRandomisationService.RandomiseAsync(
            domain, OnePartial(), Everything(), new Random(11), lease: null);

        Assert.That(changed, Is.GreaterThan(0));
        Assert.That(EditJournal.Default.CanUndo, Is.True);
        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Writes, Has.Count.EqualTo(changed),
            "one step, carrying every parameter the randomise moved");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "and only one step");
    }

    [Test]
    public async Task Sends_one_transmission_per_block()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        await ToneRandomisationService.RandomiseAsync(
            domain, OnePartial(), Everything(), new Random(12), lease: null);

        Assert.That(api.Transmissions, Is.EqualTo(1));
    }

    [Test]
    public async Task Changes_nothing_and_writes_nothing_when_no_category_is_ticked()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var changed = await ToneRandomisationService.RandomiseAsync(domain, OnePartial(),
            new RandomisationStrengths(new Dictionary<ToneCategory, double>()), new Random(13),
            lease: null);

        Assert.That(changed, Is.Zero);
        Assert.That(api.Transmissions, Is.Zero, "an untouched block is not rewritten");
        Assert.That(EditJournal.Default.CanUndo, Is.False);
    }

    /// <summary>A block the device does not answer for must abort rather than randomise from whatever
    /// values happen to be in memory -- which, for a block never read this session, are zeros.</summary>
    [Test]
    public void Refuses_when_the_device_does_not_answer()
    {
        var domain = StudioSetSnapshotServiceTests.BuildDomain(
            new TestFailedReadKeepsValues.SilentApi());

        Assert.That(async () => await ToneRandomisationService.RandomiseAsync(
                domain, OnePartial(), Everything(), new Random(14), lease: null),
            Throws.TypeOf<SnapshotFormatException>());
    }

    [Test]
    public void Names_the_drum_partial_block_for_a_note()
    {
        var block = ToneDomainNames.DrumPartialFor("SN-D", zeroBasedPartNo: 3, zeroBasedNoteIndex: 5);

        Assert.That(block.Start, Is.EqualTo("Temporary Tone Part 4"));
        Assert.That(block.Offset2, Is.EqualTo("Offset2/SuperNATURAL Drum Kit Partial 6"));
        Assert.That(ToneDomainNames.IsDrumKit("SN-D"), Is.True);
        Assert.That(ToneDomainNames.IsDrumKit("SN-S"), Is.False);
    }
}
