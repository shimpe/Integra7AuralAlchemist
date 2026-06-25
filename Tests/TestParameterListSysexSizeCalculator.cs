extern alias gen;
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using gen::Integra7AuralAlchemist.ParameterGen;

namespace Tests;

public class ParameterListSysexSizeCalculatorTests
{
    public const bool USED = false;
    public const bool RESERVED = true;

    public readonly IDictionary<int, string> CHORUS_TYPE = new Dictionary<int, string>
    {
        [0] = "Off",
        [1] = "Chorus",
        [2] = "Delay",
        [3] = "GM2 Chorus"
    };

    public readonly IDictionary<int, string> DELAY_MSEC_NOTE = new Dictionary<int, string>
    {
        [0] = "msec",
        [1] = "Note"
    };

    public readonly IDictionary<int, string> MAIN_REV = new Dictionary<int, string>
    {
        [0] = "MAIN",
        [1] = "REV",
        [2] = "MAIN+REV"
    };

    public readonly IDictionary<int, string> OFF_LPF_HPF = new Dictionary<int, string>
    {
        [0] = "Off",
        [1] = "Low Pass Filter",
        [2] = "High Pass Filter"
    };

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
    public void TestSizeWithoutDataDep()
    {
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Type", [0x00, 0x00], 0, 3, 0, 3, 1,
                USED, false, "", CHORUS_TYPE, isparent: true),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Level", [0x00, 0x01], 0, 127, 0, 127,
                1, USED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Reserved", [0x00, 0x02], 0, 3, 0, 3, 1,
                RESERVED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Output Select", [0x00, 0x03], 0, 2, 0,
                2, 1, USED, false, "", MAIN_REV)
        );
        List<Integra7ParameterSpec> l = new() { store.Get(0), store.Get(1), store.Get(2), store.Get(3) };

        var size = ParameterListSysexSizeCalculator.CalculateSysexSize(l);
        Assert.That(size, Is.EqualTo(4));
        var gap = ParameterListSysexSizeCalculator.CalculateSysexGapBetweenFirstAndLast(l);
        Assert.That(gap, Is.EqualTo(3));
    }

    [Test]
    public void TestSizeWithDataDepInMiddle()
    {
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Type", [0x00, 0x00], 0, 3, 0, 3, 1,
                USED, false, "", CHORUS_TYPE, isparent: true),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Level", [0x00, 0x01], 0, 127, 0, 127,
                1, USED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Reserved", [0x00, 0x02], 0, 3, 0, 3, 1,
                RESERVED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Output Select", [0x00, 0x03], 0, 2, 0,
                2, 1, USED, false, "", MAIN_REV),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Reserved", [0x00, 0x04],
                12768, 52768, -20000, 20000, 4, RESERVED, true, "", null,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[0]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Filter Type",
                [0x00, 0x04], 0, 2, 0, 2, 4, USED, true, "", OFF_LPF_HPF,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[1]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Delay Left",
                [0x00, 0x04], 0, 1, 0, 1, 4, USED, true, "", DELAY_MSEC_NOTE,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[2]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Pre-LPF", [0x00, 0x04],
                0, 7, 0, 7, 4, USED, true, "", null, "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[3]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 2/Reserved", [0x00, 0x04],
                12768, 52768, -20000, 20000, 4, RESERVED, true, "", null)
        );
        List<Integra7ParameterSpec> l = new()
        {
            store.Get(0), store.Get(1), store.Get(2), store.Get(3), store.Get(4),
            store.Get(5), store.Get(6), store.Get(7), store.Get(8)
        };

        var size = ParameterListSysexSizeCalculator.CalculateSysexSize(l);
        Assert.That(size, Is.EqualTo(12));
        var gap = ParameterListSysexSizeCalculator.CalculateSysexGapBetweenFirstAndLast(l);
        Assert.That(gap, Is.EqualTo(8));
    }

    [Test]
    public void TestSizeWithDataDepAtEnd()
    {
        var store = StoreOf(
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Type", [0x00, 0x00], 0, 3, 0, 3, 1,
                USED, false, "", CHORUS_TYPE, isparent: true),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Level", [0x00, 0x01], 0, 127, 0, 127,
                1, USED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Reserved", [0x00, 0x02], 0, 3, 0, 3, 1,
                RESERVED, false, "", null),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Output Select", [0x00, 0x03], 0, 2, 0,
                2, 1, USED, false, "", MAIN_REV),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Reserved", [0x00, 0x04],
                12768, 52768, -20000, 20000, 4, RESERVED, true, "", null,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[0]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Filter Type",
                [0x00, 0x04], 0, 2, 0, 2, 4, USED, true, "", OFF_LPF_HPF,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[1]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Delay Left",
                [0x00, 0x04], 0, 1, 0, 1, 4, USED, true, "", DELAY_MSEC_NOTE,
                "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[2]),
            new ParameterDef(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Pre-LPF", [0x00, 0x04],
                0, 7, 0, 7, 4, USED, true, "", null, "Studio Set Common Chorus/Chorus Type", CHORUS_TYPE[3])
        );
        List<Integra7ParameterSpec> l = new()
        {
            store.Get(0), store.Get(1), store.Get(2), store.Get(3),
            store.Get(4), store.Get(5), store.Get(6), store.Get(7)
        };

        var size = ParameterListSysexSizeCalculator.CalculateSysexSize(l);
        Assert.That(size, Is.EqualTo(8));
        var gap = ParameterListSysexSizeCalculator.CalculateSysexGapBetweenFirstAndLast(l);
        Assert.That(gap, Is.EqualTo(4));
    }
}
