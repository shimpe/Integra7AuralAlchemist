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
    public const int PartCount = 16;
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

        for (var part = 1; part <= PartCount; part++)
        {
            names.Add((Start, Offset, $"Offset2/Studio Set Part {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set Part EQ {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set MIDI Channel {part}"));
        }

        return names;
    }
}
