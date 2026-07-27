using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Every parameter block that makes up a single tone -- the patch loaded into one part -- as the three
/// address names that resolve one back to a live domain via <c>Integra7Domain.GetDomain</c>. Plain
/// strings, so the composition of a tone is testable without a device. The sibling of
/// <see cref="StudioSetDomainNames"/>.
///
/// The order is the order a snapshot is captured and restored in: the common blocks first, then the
/// partials ascending. Within a block the parameter order comes from the block itself (address order).
/// </summary>
public static class ToneDomainNames
{
    /// <summary>Start encodes which part the tone currently sits in -- nothing about the engine or the
    /// tone's identity, both of which live in Offset/Offset2. That separation is deliberate: a later
    /// task re-targets a captured tone by rewriting only this string, so a tone captured from one part
    /// can be loaded into a different part without touching any Offset2 name.</summary>
    private static string Start(int zeroBasedPartNo) => $"Temporary Tone Part {zeroBasedPartNo + 1}";

    /// <summary>The blocks that make up a tone of the given type, in capture/restore order. Throws for
    /// an unrecognised tone type: the caller has no sensible fallback, and silently returning an empty
    /// list would produce a snapshot with nothing in it.</summary>
    public static IReadOnlyList<(string Start, string Offset, string Offset2)> For(string toneType,
        int zeroBasedPartNo)
    {
        var start = Start(zeroBasedPartNo);
        return toneType switch
        {
            "PCMS" => PcmSynthTone(start),
            "PCMD" => PcmDrumKit(start),
            "SN-S" => SnSynthTone(start),
            "SN-A" => SnAcousticTone(start),
            "SN-D" => SnDrumKit(start),
            _ => throw new ArgumentException($"Unrecognised tone type '{toneType}'.", nameof(toneType)),
        };
    }

    /// <summary>So a caller can check before offering the snapshot action, rather than catching the
    /// exception <see cref="For"/> throws for anything else.</summary>
    public static bool IsKnownToneType(string toneType) =>
        toneType is "PCMS" or "PCMD" or "SN-S" or "SN-A" or "SN-D";

    /// <summary>Whether this engine's tone is a kit of independently edited notes rather than one
    /// patch. Randomise treats the two differently: a kit is randomised one note at a time, because
    /// "every note in the kit at once" is 88 partials and an undo step nobody can use.</summary>
    public static bool IsDrumKit(string toneType) => toneType is "PCMD" or "SN-D";

    /// <summary>The single partial block holding one drum note. <paramref name="zeroBasedNoteIndex"/> is
    /// the drum editor's own note index (0..61 for SN-D, 0..87 for PCMD), which is one less than the
    /// partial number in the address.</summary>
    public static (string Start, string Offset, string Offset2) DrumPartialFor(string toneType,
        int zeroBasedPartNo, int zeroBasedNoteIndex)
    {
        var (offset, block) = toneType switch
        {
            "SN-D" => ("Offset/Temporary SuperNATURAL Drum Kit", "SuperNATURAL Drum Kit Partial"),
            "PCMD" => ("Offset/Temporary PCM Drum Kit", "PCM Drum Kit Partial"),
            _ => throw new ArgumentException($"'{toneType}' is not a drum kit.", nameof(toneType)),
        };

        return (Start(zeroBasedPartNo), offset, $"Offset2/{block} {zeroBasedNoteIndex + 1}");
    }

    private static List<(string, string, string)> PcmSynthTone(string start)
    {
        const string offset = "Offset/Temporary PCM Synth Tone";
        List<(string, string, string)> names =
        [
            (start, offset, "Offset2/PCM Synth Tone Common"),
            (start, offset, "Offset2/PCM Synth Tone Common 2"),
            (start, offset, "Offset2/PCM Synth Tone Common MFX"),
            (start, offset, "Offset2/PCM Synth Tone Partial Mix Table"),
        ];

        for (var partial = 1; partial <= Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; partial++)
            names.Add((start, offset, $"Offset2/PCM Synth Tone Partial {partial}"));

        return names;
    }

    private static List<(string, string, string)> PcmDrumKit(string start)
    {
        const string offset = "Offset/Temporary PCM Drum Kit";
        List<(string, string, string)> names =
        [
            (start, offset, "Offset2/PCM Drum Kit Common"),
            (start, offset, "Offset2/PCM Drum Kit Common 2"),
            (start, offset, "Offset2/PCM Drum Kit Common MFX"),
            (start, offset, "Offset2/PCM Drum Kit Common Comp-EQ"),
        ];

        for (var partial = 1; partial <= Constants.NO_OF_PARTIALS_PCM_DRUM; partial++)
            names.Add((start, offset, $"Offset2/PCM Drum Kit Partial {partial}"));

        return names;
    }

    private static List<(string, string, string)> SnSynthTone(string start)
    {
        const string offset = "Offset/Temporary SuperNATURAL Synth Tone";
        List<(string, string, string)> names =
        [
            (start, offset, "Offset2/SuperNATURAL Synth Tone Common"),
            (start, offset, "Offset2/SuperNATURAL Synth Tone Common MFX"),
        ];

        for (var partial = 1; partial <= Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; partial++)
            names.Add((start, offset, $"Offset2/SuperNATURAL Synth Tone Partial {partial}"));

        return names;
    }

    private static List<(string, string, string)> SnAcousticTone(string start)
    {
        const string offset = "Offset/Temporary SuperNATURAL Acoustic Tone";
        return
        [
            (start, offset, "Offset2/SuperNATURAL Acoustic Tone Common"),
            (start, offset, "Offset2/SuperNATURAL Acoustic Tone Common MFX"),
        ];
    }

    private static List<(string, string, string)> SnDrumKit(string start)
    {
        const string offset = "Offset/Temporary SuperNATURAL Drum Kit";
        List<(string, string, string)> names =
        [
            (start, offset, "Offset2/SuperNATURAL Drum Kit Common"),
            (start, offset, "Offset2/SuperNATURAL Drum Kit Common MFX"),
            (start, offset, "Offset2/SuperNATURAL Drum Kit Common Comp-EQ"),
        ];

        for (var partial = 1; partial <= Constants.NO_OF_PARTIALS_SN_DRUM; partial++)
            names.Add((start, offset, $"Offset2/SuperNATURAL Drum Kit Partial {partial}"));

        return names;
    }
}
