extern alias gen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Golden test: build the real, full parameter database from the committed CSV/definitions, run the
    // build-time analyzer, serialize and reload it, then assert EVERY serialized field of EVERY parameter
    // survives the round-trip through the struct-view. This is the safety net for the whole blob format.
    [Test]
    public void Golden_FullDatabase_MatchesDefinitions()
    {
        var assets = Path.Combine(FindRepoRoot(), "Src", "Assets");
        var defs = new ParameterDefinitions().Build(assets);
        Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
        Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);

        using var ms = new MemoryStream();
        ParameterBlobWriter.Write(ms, defs);
        ms.Position = 0;
        var store = ParameterStore.Load(ms);

        Assert.That(store.Count, Is.EqualTo(defs.Count), "count");

        var mismatches = new List<string>();
        for (var i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            var s = store.Get(i);

            void Check(string field, bool ok)
            {
                if (!ok) mismatches.Add($"{field}@{i} ({d.Path})");
            }

            Check("type", (int)s.Type == (int)d.Type);
            Check("path", s.Path == d.Path);
            Check("name", s.Name == d.Name);
            Check("address", s.Address.SequenceEqual(d.Address));
            Check("imin", s.IMin == d.IMin);
            Check("imax", s.IMax == d.IMax);
            Check("omin", s.OMin == d.OMin);
            Check("omax", s.OMax == d.OMax);
            Check("bytes", s.Bytes == d.Bytes);
            Check("reserved", s.Reserved == d.Reserved);
            Check("nibble", s.PerNibble == d.PerNibble);
            Check("isparent", s.IsParent == d.IsParent);
            Check("unit", s.Unit == d.Unit);
            Check("parent", s.ParentCtrl == d.ParentCtrl);
            Check("parval", s.ParentCtrlDispValue == d.ParentCtrlDispValue);
            Check("parent2", s.ParentCtrl2 == d.ParentCtrl2);
            Check("parval2", s.ParentCtrlDispValue2 == d.ParentCtrlDispValue2);
            Check("imin2", FloatEq(s.IMin2, d.IMin2));
            Check("imax2", FloatEq(s.IMax2, d.IMax2));
            Check("omin2", FloatEq(s.OMin2, d.OMin2));
            Check("omax2", FloatEq(s.OMax2, d.OMax2));
            Check("repr", ReprEq(s.Repr, d.Repr));
            Check("discrete", DiscreteEq(s.Discrete, d.Discrete));
        }

        Assert.That(mismatches, Is.Empty,
            () => $"{mismatches.Count} field mismatch(es) across {defs.Count} params; first 20:\n" +
                  string.Join("\n", mismatches.Take(20)));
    }

    private static bool FloatEq(float a, float b) => (float.IsNaN(a) && float.IsNaN(b)) || a == b;

    private static bool ReprEq(IDictionary<int, string>? a, IDictionary<int, string>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        foreach (var kv in b)
            if (!a.TryGetValue(kv.Key, out var v) || v != kv.Value)
                return false;
        return true;
    }

    private static bool DiscreteEq(List<Tuple<int, string>>? a, List<Tuple<int, string>>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Item1 != b[i].Item1 || a[i].Item2 != b[i].Item2)
                return false;
        return true;
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Src", "Assets")))
            d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root (Src/Assets) not found");
    }
}
