# Library version history — implementation plan (phase 1 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** every write and every delete in the library keeps a copy of what was there, and the user can put
one back.

**Architecture:** one pure service, `PatchHistory`, that copies a file into `<library>/.history/<stem>/`
before anything overwrites or deletes it. `SnapshotLibrary.Write` and `SnapshotLibrary.Delete` call it, so
there is no second write path to remember. The library's metadata editor moves into a view model and a view
of its own, and gains the version list.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI 24, NUnit 4.

**Spec:** `docs/superpowers/specs/2026-07-29-library-overhaul-design.md`. Read the "Phase 1" section and
the "Architecture" section before starting.

**Phase 1 of five.** Phases 2–5 (bulk operations, audition, duplicates and deep search, DAW export) are
separate plans and are not this one's business. Do not build ahead of this plan.

---

## Conventions for every task

**Build and test with the user-local SDK** — the system `dotnet` is 8/9 and too old. `Src/bin` is routinely
locked by the user's own running application or Rider's previewer; **never kill either**, redirect instead.
The four-deep path and the junction are both load-bearing, because several tests find
`Src\Assets\parameters.bin` by walking `..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **940 passed, 0 failed**.

**Traps this project has actually hit**, all of which apply here:

- **An XML comment may not contain `--`**, in `.csproj` or `.axaml`. MSBuild then fails to *load* the
  project, nothing compiles, and a naive error count reads as zero. Check for `MSB4025` before believing a
  sudden green.
- **Never hardcode a colour in XAML.** Use `{StaticResource ...}`.
- **Do not edit `.csproj` or `.axaml` with `sed`** — they are CRLF with a BOM and sed rewrites every line,
  turning a four-line diff into a whole-file merge conflict. Use the Edit tool.
- Compiled bindings are checked at build time; a wrong member name is `AVLN2000`.
- **PowerShell 5.1's `Get-Content -Raw` / `Set-Content` default to ANSI** and will corrupt UTF-8 source.
  Use the Edit tool, not shell rewriting.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/library-overhaul`, which already holds the spec. Explicit paths only; never
`git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do not merge or push.

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `Src/Models/Services/PatchHistory.cs` | Archive before a write or a delete; list and restore versions |
| Modify `Src/Models/Services/SnapshotLibrary.cs` | Call the archive from `Write` and `Delete` |
| Create `Src/ViewModels/LibraryEditorViewModel.cs` | The metadata panel: its fields, its commands, its versions |
| Create `Src/Views/LibraryEditorView.axaml` (+ `.axaml.cs`) | The panel's markup, moved verbatim |
| Modify `Src/ViewModels/LibraryViewModel.cs` | The editor half moves out; it owns an `Editor` and feeds it |
| Modify `Src/Views/LibraryView.axaml` | The panel becomes one line |

**New tests:** `Tests/TestPatchHistory.cs`, plus additions to `Tests/TestSnapshotLibrary.cs`.

**A refinement on the spec.** The spec wrote `Archive(libraryFolder, filePath)`. The folder is always
`Path.GetDirectoryName(filePath)`, so the parameter is dropped — it is one more thing two callers could
disagree about, for no gain. The spec's `Versions` and `Restore` lose it for the same reason.

**An omission in the spec.** Its file table names `LibraryEditorViewModel.cs` but no view. Extracting the
markup into `LibraryEditorView.axaml` is what makes the refactor safe: the panel's bindings move verbatim
and **not one binding path changes**, because the new view's `DataContext` is the editor view model. This is
the pattern `LibraryView.axaml` already uses at line 229 for `RatingView`.

---

### Task 1: `PatchHistory`

**Files:** Create `Src/Models/Services/PatchHistory.cs`; Test `Tests/TestPatchHistory.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestPatchHistory.cs`:

```csharp
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
```

- [ ] **Step 2: Run and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter PatchHistoryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error `CS0103` — `PatchHistory` does not exist.

- [ ] **Step 3: Implement**

Create `Src/Models/Services/PatchHistory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One kept copy of a library file, and when its content was written.</summary>
public sealed record PatchVersion(string FilePath, DateTime Written);

/// <summary>The previous copies of a library file.
///
/// <b>What this is for.</b> Annotating a snapshot rewrites the file that holds the sound -- see
/// <see cref="SnapshotLibrary.WriteMetadata"/>, which re-reads all ~1,500 parameter values and writes them
/// back. That is the operation with the most to lose if it ever goes wrong, and it is also the one a user
/// performs most often. Deleting is worse: <see cref="SnapshotLibrary.Delete"/> does not use the recycle
/// bin, because .NET has no cross-platform API for one.
///
/// <b>Where they go.</b> A <c>.history</c> folder beside the library, one sub-folder per patch. It stays
/// out of the listing without being asked to: <see cref="SnapshotLibrary.Read"/> enumerates
/// <see cref="SearchOption.TopDirectoryOnly"/>, so a sub-folder is already invisible to it -- the test
/// <c>Sub_folders_are_not_enumerated</c> is what holds that true.
///
/// <b>The folder is not a parameter.</b> It is always the file's own directory, so passing it in would be
/// one more thing two callers could come to disagree about.
///
/// <b>A version is named after the file's own last-write time</b>, not the moment of archiving, so the name
/// says when that content was written rather than when it was displaced. The format sorts
/// lexicographically, which is what lets pruning and listing work on names alone.</summary>
public static class PatchHistory
{
    /// <summary>How many versions of one patch are kept. Ten is a working session's worth of saves and
    /// costs, for a tone, well under a megabyte -- a drum kit is 633 KB, which is the case worth
    /// remembering before raising this.</summary>
    public const int Keep = 10;

