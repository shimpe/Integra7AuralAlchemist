using System.Collections.Generic;

namespace Integra7AuralAlchemist.ParameterGen;

public enum SpecType { NUMERIC, ASCII, DISCRETE }

// Build-time authoring representation of one parameter. Mutable so the analyzer can set IsParent and
// the secondary dependency fields. Never shipped; the runtime uses the struct-view Integra7ParameterSpec.
public sealed class ParameterDef
{
    public ParameterDef(SpecType type, string path, byte[] offs, int imin, int imax, float omin, float omax,
        int bytes, bool res, bool nib, string unit, IDictionary<int, string>? repr, string par = "", string parval = "",
        bool isparent = false, string par2 = "", string parval2 = "", float imin2 = float.NaN, float imax2 = float.NaN,
        float omin2 = float.NaN, float omax2 = float.NaN, List<System.Tuple<int, string>>? discrete = null)
    {
        Type = type;
        Path = path;
        Address = offs;
        IMin = imin;
        IMax = imax;
        OMin = omin;
        OMax = omax;
        Bytes = bytes;
        Reserved = res;
        PerNibble = nib;
        Unit = unit;
        Repr = repr;
        ParentCtrl = par;
        ParentCtrlDispValue = parval;
        IsParent = isparent;
        ParentCtrl2 = par2;
        ParentCtrlDispValue2 = parval2;
        IMin2 = imin2;
        IMax2 = imax2;
        OMin2 = omin2;
        OMax2 = omax2;
        Discrete = discrete;
    }

    public SpecType Type { get; }
    public string Path { get; }
    public byte[] Address { get; }
    public int IMin { get; }
    public int IMax { get; }
    public float OMin { get; }
    public float OMax { get; }
    public int Bytes { get; }
    public bool Reserved { get; }
    public bool PerNibble { get; }
    public string Unit { get; }
    public IDictionary<int, string>? Repr { get; }
    public List<System.Tuple<int, string>>? Discrete { get; }
    public string ParentCtrl { get; set; }
    public string ParentCtrlDispValue { get; set; }
    public bool IsParent { get; set; }
    public string ParentCtrl2 { get; set; }
    public string ParentCtrlDispValue2 { get; set; }
    public float IMin2 { get; }
    public float IMax2 { get; }
    public float OMin2 { get; }
    public float OMax2 { get; }

    public string Name => Path.Split('/')[^1];
}
