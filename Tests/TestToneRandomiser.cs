using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>How far a value may move, and what may not move at all.
///
/// Driven through a real domain built over the real parameter database rather than through invented
/// parameter specs: the rules being tested are about IMin/IMax, Repr and IsParent, which are properties
/// of the database, and a hand-rolled spec would let a wrong assumption about it pass.</summary>
public class ToneRandomiserTests
{
    private const string Block = "Offset2/SuperNATURAL Synth Tone Partial 1";
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";

    private static List<FullyQualifiedParameter> PartialParameters()
    {
        var domain = StudioSetSnapshotServiceTests.BuildDomain(
            new StudioSetSnapshotServiceTests.BlankReplyApi());
        return domain.GetDomain("Temporary Tone Part 1", Offset, Block)
            .GetRelevantParameters(false, false);
    }

    private static RandomisationStrengths All(double strength) =>
        new(Enum.GetValues<ToneCategory>().ToDictionary(c => c, _ => strength));

    [Test]
    public void Changes_nothing_at_strength_zero()
    {
        var values = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.0), new Random(1));

        Assert.That(values, Is.Empty);
    }

    [Test]
    public void Never_leaves_a_parameters_own_range()
    {
        var parameters = PartialParameters();
        var byPath = parameters.ToDictionary(p => p.ParSpec.Path);

        var values = ToneRandomiser.NewValuesFor(parameters, All(1.0), new Random(2));

        Assert.That(values, Is.Not.Empty);
        foreach (var (path, raw) in values)
        {
            var spec = byPath[path].ParSpec;
            Assert.That(raw, Is.InRange((long)spec.IMin, (long)spec.IMax), path);
        }
    }

    /// <summary>The point of a strength control: a low one produces a recognisable version of the sound
    /// that was there, not a new one. Cutoff runs 0..127, so 10 % is a window of 13 either way -- and
    /// the reading is 0 (BlankReplyApi answers with zeros), so the result must stay within 13.</summary>
    [Test]
    public void A_low_strength_only_nudges_a_numeric_value()
    {
        const string cutoff = "SuperNATURAL Synth Tone Partial/Filter Cutoff";
        var strengths = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 0.1 });

        // Many draws, because one could land near the middle of the window by luck.
        for (var seed = 0; seed < 50; seed++)
        {
            var values = ToneRandomiser.NewValuesFor(PartialParameters(), strengths, new Random(seed));
            if (values.TryGetValue(cutoff, out var raw))
                Assert.That(raw, Is.InRange(0L, 13L), $"seed {seed}");
        }
    }

    [Test]
    public void Leaves_an_enum_alone_at_low_strength_and_redraws_it_at_full()
    {
        const string mode = "SuperNATURAL Synth Tone Partial/Filter Mode";
        var timid = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 0.0001 });
        var bold = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 1.0 });

        var timidHits = 0;
        var boldHits = 0;
        for (var seed = 0; seed < 30; seed++)
        {
            if (ToneRandomiser.NewValuesFor(PartialParameters(), timid, new Random(seed))
                .ContainsKey(mode)) timidHits++;
            if (ToneRandomiser.NewValuesFor(PartialParameters(), bold, new Random(seed))
                .ContainsKey(mode)) boldHits++;
        }

        Assert.That(timidHits, Is.Zero, "an enum practically never moves at a strength of 0.01 %");
        Assert.That(boldHits, Is.EqualTo(30), "an enum always moves at full strength");
    }

    [Test]
    public void Never_returns_a_discriminator_a_name_or_an_uncategorised_parameter()
    {
        var domain = StudioSetSnapshotServiceTests.BuildDomain(
            new StudioSetSnapshotServiceTests.BlankReplyApi());
        var common = domain.GetDomain("Temporary Tone Part 1", Offset,
            "Offset2/SuperNATURAL Synth Tone Common").GetRelevantParameters(false, false);
        var mfx = domain.GetDomain("Temporary Tone Part 1", Offset,
            "Offset2/SuperNATURAL Synth Tone Common MFX").GetRelevantParameters(false, false);

        var values = ToneRandomiser.NewValuesFor(common.Concat(mfx), All(1.0), new Random(3));

        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common/Tone Name"));
        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common MFX/MFX Type"));
        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common/Partial1 Switch"));
        Assert.That(values.Keys.All(p => ToneParameterCategories.For(p) is not null));
    }

    /// <summary>The case the test above cannot make. Tone Name and MFX Type are skipped as a name and as
    /// a discriminator, but no rule categorises either of them, so that test stays green even with both
    /// skips deleted. Wave Group Type and Wave Group ID are discriminators that <i>are</i> categorised --
    /// WaveChoice, like the wave number beside them -- so the IsParent skip is the only thing keeping
    /// them out, and deleting it fails this.
    ///
    /// Several seeds, because the wave number reads as 0 (BlankReplyApi) and its window is symmetric
    /// about it, so roughly half the draws clamp back to 0 and record no change. The discriminators must
    /// be absent from every seed; the wave number need only move in one of them, which is what proves the
    /// category was randomised at all and that the two absences are the skip rather than an untouched
    /// category.</summary>
    [Test]
    public void Never_returns_a_discriminator_even_when_its_category_is_randomised()
    {
        const string groupType = "PCM Synth Tone Partial/Wave Group Type";
        const string groupId = "PCM Synth Tone Partial/Wave Group ID";
        const string waveNumber = "PCM Synth Tone Partial/Wave Number L (Mono)";

        var waveNumberMoved = false;
        for (var seed = 0; seed < 20; seed++)
        {
            var domain = StudioSetSnapshotServiceTests.BuildDomain(
                new StudioSetSnapshotServiceTests.BlankReplyApi());
            var parameters = domain
                .GetDomain("Temporary Tone Part 1", "Offset/Temporary PCM Synth Tone",
                    "Offset2/PCM Synth Tone Partial 1")
                .GetRelevantParameters(false, false);

            var values = ToneRandomiser.NewValuesFor(parameters, All(1.0), new Random(seed));

            Assert.That(values.Keys, Does.Not.Contain(groupType), $"seed {seed}");
            Assert.That(values.Keys, Does.Not.Contain(groupId), $"seed {seed}");
            waveNumberMoved |= values.ContainsKey(waveNumber);
        }

        Assert.That(waveNumberMoved, Is.True,
            "WaveChoice really was randomised, so the two absences above are the IsParent skip and not a "
            + "category that never moved");
    }

    /// <summary>Ticking one category must leave the others exactly as they were -- the dialog's whole
    /// promise. A category missing from the map is not the same as one present at zero, and both have to
    /// mean "do not touch it".</summary>
    [Test]
    public void Leaves_a_category_that_is_not_in_the_map_alone()
    {
        var filterOnly = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 1.0 });

        var values = ToneRandomiser.NewValuesFor(PartialParameters(), filterOnly, new Random(4));

        Assert.That(values, Is.Not.Empty);
        Assert.That(values.Keys.Select(ToneParameterCategories.For),
            Is.All.EqualTo(ToneCategory.Filter));
    }

    [Test]
    public void Is_deterministic_for_a_seed()
    {
        var first = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.5), new Random(7));
        var second = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.5), new Random(7));

        Assert.That(second, Is.EqualTo(first));
    }
}
