using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Searching inside a patch. The file already stores what each parameter reads as on screen, so
/// this matches against that and never consults the parameter database.</summary>
public class SnapshotTextScanTests
{
    /// <summary>A snapshot with three blocks: one text parameter, several with raw values, and one
    /// parameter nested a level deeper than the rest. Written as JSON rather than built through the model,
    /// because what is being tested is a reader -- and shaped after a real library file rather than after
    /// the simplest case, because the deeper nesting is in every one of them.</summary>
    private const string Json = """
    {
      "FormatVersion": 3, "Name": "Warm Rhodes", "Kind": "tone", "ToneType": "SN-S",
      "Category": "E.Piano", "Tags": [], "Notes": "", "Rating": 0, "Favourite": false,
      "Blocks": {
        "Temporary Tone Part 1": {
          "Offset/Temporary SuperNATURAL Synth Tone": {
            "Offset2/SuperNATURAL Synth Tone Common": {
              "SuperNATURAL Synth Tone Common": {
                "Tone Name": "Warm Rhodes",
                "Tone Level": [127, "127"]
              }
            },
            "Offset2/SuperNATURAL Synth Tone Partial 1": {
              "SuperNATURAL Synth Tone Partial": {
                "OSC Wave": [6, "SuperSaw"]
              }
            },
            "Offset2/SuperNATURAL Synth Tone Common MFX": {
              "SuperNATURAL Synth Tone Common MFX": {
                "MFX Type": [1, "Equalizer"],
                "MFX Parameter 1": {
                  "Modulation Delay Left (ms-note)": [32769, "Note"]
                },
                "MFX Chorus Send Level": [42, "forty-two"]
              }
            }
          }
        }
      }
    }
    """;

    private static Stream Of(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Test]
    public void A_displayed_value_is_found_and_the_parameter_is_named()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "supersaw");

        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Path, Is.EqualTo("SuperNATURAL Synth Tone Partial/OSC Wave"));
        Assert.That(hit.Value.Value, Is.EqualTo("SuperSaw"));
    }

    /// <summary>Ordinal, ignoring case -- LibraryFilter's rule, so that the same library searches the same
    /// way on every machine.</summary>
    [Test]
    public void Matching_ignores_case()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "SUPERSAW"), Is.Not.Null);
    }

    /// <summary>A text parameter has no raw half and is stored as a bare string. It is still a value the
    /// user can see, so it is still searchable.</summary>
    [Test]
    public void A_text_parameter_is_searched_too()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "rhodes");

        Assert.That(hit!.Value.Path, Is.EqualTo("SuperNATURAL Synth Tone Common/Tone Name"));
    }

    /// <summary>A parameter path holds two slashes as often as one: the writer nests a block's values by
    /// the path's own '/', so every MFX parameter sits a level below the block's plain ones. Around thirty
    /// of a tone's parameters are down there -- all of its effect settings -- and a walk that looked at one
    /// fixed depth would search a patch's effects not at all.</summary>
    [Test]
    public void A_parameter_nested_deeper_than_the_rest_is_found_and_named_in_full()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "note");

        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Path, Is.EqualTo(
            "SuperNATURAL Synth Tone Common MFX/MFX Parameter 1/Modulation Delay Left (ms-note)"));
        Assert.That(hit.Value.Value, Is.EqualTo("Note"));
    }

    /// <summary>And the walk comes back up again. A parameter written after a nested one, at the level the
    /// nested one left, is the case that a reader tracking depth gets wrong by leaving a stale name behind
    /// or by never returning to the shallower level at all.</summary>
    [Test]
    public void A_parameter_after_a_nested_one_is_named_at_its_own_level()
    {
        var hit = SnapshotTextScan.FirstMatch(Of(Json), "forty-two");

        Assert.That(hit!.Value.Path,
            Is.EqualTo("SuperNATURAL Synth Tone Common MFX/MFX Chorus Send Level"));
    }

    /// <summary>Values are searched; the keys around them are not. A hit on a block or container name would
    /// report as a value something the user never sees as one.</summary>
    [Test]
    public void A_parameters_own_name_is_not_one_of_the_values()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "MFX Parameter 1"), Is.Null);
    }

    [Test]
    public void Nothing_matching_answers_null()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "trumpet"), Is.Null);
    }

    /// <summary>The metadata is the list's business, not this reader's: searching it is what LibraryFilter
    /// already does over the heads, and matching it here as well would make the same entry hit twice and
    /// report a parameter that does not exist.</summary>
    [Test]
    public void The_name_and_the_category_outside_Blocks_are_not_searched()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of(Json), "E.Piano"), Is.Null,
            "the category is metadata, matched by LibraryFilter over the head");
    }

    /// <summary>A file that is not a snapshot, or not JSON, is passed over rather than throwing -- the same
    /// contract SnapshotHead has, and for the same reason: a library folder is a folder.</summary>
    [Test]
    public void Something_that_is_not_json_is_not_a_match()
    {
        Assert.That(SnapshotTextScan.FirstMatch(Of("this is not JSON"), "anything"), Is.Null);
    }

    /// <summary>An editor that re-saved a snapshot may have added a byte order mark. Utf8JsonReader does
    /// not skip one.</summary>
    [Test]
    public void A_byte_order_mark_does_not_hide_a_match()
    {
        var marked = new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Json)]);

        Assert.That(SnapshotTextScan.FirstMatch(marked, "supersaw"), Is.Not.Null);
    }
}
