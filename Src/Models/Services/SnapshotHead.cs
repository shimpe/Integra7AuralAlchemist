using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a library list needs from a snapshot file: everything except the parameter data.
///
/// <b>Why this exists.</b> Listing a library means touching every file in the folder, and a snapshot file
/// is almost entirely parameter values: a Studio Set is around 1,500 of them across 53 blocks, a drum kit
/// 92 blocks' worth. <see cref="Integra7Snapshot.FromJson"/> turns all of those into
/// <see cref="SnapshotValue"/> records, two strings each -- exactly the work a list has no use for,
/// multiplied by the number of files in the folder. <c>JsonDocument.Parse</c> would be no better; it
/// materialises the whole document as a tree first. So <see cref="TryRead"/> walks the file forward with a
/// <see cref="Utf8JsonReader"/>, reads the handful of top-level properties it wants, and <b>skips the
/// parameter data without interpreting a single value</b>: no strings, no records, no lists, no tree.
/// Skipping is a scan of the bytes rather than a parse of them.
///
/// What it does not avoid is reading the file off the disk -- the bytes are buffered so the reader can work
/// over one span, which is also what lets it skip a block of values in a single call. If a very large
/// library on a slow drive ever makes that felt, the fix is a chunked reader that stops early on the normal
/// metadata-first file; it is deliberately not done here, because it would trade this class's one obvious
/// code path for the buffer-growth dance, and the parse was the expensive half.
///
/// <b>What it must not do: validate.</b> <c>FromJson</c> is where a file is judged, and it stays the only
/// place. A file whose rating is 7 still appears in the list, and fails when it is <i>opened</i>, with the
/// message <c>FromJson</c> already gives. The alternative -- refusing it here too -- means two places that
/// have to agree about what a good file is, and the failure mode when they do not agree is a file the user
/// can see in the folder and not in the application, with nothing anywhere saying why. So the only
/// judgement this type makes is the one it cannot avoid: whether the file is a snapshot at all.
///
/// The fields, and their defaults, are deliberately the ones <see cref="Integra7Snapshot"/> would end up
/// with for the same file -- an absent <c>Kind</c> is a Studio Set, an absent category is empty, an absent
/// rating is 0. That is not tidiness: the list has to show what opening the file will show, and a head that
/// defaulted differently would put an entry in one kind's filter and then open as the other.
///
/// The format version is read but deliberately not carried. It is used to recognise the file (see
/// <see cref="TryRead"/>) and nothing in a list is a place to show it -- a file of the wrong version is
/// listed like any other and refused, by version, when it is opened.</summary>
public sealed record SnapshotHead(string Name, string Kind, string? ToneType, string Category,
    IReadOnlyList<string> Tags, string Notes, int Rating, bool Favourite)
{
    /// <summary>The head of a snapshot, or null if <paramref name="json"/> does not hold one.
    ///
    /// <b>How "not a snapshot" is decided.</b> A library folder is a folder, and a user can put anything in
    /// it; the rule is that a stray file is skipped rather than thrown over. Three things are not snapshots:
    /// something that is not JSON at all, something that is JSON but not an object, and a JSON object that
    /// carries no <c>FormatVersion</c> number. That last one is the identity check, and it is the only one
    /// available that does not slide into validating: every snapshot this application has ever written says
    /// which format version it is, and nobody else's JSON does. Note that the *value* is not checked -- a
    /// version 2 file is still a snapshot, and still gets listed, and still fails when it is opened, with
    /// <c>FromJson</c> naming the version it found. Presence identifies the file; what the version says is
    /// somebody else's business.
    ///
    /// Everything past that gate is read best-effort. A property whose value is the wrong shape --
    /// <c>"Rating": "high"</c>, a tag that is not text -- reads as nothing said for that one field rather
    /// than sinking the whole entry, so the file is still listed and still gets refused, by name, at the
    /// moment the user opens it. A structurally broken document is a different matter: there is no head to
    /// be read out of it at all, so it is not a snapshot.
    ///
    /// A property named twice is not refused either, and the last one wins. That is one more thing the
    /// converter does refuse and this does not, for the same reason as the rest: the file will not open, and
    /// it is better for the entry the user is looking at to be the one that says so.
    ///
    /// <b>An I/O failure is not "not a snapshot", and is not caught here.</b> The caller had to open the
    /// file to call this, so it already has to handle a locked or vanished one; catching
    /// <see cref="IOException"/> in here as well would turn a disk problem into a library entry that is
    /// silently absent, and would be a second place handling a failure that already has a first.</summary>
    public static SnapshotHead? TryRead(Stream json)
    {
        using var buffer = new MemoryStream();
        json.CopyTo(buffer);

        try
        {
            // GetBuffer rather than ToArray: the array is already ours, and copying it a second time would
            // double the one cost this type cannot avoid. The parameterless MemoryStream constructor makes
            // its buffer publicly visible, so this is the documented use of it rather than a trick.
            return ReadHead(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (JsonException)
        {
            // Not JSON, or JSON that stops in the middle. Either way there is no head in it.
            return null;
        }
    }

    private static SnapshotHead? ReadHead(ReadOnlySpan<byte> utf8)
    {
        // A leading byte-order mark comes off first. Utf8JsonReader does not skip one, and an editor that
        // re-saved a snapshot may well have added one -- see ByteOrderMark, which is also what
        // Integra7Snapshot.FromJson calls, so that a marked file cannot be listed here and then refused
        // there.
        var reader = new Utf8JsonReader(ByteOrderMark.SkipIn(utf8));

        // An empty or whitespace-only file lands here: with no more data, and this being the final block,
        // Read simply answers false rather than throwing.
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

        // The identity gate, and then the defaults, which are Integra7Snapshot's own -- see above.
        var isSnapshot = false;
        var name = "";
        var kind = SnapshotKinds.StudioSet;
        string? toneType = null;
        var category = "";
        IReadOnlyList<string> tags = [];
        var notes = "";
        var rating = 0;
        var favourite = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            // Utf8JsonReader guarantees a property name inside an object it has not left, so reaching
            // anything else here would mean the reader itself was wrong. Answering "not a snapshot" rather
            // than asserting keeps the promise this method makes about never throwing over a file's content.
            if (reader.TokenType != JsonTokenType.PropertyName) return null;

            var property = reader.GetString()!;
            reader.Read();

            switch (property)
            {
                case nameof(Integra7Snapshot.FormatVersion):
                    isSnapshot = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out _);
                    break;
                case nameof(Integra7Snapshot.Name):
                    // A file with a null name is one FromJson refuses as missing its contents. It is still
                    // listed, with a blank name, which is a good deal more use than not being there: the
                    // user can see the file, select it, and be told what is wrong with it.
                    name = ReadText(ref reader) ?? "";
                    break;
                case nameof(Integra7Snapshot.Kind):
                    // Absent means Studio Set, which is the record's own load-bearing default. A Kind
                    // written as null gets the same treatment, for the same reason: it is a file FromJson
                    // will refuse, and until then the honest thing to show in a kind column is the default
                    // the rest of the application would apply.
                    kind = ReadText(ref reader) ?? SnapshotKinds.StudioSet;
                    break;
                case nameof(Integra7Snapshot.ToneType):
                    toneType = ReadText(ref reader);
                    break;
                case nameof(Integra7Snapshot.Category):
                    category = ReadText(ref reader) ?? "";
                    break;
                case nameof(Integra7Snapshot.Tags):
                    tags = ReadTags(ref reader);
                    break;
                case nameof(Integra7Snapshot.Notes):
                    notes = ReadText(ref reader) ?? "";
                    break;
                case nameof(Integra7Snapshot.Rating):
                    // Read as it stands, in range or not. A seven-star entry belongs in the list -- see the
                    // notes above on why this is not the place that judges a file.
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out rating))
                    {
                        reader.Skip();
                        rating = 0;
                    }

                    break;
                case nameof(Integra7Snapshot.Favourite):
                    if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                        favourite = reader.TokenType == JsonTokenType.True;
                    else
                        reader.Skip();
                    break;
                default:
                    // "Blocks", and any property a later build writes that this one has never heard of.
                    // Skip walks to the end of whatever the value is -- an object of 92 blocks, an array, a
                    // number -- without interpreting anything inside it, which is the whole point of this
                    // type. It is also what makes the metadata-first layout an optimisation rather than a
                    // requirement: a hand-edited file that puts "Blocks" first is read by this same path,
                    // stepped over, and its metadata found after it. Nothing here assumes an order, because
                    // JSON does not promise one and a file we did not write is not obliged to follow our
                    // convention.
                    reader.Skip();
                    break;
            }
        }

        return isSnapshot
            ? new SnapshotHead(name, kind, toneType, category, tags, notes, rating, favourite)
            : null;
    }

    /// <summary>A text property's value, or null for a JSON null -- and also for anything that is not text
    /// at all, which is then consumed so that the walk stays in step. <see cref="Utf8JsonReader.Skip"/> is
    /// a no-op on a scalar and walks the whole thing for an object or an array, so one call covers both;
    /// not making it is how a reader ends up inside a value it meant to step over, reading that value's
    /// members as if they were the file's own properties.</summary>
    private static string? ReadText(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        if (reader.TokenType != JsonTokenType.Null) reader.Skip();
        return null;
    }

    /// <summary>The tags, as text. A tag that is not text is dropped rather than refused -- the opposite of
    /// what the converter does with one, and deliberately so: the converter is deciding whether to hand a
    /// caller a tag list with a null in it, while this is deciding whether an otherwise readable file
    /// appears in a list at all. Dropping it loses nothing the user can act on, and the file still fails,
    /// saying so, when it is opened.</summary>
    private static IReadOnlyList<string> ReadTags(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return [];
        }

        List<string> tags = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            if (reader.TokenType == JsonTokenType.String) tags.Add(reader.GetString()!);
            else reader.Skip();

        return tags;
    }
}
