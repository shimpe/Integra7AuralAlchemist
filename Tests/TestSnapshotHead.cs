using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Reading a snapshot file's metadata without reading its parameter data.
///
/// Two properties are being pinned here, and they pull in opposite directions. One is that the parameter
/// data is genuinely never interpreted, which is what makes listing a folder of drum kits affordable. The
/// other is that this is not a second place where a file is judged: a file that <c>FromJson</c> will refuse
/// still has to appear in the list, so that the user can see it and be told why, rather than quietly not
/// being there.</summary>
public class SnapshotHeadTests
{
    private static SnapshotHead? Read(string json) =>
        SnapshotHead.TryRead(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    /// <summary>A tone snapshot with every piece of metadata set, written by the real writer rather than by
    /// hand -- so that this fixture fails if the file's property names or shape ever move away from what the
    /// head reader looks for. That coupling is the point: the two are only correct together.</summary>
    private static Integra7Snapshot Annotated() => new(
        Integra7Snapshot.CurrentFormatVersion, "Warm Rhodes",
        [
            new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                "Offset2/SuperNATURAL Synth Tone Common",
                [
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", "Warm Rhodes"),
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
                ]),
        ],
        SnapshotKinds.Tone, "SN-S", "E.Piano", ["warm", "trio gig"], "less bark", 4, true);

