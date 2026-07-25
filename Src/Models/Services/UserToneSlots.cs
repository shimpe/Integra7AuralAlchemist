using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Which user memory slot a preset occupies on the instrument.
///
/// The INTEGRA-7 numbers its user tones per engine, from zero, in the order the app's preset list
/// already carries them (ascending <see cref="Integra7Preset.Id"/>). Storing a tone addresses that
/// number directly, so it has to be derived from the *whole* preset list and nothing else.
///
/// The reason this is a separate function rather than an ordinal read off whatever list is at hand:
/// every list the UI shows is a filtered, re-sorted projection -- the save dialog filters by tone
/// type and by the search box, the part grids filter by search text and by which SRX banks are
/// loaded. A row index in any of those is an index into a subset, and the subset happens to agree
/// with the slot numbering only while the search box is empty. Type one character and it no longer
/// does. Get this number wrong and the save overwrites a different saved sound -- there is no undo
/// on the instrument.
/// </summary>
public static class UserToneSlots
{
    /// <summary>The zero-based user slot <paramref name="preset" /> occupies among the user tones of
    /// its kind, or -1 when it is not a user tone of that kind.</summary>
    /// <param name="allPresets">The complete, unfiltered preset list (PartViewModel.AllPresets).
    /// Order does not matter; slots are counted by ascending Id.</param>
    /// <param name="toneType">The engine the tone belongs to: "PCMS", "PCMD", "SN-S", "SN-A", "SN-D".</param>
    /// <param name="preset">The preset whose slot is wanted; matched on <see cref="Integra7Preset.Id" />,
    /// which is unique across the list.</param>
    public static int ZeroBasedSlotOf(IReadOnlyList<Integra7Preset> allPresets, string toneType,
        Integra7Preset? preset)
    {
        if (allPresets is null || preset is null) return -1;
        if (preset.ToneTypeStr != toneType || preset.InternalUserDefinedStr != "USR") return -1;

        var slot = 0;
        foreach (var p in allPresets
                     .Where(p => p.ToneTypeStr == toneType && p.InternalUserDefinedStr == "USR")
                     .OrderBy(p => p.Id))
        {
            if (p.Id == preset.Id) return slot;
            slot++;
        }

        return -1;
    }
}
