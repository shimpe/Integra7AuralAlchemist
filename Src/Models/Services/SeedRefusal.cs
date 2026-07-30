using System;
using System.IO;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Why a sweep of the instrument into the library may not start, and which of the reasons to say
/// when more than one of them applies.
///
/// <b>The order is the whole content of this file.</b> The three conditions are one line of code each and
/// would be unremarkable inline; what is worth a tested service is which sentence a user reads first and
/// whether it tells them what to do about it. Both of those are decisions, both are invisible in a screenshot
/// of a working application, and neither can be pinned in a view model -- ReactiveUI 24 will not let one be
/// constructed in a test at all, which is why every rule in this feature lives beside its neighbours here.
///
/// <b>Compare comes first, because it is the only one of the three that can cost the user work.</b> While
/// comparing, the instrument is playing the sound from before the edits and this application's journal holds
/// the only copy of the edits themselves -- and a sweep overwrites the borrowed part once per patch and then
/// puts a whole Studio Set back over it. The other two describe a run that cannot begin: an instrument that is
/// not plugged in stays not plugged in, and a folder that refuses to be written to goes on refusing, so
/// nothing is lost by hearing about either of them a minute later. A comparison is not like that. It is a
/// state the user is standing in, and the minutes spent fetching a MIDI cable or choosing another folder are
/// minutes in which anything that clears the journal -- loading a tone, an audition, a Studio Set chosen on
/// the front panel -- takes the edits with it. When two of these apply at once, the time-critical one is the
/// one to say.
///
/// <b>Then the instrument, because a folder is only a problem when there is something to put in it.</b>
/// Neither of the last two costs anything to learn about late, so the tie-break is not urgency but which
/// sentence describes the run that was actually asked for: this panel sweeps an instrument, and complaining
/// about the destination of a sweep that has no source names the second problem first. It is also the refusal
/// every other instrument-facing action in this window already gives, in nearly these words, so it is the one
/// a user has already learnt to read.
///
/// <b>One reason, not a list.</b> Three complaints at once about a button that did nothing reads as a broken
/// feature rather than as three things to fix; a user fixes one and presses Start again, and pressing Start
/// again is what asks the question a second time.</summary>
public static class SeedRefusal
{
    /// <summary>Why the sweep may not start, or null when it may. The parameters are in the order the reasons
    /// are answered in, so the precedence this file is about is visible in the signature as well as in the
    /// body.</summary>
    /// <param name="comparing">Whether the edit journal is holding the user's edits while the instrument
    /// plays the sound from before them -- <c>EditJournal.IsComparing</c>. Read from the journal itself
    /// rather than from anything mirrored onto the UI thread: this is a guard against losing work, not a
    /// button's enabled state.</param>
    /// <param name="haveInstrument">Whether there is a connection to sweep.</param>
    /// <param name="folderTrouble">What went wrong when the library folder was asked to take a file, or null
    /// when it took one -- <see cref="FolderTrouble"/>.</param>
    public static string? Reason(bool comparing, bool haveInstrument, string? folderTrouble)
    {
        if (comparing)
            return "The sweep would overwrite the part Compare is holding your edits for, and while you are "
                   + "comparing, this application is the only place those edits exist. Press Compare again "
                   + "to put them back on the instrument, then start the sweep.";

        if (!haveInstrument)
            return "There is no connection to the instrument, so there is nothing to sweep. Connect your "
                   + "Integra-7 and press Start again.";

        // Parenthesised rather than trailing, as SeedRun's restore warning parenthesises the device's own
        // words and for the same reason: nothing here controls whether a file system's message ends in a
        // full stop, and one that does not would run into the sentence saying what to do about it.
        if (folderTrouble is not null)
            return $"The library folder cannot be written to ({folderTrouble}). Choose another with Change… "
                   + "before starting: every patch is written the moment it is captured, so a sweep with "
                   + "nowhere to put them would be an hour of your instrument spent for nothing.";

        return null;
    }

    /// <summary>Ask the library folder to take a file. Answers what went wrong, or null when it took one.
    ///
    /// <b>Asked by writing rather than by looking.</b> <c>Directory.Exists</c> answers a different question --
    /// a share mounted read-only, a folder whose permissions were changed since the last save, and a path that
    /// is really a file all exist perfectly well and take nothing -- and the cost of finding that out later is
    /// six thousand captures with nowhere to go, each of them an irreplaceable minute of the instrument's time.
    /// The folder is created first because <c>SnapshotLibrary.Create</c> creates it too: a library folder that
    /// does not exist yet is the normal state of a fresh install, not a refusal.
    ///
    /// <b>The probe is removed, and named so that being left behind is harmless.</b> A crash between the write
    /// and the delete would strand it, so it ends in <c>.tmp</c> rather than <c>.json</c> and the browser --
    /// which lists <see cref="SnapshotLibrary.FilePattern"/> -- would not show it, nor would the duplicate scan
    /// try to read it as a snapshot. A delete that fails is logged rather than reported: the question this was
    /// asked is whether the folder takes a file, and it did.</summary>
    public static string? FolderTrouble(string folder)
    {
        var probe = Path.Combine(folder, $"seed-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(probe, []);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception e)
            {
                UserActionLog.Failed($"remove the library folder probe file '{probe}'", e.ToString());
            }
        }
    }
}
