using System;
using System.Collections.Generic;
using Avalonia.Collections;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Data;

// A zero-allocation view over one parameter held in a ParameterStore's columnar arrays.
// Constructed cheaply on demand (store + index); holds no per-parameter heap state itself.
public readonly struct Integra7ParameterSpec
{
    public enum SpecType { NUMERIC, ASCII, DISCRETE }

    private readonly ParameterStore _s;
    private readonly int _i;

    public Integra7ParameterSpec(ParameterStore store, int index)
    {
        _s = store;
        _i = index;
    }

    public SpecType Type => (SpecType)_s.Types[_i];
    public string Path => _s.Str(_s.PathIds[_i]);
    public string Name => _s.Str(_s.NameIds[_i]);
    public byte[] Address => _s.UnpackAddress(_i);   // allocates on demand (only the sysex/read paths use it)
    public int AddressInt => _s.AddrInts[_i];        // for sorting; no allocation
    public int IMin => _s.IMins[_i];
    public int IMax => _s.IMaxs[_i];
    public float OMin => _s.OMins[_i];
    public float OMax => _s.OMaxs[_i];
    public int Bytes => _s.BytesCol[_i];
    public bool Reserved => (_s.Flags[_i] & 1) != 0;
    public bool PerNibble => (_s.Flags[_i] & 2) != 0;
    public bool IsParent => (_s.Flags[_i] & 4) != 0;
    public string Unit => _s.Str(_s.UnitIds[_i]);
    public IDictionary<int, string>? Repr => _s.ReprIds[_i] < 0 ? null : _s.Reprs[_s.ReprIds[_i]];
    public List<Tuple<int, string>>? Discrete => _s.DiscreteIds[_i] < 0 ? null : _s.Discretes[_s.DiscreteIds[_i]];
    public string ParentCtrl => _s.Str(_s.ParentPathIds[_i]);
    public string ParentCtrlDispValue => _s.Str(_s.ParvalIds[_i]);
    public string ParentCtrl2 => _s.Str(_s.ParentPath2Ids[_i]);
    public string ParentCtrlDispValue2 => _s.Str(_s.Parval2Ids[_i]);
    public float IMin2 => _s.IMin2s[_i];
    public float IMax2 => _s.IMax2s[_i];
    public float OMin2 => _s.OMin2s[_i];
    public float OMax2 => _s.OMax2s[_i];

    public bool IsSameAs(Integra7ParameterSpec other) => Path == other.Path;

    public AvaloniaList<double> Ticks
    {
        get
        {
            if (Type == SpecType.ASCII || Type == SpecType.DISCRETE) return [];

            if (!float.IsNaN(IMin2) && !float.IsNaN(IMax2) && !float.IsNaN(OMin2) && !float.IsNaN(OMax2))
            {
                AvaloniaList<double> ticks = [];
                for (var i = (long)IMin2; i < (long)(IMax2 + 1); i++)
                    ticks.Add(Math.Round(Mapping.linlin(i, IMin2, IMax2, OMin2, OMax2), 2));
                return ticks;
            }

            if (OMin2 == -20000 && OMax2 == 20000)
            {
                AvaloniaList<double> ticks = [];
                for (long i = 0; i < 127 + 1; i++) ticks.Add(i);
                return ticks;
            }

            AvaloniaList<double> result = [];
            for (var i = (long)IMin; i <= IMax; i++) result.Add(Mapping.linlin(i, IMin, IMax, OMin, OMax));
            return result;
        }
    }
}
