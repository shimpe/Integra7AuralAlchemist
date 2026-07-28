using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Captures a live Studio Set, or the single tone loaded into one part, to an
/// <see cref="Integra7Snapshot"/> and restores either back to the instrument. Pure data movement -- the
/// file format lives in Integra7Snapshot, the address lists in StudioSetDomainNames and ToneDomainNames.
///
/// Both live here rather than in two services because every guard is the same guard: which parameters
/// exist, reading a block before bulk-writing it, raw beating display string, refusing wholesale rather
/// than half-restoring. Only the block list and the re-targeting differ, so the capture and restore
/// bodies are three private helpers shared between the two pairs (<see cref="CaptureBlockValues"/>,
/// <see cref="ApplyBlockValues"/>, <see cref="ValidateParametersAreKnownIn"/>). Splitting them would
/// have duplicated the reasoning, and the comments here are most of what keeps it correct.</summary>
public static class StudioSetSnapshotService
{
    /// <summary>True for the per-part block -- the one that carries the tone bank and program number,
    /// i.e. "Offset2/Studio Set Part 1".."16". Not the "Offset2/Studio Set Part EQ N" block, which
    /// shares the prefix but loads nothing, and not "Offset2/Studio Set MIDI Channel N", which does
    /// not share it. The EQ exclusion is the whole reason this is a method and not a StartsWith at the
    /// call site.</summary>
    private static bool SelectsATone(string offset2) =>
        offset2.StartsWith("Offset2/Studio Set Part ", StringComparison.Ordinal) &&
        !offset2.StartsWith("Offset2/Studio Set Part EQ ", StringComparison.Ordinal);

    /// <summary>How long to let the device load the tone a part block just selected, before reading
    /// the next block. Matches <c>PartViewModel.PresetSettleMilliseconds</c>, which is private there;
    /// its comment records why it exists -- "Reading the instant the program change goes out returns
    /// the outgoing tone". Precautionary here: restoring a part block sets that part's tone bank and
    /// program number, so the device starts loading a tone just as the very next Part EQ read goes
    /// out, and an unanswered read aborts the restore half-done. Drop this if hardware shows the
    /// device answers regardless.</summary>
    private const int PartSettleMilliseconds = 250;

    /// <summary>Check every block in <paramref name="snapshot"/> against
    /// <see cref="StudioSetDomainNames.All"/> before anything is written.
    ///
    /// <c>Integra7Domain.GetDomain</c> does not throw for an address triple it does not recognise: it
    /// logs an error and falls back to an unrelated block (the first entry in its map). A snapshot
    /// with an unknown block would silently have its values applied to that unrelated block and then
    /// bulk-written to the instrument -- corruption with nothing to say so. Validating every block up
    /// front, before any write happens, means a bad file cannot leave the instrument half restored.</summary>
    public static void ValidateBlocksAreKnown(Integra7Snapshot snapshot)
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
    public static void ValidateParametersAreKnown(Integra7Domain domain, Integra7Snapshot snapshot)
    {
        foreach (var block in snapshot.Domains)
            ValidateParametersAreKnownIn(domain.GetDomain(block.Start, block.Offset, block.Offset2), block.Values);
    }

    /// <summary>The per-block half of <see cref="ValidateParametersAreKnown"/>, split out so restoring a
    /// tone can run the identical check against the block it is really going to write to. A tone restore
    /// re-targets: the domain the values land in is chosen from the requested part, not from the Start
    /// recorded in the file, so it cannot go through the public overload above without validating against
    /// whichever part the file happened to be captured from.</summary>
    private static void ValidateParametersAreKnownIn(DomainBase d, List<SnapshotValue> values)
    {
        var known = new HashSet<string>(d.GetRelevantParameters(true, true).Select(p => p.ParSpec.Path));
        foreach (var v in values)
            if (!known.Contains(v.Path))
                throw new SnapshotFormatException(
                    $"This snapshot contains a parameter this build does not recognise: \"{v.Path}\".");
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
    public static async Task<Integra7Snapshot> CaptureAsync(Integra7Domain domain, string name, IMidiLease lease)
    {
        List<SnapshotDomain> domains = [];
        foreach (var (start, offset, offset2) in StudioSetDomainNames.All)
        {
            var d = domain.GetDomain(start, offset, offset2);
            if (!await d.ReadFromIntegraAsync(lease))
                throw new SnapshotFormatException(
                    $"Could not capture the Studio Set: the device did not answer for block " +
                    $"(\"{start}\", \"{offset}\", \"{offset2}\").");

            domains.Add(new SnapshotDomain(start, offset, offset2, CaptureBlockValues(d)));
        }

        Log.Information("Captured Studio Set snapshot {Name} ({BlockCount} blocks).", name, domains.Count);
        return new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, name, domains);
    }

