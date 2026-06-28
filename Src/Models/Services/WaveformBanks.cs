using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Platform;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Holds the per-bank PCM waveform name lists (INT + SRX1..12) and resolves a wave number to a
/// name given a wave group's Type/ID. Bank selection is delegated to <see cref="WaveBankResolver"/>.</summary>
public sealed class WaveformBanks
{
    private readonly IReadOnlyDictionary<string, IDictionary<int, string>> _banks;

    public WaveformBanks(IReadOnlyDictionary<string, IDictionary<int, string>> banks) => _banks = banks;

    /// <summary>The name list for the bank selected by (groupType, groupId), or null if absent.</summary>
    public IDictionary<int, string>? Bank(string groupType, int groupId)
        => _banks.TryGetValue(WaveBankResolver.BankName(groupType, groupId), out var b) ? b : null;

    /// <summary>The wave name for the given number in the selected bank, or the raw number if unresolved.</summary>
    public string Name(string groupType, int groupId, int number)
    {
        var b = Bank(groupType, groupId);
        return b != null && b.TryGetValue(number, out var n) ? n : number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reverse lookup: the wave number for a name in the selected bank, or null if not found.</summary>
    public int? Number(string groupType, int groupId, string name)
    {
        var b = Bank(groupType, groupId);
        if (b == null) return null;
        foreach (var kv in b)
            if (kv.Value == name) return kv.Key;
        return null;
    }

    /// <summary>The lowest wave number in the selected bank (used for the out-of-range reset), or 0.</summary>
    public int FirstWave(string groupType, int groupId)
    {
        var b = Bank(groupType, groupId);
        if (b == null || b.Count == 0) return 0;
        var min = int.MaxValue;
        foreach (var k in b.Keys)
            if (k < min) min = k;
        return min;
    }

    /// <summary>Parse one wave CSV: skip the first (comment) line, then line order gives the wave number
    /// from 0; the first comma-separated field (unquoted) is the name. Mirrors the build-time loader.</summary>
    public static Dictionary<int, string> ParseWaveCsv(TextReader reader)
    {
        var dict = new Dictionary<int, string>();
        reader.ReadLine(); // header comment
        string? line;
        var id = 0;
        while ((line = reader.ReadLine()) != null)
        {
            var name = line.Split(',')[0].Trim('"');
            dict[id++] = name;
        }
        return dict;
    }

    private static readonly string[] BankNames =
        ["INT", "SRX1", "SRX2", "SRX3", "SRX4", "SRX5", "SRX6", "SRX7", "SRX8", "SRX9", "SRX10", "SRX11", "SRX12"];

    /// <summary>Load all 13 banks from the shipped CSV assets (avares://…/Assets/PartialWaveForms_*.csv).</summary>
    public static WaveformBanks LoadFromAssets()
    {
        var banks = new Dictionary<string, IDictionary<int, string>>();
        foreach (var name in BankNames)
        {
            var uri = new Uri($"avares://Integra7AuralAlchemist/Assets/PartialWaveForms_{name}.csv");
            using var reader = new StreamReader(AssetLoader.Open(uri));
            banks[name] = ParseWaveCsv(reader);
        }
        return new WaveformBanks(banks);
    }

    private static WaveformBanks? _default;
    /// <summary>Lazily-loaded shared instance backed by the CSV assets.</summary>
    public static WaveformBanks Default => _default ??= LoadFromAssets();
}
