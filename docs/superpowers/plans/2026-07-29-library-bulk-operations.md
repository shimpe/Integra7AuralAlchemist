# Library bulk operations — implementation plan (phase 2 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** select many snapshots at once and annotate or delete all of them, so that keeping a real library
tidy costs one gesture rather than one dialog per patch.

**Architecture:** one pure service, `BulkEdit`, decides what a change means for a single snapshot; the list
gains multi-selection; a second panel appears beside it when more than one row is selected and drives that
service one file at a time.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI 24, NUnit 4.

**Spec:** `docs/superpowers/specs/2026-07-29-library-overhaul-design.md`, the "Phase 2" section. Read it and
the "Architecture" section first.

**Phase 2 of five.** Phase 1 (version history) is merged. Phases 3–5 — audition, duplicates with deep
search, DAW export — are separate plans. Do not build ahead of this one.

**What phase 1 already gives you:** every write and every delete copies the file into
`<library>/.history/<stem>/` first and keeps the newest ten. So bulk delete is recoverable without this plan
doing anything, and bulk annotation is too. Do not add a second safety net.

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

A `--filter` must come **before** `-p:OutputPath`. The suite stands at **957 passed, 0 failed**.

**Traps this project has actually hit**, all of which apply here:

- **An XML comment may not contain `--`**, in `.csproj` or `.axaml`. MSBuild then fails to *load* the
  project, nothing compiles, and a naive error count reads as zero. Check for `MSB4025` before believing a
  sudden green. Prose here uses real em dashes.
- **Never hardcode a colour in XAML.** Use `{StaticResource ...}`.
- **Do not edit `.axaml` or `.csproj` with `sed`, and do not rewrite source through PowerShell** — they are
  CRLF with a BOM and PowerShell 5.1's `Set-Content` defaults to ANSI. Use the Edit and Write tools.
- **A `ToolTip` is a popup and swallows clicks on its own control.** The bulk panel's buttons are pressed
  repeatedly; do not put tooltips on them.
- Compiled bindings are checked at build time; a wrong member name is `AVLN2000`.
- **A view model cannot be constructed in a test** under ReactiveUI 24 — `WhenAnyValue` throws
  `InvalidOperationException` demanding `RxAppBuilder`'s `.BuildApp()`. Anything worth testing goes in a
  service.

**House style:** comments say *why*, not *what*.

**Git:** branch `feature/library-bulk-operations`, which already exists and holds this plan. Explicit paths
only; never `git add -A`; never stage `Src/Assets/new-icon-orig.svg`; never `--no-verify`; do not merge or
push.

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `Src/Models/Services/BulkEdit.cs` | What one bulk change means for one snapshot |
| Create `Src/ViewModels/LibraryBulkEditViewModel.cs` | The panel shown when more than one row is selected |
| Create `Src/Views/LibraryBulkEditView.axaml` (+ `.axaml.cs`) | Its markup |
| Modify `Src/ViewModels/LibraryViewModel.cs` | Multi-selection, the batch loop, which panel shows |
| Modify `Src/Views/LibraryView.axaml` | `SelectionMode`, `SelectedItems`, the two panels |

**New tests:** `Tests/TestBulkEdit.cs`.

**A deliberate deviation from the spec, and why.** The spec says "the editor panel has two shapes". This
plan builds two panels instead, swapped by `IsVisible`. `LibraryEditorViewModel` describes one snapshot: its
name, its notes, its versions, and five commands that act on it. A bulk form shares none of that — no name,
no notes, no versions, and its buttons act on many. Folding both into one class would give every member a
"which shape am I in" question to answer, which is how the file that phase 1 had to split got that big. The
user-visible result is exactly what the spec describes.

**The second deviation, smaller.** The spec describes the bulk form as a form. This builds it as one button
per field with no Save, because a staged bulk form has to distinguish "set this to none" from "leave this
alone" for every field — and the only honest ways to do that are a third state on every control or a tick
beside each one. One button per field removes the problem instead of solving it: a field nobody pressed a
button for is simply not in the change.

---

### Task 1: `BulkEdit`

