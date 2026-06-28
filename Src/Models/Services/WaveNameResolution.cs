using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Resolves bank-selected waveform names. Pure <see cref="Resolve"/> is unit-tested; <see cref="Apply"/>
/// is the domain glue that sets EffectiveRepr + StringValue on a partial's wave-number parameters
/// (invoked from the domain read path in Phase 2).</summary>
public static class WaveNameResolution
{
    /// <summary>The effective bank + display name for a wave number given its sibling Group Type/ID values.</summary>
    public static (IDictionary<int, string>? bank, string display) Resolve(
        WaveformBanks banks, string groupType, int groupId, int number)
        => (banks.Bank(groupType, groupId), banks.Name(groupType, groupId, number));

    /// <summary>For each registered wave-number parameter in <paramref name="ps"/>, read its sibling
    /// Group Type/ID values and set its EffectiveRepr + StringValue from the selected bank.</summary>
    public static void Apply(IReadOnlyList<FullyQualifiedParameter> ps, WaveformBanks banks)
    {
        var byPath = new Dictionary<string, FullyQualifiedParameter>(ps.Count);
        foreach (var p in ps) byPath[p.ParSpec.Path] = p;

        foreach (var (wavePath, sib) in WaveBankRegistry.Entries)
        {
            if (!byPath.TryGetValue(wavePath, out var wave)
                || !byPath.TryGetValue(sib.TypePath, out var type)
                || !byPath.TryGetValue(sib.IdPath, out var id))
                continue;

            // Use the raw decoded indices (not StringValue): a wave param's StringValue may be a display
            // name (e.g. while PARTIAL_WAVEFORMS is still attached), but RawNumericValue is always the raw
            // wave/group index regardless of which repr is in effect.
            var number = (int)wave.RawNumericValue;
            var groupId = (int)id.RawNumericValue;

            var (bank, display) = Resolve(banks, type.StringValue, groupId, number);
            wave.EffectiveRepr = bank;
            wave.StringValue = display;
        }
    }
}
