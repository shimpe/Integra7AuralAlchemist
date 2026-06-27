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
}