**Files:** Create `Src/Models/Services/BulkEdit.cs`; Test `Tests/TestBulkEdit.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestBulkEdit.cs`:

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What one bulk change means for one snapshot.
///
/// Every rule here is a decision a user will assume one way or the other, and getting one wrong is not a
/// crash but a library quietly annotated wrongly in fourteen places at once. That is why the decisions are
/// in a pure function with tests rather than in the loop that calls it.</summary>
public class BulkEditTests
{
    private static SnapshotHead Head(string name, string category = "E.Piano",
        string[]? tags = null, string notes = "notes", int rating = 3, bool favourite = true) =>
        new(name, SnapshotKinds.Tone, "SN-S", category, tags ?? ["warm", "trio gig"], notes, rating,
            favourite);

    /// <summary>A change that says nothing changes nothing. This is what makes the batch loop safe to run
    /// over a selection where some fields were never touched.</summary>
    [Test]
    public void An_empty_change_leaves_every_field_as_it_was()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange());

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo("E.Piano"));
            Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
            Assert.That(result.Notes, Is.EqualTo("notes"));
            Assert.That(result.Rating, Is.EqualTo(3));
            Assert.That(result.Favourite, Is.True);
        });
    }

    /// <summary>The name is never touched by a bulk change: a rename cannot be bulk, and null is what
    /// SnapshotMetadata reads as "leave the name alone".</summary>
    [Test]
    public void The_name_is_never_part_of_a_bulk_change()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Rating: 5)).Name, Is.Null);
    }

    /// <summary>Notes are not a bulk field either -- one note pasted over fourteen sounds is not something
    /// anybody wants -- so they have to survive a change that sets something else.</summary>
    [Test]
    public void Notes_survive_a_change_to_another_field()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Favourite: false)).Notes,
            Is.EqualTo("notes"));
    }

    [Test]
    public void Setting_a_field_replaces_only_that_field()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Category: "Organ"));

        Assert.That(result.Category, Is.EqualTo("Organ"));
        Assert.That(result.Rating, Is.EqualTo(3), "and leaves the rest alone");
    }

    /// <summary>An empty category is a real value -- "this sound has no category" -- and has to be
    /// distinguishable from "do not touch the category", which is null.</summary>
    [Test]
    public void An_empty_category_clears_it_rather_than_meaning_leave_it_alone()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Category: "")).Category, Is.Empty);
    }

    /// <summary>Added tags join the ones already there. Replacing would wipe each patch's own vocabulary,
    /// which is the thing tags exist to hold.</summary>
    [Test]
    public void Added_tags_join_the_ones_already_there_in_the_order_they_were_in()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["bright"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig", "bright" }));
    }

    [Test]
    public void Adding_a_tag_a_snapshot_already_carries_changes_nothing()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["WARM"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }),
            "matched without regard to case, and the spelling already there is kept");
    }

    [Test]
    public void Removing_a_tag_takes_it_off_whatever_its_case()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(RemoveTags: ["Trio Gig"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm" }));
    }

    [Test]
    public void Removing_a_tag_a_snapshot_does_not_carry_changes_nothing()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(RemoveTags: ["loud"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
    }

    /// <summary>A tag in both lists is a mistake either way, so the answer only has to be one a user can
    /// predict: removal is applied after addition, so removal wins.</summary>
    [Test]
    public void A_tag_both_added_and_removed_is_removed()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"),
            new BulkChange(AddTags: ["bright"], RemoveTags: ["bright"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
    }

    /// <summary>Blank entries are what a half-typed tag box contributes, and are not a request for an empty
    /// tag. Whitespace is trimmed on both sides, matching LibraryListing.ParseTags and LibraryFilter.
    /// </summary>
    [Test]
    public void Blank_and_padded_tags_are_tidied_rather_than_stored()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["  ", " bright "]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig", "bright" }));
    }

    /// <summary>A Studio Set has no category -- sixteen parts each with one of their own -- so a bulk
    /// category applied across a mixed selection must not invent one for it. The caller filters the
    /// selection; this is the half that makes the filtering visible as a rule rather than a coincidence.
    /// </summary>
    [Test]
    public void A_studio_set_never_takes_a_category()
    {
        var studioSet = new SnapshotHead("World Pop", SnapshotKinds.StudioSet, null, "", [], "", 0, false);

        Assert.That(BulkEdit.Apply(studioSet, new BulkChange(Category: "Organ")).Category, Is.Empty);
    }

    [Test]
    public void A_rating_and_a_favourite_are_set_outright()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Rating: 0, Favourite: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.Rating, Is.Zero, "zero is a rating, not 'leave it alone'");
            Assert.That(result.Favourite, Is.False);
        });
    }
}
```

- [ ] **Step 2: Run and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter BulkEditTests -p:OutputPath="C:\Scripts\Temp\claude\verify\o\1\2\3\"
```

