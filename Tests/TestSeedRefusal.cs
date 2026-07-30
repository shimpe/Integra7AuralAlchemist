using System;
using System.IO;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Why a sweep may not start, and -- the part that is actually a decision -- which reason a user
/// reads when more than one of them is true at once. Each sentence is also checked for the half that says
/// what to do about it: a refusal that only names the problem leaves the user pressing the same button
/// again.</summary>
public class SeedRefusalTests
{
    private string _folder = "";

    [SetUp]
    public void CreateTempFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "Integra7AuralAlchemist.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveTempFolder()
    {
        // A deletion that fails must not fail a test that actually passed -- PatchHistoryTests' pattern.
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"Could not remove {_folder}: {e.Message}");
        }
    }

    // ---- the three reasons, one at a time -------------------------------------------------------------

    [Test]
    public void A_sweep_with_a_device_and_a_writable_folder_and_no_comparison_may_start()
    {
        Assert.That(SeedRefusal.Reason(comparing: false, haveInstrument: true, folderTrouble: null),
            Is.Null);
    }

    /// <summary>The one that loses work. While comparing, the journal's buffer is the only copy of the
    /// user's edits and the sweep is about to overwrite the part they belong to, once per patch.</summary>
    [Test]
    public void A_comparison_in_progress_refuses_the_sweep_and_says_how_to_end_it()
    {
        var reason = SeedRefusal.Reason(comparing: true, haveInstrument: true, folderTrouble: null);

        Assert.That(reason, Is.Not.Null);
        Assert.That(reason, Does.Contain("Press Compare again"),
            "a refusal that only names the problem leaves the user pressing Start again");
    }

    [Test]
    public void No_connection_refuses_the_sweep_and_says_to_connect()
    {
        var reason = SeedRefusal.Reason(comparing: false, haveInstrument: false, folderTrouble: null);

        Assert.That(reason, Is.Not.Null);
        Assert.That(reason, Does.Contain("Connect your Integra-7"));
    }

    [Test]
    public void A_folder_that_will_not_take_a_file_refuses_the_sweep_and_says_to_choose_another()
    {
        var reason = SeedRefusal.Reason(comparing: false, haveInstrument: true,
            folderTrouble: "the share is read-only");

        Assert.That(reason, Is.Not.Null);
        Assert.That(reason, Does.Contain("Change…"), "and names the button that fixes it");
    }

    /// <summary>The file system's own words survive into the sentence. Without them the user is told their
    /// library folder is unwritable and left to guess between a full disk, a share that has gone away and a
    /// permission somebody changed -- three problems with three different remedies.</summary>
    [Test]
    public void The_folders_own_complaint_is_carried_into_the_refusal()
    {
        Assert.That(SeedRefusal.Reason(false, true, "Access to the path 'X' is denied."),
            Does.Contain("Access to the path 'X' is denied."));
    }

    // ---- and which of them is said when several apply --------------------------------------------------

    /// <summary>Compare outranks a missing instrument. Both are true of a user who unplugged their
    /// Integra-7 while comparing, and only one of them describes something that can still be lost: the cable
    /// will still be out in five minutes, whereas the journal is one preset change away from empty.</summary>
    [Test]
    public void Compare_is_said_before_a_missing_instrument()
    {
        Assert.That(SeedRefusal.Reason(comparing: true, haveInstrument: false, folderTrouble: null),
            Does.Contain("Press Compare again"));
    }

    [Test]
    public void Compare_is_said_before_an_unwritable_folder()
    {
        Assert.That(SeedRefusal.Reason(comparing: true, haveInstrument: true, folderTrouble: "no room"),
            Does.Contain("Press Compare again"));
    }

    /// <summary>All three at once still answers the one that is time-critical.</summary>
    [Test]
    public void Compare_is_said_before_both_of_the_others()
    {
        Assert.That(SeedRefusal.Reason(comparing: true, haveInstrument: false, folderTrouble: "no room"),
            Does.Contain("Press Compare again"));
    }

    /// <summary>And with the comparison out of the way, the source before the destination: a folder is only
    /// a problem because there would be captures to put in it.</summary>
    [Test]
    public void A_missing_instrument_is_said_before_an_unwritable_folder()
    {
        var reason = SeedRefusal.Reason(comparing: false, haveInstrument: false, folderTrouble: "no room");

        Assert.That(reason, Does.Contain("Connect your Integra-7"));
        Assert.That(reason, Does.Not.Contain("no room"),
            "one reason, not a list: three complaints about one button read as a broken feature");
    }

    // ---- asking the folder itself ----------------------------------------------------------------------

    [Test]
    public void A_folder_that_takes_a_file_has_nothing_to_say_about_itself()
    {
        Assert.That(SeedRefusal.FolderTrouble(_folder), Is.Null);
    }

    /// <summary>And it is asked by writing, so the probe has to go again. A library folder that filled up
    /// with a file per press would be this check costing the user more than it saved them, and the browser
    /// would list a growing pile of things that are not snapshots.</summary>
    [Test]
    public void Asking_leaves_nothing_behind_in_the_folder()
    {
        SeedRefusal.FolderTrouble(_folder);

        Assert.That(Directory.GetFileSystemEntries(_folder), Is.Empty);
    }

    /// <summary>A library folder that does not exist yet is the normal state of a fresh install, not a
    /// refusal -- <c>SnapshotLibrary.Create</c> creates it too, so refusing here would be this check
    /// stopping the one sweep that has nothing to resume.</summary>
    [Test]
    public void A_folder_that_is_not_there_yet_is_created_rather_than_refused()
    {
        var fresh = Path.Combine(_folder, "not made yet");

        Assert.That(SeedRefusal.FolderTrouble(fresh), Is.Null);
        Assert.That(Directory.Exists(fresh), Is.True);
    }

    /// <summary>A path that is really a file exists perfectly well and takes nothing, which is exactly the
    /// case <c>Directory.Exists</c> would wave through.</summary>
    [Test]
    public void A_folder_that_is_really_a_file_says_so()
    {
        var file = Path.Combine(_folder, "not a folder.json");
        File.WriteAllText(file, "{}");

        Assert.That(SeedRefusal.FolderTrouble(file), Is.Not.Null);
    }
}
