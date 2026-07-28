using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A blend reaching a part. Unlike a restore this does not read the blocks first, which is what
/// makes it affordable four times a second.</summary>
public class MorphWriterTests
{
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Offset2 = "Offset2/SuperNATURAL Synth Tone Common";
    private const string ToneLevel = "SuperNATURAL Synth Tone Common/Tone Level";

    private static Integra7Snapshot Blend(long level) =>
        new(Integra7Snapshot.CurrentFormatVersion, "blend",
            [
                new SnapshotDomain("Temporary Tone Part 1", Offset, Offset2,
                    [new SnapshotValue(ToneLevel, $"{level}", level)]),
            ],
            SnapshotKinds.Tone, "SN-S");

    [Test]
    public async Task It_writes_each_block_once_and_reads_nothing()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        await MorphWriter.WriteAsync(domain, Blend(64), zeroBasedPartNo: 0, "SN-S", lease: null);

        Assert.That(api.Transmissions, Is.EqualTo(1), "one transmission for the one block");
        Assert.That(api.Requests, Is.Zero, "and no reads: a blend covers every parameter itself");
    }

    [Test]
    public async Task The_value_reaches_the_block()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        await MorphWriter.WriteAsync(domain, Blend(64), zeroBasedPartNo: 0, "SN-S", lease: null);

        var block = domain.GetDomain("Temporary Tone Part 1", Offset, Offset2);
        Assert.That(block.LookupSingleParameterDisplayedValue(ToneLevel), Is.EqualTo("64"));
    }

    /// <summary>The part number lives in the Start address, so a blend built from patches captured
    /// anywhere lands in the part asked for.</summary>
    [Test]
    public async Task It_targets_the_part_it_is_given_not_the_one_in_the_snapshot()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        await MorphWriter.WriteAsync(domain, Blend(80), zeroBasedPartNo: 4, "SN-S", lease: null);

        var block = domain.GetDomain("Temporary Tone Part 5", Offset, Offset2);
        Assert.That(block.LookupSingleParameterDisplayedValue(ToneLevel), Is.EqualTo("80"));
    }
}