Expected: compile error `CS0246` — `BulkEdit` and `BulkChange` do not exist.

- [ ] **Step 3: Implement**

Create `Src/Models/Services/BulkEdit.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One change to make to many snapshots at once. <b>Null means "leave this alone"</b>, which is
/// what lets one type describe every button on the bulk panel: each sets a single field and leaves the rest
/// null.
///
/// Notes and the name are absent on purpose. One note pasted over fourteen sounds is not something anybody
/// wants, and a rename cannot be bulk at all.</summary>
/// <param name="Category">The category to set, or null to leave it. <b>Empty is a value</b> -- "this sound
/// has no category" -- and is not the same as null.</param>
/// <param name="Rating">0 to 5, or null to leave it. <b>Zero is a rating</b>, meaning unrated.</param>
/// <param name="Favourite">Set or clear, or null to leave it.</param>
/// <param name="AddTags">Tags to add to whatever is already there.</param>
/// <param name="RemoveTags">Tags to take off. Applied after <paramref name="AddTags"/>, so a tag in both
/// lists ends up removed -- a mistake either way, but a predictable one.</param>
public sealed record BulkChange(
    string? Category = null,
    int? Rating = null,
    bool? Favourite = null,
    IReadOnlyList<string>? AddTags = null,
    IReadOnlyList<string>? RemoveTags = null);

/// <summary>What a <see cref="BulkChange"/> means for one snapshot.
///
/// <b>Apart from the loop that applies it</b> for the reason every decision in this folder is apart from its
/// caller: a view model cannot be constructed in a test under ReactiveUI 24, and these rules are not
/// arithmetic anybody can check by reading. Getting one wrong is not a crash -- it is a library quietly
/// annotated wrongly in fourteen places at once, which is exactly the kind of mistake bulk editing exists to
/// make possible.
///
/// It answers a whole <see cref="SnapshotMetadata"/> rather than a delta, because that is what
/// <see cref="SnapshotLibrary.WriteMetadata"/> takes and that method replaces every field it is given. A
/// caller assembling one by hand would be one field away from wiping a note.</summary>
public static class BulkEdit
{
    /// <summary>Ordinal, ignoring case -- <see cref="LibraryFilter"/>'s rule for tags, and for its reason:
    /// "Warm" and "warm" are one tag to anybody using this.</summary>
    private static readonly StringComparer Loosely = StringComparer.OrdinalIgnoreCase;

    public static SnapshotMetadata Apply(SnapshotHead head, BulkChange change) =>
        new(
            // A Studio Set is sixteen parts each with a category of its own and has none; a bulk category
            // applied across a mixed selection must not invent one for it.
            head.Kind == SnapshotKinds.Tone ? change.Category ?? head.Category : head.Category,
            Tags(head, change),
            head.Notes,
            change.Rating ?? head.Rating,
            change.Favourite ?? head.Favourite);

    private static List<string> Tags(SnapshotHead head, BulkChange change)
    {
        // The order already there is kept and additions go on the end: a tag list is something the user has
        // read before, and resorting it on every bulk edit would make it unrecognisable.
        var tags = head.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        foreach (var tag in (change.AddTags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0))
            if (!tags.Contains(tag, Loosely))
                tags.Add(tag);

        // After the additions, so a tag in both lists is removed. See the note on BulkChange.
        var unwanted = (change.RemoveTags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (unwanted.Count > 0) tags.RemoveAll(tag => unwanted.Contains(tag, Loosely));

        return tags;
    }
}
```

- [ ] **Step 4: Run the tests, then the whole suite**

