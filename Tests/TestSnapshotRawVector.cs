using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The packed raw values a duplicate comparison works on.</summary>
public class SnapshotRawVectorTests
{
    /// <summary>Written out here rather than shared with the text scan's fixture, because the two readers
    /// must be able to disagree about a file and have the tests say so. Shaped after a real library file:
    /// a text parameter, a reserved one, a pair of ordinary ones, and an effect parameter nested a level
    /// deeper with a reserved sibling beside it.</summary>
    private static string Json(string name, long level, long wave, string kind = "tone",
        string toneType = "SN-S") => $$"""
    {
      "FormatVersion": 3, "Name": "{{name}}", "Kind": "{{kind}}", "ToneType": "{{toneType}}",
      "Category": "", "Tags": [], "Notes": "", "Rating": 0, "Favourite": false,
      "Blocks": {
        "Temporary Tone Part 1": {
          "Offset/Temporary SuperNATURAL Synth Tone": {
            "Offset2/SuperNATURAL Synth Tone Common": {
              "SuperNATURAL Synth Tone Common": {
                "Tone Name": "{{name}}",
                "Reserved1": " ",
                "Tone Level": [{{level}}, "{{level}}"],
                "OSC Wave": [{{wave}}, "wave"]
              }
            },
            "Offset2/SuperNATURAL Synth Tone Common MFX": {
              "SuperNATURAL Synth Tone Common MFX": {
                "MFX Parameter 1": {
                  "Modulation Delay Feedback": [32827, "20"],
                  "Modulation Delay Rate Hz (Reserved)": [32786, "18"]
                },
                "Reserved5": [0, "0"]
              }
            }
          }
        }
      }
    }
    """;

    private static Stream Of(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Test]
    public void The_kind_and_the_engine_come_back_with_the_vector()
    {
        var vector = SnapshotRawVector.Read(Of(Json("a", 100, 6)));

        Assert.That(vector, Is.Not.Null);
        Assert.That(vector!.Kind, Is.EqualTo(SnapshotKinds.Tone));
        Assert.That(vector.ToneType, Is.EqualTo("SN-S"));
    }

    /// <summary>Only the raw halves, in document order. A text parameter has no raw half at all, and a
    /// reserved one is excluded for the reason the comparison report excludes it: it is filler. The effect
    /// parameter is in there too, one level below the block's plain ones, which is where every real file
    /// keeps its effects.</summary>
    [Test]
    public void Only_the_raw_values_are_collected_and_reserved_ones_are_left_out()
    {
        var vector = SnapshotRawVector.Read(Of(Json("a", 100, 6)));

        Assert.That(vector!.Values, Is.EqualTo(new long[] { 100, 6, 32827 }));
    }

    /// <summary>The rule is over the whole path, exactly as SnapshotDiff's is, so a container that says
    /// Reserved takes its children with it however innocent their own names are. The two must agree: a
    /// vector counting filler the report ignores would hide the duplicates the report calls identical.
    /// </summary>
    [Test]
    public void A_reserved_container_takes_its_children_with_it()
    {
        const string json = """
        {
          "FormatVersion": 3, "Name": "a", "Kind": "tone", "ToneType": "SN-S",
          "Blocks": { "Temporary Tone Part 1": { "Offset/T": { "Offset2/B": { "B": {
            "Kept": [1, "1"],
            "Parameter 9 (Reserved)": { "Filter Cutoff": [99, "99"] }
          } } } } }
        }
        """;

        Assert.That(SnapshotRawVector.Read(Of(json))!.Values, Is.EqualTo(new long[] { 1 }));
    }

    /// <summary>The property everything downstream rests on: two files of the same engine produce vectors
    /// that line up position by position, so a comparison never has to match paths.</summary>
    [Test]
    public void Two_files_of_the_same_engine_produce_vectors_of_the_same_shape()
    {
        var a = SnapshotRawVector.Read(Of(Json("a", 100, 6)))!;
        var b = SnapshotRawVector.Read(Of(Json("b", 101, 7)))!;

        Assert.That(a.Values, Has.Length.EqualTo(b.Values.Length));
        Assert.That(a.Values, Is.Not.EqualTo(b.Values));
    }

    /// <summary>The name is not in the vector, so renaming a patch does not make it a different sound.
    /// That is the whole point: two files differing only in what has been said about them are duplicates.
    /// </summary>
    [Test]
    public void A_different_name_alone_produces_the_same_vector()
    {
        var a = SnapshotRawVector.Read(Of(Json("Warm Rhodes", 100, 6)))!;
        var b = SnapshotRawVector.Read(Of(Json("Bright Rhodes", 100, 6)))!;

        Assert.That(a.Values, Is.EqualTo(b.Values));
    }

    /// <summary>A Studio Set names no engine, and that null is half of the bucket key -- it is what keeps a
    /// Studio Set from ever being paired with a tone.</summary>
    [Test]
    public void A_studio_set_reads_as_one_and_names_no_engine()
    {
        const string json = """
        {
          "FormatVersion": 3, "Name": "Live Set", "Kind": "studio-set", "ToneType": null,
          "Blocks": { "Temporary Studio Set": { "Offset/Not Used": { "Offset2/Studio Set Common": {
            "Studio Set Common": { "Studio Set Name": "Live Set", "Studio Set Tempo": [120, "120"] }
          } } } }
        }
        """;

        var vector = SnapshotRawVector.Read(Of(json));

        Assert.That(vector!.Kind, Is.EqualTo(SnapshotKinds.StudioSet));
        Assert.That(vector.ToneType, Is.Null);
        Assert.That(vector.Values, Is.EqualTo(new long[] { 120 }));
    }

    [Test]
    public void Something_that_is_not_a_snapshot_answers_null()
    {
        Assert.That(SnapshotRawVector.Read(Of("this is not JSON")), Is.Null);
    }

    /// <summary>JSON, and an object, and still not a snapshot -- SnapshotHead's identity rule, and needed
    /// here more than there: an empty vector equals every other empty vector, so every stray file in the
    /// folder would come back as one large group of duplicates.</summary>
    [Test]
    public void Json_that_carries_no_format_version_is_not_a_snapshot()
    {
        Assert.That(SnapshotRawVector.Read(Of("""{ "Name": "not ours", "Blocks": {} }""")), Is.Null);
    }

    [Test]
    public void A_byte_order_mark_does_not_prevent_a_read()
    {
        var marked = new MemoryStream([.. Encoding.UTF8.GetPreamble(),
            .. Encoding.UTF8.GetBytes(Json("a", 100, 6))]);

        Assert.That(SnapshotRawVector.Read(marked), Is.Not.Null);
    }
}
