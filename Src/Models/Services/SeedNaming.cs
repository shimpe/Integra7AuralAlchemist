namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a swept snapshot is called, and what it says about the name it was listed under.
///
/// <b>The device is right and the table is a label.</b> A captured tone carries its own name parameter, and
/// where that disagrees with <c>Presets.csv</c> the instrument wins: an audit on 2026-07-30 compared all
/// 5,227 reachable rows against the device and found 102 disagreements, of which 97 were corrections the
/// table needed, 3 are places the table's spelling is the better one, and 2 sit in banks this unit answers
/// nothing for. <c>Ring E.Piano</c> is the sound that comes out; <c>Ring Piano</c> is what the book says. So
/// the sweep writes the first and records the second, because a user searching the library for the name
/// printed in their manual should still find the patch.
///
/// <b>A rule with an input, an output and no device in it</b>, which is why it is here rather than inside
/// the adapter that captures. Nothing in this file opens a port or a file; the sweep hands it what it read
/// and what it planned, and takes back the annotations to write.
///
/// <b>Not the file name.</b> That was decided before the capture -- see <see cref="SeedPlan.FileNameFor"/>
/// -- because it is what the resume compares against the folder, and a name only knowable after a capture
/// cannot decide whether to capture. The library has always treated what a snapshot is called and what its
/// file is called as two different things.</summary>
public static class SeedNaming
{
    /// <summary>Where a tone of this engine keeps its own name, or null for an engine with no such block.
    ///
    /// <b>Five strings that must not be written down twice.</b> <c>Integra7Api.ChangePresetNameAsync</c>
    /// writes these same parameters when a user renames a patch, and it asks here for the paths rather than
    /// spelling them out a second time -- the two would otherwise be free to come to disagree about what a
    /// tone's name parameter is called, and the direction that broke would be the one nobody exercises by
    /// hand. What that call still decides for itself is which domain to reach for, because each engine keeps
    /// its name in a different block and there is nothing to share about that.</summary>
    public static string? NameParameterFor(string toneType) => toneType switch
    {
        "PCMD" => "PCM Drum Kit Common/Kit Name",
        "PCMS" => "PCM Synth Tone Common/PCM Synth Tone Name",
        "SN-A" => "SuperNATURAL Acoustic Tone Common/Tone Name",
        "SN-S" => "SuperNATURAL Synth Tone Common/Tone Name",
        "SN-D" => "SuperNATURAL Drum Kit Common/Kit Name",
        _ => null,
    };

    /// <summary>The annotations to write with <paramref name="captured"/>: the plan's own category and tags,
    /// the name the device gave, and a note about the name the table gave when the two differ.
    ///
    /// <b>The plan's metadata is carried, not rebuilt.</b> It comes back as a <c>with</c> on what
    /// <see cref="SeedPlan.Build"/> already decided, so there is exactly one place that chooses a swept
    /// snapshot's category and tags. Rebuilding them from the preset here would be a second opinion about
    /// the same question, and two opinions that agree today are two opinions.
    ///
    /// <b>A name is never left empty.</b> A tone whose name parameter this build cannot find -- an engine
    /// with no name block, a capture that answered without one -- keeps the catalogue's name and carries no
    /// note: there was no disagreement, so saying there was one would be an invention, and an empty name is
    /// the one field the browser cannot show. That is also why the note is decided from the name that won
    /// rather than from whether a device name was read: the two cases where nothing is worth saying -- they
    /// agreed, and there was nothing to compare -- are the same case.
    ///
    /// The instrument pads a name out to the width of its field, so what comes back has trailing spaces on
    /// it. Left in, every single row would differ from the table and every single snapshot would carry a
    /// note saying so.
    ///
    /// <b>And the catalogue side is padded too, for half the rows.</b> A factory row's name comes from
    /// <c>Presets.csv</c> already trimmed, but a user slot's comes from the instrument's own name list and
    /// arrives padded exactly like the captured one -- so trimming only what was captured left every user
    /// slot in the library carrying a note saying the table disagreed with it about trailing spaces. That
    /// is the same failure this trim exists to prevent, arriving from the other side, and on a full sweep
    /// it is up to nine hundred notes that say nothing. Verified against the instrument on 2026-07-30:
    /// five user tones swept, five spurious notes.</summary>
    public static SnapshotMetadata MetadataFor(Integra7Snapshot captured, SeedItem item)
    {
        var listed = item.Preset.Name.TrimEnd();
        var device = ToneNameIn(captured);
        var name = string.IsNullOrWhiteSpace(device) ? listed : device;

        return item.Metadata with
        {
            Name = name,
            Notes = name == listed ? "" : $"Listed as \"{listed}\"",
        };
    }

    /// <summary>The tone's own name as the capture recorded it, or null when this snapshot holds no such
    /// value. Searched across every block rather than assumed to be in the first: the name lives in the
    /// common block, which is first in <see cref="ToneDomainNames.For"/> today, and a lookup that depended
    /// on that would be a lookup that breaks when a block is added ahead of it for some unrelated reason.
    /// A name is a text parameter, so it carries no raw form and its value is the string itself.</summary>
    private static string? ToneNameIn(Integra7Snapshot captured)
    {
        if (captured.ToneType is null || NameParameterFor(captured.ToneType) is not { } path) return null;

        foreach (var block in captured.Domains)
        foreach (var value in block.Values)
            if (value.Path == path)
                return value.Value.TrimEnd();

        return null;
    }
}
