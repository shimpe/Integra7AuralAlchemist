using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Every parameter block that makes up a Studio Set, as the three address names that resolve one
/// back to a live domain via <c>Integra7Domain.GetDomain</c>. Plain strings, so the composition of a
/// Studio Set is testable without a device.
///
/// The order is the order a snapshot is captured and restored in: the common blocks first, then each
/// part. Within a block the parameter order comes from the block itself (address order).
/// </summary>
public static class StudioSetDomainNames
{
    private const string Start = "Temporary Studio Set";
    private const string Offset = "Offset/Not Used";

    public static IReadOnlyList<(string Start, string Offset, string Offset2)> All { get; } = Build();

    private static List<(string, string, string)> Build()
    {
        List<(string, string, string)> names =
        [
            (Start, Offset, "Offset2/Studio Set Common"),
            (Start, Offset, "Offset2/Studio Set Common Chorus"),
            (Start, Offset, "Offset2/Studio Set Common Reverb"),
            (Start, Offset, "Offset2/Studio Set Common Motional Surround"),
            (Start, Offset, "Offset2/Studio Set Master EQ"),
        ];

        // Constants.NO_OF_PARTS (a byte) is what Integra7Domain itself builds these very part domains
        // from -- referencing it here, rather than a second "16", keeps the two from drifting apart.
        for (var part = 1; part <= Constants.NO_OF_PARTS; part++)
        {
            names.Add((Start, Offset, $"Offset2/Studio Set Part {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set Part EQ {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set MIDI Channel {part}"));
        }

        return names;
    }
}
