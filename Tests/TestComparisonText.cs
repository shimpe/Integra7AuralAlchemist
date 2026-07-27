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
