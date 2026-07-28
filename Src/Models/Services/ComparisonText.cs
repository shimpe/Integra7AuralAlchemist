using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A comparison as plain text, for the clipboard or a file.
///
/// <b>Plain text, not Markdown.</b> This is pasted into forum posts, emails and notes at least as often
/// as into anything that renders, and a table that only looks like a table in a renderer is worse in the
/// places it does not. Alignment is done with spaces, per section, on the longest path in that section --
/// which is what makes a hundred-line list readable without any renderer at all.
///
/// Pure, so the layout is pinned by tests rather than by eye.</summary>
public static class ComparisonText
{
    /// <param name="leftSource">Where the left side came from, e.g. "library file Warm Rhodes.json" or
    /// "read 2026-07-28 10:14". The comparison itself knows the snapshots' names but not their
    /// provenance, and provenance is most of what makes a pasted comparison meaningful.</param>
    public static string Format(SnapshotComparison comparison, string leftSource, string rightSource)
    {
        var text = new StringBuilder();
        text.AppendLine("Integra-7 Aural Alchemist — comparison");
        text.AppendLine();
        text.AppendLine($"Left:   {comparison.LeftName} — {leftSource}");
        text.AppendLine($"Right:  {comparison.RightName} — {rightSource}");
        text.AppendLine();

        text.AppendLine(Summary(comparison));

        foreach (var block in comparison.Blocks.Where(b => b.Differences.Count > 0))
        {
            text.AppendLine();
            text.AppendLine($"{block.Name}  ({Count(block.Differences.Count, "difference")})");
            if (block.Note is { Length: > 0 }) text.AppendLine(NoteLine(block.Note));

            // Paths are shown relative to the block they already sit under: every path in a block starts
            // with that block's own name, and repeating it on every line costs half the width.
            var rows = block.Differences
                .Select(d => (Path: ShortPath(d.Path, block.Name), d.LeftValue, d.RightValue))
                .ToList();
            var width = rows.Max(r => r.Path.Length);
            foreach (var row in rows)
                text.AppendLine($"  {row.Path.PadRight(width)}   {row.LeftValue}  ->  {row.RightValue}");
        }

        AppendOnlyOnOneSide(text, "left", comparison.BlocksOnlyOnLeft,
            comparison.Blocks.SelectMany(b => b.PathsOnlyOnLeft));
        AppendOnlyOnOneSide(text, "right", comparison.BlocksOnlyOnRight,
            comparison.Blocks.SelectMany(b => b.PathsOnlyOnRight));

        return text.ToString();
    }

    /// <summary>Three answers, not two.
    ///
    /// A comparison whose only finding is a path or a whole block one side carries and the other does not
    /// has nothing to count, and counting it anyway said "0 differences across 0 blocks" -- which reads as
    /// the tool failing rather than as the answer. It is emphatically not identical either: the two do not
    /// hold the same parameters, which is precisely what the "Only in the ... snapshot" sections below then
    /// say. So it gets a line of its own, phrased as the finding it is.</summary>
    /// <remarks>Public because the Compare tab shows this same line on screen. It had its own copy, and
    /// the copy was written before the three-case rule above existed and never gained it -- so the tab
    /// said "0 differences across 0 blocks" for exactly the comparison this method exists to describe
    /// properly, while the text it exported said the right thing. One rule, one place.</remarks>
    public static string Summary(SnapshotComparison comparison)
    {
        if (comparison.Identical)
            return $"These two are identical; {comparison.ParametersCompared} parameters compared.";

        if (comparison.DifferenceCount == 0)
            return "The two differ only in which parameters they hold; " +
                   $"{comparison.ParametersCompared} parameters compared.";

        return $"{Count(comparison.DifferenceCount, "difference")} across " +
               $"{Count(comparison.Blocks.Count(b => b.Differences.Count > 0), "block")}; " +
               $"{comparison.ParametersCompared} parameters compared.";
    }

    private static void AppendOnlyOnOneSide(StringBuilder text, string side,
        IReadOnlyList<string> blocks, IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        if (blocks.Count == 0 && pathList.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"Only in the {side} snapshot:");
        foreach (var block in blocks)
            text.AppendLine($"  block {Strip(block)}");
        foreach (var path in pathList)
            text.AppendLine($"  {path}");
    }

    /// <summary>A block's note, under its heading and above the rows it qualifies -- "partial 2 is off on
    /// the right" is what decides whether the twenty-three rows below are worth reading, so it is read
    /// first.
    ///
    /// Led by a dash rather than aligned with the rows, because at the rows' own indent it would read as
    /// one of them. The note itself is <see cref="BlockDifference.Note"/> verbatim: the Compare tab shows
    /// that same string in that same section's heading, and the two saying different things is precisely
    /// the failure the summary line already had once.</summary>
    private static string NoteLine(string note) => $"  — {note}";

    /// <summary>"SuperNATURAL Synth Tone Common/Tone Level" under a heading that already says
    /// "SuperNATURAL Synth Tone Common" becomes "Tone Level". Anything that does not start with the
    /// block's name is left whole rather than truncated on a guess.</summary>
    private static string ShortPath(string path, string blockName) =>
        path.StartsWith(blockName + "/", System.StringComparison.Ordinal)
            ? path[(blockName.Length + 1)..]
            : path;

    private static string Strip(string offset2) =>
        offset2.StartsWith("Offset2/", System.StringComparison.Ordinal)
            ? offset2["Offset2/".Length..]
            : offset2;

    /// <summary>"1 difference", "2 differences". A count that reads as English is worth four lines.</summary>
    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
