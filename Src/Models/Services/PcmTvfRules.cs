using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure formatting helpers for the PCM Synth TVF (filter) — short labels for the rack card.</summary>
public static class PcmTvfRules
{
    private static readonly Dictionary<string, string> Abbrevs = new()
    {
        ["Off"] = "Off",
        ["Low-pass filter"] = "LPF",
        ["Band-pass filter"] = "BPF",
        ["High-pass filter"] = "HPF",
        ["Peaking filter"] = "PKG",
        ["Low-pass filter 2"] = "LPF2",
        ["Low-pass filter 3"] = "LPF3",
    };

    /// <summary>Short label for a TVF filter-type display string; passes unknown values through;
    /// null/empty → "".</summary>
    public static string Abbrev(string? filterType)
    {
        if (string.IsNullOrEmpty(filterType)) return "";
        return Abbrevs.TryGetValue(filterType, out var a) ? a : filterType;
    }
}