    /// <summary>Leading dot, which hides it on Unix and is inert on Windows. Named here rather than
    /// written into three methods.</summary>
    public const string FolderName = ".history";

    /// <summary>Sortable, second-resolution, no separators a file name cannot hold. Invariant, so a
    /// library written on one machine lists correctly on another.</summary>
    private const string Stamp = "yyyyMMddTHHmmss";

    private static string FolderFor(string filePath) =>
        Path.Combine(Path.GetDirectoryName(filePath) ?? "", FolderName,
            Path.GetFileNameWithoutExtension(filePath));

    /// <summary>Keep a copy of <paramref name="filePath"/> as it is now, then prune to <see cref="Keep"/>.
    ///
    /// <b>A file that is not there is not an error.</b> That is what creating a new snapshot looks like, so
    /// this is a no-op for it -- and it must not leave an empty history folder behind for every new patch.
    ///
    /// Everything else throws, and the caller refuses whatever it was about to do. See
    /// <see cref="SnapshotLibrary.Write"/> for why that is the right way round.</summary>
    public static void Archive(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var folder = FolderFor(filePath);
        Directory.CreateDirectory(folder);

        var stamp = File.GetLastWriteTime(filePath).ToString(Stamp, CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"{stamp}.json");
        // Two writes inside one second are ordinary -- a bulk retag does fourteen -- and the second must
        // not replace the first version.
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, $"{stamp}-{n}.json");

        File.Copy(filePath, target);
        Prune(folder);
    }

    /// <summary>The versions of <paramref name="filePath"/>, newest first. Empty when there are none, which
    /// is every patch until the first time it is written over.</summary>
    public static IReadOnlyList<PatchVersion> Versions(string filePath)
    {
        var folder = FolderFor(filePath);
        if (!Directory.Exists(folder)) return [];

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(path => (path, written: WrittenAt(path)))
            // A file this did not write -- a stray, or something dropped in by hand -- is passed over
            // rather than listed with a date that means nothing.
            .Where(v => v.written is not null)
            .OrderByDescending(v => v.written!.Value)
            .Select(v => new PatchVersion(v.path, v.written!.Value))];
    }

    /// <summary>Put <paramref name="versionPath"/> back at <paramref name="filePath"/>.
    ///
    /// <b>What is there now becomes a version in its turn</b>, so restoring the wrong one is not the single
    /// unrecoverable act in a feature built for recovery. Written through a temporary file and a rename for
    /// the reason <see cref="SnapshotLibrary"/> writes that way: a failure partway through must not leave
    /// the patch half replaced.</summary>
    public static void Restore(string filePath, string versionPath)
    {
        Archive(filePath);

        var temp = filePath + ".restoring";
        try
        {
            File.Copy(versionPath, temp, overwrite: true);
            File.Move(temp, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanup)
            {
                Serilog.Log.Warning(cleanup, "Could not remove the temporary file {Path}", temp);
            }

            throw;
        }
    }

    /// <summary>The time in a version's file name, or null when the name is not one this wrote. Read from
    /// the name rather than from the file, because a copy carries the copy's timestamp.</summary>
    private static DateTime? WrittenAt(string versionPath)
    {
        var name = Path.GetFileNameWithoutExtension(versionPath);
        // A same-second collision appends "-2"; the stamp itself is fixed width and holds no hyphen.
        var hyphen = name.IndexOf('-');
        if (hyphen >= 0) name = name[..hyphen];

        return DateTime.TryParseExact(name, Stamp, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var written) ? written : null;
    }

    /// <summary>Keep the newest <see cref="Keep"/> and delete the rest. Ordered by name, which the stamp
    /// format makes the same as ordering by time.</summary>
    private static void Prune(string folder)
    {
        var stale = Directory.EnumerateFiles(folder, "*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(Keep)
            .ToList();

        foreach (var path in stale) File.Delete(path);
    }
}
```

- [ ] **Step 4: Run the tests, then the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter PatchHistoryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: 9 pass in the filter, and 949 pass overall (940 + 9), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/PatchHistory.cs Tests/TestPatchHistory.cs
git commit -m "feat: keep the previous copies of a library file"
```

---

### Task 2: archive on every write and every delete

