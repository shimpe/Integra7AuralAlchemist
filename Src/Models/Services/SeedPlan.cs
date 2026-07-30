using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Why a preset is not in the work list.</summary>
public enum SeedSkip
{
    /// <summary>Its engine, bank or internal/user side was not ticked.</summary>
    NotSelected,

    /// <summary>A file of that name is already in the library folder. This is what makes an interrupted
    /// sweep resumable at no cost.</summary>
    AlreadyInLibrary,

    /// <summary>An untouched user slot. The instrument names them "INIT TONE", "INIT KIT" and the like, and
    /// capturing 900 copies of the same empty patch is the single largest waste a sweep can commit.</summary>
    EmptySlot,
}

/// <param name="Preset">The row this came from.</param>
/// <param name="FileName">What the file will be called, without a folder. See
/// <see cref="SeedPlan.FileNameFor"/> -- the catalogue name and the address, because the resume compares
/// this against the folder before anything is captured and neither the device's name nor a collision
/// suffix is knowable that early.</param>
/// <param name="Metadata">The annotations to write with it. Built here rather than at the write, so that
/// what a swept snapshot carries is decided in one tested place -- the tag is what makes "only my own
/// patches" a filter afterwards, and a sweep that forgot it would need 6,000 files re-annotated.</param>
public sealed record SeedItem(Integra7Preset Preset, string FileName, SnapshotMetadata Metadata);

/// <param name="Boards">The four slot values to send, or null when the round needs no board change.</param>
/// <param name="Items">The patches capturable while those boards are in the slots.</param>
public sealed record SeedRound(int[]? Boards, IReadOnlyList<SeedItem> Items);

/// <param name="Rounds">The work, grouped so the boards are loaded as few times as possible.</param>
/// <param name="Skipped">Every preset left out, with its reason. Carried rather than counted so the screen
/// can say "412 already in your library" rather than "412 skipped", which is a different sentence.</param>
/// <param name="Estimate">How long the run should take, from the per-engine costs measured on 2026-07-30
/// plus the board loads.</param>
public sealed record SeedWork(
    IReadOnlyList<SeedRound> Rounds,
    IReadOnlyList<(Integra7Preset Preset, SeedSkip Why)> Skipped,
    TimeSpan Estimate)
{
    public int Count => Rounds.Sum(round => round.Items.Count);
}

/// <summary>What a sweep of the instrument into the library would consist of: which presets, in which order,
/// under which boards, and how long it will take.
///
/// <b>Pure, and that is the point of it existing at all.</b> Everything a sweep can get quietly wrong is
/// decided here -- what a file is called, whether it has already been captured, whether a board round is
/// worth its 23 seconds -- and none of it needs a device or a folder to decide. The runner that comes after
/// this only has to do what it is told, and the whole rule set can be put under test without an
/// INTEGRA-7 on the desk.</summary>
public static class SeedPlan
{
    /// <summary>What a swept preset's file is called: its catalogue name, then its address.
    ///
    /// <b>The address is there because the name is not unique and the library will not overwrite.</b> 405
    /// of the 6,022 catalogue rows share a name with another row -- three Harps, three Shakuhachis, three
    /// Snare Menu 1s -- and <see cref="SnapshotLibrary.Create"/> answers a collision by suffixing " (2)",
    /// which is right for a user saving a sound by hand and wrong here: the sweep predicts this name before
    /// it captures anything, and a file that landed under a name the planner cannot predict would be
    /// captured again on every re-run, the folder growing by ~208 files each time while the resume looked
    /// like it was working. MSB, LSB and PC together are unique across every row in the table and across
    /// the user slots as well, which are at their own addresses, so a name built from them collides only
    /// with itself.
    ///
    /// <b>Not the device's name</b>, though the snapshot inside will carry it. This is chosen before the
    /// capture, because it is what the resume compares against the folder, and a name only knowable after a
    /// capture cannot decide whether to capture. The library already treats the two as different things.
    /// </summary>
    public static string FileNameFor(Integra7Preset preset) =>
        SnapshotLibrary.FileNameFor($"{preset.Name} [{preset.Msb}-{preset.Lsb}-{preset.Pc}]");

