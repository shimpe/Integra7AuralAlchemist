using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestLoadedSrxState
{
    [Test]
    public void SetFromSlots_KeepsOnlySrxBoards()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(2, 0, 6, 13); // 0 = Empty, 13 = ExSN1 -> dropped
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 2, 6 }));
    }

    [Test]
    public void SetFromSlots_Distinct()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(5, 5, 5, 5);
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 5 }));
    }

    [Test]
    public void SetFromSlots_AllEmpty_IsEmpty()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(0, 0, 0, 0);
        Assert.That(s.Boards, Is.Empty);
    }

    [Test]
    public void ExSnBoards_MapsSlots13to18_ToBoards1to6()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(13, 17, 0, 2); // 13->ExSN1, 17->ExSN5; 0 empty, 2 is SRX (ignored here)
        Assert.That(s.ExSnBoards, Is.EquivalentTo(new[] { 1, 5 }));
        Assert.That(s.Boards, Is.EquivalentTo(new[] { 2 })); // SRX side still correct
    }

    [Test]
    public void ExSnBoards_Distinct()
    {
        var s = new LoadedSrxState();
        s.SetFromSlots(14, 14, 14, 14);
        Assert.That(s.ExSnBoards, Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    public void Changed_FiresOnSetFromSlots()
    {
        var s = new LoadedSrxState();
        var fired = 0;
        s.Changed += () => fired++;
        s.SetFromSlots(13, 0, 0, 0);
        Assert.That(fired, Is.EqualTo(1));
    }
}
