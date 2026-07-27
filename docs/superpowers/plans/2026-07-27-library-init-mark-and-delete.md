# Library: show the init-tone mark, and delete an entry — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or
> superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** make the init-tone mark visible in the library list, and let the user remove a snapshot from
the library.

**Architecture:** the mark already lives in the settings file and is already applied by
`LibraryViewModel.MarkAsInitTone` (see `2026-07-27-tone-init-copy-randomise.md`, Task 7); this exposes it
on each row. Deleting is one new function on `SnapshotLibrary` plus a command that asks first, through the
same `ConfirmViewModel` dialog Init and Paste use.

**Tech stack:** .NET 10, Avalonia 12, ReactiveUI source generators, NUnit 3.

---

## Decisions

**Delete is permanent.** Not a move to the recycle bin: .NET has no cross-platform API for one, and this
application builds and tests on Windows, Linux and macOS. A confirmation dialog naming the file is what
stands between the user and a lost snapshot, and the message says the deletion cannot be undone.

**The mark folds into the Kind column** rather than taking a column of its own. That is the reasoning
`LibraryEntryViewModel.Kind` already records for the tone type — "an eighth column for a word that is
blank on half the rows would earn less than the width it cost" — and it is more true of a flag that is
set on at most five rows in the whole library.

**Deleting a marked tone clears its mark.** The alternative is a settings file pointing at a file the user
just deleted. `InitToneResolution` treats a stale mark as "fall back to the bundled tone and say so", so
nothing breaks either way, but leaving a mark the user cannot see or clear is a trap.

---

## Conventions

Build and test with the user-local SDK (the system `dotnet` is 8/9 and too old). `Src/bin` is locked by
the user's own running application — **never kill it**; redirect the output instead, four levels deep
beside a junction to `Src`, because several tests find `Src\Assets\parameters.bin` by walking `..\..\..\..`:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Scripts\Temp\claude\verify\o\1\2\3" | Out-Null
if (-not (Test-Path "C:\Scripts\Temp\claude\verify\Src")) { New-Item -ItemType Junction -Path "C:\Scripts\Temp\claude\verify\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **824 passed, 0 failed**.

XAML: never hardcode a colour (`{StaticResource ...}`); an em dash in prose must be the character `—`, as
a literal `--` in an XML comment fails the build. Comments say *why*, not *what*.

Git: explicit paths only, never `git add -A`, never stage `Src/Assets/new-icon-orig.svg`, never
`--no-verify`, no push, no merge. Branch `feature/tone-init-copy-randomise`.

---

### Task 1: Deleting a snapshot file

**Files:**
- Modify: `Src/Models/Services/SnapshotLibrary.cs`
- Test: `Tests/TestSnapshotLibrary.cs`

- [ ] **Step 1: Write the failing tests**

Add to the existing fixture in `Tests/TestSnapshotLibrary.cs`, beside the other write tests. Use whatever
helpers that fixture already has for making a folder and a snapshot (`Save(...)`, `Tone(...)` and the
`_folder` field — read the file and follow it rather than inventing new ones):

```csharp
    [Test]
    public void A_deleted_snapshot_is_gone_from_the_folder_and_the_listing()
    {
        var kept = Save("rhodes.json", Tone("Warm Rhodes"));
        var doomed = Save("pad.json", Tone("Glass Pad"));

        SnapshotLibrary.Delete(doomed);

        Assert.That(File.Exists(doomed), Is.False);
        Assert.That(File.Exists(kept), Is.True, "and nothing else went with it");
        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.FilePath), Is.EqualTo(new[] { kept }));
    }

    /// <summary>The listing is a snapshot of a folder other things can change -- another copy of this
    /// application, a file manager, a sync client -- so the file a user selects may already be gone by the
    /// time they press Delete. Reporting that as a failure would say something went wrong when the folder
    /// is in exactly the state they asked for.</summary>
    [Test]
    public void Deleting_a_file_that_is_already_gone_is_not_an_error()
    {
        var path = Path.Combine(_folder, "never-existed.json");

        Assert.That(() => SnapshotLibrary.Delete(path), Throws.Nothing);
    }

    /// <summary>Anything else -- no permission, a file another process holds open on Windows -- must
    /// reach the caller, which is the only place that can tell the user their snapshot is still there.</summary>
    [Test]
    public void A_deletion_that_cannot_happen_reports_it()
    {
        // A directory where the file should be: Delete refuses it rather than removing a directory
        // that is not a snapshot at all.
        var path = Path.Combine(_folder, "not-a-file.json");
        Directory.CreateDirectory(path);

        Assert.That(() => SnapshotLibrary.Delete(path), Throws.Exception);
        Assert.That(Directory.Exists(path), Is.True);
    }
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter SnapshotLibraryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error — `SnapshotLibrary.Delete` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `SnapshotLibrary`, beside `Create` and `WriteMetadata`:

```csharp
    /// <summary>Remove a snapshot from the library, permanently.
    ///
    /// <b>Not a move to the recycle bin</b>, because .NET has no cross-platform API for one and this
    /// application runs on all three desktops. What stands between the user and a lost snapshot is the
    /// confirmation the caller asks for, not this.
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

        File.Delete(filePath);
        Log.Information("Deleted the snapshot {Path} from the library.", filePath);
    }
