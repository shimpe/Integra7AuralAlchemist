namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Small pure helpers for the friendly filter UI.</summary>
public static class SnsFilterRules
{
    /// <summary>Short label for a filter mode (for the partial card summary).</summary>
    public static string Abbrev(string mode) => mode switch
    {
        "Bypass" => "BYP",
        "Low pass" => "LPF",
        "High pass" => "HPF",
        "Band pass" => "BPF",
        "Peaking" => "PEAK",
        "Low pass 2" => "LPF2",
        "Low pass 3" => "LPF3",
        "Low pass 4" => "LPF4",
        _ => mode
    };
}
