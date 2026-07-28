using System;
using System.Globalization;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Whether a partial is switched on, asked of the block that partial's parameters live in.
///
/// <b>Why this is not simply a parameter of the partial's own block.</b> A partial's on/off switch is not
/// stored with the partial: the instrument keeps all of a tone's partial switches together, in the block
/// that decides how the partials are combined. So a comparison grouped by block -- which is how a
/// comparison of two tones is readable at all -- reports twenty-three differences under "SuperNATURAL
/// Synth Tone Partial 2" and reports, in a different section entirely, that partial 2 is off on one side.
/// Both statements are correct and the second is what makes the first irrelevant, which is exactly the
/// kind of thing a reader should not have to assemble themselves. This is the lookup that lets a caller
/// put them back together.
///
/// <b>Why the two engines are spelled differently.</b> They are the instrument's own names, taken from its
/// parameter database verbatim rather than normalised here: the SuperNATURAL Synth's switch is
/// "Partial2 Switch" with no space before the digit, and the PCM Synth's is "PMT 2 Partial Switch" with
/// one -- "PMT" being the Partial Mix Table the switch sits in. Inventing a consistent spelling would mean
/// this file no longer matches anything a user can look up in the manual, and would break silently the
/// first time either name is needed for anything else.
///
/// A drum kit has no such switch at all -- every note in a kit exists -- so a drum partial has no answer
/// rather than a false one.
///
/// Pure: a snapshot in, a yes/no/unknown out. No parameter database, no domain, no device.</summary>
public static class PartialSwitches
{
    /// <summary>Whether the partial whose block this is was switched on in this snapshot. Null when the
    /// block is not a partial, when the engine has no such switch, or when the snapshot does not carry
    /// it.</summary>
    public static bool? IsOn(Integra7Snapshot snapshot, string offset2)
    {
        if (SwitchFor(offset2) is not { } governing) return null;

        var stored = snapshot.Domains
            .FirstOrDefault(d => d.Offset2 == governing.Block)?.Values
            .FirstOrDefault(v => v.Path == governing.Path);
        if (stored is null) return null;

        // The raw is the value the instrument holds and the string is only how some build rendered it, so
        // the raw decides wherever there is one. Off is exactly 0; anything else reads as on, which is the
        // direction that stays quiet -- a hand-edited file holding something that is not a value of this
        // parameter should not have a partial declared silent on the strength of it.
        // A SnapshotValue's raw is optional by design, so its absence is not a damaged file and the string
        // is not a fallback there but the only thing said.
        return stored.Raw is { } raw
            ? raw != 0
            : stored.Value.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The block and path holding the switch that governs this partial's block, or null when
    /// nothing does.</summary>
    private static (string Block, string Path)? SwitchFor(string offset2)
    {
        if (PartialNumberIn(offset2, "Offset2/SuperNATURAL Synth Tone Partial ") is { } sn)
            return ("Offset2/SuperNATURAL Synth Tone Common",
                $"SuperNATURAL Synth Tone Common/Partial{sn} Switch");

        if (PartialNumberIn(offset2, "Offset2/PCM Synth Tone Partial ") is { } pcm)
            return ("Offset2/PCM Synth Tone Partial Mix Table",
                $"PCM Synth Tone Partial Mix Table/PMT {pcm} Partial Switch");

        return null;
    }

    /// <summary>The number in "...Partial 2", or null when what follows the prefix is not one.
    ///
    /// The number is what makes this a test rather than a prefix match, and that matters for one block in
    /// particular: "Offset2/PCM Synth Tone Partial Mix Table" is the block the PCM switches live in and
    /// shares its entire prefix with the blocks it governs. <see cref="NumberStyles.None"/> so that
    /// neither surrounding space nor a sign is accepted -- only a partial number spelt the way the address
    /// spells it, which is what keeps a name this does not know from resolving to a switch.</summary>
    private static int? PartialNumberIn(string offset2, string prefix) =>
        offset2.StartsWith(prefix, StringComparison.Ordinal) &&
        int.TryParse(offset2[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
}
