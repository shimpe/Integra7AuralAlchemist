using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ParameterGen;

public static class ParameterBlobWriter
{
    public static void Write(System.IO.Stream stream, IReadOnlyList<ParameterDef> defs)
    {
        // --- Build string table (id 0 = "") ---
        var stringIds = new Dictionary<string, int>();
        var strings = new List<string>();

        strings.Add("");
        stringIds[""] = 0;

        int Intern(string? s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (stringIds.TryGetValue(s, out var existing)) return existing;
            var id = strings.Count;
            strings.Add(s);
            stringIds[s] = id;
            return id;
        }

        // --- Build repr/discrete tables (deduped by reference identity) ---
        var reprIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var reprList = new List<IDictionary<int, string>>();

        var discreteIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var discreteList = new List<List<System.Tuple<int, string>>>();

        // First pass: intern all strings and register repr/discrete tables
        foreach (var def in defs)
        {
            Intern(def.Path);
            Intern(def.Name);
            Intern(def.Unit);
            Intern(def.ParentCtrl);
            Intern(def.ParentCtrlDispValue);
            Intern(def.ParentCtrl2);
            Intern(def.ParentCtrlDispValue2);

            if (def.Repr != null && !reprIds.ContainsKey(def.Repr))
            {
                reprIds[def.Repr] = reprList.Count;
                reprList.Add(def.Repr);
                foreach (var kv in def.Repr)
                    Intern(kv.Value);
            }

            if (def.Discrete != null && !discreteIds.ContainsKey(def.Discrete))
            {
                discreteIds[def.Discrete] = discreteList.Count;
                discreteList.Add(def.Discrete);
                foreach (var t in def.Discrete)
                    Intern(t.Item2);
            }
        }

        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // 1. Header
        w.Write(ParameterBlobFormat.Magic);
        w.Write(ParameterBlobFormat.Version);
        w.Write(defs.Count);

        // 2. String table
        w.Write(strings.Count);
        foreach (var s in strings)
            w.Write(s);

        // 3. Repr tables
        w.Write(reprList.Count);
        foreach (var repr in reprList)
        {
            w.Write(repr.Count);
            foreach (var kv in repr)
            {
                w.Write(kv.Key);
                w.Write(stringIds[kv.Value]);
            }
        }

        // 4. Discrete tables
        w.Write(discreteList.Count);
        foreach (var disc in discreteList)
        {
            w.Write(disc.Count);
            foreach (var t in disc)
            {
                w.Write(t.Item1);
                w.Write(stringIds[t.Item2]);
            }
        }

        // 5. Columns — each is defs.Count entries, written as one contiguous run
        foreach (var def in defs) w.Write((byte)def.Type);
        foreach (var def in defs) w.Write(stringIds[def.Path]);
        foreach (var def in defs) w.Write(stringIds[def.Name]);
        foreach (var def in defs)
        {
            uint packed = 0;
            foreach (var b in def.Address) packed = (packed << 8) | b;
            w.Write(packed);
        }
        foreach (var def in defs) w.Write((byte)def.Address.Length);
        foreach (var def in defs) w.Write((int)ByteUtils.Bytes7ToInt(def.Address));
        foreach (var def in defs) w.Write(def.IMin);
        foreach (var def in defs) w.Write(def.IMax);
        foreach (var def in defs) w.Write(def.OMin);
        foreach (var def in defs) w.Write(def.OMax);
        foreach (var def in defs) w.Write((byte)def.Bytes);
        foreach (var def in defs)
        {
            byte flags = (byte)((def.Reserved ? 1 : 0) | (def.PerNibble ? 2 : 0) | (def.IsParent ? 4 : 0));
            w.Write(flags);
        }
        foreach (var def in defs) w.Write(Intern(def.Unit));
        foreach (var def in defs) w.Write(def.Repr == null ? -1 : reprIds[def.Repr]);
        foreach (var def in defs) w.Write(def.Discrete == null ? -1 : discreteIds[def.Discrete]);
        foreach (var def in defs) w.Write(Intern(def.ParentCtrl));
        foreach (var def in defs) w.Write(Intern(def.ParentCtrlDispValue));
        foreach (var def in defs) w.Write(Intern(def.ParentCtrl2));
        foreach (var def in defs) w.Write(Intern(def.ParentCtrlDispValue2));
        foreach (var def in defs) w.Write(def.IMin2);
        foreach (var def in defs) w.Write(def.IMax2);
        foreach (var def in defs) w.Write(def.OMin2);
        foreach (var def in defs) w.Write(def.OMax2);
    }
}
