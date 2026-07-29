using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One snapshot file in the library: where it is, what its head says, and when it last changed.
///
/// The path is called <c>FilePath</c> rather than <c>Path</c> for a small but real reason: a record property
/// named <c>Path</c> shadows <see cref="System.IO.Path"/> inside the record's own body, so the obvious next
/// member somebody adds here -- a file name, a folder -- stops compiling for a reason that takes a minute to
/// see. Two extra characters at every call site is the cheaper half of that trade.
///
/// <paramref name="Modified"/> is the file's last-write time in local time, because it exists to be shown in
/// a list beside a name and a rating, and a user reads "yesterday 19:40" rather than an offset from UTC.
/// Sorting by it is unaffected either way.</summary>
public sealed record LibraryEntry(string FilePath, SnapshotHead Head, DateTime Modified);

/// <summary>What the library lets a user change about a snapshot after the fact: the five annotations, and --
/// since the browser was built -- the name.
///
/// A record rather than parameters on <see cref="SnapshotLibrary.WriteMetadata"/> because several of them are
/// strings that would sit next to each other in a call and transpose silently -- a category written into the
/// notes is not something any type system would have caught, and not something a test would either unless it
/// happened to look.
///
/// <b><paramref name="Name"/> is the odd one out, and it is last for two reasons.</b> It is not an annotation:
/// it is what the file is called <i>inside</i> itself, it is what <c>CaptureAsync</c> took from the instrument,
/// and rewriting it is a rename. This record originally left it out and said that if the library's editor ever
/// offered one it would be one field and one line here rather than a second write path -- so that is what it
/// is. It is <b>nullable, and null means leave the name alone</b>, which keeps every caller that only wants to
/// annotate saying exactly that, and keeps "rename" something a caller has to ask for by name. And it is
/// appended rather than put first because the five above are passed positionally in places, and quietly
/// shifting them would have moved a tag list into a category.
///
/// <paramref name="Tags"/> is nullable and read through <see cref="TagList"/>, for exactly the reason
/// <see cref="Integra7Snapshot.Tags"/> is: a defaulted parameter has to be a constant expression, and an empty
/// list is not one.</summary>
public sealed record SnapshotMetadata(
    string Category = "",
    IReadOnlyList<string>? Tags = null,
    string Notes = "",
    int Rating = 0,
    bool Favourite = false,
    string? Name = null)
{
    public IReadOnlyList<string> TagList => Tags ?? [];
}

/// <summary>The library: one folder of snapshot files, listed for browsing, and annotated in place.
///
/// <b>What a listing costs.</b> Every file in the folder is opened and its head read --
/// <see cref="SnapshotHead"/> carries the reasoning for why that is affordable and why parsing each file in
/// full would not be. There is no index and nothing cached, on purpose: the metadata lives in the files, so a
/// file copied in from elsewhere is complete the moment it lands, and there is nothing that can go stale when
/// files are added or removed outside the application. The cost of that decision is paid here, once per
/// refresh.
///
/// <b>Sub-folders are not enumerated.</b> The library is one folder, not a tree. That is a stated limitation
/// rather than an oversight: a tree needs a way to show, choose and save into a branch, which is a feature of
/// its own and not one this has been asked for. If it turns out to be how a large library wants to be
/// organised, it is <c>SearchOption.AllDirectories</c> here plus that user interface -- so the limitation is
/// deliberately in one place.
///
/// <b>A stray file is skipped, not reported.</b> A library folder is a folder; the user can and will put other
/// things in it, and another application's <c>config.json</c> sitting beside the snapshots must not produce an
/// error nobody can act on. So anything <see cref="SnapshotHead.TryRead"/> refuses is passed over silently.
/// That silence is only tolerable because it is narrow: what makes a file a snapshot is that it carries a
/// format version, and a file that has one is listed whatever else is wrong with it -- wrong version, rating
/// of seven, no name -- and refused, by name, when it is opened. (It was not always narrow enough. A snapshot
/// re-saved by an editor that added a byte-order mark used to take this same silent exit; see
/// <c>ByteOrderMark</c>.)
///
/// <b>Every write and every delete keeps the copy it replaced</b> -- see <see cref="PatchHistory"/>. The
/// archive is taken before the change and is allowed to throw, which refuses the change: this class replaces
/// a file by renaming over it, so a write that continued past a failed archive would destroy the only copy.
/// </summary>
public static class SnapshotLibrary
{
    /// <summary>Snapshots are JSON, so this is what the library looks for. Named here rather than written into
    /// the one call so that a caller filtering a file dialog cannot disagree with what a listing reads.
    ///
    /// Four characters of extension, which matters more than it looks: <c>Directory.EnumerateFiles</c> on
    /// Windows matches a *three*-character extension as a prefix -- "*.xls" famously also returns .xlsx -- so
    /// anyone shortening this pattern would be widening it at the same time.</summary>
    public const string FilePattern = "*.json";

