using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestPartialAudition
{
    private static readonly bool[] AllOn = { true, true, true };

    [Test]
    public void No_solo_no_mute_is_identity()
    {
        var saved = new[] { true, false, true };
        Assert.That(PartialAudition.Effective(saved, new[] { false, false, false }, new[] { false, false, false }),
            Is.EqualTo(saved));
    }

    [Test]
    public void Mute_silences_only_that_partial()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { false, false, false }, new[] { false, true, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void Muting_a_saved_off_partial_stays_off()
    {
        var eff = PartialAudition.Effective(new[] { true, false, true }, new[] { false, false, false }, new[] { false, true, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void Solo_isolates_regardless_of_saved_or_mute()
    {
        var eff = PartialAudition.Effective(new[] { true, true, true }, new[] { true, false, false }, new[] { false, false, true });
        Assert.That(eff, Is.EqualTo(new[] { true, false, false }));
    }

    [Test]
    public void Solo_a_saved_off_partial_sounds()
    {
        var eff = PartialAudition.Effective(new[] { false, false, false }, new[] { false, false, true }, new[] { false, false, false });
        Assert.That(eff, Is.EqualTo(new[] { false, false, true }));
    }

    [Test]
    public void Solo_overrides_mute_on_same_partial()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { true, false, false }, new[] { true, false, false });
        Assert.That(eff[0], Is.True);
    }

    [Test]
    public void Multiple_solos_sound_together()
    {
        var eff = PartialAudition.Effective(AllOn, new[] { true, false, true }, new[] { false, false, false });
        Assert.That(eff, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void IsAuditioning_reflects_any_solo_or_mute()
    {
        Assert.That(PartialAudition.IsAuditioning(new[] { false, false, false }, new[] { false, false, false }), Is.False);
        Assert.That(PartialAudition.IsAuditioning(new[] { false, true, false }, new[] { false, false, false }), Is.True);
        Assert.That(PartialAudition.IsAuditioning(new[] { false, false, false }, new[] { true, false, false }), Is.True);
    }
}
