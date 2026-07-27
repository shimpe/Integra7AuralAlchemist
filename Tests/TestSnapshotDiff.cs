using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What counts as a difference, and what counts as an answer rather than a failure.
///
/// The whole point of these is the raw-value rule: format v2 added the raw value for exactly this, and a
/// comparison that used the display string would report every parameter of a renamed enum as changed, in
/// every comparison, for ever.</summary>
public class SnapshotDiffTests
{
    private const string Start = "Temporary Tone Part 1";
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Common = "Offset2/SuperNATURAL Synth Tone Common";
    private const string Partial = "Offset2/SuperNATURAL Synth Tone Partial 1";

    private static Integra7Snapshot Tone(string name, params SnapshotDomain[] domains) =>
        new(Integra7Snapshot.CurrentFormatVersion, name, [.. domains], SnapshotKinds.Tone, "SN-S");

    private static SnapshotDomain Block(string offset2, params SnapshotValue[] values) =>
        new(Start, Offset, offset2, [.. values]);

    private static SnapshotDomain BlockIn(string start, string offset2, params SnapshotValue[] values) =>
        new(start, Offset, offset2, [.. values]);

    [Test]
    public void Two_identical_snapshots_differ_in_nothing()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
        Assert.That(result.Blocks, Is.Empty, "a block with nothing to report is not listed");
        Assert.That(result.ParametersCompared, Is.EqualTo(1));
    }

    [Test]
    public void A_changed_value_is_reported_with_both_sides_as_the_user_reads_them()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "118", 118)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
        var difference = result.Blocks.Single().Differences.Single();
        Assert.That(difference.Path, Is.EqualTo("Tone/Level"));
        Assert.That(difference.LeftValue, Is.EqualTo("100"));
        Assert.That(difference.RightValue, Is.EqualTo("118"));
    }

    /// <summary>The reason the raw value is in the file. Renaming an enum label -- "Low pass" to "LPF" --
    /// must not turn every filter in the library into a difference.</summary>
    [Test]
    public void A_renamed_label_over_the_same_raw_value_is_not_a_difference()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 1)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Filter Mode", "LPF", 1)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
    }

    /// <summary>And the converse: the same string over a different raw value is a real difference, which
    /// a string comparison would have missed.</summary>
    [Test]
    public void The_same_label_over_a_different_raw_value_is_a_difference()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 1)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Filter Mode", "Low pass", 5)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
    }

    /// <summary>A text parameter's value IS its string -- it carries no raw -- so for it the strings are
    /// the comparison rather than a fallback.</summary>
    [Test]
    public void A_text_parameter_is_compared_on_its_string()
    {
        var one = Tone("a", Block(Common, new SnapshotValue("Tone/Tone Name", "Warm Rhodes")));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Tone Name", "Glass Pad")));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.DifferenceCount, Is.EqualTo(1));
    }

    /// <summary>Start says which part a tone was captured from and nothing about the sound. Matching on
    /// it would make a tone captured from part 3 differ from the same tone in part 5 in every
    /// parameter.</summary>
    [Test]
    public void The_same_tone_captured_from_two_different_parts_does_not_differ()
    {
        var one = Tone("a", BlockIn("Temporary Tone Part 3", Common,
            new SnapshotValue("Tone/Level", "100", 100)));
        var two = Tone("b", BlockIn("Temporary Tone Part 5", Common,
            new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Identical, Is.True);
    }

    [Test]
    public void A_studio_set_against_a_tone_is_refused_and_the_message_names_both()
    {
        var tone = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var studioSet = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "b",
            [new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
                [new SnapshotValue("Studio Set Common/Studio Set Tempo", "120", 120)])]);

        var e = Assert.Throws<SnapshotFormatException>(() => SnapshotDiff.Compare(studioSet, tone));

        Assert.That(e!.Message, Does.Contain(SnapshotKinds.StudioSet));
        Assert.That(e.Message, Does.Contain(SnapshotKinds.Tone));
    }

    [Test]
    public void Two_tones_of_different_engines_are_refused_and_the_message_names_both()
    {
        var sns = Tone("a", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));
        var pcm = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "b",
            [new SnapshotDomain(Start, "Offset/Temporary PCM Synth Tone",
                "Offset2/PCM Synth Tone Common",
                [new SnapshotValue("PCM Synth Tone Common/PCM Synth Tone Level", "100", 100)])],
            SnapshotKinds.Tone, "PCMS");

        var e = Assert.Throws<SnapshotFormatException>(() => SnapshotDiff.Compare(sns, pcm));

        Assert.That(e!.Message, Does.Contain("SN-S"));
        Assert.That(e.Message, Does.Contain("PCMS"));
    }

    /// <summary>An older file, or one from a build that has since gained a parameter. A real answer, and
    /// refusing it would make exactly the snapshots most worth comparing uncomparable.</summary>
    [Test]
    public void A_path_on_one_side_only_is_reported_rather_than_thrown()
    {
        var one = Tone("a", Block(Common,
            new SnapshotValue("Tone/Level", "100", 100),
            new SnapshotValue("Tone/Only Here", "1", 1)));
        var two = Tone("b", Block(Common,
            new SnapshotValue("Tone/Level", "100", 100),
            new SnapshotValue("Tone/Only There", "2", 2)));

        var result = SnapshotDiff.Compare(one, two);

        var block = result.Blocks.Single();
        Assert.That(block.Differences, Is.Empty);
        Assert.That(block.PathsOnlyOnLeft, Is.EqualTo(new[] { "Tone/Only Here" }));
        Assert.That(block.PathsOnlyOnRight, Is.EqualTo(new[] { "Tone/Only There" }));
        Assert.That(result.Identical, Is.False, "the two are not the same snapshot");
        Assert.That(result.ParametersCompared, Is.EqualTo(1), "only the path both sides carry");
    }

    [Test]
    public void A_block_on_one_side_only_is_reported_rather_than_thrown()
    {
        var one = Tone("a",
            Block(Common, new SnapshotValue("Tone/Level", "100", 100)),
            Block(Partial, new SnapshotValue("Partial/Cutoff", "127", 127)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "100", 100)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.BlocksOnlyOnLeft, Is.EqualTo(new[] { Partial }));
        Assert.That(result.BlocksOnlyOnRight, Is.Empty);
        Assert.That(result.Identical, Is.False);
    }

    /// <summary>Reproducible, and in the order everything else in the application shows these values:
    /// the left snapshot's block order, address order within a block.</summary>
    [Test]
    public void Blocks_and_values_keep_the_left_snapshots_order()
    {
        var one = Tone("a",
            Block(Partial, new SnapshotValue("Partial/B", "1", 1), new SnapshotValue("Partial/A", "1", 1)),
            Block(Common, new SnapshotValue("Tone/Level", "1", 1)));
        var two = Tone("b",
            Block(Common, new SnapshotValue("Tone/Level", "2", 2)),
            Block(Partial, new SnapshotValue("Partial/A", "2", 2), new SnapshotValue("Partial/B", "2", 2)));

        var result = SnapshotDiff.Compare(one, two);

        Assert.That(result.Blocks.Select(b => b.Offset2), Is.EqualTo(new[] { Partial, Common }));
        Assert.That(result.Blocks[0].Differences.Select(d => d.Path),
            Is.EqualTo(new[] { "Partial/B", "Partial/A" }));
    }

    [Test]
    public void A_block_listed_twice_is_refused()
    {
        var one = Tone("a",
            Block(Common, new SnapshotValue("Tone/Level", "1", 1)),
            Block(Common, new SnapshotValue("Tone/Level", "2", 2)));
        var two = Tone("b", Block(Common, new SnapshotValue("Tone/Level", "1", 1)));

        Assert.That(() => SnapshotDiff.Compare(one, two), Throws.TypeOf<SnapshotFormatException>());
    }
}