    /// <summary>The work a selection asks for, in the order it should be done.
    ///
    /// <b>Grouped by board loadout, boardless first.</b> A board load converges in about 23 seconds, so the
    /// grouping is most of what decides whether a small sweep takes one minute or five, and putting the
    /// built-in banks first means files start appearing before the first load rather than after it.
    ///
    /// <b>Skips are carried, not counted.</b> "412 are already in your library" and "412 were skipped" are
    /// different sentences and only one of them tells the user their last run worked.</summary>
    /// <param name="presets">The catalogue rows to consider -- normally every one the application knows.</param>
    /// <param name="selection">What the user ticked.</param>
    /// <param name="existingFiles">File names, not paths, already in the library folder.</param>
    /// <param name="loadedBoards">What the instrument has loaded right now, so a board already in a slot
    /// costs no round.</param>
    public static SeedWork Build(IReadOnlyList<Integra7Preset> presets, SeedSelection selection,
        IReadOnlyCollection<string> existingFiles, IReadOnlyCollection<int> loadedBoards)
    {
        // The folder is a Windows folder as often as not, where "Full Grand 1.json" and "FULL GRAND 1.JSON"
        // are one file -- so a sweep that matched case-sensitively would decide it had never captured a
        // patch it had, and then be unable to write the second copy it had talked itself into.
        var have = existingFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The engine and bank strings, by contrast, are the table's own spelling on both sides of the
        // comparison, so a case fold there would only hide a mismatch worth seeing.
        var engines = selection.Engines.ToHashSet(StringComparer.Ordinal);
        var banks = selection.Banks.ToHashSet(StringComparer.Ordinal);
        // What arrives here is four slot values, and this wants the set of boards: Off is 0, which is not a
        // board and which SeedBoards.For never answers, so letting the two vocabularies meet would only ever
        // produce a question with no meaning.
        var loaded = loadedBoards.Where(board => board != 0).ToHashSet();

        List<(Integra7Preset, SeedSkip)> skipped = [];
        List<(Integra7Preset Preset, SeedItem Item, int? Board)> work = [];

        foreach (var preset in presets)
        {
            var user = preset.InternalUserDefinedStr == "USR";
            if (!engines.Contains(preset.ToneTypeStr) || !banks.Contains(preset.ToneBankStr)
                || (user ? !selection.IncludeUser : !selection.IncludeInternal))
            {
                skipped.Add((preset, SeedSkip.NotSelected));
                continue;
            }

            // Only a user slot: the instrument ships factory tones with Init in the name, and dropping one
            // of those would be this feature overruling the tone list about what is a sound.
            if (user && preset.Name.TrimStart().StartsWith("INIT", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add((preset, SeedSkip.EmptySlot));
                continue;
            }

            var fileName = FileNameFor(preset);
            if (have.Contains(fileName))
            {
                skipped.Add((preset, SeedSkip.AlreadyInLibrary));
                continue;
            }

            // The category is the instrument's own vocabulary and the same list the library's filter offers,
            // so a swept snapshot lands inside the filters rather than outside them. The two tags are where
            // it came from and which side it came from: the second is how a user finds their own patches
            // among six thousand that are not theirs.
            var metadata = new SnapshotMetadata(
                preset.CategoryStr, [preset.ToneBankStr, user ? "user" : "factory"]);

            var board = SeedBoards.For(preset.ToneBankStr);
            work.Add((preset, new SeedItem(preset, fileName, metadata),
                loaded.Contains(board ?? 0) ? null : board));
        }

        // Boardless first and in one round -- including the banks whose board is already in a slot, which
        // is the same thing from the sweep's point of view: nothing to load before capturing.
        List<SeedRound> rounds = [];
        var boardless = work.Where(w => w.Board is null).Select(w => w.Item).ToList();
        if (boardless.Count > 0) rounds.Add(new SeedRound(null, boardless));

        // No round can come out of this empty, and it matters that the reason is upstream rather than a
        // guard here: the boards are read off the work list, which the skips have already been taken out
        // of, so a bank whose every patch is already in the library contributes no board and therefore no
        // round. A guard on the item count would look like it was preventing that and would in fact never
        // once fire -- and a sweep resumed near its end, which is when this matters, would still have to be
        // right for the upstream reason.
        foreach (var loadout in SeedBoards.Loadouts(work.Where(w => w.Board is not null)
                     .Select(w => w.Board!.Value)))
            rounds.Add(new SeedRound(loadout, work
                .Where(w => w.Board is { } board && loadout.Contains(board))
                .Select(w => w.Item).ToList()));

        return new SeedWork(rounds, skipped, Estimate(rounds));
    }

    /// <summary>Per-engine costs measured against the user's instrument on 2026-07-30, full round trip --
    /// three parameter writes, the selection settling, and the whole capture. A drum kit is fifty times an
    /// SN-A tone because it reads 88 partial blocks whether or not they hold anything, so an average would
    /// promise ten minutes for an hour's work.
    ///
    /// <b>Left alone when a second measurement came in under them.</b> Sweeps that evening timed SN-A at 77
    /// ms, SN-S at 146, PCMS at 332 and PCMD at 6,080 -- so four of these five charges run 8 to 34% high, and
    /// the drum kit 1% low, which is thirteen seconds across every kit the instrument has. Tightening them
    /// would buy a truer number for the run where nothing goes wrong, at the cost of the slack that pays for
    /// the runs where something does; an estimate that comes in early is the kinder error, and the check
    /// sweep that predicted 30.3 s and took 27.7 is where this wants to sit.
    ///
    /// <b>What is not modelled here is a row the instrument exposes nothing for.</b> Those cost 3.00 s each
    /// -- a reply deadline for the tone that never comes, and the read that asks whether the unit holds
    /// anything at all -- and they are charged their engine's capture rate instead, which on the measured
    /// unit is 32 minutes short across the 796 of them. Correcting it here would mean a table of which banks
    /// are unavailable, and this design discovers that rather than assuming it: another unit may answer for
    /// rows this one does not. The number belongs where somebody is deciding, so it is on the two banks'
    /// rows on the selection screen -- see <c>SeedRunViewModel.BankNote</c>.</summary>
    private static readonly Dictionary<string, int> MillisecondsPerPatch = new(StringComparer.Ordinal)
    {
        ["SN-A"] = 116, ["SN-S"] = 186, ["PCMS"] = 376, ["SN-D"] = 1380, ["PCMD"] = 6018,
    };

    /// <summary>A board loadout converges in about 23 seconds, measured over five of them. It is most of a
    /// small sweep's time and none of its captures.</summary>
    private static readonly TimeSpan PerLoadout = TimeSpan.FromSeconds(23);

    private static TimeSpan Estimate(IReadOnlyList<SeedRound> rounds) =>
        TimeSpan.FromMilliseconds(rounds.Sum(round => round.Items.Sum(item =>
            MillisecondsPerPatch.GetValueOrDefault(item.Preset.ToneTypeStr, 400))))
        + PerLoadout * rounds.Count(round => round.Boards is not null);
}
