using System.IO;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Naming a stored value with the table this build has, rather than with the one the build that
/// captured it had.
///
/// Run against the real parameter database, because the whole point is that the database is the authority:
/// a table this build gained since a snapshot was written is exactly the case that must resolve, and an
/// invented fixture could not tell that from a table that never existed.</summary>
public class SnapshotValueNamesTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    /// <summary>The reported bug. Every snapshot in the user's library predates the tone-category table on
    /// the SuperNATURAL engines and therefore stores "36" as its own display string.</summary>
    [Test]
    public void A_category_captured_before_this_build_had_the_table_is_named_now()
    {
        Assert.That(
            SnapshotValueNames.Best(_parameters, "SuperNATURAL Synth Tone Common/Tone Category", 36, "36"),
            Is.EqualTo("Synth Pad/Strings"));
    }

    [Test]
    public void A_repr_backed_value_is_named_from_the_repr()
    {
        Assert.That(
            SnapshotValueNames.Best(_parameters, "SuperNATURAL Synth Tone Partial/OSC Wave", 6, "6"),
            Is.EqualTo("SuperSaw"));
    }

    [Test]
    public void A_discrete_value_is_named_from_the_discrete_list()
    {
        Assert.That(
            SnapshotValueNames.Best(_parameters, "SuperNATURAL Acoustic Tone Common/Instrument", 0x4000,
                "16384"),
            Is.EqualTo("INT 001: Concert Grand"));
    }

    /// <summary>A parameter this build has dropped or renamed. The file is still the authority on what it
    /// held, so its own string is the best answer available -- and asking the database through Lookup
    /// would assert rather than answer, which is why Best uses LookupIndex.</summary>
    [Test]
    public void A_path_this_build_does_not_have_keeps_the_stored_string()
    {
        Assert.That(_parameters.LookupIndex("Made Up Block/Made Up Parameter"), Is.EqualTo(-1),
            "the fixture assumes this path is absent");
        Assert.That(
            SnapshotValueNames.Best(_parameters, "Made Up Block/Made Up Parameter", 3, "Three"),
            Is.EqualTo("Three"));
    }

    /// <summary>A text parameter -- a tone name -- carries no raw value: its value <i>is</i> its string,
    /// and there is nothing to look up.</summary>
    [Test]
    public void A_value_with_no_raw_keeps_the_stored_string()
    {
        Assert.That(
            SnapshotValueNames.Best(_parameters, "SuperNATURAL Synth Tone Common/Tone Name",
                null, "Warm Rhodes"),
            Is.EqualTo("Warm Rhodes"));
    }

    /// <summary>A snapshot from a build whose table was shorter, or a value the instrument reports outside
    /// the documented range. Nothing to name it with, so the stored string stands.</summary>
    [Test]
    public void A_raw_outside_the_repr_keeps_the_stored_string()
    {
        Assert.That(
            SnapshotValueNames.Best(_parameters, "SuperNATURAL Synth Tone Partial/OSC Wave", 99, "99"),
            Is.EqualTo("99"));
    }

    /// <summary>The guard that makes this safe. An MFX-style parameter is stored offset -- raw 32768 is
    /// the displayed 0 -- and its repr is keyed by the displayed number, so reading the repr at the raw
    /// would answer confidently and wrongly. Raw 0 is a key this repr has ("Off"), which is exactly why
    /// the case is worth pinning: without the identity check the answer would be "Off" for a value that
    /// is nothing of the sort.</summary>
    [Test]
    public void A_value_whose_display_is_not_its_raw_keeps_the_stored_string()
    {
        const string path = "Studio Set Common Chorus/Chorus Parameter 1/Chorus Filter Type";
        Assert.That(_parameters.LookupIndex(path), Is.Not.EqualTo(-1),
            "the fixture assumes this build still has this parameter");

        Assert.That(SnapshotValueNames.Best(_parameters, path, 0, "High Pass Filter"),
            Is.EqualTo("High Pass Filter"));
    }
}