**Files:** Modify `Src/Models/Services/SnapshotLibrary.cs`; Test `Tests/TestSnapshotLibrary.cs`

- [ ] **Step 1: Write the failing tests**

Append these to the existing `SnapshotLibraryTests` class in `Tests/TestSnapshotLibrary.cs`. The fixture
methods `Tone(name)`, `_folder` and the temp-folder setup are already in that file — use them, do not
redefine them. Confirm their exact names by reading the top of the file first.

```csharp
    /// <summary>Annotating rewrites the file that holds the sound, so the copy taken before it is the thing
    /// standing between a bug in that path and a patch the user cannot get back.</summary>
    [Test]
    public void Annotating_a_snapshot_keeps_the_copy_it_replaced()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Pad"), new SnapshotMetadata());

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "brighter"));

        var versions = PatchHistory.Versions(path);
        Assert.That(versions, Has.Count.EqualTo(1));
        Assert.That(Integra7Snapshot.FromJson(File.ReadAllText(versions[0].FilePath)).Notes, Is.Empty,
            "the version holds what the file said before, not after");
    }

    /// <summary>Creating a snapshot writes a file that was not there, so there is nothing to keep -- and no
    /// empty history folder should be left behind for every new patch either.</summary>
    [Test]
    public void Creating_a_snapshot_archives_nothing()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Pad"), new SnapshotMetadata());

        Assert.That(PatchHistory.Versions(path), Is.Empty);
        Assert.That(Directory.Exists(Path.Combine(_folder, PatchHistory.FolderName)), Is.False);
    }

    /// <summary>Delete does not use the recycle bin -- .NET has no cross-platform API for one -- so this
    /// copy is the only way back from a deletion, and from the bulk delete that phase 2 adds.</summary>
    [Test]
    public void Deleting_a_snapshot_keeps_a_copy_of_it()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Pad"), new SnapshotMetadata());

        SnapshotLibrary.Delete(path);

        Assert.That(File.Exists(path), Is.False);
        Assert.That(PatchHistory.Versions(path), Has.Count.EqualTo(1),
            "and the version is still findable by the path the file used to have");
    }

    /// <summary>The rule that makes the rest of it trustworthy. Write is atomic -- a temporary file and
    /// then a rename -- so continuing after a failed archive would destroy the previous version at the
    /// exact moment it has been established that no copy can be kept.</summary>
    [Test]
    public void A_write_whose_archive_fails_is_refused_and_leaves_the_file_alone()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Pad"), new SnapshotMetadata());
        var before = File.ReadAllText(path);

        // A file where the history folder needs to be: creating the directory then fails, which is the
        // portable way to make archiving fail on every platform this builds for.
        var history = Path.Combine(_folder, PatchHistory.FolderName);
        Directory.CreateDirectory(history);
        File.WriteAllText(Path.Combine(history, "Warm Pad"), "in the way");

        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "brighter")),
            Throws.InstanceOf<IOException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo(before), "and the file is exactly as it was");
    }

    /// <summary>The history folder must not appear in the library. This is already true because Read is
    /// TopDirectoryOnly, and it is pinned here as well because it is now load-bearing for a second
    /// reason.</summary>
    [Test]
    public void The_history_folder_is_not_listed_as_a_snapshot()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Pad"), new SnapshotMetadata());
        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "brighter"));

        Assert.That(SnapshotLibrary.Read(_folder), Has.Count.EqualTo(1));
    }
```

- [ ] **Step 2: Run and watch them fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter SnapshotLibraryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: the four new archive tests fail (no versions are kept); `The_history_folder_is_not_listed_as_a_snapshot` passes already.

- [ ] **Step 3: Implement**

In `Src/Models/Services/SnapshotLibrary.cs`, add the archive call as the **first** statement of the private
`Write` method, before the JSON is serialised:

```csharp
    private static void Write(string filePath, Integra7Snapshot snapshot)
    {
        // Before anything else, and allowed to throw: this method replaces the file by renaming over it, so
        // proceeding after a failed archive would destroy the previous version at the exact moment it has
        // been established that no copy can be kept. A no-op when the file does not exist, which is what
        // Create looks like.
        PatchHistory.Archive(filePath);

        var json = Integra7Snapshot.ToJson(snapshot);
        // ... the rest of the method is unchanged
```

And in `Delete`, immediately before `File.Delete(filePath)`:

```csharp
        // The only way back: this does not use the recycle bin, because .NET has no cross-platform API for
        // one. Allowed to throw, so a deletion that cannot be undone does not happen.
        PatchHistory.Archive(filePath);
        File.Delete(filePath);
```

Then extend the class's own remarks. Find the paragraph beginning "**A stray file is skipped, not
reported.**" and add this paragraph after it:

```csharp
/// <b>Every write and every delete keeps the copy it replaced</b> -- see <see cref="PatchHistory"/>. The
/// archive is taken before the change and is allowed to throw, which refuses the change: this class replaces
/// a file by renaming over it, so a write that continued past a failed archive would destroy the only copy.
```

