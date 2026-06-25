extern alias gen;
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using gen::Integra7AuralAlchemist.ParameterGen;

namespace Tests;

public class TestDisplayValueToRawValueConverter
{
    public const bool USED = false;
    public const bool RESERVED = true;

    private static ParameterStore StoreOf(params ParameterDef[] defs)
    {
        using var ms = new MemoryStream();
        ParameterBlobWriter.Write(ms, new List<ParameterDef>(defs));
        ms.Position = 0;
        return ParameterStore.Load(ms);
    }

    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test_NotNibbled_NotMapped_NoRepr()
    {
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 1, USED,
                false, "", null));
        FullyQualifiedParameter p = new("blah", "blob", "foobar", store.Get(0), 0, "0");
        DisplayValueToRawValueConverter.UpdateFromDisplayedValue("100", p);
        Assert.That(p.RawNumericValue, Is.EqualTo(100));
        Assert.That(p.StringValue, Is.EqualTo("100"));
    }

    [Test]
    public void Test_NotNibbled_NotMapped_WithRepr()
    {
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [0x64] = "YIPPEE",
            [0x34] = "WHY?"
        };
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 1, USED,
                false, "", LUT));
        FullyQualifiedParameter p = new("blah", "blob", "foobar", store.Get(0), 0x64, "YIPPEE");
        DisplayValueToRawValueConverter.UpdateFromDisplayedValue("WHY?", p);
        Assert.That(p.RawNumericValue, Is.EqualTo(0x34));
        Assert.That(p.StringValue, Is.EqualTo(LUT[0x34]));
    }

    [Test]
    public void Test_NotNibbled_WithMapped_NoRepr()
    {
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, -64, 63, 1, USED,
                false, "", null));
        FullyQualifiedParameter p = new("blah", "blob", "foobar", store.Get(0), 0, "-64");
        DisplayValueToRawValueConverter.UpdateFromDisplayedValue("0", p);
        Assert.That(p.RawNumericValue, Is.EqualTo(64));
        Assert.That(p.StringValue, Is.EqualTo("0"));
    }

    [Test]
    public void Test_NotNibbled_WithMapped_WithRepr()
    {
        // not used in practice; look up mapped value in repr table
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [0] = "YIPPEE",
            [63] = "MEH?"
        };
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, -64, 63, 1, USED,
                false, "", LUT));
        FullyQualifiedParameter p = new("blah", "blob", "foobar", store.Get(0), 127, LUT[63]);
        DisplayValueToRawValueConverter.UpdateFromDisplayedValue("YIPPEE", p);
        Assert.That(p.RawNumericValue, Is.EqualTo(64)); // raw value is unmapped value
        Assert.That(p.StringValue, Is.EqualTo(LUT[0]));
    }

    // note nibbled values behave exactly the same as non-nibbled values for this purpose
}
