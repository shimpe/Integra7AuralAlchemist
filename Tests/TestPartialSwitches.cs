using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Whether a partial is on, asked of the partial's own block -- which is not where the answer is
/// stored. The point of the service is that the switch lives in a block of its own, differently named per
/// engine, so both spellings are pinned here against the parameter database they were read from.</summary>
public class PartialSwitchesTests
{
    private const string SnOffset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string PcmOffset = "Offset/Temporary PCM Synth Tone";
    private const string Start = "Temporary Tone Part 1";

    private static Integra7Snapshot Tone(string toneType, params SnapshotDomain[] domains) =>
        new(Integra7Snapshot.CurrentFormatVersion, "a", [.. domains], SnapshotKinds.Tone, toneType);

    private static SnapshotDomain Block(string offset, string offset2, params SnapshotValue[] values) =>
        new(Start, offset, offset2, [.. values]);

    /// <summary>An SN-S tone whose three partial switches hold the given raws.</summary>
    private static Integra7Snapshot SnTone(long partial1, long partial2, long partial3) =>
        Tone("SN-S",
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Common",
                new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
                new SnapshotValue("SuperNATURAL Synth Tone Common/Partial1 Switch",
                    partial1 == 1 ? "ON" : "OFF", partial1),
                new SnapshotValue("SuperNATURAL Synth Tone Common/Partial2 Switch",
                    partial2 == 1 ? "ON" : "OFF", partial2),
                new SnapshotValue("SuperNATURAL Synth Tone Common/Partial3 Switch",
                    partial3 == 1 ? "ON" : "OFF", partial3)),
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Partial 1"),
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Partial 2"),
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Partial 3"));

    /// <summary>A PCM Synth tone, whose four switches sit in the Partial Mix Table under the other
    /// spelling.</summary>
    private static Integra7Snapshot PcmTone(long partial1, long partial2)
        => Tone("PCMS",
            Block(PcmOffset, "Offset2/PCM Synth Tone Partial Mix Table",
                new SnapshotValue("PCM Synth Tone Partial Mix Table/PMT 1 Partial Switch",
                    partial1 == 1 ? "ON" : "OFF", partial1),
                new SnapshotValue("PCM Synth Tone Partial Mix Table/PMT 2 Partial Switch",
                    partial2 == 1 ? "ON" : "OFF", partial2)),
            Block(PcmOffset, "Offset2/PCM Synth Tone Partial 1"),
            Block(PcmOffset, "Offset2/PCM Synth Tone Partial 2"));

    [Test]
    public void A_switched_on_supernatural_partial_is_on()
    {
        Assert.That(PartialSwitches.IsOn(SnTone(1, 1, 1), "Offset2/SuperNATURAL Synth Tone Partial 2"),
            Is.True);
    }

    [Test]
    public void A_switched_off_supernatural_partial_is_off()
    {
        Assert.That(PartialSwitches.IsOn(SnTone(1, 0, 1), "Offset2/SuperNATURAL Synth Tone Partial 2"),
            Is.False);
        Assert.That(PartialSwitches.IsOn(SnTone(1, 0, 1), "Offset2/SuperNATURAL Synth Tone Partial 1"),
            Is.True, "each partial is asked of its own switch, not of the first one");
    }

    /// <summary>The other engine, and the other spelling: "PMT 2 Partial Switch" with a space where the
    /// SuperNATURAL Synth has none. Getting this wrong answers null for every PCM partial, which looks
    /// like the feature simply not applying rather than like a typo.</summary>
    [Test]
    public void A_pcm_synth_partial_is_read_from_the_partial_mix_table()
    {
        Assert.That(PartialSwitches.IsOn(PcmTone(1, 0), "Offset2/PCM Synth Tone Partial 1"), Is.True);
        Assert.That(PartialSwitches.IsOn(PcmTone(1, 0), "Offset2/PCM Synth Tone Partial 2"), Is.False);
    }

