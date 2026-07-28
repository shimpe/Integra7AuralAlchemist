using System;
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The application's first persisted setting: one file, one folder path in it.
///
/// What these tests are really pinning is that reading it cannot fail. A settings file is read on the way
/// in, before the user can do anything about it, and every way that read can go wrong -- absent, truncated,
/// hand-edited into something that is not JSON, on a folder that has become unreadable -- has the same right
/// answer, which is the default folder. The one thing that must *not* be treated as a failure is a stored
/// folder that is not there right now.
///
/// <b>Temp directories.</b> Nothing in Tests/ needed one before this -- the suite reads <c>parameters.bin</c>
/// and otherwise stays in memory -- so there was no house pattern to follow, and this is the one chosen: a
/// GUID-named directory per test, under one shared parent so that anything ever left behind is findable and
/// removable in one place, created in SetUp and removed in TearDown. Per test rather than per fixture so
/// that no test can see a file another one wrote, which is the failure mode that makes a suite mysteriously
/// order-dependent.</summary>
public class LibrarySettingsTests
{
    private string _folder = "";
    private string _settingsPath = "";

    [SetUp]
    public void CreateTempFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "Integra7AuralAlchemist.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _settingsPath = Path.Combine(_folder, "settings.json");
    }

    [TearDown]
    public void RemoveTempFolder()
    {
        // A deletion that fails -- an indexer or a virus scanner still holding a handle on a file written
        // milliseconds ago -- must not fail a test that actually passed. The directory is GUID-named, so what
        // is left behind is inert, and the shared parent above is where to look for it.
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"Could not remove the temp directory {_folder}: {e.Message}");
        }
    }

    [Test]
    public void A_missing_settings_file_is_the_first_run_and_gives_the_default()
    {
        // By far the common case, and the reason nothing here throws: on a machine that has never run this,
        // the settings file is absent and that is not a problem to report.
        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(LibrarySettings.DefaultFolder));
    }

    [Test]
    public void A_malformed_settings_file_gives_the_default_without_throwing()
    {
        // Somebody opened it in an editor, or a write was interrupted. Either way this is read on the way in,
        // before the user can do anything about it, so refusing to start over it would be the wrong trade.
        File.WriteAllText(_settingsPath, "library folder = D:\\Snapshots");

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(LibrarySettings.DefaultFolder));

        File.WriteAllText(_settingsPath, "[1, 2, 3]");

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(LibrarySettings.DefaultFolder),
            "JSON of the wrong shape is no more readable than text that is not JSON");
    }

    [Test]
    public void A_settings_file_that_says_nothing_about_the_folder_gives_the_default()
    {
        File.WriteAllText(_settingsPath, """{ "SomethingElse": 1 }""");

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(LibrarySettings.DefaultFolder));
    }

    [Test]
    public void A_stored_folder_that_is_blank_gives_the_default()
    {
        // Blank is "nothing said", exactly as an absent property is. Passing it through would resolve the
        // library to the process's current directory -- wherever the application happened to be launched
        // from, which is a far stranger place to keep a library than Documents.
        File.WriteAllText(_settingsPath, """{ "LibraryFolder": "   " }""");

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(LibrarySettings.DefaultFolder));
    }

    [Test]
    public void The_library_folder_survives_a_round_trip()
    {
        var chosen = Path.Combine(_folder, "My Snapshots");
        Directory.CreateDirectory(chosen);

        LibrarySettings.Save(_settingsPath, chosen);

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(chosen));
        Assert.That(File.ReadAllText(_settingsPath), Does.Contain("LibraryFolder"),
            "renaming the property would silently reset every existing installation's library folder");
    }

    /// <summary>The one failure that is not a failure. The folder may be on a drive that is not mounted yet,
    /// or on a share that is briefly unreachable; quietly pointing the library at Documents in either case
    /// would show the user an empty library and then save new files somewhere they never asked for. Deciding
    /// a folder is gone, and what to do about it, belongs to the caller with the user in front of it.
    /// </summary>
    [Test]
    public void A_stored_folder_that_no_longer_exists_is_returned_as_stored()
    {
        var unmounted = Path.Combine(_folder, "not", "mounted", "yet");
        LibrarySettings.Save(_settingsPath, unmounted);

        Assert.That(Directory.Exists(unmounted), Is.False, "the point of the test is that it is not there");
        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(unmounted));
    }

    [Test]
    public void Saving_creates_the_folder_the_settings_file_lives_in()
    {
        // First run: nothing under application data exists yet, so a save that assumed the directory was
        // there would fail on the very first thing a new user does.
        var nested = Path.Combine(_folder, "Integra7AuralAlchemist", "settings.json");

        LibrarySettings.Save(nested, _folder);

        Assert.That(LibrarySettings.Load(nested), Is.EqualTo(_folder));
    }

    [Test]
    public void Saving_again_replaces_the_stored_folder()
    {
        LibrarySettings.Save(_settingsPath, Path.Combine(_folder, "first"));
        LibrarySettings.Save(_settingsPath, Path.Combine(_folder, "second"));

        Assert.That(LibrarySettings.Load(_settingsPath), Is.EqualTo(Path.Combine(_folder, "second")));
    }

    /// <summary>The write is atomic -- temp file, then rename over the target -- so this pins the half of that
    /// which has no other witness: when the rename fails, the temp file does not survive. Every later save
    /// writes the same temp name, so one left behind is a small mess that never cleans itself up.</summary>
    [Test]
    public void A_failed_save_reports_it_and_leaves_no_temporary_file_behind()
    {
        // A directory where the settings file should be. Writing the temp file succeeds; renaming it over a
        // directory cannot.
        Directory.CreateDirectory(_settingsPath);

        // Not a specific exception type: what Windows raises for a rename over a directory is
        // UnauthorizedAccessException rather than the IOException the operation reads like, and pinning
        // either would be pinning the platform. The contract is only that the failure is reported at all,
        // because the user just chose a folder and a save that quietly did nothing would forget it silently.
        Assert.That(() => LibrarySettings.Save(_settingsPath, _folder), Throws.Exception);
        Assert.That(File.Exists(_settingsPath + ".tmp"), Is.False);
    }

    [Test]
    public void The_default_folder_is_the_users_own_documents_and_not_application_data()
    {
        // Under Documents because these are the user's own files -- snapshots they will want to find, copy
        // and back up -- while application data is where an application's private state goes.
        Assert.That(LibrarySettings.DefaultFolder, Is.EqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Integra7AuralAlchemist", "Library")));
    }

    [Test]
    public void The_real_settings_path_is_under_application_data()
    {
        // The opposite case: this one *is* the application's private state, and it is named here rather than
        // rebuilt by each caller so that there is one place it can be got wrong.
        Assert.That(LibrarySettings.SettingsPath, Is.EqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Integra7AuralAlchemist", "settings.json")));
    }

    /// <summary>The init-tone marks are the second thing in this file, and they arrived after it
    /// shipped -- so the case that matters most is a settings file written by a build that had never
    /// heard of them.</summary>
    [Test]
    public void A_settings_file_from_before_init_tones_still_loads()
    {
        File.WriteAllText(_settingsPath, """{ "LibraryFolder": "C:\\Sounds" }""");

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.Folder, Is.EqualTo(@"C:\Sounds"));
        Assert.That(preferences.InitTones, Is.Empty);
    }

    [Test]
    public void A_mark_round_trips()
    {
        LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(@"C:\Sounds",
            new Dictionary<string, string> { ["SN-S"] = "My Init Pad.json" }));

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.InitTones["SN-S"], Is.EqualTo("My Init Pad.json"));
    }

    /// <summary>Changing the library folder goes through the one-argument Save, which predates the
    /// marks. If it wrote the whole file from its single argument it would silently forget them.</summary>
    [Test]
    public void Changing_the_folder_keeps_the_marks()
    {
        LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(@"C:\Sounds",
            new Dictionary<string, string> { ["PCMS"] = "Init.json" }));

        LibrarySettings.Save(_settingsPath, @"D:\Other");

        var preferences = LibrarySettings.LoadAll(_settingsPath);
        Assert.That(preferences.Folder, Is.EqualTo(@"D:\Other"));
        Assert.That(preferences.InitTones["PCMS"], Is.EqualTo("Init.json"));
    }

    [Test]
    public void An_unreadable_settings_file_yields_the_default_folder_and_no_marks()
    {
        File.WriteAllText(_settingsPath, "this is not JSON");

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.Folder, Is.EqualTo(LibrarySettings.DefaultFolder));
        Assert.That(preferences.InitTones, Is.Empty);
    }
}