    /// <summary>Every value a just-read block should carry into a snapshot.
    ///
    /// (true, false), not the () default: this must capture exactly the set WriteToIntegraAsync will
    /// transmit once the snapshot's own discriminators are applied, not the set the UI shows. The write
    /// flattens every currently context-valid parameter, reserved ones included --
    /// GetRelevantParameters()'s default excludes reserved, so with the plain default a reserved variant
    /// of a parameter (e.g. a per-chorus-type "Reserved" slot the UI never shows) is silently left out of
    /// the snapshot. Restoring onto a device whose live discriminator differs then makes that variant
    /// context-valid with nothing in the snapshot to set it, and it goes out as whatever raw value
    /// happens to be in memory -- raw 0 for one this session never read. Measured: restoring a chorus-off
    /// snapshot onto a device with chorus on zeroed 56 of 80 chorus-parameter bytes -- the user's rate,
    /// depth, pre-delay and feedback. Do not "tidy" this back to the user-visible default; capturing the
    /// write set is the invariant that matters here, not what the UI happens to display.
    ///
    /// Both forms of every value: the raw one the device stores, which is what restoring applies, and the
    /// displayed one, which is what makes these files readable and diffable -- the point of the format. A
    /// text parameter has no raw form (its value IS the string), so its Raw stays null and restoring
    /// falls back to the string, which for it is correct.</summary>
    private static List<SnapshotValue> CaptureBlockValues(DomainBase d) =>
        d.GetRelevantParameters(true, false)
            .Select(p => new SnapshotValue(p.ParSpec.Path, p.StringValue,
                p.IsNumeric || p.IsDiscrete ? p.RawNumericValue : null))
            .ToList();

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
    public static async Task RestoreAsync(Integra7Domain domain, Integra7Snapshot snapshot, IMidiLease lease)
    {
        // A tone file would trip ValidateBlocksAreKnown a line later anyway -- none of its blocks are in
        // StudioSetDomainNames.All -- but "contains a block this build does not recognise" would blame the
        // build for what is really the user picking the wrong file. Say what actually happened instead.
        if (snapshot.Kind != SnapshotKinds.StudioSet)
            throw new SnapshotFormatException(
                $"This file holds \"{snapshot.Kind}\", not a Studio Set.");

        ValidateBlocksAreKnown(snapshot);
        ValidateParametersAreKnown(domain, snapshot);

        foreach (var block in snapshot.Domains)
            try
            {
                var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);

                // Read the block before applying to it -- see ApplyBlockValues for why the read has to
                // happen and not just the write.
                if (!await d.ReadFromIntegraAsync(lease))
                    throw new SnapshotFormatException(
                        $"Could not restore the Studio Set: the device did not answer for block " +
                        $"(\"{block.Start}\", \"{block.Offset}\", \"{block.Offset2}\").");
                ApplyBlockValues(d, block.Values);
                await d.WriteToIntegraAsync(lease);

                // A part block just set that part's tone bank and program number, so the device is
                // now loading a tone -- and the very next thing this loop does is read that part's
                // EQ block. See PartSettleMilliseconds.
                if (SelectsATone(block.Offset2)) await Task.Delay(PartSettleMilliseconds);
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

    /// <summary>Apply a snapshot block's values to the live block they belong to, ready for one bulk
    /// write. <paramref name="d"/> must already have been read from the instrument.
    ///
    /// That read is not optional. WriteToIntegraAsync flattens every context-valid parameter in the block
    /// into one transmission, not just the ones this restore sets. For a parameter this snapshot's own
    /// discriminators will make context-valid, <see cref="CaptureBlockValues"/>'s
    /// GetRelevantParameters(true, false) already guarantees the snapshot covers it -- that invariant, not
    /// the read, is what keeps it correct. What the read protects is the parameter a snapshot leaves out
    /// entirely and that stays unconditionally valid regardless of any discriminator (an older file, say,
    /// captured before this build started including reserved parameters): without a fresh read it would go
    /// out as the raw zero an unread parameter defaults to; with one, it goes out as its current value on
    /// the device, unchanged.</summary>
    private static void ApplyBlockValues(DomainBase d, List<SnapshotValue> values)
    {
        // Which of this block's parameters are text, i.e. have no raw form at all. Asked of the domain --
        // the same query ValidateParametersAreKnownIn uses -- rather than re-derived from the parameter
        // spec here, so there is one place that decides what a parameter is. (true, true) because whether
        // a parameter is text does not depend on the parser context, and a parameter this restore is
        // about to make context-valid still has to be classified correctly.
        var textParameters = new HashSet<string>(d.GetRelevantParameters(true, true)
            .Where(p => !p.IsNumeric && !p.IsDiscrete).Select(p => p.ParSpec.Path));

        // Order matters here and must not be changed: some parameters only exist when a discriminator
        // parameter (e.g. the chorus type) has a particular value. Both Modify methods recompute the
        // parser context on every call, so applying values in the snapshot's order -- which is address
        // order, discriminator before dependents -- sets each discriminator before the parameters that
        // depend on it.
        foreach (var v in values)
            // Raw wins whenever the file has one. It is the value the device actually stores, and it
            // survives this build renaming or reordering an enum string, which the display string does
            // not: UpdateFromDisplayedValue's key.Count == 0 branch turns an unmatched string into raw 0
            // with no diagnostic at all in Release. A text parameter's value IS its string and carries no
            // raw, so it falls through to the display path, which for it is correct rather than a
            // fallback.
            if (v.Raw is { } raw && !textParameters.Contains(v.Path))
                d.ModifySingleParameterRawValue(v.Path, raw);
            else
                d.ModifySingleParameterDisplayedValue(v.Path, v.Value);
    }

    /// <summary>Read every block that makes up the tone currently loaded into
    /// <paramref name="zeroBasedPartNo"/> and record it, exactly as <see cref="CaptureAsync"/> does for a
    /// Studio Set: same lease reasoning, same abort-on-unanswered-read reasoning, same value set (see
    /// <see cref="CaptureBlockValues"/>).
    ///
    /// <paramref name="toneType"/> is the engine the part currently holds, and it decides which blocks
    /// there even are -- so it is recorded in the snapshot, because a file that does not name its engine
    /// cannot be restored to anything. Callers should have checked it with
    /// <c>ToneDomainNames.IsKnownToneType</c>; an unrecognised one throws ArgumentException from
    /// <c>ToneDomainNames.For</c> before any MIDI happens.</summary>
    public static async Task<Integra7Snapshot> CaptureToneAsync(Integra7Domain domain, int zeroBasedPartNo,
        string toneType, string name, IMidiLease lease)
    {
        List<SnapshotDomain> domains = [];
        foreach (var (start, offset, offset2) in ToneDomainNames.For(toneType, zeroBasedPartNo))
        {
            var d = domain.GetDomain(start, offset, offset2);
            if (!await d.ReadFromIntegraAsync(lease))
                throw new SnapshotFormatException(
                    $"Could not capture the tone: the device did not answer for block " +
                    $"(\"{start}\", \"{offset}\", \"{offset2}\").");

            domains.Add(new SnapshotDomain(start, offset, offset2, CaptureBlockValues(d)));
        }

        Log.Information("Captured {ToneType} tone snapshot {Name} from part {Part} ({BlockCount} blocks).",
            toneType, name, zeroBasedPartNo + 1, domains.Count);
        return new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, name, domains,
            SnapshotKinds.Tone, toneType);
    }

