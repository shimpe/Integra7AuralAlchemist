using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter, the value it displayed when the snapshot was taken, and -- from format
/// version 2 on -- the raw value the device actually stores for it.
///
/// <paramref name="Raw"/> is nullable and defaults to null, which carries real meaning rather than
/// being a convenience: a version 1 file has no such JSON property at all and deserializes to null,
/// and a text parameter has no raw form even in version 2. Null therefore reads as "this value has no
/// raw form on file, restore it from the string", which is the correct handling of both.</summary>
public sealed record SnapshotValue(string Path, string Value, long? Raw = null);

/// <summary>One parameter block, identified by the three address names that resolve it back to a
/// live domain. Values are an ordered list, not a map: restoring has to set a discriminator before
/// the parameters that only exist because of it, and address order gives exactly that.</summary>
public sealed record SnapshotDomain(string Start, string Offset, string Offset2, List<SnapshotValue> Values);

/// <summary>A complete Studio Set. Pure data — no Avalonia, no MIDI.
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
/// Two cases remain, both recorded rather than fixed. A text parameter (a name) has no raw form at all --
/// its value IS the string -- so it still carries no <c>Raw</c> and is still restored from the string,
/// which for it is exactly right and not an exposure. And a version 1 file already on disk still has no
/// raw values in it, so restoring one still runs the old path with the old exposure; that is fixed the
/// moment the user re-captures, and version 1 must keep loading regardless, because those files
/// exist.</summary>
public sealed record StudioSetSnapshot(int FormatVersion, string Name, List<SnapshotDomain> Domains)
{
    public const int CurrentFormatVersion = 2;

    /// <summary>Every version this build can read. Version 1 files exist on the user's disk and must
    /// keep restoring exactly as they always did, so this is a set, not an equality check.</summary>
    private static readonly int[] SupportedFormatVersions = [1, CurrentFormatVersion];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Indented deliberately: these files are meant to be read and diffed.</summary>
    public static string ToJson(StudioSetSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static StudioSetSnapshot FromJson(string json)
    {
        StudioSetSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<StudioSetSnapshot>(json, Options);
        }
        catch (JsonException e)
        {
            throw new SnapshotFormatException("This file is not a Studio Set snapshot.", e);
        }

        if (snapshot is null)
            throw new SnapshotFormatException("This file is empty.");
        if (Array.IndexOf(SupportedFormatVersions, snapshot.FormatVersion) < 0)
            throw new SnapshotFormatException(
                $"This snapshot is format version {snapshot.FormatVersion}; this build reads " +
                $"version {string.Join(" and ", SupportedFormatVersions)}.");

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
        // optional by design. A version 1 file has no Raw property at all and a version 2 one omits it
        // for text parameters, so null there is not a gap -- it is the documented "no raw value on
        // file, restore from the string". Adding it here would reject every file this build has to
        // read.
        if (snapshot.Name is null || snapshot.Domains is null or { Count: 0 } ||
            snapshot.Domains.Exists(d => d.Start is null || d.Offset is null || d.Offset2 is null
                                          || d.Values is null || d.Values.Exists(v => v.Path is null || v.Value is null)))
            throw new SnapshotFormatException("This snapshot file is missing its contents.");
        return snapshot;
    }
}

/// <summary>A snapshot file that cannot be read. Carries a message meant for the user.</summary>
public sealed class SnapshotFormatException : Exception
{
    public SnapshotFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
