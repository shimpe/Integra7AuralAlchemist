using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter that differs, with both sides as the user reads them. The raw values decided
/// that it differs (see <see cref="SnapshotDiff"/>); these strings are what gets shown.
///
/// The raws come along because the strings are only as good as the build that captured them: a parameter
/// this build has since gained a name table for is stored as a bare number, and the raw is what lets a
/// caller ask the database for a better one (see <see cref="SnapshotValueNames"/>). Null for a text
/// parameter, whose value is its string.</summary>
public sealed record ValueDifference(string Path, string LeftValue, string RightValue,
    long? LeftRaw = null, long? RightRaw = null);

/// <summary>What differs within one block, and what exists in only one side of it.</summary>
public sealed record BlockDifference(
    string Offset,
    string Offset2,
    IReadOnlyList<ValueDifference> Differences,
    IReadOnlyList<string> PathsOnlyOnLeft,
    IReadOnlyList<string> PathsOnlyOnRight)
{
    /// <summary>The block's name without the address prefix -- "SuperNATURAL Synth Tone Common" rather
    /// than "Offset2/SuperNATURAL Synth Tone Common". What a heading should say.</summary>
    public string Name => Offset2.StartsWith("Offset2/", System.StringComparison.Ordinal)
        ? Offset2["Offset2/".Length..]
        : Offset2;

    public bool HasAnything =>
        Differences.Count > 0 || PathsOnlyOnLeft.Count > 0 || PathsOnlyOnRight.Count > 0;
}

/// <summary>A whole comparison. Blocks with nothing to report are absent, which is what keeps a Studio
/// Set comparison readable: 53 blocks in, usually a handful out.</summary>
public sealed record SnapshotComparison(
    string LeftName,
    string RightName,
    IReadOnlyList<BlockDifference> Blocks,
    int ParametersCompared,
    IReadOnlyList<string> BlocksOnlyOnLeft,
    IReadOnlyList<string> BlocksOnlyOnRight)
{
    public int DifferenceCount => Blocks.Sum(b => b.Differences.Count);

    /// <summary>Nothing to report at all. A path or block present on only one side counts against this:
    /// the two are not the same snapshot, and calling them identical would be wrong in the way that
    /// matters.</summary>
    public bool Identical =>
        DifferenceCount == 0 && BlocksOnlyOnLeft.Count == 0 && BlocksOnlyOnRight.Count == 0 &&
        Blocks.All(b => b.PathsOnlyOnLeft.Count == 0 && b.PathsOnlyOnRight.Count == 0);
}

/// <summary>What differs between two snapshots.
///
/// <b>Difference is decided on the raw value, not on the displayed string.</b> The raw value is what the
/// device stores; the string is a rendering of it. A build that renames an enum label -- "Low pass" to
/// "LPF" -- would otherwise report every parameter of that type as changed, in every comparison, for
/// ever. Where either side carries no raw value the strings are compared, which for a text parameter is
/// not a fallback but the right question: its value *is* its string.
///
/// <b>Blocks are matched on (Offset, Offset2), never on Start.</b> Start encodes which part a tone sat in
/// when it was captured, so matching on it would make a tone captured from part 3 differ from the
/// identical tone in part 5 in every single parameter. <c>StudioSetSnapshotService.RestoreToneAsync</c>
/// re-targets on exactly this reasoning; this is the same fact from the other side.
///
/// Pure -- two snapshots in, one comparison out, no domain, no parameter database, no device -- which is
/// what lets every rule above be tested. Capturing from the instrument is the caller's job, before this
/// is called.</summary>
public static class SnapshotDiff
{
    public static SnapshotComparison Compare(Integra7Snapshot left, Integra7Snapshot right)
    {
        // Refused rather than compared: two snapshots of different kinds share no blocks at all, so
        // "everything differs" is technically true and tells the user nothing.
        if (left.Kind != right.Kind)
            throw new SnapshotFormatException(
                $"These cannot be compared: one holds \"{left.Kind}\" and the other \"{right.Kind}\".");

        if (left.ToneType != right.ToneType)
            throw new SnapshotFormatException(
                $"These cannot be compared: one is a {left.ToneType} tone and the other a " +
                $"{right.ToneType} tone. Their parameters are different sounds' parameters.");

        var rightBlocks = Index(right, "right");
        var leftBlocks = Index(left, "left");

        List<BlockDifference> blocks = [];
        List<string> onlyOnLeft = [];
        var compared = 0;

        // The left snapshot's order, which is capture order and therefore address order.
        foreach (var block in left.Domains)
        {
            if (!rightBlocks.TryGetValue((block.Offset, block.Offset2), out var other))
            {
                onlyOnLeft.Add(block.Offset2);
                continue;
            }

            var difference = CompareBlock(block, other, ref compared);
            if (difference.HasAnything) blocks.Add(difference);
        }

        var onlyOnRight = right.Domains
            .Where(b => !leftBlocks.ContainsKey((b.Offset, b.Offset2)))
            .Select(b => b.Offset2)
            .ToList();

        return new SnapshotComparison(left.Name, right.Name, blocks, compared, onlyOnLeft, onlyOnRight);
    }

