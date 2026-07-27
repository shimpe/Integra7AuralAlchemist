using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Randomise as the instrument sees it: read each block, apply the new raw values, record what
/// changed, and send the block in one transmission.
///
/// <b>Why the read is not optional.</b> The new value of a numeric parameter is drawn around the value
/// that is there, so "there" has to be what the device holds and not what memory happens to carry -- a
/// block never read this session reads back as zeros, and randomising around zero is not randomising the
/// sound the user is listening to. It is also what makes the bulk write safe: WriteToIntegraAsync
/// flattens every context-valid parameter in the block, including the ones this randomise left alone.
///
/// <b>One undo step.</b> Every change is recorded inside a single <c>BeginGesture</c> scope, so a
/// randomise across several blocks still folds into one <c>EditStep</c> and one press of Undo takes all
/// of it back. Recording happens between reading the old displayed value and reading the new one, which
/// is the order <see cref="DomainEditRecorder"/> explains: record after the change and the old value is
/// gone.</summary>
public static class ToneRandomisationService
{
    /// <summary>Randomise every block in <paramref name="blocks"/> and answer how many parameters
    /// changed. <paramref name="lease"/> is the caller's conversation, held across the whole operation so
    /// nothing else writes into the middle of it.</summary>
    public static async Task<int> RandomiseAsync(Integra7Domain domain,
        IReadOnlyList<(string Start, string Offset, string Offset2)> blocks,
        RandomisationStrengths strengths, Random rng, IMidiLease? lease)
    {
        var changed = 0;

        // Opened around every block, not per block, so a multi-block randomise is one undo step.
        using var gesture = EditJournal.Default.BeginGesture();

        foreach (var (start, offset, offset2) in blocks)
        {
            var d = domain.GetDomain(start, offset, offset2);

            if (!await d.ReadFromIntegraAsync(lease))
                throw new SnapshotFormatException(
                    $"Could not randomise the tone: the device did not answer for block " +
                    $"(\"{start}\", \"{offset}\", \"{offset2}\").");

            // (false, false): neither reserved nor context-invalid. The opposite of what a snapshot
            // capture wants, and deliberately so -- a capture has to carry parameters its own
            // discriminators will make valid, whereas randomise never moves a discriminator, so a
            // parameter that is invalid now stays invalid.
            var parameters = d.GetRelevantParameters(false, false);
            var newValues = ToneRandomiser.NewValuesFor(parameters, strengths, rng);
            if (newValues.Count == 0) continue;

            foreach (var (path, raw) in newValues)
            {
                var before = d.LookupSingleParameterDisplayedValue(path);
                d.ModifySingleParameterRawValue(path, raw);
                var after = d.LookupSingleParameterDisplayedValue(path);

                EditJournal.Default.Record(new ParameterChange(
                    Start: start, Offset: offset, Offset2: offset2, Path: path,
                    OldValue: before, NewValue: after,
                    // Never true here: ToneRandomiser refuses a discriminator outright.
                    IsDiscriminator: false));
            }

            await d.WriteToIntegraAsync(lease);
            changed += newValues.Count;
        }

        Log.Information("Randomised {Count} parameters across {Blocks} block(s). The device does not " +
                        "acknowledge parameter writes, so this confirms the data was sent.",
            changed, blocks.Count);
        return changed;
    }
}
