using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
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

    /// <summary>Applies has to agree with Apply exactly: it exists so a caller can skip building the
    /// banks when Apply would do nothing, and if it ever said "no" where Apply would have resolved a
    /// name, wave names would silently stop updating after a read.</summary>
    [Test]
    public void Applies_MatchesWhetherApplyWouldDoAnything()
    {
        var parameters = TestFailedReadKeepsValues.LoadParameters();
        var addresses = new Integra7StartAddresses();
        var api = new TestFailedReadKeepsValues.SilentApi();
        var domain = new Integra7Domain(api, addresses, parameters);

        // A Studio Set block: no wave parameters, so Apply is a no-op and Applies must say so.
        var studioSet = domain.GetDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common");
        Assert.That(WaveNameResolution.Applies(studioSet.GetRelevantParameters(true, true)), Is.False);

        // A PCM synth partial: the registry's own parameters live here.
        var partial = domain.PCMSynthTonePartial(0, 0);
        Assert.That(WaveNameResolution.Applies(partial.GetRelevantParameters(true, true)), Is.True);
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
