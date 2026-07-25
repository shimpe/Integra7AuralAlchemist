using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter and the value it displayed when the snapshot was taken.</summary>
public sealed record SnapshotValue(string Path, string Value);

/// <summary>One parameter block, identified by the three address names that resolve it back to a
/// live domain. Values are an ordered list, not a map: restoring has to set a discriminator before
/// the parameters that only exist because of it, and address order gives exactly that.</summary>
public sealed record SnapshotDomain(string Start, string Offset, string Offset2, List<SnapshotValue> Values);

/// <summary>A complete Studio Set as displayed values. Pure data — no Avalonia, no MIDI.
///
/// Known limitation of format version 1: values are stored as the strings the UI displayed, not the
/// raw bytes the device holds. Restoring goes through
/// <c>DisplayValueToRawValueConverter.UpdateFromDisplayedValue</c>, which looks the display string up
/// in the current build's enum representation and silently falls back to raw 0 in its
/// <c>key.Count == 0</c> branch when the string is not found. So a snapshot captured on one build and
/// restored on another whose parameter database renamed or reordered an enum string for some
/// parameter converts that one parameter to 0 instead of failing loudly. The real fix is storing the raw
/// value alongside the display string and adding an inverse converter that can restore from either; that
/// needs a converter that does not exist yet, so it is left for a later format version rather than
/// attempted here.</summary>
public sealed record StudioSetSnapshot(int FormatVersion, string Name, List<SnapshotDomain> Domains)
{
    public const int CurrentFormatVersion = 1;

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
        // NOTE: every new field these records gain needs a null check added here too.
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