```

- [ ] **Step 4: Run until green, then run the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter SnapshotLibraryTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/SnapshotLibrary.cs Tests/TestSnapshotLibrary.cs
git commit -m "feat: delete a snapshot from the library"
```

---

### Task 2: Showing the init-tone mark on a row

**Files:**
- Modify: `Src/ViewModels/LibraryEntryViewModel.cs`
- Modify: `Src/ViewModels/LibraryViewModel.cs`
- Modify: `Src/Views/LibraryView.axaml`

No unit test: these view models are not under test in this repo. Verification is that the solution builds
(which compiles every binding) and the suite still passes.

- [ ] **Step 1: Give an entry a settable mark**

`LibraryEntryViewModel` is currently a plain class whose properties are all computed from `Entry`. The
mark is not in the file — it is in the settings — so it has to be settable and observable. Make the class
`partial`, add `using ReactiveUI.SourceGenerators;`, and add:

```csharp
    /// <summary>Whether Init Tone starts from this snapshot for its engine. Not read from the file like
    /// every other property here: the mark lives in the settings, so the library sets it when it builds
    /// the row and again when the user moves it.</summary>
    [Reactive] private bool _isInitTone;

    /// <summary>What the Kind column adds when the mark is set. A word rather than a glyph: the two
    /// glyphs this list already uses mean favourite and rating, and a third would be one more thing to
    /// learn for a flag at most five rows in the library carry.</summary>
    public string InitMark => IsInitTone ? "init" : "";
```

Check how `[Reactive]` is spelled in this codebase before writing it — `grep -n "\[Reactive\]" Src/ViewModels/LibraryViewModel.cs` — and raise `InitMark` when `IsInitTone` changes, following whatever pattern the other view models use for a computed property over a reactive field.

- [ ] **Step 2: Set the mark when rows are built and when it moves**

In `LibraryViewModel`, `_initTones` already holds the marks (tone type to file name, relative to the
library folder). Add one private helper and call it from both places:

```csharp
    /// <summary>Point every row at the current marks. Called after the list is rebuilt and after the user
    /// moves a mark, so the row that had it stops showing it in the same gesture that gives it to another.
    /// Compared on the file name, which is what the settings store, and case-insensitively, because
    /// Windows and macOS will hand back a name that differs from the stored one only in case.</summary>
    private void ApplyInitToneMarks()
    {
        foreach (var entry in Entries)
            entry.IsInitTone = entry.Entry.Head.ToneType is { } toneType &&
                               _initTones.TryGetValue(toneType, out var file) &&
                               string.Equals(file, Path.GetFileName(entry.FilePath),
                                   StringComparison.OrdinalIgnoreCase);
    }
```

Call it at the end of whatever method fills `Entries` (`Refresh`, or the filtering method it delegates to
— read the file and find where `Entries` is repopulated), and at the end of `MarkAsInitTone`.

`InitToneNote` on the view model already computes the same comparison for the details panel. Reduce it to
one place: have `InitToneNote` read `SelectedEntry?.IsInitTone` rather than repeating the lookup.

- [ ] **Step 3: Show it in the list**

In `Src/Views/LibraryView.axaml`, the Kind column is column 2 in both the header grid
(`ColumnDefinitions="28,2*,110,140,80,130,3*"`, around line 117) and the row template (around line 134).
Widen that column from `110` to `150` **in both**, and replace the row's Kind cell with the kind and the
mark side by side:

```xml
                                <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="6">
                                    <TextBlock Text="{Binding Kind}" />
                                    <TextBlock Text="{Binding InitMark}"
                                               Foreground="{StaticResource SnMutedTextBrush}" />
                                </StackPanel>
```

Read the existing cell first and keep whatever `TextTrimming`, `VerticalAlignment` or tooltip it already
carries. The two grids must keep agreeing — that is written in the comment above them, and it is why the
width changes in both.

- [ ] **Step 4: Build and run the suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: build succeeds (an `AVLN2000` means a binding names something the view model does not have), and
the suite still passes.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/LibraryEntryViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryView.axaml
git commit -m "feat: show which library tone is an engine's init tone"
```

---

### Task 3: Removing an entry from the library

**Files:**
- Modify: `Src/ViewModels/LibraryViewModel.cs`
- Modify: `Src/Views/LibraryView.axaml`
- Modify: `Src/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Take a confirmation callback**

