using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestMfxCatalog
{
    [Test]
    public void Families_cover_every_type_0_to_67_exactly_once()
    {
        var all = MfxCatalog.Families.SelectMany(f => f.TypeIndices).ToList();
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 68), all);
        Assert.That(all.Count, Is.EqualTo(68), "no duplicates");
    }

    [Test]
    public void FamilyOf_and_TypesIn_round_trip()
    {
        for (var i = 0; i < 68; i++)
        {
            var fam = MfxCatalog.FamilyOf(i);
            Assert.That(MfxCatalog.TypesIn(fam), Does.Contain(i), $"type {i} should be in family {fam}");
        }
    }

    [Test]
    public void FamilyOf_out_of_range_returns_empty()
    {
        Assert.That(MfxCatalog.FamilyOf(-1), Is.EqualTo(""));
        Assert.That(MfxCatalog.FamilyOf(68), Is.EqualTo(""));
    }

    [Test]
    public void FriendlyParamNames_strips_effect_prefix_for_multi_param_type()
    {
        var r = MfxCatalog.FriendlyParamNames("Equalizer",
            new[] { "Equalizer Low Freq", "Equalizer Low Gain", "Equalizer High Freq" });
        Assert.That(r, Is.EqualTo(new[] { "Low Freq", "Low Gain", "High Freq" }));
    }

    [Test]
    public void FriendlyParamNames_strips_dotted_effect_name()
    {
        var r = MfxCatalog.FriendlyParamNames("Time Ctrl. Delay",
            new[] { "Time Ctrl. Delay Time (ms-note)", "Time Ctrl. Delay Feedback" });
        Assert.That(r, Is.EqualTo(new[] { "Time (ms-note)", "Feedback" }));
    }

    [Test]
    public void FriendlyParamNames_strips_combo_arrow_effect_name()
    {
        var r = MfxCatalog.FriendlyParamNames("Overdrive->Chorus",
            new[] { "Overdrive->Chorus Overdrive Drive", "Overdrive->Chorus Chorus Rate" });
        Assert.That(r, Is.EqualTo(new[] { "Overdrive Drive", "Chorus Rate" }));
    }

    [Test]
    public void FriendlyParamNames_punctuation_mismatch_still_trims_and_never_empty()
    {
        // MFX_TYPE name uses '/', the leaf names use '-' — typed strip fails, common-prefix path wins.
        var r = MfxCatalog.FriendlyParamNames("Overdrive/Distortion->TouchWah",
            new[] { "Overdrive-Distortion->TouchWah Drive Switch", "Overdrive-Distortion->TouchWah Type" });
        Assert.That(r, Is.EqualTo(new[] { "Drive Switch", "Type" }));
    }

    [Test]
    public void FriendlyParamName_single_is_never_empty()
    {
        Assert.That(MfxCatalog.FriendlyParamName("Equalizer", "Equalizer Low Freq"), Is.EqualTo("Low Freq"));
        // Degenerate: leaf equals the effect name -> fall back to the raw leaf, not empty.
        Assert.That(MfxCatalog.FriendlyParamName("Chorus", "Chorus"), Is.EqualTo("Chorus"));
    }
}
