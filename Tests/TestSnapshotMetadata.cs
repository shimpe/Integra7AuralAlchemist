using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The five things a library needs to say about a snapshot that the snapshot does not already
/// say about itself: its category, its tags, its notes, its rating and whether it is a favourite. They
/// live in the file rather than in a sidecar or an index so that a file carries its own notes when it is
/// copied or sent, and they are written before the parameter data so that listing a folder can read each
/// file's head and stop.</summary>
public class SnapshotMetadataTests
{
    private static Integra7Snapshot Minimal() => new(
        Integra7Snapshot.CurrentFormatVersion, "Warm Rhodes",
        [new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common",
            [new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", "Warm Rhodes", null)])],
        SnapshotKinds.Tone, "SN-S");

    [Test]
    public void A_snapshot_saved_without_metadata_reads_as_empty_rather_than_null()
    {
        // Every snapshot the application writes carries all five, but a file can be written by hand and
        // the library reads whatever is in the folder. Absent means empty, never null: the alternative is
        // a null check at every reader, and the one that gets forgotten is a crash while listing a folder.
        var written = Integra7Snapshot.ToJson(Minimal());
        var read = Integra7Snapshot.FromJson(written);

        Assert.That(read.Category, Is.EqualTo(""));
        Assert.That(read.TagList, Is.Not.Null.And.Empty);
        Assert.That(read.Notes, Is.EqualTo(""));
        Assert.That(read.Rating, Is.EqualTo(0), "0 is unrated");
        Assert.That(read.Favourite, Is.False);
    }

    [Test]
    public void Metadata_survives_a_round_trip()
    {
        var written = Minimal() with
        {
            Category = "E.Piano", Tags = ["warm", "trio gig"], Notes = "less bark", Rating = 4,
            Favourite = true,
        };

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(written));

        Assert.That(read.Category, Is.EqualTo("E.Piano"));
        Assert.That(read.Tags, Is.EqualTo(new[] { "warm", "trio gig" }));
        Assert.That(read.Notes, Is.EqualTo("less bark"));
        Assert.That(read.Rating, Is.EqualTo(4));
        Assert.That(read.Favourite, Is.True);
    }

    [Test]
    public void Every_piece_of_metadata_is_written_before_the_parameter_data()
    {
        // So a browse can read a file's head and stop. A drum kit snapshot is 92 blocks of values; with
        // the blocks written first, listing a library would mean parsing all of it, per file, to find a
        // name. This is the property SnapshotHead depends on and nothing else enforces. Task 0's fixture
        // pins that Blocks is last; this pins that each metadata property is before it, so adding a sixth
        // and forgetting its order fails here.
        var json = Integra7Snapshot.ToJson(Minimal() with { Category = "E.Piano", Rating = 5 });

        var blocks = json.IndexOf("\"Blocks\"", System.StringComparison.Ordinal);
        Assert.That(blocks, Is.GreaterThan(0));
        foreach (var property in new[] { "FormatVersion", "Name", "Kind", "ToneType", "Category", "Tags",
                     "Notes", "Rating", "Favourite" })
            Assert.That(json.IndexOf($"\"{property}\"", System.StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(blocks), $"{property} must be written before Blocks");
    }

    /// <summary>The test above passes a snapshot with a category and a rating, so it does not say what
    /// happens to the three that are still at their defaults. All five are written unconditionally, the
    /// way <c>ToneType</c> already is and for the same reason: a file that always carries every property
    /// is one a head reader can treat uniformly, with no "absent means the default" branch per field, and
    /// it is a file a person editing it by hand can see the shape of without knowing the schema.</summary>
    [Test]
    public void Metadata_at_its_defaults_is_still_written()
    {
        var json = Integra7Snapshot.ToJson(Minimal());

        Assert.That(json, Does.Contain("\"Category\": \"\""));
        Assert.That(json, Does.Contain("\"Tags\": []"), "no tags is an empty array, not an absent property");
        Assert.That(json, Does.Contain("\"Notes\": \"\""));
        Assert.That(json, Does.Contain("\"Rating\": 0"));
        Assert.That(json, Does.Contain("\"Favourite\": false"));
    }

    [Test]
    public void A_studio_set_carries_no_category_and_that_is_not_missing_data()
    {
        // A Studio Set is sixteen parts, each with its own category; there is no single tone category to
        // name for the whole thing. So an empty category is the normal, correct state of every Studio Set
        // in the library and must not be refused as a gap the way a missing Name is -- it is filtered by
        // kind, by tags and by rating instead.
        var studioSet = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "World Pop Set",
            [new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
                [new SnapshotValue("Studio Set Common/Studio Set Name", "World Pop Set")])]);

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(studioSet with { Rating = 3 }));

