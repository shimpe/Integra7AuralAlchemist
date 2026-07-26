using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The library browser: one folder of snapshot files, listed, filtered, annotated, and loaded back into
/// the instrument.
///
/// <b>It works with no instrument attached</b>, which nothing else on the top-level tab strip does. Browsing,
/// searching, rating and annotating are file operations, so this view model is built once in
/// <c>MainWindowViewModel</c>'s constructor and never replaced -- unlike the mixer, the layer map and the
/// Motional Surround editor, which wrap live parameters and are rebuilt on every rescan. Only <see
/// cref="LoadSelectedAsync"/> needs the device, and it asks the window, which already knows whether there is one.
///
/// <b>No filtering logic lives here.</b> Every question about which entries are admitted is
/// <see cref="LibraryFilter"/>'s, and every question about order, labels, stars and tag text is
/// <see cref="LibraryListing"/>'s -- both pure, both tested. What is left in this file is state, the wiring that
/// re-filters when the state changes, and the three things a button can ask the file system to do. That split is
/// not tidiness: a view model here cannot be tested (there is no headless Avalonia harness in this repo), so
/// anything that could be got wrong belongs on the other side of it.
///
/// <b>The list is rebuilt, not mutated.</b> A refresh re-reads the folder and builds new rows, and every write
/// goes through a refresh -- see <see cref="LibraryEntryViewModel"/> for why. The selection is restored by file
/// path afterwards, because the row object is gone by then and the path is the only thing that survives.
///
/// <b>Nothing watches the folder.</b> A file added by another application appears when the list is refreshed,
/// which is a stated limitation of this plan rather than an oversight: a watcher is a background thread, a
/// debounce and a set of races over files this application is itself writing, and Refresh is one button.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    /// <summary>Send the selected snapshot to the instrument. The window's job, not this one's: it holds the API
    /// and the domain, it knows which part is selected and which engine that part holds, and the two restore
    /// paths -- Studio Set and tone -- are the ones its own Load buttons already use. Handing it the whole entry
    /// rather than a path is what lets it choose between them without re-reading the file.</summary>
    private readonly Func<LibraryEntry, Task> _load;

    /// <summary>Ask the user for a folder, starting at the one passed in. A callback rather than an
    /// <c>Interaction</c> of this view model's own, so that every dialog this application shows is registered in
    /// one place (<c>MainWindow.RegisterDialogHandler</c>) rather than in as many places as there are view
    /// models. Answers null for a cancellation and "" for a folder with no usable local path, which is the same
    /// distinction the snapshot pickers already make.</summary>
    private readonly Func<string, Task<string?>> _pickFolder;

    /// <summary>Say something on the window's status bar: the message, and whether it is a failure. Shared with
    /// the save and load commands rather than duplicated as a status line of this tab's own -- the status bar is
    /// window chrome, it is visible from every tab, and one channel means a user never has to wonder which of two
    /// places the last answer went to.</summary>
    private readonly Action<string, bool> _report;

    /// <summary>Where the library folder is remembered. Passed in rather than read from the environment here, for
    /// the reason <see cref="LibrarySettings"/> gives: that parameter is the difference between the settings
    /// having tests and not, and a caller that wants the real path can say
    /// <c>LibrarySettings.SettingsPath</c>.</summary>
    private readonly string _settingsPath;

    /// <summary>Everything in the folder, unfiltered, as the last read found it. The filter runs over this rather
    /// than over the rows on screen, so narrowing and then widening a filter cannot lose entries.</summary>
    private IReadOnlyList<LibraryEntry> _all = [];

    /// <param name="load">See <see cref="_load"/>.</param>
    /// <param name="pickFolder">See <see cref="_pickFolder"/>.</param>
    /// <param name="report">See <see cref="_report"/>.</param>
    /// <param name="settingsPath">See <see cref="_settingsPath"/>.</param>
    public LibraryViewModel(Func<LibraryEntry, Task> load, Func<string, Task<string?>> pickFolder,
        Action<string, bool> report, string settingsPath)
    {
        _load = load;
        _pickFolder = pickFolder;
        _report = report;
        _settingsPath = settingsPath;

        // Before the subscriptions below, so that the first filter runs against the real folder rather than
        // against "" -- which resolves to the process's current directory and would list whatever is beside the
        // executable.
        _folder = LibrarySettings.Load(settingsPath);

        // Every filter and both halves of the sort, in one subscription. Seven properties rather than seven
        // subscriptions because they all do the same thing and doing it once is what stops a change of two of
        // them at a time from filtering twice. WhenAnyValue fires on subscription, so this is also what performs
        // the first filter -- there is no separate initial call to forget.
        //
        // Not throttled, unlike the preset grids' search boxes: those filter ~6,000 presets through DynamicData
        // and this filters a folder, which is tens or hundreds of entries of six string comparisons each. A
        // keystroke's worth of that is not measurable, and a throttle would make the box feel like it was
        // catching up.
        this.WhenAnyValue(x => x.SearchText, x => x.KindLabel, x => x.CategoryLabel, x => x.RatingLabel,
                x => x.FavouritesOnly, x => x.SortLabel, x => x.Descending,
                (_, _, _, _, _, _, _) => Unit.Default)
            .Subscribe(_ => ApplyFilter());

        // The editor follows the selection, and two derived flags follow the editor. HasSelection and
        // CanSaveChanges are what the buttons and the panel bind to, and neither is raised by the generated
        // setters of the properties they read.
        this.WhenAnyValue(x => x.SelectedEntry).Subscribe(_ => ShowSelected());
        this.WhenAnyValue(x => x.EditName, x => x.SelectedEntry, (_, _) => Unit.Default).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(SelectedIsTone));
            this.RaisePropertyChanged(nameof(CanSaveChanges));
        });

        Refresh();
    }

    // ---- what is in the folder ----------------------------------------------------------------------------

    /// <summary>The rows on screen: what the filter admitted, in the order the sort asked for.</summary>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; } = [];

    /// <summary>Which row is selected, or null. Two-way from the list, and assigned from here after a refresh --
    /// see <see cref="Refresh"/>.</summary>
    [Reactive] private LibraryEntryViewModel? _selectedEntry;

    /// <summary>Where the library is. Shown, and changed through <see cref="ChangeFolderAsync"/> rather than by
    /// typing: a path typed one character at a time would re-read the folder on every keystroke, and every
    /// intermediate path is a folder that does not exist.</summary>
    [Reactive] private string _folder = "";

    /// <summary>How many entries there are and how many the filters admit, in words. The count is the only thing
    /// that can distinguish "this folder is empty" from "the filters admit nothing", and those two look identical
    /// on screen while having opposite remedies.</summary>
    [Reactive] private string _summary = "";

    // ---- the filters --------------------------------------------------------------------------------------

    [Reactive] private string _searchText = "";
    [Reactive] private string _kindLabel = LibraryListing.AnyKind;
    [Reactive] private string _categoryLabel = LibraryListing.AnyCategory;
    [Reactive] private string _ratingLabel = LibraryListing.AnyRating;
    [Reactive] private bool _favouritesOnly;

    /// <summary>The tags anywhere in the library, each with a checkbox. Rebuilt on every refresh from what the
    /// files actually carry (see <see cref="LibraryListing.AllTags"/>), with the ticked ones carried across if
    /// they still exist -- a tag whose last use was just deleted cannot go on filtering.</summary>
    public ObservableCollection<LibraryTagViewModel> Tags { get; } = [];

    /// <summary>Whether anything in the library carries a tag. The whole tag row collapses when nothing does,
    /// which is what a fresh library looks like -- a caption and an empty space would be a control that appears
    /// broken rather than unused. A property rather than a binding on <c>Tags.Count</c>: that count is an int,
    /// Avalonia will not convert one to a bool, and the failure would be a runtime binding error rather than a
    /// build one.</summary>
    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<string> KindLabels => LibraryListing.KindLabels;
    public IReadOnlyList<string> CategoryLabels => LibraryListing.CategoryLabels;
    public IReadOnlyList<string> RatingLabels => LibraryListing.RatingLabels;

    // ---- the sort -----------------------------------------------------------------------------------------

    [Reactive] private string _sortLabel = "Name";

    /// <summary>Reverse the sort. One toggle for all three orders rather than three-way header clicking: the list
    /// is a ListBox with a header row this file's view draws, so there is nothing to click, and "Rating,
    /// descending" is what a user wants of a rating every time.</summary>
    [Reactive] private bool _descending;

    public IReadOnlyList<string> SortLabels => LibraryListing.SortLabels;

    // ---- the editor ---------------------------------------------------------------------------------------

    [Reactive] private string _editName = "";
    [Reactive] private string _editCategoryLabel = LibraryListing.NoCategory;
    [Reactive] private string _editTags = "";
    [Reactive] private string _editNotes = "";
    [Reactive] private bool _editFavourite;

    /// <summary>The stars. A type of its own because the save dialog wants the same five -- see
    /// <see cref="RatingViewModel"/>.</summary>
    public RatingViewModel EditRating { get; } = new();

    public IReadOnlyList<string> EditCategoryLabels => LibraryListing.EditCategoryLabels;

    public bool HasSelection => SelectedEntry is not null;

    /// <summary>Whether the selected entry is a tone, which is the only thing that has a category. A Studio Set
    /// is sixteen parts each with one of their own, so the drop-down is disabled rather than hidden for one: the
    /// row still shows what the file says, which matters for a hand-edited file that has a category it should
    /// not.</summary>
    public bool SelectedIsTone => SelectedEntry?.Entry.Head.Kind == SnapshotKinds.Tone;

    /// <summary>Whether Save changes can do anything. The name is the one field that cannot be cleared: an entry
    /// with no name is a row the user cannot tell from the one above it, and the file it names may be their only
    /// copy of that sound. <c>SnapshotLibrary</c> refuses a blank name as well -- this is the half that stops the
    /// user reaching a refusal, and that is the half that stops it being reported as an error.</summary>
    public bool CanSaveChanges => HasSelection && EditName.Trim().Length > 0;

    // ---- reading and filtering ----------------------------------------------------------------------------

    /// <summary>Re-read the folder and rebuild the list. Called on construction, after every write, and by the
    /// Refresh button -- which exists because nothing watches the folder.
    ///
    /// <b>A folder that cannot be listed is reported and leaves an empty list.</b> A missing folder is not that
    /// case: <c>SnapshotLibrary.Read</c> answers empty for one, because that is the normal state of the default
    /// library folder until the first save. What throws is a folder whose contents were refused -- a share the
    /// user has lost access to -- and telling them "your library is empty" would send them looking for files that
    /// are exactly where they left them.</summary>
    public void Refresh()
    {
        try
        {
            _all = SnapshotLibrary.Read(Folder);
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"list the library folder '{Folder}'", e.ToString());
            _all = [];
            _report($"Could not read the library folder: {e.Message}", true);
        }

        RebuildTags();
        ApplyFilter();
    }

    /// <summary>The tag checkboxes, from the tags the files carry. Ticks survive a refresh by name, so annotating
    /// a sound does not silently widen a filter the user set two minutes ago -- and a tag that no longer exists
    /// anywhere loses its tick along with its row, which is the only honest thing to do with a filter that can no
    /// longer match anything.</summary>
    private void RebuildTags()
    {
        var ticked = Tags.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Tags.Clear();
        foreach (var tag in LibraryListing.AllTags(_all))
            Tags.Add(new LibraryTagViewModel(tag, ticked.Contains(tag), ApplyFilter));
        this.RaisePropertyChanged(nameof(HasTags));
    }

    /// <summary>Rebuild <see cref="Entries"/> from <see cref="_all"/> through the filter and the sort, and put the
    /// selection back on the same file if it is still admitted.
    ///
    /// The selection is restored by path because the rows are new objects: the one that was selected no longer
    /// exists. It comes back as null when the file is filtered out, which is right -- the row is not there to be
    /// selected -- and the editor clears with it.</summary>
    private void ApplyFilter()
    {
        var filter = new LibraryFilter(
            SearchText,
            LibraryListing.KindFromLabel(KindLabel),
            LibraryListing.CategoryFromLabel(CategoryLabel),
            LibraryListing.MinimumRatingFromLabel(RatingLabel),
            FavouritesOnly,
            Tags.Where(t => t.IsSelected).Select(t => t.Name).ToList());

        var admitted = LibraryListing.Sort(filter.Apply(_all), LibraryListing.SortFromLabel(SortLabel), Descending);

        var selectedPath = SelectedEntry?.FilePath;
        Entries.Clear();
        foreach (var entry in admitted) Entries.Add(new LibraryEntryViewModel(entry));
        SelectedEntry = Entries.FirstOrDefault(row =>
            string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));

        Summary = _all.Count == 0
            ? "Nothing in this folder yet."
            : Entries.Count == _all.Count
                ? $"{_all.Count} snapshot{(_all.Count == 1 ? "" : "s")}."
                : $"{Entries.Count} of {_all.Count} snapshots.";
    }

    /// <summary>Put the selected entry's metadata into the editor -- or clear it when nothing is selected. Every
    /// field, including the empty ones: a box left holding the previous selection's notes is a box whose Save
    /// would write them onto this sound.</summary>
    private void ShowSelected()
    {
        var head = SelectedEntry?.Entry.Head;
        EditName = head?.Name ?? "";
        EditCategoryLabel = LibraryListing.EditLabelForCategory(head?.Category);
        EditTags = head is null ? "" : LibraryListing.FormatTags(head.Tags);
        EditNotes = head?.Notes ?? "";
        EditRating.Value = head?.Rating ?? 0;
        EditFavourite = head?.Favourite ?? false;
    }

    // ---- the commands -------------------------------------------------------------------------------------

    /// <summary>Write the editor's fields back into the selected file, and re-read the folder so the list shows
    /// what the file now says rather than what was typed.
    ///
    /// <b>One write path.</b> Everything here goes through <c>SnapshotLibrary.WriteMetadata</c>, including the
    /// name -- which that method learned as one more field for exactly this editor. Nothing in this file opens a
    /// snapshot, and nothing in it writes one: it cannot rewrite a parameter value, because it never holds
    /// one.
    ///
    /// <b>Renaming changes the name inside the file and not the file's own name.</b> Moving a user's file under
    /// them is a bigger thing than editing a field, it breaks anything else pointing at the path, and the browser
    /// lists what is inside the file anyway.</summary>
    public void SaveChanges()
    {
        UserActionLog.Action("button: Save changes (library)");
        var entry = SelectedEntry;
        if (entry is null || !CanSaveChanges) return;

        try
        {
            SnapshotLibrary.WriteMetadata(entry.FilePath, new SnapshotMetadata(
                LibraryListing.CategoryToWrite(EditCategoryLabel),
                LibraryListing.ParseTags(EditTags),
                EditNotes,
                EditRating.Value,
                EditFavourite,
                EditName.Trim()));

            _report($"Saved the changes to {Path.GetFileName(entry.FilePath)}.", false);
            Refresh();
        }
        catch (Exception e)
        {
            // Including SnapshotFormatException, whose message is written for the user: a file this build cannot
            // open cannot be annotated either, and saying so names the file they are looking at.
            UserActionLog.Failed($"save the metadata of '{entry.FilePath}'", e.ToString());
            _report($"Could not save the changes: {e.Message}", true);
        }
    }

    /// <summary>Send the selected snapshot to the instrument, through the window's own restore paths. A tone still
    /// refuses to load into a part holding a different engine, with the message it already gives -- that guard is
    /// <c>StudioSetSnapshotService.RestoreToneAsync</c>'s and this does not repeat it.</summary>
    public async Task LoadSelectedAsync()
    {
        UserActionLog.Action("button: Load (library)");
        var entry = SelectedEntry?.Entry;
        if (entry is null) return;
        await _load(entry);
    }

    /// <summary>Choose a different library folder, list it, and remember it.
    ///
    /// <b>In that order.</b> The folder the user picked is shown and read before anything is written to disk, so a
    /// settings file that cannot be saved costs them the memory of the choice and not the choice itself -- they
    /// are looking at the new library either way, and the failure says which part failed. <c>LibrarySettings.Save
    /// </c> throws rather than swallowing, deliberately, for this reason: silently forgetting the choice would
    /// surface as the folder having reverted at the next launch, with nothing to connect it to.</summary>
    public async Task ChangeFolderAsync()
    {
        UserActionLog.Action("button: Change library folder");
        var chosen = await _pickFolder(Folder);
        if (chosen is null) return; // cancelled -- nothing happened, so say nothing
        if (chosen.Length == 0)
        {
            // A folder was chosen but has no usable local path (a cloud or virtual location). Unlike a
            // cancellation, the user needs to know this did nothing.
            _report("Could not use that folder: it has no accessible local path.", true);
            return;
        }

        Folder = chosen;
        Refresh();

        try
        {
            LibrarySettings.Save(_settingsPath, chosen);
            _report($"The library folder is now {chosen}.", false);
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"remember the library folder '{chosen}'", e.ToString());
            _report($"Using {chosen}, but it could not be remembered for next time: {e.Message}", true);
        }
    }

    /// <summary>Put every filter back to "not asking". A single button because there are six of them and finding
    /// the one that is hiding what you are looking for is the whole difficulty of a filtered list.</summary>
    public void ClearFilters()
    {
        UserActionLog.Action("button: Clear library filters");
        SearchText = "";
        KindLabel = LibraryListing.AnyKind;
        CategoryLabel = LibraryListing.AnyCategory;
        RatingLabel = LibraryListing.AnyRating;
        FavouritesOnly = false;
        foreach (var tag in Tags) tag.Clear();
        ApplyFilter();
    }

    /// <summary>Write a freshly captured snapshot into the library, list it, and select it. Answers where it went.
    ///
    /// Called by the window's Save Studio Set and Save Tone, which is why it takes an in-memory snapshot when
    /// nothing else here will: it is the one operation whose subject genuinely is the instrument's current state
    /// rather than a file. Annotating an existing file is <see cref="SaveChanges"/>, which is handed no snapshot
    /// at all -- see <c>SnapshotLibrary.WriteMetadata</c> for why that distinction is enforced by the signatures.
    ///
    /// Throws, rather than reporting: the caller is inside a capture with its own try/catch and its own status
    /// message naming which of the two things it was saving, and two layers reporting the same failure would say
    /// it twice.</summary>
    public string SaveIntoLibrary(Integra7Snapshot snapshot, SnapshotMetadata metadata)
    {
        var path = SnapshotLibrary.Create(Folder, snapshot, metadata);
        Refresh();
        SelectedEntry = Entries.FirstOrDefault(row =>
            string.Equals(row.FilePath, path, StringComparison.OrdinalIgnoreCase));
        return path;
    }
}

/// <summary>One tag in the filter bar, with its checkbox.
///
/// A view model rather than a plain string in a multi-select list because Avalonia's <c>SelectedItems</c> is not
/// something a compiled binding can carry two-way, and a selection this application cannot see is a filter it
/// cannot apply. A checkbox per tag also says out loud what the alternative hides: the ticks are AND-ed, so
/// ticking two tags asks for the sounds carrying both (see <see cref="LibraryFilter.Tags"/>).
///
/// No ToolTip, per the rule this branch keeps for anything clicked repeatedly.</summary>
public sealed class LibraryTagViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _isSelected;

    internal LibraryTagViewModel(string name, bool isSelected, Action changed)
    {
        Name = name;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            this.RaiseAndSetIfChanged(ref _isSelected, value);
            _changed();
        }
    }

    /// <summary>Untick without re-filtering, for a caller that is about to re-filter once for all of them. Six
    /// filters cleared one at a time is six passes over the folder, and the fifth of them is a list the user
    /// never sees.</summary>
    internal void Clear() => this.RaiseAndSetIfChanged(ref _isSelected, false, nameof(IsSelected));
}
