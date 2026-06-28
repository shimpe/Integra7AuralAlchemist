using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The wave-number parameters whose name list is bank-selected, each mapped to its sibling
/// Wave Group Type + Wave Group ID parameter paths (within the same partial domain).</summary>
public static class WaveBankRegistry
{
    public readonly record struct Siblings(string TypePath, string IdPath);

    public static readonly IReadOnlyDictionary<string, Siblings> Entries = Build();

    private static Dictionary<string, Siblings> Build()
    {
        var d = new Dictionary<string, Siblings>();

        const string sp = "PCM Synth Tone Partial/";
        var synthSiblings = new Siblings(sp + "Wave Group Type", sp + "Wave Group ID");
        d[sp + "Wave Number L (Mono)"] = synthSiblings;
        d[sp + "Wave Number R"] = synthSiblings;

        const string dp = "PCM Drum Kit Partial/";
        for (var n = 1; n <= 4; n++)
        {
            var sib = new Siblings($"{dp}WMT{n} Wave Group Type", $"{dp}WMT{n} Wave Group ID");
            d[$"{dp}WMT{n} Wave Number L (Mono)"] = sib;
            d[$"{dp}WMT{n} Wave Number R"] = sib;
        }

        return d;
    }
}
