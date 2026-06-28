using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveNameResolution
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "StGrand pA L" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "Kick 1 Menu", [1] = "Kick 2 MenuL" },
    });

    [Test]
    public void Resolve_PicksBankAndName()
    {
        var (bank, display) = WaveNameResolution.Resolve(Sample(), "SRX", 1, 1);
        Assert.That(display, Is.EqualTo("Kick 2 MenuL"));
        Assert.That(bank, Is.Not.Null);
        Assert.That(bank![0], Is.EqualTo("Kick 1 Menu"));
    }

    [Test]
    public void Resolve_Internal_UsesIntBank()
    {
        var (_, display) = WaveNameResolution.Resolve(Sample(), "Internal", 0, 1);
        Assert.That(display, Is.EqualTo("StGrand pA L"));
    }

    [Test]
    public void Registry_CoversAllTenWaveParams()
    {
        Assert.That(WaveBankRegistry.Entries.Count, Is.EqualTo(10));
        Assert.That(WaveBankRegistry.Entries.ContainsKey("PCM Synth Tone Partial/Wave Number L (Mono)"));
        Assert.That(WaveBankRegistry.Entries["PCM Synth Tone Partial/Wave Number L (Mono)"].TypePath,
            Is.EqualTo("PCM Synth Tone Partial/Wave Group Type"));
        Assert.That(WaveBankRegistry.Entries.ContainsKey("PCM Drum Kit Partial/WMT3 Wave Number R"));
        Assert.That(WaveBankRegistry.Entries["PCM Drum Kit Partial/WMT3 Wave Number R"].IdPath,
            Is.EqualTo("PCM Drum Kit Partial/WMT3 Wave Group ID"));
    }
}