    /// <summary>The blocks of one snapshot, keyed the way they are matched. A snapshot listing the same
    /// block twice is refused rather than silently having one of them win -- the same guard, for the same
    /// reason, as <c>RestoreToneAsync</c>'s.</summary>
    private static Dictionary<(string Offset, string Offset2), SnapshotDomain> Index(
        Integra7Snapshot snapshot, string side)
    {
        Dictionary<(string Offset, string Offset2), SnapshotDomain> blocks = [];
        foreach (var block in snapshot.Domains)
            if (!blocks.TryAdd((block.Offset, block.Offset2), block))
                throw new SnapshotFormatException(
                    $"The {side} snapshot lists block (\"{block.Offset}\", \"{block.Offset2}\") more " +
                    "than once, so there is no telling which one to compare.");

        return blocks;
    }

    private static BlockDifference CompareBlock(SnapshotDomain left, SnapshotDomain right, ref int compared)
    {
        var rightValues = new Dictionary<string, SnapshotValue>();
        foreach (var value in right.Values) rightValues[value.Path] = value;

        List<ValueDifference> differences = [];
        List<string> onlyOnLeft = [];
        var seen = new HashSet<string>();

        foreach (var value in left.Values)
        {
            if (IsReserved(value.Path)) continue;

            seen.Add(value.Path);
            if (!rightValues.TryGetValue(value.Path, out var other))
            {
                onlyOnLeft.Add(value.Path);
                continue;
            }

            compared++;
            if (Differs(value, other))
                differences.Add(new ValueDifference(value.Path, value.Value, other.Value,
                    value.Raw, other.Raw));
        }

        var onlyOnRight = right.Values
            .Where(v => !IsReserved(v.Path) && !seen.Contains(v.Path))
            .Select(v => v.Path)
            .ToList();
        return new BlockDifference(left.Offset, left.Offset2, differences, onlyOnLeft, onlyOnRight);
    }

    /// <summary>Whether this is one of the instrument's unused slots, which a comparison must not report.
    ///
    /// They are in the snapshot on purpose -- a block is bulk-written as one transmission and every byte
    /// of it has to be there, so the capture takes reserved parameters too. They are not something a user
    /// can act on: a difference in one says the device left something else in a byte it does not read,
    /// and a report full of "Reserved12" buries the rows that mean something.
    ///
    /// Matched on the name rather than on the parameter database's own flag, so that this stays pure and
    /// a snapshot can be compared without loading the database. That is sound because every path in the
    /// database containing the word is flagged reserved -- checked, not assumed -- and because the shapes
    /// are not a closed set: "Reserved3", "MFX Parameter 1/Thru (Reserved)" and "Phaser 3 Reserved" all
    /// occur, the last of which was found only when something stopped matching on spelling. No block name
    /// contains the word, so matching the whole path cannot catch an innocent parameter by its
    /// prefix.</summary>
    private static bool IsReserved(string path) =>
        path.Contains("Reserved", StringComparison.Ordinal);

    /// <summary>Raw against raw whenever both sides have one; strings otherwise. A value with a raw on
    /// one side only is a file from a build before raw values existed compared against a current one --
    /// the strings are all they have in common, so the strings are what decides.</summary>
    private static bool Differs(SnapshotValue left, SnapshotValue right) =>
        left.Raw is { } leftRaw && right.Raw is { } rightRaw
            ? leftRaw != rightRaw
            : left.Value != right.Value;
}
