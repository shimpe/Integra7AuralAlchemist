using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Borrowing a part and giving it back. The device path, against a fake instrument.</summary>
public class AuditionTests
{
    private const string ToneType = "SN-S";
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Common = "Offset2/SuperNATURAL Synth Tone Common";
    private const string ToneLevel = "SuperNATURAL Synth Tone Common/Tone Level";

    /// <summary>Every block of the engine, because a restore refuses a tone that would only half arrive --
    /// unlike a morph, which writes whatever blocks it is given. Only the common block carries a value;
    /// the rest are empty, which is enough for a restore to read them and write them back unchanged.</summary>
    private static Integra7Snapshot Candidate(long level) => new(
        Integra7Snapshot.CurrentFormatVersion, "candidate",
        ToneDomainNames.For(ToneType, 0)
            .Select(b => new SnapshotDomain(b.Start, b.Offset, b.Offset2, ValuesOf(b.Offset2, level)))
            .ToList(),
        SnapshotKinds.Tone, ToneType);

    private static List<SnapshotValue> ValuesOf(string offset2, long level) =>
        offset2 == Common ? [new SnapshotValue(ToneLevel, $"{level}", level)] : [];

    /// <summary>Borrowing reads the part and writes nothing. The read is the whole safety of the feature --
    /// it is the only copy of what the user had -- and the write is deliberately the caller's, so that a
    /// failure to write the candidate cannot take the capture down with it.</summary>
    [Test]
    public async Task Borrowing_captures_the_part_and_writes_nothing()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var borrowed = await Audition.BorrowAsync(domain, Candidate(64), zeroBasedPartNo: 0, ToneType,
            StudioSetSnapshotServiceTests.NoRealMidi());

        Assert.Multiple(() =>
        {
            Assert.That(borrowed, Is.Not.Null);
            Assert.That(api.Requests, Is.GreaterThan(0), "the part was read");
            Assert.That(api.Transmissions, Is.Zero, "and nothing was written");
        });
    }

    /// <summary>The capture is of the part as it stands, not of the candidate about to be played in it.
    /// Getting this backwards would give the user back the tone they were auditioning.</summary>
    [Test]
    public async Task What_is_borrowed_is_the_part_not_the_candidate()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var borrowed = await Audition.BorrowAsync(domain, Candidate(64), 0, ToneType,
            StudioSetSnapshotServiceTests.NoRealMidi());

        var level = borrowed.Domains.Single(d => d.Offset2 == Common).Values
            .Single(v => v.Path == ToneLevel);
        Assert.That(level.Raw, Is.Zero, "the blank instrument answers zeros; 64 would be the candidate");
    }

    /// <summary>And stopping puts back exactly what was captured, not something rebuilt from it.</summary>
    [Test]
    public async Task Stopping_writes_back_what_was_captured()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var borrowed = await Audition.BorrowAsync(domain, Candidate(64), 0, ToneType,
            StudioSetSnapshotServiceTests.NoRealMidi());
        // What the caller does between the two: play something else in the part.
        await StudioSetSnapshotService.RestoreToneAsync(domain, Candidate(64), 0, ToneType,
            StudioSetSnapshotServiceTests.NoRealMidi());

        await Audition.StopAsync(domain, borrowed, 0, ToneType, StudioSetSnapshotServiceTests.NoRealMidi());

        var block = domain.GetDomain("Temporary Tone Part 1", Offset, Common);
        Assert.That(block.LookupSingleParameterDisplayedValue(ToneLevel), Is.EqualTo("0"),
            "the blank instrument answered zeros, so that is what has to come back");
    }

    /// <summary>The engine guard is the restore path's, not this class's -- but a candidate of the wrong
    /// engine must be refused before the part is read, or the user pays for a capture that cannot be
    /// used.</summary>
    [Test]
    public void A_candidate_of_another_engine_is_refused_before_anything_is_read()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        Assert.That(
            async () => await Audition.BorrowAsync(domain, Candidate(64), 0, "PCMS",
                StudioSetSnapshotServiceTests.NoRealMidi()),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(api.Requests, Is.Zero);
    }
}
