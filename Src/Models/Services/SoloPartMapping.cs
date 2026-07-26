using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The one <c>Studio Set Common/Solo Part</c> parameter, as the mixer needs to see it: which strip
/// is soloed, and what to write to solo one or to clear it.
///
/// Solo is a single value on this instrument -- the parameter's options are OFF, 1..16 -- so sixteen strips
/// share it and their buttons behave as a radio group with a second press that turns it off. Keeping the
/// translation here rather than in the view model makes that rule testable without a device, and it is the
/// kind of off-by-one that is invisible until the wrong strip lights up.</summary>
public static class SoloPartMapping
{
    /// <summary>The spelling this build knows for "no part is soloed". Prefer
    /// <see cref="OffValue"/>, which asks the parameter itself.</summary>
    public const string Off = "OFF";

    /// <summary>Which zero-based part <paramref name="value"/> names, or null when no part is soloed.
    /// Anything outside 1..16 -- an unexpected spelling, an empty string, a number this build does not
    /// expect -- reads as "no solo", which is the safe wrong answer: it lights up no strip rather than the
    /// wrong one.</summary>
    public static int? SoloedPart(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
        && n >= 1 && n <= Constants.NO_OF_PARTS
            ? n - 1
            : null;

    /// <summary>What to write to solo <paramref name="zeroBasedPart"/>.</summary>
    public static string ValueForPart(int zeroBasedPart) =>
        (zeroBasedPart + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>What to write to clear solo, taken from the parameter's own option list: the one option
    /// that does not name a part. Asked rather than assumed because an unmatched display string does not
    /// fail loudly -- <c>ParamString</c>'s write turns it into raw 0 with no diagnostic in Release -- so a
    /// hard-coded "OFF" that stopped matching would silently write part 1's value instead of clearing.
    /// Falls back to <see cref="Off"/> when every option looks like a part, which cannot happen for this
    /// parameter as it stands.</summary>
    public static string OffValue(IReadOnlyList<string> options) =>
        options.FirstOrDefault(o => SoloedPart(o) is null) ?? Off;
}
