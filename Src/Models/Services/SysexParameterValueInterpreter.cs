using System;
using System.Text;
using Integra7AuralAlchemist.Models.Data;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public class SysexParameterValueInterpreter
{
    public static void Interpret(byte[] parResult, Integra7ParameterSpec? parspec, out long rawNumericValue,
        out string stringValue)
    {
        rawNumericValue = 0;
        stringValue = "";

        if (parspec is null)
        {
            stringValue = "";
            return;
        }

        var spec = parspec.Value;
        if (spec.Type == Integra7ParameterSpec.SpecType.NUMERIC)
        {
            if (spec.PerNibble)
                rawNumericValue = ByteUtils.NibbledToInt(parResult);
            else
                rawNumericValue = ByteUtils.Bytes7ToInt(parResult);

            if (spec.IMin != spec.OMin || spec.IMax != spec.OMax || spec.IMin2 != spec.OMin2 ||
                spec.IMax2 != spec.OMax2)
            {
                var mapped = Mapping.linlin(rawNumericValue, spec.IMin, spec.IMax, spec.OMin, spec.OMax);
                if (!float.IsNaN(spec.IMin2) && !float.IsNaN(spec.IMax2) && !float.IsNaN(spec.OMin2) &&
                    !float.IsNaN(spec.OMax2))
                    mapped = Mapping.linlin(mapped, spec.IMin2, spec.IMax2, spec.OMin2, spec.OMax2);
                stringValue = $"{Math.Round(mapped, 2)}";
            }
            else
            {
                stringValue = $"{rawNumericValue}";
            }

            if (spec.Repr != null)
            {
                var key = int.Parse(stringValue);
                if (spec.Repr.ContainsKey(key))
                    stringValue = spec.Repr[key];
                else
                    //Debug.Assert(false, $"mapped value {key} for par {spec.Path} not found in {spec.Repr.Keys}");
                    Log.Debug($"ERROR: mapped value {key} for par {spec.Path} not found in {spec.Repr.Keys}");
            }
        }
        else if (spec.Type == Integra7ParameterSpec.SpecType.DISCRETE)
        {
            var found = false;
            long val = (parResult[0] << 8) + parResult[1];
            foreach (Tuple<int, string> entry in spec.Discrete)
                if (entry.Item1 == val)
                {
                    found = true;
                    rawNumericValue = val;
                    stringValue = entry.Item2;
                }

            if (!found)
            {
                Log.Error(
                    $"Discrete value {val} has not known value for parameter {spec.Path}. Choosing something else instead.");
                rawNumericValue = spec.Discrete[0].Item1;
                stringValue = spec.Discrete[0].Item2;
            }
        }
        else
        {
            stringValue = Encoding.ASCII.GetString(parResult);
        }
    }
}