- [ ] **Step 4: Correct two sentences that this task makes untrue**

`LibraryViewModel.DeleteSelectedAsync` tells the user the deletion **cannot be undone**. After this task it
can. Leaving it would be worse than never having written it: the user would delete cautiously for a reason
that no longer holds, and would not look for the copy that exists.

In `Src/ViewModels/LibraryViewModel.cs`, change the confirmation text from:

```csharp
        if (!await _confirm($"Delete \"{selected.Name}\" from the library? " +
                            $"The file {Path.GetFileName(selected.FilePath)} is removed for good — " +
                            "this cannot be undone.")) return;
```

to:

```csharp
        if (!await _confirm($"Delete \"{selected.Name}\" from the library? " +
                            $"The file {Path.GetFileName(selected.FilePath)} is removed, but a copy is " +
                            "kept in the history folder beside your library.")) return;
```

Note the em dash in the original is a real character, not `--`; keep prose that way.

And in the same method's doc comment, replace:

```csharp
    /// <summary>Remove the selected snapshot from the library, after asking. The file goes for good --
    /// see <c>SnapshotLibrary.Delete</c> -- so this is the one place in the library that asks before acting.
```

with:

```csharp
    /// <summary>Remove the selected snapshot from the library, after asking. It still asks, even though
    /// <see cref="PatchHistory"/> now keeps a copy: the row leaves the library, the mark on it is cleared,
    /// and getting it back means knowing the history folder exists.
```

- [ ] **Step 5: Run the tests, then the whole suite**

Expected: `SnapshotLibraryTests` all pass, and the suite is at 954 (949 + 5), 0 failed. **Every
pre-existing test in that file must still pass** — particularly
`A_deleted_snapshot_is_gone_from_the_folder_and_the_listing`,
`Deleting_a_file_that_is_already_gone_is_not_an_error` and `A_deletion_that_cannot_happen_reports_it`,
which the archive call sits directly in front of.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/SnapshotLibrary.cs Src/ViewModels/LibraryViewModel.cs Tests/TestSnapshotLibrary.cs
git commit -m "feat: keep a copy before the library overwrites or deletes a snapshot"
```

---

### Task 3: move the metadata editor into its own view model and view

**Files:** Create `Src/ViewModels/LibraryEditorViewModel.cs`, `Src/Views/LibraryEditorView.axaml` (+
`.axaml.cs`); Modify `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`

**No behaviour changes in this task.** It is a move. The version list arrives in task 4. There are no tests,
because a view model cannot be constructed in one — `WhenAnyValue` throws `InvalidOperationException`
demanding `RxAppBuilder`'s `.BuildApp()`. Verification is that the solution builds and the application still
annotates a snapshot.

- [ ] **Step 1: Read what is being moved**

Read `Src/ViewModels/LibraryViewModel.cs` in full and `Src/Views/LibraryView.axaml` lines 190–295. The
editor is:

- fields `_editName`, `_editCategoryLabel`, `_editTags`, `_editNotes`, `_editFavourite`, and `EditRating`
- `EditCategoryLabels`, `HasSelection`, `SelectedIsTone`, `CanMarkAsInitTone`, `InitToneNote`,
  `CanSaveChanges`
- `ShowSelected()`, `SaveChanges()`, `MarkAsInitTone()`, `LoadSelectedAsync()`, `CompareThisAsync()`,
  `DeleteSelectedAsync()`

- [ ] **Step 2: Create the editor view model**

Create `Src/ViewModels/LibraryEditorViewModel.cs`. It receives the selected row and the services it needs as
callbacks, which is the pattern `LibraryViewModel` itself already uses for the same reason — a view model
inside a panel has no window and no folder of its own to reach for.

```csharp
using System;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The panel beside the library list: what the selected snapshot says about itself, the four
/// things that can be done to it, and its versions.
///
/// <b>Split out of <see cref="LibraryViewModel"/></b>, which had grown to the size where an edit is harder
/// to make correctly than it should be, and which four of the five library phases have to touch. The seam
/// is the one already on screen: the list on the left, this on the right.
///
/// <b>It holds no file and opens none.</b> Every write goes out through the callbacks, all of which end at
/// <see cref="SnapshotLibrary"/>, so this cannot rewrite a parameter value -- it never holds one.</summary>
public sealed partial class LibraryEditorViewModel : ViewModelBase
{
    private readonly Func<LibraryEntryViewModel, SnapshotMetadata, Task> _save;
    private readonly Func<LibraryEntryViewModel, Task> _load;
    private readonly Func<LibraryEntryViewModel, Task> _compare;
    private readonly Func<LibraryEntryViewModel, Task> _delete;
    private readonly Action<LibraryEntryViewModel> _markAsInitTone;

