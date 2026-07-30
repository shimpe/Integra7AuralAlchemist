using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which expansion board a preset bank lives on, and how to cover a set of them in as few loadouts
/// as the instrument's four slots allow.
///
/// <b>One place where a bank name meets a board number.</b> The mapping is by name and looks trivial --
/// "SRX07" is board 7 -- which is exactly why it would otherwise be written inline at every call site and
/// then disagree with itself once. <see cref="Integra7SysexHelpers.SrxIdForLoad"/> is the authority for the
/// numbers; this is the authority for which bank asks for which.</summary>
public static class SeedBoards
{
    /// <summary>The board a bank needs, or null when it needs none. PRST and GM2 are in the instrument
    /// itself; ExPCM is a bank the unit exposes no temporary tone for at all (see the spec), and it needs no
    /// board either.</summary>
    public static int? For(string bank) => bank switch
    {
        _ when bank.StartsWith("SRX", StringComparison.Ordinal)
               && int.TryParse(bank.AsSpan(3), out var srx) && srx is >= 1 and <= 12 => srx,
        _ when bank.StartsWith("ExSN", StringComparison.Ordinal)
               && int.TryParse(bank.AsSpan(4), out var exsn) && exsn is >= 1 and <= 6 => 12 + exsn,
        _ => null,
    };

    /// <summary>The loadouts that cover <paramref name="boards"/>, four slots at a time.
    ///
    /// <b>Ordered, and padded to four.</b> Ordered so that two plans over one selection load the boards in
    /// the same sequence -- a sweep resumed after an interruption then walks the same rounds and does not
    /// reload a board it has already finished with, which is 23 seconds each time. Padded because four
    /// values is what <c>SendLoadSrxAsync</c> takes, and a slot left unnamed is not the same as a slot set
    /// to Off.</summary>
    public static IReadOnlyList<int[]> Loadouts(IEnumerable<int> boards) =>
    [
        .. boards.Distinct().OrderBy(board => board)
            .Chunk(4)
            .Select(round => round.Concat(Enumerable.Repeat(0, 4 - round.Length)).ToArray()),
    ];
}
