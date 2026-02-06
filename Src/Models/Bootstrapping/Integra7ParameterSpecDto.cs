using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Bootstrapping;

public sealed class Integra7ParameterSpecDto
{
    public Integra7ParameterSpec.SpecType Type { get; set; }
    public string Path { get; set; } = "";
    public byte[] Address { get; set; } = [];

    public int IMin { get; set; }
    public int IMax { get; set; }
    public float OMin { get; set; }
    public float OMax { get; set; }

    public int Bytes { get; set; }
    public bool Reserved { get; set; }
    public bool PerNibble { get; set; }
    public string Unit { get; set; } = "";

    public Dictionary<int, string>? Repr { get; set; }
    public List<Tuple<int, string>>? Discrete { get; set; }

    public string ParentCtrl { get; set; } = "";
    public string ParentCtrlDispValue { get; set; } = "";
    public bool IsParent { get; set; }

    public string ParentCtrl2 { get; set; } = "";
    public string ParentCtrlDispValue2 { get; set; } = "";

    public float IMin2 { get; set; }
    public float IMax2 { get; set; }
    public float OMin2 { get; set; }
    public float OMax2 { get; set; }

    public static Integra7ParameterSpecDto FromSpec(Integra7ParameterSpec s) => new()
    {
        Type = s.Type,
        Path = s.Path,
        Address = s.Address,
        IMin = s.IMin,
        IMax = s.IMax,
        OMin = s.OMin,
        OMax = s.OMax,
        Bytes = s.Bytes,
        Reserved = s.Reserved,
        PerNibble = s.PerNibble,
        Unit = s.Unit,
        Repr = s.Repr != null ? new Dictionary<int, string>(s.Repr) : null,
        Discrete = s.Discrete,
        ParentCtrl = s.ParentCtrl,
        ParentCtrlDispValue = s.ParentCtrlDispValue,
        IsParent = s.IsParent,
        ParentCtrl2 = s.ParentCtrl2,
        ParentCtrlDispValue2 = s.ParentCtrlDispValue2,
        IMin2 = s.IMin2,
        IMax2 = s.IMax2,
        OMin2 = s.OMin2,
        OMax2 = s.OMax2
    };

    public Integra7ParameterSpec ToSpec() =>
        new(Type, Path, Address, IMin, IMax, OMin, OMax,
            Bytes, Reserved, PerNibble, Unit,
            Repr, ParentCtrl, ParentCtrlDispValue, IsParent,
            ParentCtrl2, ParentCtrlDispValue2,
            IMin2, IMax2, OMin2, OMax2,
            Discrete);
}