    /// <summary>Every snapshot in <paramref name="folder"/>, in whatever order the file system offers them --
    /// sorting is the browser's business, and it offers the user several.
    ///
    /// <b>A folder that is not there lists as empty.</b> That is the normal state of the default library
    /// folder until the first save, so refusing to list it would mean the library could only ever open on an
    /// error where "nothing here yet" is the truth. It is also what a folder on a drive that is not mounted
    /// looks like, and what <c>Directory.Exists</c> answers for a folder that exists but cannot be examined at
    /// all -- that method reports false for every failure rather than throwing -- so those three cases collapse
    /// into one answer here whatever this method would prefer.
    ///
    /// <b>A folder that refuses to be enumerated does throw</b>, and the asymmetry is the point. "Empty" is a
    /// true answer for a folder with nothing in it and a lie about a folder whose contents were denied; a user
    /// whose library is on a share they have lost access to needs to be told that, because "your library is
    /// empty" would send them looking for files that are exactly where they left them. This follows
    /// <see cref="LibrarySettings"/>'s split -- reading a settings file cannot fail because it has a right
    /// answer for every failure, and this has one for a missing folder and none for a refused one.
    ///
    /// <b>One unreadable file costs that file only.</b> A snapshot held open by a sync client, a virus
    /// scanner, or this application's own save a moment ago is skipped and logged; letting it throw would mean
    /// one locked file emptied the whole browser, which is a far worse trade than one row missing from a list
    /// the user can refresh.</summary>
    public static IReadOnlyList<LibraryEntry> Read(string folder)
    {
        List<LibraryEntry> entries = [];
        if (!Directory.Exists(folder)) return entries;

        foreach (var path in Directory.EnumerateFiles(folder, FilePattern, SearchOption.TopDirectoryOnly))
        {
            SnapshotHead? head;
            DateTime modified;
            try
            {
                // One FileInfo for both the timestamp and the open, so that a file which vanishes between
                // the enumeration and the read fails once, here, rather than being listed with the
                // 1601-01-01 that File.GetLastWriteTime answers for a path that is no longer there.
                var file = new FileInfo(path);
                using var stream = file.OpenRead();
                head = SnapshotHead.TryRead(stream);
                modified = file.LastWriteTime;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Locked, denied, or gone since the enumeration named it. Logged rather than swallowed, so
                // that "a snapshot I can see in Explorer is not in the library" has something behind it.
                Log.Warning(e, "Could not read the snapshot {Path}; leaving it out of the library listing", path);
                continue;
            }

            // Not a snapshot: somebody else's JSON, or a text file that happens to end in .json. Skipped
            // without a word, for the reason on the class above.
            if (head is null) continue;

            entries.Add(new LibraryEntry(path, head, modified));
        }

        return entries;
    }

    /// <summary>Replace the annotations -- and, if <see cref="SnapshotMetadata.Name"/> says so, the name -- on
    /// the snapshot at <paramref name="filePath"/>, leaving every parameter in it exactly as it was.
    ///
    /// <b>The file is read back before it is written</b>, and that is the whole design of this method rather
    /// than a step in it. The metadata lives in the same file as ~1,500 parameter values, so annotating a
    /// sound means rewriting the file that holds the sound -- and the only thing that guarantees those values
    /// come out the way they went in is that they come from the file itself, seconds earlier, and never from
    /// anything this application is holding in memory. There is no in-memory snapshot in this method's
    /// signature for a caller to accidentally hand it, which is the strongest form that guarantee can take:
    /// editing a note <i>cannot</i> rewrite a parameter value, because nothing here knows one.
    ///
    /// <b>It is written atomically</b> -- see <see cref="Write"/>, and note that the reasoning there matters
    /// more for this method than for any other caller of it: a capture creates a file the user does not have
    /// yet, and this rewrites one they do.
    ///
    /// <b>A file that cannot be opened cannot be annotated.</b> Reading goes through
    /// <see cref="Integra7Snapshot.FromJson"/>, which judges the file -- so a hand-edited snapshot with a
    /// rating of seven or a version this build does not read throws here, with the message
    /// <c>FromJson</c> gives, rather than being quietly rewritten into something readable. Silently repairing
    /// a file the user has edited is not this method's business, and doing it as a side effect of adding a tag
    /// would be the worst possible time to do it.</summary>
    public static void WriteMetadata(string filePath, SnapshotMetadata metadata)
    {
        Write(filePath, Annotated(Integra7Snapshot.FromJson(File.ReadAllText(filePath)), metadata));
    }

