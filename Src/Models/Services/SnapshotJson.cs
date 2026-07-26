using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>How an <see cref="Integra7Snapshot"/> is written to and read from a file, as of format
/// version 3. The in-memory model is unchanged and deliberately so -- an ordered
/// <c>List&lt;SnapshotDomain&gt;</c> of ordered <c>List&lt;SnapshotValue&gt;</c>, which is what
/// <c>StudioSetSnapshotService</c> depends on -- and only the shape on disk moves.
///
/// Version 2 wrote every value as an object repeating three keys and the whole parameter path:
///
/// <code>
/// { "Path": "Studio Set Common/Studio Set Tempo", "Value": "120", "Raw": 120 }
/// </code>
///
/// A Studio Set is roughly 4000 of those. Version 3 nests them, first by the three address names that
/// identify a block and then by the parameter path's own '/':
///
/// <code>
/// "Blocks": { "Temporary Studio Set": { "Offset/Not Used": { "Offset2/Studio Set Common": {
///     "Studio Set Common": { "Studio Set Name": "Old Set", "Studio Set Tempo": [120, "120"] } } } } }
/// </code>
///
/// which is about a third of the bytes and, more to the point, diffable in the way the format was
/// always meant to be: a changed tempo is one changed line under a heading that says where it is.
///
/// <b>Two things about this are load-bearing, and both are pinned by tests rather than by this comment.</b>
///
/// <b>Order must survive the file.</b> <c>StudioSetSnapshotService.ApplyBlockValues</c> applies a block's
/// values in the order the file lists them, because a discriminator has to be applied before the
/// parameters that only exist under its value -- a chorus type before that type's knobs. A JSON object
/// contracts no ordering, and neither does <c>Dictionary&lt;string, …&gt;</c>: a dictionary happens to
/// preserve insertion order only until something is removed from it, which is exactly the kind of
/// accidental correctness that breaks silently and late. So nothing here deserializes into a dictionary.
/// Reading is a forward walk with a <see cref="Utf8JsonReader"/>, which is in document order by
/// construction, appending to a list; writing walks the list. Order in, order out, with no step in
/// between that could reorder anything.
///
/// <b>A leaf carries two values.</b> <c>Raw</c> is the value the device actually stores and is what a
/// restore writes -- it survives this build renaming or reordering an enum string, which a display
/// string does not (see <see cref="Integra7Snapshot"/>'s own notes on why version 2 exists at all). The
/// display value is what makes the file readable, which is the point of the format. So a leaf is
/// <c>[raw, "display"]</c> for anything numeric or discrete, and a bare <c>"string"</c> for a text
/// parameter, which has no raw form at all. Two shapes, one branch to read them.
///
/// <b>Duplicate keys are refused, at every level.</b> A hand-edited file can name the same parameter, or
/// the same block, twice. Reading into a dictionary would silently keep the last one; reading into a list
/// would silently apply both. Neither is something a user could ever notice, and a snapshot that quietly
/// drops or doubles a value is worse than one that will not open, so any repeated key is an error that
/// names the key.</summary>
public sealed class SnapshotJsonConverter : JsonConverter<Integra7Snapshot>
{
    /// <summary>The property the parameter data lives under. Written <b>last</b>, after every piece of
    /// metadata, so that a later reader can take a file's head -- its name, its kind, and whatever
    /// metadata the record grows -- and stop before the ~4000 values it has no use for.</summary>
    private const string BlocksProperty = "Blocks";

    public override void Write(Utf8JsonWriter writer, Integra7Snapshot value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Metadata first, blocks last. Every property added to this record in future belongs above the
        // WriteBlocks call, not below it.
        writer.WriteNumber(nameof(Integra7Snapshot.FormatVersion), value.FormatVersion);
        WriteStringOrNull(writer, nameof(Integra7Snapshot.Name), value.Name);
        WriteStringOrNull(writer, nameof(Integra7Snapshot.Kind), value.Kind);
        // Written even when null, which it is for every Studio Set. A file that always carries the
        // property is one the head reader can treat uniformly, and it is what version 2 files look like.
        WriteStringOrNull(writer, nameof(Integra7Snapshot.ToneType), value.ToneType);

        WriteBlocks(writer, value.Domains, options);

        writer.WriteEndObject();
    }