        Assert.That(read.Kind, Is.EqualTo(SnapshotKinds.StudioSet));
        Assert.That(read.Category, Is.EqualTo(""));
        Assert.That(read.Rating, Is.EqualTo(3));
    }

    [Test]
    public void A_rating_outside_zero_to_five_is_refused()
    {
        // The star control cannot produce one, but a hand-edited file can, and a five-star filter that
        // silently missed a seven-star entry would be a puzzle rather than an error.
        var json = Integra7Snapshot.ToJson(Minimal()).Replace("\"Rating\": 0", "\"Rating\": 7");
        Assert.That(() => Integra7Snapshot.FromJson(json), Throws.TypeOf<SnapshotFormatException>());
    }

    [Test]
    public void A_negative_rating_is_refused_too()
    {
        // The other end of the same range. 0 already means unrated, so there is nothing below it for a
        // negative number to mean, and a minimum-rating filter would admit it at every setting.
        var json = Integra7Snapshot.ToJson(Minimal()).Replace("\"Rating\": 0", "\"Rating\": -1");

        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson(json));
        Assert.That(e!.Message, Does.Contain("-1"), "the message has to name the rating it found");
    }

    /// <summary>A hand-edited file can null out a field that has no business being null. For the two free
    /// text fields that is read as "nothing said", exactly as an absent property is -- which is the
    /// opposite of what the converter does with <c>Kind</c>, deliberately: a null Kind decides where a
    /// file's blocks get applied and so is kept and refused by name, while a null note decides nothing at
    /// all. Refusing to open an otherwise perfect snapshot over a null annotation would be the wrong
    /// trade.</summary>
    [Test]
    public void A_category_or_note_written_as_null_reads_as_empty()
    {
        var json = Integra7Snapshot.ToJson(Minimal())
            .Replace("\"Category\": \"\"", "\"Category\": null")
            .Replace("\"Notes\": \"\"", "\"Notes\": null");

        var read = Integra7Snapshot.FromJson(json);

        Assert.That(read.Category, Is.EqualTo(""));
        Assert.That(read.Notes, Is.EqualTo(""));
    }

    [Test]
    public void A_tag_that_is_not_text_is_refused()
    {
        // TagList promises no reader ever sees a null tag list; a list *containing* a null would satisfy
        // that promise to the letter and still crash the first filter that lowercases a tag. There is
        // nothing to coalesce a null element to either -- an empty tag is not a tag, and dropping it
        // silently would lose something the file said -- so it is refused.
        var json = Integra7Snapshot.ToJson(Minimal()).Replace("\"Tags\": []", "\"Tags\": [\"warm\", null]");

        Assert.That(() => Integra7Snapshot.FromJson(json), Throws.TypeOf<SnapshotFormatException>());
    }

    [Test]
    public void Refuses_to_write_a_tag_that_is_not_text()
    {
        // The same case from the other side. A file this build writes and then refuses to read is the
        // worst thing the writer could produce, so it does not produce one -- the same rule the nesting
        // already follows for two parameter paths that would collide.
        var nullTag = Minimal() with { Tags = ["warm", null!] };

        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.ToJson(nullTag));
        Assert.That(e!.Message, Does.Contain("tag"));
    }
}