    /// <summary>Write <paramref name="snapshot"/> into <paramref name="folder"/> as a new file, named after the
    /// snapshot itself, and answer where it went.
    ///
    /// <b>This is what "save into the library" is</b>, as opposed to <see cref="WriteMetadata"/>, which is what
    /// "annotate what is already in the library" is. The two are different operations on purpose -- one takes an
    /// in-memory snapshot the instrument was just read into, the other refuses to see one (read its remarks) --
    /// and they share the one thing they must: <see cref="Write"/>, so there is a single place that turns a
    /// snapshot into bytes on disk, and <see cref="Annotated"/>, so there is a single place that decides what
    /// the metadata fields mean.
    ///
    /// <b>The folder is created if it is not there.</b> That is the normal first-save state of the default
    /// library folder, and <c>LibrarySettings</c> deliberately does not create it -- recording where the library
    /// is and putting a file in it are different questions, and this is the one that needs the folder to exist.
    ///
    /// The file name is <see cref="FileNameFor"/>'s, made unique against what is already there. A name that
    /// collides gets " (2)", " (3)" and so on: the alternative is overwriting a snapshot the user still has, and
    /// two sounds called "Init Tone" is exactly what a library of captures looks like.</summary>
    public static string Create(string folder, Integra7Snapshot snapshot, SnapshotMetadata metadata)
    {
        var annotated = Annotated(snapshot, metadata);
        Directory.CreateDirectory(folder);
        var path = UniquePath(folder, FileNameFor(annotated.Name));
        Write(path, annotated);
        return path;
    }

    /// <summary>Remove a snapshot from the library.
    ///
    /// <b>Not a move to the recycle bin</b>, because .NET has no cross-platform API for one and this
    /// application runs on all three desktops. What stands in its place is <see cref="PatchHistory"/>: the
    /// file is copied into the history folder first, so a deletion is recoverable by someone who knows that
    /// folder is there. The confirmation the caller asks for is still what stops it happening by accident,
    /// because leaving the library is what the user will notice, not the copy that remains.
    ///
    /// A file that is already gone is not an error: the listing is a picture of a folder other things can
    /// change, so by the time the user presses Delete another copy of this application, a file manager or
    /// a sync client may have removed it. The folder ends in the state they asked for either way.
    /// Everything else -- a denied folder, a file another process holds open, a directory sitting where a
    /// snapshot should be -- is thrown, because the caller is the only one who can say the snapshot is
    /// still there.</summary>
    public static void Delete(string filePath)
    {
        if (!File.Exists(filePath))
        {
            // Deliberately checked rather than caught: File.Delete does not throw for a missing file,
            // but it does for a *directory* at that path, and this must not swallow that.
            if (Directory.Exists(filePath))
                throw new IOException(
                    $"Cannot delete \"{filePath}\": it is a folder, not a snapshot file.");

            Log.Information("Not deleting {Path}: it is no longer there.", filePath);
            return;
        }

        // The only way back: this does not use the recycle bin, because .NET has no cross-platform API for
        // one. Allowed to throw, so a deletion that cannot be undone does not happen.
        PatchHistory.Archive(filePath);
        File.Delete(filePath);
        Log.Information("Deleted the snapshot {Path} from the library.", filePath);
    }

    /// <summary>What no file name here may hold.
    ///
    /// Deliberately <b>not</b> <see cref="Path.GetInvalidFileNameChars"/> on its own: that answers for the
    /// platform this build happens to be running on, and on Linux and macOS the answer is two characters -- NUL
    /// and '/'. A library folder is a folder like any other, so it is synced, shared and copied between
    /// machines, and a snapshot written on Linux as "Pad:2/3*.json" is a file Windows cannot receive at all.
    /// The set below is Windows' -- the strictest of the three -- unioned with whatever the running platform
    /// adds, so a name is scrubbed identically everywhere and the file it produces can travel.
    ///
    /// The union repeats characters on Windows, which does not matter: this is only ever a separator set for
    /// <see cref="string.Split(char[])"/>.</summary>
    private static readonly char[] IllegalInAFileName =
    [
        ..Path.GetInvalidFileNameChars(),
        '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        ..Enumerable.Range(0, 32).Select(c => (char)c),
    ];

