using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveformBanks
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "StGrand pA L", [2] = "StGrand pA R" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "Kick 1 Menu", [1] = "Kick 2 MenuL" },
    });

    [Test]
    public void ParseWaveCsv_SkipsHeader_AndIndexesFromZero()
    {
        using var r = new StringReader("// header comment\n\"Off\"\n\"Wave A\"\n\"Wave B\"\n");
        var d = WaveformBanks.ParseWaveCsv(r);
        Assert.That(d[0], Is.EqualTo("Off"));
        Assert.That(d[1], Is.EqualTo("Wave A"));
        Assert.That(d[2], Is.EqualTo("Wave B"));
        Assert.That(d.Count, Is.EqualTo(3));
    }

    [Test]
    public void Name_ResolvesViaSelectedBank()
    {
        var b = Sample();
        Assert.That(b.Name("Internal", 0, 1), Is.EqualTo("StGrand pA L"));
        Assert.That(b.Name("SRX", 1, 1), Is.EqualTo("Kick 2 MenuL"));
    }

    [Test]
    public void Name_OutOfRange_ReturnsRawNumber()
        => Assert.That(Sample().Name("Internal", 0, 999), Is.EqualTo("999"));

    [Test]
    public void Number_ReverseLookup()
    {
        Assert.That(Sample().Number("Internal", 0, "StGrand pA R"), Is.EqualTo(2));
        Assert.That(Sample().Number("Internal", 0, "nope"), Is.Null);
    }

    [Test]
    public void FirstWave_ReturnsLowestKey()
    {
        Assert.That(Sample().FirstWave("Internal", 0), Is.EqualTo(0));
        Assert.That(Sample().FirstWave("SRX", 1), Is.EqualTo(0));
    }

    [Test]
    public void Bank_UnknownBank_ReturnsNull()
        => Assert.That(Sample().Bank("SRX", 7), Is.Null); // SRX7 not in sample
}
