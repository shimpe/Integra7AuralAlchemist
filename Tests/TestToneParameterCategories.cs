using System;
using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What may be randomised, and what may never be.
///
/// These run against the real parameter database rather than against invented paths, so a parameter
/// this build renames stops being categorised and a test says so, instead of it silently dropping out
/// of randomisation with nothing to notice.</summary>
public class ToneParameterCategoriesTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    [TestCase("SuperNATURAL Synth Tone Partial/OSC Pitch", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Synth Tone Partial/OSC Pitch Env Depth", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Synth Tone Partial/OSC Wave", ToneCategory.WaveChoice)]
    [TestCase("SuperNATURAL Synth Tone Partial/Filter Cutoff", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Synth Tone Partial/Filter Env Attack Time", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Synth Tone Partial/AMP Env Decay Time", ToneCategory.Amplifier)]
    [TestCase("SuperNATURAL Synth Tone Partial/Modulation LFO Rate", ToneCategory.LfoAndModulation)]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Parameter 1", ToneCategory.Effects)]
    [TestCase("PCM Synth Tone Partial/TVF Cutoff Frequency", ToneCategory.Filter)]
    [TestCase("PCM Synth Tone Partial/TVA Env Time 1", ToneCategory.Amplifier)]
    [TestCase("PCM Synth Tone Partial/LFO1 Rate", ToneCategory.LfoAndModulation)]
    [TestCase("PCM Synth Tone Partial/LFO Step 1", ToneCategory.LfoAndModulation)]
    [TestCase("PCM Synth Tone Partial/Wave Number L (Mono)", ToneCategory.WaveChoice)]
    [TestCase("PCM Synth Tone Common/Cutoff Offset", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Acoustic Tone Common/Modify Parameter 1", ToneCategory.InstrumentCharacter)]
    [TestCase("SuperNATURAL Acoustic Tone Common/Vibrato Rate", ToneCategory.LfoAndModulation)]
    [TestCase("SuperNATURAL Drum Kit Partial/Brilliance", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Drum Kit Partial/Tune", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Drum Kit Partial/Inst Number", ToneCategory.WaveChoice)]
    [TestCase("PCM Drum Kit Partial/WMT1 Wave Number L (Mono)", ToneCategory.WaveChoice)]
    [TestCase("PCM Drum Kit Partial/WMT3 Wave Coarse Tune", ToneCategory.PitchAndOscillator)]
    [TestCase("PCM Drum Kit Partial/TVF Cutoff Frequency", ToneCategory.Filter)]
    public void Categorises_a_parameter(string path, ToneCategory expected)
    {
        Assert.That(ToneParameterCategories.For(path), Is.EqualTo(expected));
    }

    [TestCase("PCM Drum Kit Partial/Partial Output Assign")]
    [TestCase("SuperNATURAL Drum Kit Partial/Output Assign")]
    [TestCase("PCM Drum Kit Partial/Partial Name")]
    [TestCase("PCM Drum Kit Partial/Assign Type")]
    [TestCase("PCM Drum Kit Partial/Mute Group")]
    [TestCase("PCM Drum Kit Partial/WMT1 Velocity Range Lower")]
    [TestCase("PCM Synth Tone Common/PCM Synth Tone Name")]
    [TestCase("PCM Synth Tone Common/Matrix Control 1 Source")]
    [TestCase("PCM Synth Tone Partial/Partial Receive Sustain")]
    [TestCase("PCM Synth Tone Partial Mix Table/PMT 1 Keyboard Range Lower")]
    [TestCase("SuperNATURAL Synth Tone Common/Tone Name")]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Type")]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Control 1 Source")]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Control 1 Sens")]
    [TestCase("PCM Synth Tone Common MFX/MFX Control Assign 1")]
    [TestCase("SuperNATURAL Acoustic Tone Common/Instrument")]
    [TestCase("SuperNATURAL Drum Kit Common Comp-EQ/Comp1 Switch")]
    [TestCase("Studio Set Part/Part Output Assign")]
    public void Never_randomises_routing_identity_or_control_assignments(string path)
    {
        Assert.That(ToneParameterCategories.For(path), Is.Null);
    }

    /// <summary>Reserved parameters are excluded by the caller's GetRelevantParameters(false, false)
    /// too, but a rule that swept one up would be a rule matching more than it should, so the table
    /// itself has to refuse them.</summary>
    [Test]
    public void Never_categorises_a_reserved_parameter()
    {
        var reserved = _parameters.GetParametersWithPrefix("SuperNATURAL Synth Tone")
            .Where(p => p.Path.Contains("/Reserved"))
            .Select(p => p.Path)
            .ToList();

        Assert.That(reserved, Is.Not.Empty, "the fixture assumes this build has reserved parameters");
        foreach (var path in reserved)
            Assert.That(ToneParameterCategories.For(path), Is.Null, path);
    }

    [Test]
    public void Reports_which_categories_an_engine_has()
    {
        Assert.That(ToneParameterCategories.PresentIn("SN-A"),
            Does.Contain(ToneCategory.InstrumentCharacter));
        Assert.That(ToneParameterCategories.PresentIn("SN-S"),
            Does.Not.Contain(ToneCategory.InstrumentCharacter));
        foreach (var engine in new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" })
            Assert.That(ToneParameterCategories.PresentIn(engine), Does.Contain(ToneCategory.Filter),
                engine);
    }

    /// <summary>Every path a randomise will really be offered comes from these blocks, so the table is
    /// checked against them rather than against a list written here: a block that gains a parameter this
    /// build does not categorise is fine (unmapped means untouched), but a *rule* that matches nothing at
    /// all is a typo, and this is what catches it.</summary>
    /// <summary>An address names a partial by number ("Offset2/PCM Synth Tone Partial 3"); a parameter
    /// path names the block generically ("PCM Synth Tone Partial/TVF Cutoff Frequency"). Only the
    /// trailing number after the word "Partial" is dropped -- "PCM Synth Tone Common 2" is a block in its
    /// own right, and stripping its 2 would look up the wrong rules.</summary>
    private static string BlockNameOf(string offset2)
    {
        var name = offset2["Offset2/".Length..];
        var space = name.LastIndexOf(' ');
        if (space <= 0 || !int.TryParse(name[(space + 1)..], out _)) return name;

        var beforeNumber = name[..space];
        return beforeNumber.EndsWith(" Partial", StringComparison.Ordinal) ? beforeNumber : name;
    }

    [Test]
    public void Every_engine_has_at_least_one_parameter_in_every_category_it_claims()
    {
        foreach (var engine in new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" })
        {
            var found = ToneDomainNames.For(engine, 0)
                .SelectMany(b => _parameters.GetParametersWithPrefix(BlockNameOf(b.Offset2) + "/"))
                .Select(p => ToneParameterCategories.For(p.Path))
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .ToHashSet();

            Assert.That(found, Is.EquivalentTo(ToneParameterCategories.PresentIn(engine)), engine);
        }
    }
}
