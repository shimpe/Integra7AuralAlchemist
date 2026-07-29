using System.Collections.Generic;
using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Blending two tones. Against the real parameter database, because what may be interpolated
/// and what may not is a property of it: a parameter with a Repr is a list of labels, and one naming a
/// parent exists only while that parent holds a particular value.</summary>
public class MorphedToneTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";
    private const string Common = "Offset2/SuperNATURAL Synth Tone Common";
    private const string Partial = "Offset2/SuperNATURAL Synth Tone Partial 1";

    private static Integra7Snapshot Tone(string name, long level, long filterMode, string toneName) =>
        new(Integra7Snapshot.CurrentFormatVersion, name,
            [
                new SnapshotDomain("Temporary Tone Part 1", Offset, Common,
                [
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", $"{level}", level),
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", toneName),
                ]),
                new SnapshotDomain("Temporary Tone Part 1", Offset, Partial,
                [
                    new SnapshotValue("SuperNATURAL Synth Tone Partial/Filter Mode",
                        $"{filterMode}", filterMode),
                ]),
            ],
            SnapshotKinds.Tone, "SN-S");

    [Test]
    public void A_numeric_parameter_is_the_weighted_average()
    {
        var a = Tone("a", 100, 1, "A");
        var b = Tone("b", 0, 1, "B");

        var blend = MorphedTone.Blend([a, b], [0.75, 0.25], winner: 0, _parameters);

        var level = blend.Domains.Single(d => d.Offset2 == Common).Values
            .Single(v => v.Path.EndsWith("Tone Level"));
        Assert.That(level.Raw, Is.EqualTo(75));
    }

    [Test]
    public void A_numeric_parameter_never_leaves_its_range()
    {
        var a = Tone("a", 127, 1, "A");
        var b = Tone("b", 127, 1, "B");

        var blend = MorphedTone.Blend([a, b], [0.5, 0.5], winner: 0, _parameters);

        var level = blend.Domains.Single(d => d.Offset2 == Common).Values
            .Single(v => v.Path.EndsWith("Tone Level"));
        Assert.That(level.Raw, Is.InRange(0, 127));
    }

    /// <summary>Filter Mode is a list of labels: Low pass is not half way between Bypass and Peaking.
    /// </summary>
    [Test]
    public void A_discrete_parameter_comes_from_the_winner()
    {
        var a = Tone("a", 0, 1, "A");
        var b = Tone("b", 0, 5, "B");

        var blend = MorphedTone.Blend([a, b], [0.5, 0.5], winner: 1, _parameters);

        var mode = blend.Domains.Single(d => d.Offset2 == Partial).Values
            .Single(v => v.Path.EndsWith("Filter Mode"));
        Assert.That(mode.Raw, Is.EqualTo(5), "the winner's, not an average");
    }

    [Test]
    public void The_name_comes_from_the_winner()
    {
        var a = Tone("a", 0, 1, "Warm Pad");
        var b = Tone("b", 0, 1, "Glass Bell");

        var blend = MorphedTone.Blend([a, b], [0.9, 0.1], winner: 1, _parameters);

        var name = blend.Domains.Single(d => d.Offset2 == Common).Values
            .Single(v => v.Path.EndsWith("Tone Name"));
        Assert.That(name.Value, Is.EqualTo("Glass Bell"));
    }

    /// <summary>The case that makes this more than an average: "MFX Parameter 1" is one effect's control
    /// under one MFX Type and a different effect's under another, which is why the database gives every
    /// variant its own path naming MFX Type as its parent -- Enhancer Sens exists only while the type is
    /// Enhancer. Any parameter with a parent follows the corner that won the type instead of being
    /// blended, and that holds even here, where both corners are on the same effect and so both carry the
    /// very same path. Averaging it would be arithmetic across two independently-tuned effects.</summary>
    [Test]
    public void A_parameter_governed_by_a_discriminator_comes_from_the_winner()
    {
        const string mfx = "Offset2/SuperNATURAL Synth Tone Common MFX";
        const string path = "SuperNATURAL Synth Tone Common MFX/MFX Parameter 1/Enhancer Sens";
        const long enhancer = 5;

        Integra7Snapshot WithMfx(string name, long type, long p1) =>
            new(Integra7Snapshot.CurrentFormatVersion, name,
                [
                    new SnapshotDomain("Temporary Tone Part 1", Offset, mfx,
                    [
                        new SnapshotValue("SuperNATURAL Synth Tone Common MFX/MFX Type", $"{type}", type),
                        new SnapshotValue(path, $"{p1}", p1),
                    ]),
                ],
                SnapshotKinds.Tone, "SN-S");

        var blend = MorphedTone.Blend([WithMfx("a", enhancer, 20000), WithMfx("b", enhancer, 40000)],
            [0.5, 0.5], winner: 1, _parameters);

        var values = blend.Domains.Single().Values;
        Assert.That(values.Single(v => v.Path.EndsWith("MFX Type")).Raw, Is.EqualTo(enhancer));
        Assert.That(values.Single(v => v.Path == path).Raw, Is.EqualTo(40000), "not 30000");
    }

    /// <summary>PCM Synth's wave selection, which is the case the Repr test above does not catch: unlike
    /// SuperNATURAL Synth's Wave Number, none of these three carries one. A wave number is a position in a
    /// table of thousands of unrelated samples, so 300 is not between 100 and 500 in any sense a listener
    /// would recognise, and Wave Group ID is besides a discriminator -- a number resolved against it is
    /// meaningless once it names a bank neither corner used.</summary>
    [Test]
    public void A_pcm_synth_wave_selection_comes_from_the_winner_whole()
    {
        const string offset = "Offset/Temporary PCM Synth Tone";
        const string partial = "Offset2/PCM Synth Tone Partial 1";
        const string groupId = "PCM Synth Tone Partial/Wave Group ID";
        const string waveL = "PCM Synth Tone Partial/Wave Number L (Mono)";

        Integra7Snapshot WithWave(string name, long bank, long wave, long level) =>
            new(Integra7Snapshot.CurrentFormatVersion, name,
                [
                    new SnapshotDomain("Temporary Tone Part 1", offset, partial,
                    [
                        new SnapshotValue(groupId, $"{bank}", bank),
                        new SnapshotValue(waveL, $"{wave}", wave),
                        new SnapshotValue("PCM Synth Tone Partial/Partial Level", $"{level}", level),
                    ]),
                ],
                SnapshotKinds.Tone, "PCMS");

        var blend = MorphedTone.Blend([WithWave("a", 0, 100, 0), WithWave("b", 1, 500, 100)],
            [0.5, 0.5], winner: 1, _parameters);

        var values = blend.Domains.Single().Values;
        Assert.Multiple(() =>
        {
            Assert.That(values.Single(v => v.Path == groupId).Raw, Is.EqualTo(1), "the winner's bank");
            Assert.That(values.Single(v => v.Path == waveL).Raw, Is.EqualTo(500), "not 300");
            Assert.That(values.Single(v => v.Path.EndsWith("Partial Level")).Raw, Is.EqualTo(50),
                "and the level beside it is still blended, so the guard is not simply catching everything");
        });
    }

    /// <summary>An older corner file may lack a parameter the others carry. Taking the winner's value is
    /// the only safe answer -- treating it as zero would silence or detune the blend -- and the caller is
    /// told so it can say so once.</summary>
    [Test]
    public void A_parameter_only_some_corners_carry_comes_from_the_winner_and_is_reported()
    {
        var full = Tone("a", 100, 1, "A");
        var sparse = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "b",
            [
                new SnapshotDomain("Temporary Tone Part 1", Offset, Common,
                    [new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", "B")]),
                new SnapshotDomain("Temporary Tone Part 1", Offset, Partial,
                    [new SnapshotValue("SuperNATURAL Synth Tone Partial/Filter Mode", "1", 1)]),
            ],
            SnapshotKinds.Tone, "SN-S");

        var blend = MorphedTone.Blend([full, sparse], [0.5, 0.5], winner: 0, _parameters,
            out var incomplete);

        var level = blend.Domains.Single(d => d.Offset2 == Common).Values
            .Single(v => v.Path.EndsWith("Tone Level"));
        Assert.That(level.Raw, Is.EqualTo(100), "the winner's, not half of it");
        Assert.That(incomplete, Is.True);
    }

    /// <summary>Two corners on different effects carry different variant paths, because a capture only
    /// holds the variants its own MFX Type makes valid. The discriminator rule answers that before the
    /// missing-parameter rule can, so the winner's effect comes across whole and the blend is not
    /// reported as incomplete -- which would otherwise fire on every pad whose corners disagree.</summary>
    [Test]
    public void Corners_on_different_effects_are_not_reported_incomplete()
    {
        const string mfx = "Offset2/SuperNATURAL Synth Tone Common MFX";

        Integra7Snapshot WithMfx(string name, long type, string path, long p1) =>
            new(Integra7Snapshot.CurrentFormatVersion, name,
                [
                    new SnapshotDomain("Temporary Tone Part 1", Offset, mfx,
                    [
                        new SnapshotValue("SuperNATURAL Synth Tone Common MFX/MFX Type", $"{type}", type),
                        new SnapshotValue(path, $"{p1}", p1),
                    ]),
                ],
                SnapshotKinds.Tone, "SN-S");

        var equalizer = WithMfx("a", 1,
            "SuperNATURAL Synth Tone Common MFX/MFX Parameter 1/Equalizer Low Freq", 20000);
        var enhancer = WithMfx("b", 5,
            "SuperNATURAL Synth Tone Common MFX/MFX Parameter 1/Enhancer Sens", 40000);

        var blend = MorphedTone.Blend([equalizer, enhancer], [0.5, 0.5], winner: 1, _parameters,
            out var incomplete);

        Assert.That(blend.Domains.Single().Values.Single(v => v.Path.EndsWith("Enhancer Sens")).Raw,
            Is.EqualTo(40000));
        Assert.That(incomplete, Is.False);
    }

    [Test]
    public void The_blend_keeps_the_engine_and_the_block_layout()
    {
        var a = Tone("a", 10, 1, "A");
        var b = Tone("b", 20, 1, "B");

        var blend = MorphedTone.Blend([a, b], [0.5, 0.5], winner: 0, _parameters);

        Assert.That(blend.Kind, Is.EqualTo(SnapshotKinds.Tone));
        Assert.That(blend.ToneType, Is.EqualTo("SN-S"));
        Assert.That(blend.Domains.Select(d => d.Offset2), Is.EqualTo(new[] { Common, Partial }));
    }
}
