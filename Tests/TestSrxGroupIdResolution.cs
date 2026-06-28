using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestSrxGroupIdResolution
{
    [Test]
    public void VisibleBoards_MergesCurrentIntoLoaded_Sorted()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 6, 2 }, 5), Is.EqualTo(new[] { 2, 5, 6 }));

    [Test]
    public void VisibleBoards_NoDuplicateWhenCurrentAlreadyLoaded()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 2, 6 }, 6), Is.EqualTo(new[] { 2, 6 }));

    [Test]
    public void VisibleBoards_CurrentOnlyWhenNoneLoaded()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new int[0], 5), Is.EqualTo(new[] { 5 }));

    [Test]
    public void VisibleBoards_DropsOutOfRange()
        => Assert.That(SrxGroupIdResolution.VisibleBoards(new[] { 0, 13, 19 }, 0), Is.Empty);

    [Test]
    public void BuildRepr_Srx_LoadedBoardsAreUnlabelled()
    {
        var repr = SrxGroupIdResolution.BuildRepr("SRX", new[] { 2, 6 }, 6); // current is loaded
        Assert.That(repr, Is.Not.Null);
        Assert.That(repr!.Keys, Is.EquivalentTo(new[] { 2, 6 }));
        Assert.That(repr[2], Is.EqualTo("2"));
        Assert.That(repr[6], Is.EqualTo("6"));
    }

    [Test]
    public void BuildRepr_NonSrx_ReturnsNull()
        => Assert.That(SrxGroupIdResolution.BuildRepr("Internal", new[] { 2, 6 }, 5), Is.Null);

    [Test]
    public void BuildRepr_KeepsUnloadedCurrentBoard_LabelledNotLoaded()
    {
        var repr = SrxGroupIdResolution.BuildRepr("SRX", new[] { 2, 6 }, 9); // SRX9 referenced but not loaded
        Assert.That(repr!.ContainsKey(9), Is.True);
        Assert.That(repr[9], Is.EqualTo("9 (not loaded)"));
        Assert.That(repr[2], Is.EqualTo("2")); // loaded boards stay unlabelled
    }
}