    /// <summary>Write a captured tone into <paramref name="zeroBasedPartNo"/>, one block at a time. The
    /// same caveats as <see cref="RestoreAsync"/> apply: the lease is the caller's for the whole restore,
    /// the device acknowledges nothing, and a failure partway through leaves the part holding a mix that
    /// re-running the same restore repairs.
    ///
    /// The part number lives in the Start address of every block, so this re-targets rather than replaying
    /// what the file recorded: the target triples come from
    /// <c>ToneDomainNames.For(snapshot.ToneType, zeroBasedPartNo)</c> and each snapshot block is matched to
    /// one by (Offset, Offset2), which carry the engine and the block identity and nothing about the part.
    /// That is what lets a tone captured from part 3 load into part 5 -- most of the point of the feature.
    /// The Start recorded in the file is deliberately never used, not even to check: a file hand-edited to
    /// name a part that does not exist would resolve, via GetDomain's silent fallback, to an unrelated
    /// block.
    ///
    /// <paramref name="currentToneType"/> is the engine the target part holds right now, and it must match
    /// the snapshot's. These blocks are the *temporary tone* area, whose layout is the engine's: PCM Synth
    /// data written into a part whose temporary tone is SuperNATURAL lands at addresses that mean something
    /// else entirely.</summary>
    /// <summary>The three things that must be true before anything is written into a part's temporary
    /// tone, as a method of its own so that the second path into that area -- the morph pad, which writes
    /// a blend rather than a file -- refuses the same cases in the same words.
    ///
    /// It was extracted rather than copied for one reason: the engine message tells the user which preset
    /// to select before trying again, and two versions of that sentence would eventually be two different
    /// pieces of advice. Every throw carries a message written to be shown.</summary>
    public static void EnsureToneFitsPart(Integra7Snapshot snapshot, int zeroBasedPartNo,
        string currentToneType)
    {
        if (snapshot.Kind != SnapshotKinds.Tone)
            throw new SnapshotFormatException($"This file holds \"{snapshot.Kind}\", not a tone.");

        // FromJson enforces this for a loaded file; a snapshot built in code has not been through it, and
        // the block list cannot be built without a recognised engine either way.
        if (snapshot.ToneType is null || !ToneDomainNames.IsKnownToneType(snapshot.ToneType))
            throw new SnapshotFormatException(
                $"This tone snapshot names no tone type this build recognises (\"{snapshot.ToneType}\").");

        if (snapshot.ToneType != currentToneType)
            throw new SnapshotFormatException(
                $"This snapshot holds a {snapshot.ToneType} tone, but part {zeroBasedPartNo + 1} currently " +
                $"holds a {currentToneType} tone. Select a {snapshot.ToneType} preset on that part first, " +
                $"then load the snapshot again.");
    }

