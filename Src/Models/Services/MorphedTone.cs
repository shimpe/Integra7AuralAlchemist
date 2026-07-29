using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One blended tone, built from the snapshots at the pad's corners.
///
/// <b>What may be averaged and what may not is a property of the parameter database, not of this code.</b>
/// A parameter carrying a Repr or a Discrete list is a set of labels -- Low pass is not half way between
/// Bypass and Peaking -- and one naming a parent exists only while that parent holds a particular value,
/// so "MFX Parameter 1" is one effect's control under one MFX Type and a different effect's under
/// another. Both follow the winning corner; only genuinely continuous values are mixed.
///
/// <b>Two kinds of parameter the database does not mark, and this does.</b> A discriminator -- a parameter
/// other parameters are read against -- is skipped by its own flag, because an averaged one describes a
/// context no corner had. And a wave selector is skipped by category: its raw value is a position in a
/// table of thousands of unrelated samples, so the midpoint of two of them is a third sound related to
/// neither. Both are numeric, and neither is a quantity.
///
/// Works in raw space for the reason <c>ToneRandomiser</c> records: the raw value is what the device
/// stores, and a display string is a rendering that does not always survive a round trip through an
/// integer formatter.
///
/// The winner's snapshot is the template -- its blocks, in its order, with its paths -- so the result has
/// the layout the write path expects.</summary>
public static class MorphedTone
{
    public static Integra7Snapshot Blend(IReadOnlyList<Integra7Snapshot> corners,
        IReadOnlyList<double> weights, int winner, Integra7Parameters parameters) =>
        Blend(corners, weights, winner, parameters, out _);

    /// <param name="incomplete">True when at least one corner did not carry a parameter the winner has,
    /// so that value came from the winner alone. The screen says so once rather than silently blending
    /// something that is not a blend.</param>
    public static Integra7Snapshot Blend(IReadOnlyList<Integra7Snapshot> corners,
        IReadOnlyList<double> weights, int winner, Integra7Parameters parameters, out bool incomplete)
    {
        var template = corners[winner];
        var byPath = corners
            .Select(c => c.Domains.ToDictionary(
                d => (d.Offset, d.Offset2),
                d => d.Values.ToDictionary(v => v.Path)))
            .ToList();

        var anyMissing = false;
        List<SnapshotDomain> blended = [];

        foreach (var block in template.Domains)
        {
            List<SnapshotValue> values = [];
            foreach (var value in block.Values)
            {
                values.Add(BlendOne(block, value, corners, weights, winner, parameters, byPath,
                    ref anyMissing));
            }

            blended.Add(new SnapshotDomain(block.Start, block.Offset, block.Offset2, values));
        }

        incomplete = anyMissing;
        return template with { Name = template.Name, Domains = blended };
    }

    private static SnapshotValue BlendOne(SnapshotDomain block, SnapshotValue value,
        IReadOnlyList<Integra7Snapshot> corners, IReadOnlyList<double> weights, int winner,
        Integra7Parameters parameters,
        List<Dictionary<(string, string), Dictionary<string, SnapshotValue>>> byPath, ref bool anyMissing)
    {
        // No raw form: a name. Its value IS its string, so the winner's is the only sensible answer.
        if (value.Raw is not { } winnerRaw) return value;

        var index = parameters.LookupIndex(value.Path);
        if (index < 0) return value;   // not in this build's database; the winner's, unchanged

        var spec = parameters.Lookup(value.Path);

        // Labels, and anything that exists only under a particular parent value. See the class remarks.
        if (spec.Repr is not null || spec.Discrete is not null || spec.ParentCtrl.Length > 0) return value;

        // A discriminator decides how everything beneath it is read, so an average of two corners' values
        // describes a context neither corner had. ToneRandomiser skips these for the same reason and by the
        // same flag. The check above does not cover them: PCM Synth's Wave Group ID is a discriminator with
        // no Repr of its own.
        if (spec.IsParent) return value;

        // A wave is a position in a table of thousands of unrelated samples, not a quantity: half way
        // between two of them is a third sound with nothing to do with either, which is not what the pad
        // promises. Which parameters choose one is engine-specific and the category table already knows,
        // so it is asked rather than a second list of names being kept here.
        //
        // SuperNATURAL Synth's Wave Number carries a Repr and was already safe; PCM Synth's Wave Number L
        // and R carry none, and are what this line is for.
        if (ToneParameterCategories.For(value.Path) == ToneCategory.WaveChoice) return value;

        var total = 0.0;
        for (var i = 0; i < corners.Count; i++)
        {
            if (!byPath[i].TryGetValue((block.Offset, block.Offset2), out var inBlock) ||
                !inBlock.TryGetValue(value.Path, out var theirs) || theirs.Raw is not { } raw)
            {
                // A corner that does not carry this parameter cannot vote on it. Blending the rest would
                // weight the survivors wrongly, so the winner takes it outright.
                anyMissing = true;
                return value;
            }

            total += weights[i] * raw;
        }

        // Casts because IMin and IMax are int while a raw value is long; without them Math.Clamp has no
        // overload to bind to.
        var mixed = Math.Clamp((long)Math.Round(total), (long)spec.IMin, (long)spec.IMax);
        return value with { Raw = mixed, Value = $"{mixed}" };
    }
}
