extern alias gen;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using gen::Integra7AuralAlchemist.ParameterGen;

namespace Tests;

public class TestSysexParameterValueInterpreter
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
        byte[] parData = [0x00, 0x64];
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 1, USED,
                false, "", null));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(0x64));
        Assert.That(s, Is.EqualTo("100"));
    }

    [Test]
    public void Test_NotNibbled_NotMapped_WithRepr()
    {
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [0x64] = "YIPPEE"
        };
        byte[] parData = [0x00, 0x64];
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 1, USED,
                false, "", LUT));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(0x64));
        Assert.That(s, Is.EqualTo(LUT[0x64]));
    }

    [Test]
    public void Test_NotNibbled_WithMapped_NoRepr()
    {
        byte[] parData = [0x00, 0x64];
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, -64, 63, 1, USED,
                false, "", null));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(0x64));
        Assert.That(s, Is.EqualTo("36"));
    }

    [Test]
    public void Test_NotNibbled_WithMapped_WithRepr()
    {
        // not yet used in practice; look up mapped value in repr table
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [0] = "YIPPEE"
        };

        byte[] parData = [0x00, 0x40];
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, -64, 63, 1, USED,
                false, "", LUT));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(0x40));
        Assert.That(s, Is.EqualTo(LUT[0]));
    }

    [Test]
    public void Test_Nibbled_NotMapped_NoRepr()
    {
        var parData = ByteUtils.IntToNibbled(325, 4);
        Assert.That(parData, Is.EquivalentTo((byte[]) [0x0, 0x01, 0x04, 0x05]));

        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 2, USED,
                true, "", null));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(325));
        Assert.That(s, Is.EqualTo("325"));
    }

    [Test]
    public void Test_Nibbled_NotMapped_WithRepr()
    {
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [325] = "MEH"
        };
        var parData = ByteUtils.IntToNibbled(325, 4);
        Assert.That(parData, Is.EquivalentTo((byte[]) [0x0, 0x01, 0x04, 0x05]));

        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 0, 127, 2, USED,
                true, "", LUT));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(325));
        Assert.That(s, Is.EqualTo(LUT[325]));
    }

    [Test]
    public void Test_Nibbled_WithMapped_NoRepr()
    {
        var parData = ByteUtils.IntToNibbled(325, 4);
        Assert.That(parData, Is.EquivalentTo((byte[]) [0x0, 0x01, 0x04, 0x05]));

        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 1, 128, 2, USED,
                true, "", null));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(325)); // raw value is always unmapped
        Assert.That(s, Is.EqualTo("326")); // mapped value
    }

    [Test]
    public void Test_Nibbled_WithMapped_WithRepr()
    {
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [326] = "MEH"
        };
        var parData = ByteUtils.IntToNibbled(325, 4);
        Assert.That(parData, Is.EquivalentTo((byte[]) [0x0, 0x01, 0x04, 0x05]));

        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "System Common/Master Level", [0x00, 0x05], 0, 127, 1, 128, 2, USED,
                true, "", LUT));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(325)); // raw value is always unmapped
        Assert.That(s, Is.EqualTo(LUT[326])); // nibbled+mapped looks up mapped value in repr table
    }

    [Test]
    public void Test_WithNibbled_WithMapped_WithRepr_RoundingError()
    {
        IDictionary<int, string> LUT = new Dictionary<int, string>
        {
            [5] = "ROUNDING ERROR",
            [6] = "CORRECT"
        };
        var parData = ByteUtils.IntToNibbled(32774, 4);
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "blah", [0x00, 0x05], 12768, 52768, -20000, 20000, 4, USED, true, "",
                LUT));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(32774)); // raw value is always unmapped
        Assert.That(s,
            Is.EqualTo(LUT[6])); // nibbled+mapped looks up mapped value in repr table (without call to Round in "Interpret" this results in LUT[5])
    }

    [Test]
    public void Test_Ascii()
    {
        var parData = Encoding.ASCII.GetBytes("Integra Preview ");
        var store = StoreOf(
            new ParameterDef(SpecType.ASCII, "Studio Set Common/Studio Set Name", [0x00, 0x00], 32, 127, 32, 127, 16,
                USED, false, "", null));
        long n;
        string s;
        SysexParameterValueInterpreter.Interpret(parData, store.Get(0), out n, out s);
        Assert.That(n, Is.EqualTo(0)); // not used
        Assert.That(s, Is.EqualTo("Integra Preview "));
    }
}
