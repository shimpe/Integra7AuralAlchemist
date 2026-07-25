using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter, the value it displayed when the snapshot was taken, and the raw value the
/// device actually stores for it.
///
/// <paramref name="Raw"/> is nullable because a text parameter has no raw form -- its value IS the
/// string. Null therefore reads as "this value has no raw form, restore it from the string", which
/// for a name is exactly right rather than a fallback.</summary>
public sealed record SnapshotValue(string Path, string Value, long? Raw = null);

/// <summary>One parameter block, identified by the three address names that resolve it back to a
/// live domain. Values are an ordered list, not a map: restoring has to set a discriminator before
/// the parameters that only exist because of it, and address order gives exactly that.</summary>
public sealed record SnapshotDomain(string Start, string Offset, string Offset2, List<SnapshotValue> Values);

/// <summary>A complete Studio Set, or a single tone. Pure data — no Avalonia, no MIDI. Named for the
/// instrument rather than for either of them because the shape (a version, a name, an ordered list of
/// address-identified blocks) is the same for both; <see cref="Kind"/> says which one a given file is.
///
/// Format version 2 records, for every numeric and discrete parameter, the raw value the device stores
/// alongside the string the UI displayed. The string is what makes these files readable and diffable,
/// which was the point of the format; the raw value is what a restore actually applies, because it is
/// the value the device holds and it is stable across builds in a way a display string is not.
///
/// That is why version 2 exists. Version 1 stored the display string only, and restoring went through
/// <c>DisplayValueToRawValueConverter.UpdateFromDisplayedValue</c>, which looks the display string up in
/// the current build's enum representation and, in its <c>key.Count == 0</c> branch, silently falls back
/// to raw 0 <i>and still assigns the unmatched string to <c>StringValue</c></i> -- and in a Release build
/// the diagnostic on that branch is compiled out entirely. So a snapshot captured on one build and
/// restored on another whose parameter database renamed or reordered an enum string did not merely zero
/// that one parameter. If the parameter is a discriminator, the unmatched string is what the parser
/// context then holds, no variant of any group that depends on it is valid, and the block's bulk write
/// assembles fewer bytes than the block occupies -- which, as one DT1 at the block's base address, would
/// land every later parameter at the wrong address. So the blast radius was the remainder of the block,
/// not one parameter. <c>FullyQualifiedParameterRange.WriteToIntegraAsync</c> compares the assembled
/// length against the block's computed size and refuses to transmit when they disagree, so that fails
/// loudly instead of corrupting -- but the restore still fails.
///
/// One case remains, recorded rather than fixed: a text parameter (a name) has no raw form at all --
/// its value IS the string -- so it carries no <c>Raw</c> and is restored from the string, which for it
/// is exactly right and not an exposure.
///
/// Version 1 is deliberately NOT read. It stored no raw values, so restoring one would run the old
/// path with the old exposure, and no version 1 file was ever released -- the format changed while the
/// feature was still being verified. Refusing with a message that names the version is better than
/// silently restoring through the weaker path.</summary>
/// <param name="Kind">What this file holds -- one of <see cref="SnapshotKinds"/>. Defaults to
/// <see cref="SnapshotKinds.StudioSet"/>, and that default is load-bearing rather than a convenience:
/// a file written before tones existed carries no <c>Kind</c> property at all, System.Text.Json fills
/// the missing constructor parameter with this default, and a Studio Set is exactly what such a file
/// is. Do not change it.</param>
/// <param name="ToneType">Which engine a tone snapshot came from -- one of the five keys
/// <c>ToneDomainNames.IsKnownToneType</c> accepts. Null for a Studio Set, where there is no single
/// engine to name.</param>
public sealed record Integra7Snapshot(
    int FormatVersion,
    string Name,
    List<SnapshotDomain> Domains,
    string Kind = SnapshotKinds.StudioSet,
    string? ToneType = null)
{
    public const int CurrentFormatVersion = 2;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Indented deliberately: these files are meant to be read and diffed.</summary>
    public static string ToJson(Integra7Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static Integra7Snapshot FromJson(string json)
    {
        Integra7Snapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Integra7Snapshot>(json, Options);
        }
        catch (JsonException e)
        {
            throw new SnapshotFormatException("This file is not an INTEGRA-7 snapshot.", e);
        }

        if (snapshot is null)
            throw new SnapshotFormatException("This file is empty.");
        if (snapshot.FormatVersion != CurrentFormatVersion)
            throw new SnapshotFormatException(
                $"This snapshot is format version {snapshot.FormatVersion}; this build reads version {CurrentFormatVersion}.");

        // System.Text.Json silently passes `default` for any constructor parameter with no matching
        // JSON property, so a truncated or hand-edited file can deserialize "successfully" into a
        // snapshot with nulls (or an empty Domains) anywhere none of these types declare nullable.
        // Restore feeds these fields straight into GetDomain(Start, Offset, Offset2) and
        // ModifySingleParameterDisplayedValue(Path, Value) without checking them, and neither of
        // those fail loudly: an unresolvable address logs and silently falls back to an unrelated
        // domain, a null path is a silent no-op, and a null value only throws for some parameter
        // kinds and otherwise writes stale or zero data. So a gap here isn't a crash to catch — it's
        // wrong data reaching the instrument with nothing to say so. Reject it here instead.
        // NOTE: every new *required* field these records gain needs a null check added here too.
        // SnapshotValue.Raw is deliberately absent from this condition and must stay absent: it is
        // optional by design: a text parameter has no raw form, so a null there is not a gap -- it is
        // the documented "no raw value, restore from the string". Adding it here would reject every
        // file that names a Studio Set.
        // Kind and ToneType are absent from it for a different reason, and must also stay absent.
        // Kind's whole point is its default: a file written before tones existed has no Kind property,
        // so `default` -- SnapshotKinds.StudioSet -- is what makes it still read as the Studio Set it
        // is. Requiring the property here would reject every file already on disk. ToneType is null for
        // a Studio Set by design. Both are instead checked below, against the Kind they belong to,
        // where "missing" and "wrong for this kind" can be told apart and reported as such.
        if (snapshot.Name is null || snapshot.Domains is null or { Count: 0 } ||
            snapshot.Domains.Exists(d => d.Start is null || d.Offset is null || d.Offset2 is null
                                          || d.Values is null || d.Values.Exists(v => v.Path is null || v.Value is null)))
            throw new SnapshotFormatException("This snapshot file is missing its contents.");

        // An unrecognised Kind means a file from a build that knows a kind this one does not. Restoring
        // it as whatever this build assumes would apply its blocks somewhere they do not belong, so
        // refuse and name what was found.
        if (snapshot.Kind is not (SnapshotKinds.StudioSet or SnapshotKinds.Tone))
            throw new SnapshotFormatException(
                $"This snapshot says it holds \"{snapshot.Kind}\", which this build does not recognise.");

        // A tone snapshot's engine decides which blocks it is made of and which part layout it can be
        // restored into. Without it there is nothing to restore the file to -- and an engine this build
        // has never heard of is no better than none, because the block list for it cannot be built.
        if (snapshot.Kind == SnapshotKinds.Tone &&
            (snapshot.ToneType is null || !ToneDomainNames.IsKnownToneType(snapshot.ToneType)))
            throw new SnapshotFormatException(
                $"This tone snapshot names no tone type this build recognises (\"{snapshot.ToneType}\").");

        return snapshot;
    }
}

/// <summary>What an <see cref="Integra7Snapshot"/> holds. Strings rather than an enum because they are
/// written into the file verbatim: a string survives an enum gaining, losing or reordering members, and
/// it is readable in the file, which is the point of the format. Lower case and hyphenated so it looks
/// like the data it is rather than like a leaked .NET identifier.</summary>
public static class SnapshotKinds
{
    public const string StudioSet = "studio-set";
    public const string Tone = "tone";
}

/// <summary>A snapshot file that cannot be read. Carries a message meant for the user.</summary>
public sealed class SnapshotFormatException : Exception
{
    public SnapshotFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
