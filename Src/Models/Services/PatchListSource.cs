using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The instrument's presets as something a DAW can address.
///
/// <b>The source is the instrument, not the library.</b> A DAW patch list is reachable by bank select and
/// program change; a library file is not reachable that way at all. So this reads the presets already in
/// memory -- the factory data this build ships with, plus whatever user-memory names have been read from a
/// connected instrument -- and nothing here opens a file or needs a device.
///
/// <b>Everything that can be got wrong is here rather than in a writer.</b> The program base, the bank
/// naming, the ordering and the two patches that share one address are one decision each, made once, tested
/// once, and handed to all four formats already settled.</summary>
public static class PatchListSource
{
    /// <summary>The lowest and highest program a MIDI program change can carry. The instrument's own tone
    /// list counts from 1, so this is not the range of the numbers coming in.</summary>
    private const int FirstProgram = 0, LastProgram = 127;

    public static PatchList From(IReadOnlyList<Integra7Preset> presets, string device = "INTEGRA-7")
    {
        // Filled as a side effect of the two pipelines below rather than projected out of them, because
        // both are answers about rows that do *not* reach the result, and a projection can only speak for
        // rows that do. That is safe for exactly two reasons, and an edit that breaks either breaks the
        // answer silently: every pipeline that writes to one of these is forced with ToList() before
        // anything reads it, and neither pipeline is parallel -- List<T>.Add is not thread-safe, and 6,023
        // rows is the size at which reaching for AsParallel starts to look reasonable.
        List<string> skipped = [];
        List<string> collisions = [];

        // Indexed rather than grouped straight away: a stable sort has to be able to say which of two
        // patches at one address came first, and the presets' own order is the only order a user could
        // recognise -- it is the order the instrument's tone list is printed in.
        var rows = presets
            .Select((preset, index) => (preset, index))
            .Where(row =>
            {
                var program = row.preset.Pc - 1;
                if (program is >= FirstProgram and <= LastProgram) return true;
                // Left out rather than clamped. A clamp would put this patch's name on some other patch's
                // program, and every name after it would be a lie the user only discovers by playing it.
                skipped.Add($"{row.preset.Name} (program {row.preset.Pc})");
                return false;
            })
            .ToList();

        var banks = rows
            .GroupBy(row => (row.preset.Msb, row.preset.Lsb))
            .OrderBy(bank => bank.Key.Msb).ThenBy(bank => bank.Key.Lsb)
            .Select(bank =>
            {
                var patches = bank
                    // The tie-break on the index is insurance rather than the mechanism, and no test can
                    // tell: LINQ-to-Objects OrderBy is already stable, so document order holds for two
                    // patches at one program whether it is here or not. It is here for the day this
                    // becomes an Array.Sort or an AsParallel, neither of which is stable, so that the tie
                    // is still broken the way the instrument's own tone list breaks it.
                    .OrderBy(row => row.preset.Pc).ThenBy(row => row.index)
                    .Select(row => new PatchEntry(row.preset.Pc - 1, row.preset.Name, row.preset.ToneTypeStr,
                        row.preset.CategoryStr, row.preset.InternalUserDefinedStr == "USR"))
                    .ToList();

                foreach (var shared in patches.GroupBy(patch => patch.Program).Where(g => g.Count() > 1))
                    collisions.Add($"MSB {bank.Key.Msb} LSB {bank.Key.Lsb} program {shared.Key}: " +
                                   string.Join(", ", shared.Select(patch => patch.Name)));

                return new PatchBank(bank.Key.Msb, bank.Key.Lsb, NameOf(bank.First().preset), patches);
            })
            .ToList();

        return new PatchList(device, banks, collisions, skipped);
    }

    /// <summary>What to call a bank: the engine, the bank it came from, and the address it answers on.
    ///
    /// <b>Engine and bank are read from any one member</b>, and that much is safe: across all 6,023 rows of
    /// the factory data no (MSB, LSB) carries two different (engine, bank) pairs, so every member of a bank
    /// agrees about what it is.
    ///
    /// <b>The address is in the name because engine and bank do not identify a bank.</b> Only the one
    /// direction above was ever verified; the converse is false and badly so. One (engine, bank) spans up
    /// to ten addresses -- "PCMS GM2/GM2#" names ten banks, "SN-S PRST" names nine covering 1,109 presets,
    /// "PCMS PRST" seven -- and 51 of the 75 factory banks would otherwise share a name with another. The
    /// CSV would have survived that, because it prints MSB and LSB as columns of their own; the other three
    /// formats show the user a name and nothing else, so what the user got was nine indistinguishable
    /// "SN-S PRST" entries in a dropdown and no way to tell which one held their tone.
    ///
    /// <b>The address rather than an ordinal</b>, because Roland prints MSB and LSB against every tone in
    /// its own list, so this is a number the user can look up; "SN-S PRST 3" would be a number this
    /// application invented, and one that would move if a bank were ever absent. <b>And on every bank
    /// rather than only the ambiguous ones</b>, so that a bank's name never depends on which other banks
    /// were exported beside it: connecting the instrument adds the user banks, and a naming rule that
    /// noticed them could rename a factory bank between two exports and quietly break the DAW project that
    /// referred to it by name.
    ///
    /// <b>User memory is asked about first.</b> The presets built from the instrument's user-tone names
    /// carry a <c>ToneBankStr</c> of "PRST", which the source that builds them marks as wrong and is: they
    /// are not the factory bank. Naming from the bank string alone would label the user's own sounds as
    /// factory ones in every exported file, which is the one label in a patch list that must not be
    /// wrong.</summary>
    private static string NameOf(Integra7Preset preset)
    {
        var bank = preset.InternalUserDefinedStr == "USR" ? "USER" : preset.ToneBankStr;
        return $"{preset.ToneTypeStr} {bank} ({preset.Msb}/{preset.Lsb})";
    }
}
