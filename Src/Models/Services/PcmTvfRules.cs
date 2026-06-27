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

    private static readonly Dictionary<string, string> CurveModes = new()
    {
        ["Off"] = "Bypass",
        ["Low-pass filter"] = "Low pass",
        ["Band-pass filter"] = "Band pass",
        ["High-pass filter"] = "High pass",
        ["Peaking filter"] = "Peaking",
        ["Low-pass filter 2"] = "Low pass",
        ["Low-pass filter 3"] = "Low pass",
    };

    /// <summary>Maps a TVF filter-type display string to a <c>FilterCurve</c> mode; unknown → "Low pass".</summary>
    public static string CurveMode(string? filterType)
    {
        if (filterType is not null && CurveModes.TryGetValue(filterType, out var m)) return m;
        return "Low pass";
    }

    /// <summary>True for the steeper low-pass variants (LPF2 / LPF3), which roll off faster.</summary>
    public static bool CurveSteep(string? filterType) =>
        filterType is "Low-pass filter 2" or "Low-pass filter 3";
}