    [Test]
    public void Reads_everything_a_list_needs_from_a_real_snapshot_file()
    {
        var head = Read(Integra7Snapshot.ToJson(Annotated()));

        Assert.That(head, Is.Not.Null);
        Assert.That(head!.Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(head.Kind, Is.EqualTo(SnapshotKinds.Tone));
        Assert.That(head.ToneType, Is.EqualTo("SN-S"));
        Assert.That(head.Category, Is.EqualTo("E.Piano"));
        Assert.That(head.Tags, Is.EqualTo(new[] { "warm", "trio gig" }));
        Assert.That(head.Notes, Is.EqualTo("less bark"));
        Assert.That(head.Rating, Is.EqualTo(4));
        Assert.That(head.Favourite, Is.True);
    }

    /// <summary>Every snapshot saved before the library existed -- which is every one of them, on the one
    /// machine that has any -- carries none of the five metadata properties. They have to read as "nothing
    /// said" rather than as an unreadable file, because those are exactly the files the first library listing
    /// will be made of.</summary>
    [Test]
    public void A_file_written_before_the_metadata_existed_reads_as_nothing_said()
    {
        var head = Read("""
            {
              "FormatVersion": 3,
              "Name": "World Pop Set",
              "Kind": "studio-set",
              "ToneType": null,
              "Blocks": {
                "Temporary Studio Set": {
                  "Offset/Not Used": {
                    "Offset2/Studio Set Common": {
                      "Studio Set Common": {
                        "Studio Set Name": "World Pop Set",
                        "Studio Set Tempo": [120, "120"]
                      }
                    }
                  }
                }
              }
            }
            """);

        Assert.That(head, Is.Not.Null);
        Assert.That(head!.Name, Is.EqualTo("World Pop Set"));
        Assert.That(head.Kind, Is.EqualTo(SnapshotKinds.StudioSet));
        Assert.That(head.ToneType, Is.Null);
        Assert.That(head.Category, Is.EqualTo(""));
        Assert.That(head.Tags, Is.Not.Null.And.Empty);
        Assert.That(head.Notes, Is.EqualTo(""));
        Assert.That(head.Rating, Is.EqualTo(0), "0 is unrated");
        Assert.That(head.Favourite, Is.False);
    }

    /// <summary>The difference between an optimisation and a requirement. The writer puts the metadata
    /// before the parameter data so that this read can be cheap, but that is a convention of ours and not a
    /// rule of JSON: a file edited by hand, or written by some other tool, can put the blocks first. Reading
    /// past them has to work, and this is the test that says the reader steps over the parameter data
    /// wherever it finds it rather than stopping at it.</summary>
    [Test]
    public void Reads_the_metadata_even_when_the_parameter_data_comes_first()
    {
        var head = Read("""
            {
              "Blocks": {
                "Temporary Studio Set": {
                  "Offset/Not Used": {
                    "Offset2/Studio Set Common": {
                      "Studio Set Common": {
                        "Studio Set Name": "World Pop Set",
                        "Studio Set Tempo": [120, "120"]
                      }
                    }
                  }
                }
              },
              "FormatVersion": 3,
              "Name": "World Pop Set",
              "Kind": "studio-set",
              "Category": "",
              "Tags": ["for the trio gig"],
              "Notes": "second half only",
              "Rating": 5,
              "Favourite": true
            }
            """);

        Assert.That(head, Is.Not.Null);
        Assert.That(head!.Name, Is.EqualTo("World Pop Set"));
        Assert.That(head.Tags, Is.EqualTo(new[] { "for the trio gig" }));
        Assert.That(head.Notes, Is.EqualTo("second half only"));
        Assert.That(head.Rating, Is.EqualTo(5));
        Assert.That(head.Favourite, Is.True);
    }

    /// <summary>The claim this whole type is built on, tested from the only angle that can actually prove it:
    /// a file whose parameter data is a shape the converter refuses. The head reads fine and opening the file
    /// fails, which is only possible if the head reader never looked inside the blocks. Asserting on timing
    /// or on allocations would be a much weaker statement about the same thing.</summary>
    [Test]
    public void The_parameter_data_is_never_interpreted()
    {
        // "Studio Set Tempo": 120 -- a bare number, which is neither a text parameter's bare string nor a
        // [raw, "display"] pair, and which the converter refuses by name.
        const string json = """
            {
              "FormatVersion": 3,
              "Name": "World Pop Set",
              "Kind": "studio-set",
              "Category": "",
              "Tags": [],
              "Notes": "",
              "Rating": 2,
              "Favourite": false,
              "Blocks": {
                "Temporary Studio Set": {
                  "Offset/Not Used": {
                    "Offset2/Studio Set Common": {
                      "Studio Set Common": { "Studio Set Tempo": 120 }
                    }
                  }
                }
              }
            }
            """;

        var head = Read(json);

        Assert.That(head, Is.Not.Null, "the head is readable even though the parameter data is not");
        Assert.That(head!.Rating, Is.EqualTo(2));
        Assert.That(() => Integra7Snapshot.FromJson(json), Throws.TypeOf<SnapshotFormatException>(),
            "and the file really is one that cannot be opened");
    }

    /// <summary>The rule stated as plainly as it can be: listing is not judging. A hand-edited seven-star
    /// entry is in the folder, says it is the best thing there, and has to be visible -- it fails when it is
    /// opened, with the message FromJson gives, which is one place to be wrong instead of two.</summary>
    [Test]
    public void A_rating_the_snapshot_reader_would_refuse_still_appears_in_the_list()
    {
        var json = Integra7Snapshot.ToJson(Annotated()).Replace("\"Rating\": 4", "\"Rating\": 7");

        Assert.That(Read(json)!.Rating, Is.EqualTo(7), "read as it stands, in range or not");
        Assert.That(() => Integra7Snapshot.FromJson(json), Throws.TypeOf<SnapshotFormatException>());
    }

    /// <summary>Same rule, applied to a field whose shape is wrong rather than whose value is out of range.
    /// Nothing said for that one field, and the rest of the entry survives -- because the alternative is an
    /// entry that vanishes over a property a list does not even show.</summary>
    [Test]
    public void A_property_of_the_wrong_shape_costs_that_field_and_not_the_entry()
    {
        var head = Read("""
            {
              "FormatVersion": 3,
              "Name": "Warm Rhodes",
              "Kind": "tone",
              "ToneType": "SN-S",
              "Rating": "very good indeed",
              "Tags": { "warm": true },
              "Favourite": "yes",
              "Notes": "still here",
              "Blocks": {}
            }
            """);

        Assert.That(head, Is.Not.Null);
        Assert.That(head!.Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(head.Notes, Is.EqualTo("still here"), "reading past a bad field must not lose the good ones");
        Assert.That(head.Rating, Is.EqualTo(0));
        Assert.That(head.Tags, Is.Empty);
        Assert.That(head.Favourite, Is.False);
    }

    [Test]
    public void A_tag_that_is_not_text_is_dropped_and_the_rest_of_the_tags_survive()
    {
        var head = Read("""
            {
              "FormatVersion": 3, "Name": "Warm Rhodes", "Kind": "tone", "ToneType": "SN-S",
              "Tags": ["warm", 7, "trio gig"], "Blocks": {}
            }
            """);

        Assert.That(head!.Tags, Is.EqualTo(new[] { "warm", "trio gig" }));
    }

    /// <summary>A file that says nothing about its kind is a Studio Set. That default is
    /// <c>Integra7Snapshot</c>'s and load-bearing there, and it has to be the same here: a head that
    /// defaulted the other way would file the entry under one kind and then open it as the other.</summary>
    [Test]
    public void A_file_that_names_no_kind_lists_as_a_studio_set()
    {
        var head = Read("""{ "FormatVersion": 3, "Name": "World Pop Set", "Blocks": {} }""");

        Assert.That(head!.Kind, Is.EqualTo(SnapshotKinds.StudioSet));
    }

    [Test]
    public void A_text_file_is_not_a_snapshot()
    {
        // A library folder is a folder. Whatever else is in it gets skipped, not thrown over.
        Assert.That(Read("These are my notes about the gig on Friday."), Is.Null);
    }

    [Test]
    public void An_empty_file_is_not_a_snapshot()
    {
        Assert.That(Read(""), Is.Null);
    }

    [Test]
    public void A_truncated_snapshot_is_not_a_snapshot()
    {
        // Half a file -- a copy interrupted, or a disk that filled up. There is no head to read out of it.
        var json = Integra7Snapshot.ToJson(Annotated());
        Assert.That(Read(json[..(json.Length / 2)]), Is.Null);
    }

    /// <summary>Some other application's JSON, in the same folder. It is skipped rather than listed as a
    /// nameless Studio Set that cannot be opened, and the thing that separates it from a snapshot is the
    /// format version -- the one property every snapshot this application has ever written carries, and the
    /// only identity check available that does not amount to validating the file.</summary>
    [Test]
    public void Json_that_names_no_format_version_is_not_a_snapshot()
    {
        Assert.That(Read("""{ "name": "some other tool's file", "version": "1.4" }"""), Is.Null);
        Assert.That(Read("{}"), Is.Null);
        Assert.That(Read("[1, 2, 3]"), Is.Null, "a snapshot file is a JSON object");
        Assert.That(Read("\"just a string\""), Is.Null);
    }

    /// <summary>A version this build does not read is still a snapshot, and is still listed. Refusing it here
    /// would mean the user's older files silently were not in the library, instead of being there and saying
    /// what is wrong with them when opened -- which is the whole reason the version is checked in exactly one
    /// place.</summary>
    [Test]
    public void A_file_of_an_older_format_version_is_still_listed()
    {
        var json = Integra7Snapshot.ToJson(Annotated()).Replace("\"FormatVersion\": 3", "\"FormatVersion\": 2");

        Assert.That(Read(json)!.Name, Is.EqualTo("Warm Rhodes"));

        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson(json));
        Assert.That(e!.Message, Does.Contain("version 2"), "and opening it names the version it found");
    }
}
