using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Where the snapshot library lives, remembered between sessions.
///
/// This is the first thing this application has ever persisted, so it is deliberately the smallest thing
/// that can work: one JSON file in the user's application data, holding one folder path. No settings
/// framework, no schema, no migration, and nothing else moves in here without a reason of its own. The
/// moment a second setting appears this will want a shape rather than a method per field, and it will be
/// obvious; guessing at that shape now, for a file with one line in it, would be building the framework
/// this comment exists to say we are not building.
///
/// <b><see cref="Load"/> never throws.</b> A missing file is the first run. An unreadable or malformed one
/// is a file somebody edited, or a disk that failed, or a folder that has become unreadable -- and none of
/// those is a reason to refuse to start. Every one of them answers with <see cref="DefaultFolder"/>, which
/// the user can then change again; a dialog they cannot dismiss, on the way in, over a file holding one
/// path, would be much the worse outcome. The failure is logged, so that "my library folder keeps resetting"
/// has something behind it.
///
/// <b><see cref="Save"/> does throw</b>, and that asymmetry is deliberate rather than an oversight. Loading
/// has a right answer for every failure -- the default -- and the user did not ask for it to happen. Saving
/// has none: the user just chose a folder, and a save that quietly did nothing means the choice is silently
/// forgotten by the next session, with no way for the application to have said so. So a caller changing the
/// library folder is expected to catch and report it, the way the snapshot save commands already report a
/// write that failed.</summary>
public static class LibrarySettings
{
    /// <summary>The library folder used until the user picks one. Under Documents rather than in application
    /// data because it holds the user's own files -- snapshots they will want to find, copy and back up --
    /// and application data is where an application's private state goes, which these are not.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Integra7AuralAlchemist", "Library");

    /// <summary>Where the settings file really lives. <see cref="Load"/> and <see cref="Save"/> take the
    /// path as a parameter instead of reading the environment themselves -- that is what lets them be
    /// tested against a temp directory, and it is the difference between this having tests and not -- so this
    /// is the one place the real path is written down, for the callers that want it rather than a temp one.
    /// </summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Integra7AuralAlchemist", "settings.json");

    /// <summary>The file's shape, which is one property. A record rather than hand-rolled reader calls
    /// because there is nothing here worth hand-rolling; the snapshot format has a converter because it has
    /// ~1,500 values and an order that matters, and this has a string.
    ///
    /// The path is nullable so that a file which does not mention it deserializes rather than failing --
    /// "nothing said" is a state this file is allowed to be in, and it means the default.</summary>
    private sealed record Stored(string? LibraryFolder);

    /// <summary>Indented, for the same reason the snapshot files are: somebody will open this in an editor,
    /// and a settings file is a thing people edit.</summary>
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The library folder recorded in <paramref name="settingsPath"/>, or
    /// <see cref="DefaultFolder"/> if there is nothing usable there.
    ///
    /// <b>A stored path that no longer exists is returned as stored</b>, not replaced by the default. The
    /// folder may be on a drive that is not mounted yet, or on a share that is briefly unreachable, and
    /// silently pointing the library at Documents in either case would be the worst possible response: the
    /// user's library appears empty, and saving into it puts new files somewhere they did not ask for.
    /// Deciding a folder is gone -- and what to do about it -- is the caller's job, with the user in front
    /// of it.</summary>
    public static string Load(string settingsPath)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(settingsPath), Options);
            var folder = stored?.LibraryFolder;
            // Empty or blank is "nothing said", exactly as an absent property is. Passing it through would
            // resolve the library to the process's current directory, which is wherever the application
            // happened to be launched from -- a far stranger place to put a library than Documents.
            return string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
        }
        catch (Exception e)
        {
            // Deliberately everything, and the width is the point rather than laziness. This method's whole
            // contract is that it answers with a folder, and it has exactly one answer for every way reading
            // a file can fail: the file is not there (first run, by far the common case), it is not readable,
            // the folder above it is not readable, the path is not a legal path, the contents are not JSON,
            // the contents are JSON of the wrong shape. Narrowing this to the six or so exception types that
            // covers would produce a list that has to be complete, and the one left off it is a crash on
            // startup over a one-line file. Logged rather than swallowed, so that a folder which keeps
            // reverting has a reason recorded somewhere.
            Log.Warning(e, "Could not read the library settings at {Path}; using the default folder", settingsPath);
            return DefaultFolder;
        }
    }

    /// <summary>Record <paramref name="folder"/> as the library folder in <paramref name="settingsPath"/>,
    /// creating the folder the settings file itself lives in if this is the first run.
    ///
    /// Written atomically -- to a sibling temp file, then renamed over the target, which is atomic on the
    /// same volume -- for the reason <c>MainWindowViewModel.SaveStudioSetAsync</c> gives about snapshots: a
    /// failure partway through a direct write must not destroy what was already at the path. The stakes are
    /// smaller here, one path rather than a Studio Set, but the failure is worse than it looks: a settings
    /// file truncated mid-write is not JSON, so <see cref="Load"/> would answer with the default and the
    /// user's library would appear to have emptied itself. Two lines to make that impossible is a bargain.
    ///
    /// The library folder itself is not created here. This method records where the library is; whether that
    /// place exists is a separate question, and it is the same question <see cref="Load"/> refuses to answer
    /// for a stored path -- see there for why.</summary>
    public static void Save(string settingsPath, string folder)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        // Empty for a bare file name, which is a legal path relative to the current directory and has no
        // directory to create.
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = settingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new Stored(folder), Options));
            File.Move(tempPath, settingsPath, overwrite: true);
        }
        catch
        {
            // Leaving a stray .tmp beside the settings file would be a small mess that never cleans itself
            // up, since every later save writes the same name. Deleting it must not replace the failure
            // being reported, which is the only reason this swallows anything.
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception cleanup)
            {
                Log.Warning(cleanup, "Could not remove the temporary settings file {Path}", tempPath);
            }

            throw;
        }
    }
}