    /// <param name="save">Write the edited metadata back. Takes the row as well as the metadata so that
    /// the caller, which owns the folder and the refresh, does not have to ask what is selected.</param>
    /// <param name="load">Send this snapshot to the instrument.</param>
    /// <param name="compare">Hand this snapshot to the Compare tab.</param>
    /// <param name="delete">Remove it from the library, after asking.</param>
    /// <param name="markAsInitTone">Make it the tone Init starts from for its engine.</param>
    public LibraryEditorViewModel(
        Func<LibraryEntryViewModel, SnapshotMetadata, Task> save,
        Func<LibraryEntryViewModel, Task> load,
        Func<LibraryEntryViewModel, Task> compare,
        Func<LibraryEntryViewModel, Task> delete,
        Action<LibraryEntryViewModel> markAsInitTone)
    {
        _save = save;
        _load = load;
        _compare = compare;
        _delete = delete;
        _markAsInitTone = markAsInitTone;

        // The four flags the buttons bind to are not raised by the generated setters of the properties they
        // read, so they are raised together whenever either input changes.
        this.WhenAnyValue(x => x.Selected, x => x.EditName, (_, _) => System.Reactive.Unit.Default)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(SelectedIsTone));
                this.RaisePropertyChanged(nameof(CanSaveChanges));
                this.RaisePropertyChanged(nameof(CanMarkAsInitTone));
                this.RaisePropertyChanged(nameof(InitToneNote));
            });

        this.WhenAnyValue(x => x.Selected).Subscribe(_ => ShowSelected());
    }

    /// <summary>Which row the panel is describing, or null. Assigned by the list.</summary>
    [Reactive] private LibraryEntryViewModel? _selected;

    [Reactive] private string _editName = "";
    [Reactive] private string _editCategoryLabel = LibraryListing.NoCategory;
    [Reactive] private string _editTags = "";
    [Reactive] private string _editNotes = "";
    [Reactive] private bool _editFavourite;

    /// <summary>The stars. A type of its own because the save dialog wants the same five -- see
    /// <see cref="RatingViewModel"/>.</summary>
    public RatingViewModel EditRating { get; } = new();

    public IReadOnlyList<string> EditCategoryLabels => LibraryListing.EditCategoryLabels;

    public bool HasSelection => Selected is not null;

    /// <summary>Whether the selected entry is a tone, which is the only thing that has a category. A Studio
    /// Set is sixteen parts each with one of their own, so the drop-down is disabled rather than hidden for
    /// one: the row still shows what the file says, which matters for a hand-edited file that has a
    /// category it should not.</summary>
    public bool SelectedIsTone => Selected?.Entry.Head.Kind == SnapshotKinds.Tone;

    /// <summary>Whether the selected entry can be made an init tone: a tone whose engine this build
    /// recognises, since the mark is stored per engine.</summary>
    public bool CanMarkAsInitTone =>
        SelectedIsTone && Selected?.Entry.Head.ToneType is { } t && ToneDomainNames.IsKnownToneType(t);

    /// <summary>What the panel says about the selected entry's init-tone status -- empty when there is
    /// nothing to say, which is most of the time. Reads the row's own mark rather than repeating the lookup
    /// the list already made: two places comparing the same file name against the same map is two places
    /// that can come to disagree.</summary>
    public string InitToneNote =>
        Selected is { IsInitTone: true, Entry.Head.ToneType: { } toneType }
            ? $"Init Tone starts from this when the part holds a {toneType} tone."
            : "";

    /// <summary>Whether Save changes can do anything. The name is the one field that cannot be cleared: an
    /// entry with no name is a row the user cannot tell from the one above it, and the file it names may be
    /// their only copy of that sound.</summary>
    public bool CanSaveChanges => HasSelection && EditName.Trim().Length > 0;

    /// <summary>Put the selected entry's metadata into the fields -- or clear them when nothing is
    /// selected. Every field, including the empty ones: a box left holding the previous selection's notes
    /// is a box whose Save would write them onto this sound.</summary>
    private void ShowSelected()
    {
        var head = Selected?.Entry.Head;
        EditName = head?.Name ?? "";
        EditCategoryLabel = LibraryListing.EditLabelForCategory(head?.Category);
        EditTags = head is null ? "" : LibraryListing.FormatTags(head.Tags);
        EditNotes = head?.Notes ?? "";
        EditRating.Value = head?.Rating ?? 0;
        EditFavourite = head?.Favourite ?? false;
    }

    public async Task SaveChanges()
    {
        UserActionLog.Action("button: Save changes (library)");
        if (Selected is not { } row || !CanSaveChanges) return;

        await _save(row, new SnapshotMetadata(
            LibraryListing.CategoryToWrite(EditCategoryLabel),
            LibraryListing.ParseTags(EditTags),
            EditNotes,
            EditRating.Value,
            EditFavourite,
            EditName.Trim()));
    }

    public async Task LoadSelectedAsync()
    {
        UserActionLog.Action("button: Load (library)");
        if (Selected is { } row) await _load(row);
    }

    public async Task CompareThisAsync()
    {
        UserActionLog.Action("button: Compare this");
        if (Selected is { } row) await _compare(row);
    }

    public async Task DeleteSelectedAsync()
    {
        if (Selected is { } row) await _delete(row);
    }

    public void MarkAsInitTone()
    {
        UserActionLog.Action("button: Use as the init tone (library)");
        if (Selected is { } row) _markAsInitTone(row);
    }

    /// <summary>Raised by the list after it has moved the init-tone marks, so the note follows the mark in
    /// the same gesture.</summary>
    public void InitToneMarksChanged() => this.RaisePropertyChanged(nameof(InitToneNote));
}
```

Add `using System.Collections.Generic;` at the top — `EditCategoryLabels` returns `IReadOnlyList<string>`.

- [ ] **Step 3: Create the editor view**

Create `Src/Views/LibraryEditorView.axaml`.

**The markup is moved, not rewritten, and that is the point of doing it this way** — every binding path
inside it already resolves against the new view model, so a verbatim move cannot introduce a binding error.
It is not reproduced in this plan because a copy here could drift from the file; move what is actually
there.

**The exact block** in `Src/Views/LibraryView.axaml`, as it stands at the start of this task:

- **First line to move:** the `<TextBlock` whose attribute is `IsVisible="{Binding !HasSelection}"` —
  currently line 198, with the comment above it if there is one.
- **Last line to move:** the closing tag of the `Button` whose `Command` is
  `{Binding DeleteSelectedAsync}` — currently around line 285 — together with the closing tags of the
  containers that hold only these controls.
- **Leave behind** the outer `Border` (or whatever carries `Grid.Column="1"`, the width and the margin) —
  those become attributes on the `<local:LibraryEditorView>` element in step 4.

**Check the move was faithful** before building:

```bash
git diff -- Src/Views/LibraryView.axaml | grep '^-' | grep -v '^---' | wc -l
git diff --cached --stat -- Src/Views/LibraryEditorView.axaml
```

The count of removed lines from `LibraryView.axaml` should match the number of markup lines that arrived in
`LibraryEditorView.axaml`, allowing for the container tags and re-indentation. A large mismatch means
something was retyped rather than moved.

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:local="clr-namespace:Integra7AuralAlchemist.Views"
             mc:Ignorable="d" d:DesignWidth="420" d:DesignHeight="800"
             x:Class="Integra7AuralAlchemist.Views.LibraryEditorView"
             x:DataType="vm:LibraryEditorViewModel">

    <!-- What the selected snapshot says about itself, and the four things that can be done to it. Moved
         out of LibraryView unchanged: the bindings are the same paths against a view model that now holds
         only these fields. No ToolTip anywhere, for the reason LibraryView gives. -->

    <!-- PASTE the moved markup here, from the <TextBlock ... IsVisible="{Binding !HasSelection}" />
         through the Delete button, keeping its outer container. -->
</UserControl>
```