    public static async Task RestoreToneAsync(Integra7Domain domain, Integra7Snapshot snapshot,
        int zeroBasedPartNo, string currentToneType, IMidiLease lease)
    {
        EnsureToneFitsPart(snapshot, zeroBasedPartNo, currentToneType);

        var targets = ToneDomainNames.For(snapshot.ToneType!, zeroBasedPartNo);

        // Matched on the two offsets alone: they name the engine and the block within it, and nothing
        // about which part the tone was captured from.
        var byOffsets = new Dictionary<(string Offset, string Offset2), SnapshotDomain>();
        foreach (var block in snapshot.Domains)
            if (!byOffsets.TryAdd((block.Offset, block.Offset2), block))
                throw new SnapshotFormatException(
                    $"This tone snapshot lists block (\"{block.Offset}\", \"{block.Offset2}\") more than " +
                    $"once, so there is no telling which one to restore.");

        // Both directions, before anything is read or written. A snapshot block with no target would be
        // silently dropped; a target with no snapshot block would be left holding whatever the part had
        // before, which is a tone that is part one patch and part another -- worse than refusing outright.
        foreach (var block in snapshot.Domains)
            if (!targets.Any(t => t.Offset == block.Offset && t.Offset2 == block.Offset2))
                throw new SnapshotFormatException(
                    $"This tone snapshot contains a block that is not part of a {snapshot.ToneType} tone: " +
                    $"(\"{block.Offset}\", \"{block.Offset2}\").");
        foreach (var t in targets)
            if (!byOffsets.ContainsKey((t.Offset, t.Offset2)))
                throw new SnapshotFormatException(
                    $"This tone snapshot is missing block (\"{t.Offset}\", \"{t.Offset2}\"), so it would " +
                    $"restore only part of the tone.");

        // Validated against the blocks this restore will really write to -- the re-targeted ones -- not
        // against whatever part the file was captured from. Up front, before any read or write, for the
        // reason ValidateParametersAreKnown gives: a mismatch must be refused wholesale rather than leave
        // the part half restored.
        foreach (var t in targets)
            ValidateParametersAreKnownIn(domain.GetDomain(t.Start, t.Offset, t.Offset2),
                byOffsets[(t.Offset, t.Offset2)].Values);

        // In target order, which is ToneDomainNames' address order and therefore the order the capture
        // wrote them in. Taking it from the target list rather than from the file means a hand-reordered
        // file still restores in the canonical order.
        foreach (var (start, offset, offset2) in targets)
            try
            {
                var d = domain.GetDomain(start, offset, offset2);

                // See ApplyBlockValues for why the block is read before being applied to. No settle delay
                // anywhere in this loop, unlike RestoreAsync: none of these blocks selects a tone, they
                // *are* the tone, so nothing here starts the device loading a patch.
                if (!await d.ReadFromIntegraAsync(lease))
                    throw new SnapshotFormatException(
                        $"Could not restore the tone: the device did not answer for block " +
                        $"(\"{start}\", \"{offset}\", \"{offset2}\").");
                ApplyBlockValues(d, byOffsets[(offset, offset2)].Values);
                await d.WriteToIntegraAsync(lease);
            }
            catch (Exception e) when (e is not SnapshotFormatException)
            {
                throw new SnapshotFormatException(
                    $"Restoring the tone failed on block (\"{start}\", \"{offset}\", \"{offset2}\").", e);
            }

        Log.Information(
            "Sent {ToneType} tone snapshot {Name} ({BlockCount} blocks) to part {Part}. The device does " +
            "not acknowledge parameter writes, so this confirms the data was sent, not that it was applied.",
            snapshot.ToneType, snapshot.Name, targets.Count, zeroBasedPartNo + 1);
    }
}
