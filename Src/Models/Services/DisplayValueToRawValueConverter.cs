using System;
using System.Diagnostics;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

public class DisplayValueToRawValueConverter
{
    public static void UpdateFromDisplayedValue(string displayValue, FullyQualifiedParameter p)
    {
        if (p.IsNumeric)
        {
            var repr = p.EffectiveRepr ?? p.ParSpec.Repr;
            if (repr != null)
            {
                var key = repr
                    .Where(keyvaluepair => keyvaluepair.Value == displayValue)
                    .Select(keyvaluepair => keyvaluepair.Key)
                    .ToList();
                if (key.Count == 0)
                {
                    Debug.WriteLine(false, $"cannot find {displayValue} in {p.ParSpec.Repr}");
                    p.RawNumericValue = 0;
                }
                else
                {
                    p.RawNumericValue =
                        key.First(); // if a repr is present, and a mapping is present this is still a "mapped value"
                }
            }

            if (p.ParSpec.IMin != p.ParSpec.OMin || p.ParSpec.IMax != p.ParSpec.OMax ||
                p.ParSpec.IMin2 != p.ParSpec.OMin2 || p.ParSpec.IMax2 != p.ParSpec.OMax2)
            {
                // need to unmap mapped value to raw value
                if (p.ParSpec.Repr != null)
                {
                    double unmapped = p.RawNumericValue;
                    if (!float.IsNaN(p.ParSpec.IMin2) && !float.IsNaN(p.ParSpec.IMax2) &&
                        !float.IsNaN(p.ParSpec.OMin2) && !float.IsNaN(p.ParSpec.OMax2))
                        unmapped = Mapping.linlin(unmapped, p.ParSpec.OMin2, p.ParSpec.OMax2, p.ParSpec.IMin2,
                            p.ParSpec.IMax2, true);
                    p.RawNumericValue = (long)Math.Round(Mapping.linlin(unmapped, p.ParSpec.OMin, p.ParSpec.OMax,
                        p.ParSpec.IMin, p.ParSpec.IMax, true));
                }
                else
                {
                    var unmapped = double.Parse(displayValue);
                    if (!float.IsNaN(p.ParSpec.IMin2) && !float.IsNaN(p.ParSpec.IMax2) &&
                        !float.IsNaN(p.ParSpec.OMin2) && !float.IsNaN(p.ParSpec.OMax2))
                        unmapped = Mapping.linlin(unmapped, p.ParSpec.OMin2, p.ParSpec.OMax2, p.ParSpec.IMin2,
                            p.ParSpec.IMax2, true);
                    p.RawNumericValue = (long)Math.Round(Mapping.linlin(unmapped, p.ParSpec.OMin, p.ParSpec.OMax,
                        p.ParSpec.IMin, p.ParSpec.IMax, true));
                }
            }
            else
            {
                if (p.ParSpec.Repr == null) // otherwise p.RawNumericValue is already found in the previous paragraph
                    p.RawNumericValue = (long)Math.Round(double.Parse(displayValue));
            }

            p.StringValue = displayValue;
        }
        else if (p.IsDiscrete)
        {
            foreach (var entry in p.ParSpec.Discrete)
                if (entry.Item2 == displayValue)
                {
                    p.RawNumericValue = entry.Item1;
                    break;
                }

            p.StringValue = displayValue;
        }
        else
        {
            if (displayValue.Length > p.ParSpec.Bytes)
                p.StringValue = displayValue[..p.ParSpec.Bytes]; // clip string to max length
            else
                p.StringValue = displayValue;
        }

        p.DebugLog();
    }
}