Create `Src/Views/LibraryEditorView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class LibraryEditorView : UserControl
{
    public LibraryEditorView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Use it from `LibraryView.axaml`**

Replace the markup that was moved with one line, exactly as line 229 of the same file already does for the
rating control:

```xml
                <local:LibraryEditorView Grid.Column="1" DataContext="{Binding Editor}" />
```

Keep whatever `Grid.Column`, `Width` and `Margin` the moved container carried, on this element.

- [ ] **Step 5: Wire it up in `LibraryViewModel`**

Delete from `LibraryViewModel` everything listed in step 1, and add:

```csharp
    /// <summary>The panel beside the list. Built once, and told which row is selected -- see
    /// <see cref="LibraryEditorViewModel"/> for why the editor is not in this file.</summary>
    public LibraryEditorViewModel Editor { get; }
```

In the constructor, before the subscriptions, build it with the callbacks. `SaveChangesAsync`,
`LoadAsync`, `CompareAsync`, `DeleteAsync` and `MarkAsInitTone` below are the bodies that were already in
this file, with `SelectedEntry` replaced by the `row` parameter:

```csharp
        Editor = new LibraryEditorViewModel(SaveChangesAsync, LoadAsync, CompareAsync, DeleteAsync,
            MarkAsInitTone);
```

Replace the old selection subscription — the one that raised `HasSelection` and its four neighbours — with
one that feeds the editor:

```csharp
        // The panel follows the selection. The flags it raises are its own; this only tells it what to
        // describe.
        this.WhenAnyValue(x => x.SelectedEntry).Subscribe(row => Editor.Selected = row);
