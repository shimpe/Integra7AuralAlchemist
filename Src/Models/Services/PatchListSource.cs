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

    /// <summary>What to call a bank, taken from any one of its members because an address is one engine's
    /// one bank -- verified across all 6,022 rows of the factory data.
    ///
    /// <b>User memory is asked about first.</b> The presets built from the instrument's user-tone names
    /// carry a <c>ToneBankStr</c> of "PRST", which the source that builds them marks as wrong and is: they
    /// are not the factory bank. Naming from the bank string alone would label the user's own sounds as
    /// factory ones in every exported file, which is the one label in a patch list that must not be
    /// wrong.</summary>
    private static string NameOf(Integra7Preset preset) =>
        preset.InternalUserDefinedStr == "USR"
            ? $"{preset.ToneTypeStr} USER"
            : $"{preset.ToneTypeStr} {preset.ToneBankStr}";
}
