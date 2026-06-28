using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>On a wave-group Type/ID change, resets any governed wave number that is out of range for the
/// newly-selected bank to that bank's first wave. Edit-triggered only (called from the write paths'
/// IsParent branch), so loads never auto-correct a stored value.</summary>
public static class WaveOutOfRangeReset
{
    /// <summary>True if <paramref name="path"/> is one of the wave-group discriminators (a Type or ID).</summary>
    public static bool IsWaveGroupDiscriminator(string path)
    {
        foreach (var sib in WaveBankRegistry.Entries.Values)
            if (sib.TypePath == path || sib.IdPath == path) return true;
        return false;
    }

    /// <summary>True if the selected bank exists and does NOT contain <paramref name="number"/>.</summary>
    public static bool NeedsReset(WaveformBanks banks, string groupType, int groupId, int number)
    {
        var bank = banks.Bank(groupType, groupId);
        return bank != null && !bank.ContainsKey(number);
    }

    /// <summary>If <paramref name="edited"/> is a wave-group discriminator, reset each governed wave whose
    /// number is out of range for the new bank to the bank's first wave (writing it to hardware).</summary>
    public static async Task ApplyAsync(DomainBase domain, FullyQualifiedParameter edited, WaveformBanks banks)
    {
        if (!IsWaveGroupDiscriminator(edited.ParSpec.Path)) return;

        var byPath = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in domain.GetRelevantParameters(true, true)) byPath[p.ParSpec.Path] = p;

        foreach (var (wavePath, sib) in WaveBankRegistry.Entries)
        {
            if (sib.TypePath != edited.ParSpec.Path && sib.IdPath != edited.ParSpec.Path) continue;
            if (!byPath.TryGetValue(wavePath, out var wave)
                || !byPath.TryGetValue(sib.TypePath, out var type)
                || !byPath.TryGetValue(sib.IdPath, out var id))
                continue;

            var groupId = (int)id.RawNumericValue;
            var number = (int)wave.RawNumericValue;
            if (!NeedsReset(banks, type.StringValue, groupId, number)) continue;

            var bank = banks.Bank(type.StringValue, groupId)!;
            var first = banks.FirstWave(type.StringValue, groupId);
            wave.RawNumericValue = first;
            wave.StringValue = bank.TryGetValue(first, out var name)
                ? name : first.ToString(CultureInfo.InvariantCulture);
            wave.EffectiveRepr = bank;
            await domain.WriteToIntegraAsync(wave.ParSpec.Path); // single-arg: writes the FQP's current raw value
        }
    }
}
