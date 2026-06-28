using System.Collections.Generic;
using System.Globalization;
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

            if (!int.TryParse(wave.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                continue; // wave StringValue is the raw number until we override it below

            if (!int.TryParse(id.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId))
                groupId = 0;

            var (bank, display) = Resolve(banks, type.StringValue, groupId, number);
            wave.EffectiveRepr = bank;
            wave.StringValue = display;
        }
    }
}