Expected: 13 pass in the filter, 970 overall (957 + 13), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/BulkEdit.cs Tests/TestBulkEdit.cs
git commit -m "feat: decide what a bulk change means for one snapshot"
```

---

### Task 2: multi-selection, established by experiment

**Files:** Modify `Src/Views/LibraryView.axaml:145-146`, `Src/ViewModels/LibraryViewModel.cs`

**Read this before writing anything.** Avalonia 12's `ListBox` exposes `SelectedItem`, `SelectedItems`,
`Selection` and `SelectionMode`, all public — confirmed from `Avalonia.Controls.xml` in the package, not
assumed. What it does **not** promise is how `SelectedItem` and `SelectedItems` behave when both are bound
at once, or exactly when the bound collection is repopulated as `Entries` is cleared and refilled by
`ApplyFilter`. The rest of this phase sits on top of that behaviour, so it is established by running the
application before anything is built on it.

- [ ] **Step 1: Make the list multi-selectable and observe what the view model sees**

In `Src/Views/LibraryView.axaml`, change lines 145–146 from:

```xml
                         SelectedItem="{Binding SelectedEntry, Mode=TwoWay}"
                         SelectionMode="Single">
```

to:

```xml
                         SelectedItem="{Binding SelectedEntry, Mode=TwoWay}"
                         SelectedItems="{Binding SelectedEntries}"
                         SelectionMode="Extended">
```

In `LibraryViewModel`, add beside `Entries`:

```csharp
    /// <summary>Every selected row. Avalonia fills this collection as the selection changes; the view model
    /// does not assign it, which is why it is get-only and why the binding is not two-way.</summary>
    public ObservableCollection<LibraryEntryViewModel> SelectedEntries { get; } = [];
```

And, temporarily, in the constructor after the existing subscriptions:

```csharp
        // TEMPORARY, removed at the end of this task: what the view model actually sees as the selection
        // changes, so the rest of the phase is built on observed behaviour rather than on an assumption.
        SelectedEntries.CollectionChanged += (_, _) =>
            Serilog.Log.Information("selection: {Count} rows, anchor {Anchor}",
                SelectedEntries.Count, SelectedEntry?.Name ?? "none");
```

- [ ] **Step 2: Build, run, and record the answers**

Build, run the application, go to the Library tab and do each of these, reading `logs/` afterwards:

1. Click one row. How many rows does the log report, and is `SelectedEntry` that row?
2. Control-click a second row. Does the count go to 2, and what is the anchor?
3. Shift-click a third. Count?
4. Type in the search box so the list re-filters while two rows are selected. What does the count do, and
   does `ApplyFilter`'s selection restoration still work?
5. Press Refresh with two rows selected. Same question.

**Write the five answers into your report.** They decide two things in task 3: whether the bulk panel can
trust `SelectedEntries.Count`, and whether `ApplyFilter` has to restore a multi-selection by path the way it
already restores the single one.

- [ ] **Step 3: Restore the selection across a refresh, if step 2 showed it is lost**

`ApplyFilter` already restores the single selection by path, because the row objects are rebuilt:

```csharp
        var selectedPath = SelectedEntry?.FilePath;
