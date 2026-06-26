using System.Linq;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestInstrumentCatalog
{
    [Test]
    public void Families_cover_every_instrument_index_exactly_once()
    {
        var all = InstrumentCatalog.Families.SelectMany(f => f.Indices).ToList();
        CollectionAssert.AreEquivalent(Enumerable.Range(0, InstrumentCatalog.Count), all);
        Assert.That(all.Count, Is.EqualTo(InstrumentCatalog.Count), "no duplicate indices");
    }

    [Test]
    public void FamilyOf_and_ValuesIn_round_trip()
    {
        for (var i = 0; i < InstrumentCatalog.Count; i++)
            Assert.That(InstrumentCatalog.ValuesIn(InstrumentCatalog.FamilyOf(i)), Does.Contain(i));
    }
}
