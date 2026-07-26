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
/// silently restoring through the weaker path.
///
/// Format version 3 keeps every one of those decisions and changes only the shape on disk: the values
/// nest by the three address names and then by the parameter path's own '/', which makes a Studio Set
/// file about a third of its former size and makes a one-parameter change a one-line diff under a
/// heading that says where it is. <see cref="SnapshotJsonConverter"/> is that shape and carries the
/// reasoning for it; this record and everything that consumes it are unchanged, which is what made the
/// change a small one. Version 2 is refused for the same reason version 1 is, minus the danger: no
/// version 2 file was ever released either, so there is nothing to stay compatible with, and a build
/// that reads one shape should say so rather than read half a file.
///
/// Version 3 also carries the metadata a library needs -- a category, tags, notes, a rating and a
/// favourite flag -- in the file itself rather than in a sidecar or one index, so that a file carries its
/// own notes when it is copied or sent, there is one thing to back up, and nothing goes stale when files
/// are added or removed outside the application. All five are optional and all five default to "nothing
/// said", so a version 3 file that carries none of them -- which is every one written before this, all of
/// them on the machine this was built on -- still reads correctly. That is why adding them did not move
/// the format version again: there is no file anywhere that this build would read differently than the
/// build that wrote it meant.</summary>
/// <param name="Kind">What this file holds -- one of <see cref="SnapshotKinds"/>. Defaults to
/// <see cref="SnapshotKinds.StudioSet"/>, and that default is load-bearing rather than a convenience:
/// a file written before tones existed carries no <c>Kind</c> property at all, System.Text.Json fills
/// the missing constructor parameter with this default, and a Studio Set is exactly what such a file
/// is. Do not change it.</param>
/// <param name="ToneType">Which engine a tone snapshot came from -- one of the five keys
/// <c>ToneDomainNames.IsKnownToneType</c> accepts. Null for a Studio Set, where there is no single
/// engine to name.</param>
/// <param name="Category">One of the instrument's own tone categories, as <c>Integra7Preset</c>
/// parses them -- "Ac.Piano", "Organ", "Synth Lead" and the rest. Stored as the string rather than as
/// <c>EnumCategory</c> for the same reason <see cref="SnapshotKinds"/> stores strings: it is written into
/// the file verbatim, and a string survives that enum gaining, losing or reordering a member while
/// staying readable in the file, which is the point of the format. Empty for a Studio Set, which is
/// sixteen parts each with its own and has no single category to name.</param>
/// <param name="Tags">Free text, for what a fixed vocabulary cannot say -- "for the trio gig", "needs
/// less bark". Nullable and defaulting to null because a defaulted parameter has to be a constant
/// expression and an empty list is not one; read it through <see cref="TagList"/>, which is what makes
/// that an implementation detail rather than every caller's problem.</param>
/// <param name="Notes">Whatever the user wants to remember about this sound. Never interpreted.</param>
/// <param name="Rating">0 to 5 stars, 0 meaning unrated.</param>
/// <param name="Favourite">Set by hand, and independent of the rating: a sound can be a favourite
/// without being the best thing in the library.</param>
public sealed record Integra7Snapshot(
    int FormatVersion,
    string Name,
    List<SnapshotDomain> Domains,
    string Kind = SnapshotKinds.StudioSet,
    string? ToneType = null,
    string Category = "",
    List<string>? Tags = null,
    string Notes = "",
    int Rating = 0,
    bool Favourite = false)
{
    public const int CurrentFormatVersion = 3;

    /// <summary>Never null, whatever the file said. A file written by hand may carry no Tags property at
    /// all, and the converter then passes the record's own default, so every reader would otherwise need
    /// the same null check -- and the one that gets forgotten is a crash while listing a folder, which is
    /// the one place this application has no business failing.</summary>
    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>The converter is what decides the shape of the file, including that the parameter data is
    /// written last. Registered here rather than by attribute so that both directions go through the same
    /// options object and cannot drift apart.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new SnapshotJsonConverter() },
    };

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
        // Version 3's shape makes some of these unreachable from a file rather than unnecessary, and they
        // stay for that reason. An address name is now an object key, and a JSON object key cannot be
        // null, so a block with a null Start, Offset or Offset2 can no longer be written by hand; the same
        // goes for a null parameter path, which is now a key too, and for a null Values, which the reader
        // always builds as a list. What that leaves reachable is a null Name, an absent or empty Blocks,
        // and a null value (`"Studio Set Tempo": null`), which the converter deliberately keeps as a null
        // rather than dropping so that this one condition is still what refuses it. Deleting the now-
        // structural halves would save one expression and cost the property that this single check is
        // where a snapshot's contents are judged, whatever shape a future version writes them in.
        // The five metadata fields -- Category, Tags, Notes, Rating and Favourite -- are absent from it
        // for the same reason SnapshotValue.Raw is: they are optional by design. A snapshot saved before
        // the library existed carries none of them, and a file written by hand is not obliged to either;
        // "nothing said" is a perfectly good answer for all five and is what their defaults mean. So the
        // rule above -- every new *required* field needs a null check here -- does not reach them, and
        // adding one would reject every snapshot already on disk to no purpose. What replaces it is that
        // none of the five can be null by the time a reader sees it: Rating and Favourite are value types,
        // Category and Notes are coalesced to empty by the converter (which keeps a null Kind, precisely
        // because a null Kind decides where blocks get applied and a null note decides nothing), and Tags
        // is read through TagList. Their contents are still judged, below -- a rating has a range, and a
        // range is not a null check.
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

        // The star control cannot produce a rating outside the range, but a hand-edited file can, and a
        // filter for five-star sounds that silently skipped a seven-star entry would be a puzzle rather
        // than an error -- the file is in the folder, it says it is the best thing there, and it does not
        // appear. Refusing it names the problem at the one moment the user is looking at that file.
        if (snapshot.Rating is < 0 or > 5)
            throw new SnapshotFormatException(
                $"This snapshot's rating is {snapshot.Rating}; ratings run from 0 to 5.");

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
