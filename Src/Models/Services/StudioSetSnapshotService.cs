using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Captures a live Studio Set to a <see cref="StudioSetSnapshot"/> and restores one back to
/// the instrument. Pure data movement -- the file format lives in StudioSetSnapshot, the address list
/// in StudioSetDomainNames.</summary>
public static class StudioSetSnapshotService
{
    /// <summary>Check every block in <paramref name="snapshot"/> against
    /// <see cref="StudioSetDomainNames.All"/> before anything is written.
    ///
    /// <c>Integra7Domain.GetDomain</c> does not throw for an address triple it does not recognise: it
    /// logs an error and falls back to an unrelated block (the first entry in its map). A snapshot
    /// with an unknown block would silently have its values applied to that unrelated block and then
    /// bulk-written to the instrument -- corruption with nothing to say so. Validating every block up
    /// front, before any write happens, means a bad file cannot leave the instrument half restored.</summary>
    public static void ValidateBlocksAreKnown(StudioSetSnapshot snapshot)
    {
        var known = new HashSet<(string Start, string Offset, string Offset2)>(StudioSetDomainNames.All);
        foreach (var d in snapshot.Domains)
            if (!known.Contains((d.Start, d.Offset, d.Offset2)))
                throw new SnapshotFormatException(
                    $"This snapshot contains a block this build does not recognise: " +
                    $"(\"{d.Start}\", \"{d.Offset}\", \"{d.Offset2}\").");
    }

    /// <summary>Read every block that makes up a Studio Set from the instrument and record it as
    /// displayed values. <paramref name="lease"/> is held by the caller across the whole capture so
    /// nothing else can write to the instrument in the middle of it and produce a Studio Set that
    /// never actually existed.</summary>
    public static async Task<StudioSetSnapshot> CaptureAsync(Integra7Domain domain, string name, IMidiLease lease)
    {
        List<SnapshotDomain> domains = [];
        foreach (var (start, offset, offset2) in StudioSetDomainNames.All)
        {
            var d = domain.GetDomain(start, offset, offset2);
            await d.ReadFromIntegraAsync(lease);
            List<SnapshotValue> values = d.GetRelevantParameters()
                .Select(p => new SnapshotValue(p.ParSpec.Path, p.StringValue))
                .ToList();
            domains.Add(new SnapshotDomain(start, offset, offset2, values));
        }

        Log.Information("Captured Studio Set snapshot {Name} ({BlockCount} blocks).", name, domains.Count);
        return new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, name, domains);
    }

    /// <summary>Write a captured Studio Set back to the instrument, one block at a time.
    /// <paramref name="lease"/> is held by the caller across the whole restore for the same reason as
    /// <see cref="CaptureAsync"/>: interleaving with anything else writing to the instrument would
    /// produce a Studio Set that was never actually restored as a whole.</summary>
    public static async Task RestoreAsync(Integra7Domain domain, StudioSetSnapshot snapshot, IMidiLease lease)
    {
        ValidateBlocksAreKnown(snapshot);

        foreach (var block in snapshot.Domains)
        {
            var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
            // Order matters here and must not be changed: some parameters only exist when a
            // discriminator parameter (e.g. the chorus type) has a particular value.
            // ModifySingleParameterDisplayedValue recomputes the parser context on every call, so
            // applying values in the snapshot's order -- which is address order, discriminator
            // before dependents -- sets each discriminator before the parameters that depend on it.
            foreach (var v in block.Values) d.ModifySingleParameterDisplayedValue(v.Path, v.Value);
            await d.WriteToIntegraAsync(lease);
        }

        Log.Information("Restored Studio Set snapshot {Name} ({BlockCount} blocks).", snapshot.Name,
            snapshot.Domains.Count);
    }
}
