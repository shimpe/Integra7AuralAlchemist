using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure logic for the SN-S partial Solo/Mute audition overlay. Computes the effective on/off of each
/// partial from the saved (real) switch states plus the transient solo/mute sets. Solo isolates: if
/// any partial is soloed, only soloed partials sound (overriding mute and saved-off); with no solo,
/// mutes silence saved-on partials. No UI or hardware dependency.
/// </summary>
public static class PartialAudition
{
    /// <summary>Effective on/off per partial. All three lists must be the same length as <paramref name="saved"/>.</summary>
    public static bool[] Effective(IReadOnlyList<bool> saved, IReadOnlyList<bool> solo, IReadOnlyList<bool> mute)
    {
        var anySolo = solo.Any(s => s);
        var result = new bool[saved.Count];
        for (var i = 0; i < saved.Count; i++)
            result[i] = anySolo ? solo[i] : (saved[i] && !mute[i]);
        return result;
    }

    /// <summary>True when any solo or any mute is engaged (an audition is active).</summary>
    public static bool IsAuditioning(IReadOnlyList<bool> solo, IReadOnlyList<bool> mute)
        => solo.Any(s => s) || mute.Any(m => m);
}
