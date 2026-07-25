using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// The Setup parameters that say which Studio Set the INTEGRA-7 currently holds: a bank select pair
/// and a program change. Two consequences follow from that, and both are read off this one list:
///
/// - The app never offers an editor for them (see <see cref="ReadOnlyParameters"/>). Writing one
///   loads a different Studio Set on the device without anything here noticing.
/// - When the device reports one changing, the whole Studio Set changed underneath us — every part's
///   tone, the common blocks, all of it — so the app resyncs rather than updating that one value.
///
/// Pure (no Avalonia) so it is unit-testable.
/// </summary>
public static class StudioSetSelectors
{
    private static readonly HashSet<string> Paths = new(StringComparer.Ordinal)
    {
        "Setup/Studio Set BS MSB",
        "Setup/Studio Set BS LSB",
        "Setup/Studio Set PC",
    };

    public static bool Contains(string path) => Paths.Contains(path);
}
