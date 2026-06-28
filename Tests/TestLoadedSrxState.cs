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
}
