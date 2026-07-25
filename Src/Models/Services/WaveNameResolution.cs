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

    /// <summary>True when <paramref name="ps"/> holds at least one registered wave-number parameter,
    /// i.e. when <see cref="Apply"/> would do anything at all -- <see cref="Apply"/> skips every entry
    /// whose paths are absent, so for everything else it is already a no-op.
    ///
    /// Worth asking before calling, because the banks argument is not free: <c>WaveformBanks.Default</c>
    /// parses 13 CSV assets through Avalonia's asset loader the first time it is touched, and a domain
    /// with no wave parameters -- every Studio Set block, every system block -- has no reason to pay
    /// for that, nor, in a host with no Avalonia application running, any way to.</summary>
    public static bool Applies(IReadOnlyList<FullyQualifiedParameter> ps)
    {
        for (var i = 0; i < ps.Count; i++)
            if (WaveBankRegistry.Entries.ContainsKey(ps[i].ParSpec.Path))
                return true;
        return false;
    }

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