```

If step 2 showed the multi-selection is dropped by a refresh, capture the paths the same way and put them
back after `Entries` is refilled:

```csharp
        var selectedPaths = SelectedEntries.Select(row => row.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // ... after Entries is refilled and the single selection restored:
        foreach (var row in Entries.Where(row => selectedPaths.Contains(row.FilePath)))
            if (!SelectedEntries.Contains(row))
                SelectedEntries.Add(row);
```

A bulk edit ends in `Refresh()`, so without this a user annotating fourteen snapshots would have to select
them again to do anything else to them. **If step 2 showed the selection survives on its own, do not add
this** — say so in your report instead.

- [ ] **Step 4: Remove the temporary logging, build, run the suite**

Expected: build succeeds, 970 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/LibraryView.axaml Src/ViewModels/LibraryViewModel.cs
git commit -m "feat: let the library list select more than one snapshot"
```

---

### Task 3: the bulk panel

**Files:** Create `Src/ViewModels/LibraryBulkEditViewModel.cs`, `Src/Views/LibraryBulkEditView.axaml` (+
`.axaml.cs`); Modify `Src/ViewModels/LibraryViewModel.cs`, `Src/Views/LibraryView.axaml`

- [ ] **Step 1: Write the view model**

Create `Src/ViewModels/LibraryBulkEditViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The panel that replaces the metadata editor when more than one snapshot is selected.
///
/// <b>One button, one field.</b> Every action here builds a <see cref="BulkChange"/> with exactly one thing
/// set and hands it over; nothing is staged and there is no Save. That is what removes the hardest problem a
/// bulk form has -- telling "set this to none" apart from "leave this alone" -- because a field the user did
/// not press a button for is simply not in the change.
///
/// <b>It holds no snapshot and opens no file.</b> The selection and the writing both belong to
/// <see cref="LibraryViewModel"/>, which owns the folder and the refresh.</summary>
public sealed partial class LibraryBulkEditViewModel : ViewModelBase
{
    private readonly Func<BulkChange, Task> _apply;
    private readonly Func<Task> _delete;

    /// <param name="apply">Write one change across the selection.</param>
    /// <param name="delete">Remove the selection from the library, after asking.</param>
    public LibraryBulkEditViewModel(Func<BulkChange, Task> apply, Func<Task> delete)
    {
        _apply = apply;
        _delete = delete;
    }

    /// <summary>How many rows the panel is acting on. Set by the list; shown on every button that acts, so
    /// that pressing one is never a guess about how much it does.</summary>
    [Reactive] private int _count;

    public string Summary => $"{Count} snapshots selected.";

    public string DeleteLabel => $"Delete {Count} snapshots…";

    /// <summary>Raised by the list when the selection changes, because the two strings above are computed
    /// and the generated setter for Count does not know about them.</summary>
    public void CountChanged()
    {
        this.RaisePropertyChanged(nameof(Summary));
        this.RaisePropertyChanged(nameof(DeleteLabel));
    }

    [Reactive] private string _tagsToAdd = "";
    [Reactive] private string _tagsToRemove = "";
    [Reactive] private string _categoryLabel = LibraryListing.NoCategory;

    /// <summary>The stars, reused from the single editor -- see <see cref="RatingViewModel"/>.</summary>
    public RatingViewModel Rating { get; } = new();

    public IReadOnlyList<string> CategoryLabels => LibraryListing.EditCategoryLabels;

    public async Task AddTagsAsync()
    {
        UserActionLog.Action("button: Add tags to all (library)");
        await _apply(new BulkChange(AddTags: LibraryListing.ParseTags(TagsToAdd)));
        TagsToAdd = "";
    }

    public async Task RemoveTagsAsync()
    {
        UserActionLog.Action("button: Remove tags from all (library)");
        await _apply(new BulkChange(RemoveTags: LibraryListing.ParseTags(TagsToRemove)));
        TagsToRemove = "";
    }

    public async Task SetCategoryAsync()
    {
        UserActionLog.Action("button: Set category on all (library)");
        await _apply(new BulkChange(Category: LibraryListing.CategoryToWrite(CategoryLabel)));
    }

    public async Task SetRatingAsync()
    {
        UserActionLog.Action("button: Set rating on all (library)");
        await _apply(new BulkChange(Rating: Rating.Value));
    }

    public async Task MarkFavouriteAsync()
    {
        UserActionLog.Action("button: Mark all as favourite (library)");
        await _apply(new BulkChange(Favourite: true));
    }

    public async Task ClearFavouriteAsync()
    {
        UserActionLog.Action("button: Clear favourite on all (library)");
        await _apply(new BulkChange(Favourite: false));
    }

    public async Task DeleteAsync()
    {
        UserActionLog.Action("button: Delete selected (library)");
        await _delete();
    }
}
```

- [ ] **Step 2: Write the view**

Create `Src/Views/LibraryBulkEditView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:local="clr-namespace:Integra7AuralAlchemist.Views"
             mc:Ignorable="d" d:DesignWidth="380" d:DesignHeight="800"
             x:Class="Integra7AuralAlchemist.Views.LibraryBulkEditView"
             x:DataType="vm:LibraryBulkEditViewModel">

    <!-- One button per field, and each says how many snapshots it acts on. Nothing is staged and there is
         no Save: a field the user did not press a button for is simply not part of the change, which is
         how this avoids having to tell "set to none" apart from "leave alone".

         No ToolTip on any of these. They are pressed repeatedly while a library is being tidied, and a
         tooltip is a popup that swallows the click on the control it describes. -->

    <ScrollViewer>
        <StackPanel Orientation="Vertical" Spacing="10" Margin="0,0,16,0">

            <TextBlock Text="{Binding Summary}" TextWrapping="Wrap" FontWeight="Bold" />
            <TextBlock Text="The name and the notes are not here: a rename cannot be bulk, and one note across many sounds is not something anybody wants."
                       TextWrapping="Wrap"
                       Foreground="{StaticResource SnMutedTextBrush}" />

            <TextBlock Text="Tags to add, separated by commas" />
            <TextBox Text="{Binding TagsToAdd, Mode=TwoWay}" />
            <Button Content="Add to all" Command="{Binding AddTagsAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />

            <TextBlock Text="Tags to remove" />
            <TextBox Text="{Binding TagsToRemove, Mode=TwoWay}" />
            <Button Content="Remove from all" Command="{Binding RemoveTagsAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />

            <TextBlock Text="Category" />
            <ComboBox ItemsSource="{Binding CategoryLabels}"
                      SelectedItem="{Binding CategoryLabel, Mode=TwoWay}"
                      HorizontalAlignment="Stretch" />
            <Button Content="Set on all" Command="{Binding SetCategoryAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />

            <TextBlock Text="Rating" />
            <local:RatingView DataContext="{Binding Rating}" />
            <Button Content="Set on all" Command="{Binding SetRatingAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />

            <Button Content="Mark all as favourite" Command="{Binding MarkFavouriteAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />
            <Button Content="Clear favourite on all" Command="{Binding ClearFavouriteAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />

            <Button Content="{Binding DeleteLabel}" Command="{Binding DeleteAsync}"
                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" />
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

Note the `DataContext` on `RatingView`: that is the pattern `LibraryEditorView` already uses for the same
control.

Create `Src/Views/LibraryBulkEditView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class LibraryBulkEditView : UserControl
{
    public LibraryBulkEditView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Wire it into `LibraryViewModel`**

Add beside `Editor`:

```csharp
    /// <summary>The panel shown instead of <see cref="Editor"/> when more than one row is selected.</summary>
    public LibraryBulkEditViewModel BulkEditor { get; }

    /// <summary>Which of the two panels the view shows. More than one row is what makes a bulk change
    /// meaningful; one row is the editor, because a bulk form cannot rename or take a note.</summary>
    public bool IsBulkSelection => SelectedEntries.Count > 1;
```

Build it beside `Editor` in the constructor:

```csharp
        BulkEditor = new LibraryBulkEditViewModel(ApplyBulkChangeAsync, DeleteSelectionAsync);
```

Keep the two panels in step with the selection. **This must come after `BulkEditor` is assigned** — the
handler dereferences it, and a selection change can arrive as soon as `Refresh()` runs at the end of the
constructor:

```csharp
        SelectedEntries.CollectionChanged += (_, _) =>
        {
            BulkEditor.Count = SelectedEntries.Count;
            BulkEditor.CountChanged();
            this.RaisePropertyChanged(nameof(IsBulkSelection));
        };
```

If task 2 added the temporary logging handler in the same place, this replaces it.

The batch loop, which is the whole reason the service above is a service:

```csharp
    /// <summary>Apply one change to every selected snapshot, one file at a time.
    ///
    /// <b>A failure costs that file only.</b> A snapshot held open by a sync client must not abandon the
    /// other thirteen, so each is attempted and the failures are named at the end rather than thrown. Each
    /// write archives the previous copy through <see cref="PatchHistory"/>, so a bulk change is as
    /// recoverable as a single one.</summary>
    private Task ApplyBulkChangeAsync(BulkChange change)
    {
        // Copied first: the write path refreshes the list, which rebuilds the very rows being iterated.
        var rows = SelectedEntries.ToList();
        List<string> failed = [];

        foreach (var row in rows)
        {
            try
            {
                SnapshotLibrary.WriteMetadata(row.FilePath, BulkEdit.Apply(row.Entry.Head, change));
            }
            catch (Exception e)
            {
                UserActionLog.Failed($"bulk edit '{row.FilePath}'", e.ToString());
                failed.Add(row.Name);
            }
        }

        _report(failed.Count == 0
            ? $"Updated {rows.Count} snapshots."
            : $"Updated {rows.Count - failed.Count} of {rows.Count} snapshots; " +
              $"{failed.Count} could not be written: {string.Join(", ", failed)}.", failed.Count > 0);

        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Remove every selected snapshot, after asking once for all of them. Each is archived by
    /// <see cref="PatchHistory"/>, which is what makes one button able to remove fourteen files.</summary>
    private async Task DeleteSelectionAsync()
    {
        var rows = SelectedEntries.ToList();
        if (rows.Count == 0) return;

        if (!await _confirm($"Delete {rows.Count} snapshots from the library? " +
                            "A copy of each is kept in the history folder beside your library.",
                            "Delete")) return;

        List<string> failed = [];
        foreach (var row in rows)
        {
            try
            {
                SnapshotLibrary.Delete(row.FilePath);
            }
            catch (Exception e)
            {
                UserActionLog.Failed($"bulk delete '{row.FilePath}'", e.ToString());
                failed.Add(row.Name);
            }
        }

        _report(failed.Count == 0
            ? $"Deleted {rows.Count} snapshots."
            : $"Deleted {rows.Count - failed.Count} of {rows.Count}; " +
              $"{failed.Count} could not be removed: {string.Join(", ", failed)}.", failed.Count > 0);

        Refresh();
    }
```

- [ ] **Step 4: Show the right panel**

In `Src/Views/LibraryView.axaml`, replace the single line at 192 with the two panels:

```xml
                <local:LibraryEditorView DataContext="{Binding Editor}"
                                         IsVisible="{Binding !IsBulkSelection}" />
                <local:LibraryBulkEditView DataContext="{Binding BulkEditor}"
                                           IsVisible="{Binding IsBulkSelection}" />
```

**`IsVisible` binds against the outer view model, not the panel's own** — the `DataContext` on the same
element applies to the element's children, and Avalonia resolves the other bindings on that element against
the parent's context. If the build disagrees (`AVLN2000`), wrap each in a `Panel` carrying the `IsVisible`
and put the `DataContext` on the child.

- [ ] **Step 5: Build and run the suite**

Expected: build succeeds, 970 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/LibraryBulkEditViewModel.cs Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryBulkEditView.axaml Src/Views/LibraryBulkEditView.axaml.cs Src/Views/LibraryView.axaml
git commit -m "feat: annotate or delete many snapshots at once"
```

---

### Task 4: verify it by hand

**Files:** none — this task changes nothing.

- [ ] **Step 1: Run against a throwaway library**

There is a harness from phase 1 at
`C:\Scripts\Temp\claude\D--Projects-Integra7AuralAlchemist\8c8d7f87-72b2-4a26-87a8-d5f4e2f3e26d\scratchpad\historycheck.ps1`
which points the library folder at a scratch directory by swapping the settings file and putting it back in
a `finally`. Copy its setup; **do not point any check at the user's own library**.

- [ ] **Step 2: Walk the checks**

1. Select one row: the metadata editor is shown, as before.
2. Control-click a second: the panel changes to the bulk form and says "2 snapshots selected".
3. Add a tag to both, and confirm both files carry it and neither lost the tags it already had.
4. Remove that tag from both.
5. Set a rating on both; confirm the list's Rating column changes for both rows.
6. Confirm each of the four files now has versions in `.history`, one per bulk write.
7. Delete both: one confirmation naming the count, both files gone, copies kept in `.history`.
8. Select two rows, then type in the search box so one of them is filtered out. Confirm the panel and the
   selection do something sensible rather than acting on a row that is no longer visible.

- [ ] **Step 3: Report**

Report what was seen for each, and attach a screenshot of the bulk panel. Do not commit anything.

---

## Verification by hand (user)

- [ ] Control-click and shift-click select ranges the way they do everywhere else.
- [ ] A bulk tag add leaves each snapshot's existing tags alone.
- [ ] A bulk change to fourteen snapshots is one gesture, and the status line says how many were written.
- [ ] Bulk delete asks once, names the count, and leaves copies in the history folder.
- [ ] One unwritable file does not stop the other thirteen being updated.
