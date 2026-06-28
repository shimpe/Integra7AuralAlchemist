using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveOutOfRangeReset
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "A", [2] = "B" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "K1", [1] = "K2" }, // only 0,1 valid
    });

    [Test]
    public void NeedsReset_TrueWhenNumberNotInNewBank()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 1, 2), Is.True); // SRX1 has no #2

    [Test]
    public void NeedsReset_FalseWhenNumberInBank()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 1, 1), Is.False);

    [Test]
    public void NeedsReset_FalseWhenBankMissing()
        => Assert.That(WaveOutOfRangeReset.NeedsReset(Sample(), "SRX", 9, 0), Is.False); // SRX9 absent → no reset

    [Test]
    public void IsWaveGroupDiscriminator_RecognizesTypeAndId()
    {
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Synth Tone Partial/Wave Group Type"), Is.True);
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Drum Kit Partial/WMT2 Wave Group ID"), Is.True);
        Assert.That(WaveOutOfRangeReset.IsWaveGroupDiscriminator("PCM Synth Tone Partial/Wave Number L (Mono)"), Is.False);
    }
}
