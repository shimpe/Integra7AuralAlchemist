using System.Globalization;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What an export is called and what is said about it afterwards.
///
/// <b>Here rather than in the view model, because the sentences are the answer.</b> A patch list carries two
/// lists of prose -- what shares an address and what could not be given a program -- and they are not
/// diagnostics: a file that quietly lost a patch is byte-for-byte the sort of thing a correct file looks
/// like, and the user finds out when a track plays the wrong sound. So the words the user reads are the last
/// place the truth can still be told, and they belong somewhere a test can call them. A view model in this
/// application cannot be constructed in a test at all, so a sentence composed there is a sentence nothing
/// checks.
///
/// <b>Nothing here touches a file.</b> Naming one and describing one are both string work; the writing is
/// the caller's, because only the caller can report a failure.</summary>
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
    /// carries -- wire numbering, so the instrument's own printed tone list will say one more.</summary>
    /// <param name="list">What was written, including what it could not represent.</param>
    /// <param name="fileName">The file's own name, not its path: the path is the user's, they chose it a
    /// second ago, and it is usually longer than the status bar.</param>
    public static string Outcome(PatchList list, string fileName)
    {
        var patches = list.Banks.Sum(bank => bank.Patches.Count);

        // Nought is said differently rather than dropped into the ordinary sentence. An empty list is what a
        // failed preset load looks like from here -- the file is still written, and it is still valid -- and
        // "Exported 0 patches" reads as a success while being the one outcome the user can do something
        // about.
        var said = patches == 0
            ? $"Wrote {fileName}, but there were no patches to export."
            : $"Exported {patches.ToString("N0", CultureInfo.InvariantCulture)} " +
              $"{(patches == 1 ? "patch" : "patches")} to {fileName}.";

        if (list.Collisions.Count > 0)
            said += list.Collisions.Count == 1
                ? $" 1 address carries more than one patch ({list.Collisions[0]}); your DAW will show one " +
                  "of them."
                : $" {list.Collisions.Count} addresses carry more than one patch, the first being " +
                  $"{list.Collisions[0]}; your DAW will show one patch at each.";

        if (list.Skipped.Count > 0)
            said += list.Skipped.Count == 1
                ? $" 1 patch was left out because its program cannot be sent: {list.Skipped[0]}."
                : $" {list.Skipped.Count} patches were left out because their programs cannot be sent, " +
                  $"the first being {list.Skipped[0]}.";

        return said;
    }
}
