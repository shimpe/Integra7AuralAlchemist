using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A snapshot reduced to what a duplicate comparison needs.</summary>
/// <param name="Kind">Tone or Studio Set. Part of the bucket key, so the two never pair.</param>
/// <param name="ToneType">The engine, or null for a Studio Set.</param>
/// <param name="Values">Every raw value, in document order, reserved ones left out.</param>
public sealed record RawVector(string Kind, string? ToneType, long[] Values);

/// <summary>A snapshot as a packed list of raw values, for deciding whether two files are the same sound.
///
/// <b>The raw half of a leaf, not the displayed one</b> -- the opposite half from
/// <see cref="SnapshotTextScan"/>, and for the opposite reason. The raw value is what the device stores,
/// so it is what "the same sound" is made of; the display string is a rendering of it that a build can
/// rename without a note changing. A text parameter, which is a bare string and has no raw half at all, is
/// left out entirely: a name is something said about a sound rather than part of it, and leaving it out is
/// what makes two files differing only in their names duplicates instead of strangers.
///
/// <b>Positional comparison is only sound because the sequence is fixed.</b> Two files of the same engine
/// yield vectors that line up position by position, because the writer emits blocks and parameters in a
/// fixed order, text parameters are always absent and reserved ones always excluded. Everything downstream
/// rests on that sentence: it is what lets a comparison be a walk over two <c>long[]</c> instead of a match
/// on paths, and it is why no path is stored per patch at all -- which is the difference between a cache of
/// a few megabytes and one that repeats every parameter name once per file.
///
/// <b>Built like <see cref="SnapshotHead"/> and like <see cref="SnapshotTextScan"/></b>: a forward-only
/// walk that takes one primitive per parameter and materialises nothing. Where the head reader skips
/// <c>Blocks</c> whole, this one walks into it -- and, as the text scan does, tracks a name per level
/// rather than fixing on one depth, because a block's parameters are not all at the same depth: the writer
/// nests them by the parameter path's own '/', so a tone's effect settings sit a level below its plain
/// ones.
///
/// <b>It answers null rather than throwing</b> for anything that is not a snapshot -- the same contract the
/// listing has, because a library folder is a folder and the user can put anything in it. Unlike the text
/// scan, it insists on the identity check <see cref="SnapshotHead"/> makes, a <c>FormatVersion</c> being
/// present: a file that is not a snapshot would otherwise contribute an empty vector, and empty vectors are
/// all equal to one another, so every stray file in the folder would come back as one large group of
/// duplicates.</summary>
public static class SnapshotRawVector
{
    /// <summary>The vector of <paramref name="json"/>, or null if it does not hold a snapshot.</summary>
    public static RawVector? Read(Stream json)
    {
        using var buffer = new MemoryStream();
        json.CopyTo(buffer);

        try
        {
            return ReadVector(ByteOrderMark.SkipIn(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RawVector? ReadVector(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

        var isSnapshot = false;
        // The head reader's default, and for its reason: a file that says nothing about its kind is a
        // Studio Set, and a vector that bucketed it as anything else would put it in a group the list says
        // it is not in.
        var kind = SnapshotKinds.StudioSet;
        string? toneType = null;
        List<long> values = [];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) return null;

            var property = reader.GetString()!;
            reader.Read();

            switch (property)
            {
                case nameof(Integra7Snapshot.FormatVersion):
                    isSnapshot = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out _);
                    break;
                case nameof(Integra7Snapshot.Kind):
                    kind = ReadText(ref reader) ?? SnapshotKinds.StudioSet;
                    break;
                case nameof(Integra7Snapshot.ToneType):
                    toneType = ReadText(ref reader);
                    break;
                case "Blocks":
                    Collect(ref reader, values);
                    break;
                default:
                    // The metadata the bucket key does not need, and anything a later build writes. None of
                    // it is part of the sound -- see above on why a name in particular is not.
                    reader.Skip();
                    break;
            }
        }

        return isSnapshot ? new RawVector(kind, toneType, [.. values]) : null;
    }

    /// <summary>A text property's value, or null for anything that is not text -- which is then consumed,
    /// so that the walk stays in step. The head reader's helper, for the two fields both of them read.
    /// </summary>
    private static string? ReadText(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        if (reader.TokenType != JsonTokenType.Null) reader.Skip();
        return null;
    }

    /// <summary>Every raw value inside Blocks, in document order.</summary>
    private static void Collect(ref Utf8JsonReader reader, List<long> values)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return;

        // Depth is counted rather than the levels being named, because naming them would be a second place
        // that has to agree with the writer about how deep the nesting is: three levels of address, then
        // the block, then its parameters -- and then, for an effect parameter, one more.
        var blocksDepth = reader.CurrentDepth;
        var blockDepth = blocksDepth + 4;

        // The parameter path being walked, outermost first: the block name, then any containers, then the
        // leaf. Kept as its parts rather than joined, since the only question asked of it is whether any
        // part of it is reserved and nothing here has a use for the string.
        List<string> path = [];

        while (reader.Read())
        {
            // Out of Blocks entirely. Every End token inside it is deeper than the object itself.
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth <= blocksDepth) return;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            // The three address levels, which say which part a tone was captured from. They are deliberately
            // no part of the vector: SnapshotDiff matches blocks without them for the same reason, since a
            // tone captured from part 3 is the same sound as the identical tone in part 5.
            if (reader.CurrentDepth < blockDepth) continue;

            var level = reader.CurrentDepth - blockDepth;
            if (path.Count > level) path.RemoveRange(level, path.Count - level);
            path.Add(reader.GetString()!);

            reader.Read();
            // A container rather than a leaf: descend into it, and its children land one level down.
            if (reader.TokenType == JsonTokenType.StartObject) continue;

            // The value is read whatever then happens to it. Testing first and reading second would leave
            // the reader inside a pair it decided to drop, and every later parameter out of step.
            var raw = RawOf(ref reader);
            if (raw is { } value && !IsReserved(path)) values.Add(value);
        }
    }

    /// <summary>The raw half of a leaf, or null when it has none -- a bare string is a text parameter,
    /// whose value is its name.</summary>
    private static long? RawOf(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) return null;

        long? raw = null;
        reader.Read();
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var first)) raw = first;

        // Whatever else the pair held, the reader is left at the end of it.
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
            reader.Read();
        }

        return raw;
    }

    /// <summary>Whether this is one of the instrument's unused slots, which a duplicate check must not
    /// count. They are in the file on purpose -- a block is bulk-written as one transmission and every byte
    /// of it has to be there -- and a difference in one says the device left something else in a byte it
    /// does not read, which is not a different sound.
    ///
    /// <b>This must agree with <see cref="SnapshotDiff"/>'s rule exactly</b>, which is
    /// <c>path.Contains("Reserved")</c> over the whole path; its remarks record the three name shapes that
    /// covers and why the name rather than the database's own flag decides. Testing each part separately is
    /// the same question asked of the same characters, since '/' cannot be part of the word and so no
    /// occurrence can straddle a join. If the two ever disagreed, a pair the report calls identical could
    /// be one this refuses to group, with nothing anywhere saying why.</summary>
    private static bool IsReserved(List<string> path)
    {
        foreach (var segment in path)
            if (segment.Contains("Reserved", StringComparison.Ordinal))
                return true;

        return false;
    }
}
