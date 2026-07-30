using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A file as a folder listing found it: where it is, and the two cheap facts that say whether it is
/// still the file somebody read.
///
/// <b>Both facts, not just the time.</b> A last-write time is a clock reading, and a clock is coarse: FAT32
/// rounds it to two seconds, and even NTFS's tick is wide enough that a save landing immediately after a read
/// can carry the same stamp. The length is free -- a directory walk hands it over with the name -- and it
/// catches the one case the clock can miss that matters here, which is a file replaced by a different one.
///
/// It carries no vector and no head. It is what a scan learns about the whole folder before deciding what to
/// open, so it has to be cheap for a thousand files at once.</summary>
/// <param name="Path">The file, as the listing spells it.</param>
/// <param name="Modified">Its last-write time, in local time -- <see cref="LibraryEntry.Modified"/>'s
/// convention, so the two can be compared without anybody having to remember which is which.</param>
/// <param name="Length">Its size in bytes.</param>
public sealed record SnapshotFileStamp(string Path, DateTime Modified, long Length);

/// <summary>What a duplicate scan already knows: the vector of every file it has read, and whether that is
/// still what the file holds.
///
/// <b>Why there is a cache here at all, when the library deliberately has no index.</b>
/// <see cref="SnapshotLibrary"/>'s remarks say why a listing re-reads every file every time: the metadata lives
/// in the files, so nothing can go stale, and a file copied in from elsewhere is complete the moment it lands.
/// A duplicate scan is the one operation in the library that cannot pay that price twice. A listing reads a few
/// hundred bytes of each file's head; a scan parses every file whole, and the user's own measure of the feature
/// is whether it is quick enough to be worth pressing twice -- which is exactly what a second scan of a folder
/// nobody has touched has to be.
///
/// <b>It is in memory and it stays there, deliberately.</b> A cache on disk would be an index, with all of the
/// index's problems and none of its excuse: it would have to be right about files added, removed and rewritten
/// by other applications between two runs of this one, and the failure -- two sounds silently not reported as
/// duplicates -- is invisible. A cache that cannot outlive the process cannot be wrong across runs, and the
/// first scan of a session pays the same price the library pays on every refresh.
///
/// <b>The stamp is the whole staleness rule.</b> A file whose time or length differs from what was read is read
/// again; anything else is answered from memory. What that cannot catch is a file replaced by one of exactly
/// the same length within the same clock tick -- which is not reachable by saving, since a save writes at the
/// time of the save, and would in any case cost one missed duplicate rather than a wrong one.
///
/// <b>Nothing is remembered about a file that is not in the folder.</b> Every scan is told the whole folder and
/// forgets everything else, so the cache cannot outgrow the library -- and a path that comes back after a
/// delete is a fresh question, which is the case this phase's own workflow creates: deleting a duplicate frees
/// its name, and <c>SnapshotLibrary.UniquePath</c> only avoids a name that is taken at the time.
///
/// <b>No file is opened here.</b> Reading one is <see cref="SnapshotRawVector"/>'s, and doing it for a folder
/// belongs to whoever can do it off the UI thread -- the same split <see cref="DeepSearch"/> makes, for the
/// same reason: what is left is the part that can be got wrong quietly, and it is here where a test can reach
/// it.</summary>
public sealed class SnapshotVectorCache
{
    /// <summary>Ordinal, ignoring case -- <see cref="LibraryFilter"/>'s rule for every path in the library, and
    /// needed here for a reason of its own: Windows and macOS will hand back a name that differs from the
    /// stored one only in case, and treating those as two files would re-read the folder every scan.</summary>
    private static readonly StringComparer Loosely = StringComparer.OrdinalIgnoreCase;

    /// <summary>Every file the last scan was told about, with what was read of it.
    ///
    /// <b>The vector is nullable, and null is knowledge.</b> It means "opened, and not a snapshot" -- somebody
    /// else's config.json sitting in the library folder. Remembering that is what stops a folder of strays
    /// being opened in full on every scan. A file that could not be opened <i>at all</i> is a different thing
    /// and is not recorded, so the next scan tries it again; see <see cref="Vectors"/>.</summary>
    private Dictionary<string, (DateTime Modified, long Length, RawVector? Vector)> _known = new(Loosely);

    /// <summary>Which of <paramref name="files"/> have to be opened: the ones this has never seen, and the ones
    /// whose stamp has moved since it saw them.</summary>
    public IReadOnlyList<SnapshotFileStamp> ToRead(IReadOnlyList<SnapshotFileStamp> files) =>
        [.. files.Where(file => !Remembered(file, out _))];

    /// <summary>The vectors of <paramref name="files"/>, taking <paramref name="justRead"/> for the ones that
    /// have just been opened and memory for the rest -- and remembering exactly that, so the next scan reads
    /// less.
    ///
    /// <b>What is left out of the answer, and why each is left out rather than guessed at.</b> A file that read
    /// as null is not a snapshot, so it is no part of any comparison -- and an empty vector equals every other
    /// empty vector, which would make one large group out of every stray file in the folder (see
    /// <see cref="SnapshotRawVector"/>). A file that <see cref="ToRead"/> asked for and
    /// <paramref name="justRead"/> does not answer for could not be opened; it is left out <i>and</i> left
    /// unrecorded, because pressing Scan again is the user's entire remedy for a file a sync client had open,
    /// and a cache that had recorded the failure would make that remedy do nothing.
    ///
    /// A path is answered once however many times it appears, since two rows for one file would be a file
    /// grouped with itself.</summary>
    public IReadOnlyList<(string Path, RawVector Vector)> Vectors(IReadOnlyList<SnapshotFileStamp> files,
        IReadOnlyList<(string Path, RawVector? Vector)> justRead)
    {
        // Keyed here rather than taken as a dictionary, so that a caller cannot hand one keyed the wrong way
        // and get a folder re-read every scan with nothing saying why.
        Dictionary<string, RawVector?> read = new(Loosely);
        foreach (var (path, vector) in justRead) read[path] = vector;

        Dictionary<string, (DateTime, long, RawVector?)> keeping = new(Loosely);
        List<(string, RawVector)> entries = [];

        foreach (var file in files)
        {
            // Already answered under another spelling of the same path, or listed twice.
            if (keeping.ContainsKey(file.Path)) continue;

            RawVector? vector;
            if (read.TryGetValue(file.Path, out var fresh)) vector = fresh;
            // Not offered and not remembered: unreadable. Deliberately not recorded -- see above.
            else if (!Remembered(file, out vector)) continue;

            keeping[file.Path] = (file.Modified, file.Length, vector);
            if (vector is not null) entries.Add((file.Path, vector));
        }

        // Replaced rather than merged: what is not in the folder is not remembered.
        _known = keeping;
        return entries;
    }

    /// <summary>Whether what was read of <paramref name="file"/> is still what the file holds. Both halves of
    /// the stamp, for the reason <see cref="SnapshotFileStamp"/> gives.</summary>
    private bool Remembered(SnapshotFileStamp file, out RawVector? vector)
    {
        vector = null;
        if (!_known.TryGetValue(file.Path, out var entry)) return false;
        if (entry.Modified != file.Modified || entry.Length != file.Length) return false;

        vector = entry.Vector;
        return true;
    }
}
