extern alias gen;
using gen::Integra7AuralAlchemist.ParameterGen;

namespace Tests;

public class TestIntegra7ParameterDatabaseAnalyzer
{
    public const bool USED = false;
    public const bool RESERVED = true;

    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test_MarkParentControls()
    {
        List<ParameterDef> db =
        [
            new(SpecType.NUMERIC, "a", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null),
            new(SpecType.NUMERIC, "b", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null,
                "a", "23"),
            new(SpecType.NUMERIC, "c", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null,
                "b", "1")
        ];
        Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(db);
        Assert.That(db[0].IsParent, Is.EqualTo(true)); // a is parent parameter for b
        Assert.That(db[1].IsParent, Is.EqualTo(true)); // b is parent parameter for c
        Assert.That(db[2].IsParent, Is.EqualTo(false)); // c is not a parent parameter
    }

    [Test]
    public void Test_SecondaryDeps()
    {
        List<ParameterDef> db =
        [
            new(SpecType.NUMERIC, "a", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null),
            new(SpecType.NUMERIC, "b", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null,
                "a", "23"),
            new(SpecType.NUMERIC, "c", [0x00, 0x00], 1, 4, 1, 4, 1, USED, false, "", null,
                "b", "1")
        ];
        Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(db);
        Assert.That(db[0].ParentCtrl, Is.EqualTo(""));
        Assert.That(db[0].ParentCtrlDispValue, Is.EqualTo(""));
        Assert.That(db[1].ParentCtrl, Is.EqualTo("a"));
        Assert.That(db[1].ParentCtrlDispValue, Is.EqualTo("23"));
        Assert.That(db[1].ParentCtrl2, Is.EqualTo(""));
        Assert.That(db[1].ParentCtrlDispValue2, Is.EqualTo(""));
        Assert.That(db[2].ParentCtrl, Is.EqualTo("a"));
        Assert.That(db[2].ParentCtrlDispValue, Is.EqualTo("23"));
        Assert.That(db[2].ParentCtrl2, Is.EqualTo("b"));
        Assert.That(db[2].ParentCtrlDispValue2, Is.EqualTo("1"));
    }
}
