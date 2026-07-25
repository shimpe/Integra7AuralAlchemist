using System;
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

    /// <summary>Check every parameter path in <paramref name="snapshot"/> exists in its block, per the
    /// live parameter database <paramref name="domain"/> was built from.
    ///
    /// <c>ModifySingleParameterDisplayedValue</c> is <c>void</c> and silently logs-and-skips a path it
    /// does not recognise rather than throwing, so a snapshot captured against a different parameter
    /// database (a build with a renamed or removed parameter) would otherwise half-apply with nothing
    /// to say so. Like <see cref="ValidateBlocksAreKnown"/>, this checks everything up front, before any
    /// read or write, so a mismatch is refused wholesale instead of leaving the instrument partially
    /// restored.
    ///
    /// Queries <c>GetRelevantParameters(true, true)</c> -- reserved and context-invalid parameters
    /// included -- because this is only an existence check, not a validity check: a parameter that is
    /// not applicable right now (its discriminator currently holds a different value) may become
    /// applicable once an earlier value in the same block sets that discriminator, which is exactly what
    /// restoring in the snapshot's captured order does.
    ///
    /// Assumes every block in <paramref name="snapshot"/> is already known -- call
    /// <see cref="ValidateBlocksAreKnown"/> first. <c>Integra7Domain.GetDomain</c> falls back to an
    /// unrelated block for an address it does not recognise, and this would then validate paths against
    /// the wrong block instead of catching the real problem.</summary>
    public static void ValidateParametersAreKnown(Integra7Domain domain, StudioSetSnapshot snapshot)
    {
        foreach (var block in snapshot.Domains)
        {
            var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
            var known = new HashSet<string>(d.GetRelevantParameters(true, true).Select(p => p.ParSpec.Path));
            foreach (var v in block.Values)
                if (!known.Contains(v.Path))
                    throw new SnapshotFormatException(
                        $"This snapshot contains a parameter this build does not recognise: \"{v.Path}\".");
        }
    }

    /// <summary>Read every block that makes up a Studio Set from the instrument and record it as
    /// displayed values. <paramref name="lease"/> is held by the caller across the whole capture so
    /// nothing else can write to the instrument in the middle of it and produce a Studio Set that
    /// never actually existed.
    ///
    /// Throws when the device does not answer for a block: <c>DomainBase.ReadFromIntegraAsync</c>
    /// deliberately keeps the previous in-memory values on a failed read, which is right for the screen
    /// but wrong for a file -- silently recording stale (or, for a block never read this session, blank)
    /// values would produce a snapshot that looks complete and later gets written back to hardware with
    /// confidence. A partial capture is worse than none, so the whole capture fails instead.</summary>
    public static async Task<StudioSetSnapshot> CaptureAsync(Integra7Domain domain, string name, IMidiLease lease)
    {
        List<SnapshotDomain> domains = [];
        foreach (var (start, offset, offset2) in StudioSetDomainNames.All)
        {
            var d = domain.GetDomain(start, offset, offset2);
            if (!await d.ReadFromIntegraAsync(lease))
                throw new SnapshotFormatException(
                    $"Could not capture the Studio Set: the device did not answer for block " +
                    $"(\"{start}\", \"{offset}\", \"{offset2}\").");

            // (true, false), not the () default: this must capture exactly the set WriteToIntegraAsync
            // will transmit once the snapshot's own discriminators are applied, not the set the UI
            // shows. The write flattens every currently context-valid parameter, reserved ones
            // included -- GetRelevantParameters()'s default excludes reserved, so with the plain
            // default a reserved variant of a parameter (e.g. a per-chorus-type "Reserved" slot the UI
            // never shows) is silently left out of the snapshot. Restoring onto a device whose live
            // discriminator differs then makes that variant context-valid with nothing in the snapshot
            // to set it, and it goes out as whatever raw value happens to be in memory -- raw 0 for one
            // this session never read. Measured: restoring a chorus-off snapshot onto a device with
            // chorus on zeroed 56 of 80 chorus-parameter bytes -- the user's rate, depth, pre-delay and
            // feedback. Do not "tidy" this back to the user-visible default; capturing the write set is
            // the invariant that matters here, not what the UI happens to display.
            List<SnapshotValue> values = d.GetRelevantParameters(true, false)
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
    /// produce a Studio Set that was never actually restored as a whole.
    ///
    /// The device never acknowledges a parameter write -- a DT1 gets no reply, and a failed send is
    /// swallowed inside <c>MidiOut.SafeSend</c> -- so finishing this call only means every block was
    /// *sent*, never that the instrument applied any of it. A failure partway through (a block whose
    /// read times out, most likely) leaves the instrument holding a mix of the snapshot and whatever was
    /// there before: blocks already sent carry the snapshot, the failed block and everything after it
    /// are untouched. Restoring the very same snapshot again is safe and finishes the job rather than
    /// compounding the problem -- every block is applied independently, in the same order, from the same
    /// file.</summary>
    public static async Task RestoreAsync(Integra7Domain domain, StudioSetSnapshot snapshot, IMidiLease lease)
    {
        ValidateBlocksAreKnown(snapshot);
        ValidateParametersAreKnown(domain, snapshot);

        foreach (var block in snapshot.Domains)
            try
            {
                var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);

                // Read the block before applying to it. WriteToIntegraAsync flattens every
                // context-valid parameter in the block into one transmission, not just the ones this
                // restore sets. For a parameter this snapshot's own discriminators will make
                // context-valid, CaptureAsync's GetRelevantParameters(true, false) already guarantees
                // the snapshot covers it -- that invariant, not this read, is what keeps it correct.
                // What this read protects is the parameter a snapshot leaves out entirely and that
                // stays unconditionally valid regardless of any discriminator (an older file, say,
                // captured before this build started including reserved parameters): without a fresh
                // read it would go out as the raw zero an unread parameter defaults to; with one, it
                // goes out as its current value on the device, unchanged.
                if (!await d.ReadFromIntegraAsync(lease))
                    throw new SnapshotFormatException(
                        $"Could not restore the Studio Set: the device did not answer for block " +
                        $"(\"{block.Start}\", \"{block.Offset}\", \"{block.Offset2}\").");

                // Order matters here and must not be changed: some parameters only exist when a
                // discriminator parameter (e.g. the chorus type) has a particular value.
                // ModifySingleParameterDisplayedValue recomputes the parser context on every call, so
                // applying values in the snapshot's order -- which is address order, discriminator
                // before dependents -- sets each discriminator before the parameters that depend on it.
                foreach (var v in block.Values) d.ModifySingleParameterDisplayedValue(v.Path, v.Value);
                await d.WriteToIntegraAsync(lease);
            }
            catch (Exception e) when (e is not SnapshotFormatException)
            {
                // Anything unexpected still needs to say which block it happened on -- the loop is the
                // only place that knows, the exception itself would not.
                throw new SnapshotFormatException(
                    $"Restoring the Studio Set failed on block (\"{block.Start}\", \"{block.Offset}\", " +
                    $"\"{block.Offset2}\").", e);
            }

        Log.Information(
            "Sent Studio Set snapshot {Name} ({BlockCount} blocks) to the instrument. The device does " +
            "not acknowledge parameter writes, so this confirms the data was sent, not that it was applied.",
            snapshot.Name, snapshot.Domains.Count);
    }
}
