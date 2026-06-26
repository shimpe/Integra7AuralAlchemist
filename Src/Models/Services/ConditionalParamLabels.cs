using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Generic friendly-label cleanup for "discriminated" parameters whose leaf name is prefixed with a
/// discriminator value (e.g. MFX "Equalizer Low Freq" or SN-A "ConcertGrand String Resonance").
/// Strips "&lt;valueName&gt; " when present; otherwise strips the longest common word-boundary prefix
/// shared by the set (handles cases where the param prefix differs from the display name, e.g.
/// "ConcertGrand" vs "INT 001: Concert Grand"). Never returns empty.
/// </summary>
public static class ConditionalParamLabels
{
    public static string FriendlyName(string valueName, string leafName)
        => StripTyped(valueName, leafName) ?? leafName;

    public static IReadOnlyList<string> FriendlyNames(string valueName, IReadOnlyList<string> leafNames)
    {
        if (leafNames.Count == 0) return leafNames;
        var common = CommonWordPrefix(leafNames);
        return leafNames.Select(n =>
        {
            var r = StripTyped(valueName, n)
                    ?? (common.Length > 0 && n.StartsWith(common, StringComparison.Ordinal)
                            ? n[common.Length..]
                            : n);
            r = r.Trim();
            return r.Length == 0 ? n : r;
        }).ToList();
    }

    private static string? StripTyped(string valueName, string leafName)
    {
        var typed = valueName + " ";
        return leafName.StartsWith(typed, StringComparison.Ordinal) ? leafName[typed.Length..] : null;
    }

    private static string CommonWordPrefix(IReadOnlyList<string> names)
    {
        var p = names[0];
        foreach (var n in names.Skip(1))
        {
            var k = 0;
            while (k < p.Length && k < n.Length && p[k] == n[k]) k++;
            p = p[..k];
            if (p.Length == 0) break;
        }
        var sp = p.LastIndexOf(' ');
        return sp < 0 ? "" : p[..(sp + 1)];
    }
}
