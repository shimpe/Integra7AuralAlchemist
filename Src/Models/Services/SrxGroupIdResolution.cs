using System.Collections.Generic;
using System.Globalization;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Filters the PCM "Wave Group ID" (SRX board) option list to the currently-loaded SRX boards.
/// Pure <see cref="VisibleBoards"/>/<see cref="BuildRepr"/> are unit-tested; Apply (added
/// in the same file) is the domain glue that sets each Wave Group ID param's EffectiveRepr — mirrors
/// <see cref="WaveNameResolution"/>.</summary>
public static class SrxGroupIdResolution
{
    /// <summary>Board numbers to show: the loaded boards plus the patch's current board (so an unloaded
    /// current board stays selectable), clamped to 1..12, deduped, ascending.</summary>
    public static List<int> VisibleBoards(IReadOnlyCollection<int> loaded, int current)
    {
        var set = new SortedSet<int>();
        foreach (var b in loaded)
            if (b is >= 1 and <= 12) set.Add(b);
        if (current is >= 1 and <= 12) set.Add(current);
        return new List<int>(set);
    }

    /// <summary>The EffectiveRepr (board number -> its string) for a Wave Group ID whose sibling Wave
    /// Group Type is <paramref name="groupType"/>: the filtered board list when SRX, otherwise null
    /// (leaving the param as its plain numeric field, unchanged from today).</summary>
    public static IDictionary<int, string>? BuildRepr(string groupType, IReadOnlyCollection<int> loaded, int current)
    {
        if (groupType != WaveBankResolver.TypeSrx) return null;
        var dict = new Dictionary<int, string>();
        foreach (var b in VisibleBoards(loaded, current))
            dict[b] = b.ToString(CultureInfo.InvariantCulture);
        return dict;
    }

    /// <summary>For each distinct Wave Group ID param (paired with its Wave Group Type via
    /// <see cref="WaveBankRegistry"/>), set its EffectiveRepr from the loaded set + current board, then
    /// re-fire StringValue so the friendly ParamString.Options and the advanced-grid combo refresh
    /// (the read's StringValue notification fired before this pass set EffectiveRepr).</summary>
    public static void Apply(IReadOnlyList<FullyQualifiedParameter> ps, IReadOnlyCollection<int> loaded)
    {
        var byPath = new Dictionary<string, FullyQualifiedParameter>(ps.Count);
        foreach (var p in ps) byPath[p.ParSpec.Path] = p;

        var seen = new HashSet<string>();
        foreach (var sib in WaveBankRegistry.Entries.Values)
        {
            if (!seen.Add(sib.IdPath)) continue; // each (Type, Id) pair once
            if (!byPath.TryGetValue(sib.IdPath, out var id) || !byPath.TryGetValue(sib.TypePath, out var type))
                continue;

            id.EffectiveRepr = BuildRepr(type.StringValue, loaded, (int)id.RawNumericValue);
            var refire = id.StringValue;
            id.StringValue = refire; // notify listeners now that EffectiveRepr changed
        }
    }
}
