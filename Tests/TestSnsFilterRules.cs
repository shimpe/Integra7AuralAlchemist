using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsFilterRulesTests
{
    [TestCase("Bypass", "BYP")]
    [TestCase("Low pass", "LPF")]
    [TestCase("High pass", "HPF")]
    [TestCase("Band pass", "BPF")]
    [TestCase("Peaking", "PEAK")]
    [TestCase("Low pass 2", "LPF2")]
    [TestCase("Low pass 3", "LPF3")]
    [TestCase("Low pass 4", "LPF4")]
    [TestCase("something else", "something else")]
    public void Abbreviates_filter_mode(string mode, string expected)
        => Assert.That(SnsFilterRules.Abbrev(mode), Is.EqualTo(expected));
}
