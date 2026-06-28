using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure filtering for the SN-Acoustic instrument picker's "Expansion" family: which ExSN
/// instruments are visible given the loaded ExSN boards, and how an unloaded one is labelled. The ExSN
/// board is implicit in the instrument name's "ExSN{k}" prefix (the hardware has no board parameter).</summary>
public static class ExSnInstrumentFilter
{
    public const string ExpansionFamily = "Expansion";

    /// <summary>The ExSN board number (1..6) named by an instrument's "ExSN{k} …" prefix, or null for a
    /// non-ExSN (INT) instrument.</summary>
    public static int? ExSnBoardOf(string instrumentName)
    {
        const string prefix = "ExSN";
        if (instrumentName.Length <= prefix.Length || !instrumentName.StartsWith(prefix)) return null;
        var digit = instrumentName[prefix.Length];
        return digit is >= '1' and <= '6' ? digit - '0' : null;
    }

    /// <summary>The family names with "Expansion" removed when no ExSN board is loaded; order preserved.</summary>
    public static List<string> VisibleFamilies(IReadOnlyList<string> allFamilyNames, IReadOnlyCollection<int> loadedExSn)
        => loadedExSn.Count > 0
            ? allFamilyNames.ToList()
            : allFamilyNames.Where(f => f != ExpansionFamily).ToList();

    /// <summary>The subset of <paramref name="expansionIndices"/> whose instrument name (looked up in
    /// <paramref name="names"/>) belongs to a loaded ExSN board.</summary>
    public static List<int> LoadedExpansionIndices(IReadOnlyList<int> expansionIndices,
        IReadOnlyList<string> names, IReadOnlyCollection<int> loadedExSn)
    {
        var loaded = new HashSet<int>(loadedExSn);
        return expansionIndices
            .Where(i => i >= 0 && i < names.Count && ExSnBoardOf(names[i]) is { } b && loaded.Contains(b))
            .ToList();
    }

    /// <summary>An instrument's display name: the name suffixed with "(not loaded)" when it is an ExSN
    /// instrument whose board isn't loaded; otherwise the name unchanged. Shares Phase 1's suffix.</summary>
    public static string DisplayName(string instrumentName, IReadOnlyCollection<int> loadedExSn)
    {
        var board = ExSnBoardOf(instrumentName);
        if (board is { } b && !loadedExSn.Contains(b))
            return instrumentName + SrxGroupIdResolution.NotLoadedSuffix;
        return instrumentName;
    }
}
