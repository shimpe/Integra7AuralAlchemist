using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>How far each ticked category may move, 0..1. A category absent from the map, or present with
/// a strength of zero, is not randomised at all.</summary>
public sealed record RandomisationStrengths(IReadOnlyDictionary<ToneCategory, double> ByCategory)
{
    public double For(ToneCategory category) =>
        ByCategory.TryGetValue(category, out var s) ? Math.Clamp(s, 0.0, 1.0) : 0.0;

    public bool Any => ByCategory.Values.Any(s => s > 0.0);
}

/// <summary>What a randomise would change, and to what.
///
/// <b>Raw values, not display strings.</b> Every parameter has an integer raw range (IMin..IMax) that the
/// device actually stores, and arithmetic on it is exact. The display value is a formatted string, and
/// some of them are not even integers -- Master Tune's are fractional -- so a randomiser that worked in
/// display space would have to parse and re-format, and would quietly mangle those.
///
/// <b>Nothing here talks to a device.</b> The caller reads the block, hands the parameters over, applies
/// what comes back and writes. That is what makes this testable, and it is where the whole "what may
/// move" question is settled.</summary>
public static class ToneRandomiser
{
    /// <summary>The new raw value for each parameter that should change. A parameter absent from the
    /// result is one the caller must leave exactly as it is.</summary>
    public static IReadOnlyDictionary<string, long> NewValuesFor(
        IEnumerable<FullyQualifiedParameter> parameters, RandomisationStrengths strengths, Random rng)
    {
        Dictionary<string, long> result = [];

        foreach (var p in parameters)
        {
            var spec = p.ParSpec;

            // A discriminator decides how every parameter that depends on it is interpreted, so moving
            // one would mean writing values against a context that no longer holds. In this database
            // that is MFX Type and the SuperNATURAL Acoustic instrument -- both of which a user asking
            // to "randomise the effects" or "vary this piano" means to keep.
            if (spec.IsParent) continue;

            // A name is text; there is no range to draw from and nothing musical to gain.
            if (spec.Type == Integra7ParameterSpec.SpecType.ASCII) continue;

            if (ToneParameterCategories.For(spec.Path) is not { } category) continue;

            var strength = strengths.For(category);
            if (strength <= 0.0) continue;

            // Enumerated: the values are labels, so the distance between two of them means nothing and a
            // window around the current one would be arithmetic on names. Strength becomes the chance of
            // re-drawing instead, which is what keeps most switches and modes still at a low setting.
            var choices = LegalValues(spec);
            if (choices is not null)
            {
                if (rng.NextDouble() >= strength) continue;

                // Drawn from the values other than the one already there. Including it would make the
                // strength mean less than it says -- the real chance of a change would be
                // strength * (n-1)/n, which for a two-value switch is half what was asked for, and a
                // "randomise everything" would leave some switches visibly untouched.
                var others = choices.Where(v => v != p.RawNumericValue).ToList();
                if (others.Count == 0) continue;
                result[spec.Path] = others[rng.Next(others.Count)];
                continue;
            }

            var window = (long)Math.Round(strength * (spec.IMax - spec.IMin));
            if (window <= 0) continue;

            // Symmetric around the value that is there, then clamped -- so a parameter already near its
            // limit stays legal instead of wrapping to the other end of its range, which for a cutoff or
            // a level is the difference between a nudge and a jump.
            var moved = Math.Clamp(p.RawNumericValue + rng.NextInt64(-window, window + 1),
                spec.IMin, spec.IMax);
            if (moved != p.RawNumericValue) result[spec.Path] = moved;
        }

        return result;
    }

    /// <summary>The raw values an enumerated parameter may legally take, or null when it is a plain
    /// numeric one. DISCRETE parameters carry an explicit list; NUMERIC ones with a Repr are switches and
    /// modes whose raw values are keys in it. <c>EffectiveRepr</c> is deliberately not consulted: it is
    /// the bank-resolved wave-name list, which is presentation, and wave *numbers* are a plain numeric
    /// range that should be drawn from as one.</summary>
    private static IReadOnlyList<long>? LegalValues(Integra7ParameterSpec spec)
    {
        if (spec.Discrete is { } discrete) return [.. discrete.Select(d => (long)d.Item1)];
        if (spec.Repr is { } repr) return [.. repr.Keys.Select(k => (long)k)];
        return null;
    }
}
