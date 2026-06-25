using System;
using System.Collections.Generic;
using System.IO;

namespace Integra7AuralAlchemist.Models.Data;

/// <summary>
/// Columnar in-memory store loaded from the compact binary blob produced by ParameterBlobWriter.
/// Each public array column has one entry per parameter, at the same index.
/// </summary>
public sealed class ParameterStore
{
    private string[] _strings = [];

    private ParameterStore() { }

    // ── Metadata ────────────────────────────────────────────────────────────
    public int Count { get; private set; }

    // ── Columns ─────────────────────────────────────────────────────────────
    public byte[]   Types           { get; private set; } = [];
    public int[]    PathIds         { get; private set; } = [];
    public int[]    NameIds         { get; private set; } = [];
    public uint[]   AddrPacked      { get; private set; } = [];
    public byte[]   AddrLen         { get; private set; } = [];
    public int[]    AddrInts        { get; private set; } = [];
    public int[]    IMins           { get; private set; } = [];
    public int[]    IMaxs           { get; private set; } = [];
    public float[]  OMins           { get; private set; } = [];
    public float[]  OMaxs           { get; private set; } = [];
    public byte[]   BytesCol        { get; private set; } = [];
    public byte[]   Flags           { get; private set; } = [];
    public int[]    UnitIds         { get; private set; } = [];
    public int[]    ReprIds         { get; private set; } = [];
    public int[]    DiscreteIds     { get; private set; } = [];
    public int[]    ParentPathIds   { get; private set; } = [];
    public int[]    ParvalIds       { get; private set; } = [];
    public int[]    ParentPath2Ids  { get; private set; } = [];
    public int[]    Parval2Ids      { get; private set; } = [];
    public float[]  IMin2s          { get; private set; } = [];
    public float[]  IMax2s          { get; private set; } = [];
    public float[]  OMin2s          { get; private set; } = [];
    public float[]  OMax2s          { get; private set; } = [];

    // ── Side tables ─────────────────────────────────────────────────────────
    public IDictionary<int, string>?[]          Reprs     { get; private set; } = [];
    public List<Tuple<int, string>>?[]          Discretes { get; private set; } = [];

    // ── Accessors ────────────────────────────────────────────────────────────
    /// <summary>Returns the interned string at the given string-table index.</summary>
    public string Str(int id) => _strings[id];

    /// <summary>Returns a lightweight struct-view over the parameter at the given index.</summary>
    public Integra7ParameterSpec Get(int idx) => new(this, idx);

    /// <summary>
    /// Reconstructs the original byte[] address for parameter <paramref name="i"/>
    /// from the packed representation (big-endian, most-significant byte first).
    /// </summary>
    public byte[] UnpackAddress(int i)
    {
        var len    = AddrLen[i];
        var packed = AddrPacked[i];
        var result = new byte[len];
        for (int j = 0; j < len; j++)
            result[j] = (byte)((packed >> (8 * (len - 1 - j))) & 0xFF);
        return result;
    }

    // ── Factory ──────────────────────────────────────────────────────────────
    public static ParameterStore Load(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // 1. Header — validate magic and version
        uint magic = r.ReadUInt32();
        if (magic != ParameterBlobFormat.Magic)
            throw new InvalidDataException(
                $"Bad magic: expected 0x{ParameterBlobFormat.Magic:X8}, got 0x{magic:X8}");

        int version = r.ReadInt32();
        if (version != ParameterBlobFormat.Version)
            throw new InvalidDataException(
                $"Version mismatch: expected {ParameterBlobFormat.Version}, got {version}");

        int count = r.ReadInt32();

        // 2. String table
        int nStrings = r.ReadInt32();
        var strings  = new string[nStrings];
        for (int i = 0; i < nStrings; i++)
            strings[i] = r.ReadString();

        // 3. Repr tables
        int reprCount = r.ReadInt32();
        var reprs     = new IDictionary<int, string>?[reprCount];
        for (int i = 0; i < reprCount; i++)
        {
            int ec   = r.ReadInt32();
            var dict = new Dictionary<int, string>(ec);
            for (int j = 0; j < ec; j++)
            {
                int key = r.ReadInt32();
                int sid = r.ReadInt32();
                dict[key] = strings[sid];
            }
            reprs[i] = dict;
        }

        // 4. Discrete tables
        int discreteCount = r.ReadInt32();
        var discretes     = new List<Tuple<int, string>>?[discreteCount];
        for (int i = 0; i < discreteCount; i++)
        {
            int ec   = r.ReadInt32();
            var list = new List<Tuple<int, string>>(ec);
            for (int j = 0; j < ec; j++)
            {
                int key = r.ReadInt32();
                int sid = r.ReadInt32();
                list.Add(Tuple.Create(key, strings[sid]));
            }
            discretes[i] = list;
        }

        // 5. Columns — read in exactly the same order as the writer
        var store = new ParameterStore();
        store.Count     = count;
        store._strings  = strings;
        store.Reprs     = reprs;
        store.Discretes = discretes;

        store.Types          = ReadBytes (r, count);
        store.PathIds        = ReadInts  (r, count);
        store.NameIds        = ReadInts  (r, count);
        store.AddrPacked     = ReadUInts (r, count);
        store.AddrLen        = ReadBytes (r, count);
        store.AddrInts       = ReadInts  (r, count);
        store.IMins          = ReadInts  (r, count);
        store.IMaxs          = ReadInts  (r, count);
        store.OMins          = ReadFloats(r, count);
        store.OMaxs          = ReadFloats(r, count);
        store.BytesCol       = ReadBytes (r, count);
        store.Flags          = ReadBytes (r, count);
        store.UnitIds        = ReadInts  (r, count);
        store.ReprIds        = ReadInts  (r, count);
        store.DiscreteIds    = ReadInts  (r, count);
        store.ParentPathIds  = ReadInts  (r, count);
        store.ParvalIds      = ReadInts  (r, count);
        store.ParentPath2Ids = ReadInts  (r, count);
        store.Parval2Ids     = ReadInts  (r, count);
        store.IMin2s         = ReadFloats(r, count);
        store.IMax2s         = ReadFloats(r, count);
        store.OMin2s         = ReadFloats(r, count);
        store.OMax2s         = ReadFloats(r, count);

        return store;
    }

    // ── Private read helpers ─────────────────────────────────────────────────
    private static byte[] ReadBytes(BinaryReader r, int n)
    {
        var a = new byte[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadByte();
        return a;
    }

    private static int[] ReadInts(BinaryReader r, int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
        return a;
    }

    private static uint[] ReadUInts(BinaryReader r, int n)
    {
        var a = new uint[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadUInt32();
        return a;
    }

    private static float[] ReadFloats(BinaryReader r, int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadSingle();
        return a;
    }
}
