using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class DualEnvelopeHitTestTests
{
    private static SnsEnvelopeMapping.EnvPoints Env(double peakX) => new(
        new SnsEnvelopeMapping.Point(0, 100),
        new SnsEnvelopeMapping.Point(peakX, 0),
        new SnsEnvelopeMapping.Point(peakX + 10, 50),
        new SnsEnvelopeMapping.Point(peakX + 50, 50),
        new SnsEnvelopeMapping.Point(peakX + 90, 100));

    [Test]
    public void Picks_nearest_handle_of_either_envelope()
    {
        var hit = SnsEnvelopeMapping.NearestHandle(12, 2, Env(10), Env(200), activeEnv: 0, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(0));
        Assert.That(hit.Handle, Is.EqualTo(0));
    }

    [Test]
    public void Returns_none_when_outside_radius()
    {
        var hit = SnsEnvelopeMapping.NearestHandle(500, 500, Env(10), Env(200), activeEnv: 0, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(-1));
        Assert.That(hit.Handle, Is.EqualTo(-1));
    }

    [Test]
    public void Active_envelope_wins_ties()
    {
        var hit = SnsEnvelopeMapping.NearestHandle(100, 0, Env(100), Env(100), activeEnv: 1, radius: 14);
        Assert.That(hit.Env, Is.EqualTo(1));
    }
}
