using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPcmTvfRules
{
    [TestCase("Off", "Off")]
    [TestCase("Low-pass filter", "LPF")]
    [TestCase("Band-pass filter", "BPF")]
    [TestCase("High-pass filter", "HPF")]
    [TestCase("Peaking filter", "PKG")]
    [TestCase("Low-pass filter 2", "LPF2")]
    [TestCase("Low-pass filter 3", "LPF3")]
    public void Abbrev_maps_known_types(string input, string expected)
    {
        Assert.That(PcmTvfRules.Abbrev(input), Is.EqualTo(expected));
    }

    [Test]
    public void Abbrev_passes_through_unknown()
    {
        Assert.That(PcmTvfRules.Abbrev("Something Else"), Is.EqualTo("Something Else"));
    }

    [Test]
    public void Abbrev_handles_null_or_empty()
    {
        Assert.That(PcmTvfRules.Abbrev(null), Is.EqualTo(""));
        Assert.That(PcmTvfRules.Abbrev(""), Is.EqualTo(""));
    }

    [TestCase("Off", "Bypass")]
    [TestCase("Low-pass filter", "Low pass")]
    [TestCase("Band-pass filter", "Band pass")]
    [TestCase("High-pass filter", "High pass")]
    [TestCase("Peaking filter", "Peaking")]
    [TestCase("Low-pass filter 2", "Low pass")]
    [TestCase("Low-pass filter 3", "Low pass")]
    public void CurveMode_maps_to_FilterCurve_modes(string input, string expected)
    {
        Assert.That(PcmTvfRules.CurveMode(input), Is.EqualTo(expected));
    }

    [TestCase("Low-pass filter", false)]
    [TestCase("Low-pass filter 2", true)]
    [TestCase("Low-pass filter 3", true)]
    [TestCase("Off", false)]
    public void CurveSteep_flags_the_steeper_lowpass_variants(string input, bool expected)
    {
        Assert.That(PcmTvfRules.CurveSteep(input), Is.EqualTo(expected));
    }
}
