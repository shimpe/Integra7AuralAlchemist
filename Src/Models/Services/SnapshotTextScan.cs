using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Whether any parameter of a snapshot reads as something, and which one.
///
/// <b>The file already stores what every parameter shows on screen</b> -- a leaf is either a bare string,
/// for a text parameter, or <c>[raw, "displayed"]</c> -- so searching inside a patch is a substring test
/// against text that is already on disk. Nothing here consults the parameter database and nothing is
/// rendered.
///
/// <b>Built like <see cref="SnapshotHead"/> and for the same reason</b>: a forward-only walk that
/// interprets one primitive at a time and materialises nothing. Where that one skips <c>Blocks</c> whole,
/// this one walks into it and skips the metadata instead.
///
/// <b>It stops at the first hit.</b> The caller wants to know whether this file matches and what to show as
/// the reason; a second match would not change either answer.
///
/// A file that is not a snapshot, or not JSON at all, is simply not a match. A library folder is a folder,
/// and the user can and will put other things in it -- the same contract the listing has.</summary>
public static class SnapshotTextScan
{
    /// <summary>The first parameter whose displayed value contains <paramref name="text"/>, or null.
    /// Ordinal and ignoring case, matching <see cref="LibraryFilter"/>: the same library must search the
    /// same way on every machine, and nobody searching their own sounds is thinking about capitals.</summary>
    public static (string Path, string Value)? FirstMatch(Stream json, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        using var buffer = new MemoryStream();
        json.CopyTo(buffer);

        try
        {
            return Match(ByteOrderMark.SkipIn(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)), text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Path, string Value)? Match(ReadOnlySpan<byte> utf8, string text)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) return null;

            var property = reader.GetString()!;
            reader.Read();

            // Everything except Blocks is metadata, which LibraryFilter already searches over the heads.
            // Matching it here as well would hit the same entry twice and name a parameter that does not
            // exist.
            if (property != "Blocks") { reader.Skip(); continue; }

            return MatchInBlocks(ref reader, text);
        }

        return null;
    }

    /// <summary>Walk into Blocks -- three levels of address, then the block, then its parameters -- and test
    /// every leaf.
    ///
    /// <b>A block's parameters are not all at one depth.</b> <see cref="SnapshotJsonConverter"/> nests them
    /// by the parameter path's own '/', so "SN Synth Tone Common MFX/MFX Parameter 1/Delay Feedback" is
    /// three levels below the block's Offset2 rather than two, and a tone's effect settings -- around
    /// thirty parameters in every file in the library -- live down there. So the walk tracks a name per
    /// level and joins them, which searches a patch's effects and names them the way the rest of the
    /// application does, rather than fixing on one depth and searching everything except them.</summary>
    private static (string Path, string Value)? MatchInBlocks(ref Utf8JsonReader reader, string text)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return null;

        // Depth is counted rather than the levels being named, because naming them would be a second place
        // that has to agree with the writer about how deep the nesting is.
        var blocksDepth = reader.CurrentDepth;
        var blockDepth = blocksDepth + 4;

        // The parameter path being walked, outermost first: the block name, then any containers, then the
        // leaf. Joined only when something matches, since a string per parameter is exactly the work this
        // class exists to avoid.
        List<string> path = [];

        while (reader.Read())
        {
            // Out of Blocks entirely. Every End token inside it is deeper than the object itself.
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth <= blocksDepth) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            // The three address levels, which say where a block came from and hold no values of their own.
            if (reader.CurrentDepth < blockDepth) continue;

            var level = reader.CurrentDepth - blockDepth;
            if (path.Count > level) path.RemoveRange(level, path.Count - level);
            path.Add(reader.GetString()!);

            reader.Read();
            // A container rather than a leaf: descend into it instead of stepping over it, and the names of
            // its children land at the next level down.
            if (reader.TokenType == JsonTokenType.StartObject) continue;

            var value = ValueOf(ref reader);
            if (value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase))
                return (string.Join('/', path), value);
        }

        return null;
    }

    /// <summary>What a leaf reads as: itself when it is a bare string, and the second element when it is
    /// the <c>[raw, "displayed"]</c> pair. Anything else is stepped over rather than guessed at.</summary>
    private static string? ValueOf(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();

        // A number, a null, a boolean: already consumed, and none of them is text the user reads.
        if (reader.TokenType != JsonTokenType.StartArray) return null;

        string? displayed = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            if (reader.TokenType == JsonTokenType.String) displayed = reader.GetString();
            // Nothing this format writes, but a hand-edited file can hold anything, and stepping over it
            // whole is what keeps the walk in step with the document -- see SnapshotHead.ReadText.
            else reader.Skip();

        return displayed;
    }
}
