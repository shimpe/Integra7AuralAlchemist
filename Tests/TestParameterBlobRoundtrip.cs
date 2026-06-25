extern alias gen;
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;
using gen::Integra7AuralAlchemist.ParameterGen;
using NUnit.Framework;

namespace Tests;

public class TestParameterBlobRoundtrip
{
    private static List<ParameterDef> SampleDefs() => new()
    {
        new(SpecType.NUMERIC, "Setup/Sound Mode", new byte[]{0x00,0x00}, 1, 4, 1, 4, 1, false, false, "",
            new Dictionary<int,string>{[1]="STUDIO",[2]="GM1"}),
        new(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Delay Left", new byte[]{0x00,0x04},
            12768, 52768, -20000, 20000, 4, false, true, "ms", null,
            par:"Studio Set Common Chorus/Chorus Type", parval:"Delay"),
        new(SpecType.ASCII, "SuperNATURAL Synth Tone Common/Tone Name", new byte[]{0x00,0x10}, 32, 127, 32, 127,
            12, false, false, "", null),
    };

    [Test]
    public void Roundtrip_PreservesRawColumns()
    {
        var defs = SampleDefs();
        using var ms = new MemoryStream();
        ParameterBlobWriter.Write(ms, defs);
        ms.Position = 0;
        var store = ParameterStore.Load(ms);

        Assert.That(store.Count, Is.EqualTo(defs.Count));
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            Assert.That(store.Str(store.PathIds[i]), Is.EqualTo(d.Path), $"path@{i}");
            Assert.That(store.Str(store.NameIds[i]), Is.EqualTo(d.Name), $"name@{i}");
            Assert.That((int)store.Types[i], Is.EqualTo((int)d.Type), $"type@{i}");
            Assert.That(store.UnpackAddress(i), Is.EqualTo(d.Address), $"addr@{i}");
            Assert.That(store.IMins[i], Is.EqualTo(d.IMin), $"imin@{i}");
            Assert.That(store.Str(store.ParentPathIds[i]), Is.EqualTo(d.ParentCtrl), $"parent@{i}");
            Assert.That((store.Flags[i] & 2) != 0, Is.EqualTo(d.PerNibble), $"nib@{i}");
            if (d.Repr is null) Assert.That(store.ReprIds[i], Is.EqualTo(-1), $"reprnull@{i}");
            else Assert.That(store.Reprs[store.ReprIds[i]]![1], Is.EqualTo(d.Repr[1]), $"repr@{i}");
        }
    }
}
