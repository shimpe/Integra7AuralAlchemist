using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure, UI-free catalog for the Integra-7 MFX: groups the 68 effect types (0 = Thru) into
/// musical families for a two-step picker, and cleans up per-type parameter leaf names by stripping
/// the redundant effect-name prefix (e.g. "Equalizer Low Freq" -> "Low Freq").
/// Type indices match the MFX_TYPE enum order (= ParamString.Options order for the MFX Type param).
/// </summary>
public static class MfxCatalog
{
    /// <summary>One picker family: a display name and the MFX type indices it contains.</summary>
    public sealed record Family(string Name, int[] TypeIndices);

    /// <summary>Families in display order; the union of TypeIndices covers 0..67 exactly once.</summary>
    public static IReadOnlyList<Family> Families { get; } = new List<Family>
    {
        new("Off (Thru)",       new[] { 0 }),
        new("EQ & Filter",      new[] { 1, 2, 3, 4, 5 }),
        new("Wah & Voice",      new[] { 6, 7 }),
        new("Phaser",           new[] { 9, 10, 11, 12, 13, 14 }),
        new("Chorus & Mod",     new[] { 22, 23, 24, 25, 26, 27 }),
        new("Tremolo & Pan",    new[] { 15, 16, 17, 18 }),
        new("Rotary",           new[] { 19, 20, 21 }),
        new("Amp & Distortion", new[] { 8, 28, 29, 30 }),
        new("Dynamics",         new[] { 31, 32, 33 }),
        new("Delay",            new[] { 34, 35, 36, 37, 38, 39, 40 }),
        new("Lo-Fi & Pitch",    new[] { 41, 42, 43, 44 }),
        new("Combos",           Enumerable.Range(45, 23).ToArray()), // 45..67
    };

    /// <summary>Family name containing the given type index, or "" if out of range.</summary>
    public static string FamilyOf(int typeIndex)
        => Families.FirstOrDefault(f => f.TypeIndices.Contains(typeIndex))?.Name ?? "";

    /// <summary>Type indices in a family (empty if the family name is unknown).</summary>
    public static IReadOnlyList<int> TypesIn(string familyName)
        => Families.FirstOrDefault(f => f.Name == familyName)?.TypeIndices ?? Array.Empty<int>();

    /// <summary>Friendly label for a single per-type parameter leaf name.</summary>
    public static string FriendlyParamName(string effectTypeName, string leafName)
        => StripTyped(effectTypeName, leafName) ?? leafName;

    /// <summary>
    /// Friendly labels for all of one effect type's parameter leaf names. Prefers stripping
    /// "&lt;effectTypeName&gt; "; for names that don't match (e.g. punctuation differences in combo
    /// types) falls back to the longest common word-boundary prefix shared by the set. Never empty.
    /// </summary>
    public static IReadOnlyList<string> FriendlyParamNames(string effectTypeName, IReadOnlyList<string> leafNames)
    {
        if (leafNames.Count == 0) return leafNames;
        var common = CommonWordPrefix(leafNames);
        return leafNames.Select(n =>
        {
            var r = StripTyped(effectTypeName, n)
                    ?? (common.Length > 0 && n.StartsWith(common, StringComparison.Ordinal)
                            ? n[common.Length..]
                            : n);
            r = r.Trim();
            return r.Length == 0 ? n : r;
        }).ToList();
    }

    // Strips "<effectTypeName> " from the leaf if present; returns null if it doesn't match.
    private static string? StripTyped(string effectTypeName, string leafName)
    {
        var typed = effectTypeName + " ";
        return leafName.StartsWith(typed, StringComparison.Ordinal) ? leafName[typed.Length..] : null;
    }

    // Longest common prefix across all names, trimmed back to a whole-word boundary (incl. trailing space).
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
