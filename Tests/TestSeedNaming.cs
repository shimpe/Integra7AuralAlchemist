using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What a swept snapshot is called once it has been captured, and what it records about the name it
/// was listed under. The device is the authority and the catalogue is a label: an audit of all 5,227
/// reachable rows on 2026-07-30 found 102 places they disagreed, and 97 of those were the table being wrong.
/// </summary>
public class SeedNamingTests
{
    /// <summary>Through <see cref="SeedPlan.Build"/> rather than assembled by hand, so these run over the
    /// category, tags and file name the planner really produces -- a hand-built item would be a second
    /// opinion about the shape of the work, and the two would drift without either being wrong alone.
    /// </summary>
    private static SeedItem Item(string name, string toneType = "SN-S") =>
        SeedPlan.Build([new Integra7Preset(0, "INT", toneType, "PRST", 1, name, 89, 64, 1, "Ac.Piano")],
            new SeedSelection([toneType], ["PRST"]), [], []).Rounds[0].Items[0];

    /// <summary>A capture of one engine's common block, which is where every engine keeps its name and which
    /// is why the addresses come from <see cref="ToneDomainNames"/> rather than being typed out again.
    /// </summary>
    private static Integra7Snapshot Captured(string toneType, params SnapshotValue[] values)
    {
        var (start, offset, offset2) = ToneDomainNames.For(toneType, 0)[0];
        return new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "as captured",
            [new SnapshotDomain(start, offset, offset2, [.. values])], SnapshotKinds.Tone, toneType);
    }

    /// <summary>An SN-S capture whose common block holds a level and then a name. The name is deliberately
    /// not the first value in the block: a lookup that took whatever came first would pass every test in
    /// this file and read a tone level as a patch name on the instrument.</summary>
    private static Integra7Snapshot Tone(string toneName) => Captured("SN-S",
        new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
        new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", toneName));

    /// <summary>The ordinary 98% of rows. Nothing to say, so nothing is said -- a note on every one of six
    /// thousand snapshots would make the field useless for the 2% that need it.</summary>
    [Test]
    public void A_name_the_table_agrees_with_is_written_without_a_note()
    {
        var metadata = SeedNaming.MetadataFor(Tone("Full Grand 1"), Item("Full Grand 1"));

        Assert.That(metadata.Name, Is.EqualTo("Full Grand 1"));
        Assert.That(metadata.Notes, Is.Empty);
    }

    /// <summary>And the 2% that do. "Ring E.Piano" is the sound that comes out of the instrument; "Ring
    /// Piano" is what the book says -- so the snapshot is called the first and remembers the second, because
    /// a user searching their library for the name printed in their manual should still find the patch.
    /// </summary>
    [Test]
    public void A_name_the_table_disagrees_with_is_the_devices_and_the_table_becomes_a_note()
    {
        var metadata = SeedNaming.MetadataFor(Tone("Ring E.Piano"), Item("Ring Piano"));

        Assert.That(metadata.Name, Is.EqualTo("Ring E.Piano"));
        Assert.That(metadata.Notes, Is.EqualTo("Listed as \"Ring Piano\""));
    }

    /// <summary>The instrument pads a name out to the width of its field. Left on, every row in the
    /// catalogue would differ from the table and every snapshot in the library would carry a note saying so
    /// -- which is the failure that hides the hundred that are real.</summary>
    [Test]
    public void The_padding_the_instrument_writes_after_a_name_is_not_part_of_it()
    {
        var metadata = SeedNaming.MetadataFor(Tone("Ring E.Piano    "), Item("Ring E.Piano"));

        Assert.That(metadata.Name, Is.EqualTo("Ring E.Piano"));
        Assert.That(metadata.Notes, Is.Empty, "and the padding is not a disagreement worth recording");
    }

    /// <summary>A capture with no name in it keeps the catalogue's. Two things are being refused at once: an
    /// empty name, which is the one field the browser cannot show and which would leave a row the user
    /// cannot tell from the row above it; and a note claiming the table disagreed with nothing.</summary>
    [Test]
    public void A_capture_with_no_readable_name_keeps_the_one_the_table_gave()
    {
        var nameless = Captured("SN-S",
            new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100));

        var metadata = SeedNaming.MetadataFor(nameless, Item("Full Grand 1"));

        Assert.That(metadata.Name, Is.EqualTo("Full Grand 1"));
        Assert.That(metadata.Notes, Is.Empty);
    }

    /// <summary>Every engine keeps its name under its own path, and this is the test that says the mapping
    /// is really five answers rather than one. Reading a PCM drum kit through the SuperNATURAL synth's path
    /// finds nothing at all, so all 216 kits would silently fall back to the catalogue name -- green
    /// everywhere else in this file, and wrong on the engine that costs the most to sweep.</summary>
    [Test]
    public void Each_engine_is_read_from_its_own_name_parameter()
    {
        (string ToneType, string Path)[] engines =
        [
            ("PCMD", "PCM Drum Kit Common/Kit Name"),
            ("PCMS", "PCM Synth Tone Common/PCM Synth Tone Name"),
            ("SN-A", "SuperNATURAL Acoustic Tone Common/Tone Name"),
            ("SN-S", "SuperNATURAL Synth Tone Common/Tone Name"),
            ("SN-D", "SuperNATURAL Drum Kit Common/Kit Name"),
        ];

        foreach (var (toneType, path) in engines)
        {
            var captured = Captured(toneType, new SnapshotValue(path, "What The Device Says"));

            Assert.That(SeedNaming.MetadataFor(captured, Item("What The Table Says", toneType)).Name,
                Is.EqualTo("What The Device Says"), toneType);
        }
    }

    /// <summary>The plan's annotations are carried through, not built again. There is one place that decides
    /// what a swept snapshot is categorised and tagged as, and it is <see cref="SeedPlan.Build"/>; a second
    /// place deriving the same answers from the same preset would agree until the day it did not. The
    /// fixture therefore carries annotations no planner would ever produce, so that anything rebuilt from
    /// the preset here is visible rather than coincidentally identical.</summary>
    [Test]
    public void The_annotations_the_plan_decided_on_are_carried_through_untouched()
    {
        var item = Item("Full Grand 1") with
        {
            Metadata = new SnapshotMetadata("E.Piano", ["mine", "for the trio gig"], Rating: 4,
                Favourite: true),
        };

        var metadata = SeedNaming.MetadataFor(Tone("Ring E.Piano"), item);

        Assert.That(metadata.Category, Is.EqualTo("E.Piano"));
        Assert.That(metadata.TagList, Is.EqualTo(new[] { "mine", "for the trio gig" }));
        Assert.That(metadata.Rating, Is.EqualTo(4));
        Assert.That(metadata.Favourite, Is.True);
        Assert.That(metadata.Name, Is.EqualTo("Ring E.Piano"), "and the name is still the device's");
    }

    /// <summary>What the sweep actually gets from the planner, in case the two tests above ever stop being
    /// about the same object: the category is the instrument's own vocabulary and the tags say where the
    /// sound came from and whose side it is on.</summary>
    [Test]
    public void A_swept_snapshot_carries_the_plans_category_and_tags()
    {
        var metadata = SeedNaming.MetadataFor(Tone("Full Grand 1"), Item("Full Grand 1"));

        Assert.That(metadata.Category, Is.EqualTo("Ac.Piano"));
        Assert.That(metadata.TagList, Is.EquivalentTo(new[] { "PRST", "factory" }));
    }
}