```

`SaveChangesAsync` keeps the body that was in `SaveChanges`, ending with `Refresh()`, and takes the
metadata from the editor rather than reading its fields:

```csharp
    private Task SaveChangesAsync(LibraryEntryViewModel row, SnapshotMetadata metadata)
    {
        try
        {
            SnapshotLibrary.WriteMetadata(row.FilePath, metadata);
            _report($"Saved the changes to {Path.GetFileName(row.FilePath)}.", false);
            Refresh();
        }
        catch (Exception e)
        {
            // Including SnapshotFormatException, whose message is written for the user, and now also an
            // IOException from PatchHistory: a file whose previous version cannot be kept is not written.
            UserActionLog.Failed($"save the metadata of '{row.FilePath}'", e.ToString());
            _report($"Could not save the changes: {e.Message}", true);
        }

        return Task.CompletedTask;
    }
```

The other four callbacks are the existing methods, taking the row rather than reading `SelectedEntry`.
`LoadAsync` and `CompareAsync` are one line each, because the parent's own callbacks already take a
`LibraryEntry`:

```csharp
    private Task LoadAsync(LibraryEntryViewModel row) => _load(row.Entry);

    private Task CompareAsync(LibraryEntryViewModel row) => _compare(row.Entry);
```

`DeleteAsync` is the body of the existing `DeleteSelectedAsync` with its first two lines changed — the
`UserActionLog` line stays here rather than moving to the editor, because this is where the deletion
happens:

```csharp
    private async Task DeleteAsync(LibraryEntryViewModel selected)
    {
        UserActionLog.Action("button: Delete from library");
        // ... the rest of the existing body, unchanged, including the confirmation reworded in task 2,
        // the try/catch around SnapshotLibrary.Delete, the init-tone mark clearing and the Refresh().
        // Delete only the two lines that read SelectedEntry:
        //     if (SelectedEntry is not { } selected) return;
    }
```

`MarkAsInitTone` is the existing body with its guard changed. Its first line read
`if (SelectedEntry?.Entry.Head.ToneType is not { } toneType) return;` and becomes:

```csharp
    private void MarkAsInitTone(LibraryEntryViewModel row)
    {
        if (row.Entry.Head.ToneType is not { } toneType) return;

        _initTones[toneType] = Path.GetFileName(row.FilePath);
        // ... the rest of the existing body, unchanged, except that the three later uses of
        // SelectedEntry become row: the two in the success message and the RaisePropertyChanged at the end,
        // which is now Editor.InitToneMarksChanged() and is already called by ApplyInitToneMarks below.
    }
```

The `UserActionLog.Action` calls for Save changes, Load, Compare this and Use as the init tone move **to**
the editor view model — they are in the code shown in step 2 — so delete them from these bodies. Delete is
the exception, above.

`ApplyInitToneMarks` gains one line at its end so the panel's note follows the marks:

```csharp
        Editor.InitToneMarksChanged();
```

- [ ] **Step 6: Build**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: `Build succeeded`, 0 errors. An `AVLN2000` means a binding in the moved markup names a member the
editor view model does not have — compare against the list in step 1. Check for `MSB4025` before believing
a sudden green.

- [ ] **Step 7: Run the whole suite**

Expected: 954 passed, 0 failed — unchanged, because nothing tested was touched.

- [ ] **Step 8: Commit**

```bash
git add Src/ViewModels/LibraryEditorViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryEditorView.axaml Src/Views/LibraryEditorView.axaml.cs Src/Views/LibraryView.axaml
git commit -m "refactor: move the library's metadata editor into its own view model and view"
```

---

### Task 4: show the versions and put one back

**Files:** Modify `Src/ViewModels/LibraryEditorViewModel.cs`, `Src/Views/LibraryEditorView.axaml`,
`Src/ViewModels/LibraryViewModel.cs`

- [ ] **Step 1: Add the versions to the editor view model**

Add to `LibraryEditorViewModel`:

```csharp
    /// <summary>The kept copies of the selected snapshot, newest first, as dates a user reads. Rebuilt when
    /// the selection changes and after a restore, because a restore adds one.</summary>
    public ObservableCollection<PatchVersionViewModel> Versions { get; } = [];

    [Reactive] private PatchVersionViewModel? _selectedVersion;

    public bool HasVersions => Versions.Count > 0;

    /// <summary>Reading the history folder is a directory listing, so it is done on the selection rather
    /// than lazily: the panel is already showing the file's own fields, and one more folder read is not
    /// what makes this screen slow.</summary>
    private void ShowVersions()
    {
        Versions.Clear();
        if (Selected is { } row)
            foreach (var version in PatchHistory.Versions(row.FilePath))
                Versions.Add(new PatchVersionViewModel(version));

        SelectedVersion = Versions.Count > 0 ? Versions[0] : null;
        this.RaisePropertyChanged(nameof(HasVersions));
    }
```

Call `ShowVersions()` at the end of `ShowSelected()`.

Add the row type in the same file, above `LibraryEditorViewModel`:

```csharp
/// <summary>One kept copy, as a row in the version list. A type rather than a formatted string because the
/// list has to hand the file path back when one is chosen.</summary>
public sealed class PatchVersionViewModel(PatchVersion version)
{
    public PatchVersion Version { get; } = version;

