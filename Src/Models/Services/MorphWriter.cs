using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Send one blend to a part.
///
/// <b>It does not read the blocks first, and that is the whole reason it exists</b> rather than calling
/// <c>StudioSetSnapshotService.RestoreToneAsync</c>. A restore reads because a snapshot may not cover
/// every parameter the bulk write will transmit, and an unread parameter would go out as raw zero. A
/// blend is built from full captures of the engine it targets, so it covers all of them by construction.
/// Skipping the read is what makes a flush affordable four times a second while a pointer is moving.
///
/// Values are applied in the snapshot's own order, which is address order, so a discriminator is set
/// before the parameters that depend on it -- the property <c>ApplyBlockValues</c> relies on for the same
/// reason. The part number comes from the caller and is written into the Start address, so a blend of
/// patches captured from anywhere lands where it is asked to.</summary>
public static class MorphWriter
{
    public static async Task WriteAsync(Integra7Domain domain, Integra7Snapshot blend,
        int zeroBasedPartNo, string toneType, IMidiLease? lease)
    {
        var byOffsets = blend.Domains.ToDictionary(d => (d.Offset, d.Offset2));

        foreach (var (start, offset, offset2) in ToneDomainNames.For(toneType, zeroBasedPartNo))
        {
            if (!byOffsets.TryGetValue((offset, offset2), out var block)) continue;

            var d = domain.GetDomain(start, offset, offset2);
            foreach (var value in block.Values)
            {
                if (value.Raw is { } raw) d.ModifySingleParameterRawValue(value.Path, raw);
                else d.ModifySingleParameterDisplayedValue(value.Path, value.Value);
            }

            await d.WriteToIntegraAsync(lease);
        }
    }
}
