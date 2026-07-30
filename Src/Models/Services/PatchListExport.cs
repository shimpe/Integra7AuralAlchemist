using System.Globalization;
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What an export is called, what bytes it is made of, and what is said about it afterwards.
///
/// <b>Here rather than in the view model, because all three are things that can be got wrong silently.</b> A
/// patch list carries two lists of prose -- what shares an address and what could not be given a program --
/// and they are not diagnostics: a file that quietly lost a patch is byte-for-byte the sort of thing a
/// correct file looks like, and the user finds out when a track plays the wrong sound. The byte-order mark is
/// the same shape of problem one level down. A view model in this application cannot be constructed in a test
/// at all, so anything decided there is decided where nothing can check it.
///
/// <b>Nothing here touches a file.</b> Naming one, describing one and turning a patch list into the bytes of
/// one are all pure; the writing is the caller's, because only the caller can report a failure.</summary>
public static class PatchListExport
{
    /// <summary>What to suggest calling the file: the instrument's name and the format's extension.
    ///
    /// <b>The device name is this application's own constant</b> (<see cref="PatchListSource"/> defaults it
    /// to "INTEGRA-7" and only ever gets that), so this does not scrub it the way
    /// <c>SnapshotLibrary.FileNameFor</c> scrubs a name the instrument gave a sound. What it does guard
    /// against is the name being nothing, because ".csv" on Windows with extensions hidden is a file with no
    /// name at all -- and the user is about to look for this file in another application's import
    /// dialog.</summary>
    public static string FileNameFor(string device, string extension)
    {
        var name = (device ?? "").Trim();
        return $"{(name.Length == 0 ? "Patch list" : name)}.{extension}";
    }

    /// <summary>The exact bytes of the file, mark and all.
    ///
    /// <b>The join between a writer's answer and a file is here so that a test can see it.</b> Each writer
    /// says whether its format wants a byte-order mark, and each of those answers is pinned by a test -- but
    /// the line that turned them into a file used to sit in a view model, where hardcoding either answer left
    /// the whole suite green. Both failures are silent and opposite: Excel opening a BOM-less UTF-8 .csv by
    /// double-click falls back to the system code page and mangles the 84 factory names that carry a curly
    /// apostrophe, while Reaper's parser and several midnam readers take a leading mark as part of the first
    /// token and lose a whole bank.
    ///
    /// <b>The mark is written out by hand rather than left to the encoding.</b> A <c>UTF8Encoding</c>
    /// constructed to emit one still does not put it in <c>GetBytes</c> -- only a <c>StreamWriter</c> creating
    /// a file emits the preamble -- so bytes produced the obvious way would silently never carry it. That
    /// asymmetry is a large part of why this is worth being a function with a name.</summary>
    public static byte[] BytesFor(IPatchListWriter writer, PatchList list)
    {
        var encoding = new UTF8Encoding(writer.WantsByteOrderMark);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(writer.Write(list));
        if (preamble.Length == 0) return body;

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    /// <summary>What the status bar says once the bytes are down.
    ///
    /// <b>The count is grouped with the invariant separator</b>, not the machine's. Six thousand of anything
    /// is unreadable without one, and the invariant one keeps the sentence the same on every machine the
    /// application runs on -- which is what lets it be tested at all.
    ///
    /// <b>Only the first of each list is named.</b> The status bar is one line; a list of every collision
    /// would fill it and be scrolled away unread, and a bare count would tell the user something happened
    /// while giving them nothing to look at. The first, with the count beside it, is the most that fits and
    /// the least that is actionable. The collision strings are the builder's own, quoted verbatim, so the
    /// address and the program number the user reads here are the ones the file they just wrote actually
    /// carries -- wire numbering, so the instrument's own printed tone list will say one more.
    ///
    /// <b>A collision is not described as the DAW hiding a name.</b> Three of the four formats can name two
    /// patches at one address, and their readers will list both; what the two share is the program change, so
    /// which sound arrives is settled by the instrument. Telling a user to expect one entry, in a menu that
    /// then shows two, would teach them the message is unreliable about everything else too.</summary>
    /// <param name="list">What was written, including what it could not represent.</param>
    /// <param name="fileName">The file's own name, not its path: the path is the user's, they chose it a
    /// second ago, and it is usually longer than the status bar.</param>
    /// <param name="userMemoryComplete">Whether the sweep that reads the instrument's user-tone names had
    /// finished when the list was taken.
    ///
    /// <b>Without this the sentence is confidently wrong in the two states it matters in.</b> The names
    /// arrive over tens of seconds of sysex after a connection, and a rescan that fails to reconnect cancels
    /// the sweep while leaving the half-filled list in place for the rest of the session -- so an export can
    /// be missing some or all of the user's own sounds while reading exactly like a complete one. Which tones
    /// are absent is not knowable here; that some are is, and it is what sends the user to press Rescan
    /// rather than to conclude the feature is broken.</param>
    public static string Outcome(PatchList list, string fileName, bool userMemoryComplete)
    {
        var patches = list.Banks.Sum(bank => bank.Patches.Count);

        // Nought is said differently rather than dropped into the ordinary sentence. An empty list is what a
        // failed preset load looks like from here -- the file is still written, and it is still valid -- and
        // "Exported 0 patches" reads as a success while being the one outcome the user can do something
        // about. Nothing is said about user memory either: there are no contents to characterise.
        if (patches == 0) return $"Wrote {fileName}, but there were no patches to export.";

        var said = $"Exported {Count(patches)} {(patches == 1 ? "patch" : "patches")} to {fileName}." +
                   UserMemoryClause(list, userMemoryComplete);

        if (list.Collisions.Count > 0)
            said += list.Collisions.Count == 1
                ? $" 1 address carries more than one patch ({list.Collisions[0]}); both are in the file and " +
                  "both send the same program change, so which sound you get is the instrument's decision."
                : $" {list.Collisions.Count} addresses carry more than one patch, the first being " +
                  $"{list.Collisions[0]}; every name is in the file, and the ones sharing an address all " +
                  "send the same program change.";

        if (list.Skipped.Count > 0)
            said += list.Skipped.Count == 1
                ? $" 1 patch was left out because its program cannot be sent: {list.Skipped[0]}."
                : $" {list.Skipped.Count} patches were left out because their programs cannot be sent, " +
                  $"the first being {list.Skipped[0]}.";

        return said;
    }

    /// <summary>Whether the user's own sounds are in the file, which is the question a patch list is exported
    /// to answer and the one a bare total cannot. Four states rather than two, because "we read the user
    /// memory and it was empty" and "nobody read it" are opposite facts with opposite remedies, and a partial
    /// read has to say so without pretending to know how much is missing.</summary>
    private static string UserMemoryClause(PatchList list, bool complete)
    {
        var user = list.Banks.Sum(bank => bank.Patches.Count(patch => patch.UserMemory));

        if (complete)
            return user == 0
                ? " None of them are from the instrument's user memory."
                : $" {Count(user)} of them are your own, from the instrument's user memory.";

        return user == 0
            ? " No user-memory tones are in it: none had been read from the instrument."
            : $" {Count(user)} of them are your own, but the user memory was still being read, so some are " +
              "missing.";
    }

    /// <summary>A count as the sentence spells it. Invariant, so that two machines describe one export the
    /// same way and so that the wording can be asserted at all.</summary>
    private static string Count(int howMany) => howMany.ToString("N0", CultureInfo.InvariantCulture);
}