    /// <summary>A file name for a snapshot called <paramref name="name"/>: the name itself where that is legal,
    /// with a .json extension.
    ///
    /// Pure, and separate from <see cref="Create"/> so that it can be tested without a disk. The instrument's
    /// character set includes ':', '/' and '*', none of which a file name can hold, and the same substitution
    /// the save dialogs already make on their suggested name is made here -- the snapshot keeps the real name
    /// inside itself either way, which is why scrubbing the file name loses nothing.
    ///
    /// A name that is nothing but unusable characters, or nothing at all, becomes "Snapshot": a file called
    /// "_.json", or ".json" -- which on Windows is a hidden-extension file with no name -- would be worse than a
    /// generic one, and the browser lists what is inside the file rather than what it is called.</summary>
    public static string FileNameFor(string name)
    {
        var scrubbed = string.Join("_", (name ?? "").Split(IllegalInAFileName)).Trim();
        // Trailing dots and spaces are legal in the string and not in a Windows file name -- the API silently
        // drops them, so "Warm Rhodes ." would be created as "Warm Rhodes" and a uniqueness check done on the
        // longer name would not see the collision.
        scrubbed = scrubbed.TrimEnd('.', ' ');
        return (scrubbed.Length == 0 ? "Snapshot" : scrubbed) + ".json";
    }

    /// <summary><paramref name="fileName"/> in <paramref name="folder"/>, suffixed until it names nothing that
    /// is there. The loop is bounded by nothing but the file system, which is right: it stops the first time it
    /// asks a question whose answer is no, and the only way it does not stop is a folder that is filling up as
    /// fast as it is read.</summary>
    private static string UniquePath(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var path = Path.Combine(folder, fileName);
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }

    /// <summary><paramref name="snapshot"/> with <paramref name="metadata"/>'s fields on it. The one place the
    /// metadata record is turned into a snapshot's own fields, so that saving a new file into the library and
    /// annotating one already in it cannot come to disagree about what any of them means.</summary>
    private static Integra7Snapshot Annotated(Integra7Snapshot snapshot, SnapshotMetadata metadata)
    {
        // Refused before anything is written, for the rule the converter already follows about null tags: the
        // worst thing a writer here can produce is a file this build writes and then refuses to read. A star
        // control cannot produce a rating outside the range, so this is about a caller with a bug rather than
        // a user with a mouse -- which is exactly the case that would otherwise turn a good file into an
        // unopenable one.
        if (metadata.Rating is < 0 or > 5)
            throw new SnapshotFormatException(
                $"A rating of {metadata.Rating} cannot be saved; ratings run from 0 to 5.");

        // A blank name is refused for the same reason and one more: it is the only metadata field whose absence
        // the browser cannot show. An entry with no name is a row the user cannot tell from the row above it,
        // and the file it names is the one thing here they may have no other copy of. Null is not blank -- it is
        // "do not touch the name" -- so this only fires on a caller that asked for a rename and had nothing to
        // rename it to, which is a bug rather than a preference.
        if (metadata.Name is not null && string.IsNullOrWhiteSpace(metadata.Name))
            throw new SnapshotFormatException("A snapshot needs a name.");

        return snapshot with
        {
            Category = metadata.Category,
            // Copied into a List because that is what the record holds, and copied rather than shared so that
            // a caller mutating its own list afterwards cannot change what a snapshot in flight says.
            Tags = [..metadata.TagList],
            Notes = metadata.Notes,
            Rating = metadata.Rating,
            Favourite = metadata.Favourite,
            // The one clause the name needed. Null leaves what the file already says -- see SnapshotMetadata.
            Name = metadata.Name ?? snapshot.Name,
        };
    }

    /// <summary>Put <paramref name="snapshot"/> at <paramref name="filePath"/>, atomically.
    ///
    /// <b>A sibling temp file, then a rename over the target</b>, which is atomic on the same volume -- for the
    /// reason <c>MainWindowViewModel.SaveStudioSetAsync</c> gives: this file may be the user's only copy of a
    /// Studio Set, and a failure partway through a direct write must not destroy what was already there. That
    /// matters most for <see cref="WriteMetadata"/>, which rewrites a file the user already has rather than
    /// creating one, and costs nothing for <see cref="Create"/>. The temp file is named beside the target rather
    /// than under the system temp folder so that the rename stays on one volume; it does not end in .json, so a
    /// listing racing this write cannot see it.</summary>
    private static void Write(string filePath, Integra7Snapshot snapshot)
    {
        // Before anything else, and allowed to throw: this method replaces the file by renaming over it, so
        // proceeding after a failed archive would destroy the previous version at the exact moment it has
        // been established that no copy can be kept. A no-op when the file does not exist, which is what
        // Create looks like.
        PatchHistory.Archive(filePath);

        var json = Integra7Snapshot.ToJson(snapshot);
        var tempPath = filePath + ".saving";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            // A stray temp file beside every snapshot the user ever failed to annotate would be a mess that
            // never cleans itself up. Cleaning up must not replace the failure being reported, which is the
            // only reason this swallows anything.
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception cleanup)
            {
                Log.Warning(cleanup, "Could not remove the temporary snapshot file {Path}", tempPath);
            }

            throw;
        }
    }
}
