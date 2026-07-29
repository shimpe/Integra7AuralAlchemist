using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One kept copy of a library file, and when its content was written.</summary>
public sealed record PatchVersion(string FilePath, DateTime Written);

/// <summary>The previous copies of a library file.
///
/// <b>What this is for.</b> Annotating a snapshot rewrites the file that holds the sound -- see
/// <see cref="SnapshotLibrary.WriteMetadata"/>, which re-reads all ~1,500 parameter values and writes them
/// back. That is the operation with the most to lose if it ever goes wrong, and it is also the one a user
/// performs most often. Deleting is worse: <see cref="SnapshotLibrary.Delete"/> does not use the recycle
/// bin, because .NET has no cross-platform API for one.
///
/// <b>Where they go.</b> A <c>.history</c> folder beside the library, one sub-folder per patch. It stays
/// out of the listing without being asked to: <see cref="SnapshotLibrary.Read"/> enumerates
/// <see cref="SearchOption.TopDirectoryOnly"/>, so a sub-folder is already invisible to it -- the test
/// <c>Sub_folders_are_not_enumerated</c> is what holds that true.
///
/// <b>The folder is not a parameter.</b> It is always the file's own directory, so passing it in would be
/// one more thing two callers could come to disagree about.
///
/// <b>A version is named after the file's own last-write time</b>, not the moment of archiving, so the name
/// says when that content was written rather than when it was displaced. The format sorts
/// lexicographically, which is what lets pruning and listing work on names alone.</summary>
public static class PatchHistory
{
    /// <summary>How many versions of one patch are kept. Ten is a working session's worth of saves and
    /// costs, for a tone, well under a megabyte -- a drum kit is 633 KB, which is the case worth
    /// remembering before raising this.</summary>
    public const int Keep = 10;

    /// <summary>Leading dot, which hides it on Unix and is inert on Windows. Named here rather than
    /// written into three methods.</summary>
    public const string FolderName = ".history";

    /// <summary>Sortable, second-resolution, no separators a file name cannot hold. Invariant, so a
    /// library written on one machine lists correctly on another.</summary>
    private const string Stamp = "yyyyMMddTHHmmss";

    private static string FolderFor(string filePath) =>
        Path.Combine(Path.GetDirectoryName(filePath) ?? "", FolderName,
            Path.GetFileNameWithoutExtension(filePath));

    /// <summary>Keep a copy of <paramref name="filePath"/> as it is now, then prune to <see cref="Keep"/>.
    ///
    /// <b>A file that is not there is not an error.</b> That is what creating a new snapshot looks like, so
    /// this is a no-op for it -- and it must not leave an empty history folder behind for every new patch.
    ///
    /// Everything else throws, and the caller refuses whatever it was about to do. See
    /// <see cref="SnapshotLibrary"/>'s remarks for why that is the right way round.</summary>
    public static void Archive(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var folder = FolderFor(filePath);
        Directory.CreateDirectory(folder);

        var stamp = File.GetLastWriteTime(filePath).ToString(Stamp, CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"{stamp}.json");
        // Two writes inside one second are ordinary -- a bulk retag does fourteen -- and the second must
        // not replace the first version.
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, $"{stamp}-{n}.json");

        File.Copy(filePath, target);
        Prune(folder);
    }

    /// <summary>The versions of <paramref name="filePath"/>, newest first. Empty when there are none, which
    /// is every patch until the first time it is written over.</summary>
    public static IReadOnlyList<PatchVersion> Versions(string filePath)
    {
        var folder = FolderFor(filePath);
        if (!Directory.Exists(folder)) return [];

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(path => (path, written: WrittenAt(path)))
            // A file this did not write -- a stray, or something dropped in by hand -- is passed over
            // rather than listed with a date that means nothing.
            .Where(v => v.written is not null)
            .OrderByDescending(v => v.written!.Value)
            .Select(v => new PatchVersion(v.path, v.written!.Value))];
    }

    /// <summary>Put <paramref name="versionPath"/> back at <paramref name="filePath"/>.
    ///
    /// <b>What is there now becomes a version in its turn</b>, so restoring the wrong one is not the single
    /// unrecoverable act in a feature built for recovery. Written through a temporary file and a rename for
    /// the reason <see cref="SnapshotLibrary"/> writes that way: a failure partway through must not leave
    /// the patch half replaced.
    ///
    /// <b>The restored file is stamped with now</b>, because copying and moving both carry the source's
    /// timestamp over and the file has in fact just been written. Leaving the old one produces two failures
    /// that do not look like timestamp failures: the library list's Date column jumps backwards, so a
    /// restore reads as having done nothing, and the next write archives under a stamp already taken,
    /// leaving two version rows the user cannot tell apart.</summary>
    public static void Restore(string filePath, string versionPath)
    {
        Archive(filePath);

        var temp = filePath + ".restoring";
        try
        {
            File.Copy(versionPath, temp, overwrite: true);
            File.Move(temp, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanup)
            {
                Serilog.Log.Warning(cleanup, "Could not remove the temporary file {Path}", temp);
            }

            throw;
        }

        // Outside the block above, and swallowed, because by this line the file already holds the restored
        // content: the move is what put it there and it is atomic. A failure to stamp it is not a failure
        // to restore it, and throwing here would tell the user their patch had not been put back when it
        // had -- and would skip the refresh that shows them it was.
        try
        {
            File.SetLastWriteTime(filePath, DateTime.Now);
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Restored {Path} but could not stamp it with the current time", filePath);
        }
    }

    /// <summary>The time in a version's file name, or null when the name is not one this wrote. Read from
    /// the name rather than from the file, because a copy carries the copy's timestamp.</summary>
    private static DateTime? WrittenAt(string versionPath)
    {
        var name = Path.GetFileNameWithoutExtension(versionPath);
        // A same-second collision appends "-2"; the stamp itself is fixed width and holds no hyphen.
        var hyphen = name.IndexOf('-');
        if (hyphen >= 0) name = name[..hyphen];

        return DateTime.TryParseExact(name, Stamp, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var written) ? written : null;
    }

    /// <summary>Keep the newest <see cref="Keep"/> and delete the rest. Ordered by name, which the stamp
    /// format makes the same as ordering by time.
    ///
    /// <b>Strays are passed over here exactly as <see cref="Versions"/> passes over them</b>, and the two
    /// must agree. A name beginning with a letter sorts above every timestamp under an ordinal comparison,
    /// so a stray left in the ordering would hold the newest slot for ever and push a genuine version out
    /// on every archive after that -- deleting the real one and keeping the stray, which is backwards. It
    /// also means this never deletes a file somebody put here by hand.</summary>
    private static void Prune(string folder)
    {
        var stale = Directory.EnumerateFiles(folder, "*.json")
            .Where(path => WrittenAt(path) is not null)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(Keep)
            .ToList();

        foreach (var path in stale) File.Delete(path);
    }
}