    [Test]
    public void A_block_that_is_not_a_partial_has_no_answer()
    {
        Assert.That(PartialSwitches.IsOn(SnTone(1, 1, 1), "Offset2/SuperNATURAL Synth Tone Common"),
            Is.Null);
        Assert.That(PartialSwitches.IsOn(SnTone(1, 1, 1), "Offset2/SuperNATURAL Synth Tone Common MFX"),
            Is.Null);
    }

    /// <summary>The block holding the PCM switches shares its whole prefix with the blocks it governs, so
    /// a prefix test alone would ask it whether it is switched on.</summary>
    [Test]
    public void The_partial_mix_table_is_not_itself_a_partial()
    {
        Assert.That(PartialSwitches.IsOn(PcmTone(1, 1), "Offset2/PCM Synth Tone Partial Mix Table"),
            Is.Null);
    }

    /// <summary>A drum kit's notes all exist; there is no switch to read, and answering "off" for one
    /// would be an invention.</summary>
    [Test]
    public void A_drum_partial_has_no_switch_and_so_no_answer()
    {
        var kit = Tone("SN-D",
            Block("Offset/Temporary SuperNATURAL Drum Kit", "Offset2/SuperNATURAL Drum Kit Common"),
            Block("Offset/Temporary SuperNATURAL Drum Kit", "Offset2/SuperNATURAL Drum Kit Partial 6"));

        Assert.That(PartialSwitches.IsOn(kit, "Offset2/SuperNATURAL Drum Kit Partial 6"), Is.Null);
        Assert.That(PartialSwitches.IsOn(kit, "Offset2/PCM Drum Kit Partial 6"), Is.Null);
    }

    /// <summary>A snapshot need not hold every block -- an older file, or one written by hand. Unknown is
    /// the answer, and a caller that says nothing is better than one that says "off".</summary>
    [Test]
    public void A_snapshot_without_the_governing_block_has_no_answer()
    {
        var partialOnly = Tone("SN-S", Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Partial 2"));

        Assert.That(PartialSwitches.IsOn(partialOnly, "Offset2/SuperNATURAL Synth Tone Partial 2"),
            Is.Null);
    }

    /// <summary>The governing block being present is not the same as it carrying this switch.</summary>
    [Test]
    public void A_governing_block_without_the_switch_has_no_answer()
    {
        var withoutSwitches = Tone("SN-S",
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Common",
                new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100)));

        Assert.That(PartialSwitches.IsOn(withoutSwitches, "Offset2/SuperNATURAL Synth Tone Partial 2"),
            Is.Null);
    }

    /// <summary>A partial number the engine does not have. SN-S has three; there is no fourth switch to
    /// find, so there is nothing to say about a fourth partial.</summary>
    [Test]
    public void A_partial_the_engine_does_not_have_has_no_answer()
    {
        Assert.That(PartialSwitches.IsOn(SnTone(1, 1, 1), "Offset2/SuperNATURAL Synth Tone Partial 4"),
            Is.Null);
    }

    /// <summary>The raw is what the device stores and what decides. The string is the fallback for a
    /// snapshot that carries no raw for it.</summary>
    [Test]
    public void The_string_decides_only_when_there_is_no_raw()
    {
        var noRaw = Tone("SN-S",
            Block(SnOffset, "Offset2/SuperNATURAL Synth Tone Common",
                new SnapshotValue("SuperNATURAL Synth Tone Common/Partial1 Switch", "on"),
                new SnapshotValue("SuperNATURAL Synth Tone Common/Partial2 Switch", "OFF")));

        Assert.That(PartialSwitches.IsOn(noRaw, "Offset2/SuperNATURAL Synth Tone Partial 1"), Is.True,
            "case-insensitively");
        Assert.That(PartialSwitches.IsOn(noRaw, "Offset2/SuperNATURAL Synth Tone Partial 2"), Is.False);
    }
}
