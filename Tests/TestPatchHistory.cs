using System;
using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Keeping the previous copy of a library file. Every rule here exists because the alternative is
/// a user losing a sound they have no other copy of, so each is pinned rather than left to the reading of
/// the implementation.</summary>
public class PatchHistoryTests
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
        // A deletion that fails must not fail a test that actually passed -- the same reasoning as
        // LibrarySettingsTests, whose pattern this is.
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"Could not remove {_folder}: {e.Message}");
        }
    }

    /// <summary>Writes a file and stamps it, so that a version's name can be asserted exactly rather than
    /// against whatever the clock said.</summary>
    private string WriteFile(string name, string content, DateTime written)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, written);
        return path;
    }

    private string HistoryFolder(string stem) =>
        Path.Combine(_folder, PatchHistory.FolderName, stem);

    [Test]
    public void Archiving_a_file_that_is_not_there_does_nothing()
    {
        PatchHistory.Archive(Path.Combine(_folder, "Nothing.json"));

        Assert.That(Directory.Exists(Path.Combine(_folder, PatchHistory.FolderName)), Is.False,
            "and does not create the history folder either: Create writes a file that did not exist, and "
            + "every new snapshot must not leave an empty folder behind");
    }

    /// <summary>A version is named after the file's <em>own</em> last-write time, not the moment it was
    /// archived, so the name says when the content was written.</summary>
    [Test]
    public void A_version_is_named_after_the_time_the_content_was_written()
    {
        var path = WriteFile("Warm Pad.json", "{}", new DateTime(2026, 7, 28, 7, 25, 13));

        PatchHistory.Archive(path);

        var versions = Directory.GetFiles(HistoryFolder("Warm Pad"));
        Assert.That(versions.Select(Path.GetFileName), Is.EqualTo(new[] { "20260728T072513.json" }));
    }

    [Test]
    public void An_archived_version_holds_what_the_file_held()
    {
        var path = WriteFile("Warm Pad.json", "the original", new DateTime(2026, 7, 28, 7, 25, 13));

        PatchHistory.Archive(path);
        File.WriteAllText(path, "overwritten");

        var version = Directory.GetFiles(HistoryFolder("Warm Pad")).Single();
        Assert.That(File.ReadAllText(version), Is.EqualTo("the original"));
    }

    /// <summary>Two writes inside one second are ordinary -- a bulk retag does fourteen of them -- and the
    /// second must not silently replace the first version.</summary>
    [Test]
    public void Two_versions_written_in_the_same_second_are_both_kept()
    {
        var stamp = new DateTime(2026, 7, 28, 7, 25, 13);
        var path = WriteFile("Warm Pad.json", "first", stamp);
        PatchHistory.Archive(path);

        File.WriteAllText(path, "second");
        File.SetLastWriteTime(path, stamp);
        PatchHistory.Archive(path);

        // Ordinal, not Order(): the default comparer is culture-sensitive, and '-' against '.' is exactly
        // the sort of comparison that does not answer the same way in every culture.
        var versions = Directory.GetFiles(HistoryFolder("Warm Pad"))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.That(versions, Is.EqualTo(new[] { "20260728T072513-2.json", "20260728T072513.json" }));
    }

    [Test]
    public void Only_the_newest_versions_are_kept()
    {
        var path = WriteFile("Warm Pad.json", "v0", new DateTime(2026, 7, 1, 0, 0, 0));

        // One more than the limit, each a minute apart, so the oldest is the one that must go.
        for (var i = 1; i <= PatchHistory.Keep + 1; i++)
        {
            File.WriteAllText(path, $"v{i}");
            File.SetLastWriteTime(path, new DateTime(2026, 7, 1, 0, i, 0));
            PatchHistory.Archive(path);
        }

        var kept = Directory.GetFiles(HistoryFolder("Warm Pad"))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.That(kept, Has.Count.EqualTo(PatchHistory.Keep));
        Assert.That(kept.First(), Is.EqualTo("20260701T000200.json"), "the oldest was pruned");
    }

    [Test]
    public void Versions_are_listed_newest_first()
    {
        var path = WriteFile("Warm Pad.json", "old", new DateTime(2026, 7, 1, 9, 0, 0));
        PatchHistory.Archive(path);
        File.WriteAllText(path, "new");
        File.SetLastWriteTime(path, new DateTime(2026, 7, 2, 9, 0, 0));
        PatchHistory.Archive(path);

        var versions = PatchHistory.Versions(path);

        Assert.That(versions.Select(v => v.Written), Is.EqualTo(new[]
        {
            new DateTime(2026, 7, 2, 9, 0, 0),
            new DateTime(2026, 7, 1, 9, 0, 0),
        }));
    }

    [Test]
    public void A_file_with_no_history_lists_no_versions()
    {
        var path = WriteFile("Warm Pad.json", "{}", new DateTime(2026, 7, 1, 9, 0, 0));

        Assert.That(PatchHistory.Versions(path), Is.Empty);
    }

    /// <summary>Restoring is itself undoable: what was there when Restore was pressed becomes a version in
    /// its turn. Without this, putting back the wrong version would be the one unrecoverable act in a
    /// feature whose whole purpose is recovery.</summary>
    [Test]
    public void Restoring_puts_the_content_back_and_archives_what_was_there()
    {
        var path = WriteFile("Warm Pad.json", "original", new DateTime(2026, 7, 1, 9, 0, 0));
        PatchHistory.Archive(path);
        File.WriteAllText(path, "current");
        File.SetLastWriteTime(path, new DateTime(2026, 7, 2, 9, 0, 0));

        var version = PatchHistory.Versions(path).Single();
        PatchHistory.Restore(path, version.FilePath);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
            Assert.That(PatchHistory.Versions(path).Select(v => v.Written),
                Does.Contain(new DateTime(2026, 7, 2, 9, 0, 0)), "what was replaced is now a version");
        });
    }

    /// <summary>A file in the history folder that this did not write -- a stray, or something a user
    /// dropped there -- is ignored rather than listed with a meaningless date.</summary>
    [Test]
    public void A_file_whose_name_is_not_a_timestamp_is_not_a_version()
    {
        var path = WriteFile("Warm Pad.json", "{}", new DateTime(2026, 7, 1, 9, 0, 0));
        PatchHistory.Archive(path);
        File.WriteAllText(Path.Combine(HistoryFolder("Warm Pad"), "notes.json"), "{}");

        Assert.That(PatchHistory.Versions(path), Has.Count.EqualTo(1));
    }
}