    /// <summary>The user's own short date and time, which is what a file listing shows everywhere else on
    /// the machine -- a fixed pattern here would be this one list disagreeing with all of them.</summary>
    public string Written => Version.Written.ToString("g", CultureInfo.CurrentCulture);
}
```

Add `using System.Collections.ObjectModel;` and `using System.Globalization;`.

- [ ] **Step 2: Add the restore command**

Add a sixth callback to the constructor — `Func<LibraryEntryViewModel, PatchVersion, Task> restore` — stored
as `_restore`, and:

```csharp
    /// <summary>Put the chosen version back. Confirmed by the caller, which owns the dialog: this is the
    /// second time today the same sound is being overwritten, and the first time was the accident.</summary>
    public async Task RestoreVersionAsync()
    {
        UserActionLog.Action("button: Restore version (library)");
        if (Selected is { } row && SelectedVersion is { } version)
            await _restore(row, version.Version);
    }

    public bool CanRestore => HasSelection && SelectedVersion is not null;
```

Raise `CanRestore` alongside the other flags, and add `x => x.SelectedVersion` to the `WhenAnyValue` that
raises them.

- [ ] **Step 3: Add the restore body to `LibraryViewModel`**

```csharp
    /// <summary>Put a kept copy back, after asking. The confirmation is not ceremony: restoring overwrites
    /// the file that is there now, and the user is by definition already having a bad day.</summary>
    private async Task RestoreVersionAsync(LibraryEntryViewModel row, PatchVersion version)
    {
        var when = version.Written.ToString("g", CultureInfo.CurrentCulture);
        if (!await _confirm($"Replace \"{row.Name}\" with the copy from {when}? " +
                            "What is there now is kept as a version, so this can be undone."))
            return;

        try
        {
            PatchHistory.Restore(row.FilePath, version.FilePath);
            _report($"Restored {Path.GetFileName(row.FilePath)} from {when}.", false);
            Refresh();
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"restore '{row.FilePath}' from '{version.FilePath}'", e.ToString());
            _report($"Could not restore that version: {e.Message}", true);
        }
    }
```

Pass it as the sixth argument where `Editor` is constructed. Add `using System.Globalization;` if it is not
already there.

- [ ] **Step 4: Add the markup**

In `Src/Views/LibraryEditorView.axaml`, after the init-tone note and before the Compare button:

```xml
                <!-- The kept copies. Collapses entirely when there are none, which is every snapshot until
                     the first time it is written over -- a caption above an empty box would read as a
                     control that is broken rather than one that is unused. -->
                <StackPanel Orientation="Vertical" Spacing="4" IsVisible="{Binding HasVersions}">
                    <TextBlock Text="Earlier versions"
                               Foreground="{StaticResource SnMutedTextBrush}" />
                    <ComboBox ItemsSource="{Binding Versions}"
                              SelectedItem="{Binding SelectedVersion, Mode=TwoWay}"
                              HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate x:DataType="vm:PatchVersionViewModel">
                                <TextBlock Text="{Binding Written}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                    <Button Content="Restore this version"
                            Command="{Binding RestoreVersionAsync}"
                            IsEnabled="{Binding CanRestore}"
                            HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Center" />
                </StackPanel>
```

- [ ] **Step 5: Build and run the whole suite**

Expected: build succeeds, 954 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/LibraryEditorViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryEditorView.axaml
git commit -m "feat: show a snapshot's earlier versions and put one back"
```

---

### Task 5: verify it by hand

**Files:** none — this task changes nothing.

The application's own log records every UI action, and the library folder is a folder, so this is checkable
without hardware.

- [ ] **Step 1: Run the application against a throwaway library**

Launch the built executable, point the library at a new empty folder through Change…, and check the
following, in order. Capture a screenshot of the editor panel with a version list showing.

- [ ] **Step 2: Walk the checks**

1. Save a tone into the library from the Parameters tab. The editor panel shows **no** version list, and no
   `.history` folder exists yet.
2. Change its notes and press Save changes. A version list appears with one entry, dated when the file was
   first written.
3. Change the notes again. Two entries.
4. Choose the older version, press Restore this version, confirm. The notes go back to what they were, and a
   third entry appears — the one that was just replaced.
5. Delete the snapshot. It leaves the list; `.history/<name>/` still holds its copies.
6. Make `.history/<name>` a *file* rather than a folder, then try to save a change: the save is refused with
   a message naming the reason, and the snapshot on disk is untouched.

- [ ] **Step 3: Report**

Report what was seen for each of the six, and attach the screenshot. Do not commit anything.

---

## Verification by hand (user)

- [ ] Annotating a patch keeps its previous copy, and the dates in the list are the dates the content was
  written rather than when it was archived.
- [ ] Restoring an older version brings back the right sound, and the version that was displaced is itself
  restorable.
- [ ] Deleting a patch leaves a copy in `.history`.
- [ ] The library list never shows anything from `.history`.
- [ ] After ten or more saves of the same patch, only ten versions remain.
