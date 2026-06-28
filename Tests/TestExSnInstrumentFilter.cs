using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestExSnInstrumentFilter
{
    [Test]
    public void ExSnBoardOf_ParsesPrefix()
    {
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("ExSN3 001: TC Guitar w/Fing"), Is.EqualTo(3));
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("ExSN5 011: Mute French Horn"), Is.EqualTo(5));
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("INT 001: Concert Grand"), Is.Null);
        Assert.That(ExSnInstrumentFilter.ExSnBoardOf("Strings"), Is.Null);
    }

    [Test]
    public void VisibleFamilies_DropsExpansionWhenNoneLoaded()
    {
        var all = new[] { "Pianos", "Expansion" };
        Assert.That(ExSnInstrumentFilter.VisibleFamilies(all, new int[0]), Is.EqualTo(new[] { "Pianos" }));
        Assert.That(ExSnInstrumentFilter.VisibleFamilies(all, new[] { 2 }), Is.EqualTo(new[] { "Pianos", "Expansion" }));
    }

    [Test]
    public void LoadedExpansionIndices_KeepsOnlyLoadedBoards()
    {
        var names = new List<string> { "ExSN1 001: A", "ExSN2 001: B", "ExSN3 001: C" };
        var expansion = new[] { 0, 1, 2 };
        Assert.That(ExSnInstrumentFilter.LoadedExpansionIndices(expansion, names, new[] { 3 }),
            Is.EqualTo(new[] { 2 }));
        Assert.That(ExSnInstrumentFilter.LoadedExpansionIndices(expansion, names, new[] { 1, 2 }),
            Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void DisplayName_LabelsUnloadedExSn()
    {
        Assert.That(ExSnInstrumentFilter.DisplayName("ExSN3 001: C", new[] { 2 }), Is.EqualTo("ExSN3 001: C (not loaded)"));
        Assert.That(ExSnInstrumentFilter.DisplayName("ExSN3 001: C", new[] { 3 }), Is.EqualTo("ExSN3 001: C"));
        Assert.That(ExSnInstrumentFilter.DisplayName("INT 001: Y", new int[0]), Is.EqualTo("INT 001: Y"));
    }
}