    /// <summary><see cref="Utf8JsonWriter.WriteString(string, string)"/> is documented to write a JSON
    /// null for a null value, but going through it for a field that can legitimately be null reads as if
    /// null were an accident. Being explicit costs one method and says what is meant.</summary>
    private static void WriteStringOrNull(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null) writer.WriteNull(property);
        else writer.WriteString(property, value);
    }

    private static void WriteBlocks(Utf8JsonWriter writer, List<SnapshotDomain>? domains,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject(BlocksProperty);

        // A leaf that carries a raw value is written as a raw fragment rather than through
        // WriteStartArray/WriteEndArray, because an indented Utf8JsonWriter puts every array element on
        // its own line -- three lines and two indents per parameter, which would give back most of what
        // the nesting just saved and would make a one-parameter change a three-line diff. The fragment is
        // built by a second, un-indented writer over a reusable buffer, so the escaping is System.Text.
        // Json's own and matches this document's encoder rather than being hand-rolled here.
        var scratch = new ArrayBufferWriter<byte>(64);
        using var scratchWriter = new Utf8JsonWriter(scratch,
            new JsonWriterOptions { Indented = false, Encoder = options.Encoder });

        // Every block of a Studio Set shares one Start and one Offset, and so does every block of a tone,
        // so in practice these two levels each open exactly once. They are still written as a run rather
        // than assumed to be constant: the address triple is the model, and a snapshot that did span two
        // Starts would otherwise emit the same key twice and produce a file this build's own reader
        // refuses. Only *consecutive* blocks are coalesced, because reordering them to group a repeated
        // address would change restore order -- the one thing this format may not do. A non-consecutive
        // repeat is therefore refused rather than silently reordered.
        var startsSeen = new HashSet<string>(StringComparer.Ordinal);
        var offsetsSeen = new HashSet<string>(StringComparer.Ordinal);
        var offset2sSeen = new HashSet<string>(StringComparer.Ordinal);
        string? openStart = null;
        string? openOffset = null;

        foreach (var block in domains ?? [])
        {
            var start = Required(block.Start, "Start");
            var offset = Required(block.Offset, "Offset");
            var offset2 = Required(block.Offset2, "Offset2");

            if (openStart != start)
            {
                if (openOffset is not null) writer.WriteEndObject();
                if (openStart is not null) writer.WriteEndObject();
                if (!startsSeen.Add(start))
                    throw Interleaved(start);
                writer.WriteStartObject(start);
                openStart = start;
                openOffset = null;
                offsetsSeen.Clear();
            }

            if (openOffset != offset)
            {
                if (openOffset is not null) writer.WriteEndObject();
                if (!offsetsSeen.Add(offset))
                    throw Interleaved(offset);
                writer.WriteStartObject(offset);
                openOffset = offset;
                offset2sSeen.Clear();
            }

            // Offset2 identifies the block within its Start/Offset, so a repeat is the same block twice.
            if (!offset2sSeen.Add(offset2))
                throw new SnapshotFormatException(
                    $"This snapshot lists block (\"{start}\", \"{offset}\", \"{offset2}\") more than once, " +
                    $"so it cannot be written to a file that names each block once.");

            writer.WriteStartObject(offset2);
            WriteBlockValues(writer, block, scratch, scratchWriter);
            writer.WriteEndObject();
        }

        if (openOffset is not null) writer.WriteEndObject();
        if (openStart is not null) writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static SnapshotFormatException Interleaved(string address) =>
        new($"This snapshot's blocks return to the address \"{address}\" after leaving it. Writing that " +
            "would either repeat a key or reorder the blocks, and block order is what a restore applies " +
            "them in.");

    private static string Required(string? address, string which) =>
        address ?? throw new SnapshotFormatException(
            $"This snapshot has a block with no {which} address, so there is nowhere to write it.");

    /// <summary>One block's values, nested by the parameter path's own '/'.
    ///
    /// The whole method is a run-length encoding of the paths' containing segments: a stack of objects
    /// that are currently open, closed only when the next path stops sharing them. That is what turns a
    /// run of "Chorus Parameter 1/..." paths into one object, and it is why nothing here sorts or groups
    /// -- sorting would destroy the capture order a restore depends on. Real data suits it exactly:
    /// every path in the parameter database has either one '/' or two, and across all 225 blocks a
    /// snapshot can hold, no container segment is ever returned to after being left.
    ///
    /// Each open object remembers every key ever created inside it, not just the ones still open, so
    /// two paths that would collide once nested -- "A/B" as a value and "A/B/C" as an object, or a
    /// container revisited -- are refused rather than written as a file with a repeated key that this
    /// build's own reader would then reject. The parameter database contains no such pair today; this
    /// is what would catch one being introduced, at the moment it would first produce an unreadable
    /// snapshot rather than a puzzle later.</summary>
    private static void WriteBlockValues(Utf8JsonWriter writer, SnapshotDomain block,
        ArrayBufferWriter<byte> scratch, Utf8JsonWriter scratchWriter)
    {
        // The container segments of the object currently open, outermost first...
        var openSegments = new List<string>();
        // ...and, for each of them plus the block object itself, every key it has ever been given.
        var usedKeys = new List<HashSet<string>> { new(StringComparer.Ordinal) };

        foreach (var value in block.Values ?? [])
        {
            var path = value.Path ?? throw new SnapshotFormatException(
                "This snapshot has a value with no parameter path, so there is nowhere to write it.");
            var segments = path.Split('/');
            var containerCount = segments.Length - 1;

            var shared = 0;
            while (shared < openSegments.Count && shared < containerCount &&
                   string.Equals(openSegments[shared], segments[shared], StringComparison.Ordinal))
                shared++;

            while (openSegments.Count > shared)
            {
                writer.WriteEndObject();
                openSegments.RemoveAt(openSegments.Count - 1);
                usedKeys.RemoveAt(usedKeys.Count - 1);
            }

            for (var i = shared; i < containerCount; i++)
            {
                if (!usedKeys[^1].Add(segments[i]))
                    throw Collides(path, segments[i]);
                writer.WriteStartObject(segments[i]);
                openSegments.Add(segments[i]);
                usedKeys.Add(new HashSet<string>(StringComparer.Ordinal));
            }

            if (!usedKeys[^1].Add(segments[^1]))
                throw Collides(path, segments[^1]);

            WriteLeaf(writer, segments[^1], value, scratch, scratchWriter);
        }

        for (var i = openSegments.Count; i > 0; i--) writer.WriteEndObject();
    }

    private static SnapshotFormatException Collides(string path, string segment) =>
        new($"This snapshot's parameter \"{path}\" collides with another one at \"{segment}\" once the " +
            "paths are nested, so the two cannot both be written.");

    private static void WriteLeaf(Utf8JsonWriter writer, string name, SnapshotValue value,
        ArrayBufferWriter<byte> scratch, Utf8JsonWriter scratchWriter)
    {
        // A text parameter has no raw form -- its value IS the string -- so it is a bare string, and
        // reading one back gives Raw null, which is what tells a restore to use the string.
        if (value.Raw is not { } raw)
        {
            WriteStringOrNull(writer, name, value.Value);
            return;
        }

        scratch.Clear();
        scratchWriter.Reset(scratch);
        scratchWriter.WriteStartArray();
        scratchWriter.WriteNumberValue(raw);
        if (value.Value is null) scratchWriter.WriteNullValue();
        else scratchWriter.WriteStringValue(value.Value);
        scratchWriter.WriteEndArray();
        scratchWriter.Flush();

        writer.WritePropertyName(name);
        // skipInputValidation: the fragment was just produced by a Utf8JsonWriter, so re-parsing it to
        // check it is well-formed JSON would only be checking System.Text.Json against itself.
        writer.WriteRawValue(scratch.WrittenSpan, skipInputValidation: true);
    }

    public override Integra7Snapshot Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A snapshot file is a JSON object.");

        // The defaults matter and are the record's own, for the record's own reasons: an absent Kind
        // reads as a Studio Set (see Integra7Snapshot), an absent FormatVersion as 0, which the version
        // check then refuses by name. Nothing here invents a value that FromJson would not go on to judge.
        var formatVersion = 0;
        string? name = null;
        var kind = SnapshotKinds.StudioSet;
        string? toneType = null;
        List<SnapshotDomain> domains = [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("A snapshot file is a JSON object.");

            var property = reader.GetString()!;
            // Two "Blocks" properties, or two "Name"s, would otherwise resolve to whichever came last,
            // with the other silently gone.
            if (!seen.Add(property))
                throw new SnapshotFormatException(
                    $"This snapshot file names \"{property}\" more than once, so there is no telling which " +
                    "one it means.");

            reader.Read();
            switch (property)
            {
                case nameof(Integra7Snapshot.FormatVersion):
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out formatVersion))
                        throw new JsonException("A snapshot's format version is a whole number.");
                    break;
                case nameof(Integra7Snapshot.Name):
                    name = ReadStringOrNull(ref reader, "Name");
                    break;
                case nameof(Integra7Snapshot.Kind):
                    // A null here deliberately survives as null rather than falling back to the default:
                    // a file that says nothing about its kind is a Studio Set, but a file that says its
                    // kind is nothing is a file FromJson should refuse and name.
                    kind = ReadStringOrNull(ref reader, "Kind")!;
                    break;
                case nameof(Integra7Snapshot.ToneType):
                    toneType = ReadStringOrNull(ref reader, "ToneType");
                    break;
                case BlocksProperty:
                    ReadBlocks(ref reader, domains);
                    break;
                default:
                    // A property from a build that knows a field this one does not. Skipping it is what
                    // System.Text.Json did before and is right: an unknown *field* is not a reason to
                    // refuse a file whose values this build understands perfectly well.
                    reader.Skip();
                    break;
            }
        }

        return new Integra7Snapshot(formatVersion, name!, domains, kind, toneType);
    }

    private static string? ReadStringOrNull(ref Utf8JsonReader reader, string property) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"A snapshot's {property} is text."),
        };

    /// <summary>The three address levels, which are fixed at exactly three because the address model is:
    /// Start, then Offset, then Offset2, and then the block's parameters. A file nested any other number
    /// of levels deep is not a snapshot, and finding that out here -- rather than resolving whatever
    /// happens to be at the third level as a block name -- is what keeps a mangled file from reaching
    /// <c>GetDomain</c>, which does not throw for an address it cannot resolve but silently falls back to
    /// an unrelated block.</summary>
    private static void ReadBlocks(ref Utf8JsonReader reader, List<SnapshotDomain> domains)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A snapshot's blocks are a JSON object keyed by address.");

        // Three explicit loops rather than a recursive walk to some depth, because the depth is not a
        // parameter: it is the address model, which has exactly three levels. Written out, a file nested
        // two or four deep fails on the level it is actually wrong at. (Utf8JsonReader is a ref struct
        // and cannot be captured by an iterator, so a tidy "for each key" enumerator is not available
        // here even if the depth were variable.)
        var startsSeen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var start = NextKey(ref reader, startsSeen, BlocksProperty);
            if (start is null) return;
            ExpectStartObject(ref reader, start);

            var offsetsSeen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var offset = NextKey(ref reader, offsetsSeen, start);
                if (offset is null) break;
                ExpectStartObject(ref reader, offset);

                var offset2sSeen = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    var offset2 = NextKey(ref reader, offset2sSeen, offset);
                    if (offset2 is null) break;

                    // Onto the block object itself; ReadBlockValues is what refuses anything else there,
                    // which is also how a file nested fewer than three levels deep is caught.
                    reader.Read();
                    List<SnapshotValue> values = [];
                    ReadBlockValues(ref reader, "", values);
                    domains.Add(new SnapshotDomain(start, offset, offset2, values));
                }
            }
        }
    }

    /// <summary>The next key of the object the reader is inside, or null once it reaches the end of that
    /// object. Leaves the reader on the key, so the caller decides how to consume the value.</summary>
    private static string? NextKey(ref Utf8JsonReader reader, HashSet<string> seen, string owner)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.EndObject) return null;
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw new JsonException($"\"{owner}\" should hold a JSON object keyed by address.");

        var key = reader.GetString()!;
        if (!seen.Add(key))
            throw new SnapshotFormatException(
                $"This snapshot names \"{key}\" more than once inside \"{owner}\", so there is no telling " +
                "which one it means.");
        return key;
    }

    private static void ExpectStartObject(ref Utf8JsonReader reader, string owner)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"\"{owner}\" should hold a JSON object keyed by address.");
    }

    private static void ReadBlockValues(ref Utf8JsonReader reader, string prefix, List<SnapshotValue> values)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A block's parameters are a JSON object.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("A block's parameters are a JSON object.");

            var key = reader.GetString()!;
            if (!seen.Add(key))
                throw new SnapshotFormatException(
                    $"This snapshot names \"{prefix}{key}\" more than once in one block, so there is no " +
                    "telling which value it means.");

            reader.Read();
            switch (reader.TokenType)
            {
                // Another level of the parameter path.
                case JsonTokenType.StartObject:
                    ReadBlockValues(ref reader, $"{prefix}{key}/", values);
                    break;
                // A text parameter: its value IS the string, and Raw stays null, which is what tells a
                // restore to apply it as a string.
                case JsonTokenType.String:
                    values.Add(new SnapshotValue($"{prefix}{key}", reader.GetString()!));
                    break;
                // A hand-edited file can null a value out. Kept rather than dropped, so that FromJson's
                // own "missing its contents" check is what refuses it, in the one place that judges a
                // file's contents.
                case JsonTokenType.Null:
                    values.Add(new SnapshotValue($"{prefix}{key}", null!));
                    break;
                // [raw, "display"].
                case JsonTokenType.StartArray:
                    values.Add(ReadLeafPair(ref reader, $"{prefix}{key}"));
                    break;
                default:
                    throw new JsonException(
                        $"The parameter \"{prefix}{key}\" is neither a string nor a [raw, \"display\"] pair.");
            }
        }
    }

    private static SnapshotValue ReadLeafPair(ref Utf8JsonReader reader, string path)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var raw))
            throw new JsonException($"The parameter \"{path}\" should start with the raw value the device stores.");

        reader.Read();
        var display = ReadStringOrNull(ref reader, $"parameter \"{path}\"'s displayed value");

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException($"The parameter \"{path}\" should be exactly [raw, \"display\"].");

        return new SnapshotValue(path, display!, raw);
    }
}
