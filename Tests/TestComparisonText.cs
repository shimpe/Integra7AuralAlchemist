using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The text a user pastes into a forum post or an email. Pinned exactly, because "readable" is
/// the whole feature and nothing else would notice it regressing.</summary>
public class ComparisonTextTests
{
    private static SnapshotComparison Comparison(params BlockDifference[] blocks) =>
        new("Warm Rhodes", "the instrument", blocks, ParametersCompared: 1402, [], []);

    [Test]
    public void Renders_a_heading_a_summary_and_one_section_per_block()
    {
        var comparison = Comparison(new BlockDifference(
            "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common",
            [
                new ValueDifference("SuperNATURAL Synth Tone Common/Tone Level", "100", "118"),
                new ValueDifference("SuperNATURAL Synth Tone Common/Portamento Switch", "OFF", "ON"),
            ], [], []));

        var text = ComparisonText.Format(comparison, "library file Warm Rhodes.json",
            "read 2026-07-28 10:14");

        Assert.That(text, Does.StartWith("Integra-7 Aural Alchemist — comparison"));
        Assert.That(text, Does.Contain("Left:   Warm Rhodes — library file Warm Rhodes.json"));
        Assert.That(text, Does.Contain("Right:  the instrument — read 2026-07-28 10:14"));
        Assert.That(text, Does.Contain("2 differences across 1 block; 1402 parameters compared."));
        Assert.That(text, Does.Contain("SuperNATURAL Synth Tone Common  (2 differences)"));
        // Paths are shown without the block name they already sit under, and the values are aligned on
        // the longest path in the section.
        Assert.That(text, Does.Contain("  Tone Level          100  ->  118"));
        Assert.That(text, Does.Contain("  Portamento Switch   OFF  ->  ON"));
    }

    [Test]
    public void Says_so_when_there_is_nothing_to_report()
    {
        var text = ComparisonText.Format(Comparison(), "file A", "file B");

        Assert.That(text, Does.Contain("These two are identical; 1402 parameters compared."));
        Assert.That(text, Does.Not.Contain("differences across"));
    }

    /// <summary>The third answer, which used to be rendered as the second: nothing counted differs, but
    /// the two do not hold the same parameters. "0 differences across 0 blocks" read as the tool failing,
    /// and "identical" would have been a lie -- what is left is the finding itself.</summary>
    [Test]
    public void Says_the_two_hold_different_parameters_when_that_is_the_only_finding()
    {
        var comparison = new SnapshotComparison("A", "B",
            [new BlockDifference("Offset/X", "Offset2/Common", [], ["Common/Only Here"], [])],
            ParametersCompared: 1402,
            BlocksOnlyOnLeft: [],
            BlocksOnlyOnRight: []);

        var text = ComparisonText.Format(comparison, "file A", "file B");

        Assert.That(text, Does.Contain(
            "The two differ only in which parameters they hold; 1402 parameters compared."));
        Assert.That(text, Does.Not.Contain("identical"));
        Assert.That(text, Does.Not.Contain("0 differences"));
    }

    [Test]
    public void Lists_what_exists_on_only_one_side_when_there_is_any()
    {
        var comparison = new SnapshotComparison("A", "B",
            [
                new BlockDifference("Offset/X", "Offset2/Common", [],
                    ["Common/Only Here"], ["Common/Only There"]),
            ],
            ParametersCompared: 3,
            BlocksOnlyOnLeft: ["Offset2/Partial 4"],
            BlocksOnlyOnRight: []);

        var text = ComparisonText.Format(comparison, "file A", "file B");

        Assert.That(text, Does.Contain("Only in the left snapshot:"));
        Assert.That(text, Does.Contain("  Common/Only Here"));
        Assert.That(text, Does.Contain("  block Partial 4"));
        Assert.That(text, Does.Contain("Only in the right snapshot:"));
        Assert.That(text, Does.Contain("  Common/Only There"));
    }

    /// <summary>A block's note goes directly under its heading, where it is read before the rows it
    /// qualifies rather than after them. It is what the tab shows in that section's heading, so a pasted
    /// comparison and the screen say the same thing -- which the summary line proved is not automatic.
    /// </summary>
    [Test]
    public void Prints_a_blocks_note_directly_under_its_heading()
    {
        var comparison = Comparison(new BlockDifference(
            "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Partial 2",
            [new ValueDifference("SuperNATURAL Synth Tone Partial 2/OSC Pitch", "0", "12")],
            [], [], "switched off on the right"));

        var lines = Lines(ComparisonText.Format(comparison, "file A", "file B"));

        var heading = lines.IndexOf("SuperNATURAL Synth Tone Partial 2  (1 difference)");
        Assert.That(heading, Is.GreaterThan(-1));
        Assert.That(lines[heading + 1], Is.EqualTo("  — switched off on the right"));
        Assert.That(lines[heading + 2], Does.Contain("OSC Pitch"), "then the rows");
    }

    [Test]
    public void Prints_nothing_extra_for_a_block_with_no_note()
    {
        var comparison = Comparison(new BlockDifference("Offset/X", "Offset2/Common",
            [new ValueDifference("Common/Level", "1", "2")], [], []));

        var lines = Lines(ComparisonText.Format(comparison, "file A", "file B"));

        var heading = lines.IndexOf("Common  (1 difference)");
        Assert.That(lines[heading + 1], Does.Contain("Level"), "the rows, with nothing between");
    }

    /// <summary>Line endings are the platform's, and CI runs on three of them.</summary>
    private static System.Collections.Generic.List<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];

    [Test]
    public void Counts_one_block_and_one_difference_in_the_singular()
    {
        var comparison = Comparison(new BlockDifference("Offset/X", "Offset2/Common",
            [new ValueDifference("Common/Level", "1", "2")], [], []));

        var text = ComparisonText.Format(comparison, "file A", "file B");

        Assert.That(text, Does.Contain("1 difference across 1 block;"));
        Assert.That(text, Does.Contain("Common  (1 difference)"));
    }
}
