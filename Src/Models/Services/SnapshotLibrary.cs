using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>The five things about a snapshot that the library lets a user change after the fact.
///
/// A record rather than five parameters on <see cref="SnapshotLibrary.WriteMetadata"/> because two of the five
/// are strings that would sit next to each other in a call and transpose silently -- a category written into
/// the notes is not something any type system would have caught, and not something a test would either unless
/// it happened to look.
///
/// The name is deliberately not here. It is what the file is called <i>inside</i> itself, it is what
/// <c>CaptureAsync</c> took from the instrument, and rewriting it is a rename rather than an annotation. If
/// the library's editor is to offer one, this is where it goes and the reasoning above is what it has to
/// answer -- it is one field and one line, not a second write path.
///
/// <paramref name="Tags"/> is nullable and read through <see cref="TagList"/>, for exactly the reason
/// <see cref="Integra7Snapshot.Tags"/> is: a defaulted parameter has to be a constant expression, and an empty
/// list is not one.</summary>
public sealed record SnapshotMetadata(
    string Category = "",
    IReadOnlyList<string>? Tags = null,
    string Notes = "",
    int Rating = 0,
    bool Favourite = false)
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
/// <c>ByteOrderMark</c>.)</summary>
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

    /// <summary>Replace the five annotations on the snapshot at <paramref name="filePath"/>, leaving every
    /// parameter in it exactly as it was.
    ///
    /// <b>The file is read back before it is written</b>, and that is the whole design of this method rather
    /// than a step in it. The metadata lives in the same file as ~1,500 parameter values, so annotating a
    /// sound means rewriting the file that holds the sound -- and the only thing that guarantees those values
    /// come out the way they went in is that they come from the file itself, seconds earlier, and never from
    /// anything this application is holding in memory. There is no in-memory snapshot in this method's
    /// signature for a caller to accidentally hand it, which is the strongest form that guarantee can take:
    /// editing a note <i>cannot</i> rewrite a parameter value, because nothing here knows one.
    ///
    /// <b>It is written atomically</b> -- a sibling temp file, then a rename over the target, which is atomic
    /// on the same volume -- for the reason <c>MainWindowViewModel.SaveStudioSetAsync</c> gives: this file may
    /// be the user's only copy of a Studio Set, and a failure partway through a direct write must not destroy
    /// what was already there. That matters more here than it does for a capture, because a capture writes a
    /// file the user is creating and this rewrites one they already have. The temp file is named beside the
    /// target rather than under the system temp folder so that the rename stays on one volume; it does not
    /// end in .json, so a listing racing this write cannot see it.
    ///
    /// <b>A file that cannot be opened cannot be annotated.</b> Reading goes through
    /// <see cref="Integra7Snapshot.FromJson"/>, which judges the file -- so a hand-edited snapshot with a
    /// rating of seven or a version this build does not read throws here, with the message
    /// <c>FromJson</c> gives, rather than being quietly rewritten into something readable. Silently repairing
    /// a file the user has edited is not this method's business, and doing it as a side effect of adding a tag
    /// would be the worst possible time to do it.</summary>
    public static void WriteMetadata(string filePath, SnapshotMetadata metadata)
    {
        // Refused before the file is touched, for the rule the converter already follows about null tags: the
        // worst thing a writer here can produce is a file this build writes and then refuses to read. A star
        // control cannot produce a rating outside the range, so this is about a caller with a bug rather than
        // a user with a mouse -- which is exactly the case that would otherwise turn a good file into an
        // unopenable one.
        if (metadata.Rating is < 0 or > 5)
            throw new SnapshotFormatException(
                $"A rating of {metadata.Rating} cannot be saved; ratings run from 0 to 5.");

        var snapshot = Integra7Snapshot.FromJson(File.ReadAllText(filePath));
        var json = Integra7Snapshot.ToJson(snapshot with
        {
            Category = metadata.Category,
            // Copied into a List because that is what the record holds, and copied rather than shared so that
            // a caller mutating its own list afterwards cannot change what a snapshot in flight says.
            Tags = [..metadata.TagList],
            Notes = metadata.Notes,
            Rating = metadata.Rating,
            Favourite = metadata.Favourite,
        });

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