`LibraryViewModel`'s constructor already takes its dialogs as callbacks rather than reaching for a window
— `Func<LibraryEntry, Task> load`, `Func<string, Task<string?>> pickFolder`, a status reporter, and the
settings path. Add one more in the same style, after `pickFolder`:

```csharp
    /// <param name="confirm">Ask the user a yes/no question. A callback for the same reason pickFolder is
    /// one: this view model is inside a tab, the dialog belongs to the window, and a view model that
    /// reached for a window could not be constructed without one.</param>
```

with the parameter `Func<string, Task<bool>> confirm`, stored in a `_confirm` field beside the others.

- [ ] **Step 2: Add the command**

```csharp
    /// <summary>Remove the selected snapshot from the library, after asking. The file goes for good --
    /// see SnapshotLibrary.Delete -- so this is the one place in the library that asks before acting.
    ///
    /// A mark pointing at the file goes with it. InitToneResolution copes with a stale mark by falling
    /// back to the bundled tone and saying so, but a mark the user can no longer see or clear is a trap,
    /// and this is the moment it is cheapest to tidy.</summary>
    public async Task DeleteSelectedAsync()
    {
        UserActionLog.Action("button: Delete from library");
        if (SelectedEntry is not { } selected) return;

        if (!await _confirm($"Delete \"{selected.Name}\" from the library? " +
                            $"The file {Path.GetFileName(selected.FilePath)} is removed for good — " +
                            "this cannot be undone.")) return;

        try
        {
            SnapshotLibrary.Delete(selected.FilePath);
        }
        catch (Exception e)
        {
            UserActionLog.Failed("delete a snapshot from the library", e.ToString());
            _report($"Could not delete {selected.Name}: {e.Message}", true);
            return;
        }

        // Clear a mark that pointed at it, before the refresh, so the row that replaces the selection is
        // built against the marks as they now are.
        var markedEngine = _initTones.FirstOrDefault(m =>
            string.Equals(m.Value, Path.GetFileName(selected.FilePath), StringComparison.OrdinalIgnoreCase));
        if (markedEngine.Key is not null)
        {
            _initTones.Remove(markedEngine.Key);
            try
            {
                LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(Folder, _initTones));
            }
            catch (Exception e)
            {
                // The snapshot is already gone, so this cannot be undone by refusing. Say it and carry on:
                // the mark is stale, which InitToneResolution handles, rather than wrong.
                UserActionLog.Failed("clear the init-tone mark of a deleted snapshot", e.ToString());
            }
        }

        Refresh();
        _report($"Deleted {selected.Name} from the library.", false);
    }
```

Check the real names of `_report`, `_settingsPath` and `Refresh` in the file before using them, and match
whatever the neighbouring commands do about `UserActionLog`. `LibraryPreferences` is the namespace-level
record added with the init-tone settings.

- [ ] **Step 3: Add the button**

In the details panel of `Src/Views/LibraryView.axaml`, after the "Use as the init tone" button:

```xml
                            <Button Content="Delete from the library"
                                    Command="{Binding DeleteSelectedAsync}"
                                    IsEnabled="{Binding HasSelection}"
                                    ToolTip.Tip="Remove this snapshot from the library. The file is deleted for good — it does not go to the recycle bin."
                                    HorizontalAlignment="Stretch"
                                    HorizontalContentAlignment="Center" />
```

Match the neighbouring buttons' alignment and padding exactly — read them first; the ones there use
`HorizontalAlignment="Stretch"` inside a `StackPanel` with its own spacing.

- [ ] **Step 4: Wire the confirmation to the window's dialog**

In `Src/ViewModels/MainWindowViewModel.cs`, `LibraryVm` is constructed with its callbacks (search for
`new LibraryViewModel(`). Add the new argument in the position Step 1 gave it, routed to the confirm
dialog Init and Paste already use:

```csharp
            async message => await ShowConfirmDialog.Handle(new ConfirmViewModel(message, "Delete")),
```

- [ ] **Step 5: Build and run the suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/LibraryViewModel.cs Src/ViewModels/MainWindowViewModel.cs Src/Views/LibraryView.axaml
git commit -m "feat: delete the selected snapshot from the library"
```

---

## Verification by hand (user)

- [ ] A tone marked as an init tone shows "init" beside its kind, and marking another tone of the same
  engine moves the word in the same gesture.
- [ ] Delete asks first, and cancelling leaves the file alone.
- [ ] Deleting the tone that was marked leaves no mark behind: Init for that engine reports that none is
  set, rather than that the marked one is missing.
- [ ] Deleting a file another program has already removed reports the deletion, not a failure.
