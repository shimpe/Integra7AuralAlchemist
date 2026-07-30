using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
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
/// cref="LoadAsync"/> needs the device, and it asks the window, which already knows whether there is one.
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

    /// <summary>Ask the user a yes/no question: the message, and what the affirmative button says. A callback
    /// for the same reason <see cref="_pickFolder"/> is one: this view model is inside a tab, the dialog
    /// belongs to the window, and a view model that reached for a window could not be constructed without
    /// one.
    ///
    /// <b>The label is a parameter because there are now two questions.</b> It used to be fixed at "Delete",
    /// which was honest while deleting was the only thing here that asked -- and became a dialog inviting the
    /// user to press Delete to confirm a restore the moment a second question existed.</summary>
    private readonly Func<string, string, Task<bool>> _confirm;

    /// <summary>Hand this entry to the Compare tab. A callback for the same reason the others are: this view
    /// model knows nothing about its neighbours.
    ///
    /// Task-returning like <see cref="_load"/>, because filling a slot means reading and parsing the file,
    /// and a Studio Set snapshot is large enough that doing it synchronously on the click stalls the
    /// window visibly.</summary>
    private readonly Func<LibraryEntry, Task> _compare;

    /// <summary>Hand two entries to the Compare tab, replacing whatever it holds.
    ///
    /// <b>Separate from <see cref="_compare"/> rather than two calls to it.</b> That one fills whichever
    /// slot is free, which is right when the user is building a comparison one snapshot at a time -- but
    /// with both slots already full, calling it twice would replace the left one twice and show the second
    /// selected snapshot against a stranger. Asking for two is asking for exactly those two.</summary>
    private readonly Func<LibraryEntry, LibraryEntry, Task> _compareTwo;

    /// <summary>Hear this entry in the selected part, or stop hearing it. The window's job for the reason
    /// <see cref="_load"/> is its job -- it holds the API, it knows which part is selected and what engine
    /// that part holds -- and additionally because it is the window that owns the one session: whether a
    /// press means start, play something else, or stop is a question about what is already playing, which
    /// nothing in this file knows.</summary>
    private readonly Func<LibraryEntry, Task> _audition;

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

    /// <summary>Which library file is the init tone for each engine, keyed by tone type. Held here
    /// rather than re-read on every use because it is also what the "Use as the init tone" button
    /// edits.</summary>
    private Dictionary<string, string> _initTones = [];

    /// <summary>What the last deep search found, and the exact question it answers, or null for "nobody has
    /// looked".
    ///
    /// <b>Held here because <see cref="ApplyFilter"/> has to stay synchronous.</b> It is called from the
    /// constructor, from every refresh and from every tag checkbox, and reading a folder from any of those is
    /// not something a filter may do. So the read is <see cref="SearchInsideAsync"/>'s, once, when the user
    /// asks, and what it found is consulted by the filter without touching a file.
    ///
    /// <b>It is only ever used where it is evidence</b> -- for the same text, over files it actually opened,
    /// and only while those files have not been written since. All three rules are <see cref="DeepSearch"/>'s,
    /// with the reasoning and the tests; what is left here is dropping the answer and saying so when they
    /// fail.</summary>
    private DeepSearchAnswer? _deepSearch;

    /// <summary>What every duplicate scan has already read, so that a second scan of a folder nobody has
    /// touched opens no file at all. <see cref="SnapshotVectorCache"/> carries the reasoning, including why
    /// the library deliberately has no index and why this one is nonetheless worth having.
    ///
    /// Touched from a thread-pool thread, which is safe for exactly one reason: only one scan runs at a time
    /// (see <see cref="_scanning"/>), and nothing on the UI thread reads it. It is handed to the static scan
    /// method as an argument rather than reached for through <c>this</c>, so that the method obeys the same
    /// rule the deep search's does -- nothing in it touches this view model's state.</summary>
    private readonly SnapshotVectorCache _vectors = new();

    /// <summary>Whether a duplicate scan is running right now, so that only one can be. The reasoning is
    /// <see cref="_searching"/>'s, plus one of its own: the scan and the cache it folds its reads into are not
    /// safe to run twice at once, and a second scan would be reading a folder the first is still
    /// answering for.</summary>
    private bool _scanning;

    /// <summary>Whether something asked for a duplicate scan while one was already running.
    ///
    /// <b>A declined request used to be a dropped one</b>, and that was how the panel came to hold an answer
    /// about a folder nobody was looking at: changing folder mid-scan refreshed the list, asked for a scan,
    /// was told "still looking", and nothing ever asked again -- so the groups from the old folder stayed on
    /// screen with a live Delete button. The rows are now cleared when a scan starts, so there is nothing
    /// stale to act on either way; this is what makes the panel fill up again afterwards instead of sitting
    /// empty. A plain bool because all of these gestures arrive on the UI thread, and because two requests
    /// during one scan want the same thing.</summary>
    private bool _rescanWanted;

    /// <summary>How many times the folder has been re-listed. Nothing reads the number; a scan captures it
    /// and compares it afterwards, and any difference means the folder is not what it was.
    ///
    /// <b>It counts refreshes rather than writes because every write here ends in one</b> -- annotating,
    /// renaming, restoring a version, a bulk change, a delete, a capture saved from another tab -- so
    /// <see cref="Refresh"/> is the single place that already knows the library has moved on. The Refresh
    /// button counts too, which is right: it means "the folder may have changed" and a scan reading through
    /// one that has cannot answer for it.</summary>
    private int _libraryVersion;

    /// <summary>Whether the duplicate panel is up. Not a filter and not a selection -- it is the one panel
    /// that is about the whole folder rather than about what is selected, so it is opened by a button and
    /// closed by one.</summary>
    private bool _findingDuplicates;

    /// <summary>Whether a folder is being read right now, so that only one read can be.
    ///
    /// <b>Two searches at once is not a performance question.</b> A held Enter repeats -- Avalonia raises
    /// KeyDown on the keyboard's auto-repeat -- so without this a leant-on key starts a folder read per
    /// repeat, each opening every candidate file; and two answers in flight would race to decide which
    /// question <see cref="_deepSearch"/> ends up claiming to answer, settled by whichever finished last. A
    /// plain bool is enough because all three gestures arrive on the UI thread, and nothing else in this file
    /// serialises long operations.</summary>
    private bool _searching;

    /// <param name="load">See <see cref="_load"/>.</param>
    /// <param name="pickFolder">See <see cref="_pickFolder"/>.</param>
    /// <param name="confirm">See <see cref="_confirm"/>.</param>
    /// <param name="compare">See <see cref="_compare"/>.</param>
    /// <param name="compareTwo">See <see cref="_compareTwo"/>.</param>
    /// <param name="audition">See <see cref="_audition"/>.</param>
    /// <param name="report">See <see cref="_report"/>.</param>
    /// <param name="settingsPath">See <see cref="_settingsPath"/>.</param>
    public LibraryViewModel(Func<LibraryEntry, Task> load, Func<string, Task<string?>> pickFolder,
        Func<string, string, Task<bool>> confirm, Func<LibraryEntry, Task> compare,
        Func<LibraryEntry, LibraryEntry, Task> compareTwo, Func<LibraryEntry, Task> audition,
        Action<string, bool> report, string settingsPath)
    {
        _load = load;
        _pickFolder = pickFolder;
        _confirm = confirm;
        _compare = compare;
        _compareTwo = compareTwo;
        _audition = audition;
        _report = report;
        _settingsPath = settingsPath;

        // Before the subscriptions below, so that the first filter runs against the real folder rather than
        // against "" -- which resolves to the process's current directory and would list whatever is beside the
        // executable.
        var preferences = LibrarySettings.LoadAll(settingsPath);
        _folder = preferences.Folder;
        _initTones = new Dictionary<string, string>(preferences.InitTones);

        // Before the subscriptions and before the first Refresh, both of which reach for it: one feeds it the
        // selection, and the other tells it the init-tone marks have moved.
        Editor = new LibraryEditorViewModel(SaveChangesAsync, LoadAsync, CompareAsync, DeleteAsync,
            MarkAsInitTone, RestoreVersionAsync, AuditionRowAsync);

        BulkEditor = new LibraryBulkEditViewModel(ApplyBulkChangeAsync, DeleteSelectionAsync,
            CompareSelectionAsync);

        // The pair compare is handed over as it stands: two snapshots is two snapshots whether they were
        // picked out of the list or out of a duplicate group, and the ordering rule CompareSelectionAsync
        // needs does not apply to tick boxes -- see DuplicateScanViewModel.Ticked.
        Duplicates = new DuplicateScanViewModel(RefreshAndScanAsync, DeleteDuplicatesAsync, _compareTwo,
            CloseDuplicates);

        // After BulkEditor is assigned, not before: this dereferences it, and a selection change arrives as
        // soon as the Refresh at the end of this constructor puts rows on screen.
        SelectedEntries.CollectionChanged += (_, _) =>
        {
            BulkEditor.Count = SelectedEntries.Count;
            BulkEditor.CountChanged();
            PanelChanged();
        };

        // Every filter and both halves of the sort, in one subscription. Seven properties rather than seven
        // subscriptions because they all do the same thing and doing it once is what stops a change of two of
        // them at a time from filtering twice. WhenAnyValue fires on subscription, so this is also what performs
        // the first filter -- there is no separate initial call to forget.
        //
        // Not throttled, unlike the preset grids' search boxes: those filter ~6,000 presets through DynamicData
        // and this filters a folder, which is tens or hundreds of entries of six string comparisons each. A
        // keystroke's worth of that is not measurable, and a throttle would make the box feel like it was
        // catching up.
        this.WhenAnyValue(x => x.SearchText, x => x.KindLabel, x => x.EngineLabel, x => x.CategoryLabel,
                x => x.RatingLabel, x => x.FavouritesOnly, x => x.SortLabel, x => x.Descending,
                (_, _, _, _, _, _, _, _) => Unit.Default)
            .Subscribe(_ => ApplyFilter());

        // Looking inside patches, on its own subscription and deliberately not in the one above: those
        // properties re-filter over heads already in memory, and this one reads files. Ticking the box is the
        // user asking, and unticking it is the user asking for the rows it added to go -- both are this same
        // method, which decides which of the two it was (see SearchInsideAsync).
        //
        // On the property rather than on the checkbox's Command, because the search reads this property to
        // decide which of those two things it is being asked for: a Command on a ToggleButton fires as part
        // of the click, and whether the new IsChecked has reached here by then is Avalonia's business and not
        // visible from this file. A property change cannot be early. Skip(1) because WhenAnyValue opens with
        // the current value, and a box that has never been ticked is not a request.
        //
        // Nothing awaits it: a subscription cannot, and there is nothing to wait for -- the method reports
        // its own outcome and logs its own failures. The parameter is named rather than discarded because
        // "_" here would be the parameter, and assigning the task to it is what the compiler would read.
        this.WhenAnyValue(x => x.SearchInsidePatches)
            .Skip(1)
            .Subscribe(ticked => _ = SearchInsideAsync());

        // The Search inside button follows both halves of "is there a search to run". Its own subscription
        // because it is the only thing either property does that is not a filter: one of them re-filters
        // (above), the other reads a folder (also above), and this only greys out a button.
        this.WhenAnyValue(x => x.SearchInsidePatches, x => x.SearchText, (_, _) => Unit.Default)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CanSearchInside)));

        // The panel follows the selection. The flags it raises are its own; this only tells it what to
        // describe.
        this.WhenAnyValue(x => x.SelectedEntry).Subscribe(row => Editor.Selected = row);

        Refresh();
    }

    // ---- what is in the folder ----------------------------------------------------------------------------

    /// <summary>The rows on screen: what the filter admitted, in the order the sort asked for.</summary>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; } = [];

    /// <summary>Which row is selected, or null. Two-way from the list, and assigned from here after a refresh --
    /// see <see cref="Refresh"/>.</summary>
    [Reactive] private LibraryEntryViewModel? _selectedEntry;

    /// <summary>Every selected row. Avalonia fills this collection as the selection changes; nothing here
    /// assigns it, which is why it is get-only and the binding is not two-way.</summary>
    public ObservableCollection<LibraryEntryViewModel> SelectedEntries { get; } = [];

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

    /// <summary>Which engine, or all of them. Beside the kind rather than among the annotations because it is
    /// the same sort of question -- what the file is, not what the user has said about it -- and because
    /// choosing one is how a library big enough to need filtering gets down to the tones that can go in one
    /// part.</summary>
    [Reactive] private string _engineLabel = LibraryListing.AnyEngine;

    [Reactive] private string _categoryLabel = LibraryListing.AnyCategory;
    [Reactive] private string _ratingLabel = LibraryListing.AnyRating;
    [Reactive] private bool _favouritesOnly;

    /// <summary>Whether the search box also asks what is inside a patch, rather than only what has been said
    /// about it.
    ///
    /// <b>Deliberately not among the seven the constructor watches.</b> Those re-filter on every keystroke over
    /// heads that are already in memory; this one reads every candidate file in the folder, and a search that
    /// did that per keystroke would be a folder read per letter. It runs when the user asks -- see
    /// <see cref="SearchInsideAsync"/>, which is what the box, the button and Enter all reach.</summary>
    [Reactive] private bool _searchInsidePatches;

    /// <summary>Whether there is a deep search to run: the box is ticked and there is something to look for.
    ///
    /// The button's <c>IsEnabled</c>. It watches both halves because a button that can be pressed and answers
    /// "nothing happened" is worse than one that is visibly not offering -- and the empty search box is the
    /// half that was missing while this was bound to the checkbox alone.</summary>
    public bool CanSearchInside => SearchInsidePatches && SearchText.Trim().Length > 0;

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
    public IReadOnlyList<string> EngineLabels => LibraryListing.EngineLabels;
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

    /// <summary>The panel beside the list. Built once, and told which row is selected -- see
    /// <see cref="LibraryEditorViewModel"/> for why the editor is not in this file.</summary>
    public LibraryEditorViewModel Editor { get; }

    /// <summary>The panel shown instead of <see cref="Editor"/> when more than one row is selected.</summary>
    public LibraryBulkEditViewModel BulkEditor { get; }

    /// <summary>The panel shown instead of either of the other two while the user is looking for duplicates.
    /// </summary>
    public DuplicateScanViewModel Duplicates { get; }

    /// <summary>Which of the three panels the view shows -- one flag each, rather than one value the view
    /// compares against.
    ///
    /// <b>Three booleans because there are now three panels, and a boolean can only choose between two.</b>
    /// This was <c>IsBulkSelection</c>, and the view showed the editor on <c>!IsBulkSelection</c>. An enum
    /// would be the tidier model and is unusable here: a compiled binding cannot compare a value against a
    /// constant, so it would need a converter per panel and the check the build performs on every other
    /// binding in this application would be lost on exactly the three that decide what is on screen.
    ///
    /// <b>Each is written so that no two can be true.</b> The duplicate panel wins outright -- it was asked
    /// for by name and is about the whole folder, so a click in the list behind it must not take it away
    /// mid-scan -- and the other two split what is left on the count that used to decide everything. Three
    /// positive expressions rather than a negation in the view, so that the one place the rule lives is
    /// here.</summary>
    public bool ShowsDuplicates => _findingDuplicates;

    /// <inheritdoc cref="ShowsDuplicates"/>
    public bool ShowsBulkEditor => !_findingDuplicates && SelectedEntries.Count > 1;

    /// <inheritdoc cref="ShowsDuplicates"/>
    public bool ShowsEditor => !_findingDuplicates && SelectedEntries.Count <= 1;

    /// <summary>Say that which panel is up may have changed. Called from the two things that can move it: the
    /// selection, and the duplicate panel being opened or closed. All three are raised together every time,
    /// because working out which of them actually moved would be three conditions that have to agree with the
    /// three expressions above.</summary>
    private void PanelChanged()
    {
        this.RaisePropertyChanged(nameof(ShowsDuplicates));
        this.RaisePropertyChanged(nameof(ShowsBulkEditor));
        this.RaisePropertyChanged(nameof(ShowsEditor));
    }

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

        // After the read, so that a scan capturing this has a listing at least as new as the number. See
        // _libraryVersion: this is the one place that knows the folder has been looked at again, and every
        // write in this file ends here.
        _libraryVersion++;

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
        var filter = CurrentFilter();

        // The deep pass widens the text axis and nothing else. An entry is admitted when it passes every
        // other axis AND the text matches its metadata OR any of its parameter values -- so ticking the box
        // can only ever add rows, which is what a user expects of a checkbox that says "look inside
        // patches too". The arithmetic is DeepSearch's, where it has tests; LibraryFilter is asked twice
        // there and stays pure over heads.
        //
        // Nothing is read from disk here. This runs on every keystroke, and what the last search found is
        // evidence about the files it opened -- see DeepSearch.Answers for when that is still evidence about
        // what is being asked, and why the rule is the files rather than the filter that chose them.
        var listing = DeepSearch.Widen(filter, _all, SearchInsidePatches ? _deepSearch : null);

        // An answer that does not cover what is being asked is dropped here rather than left lying about to
        // be ignored, and it is said out loud. Widening a filter after a search -- the engine back to any, a
        // lower minimum rating, a tag unticked -- asks about files that were never opened, and so does a
        // folder that has gained one; the difference between "no patch says supersaw inside" and "the ones I
        // read did not" is the whole value of the feature. Silence would be indistinguishable from an answer.
        if (listing.AnswersAnotherQuestion)
        {
            _deepSearch = null;
            // "The search", not "the filters": the text in the box is one of the seven axes, so typing
            // another letter lands here as well, and a message about filters would send a user who has just
            // typed one looking at the drop-downs.
            _report("The search has changed since you looked inside the patches; " +
                    "press Search inside again.", false);
        }
        // A file written since it was read is no longer the file that was read: a version restored, a patch
        // saved again, or a name freed by a delete and handed to the next save (SnapshotLibrary.UniquePath
        // only avoids a name that is taken at the time). Its row loses its reason and, if that was all it
        // had, leaves the list -- so why it left is said, and the hit is forgotten so it is said once.
        else if (listing.Changed.Count > 0 && _deepSearch is not null)
        {
            _deepSearch = _deepSearch with
            {
                Hits = [.._deepSearch.Hits.Where(hit => !listing.Changed.Contains(hit.FilePath))],
            };
            _report($"{listing.Changed.Count} of the patches found inside " +
                    $"{(listing.Changed.Count == 1 ? "has" : "have")} changed since; " +
                    "press Search inside again to take another look.", false);
        }

        var admitted = LibraryListing.Sort(listing.Admitted, LibraryListing.SortFromLabel(SortLabel),
            Descending);

        var selectedPath = SelectedEntry?.FilePath;
        // Every selected path, not only the anchor's. Rebuilding the list empties the control's selection
        // outright -- measured, not assumed: the collection reports Remove down to zero as Entries is
        // cleared -- and a bulk edit ends in a refresh, so without this a user who had just annotated
        // fourteen snapshots would have to select them all again to do anything else to them.
        var selectedPaths = SelectedEntries.Select(row => row.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Entries.Clear();
        // Every row is asked for its reason rather than only the ones the deep pass added, because by the
        // time it gets here there is one list and not two: the pass's job was to widen the listing, not to
        // keep a second one beside it. A row with nothing to explain gets "".
        foreach (var entry in admitted)
            Entries.Add(new LibraryEntryViewModel(entry)
                { MatchedInside = listing.Reasons.GetValueOrDefault(entry.FilePath, "") });
        // Before the selection is restored, not after: the panel's init-tone note reads the selected row's own
        // mark, so a row marked afterwards would be handed to the panel unmarked and the panel would say
        // nothing about a tone the list is already flagging.
        ApplyInitToneMarks();
        SelectedEntry = Entries.FirstOrDefault(row =>
            string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));

        // After the anchor, because assigning it is itself what puts the first row back into the control's
        // selection; adding it a second time would leave a duplicate the batch loop would then write twice.
        // A row the filter no longer admits is simply not put back, which is right: it is not on screen to
        // be acted on.
        foreach (var row in Entries.Where(row => selectedPaths.Contains(row.FilePath)))
            if (!SelectedEntries.Contains(row))
                SelectedEntries.Add(row);

        Summary = _all.Count == 0
            ? "Nothing in this folder yet."
            : Entries.Count == _all.Count
                ? $"{_all.Count} snapshot{(_all.Count == 1 ? "" : "s")}."
                : $"{Entries.Count} of {_all.Count} snapshots.";
    }

    /// <summary>The seven axes as the controls stand right now.
    ///
    /// A method rather than a construction in each of the two places that need one -- the filter and the deep
    /// search -- because <see cref="LibraryFilter"/> is a seven-argument positional record of mostly strings,
    /// and its own remarks say what goes wrong when two such constructions have to agree and somebody edits
    /// one of them.</summary>
    private LibraryFilter CurrentFilter() => new(
        SearchText,
        LibraryListing.KindFromLabel(KindLabel),
        LibraryListing.CategoryFromLabel(CategoryLabel),
        LibraryListing.MinimumRatingFromLabel(RatingLabel),
        FavouritesOnly,
        Tags.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
        LibraryListing.EngineFromLabel(EngineLabel));

    /// <summary>Read the patches the other filters admit and ask whether the search text is anywhere inside
    /// them. What is found is remembered and the list is rebuilt around it.
    ///
    /// <b>What is read, and what is not.</b> Only the entries the other six axes admit and the text did not:
    /// a row already on screen is on screen whatever is inside it, and a row the kind or the engine filter
    /// excluded is not wanted at any price. So the narrower the other filters, the less this reads -- and a
    /// user who has narrowed to one engine has narrowed the folder read too.
    ///
    /// <b>It runs when it is asked and not on a keystroke</b> -- see <see cref="SearchInsidePatches"/>. Three
    /// gestures reach this one method: ticking the box (through the constructor's subscription to it), the
    /// button beside it, and Enter in the search box. Unticking the box reaches it too, and falls into the
    /// branch that drops what the last search found -- which is what puts the list back, and the reason
    /// unticking is not simply left to do nothing.
    ///
    /// <b>The scan is off the UI thread</b>, for the reason the bulk loops give: it opens and streams every
    /// candidate file in the folder, and doing that on the click is a freeze with nothing on screen to
    /// explain it. Only the candidate entries and the text cross the thread -- both immutable records -- and
    /// only a list of hits comes back. One read runs at a time; see <see cref="_searching"/>.
    ///
    /// <b>An answer nobody is waiting for is discarded rather than shown.</b> The filters, the folder and the
    /// box itself can all change while the folder is being read, and each of them makes the answer about a
    /// question that is no longer being asked. Only one read runs at a time, so a discard is never overwriting
    /// a newer answer -- which is what lets it say something when the user is owed an explanation and stay
    /// quiet when they are not.
    ///
    /// <b>Everything is guarded, not only the per-file read.</b> This is invoked by a command binding and by
    /// two callers that cannot await it, none of which observes the task it gets, so an exception anywhere in
    /// here -- building a filter, reporting, rebuilding the list -- would otherwise be silence with a
    /// half-rebuilt list behind it. Every other method in this file that touches files reports its failures
    /// the same way.</summary>
    public async Task SearchInsideAsync()
    {
        try
        {
            await LookInsideAsync();
        }
        catch (Exception e)
        {
            UserActionLog.Failed("look inside the library's patches", e.ToString());
            _report($"Could not look inside the patches: {e.Message}", true);
        }
    }

    /// <summary>The body of <see cref="SearchInsideAsync"/> -- a method of its own only so that the public one
    /// can be nothing but the guard around it.</summary>
    private async Task LookInsideAsync()
    {
        var text = SearchText.Trim();
        UserActionLog.Action($"library: look inside patches for \"{text}\" " +
                             $"({(SearchInsidePatches ? "on" : "off")})");

        if (!SearchInsidePatches)
        {
            // Unticking the box: what the last search found stops applying, and the rows it added go. This
            // is the reason unticking is not simply left to do nothing -- nothing re-filters on this
            // property, so the rows would otherwise sit there contradicting the box.
            if (_deepSearch is not null)
            {
                _deepSearch = null;
                ApplyFilter();
                return;
            }

            // Nothing to drop, so nothing moved, so something has to be said: Enter in the search box
            // without the box ticked would otherwise be a dead key -- the handler marks it handled -- and
            // "nothing happened" is indistinguishable from "it did not work".
            if (text.Length > 0)
                _report("Tick \"Look inside patches\" to search inside them as well.", false);
            return;
        }

        if (text.Length == 0)
        {
            // Ticked with nothing to look for. Whatever an earlier search found does not apply either: the
            // text it answered has been deleted.
            if (_deepSearch is not null)
            {
                _deepSearch = null;
                ApplyFilter();
            }

            _report("Type what to look for, then press Search inside.", false);
            return;
        }

        // One read at a time -- see _searching for what a leant-on Enter key does otherwise.
        if (_searching)
        {
            _report("Still looking inside the patches…", false);
            return;
        }

        var filter = CurrentFilter();
        var candidates = DeepSearch.Candidates(filter, _all);

        if (candidates.Count == 0)
        {
            // Said rather than passed over in silence: the user pressed something and no file is going to be
            // opened. Whatever an earlier search found is left alone -- it either still answers the question
            // on screen or has already been dropped, and this branch has learnt nothing to change either.
            _report(filter.Apply(_all).Count == 0
                ? "The other filters admit nothing, so there is nothing to look inside."
                : "Every patch the other filters admit already matches; nothing left to look inside.", false);
            return;
        }

        // The folder is captured because it can be changed while this is reading, and an answer about
        // another folder is not an answer at all.
        var folder = Folder;
        _report($"Looking inside {candidates.Count} patch{(candidates.Count == 1 ? "" : "es")}…", false);

        _searching = true;
        List<DeepSearchHit> hits;
        int unreadable;
        try
        {
            // Off the UI thread. Only the entries and the text cross, and both are immutable; only a list of
            // hits comes back.
            (hits, unreadable) = await Task.Run(() => Scan(candidates, text));
        }
        finally
        {
            // In a finally, and before the answer is adopted: an adoption that threw would otherwise leave
            // the flag set and the feature dead for the rest of the session.
            _searching = false;
        }

        // Back on the UI thread: the await resumed on the context the gesture arrived on. Nothing below
        // awaits, so no other gesture can interleave with it.
        //
        // Quietly, both of these: the user has unticked the box or moved to another folder, so the rows this
        // would have added are not rows they are waiting for, and there is nothing for them to do about it.
        if (!SearchInsidePatches) return;
        if (!string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase)) return;

        // A search changed while reading is worth saying, because the rows the user *is* waiting for are not
        // going to appear. Nothing newer has been adopted in the meantime -- only one read runs at a time --
        // so this cannot be contradicting a status line that is already right.
        //
        // Asked of the answer rather than of the filter, so that the one rule about what an answer covers is
        // applied in one place: a filter *narrowed* while reading leaves the answer perfectly good, and
        // refusing it here would throw a folder read away for the gesture a user is likeliest to make while
        // waiting for one.
        var answer = new DeepSearchAnswer(text, candidates.Select(entry => entry.FilePath).ToList(), hits);
        if (!DeepSearch.Answers(answer, CurrentFilter(), _all))
        {
            _report("The search changed while the patches were being read; press Search inside again.", false);
            return;
        }

        _deepSearch = answer;

        var missed = unreadable == 0 ? "" : $" {unreadable} could not be read.";
        _report(hits.Count == 0
            ? $"Nothing inside the other {candidates.Count} patches mentions \"{text}\".{missed}"
            : $"{hits.Count} more patch{(hits.Count == 1 ? "" : "es")} mention \"{text}\" inside.{missed}",
            unreadable > 0);

        ApplyFilter();
    }

    /// <summary>Open each candidate and ask whether <paramref name="text"/> is anywhere inside it. Answers the
    /// hits and how many files could not be read.
    ///
    /// Static, and handed everything it needs, because it runs on a thread-pool thread: nothing in here may
    /// touch this view model's state. The last-write time recorded against a hit is the listing's rather than
    /// the file's at this moment, which is the safe direction -- a file written between the listing and this
    /// read is a hit that the next refresh discards rather than one that outlives what it describes.
    ///
    /// <b>A file that cannot be read is counted as well as logged.</b> One snapshot held open by a sync client
    /// must not sink the search, for the reason the bulk loops give -- but a search that quietly missed the
    /// sound the user is looking for is worse than a slow one, so the count is reported.</summary>
    private static (List<DeepSearchHit> Hits, int Unreadable) Scan(IReadOnlyList<LibraryEntry> candidates,
        string text)
    {
        List<DeepSearchHit> hits = [];
        var unreadable = 0;

        foreach (var entry in candidates)
            try
            {
                using var file = File.OpenRead(entry.FilePath);
                // "Partial 1/OSC Wave = SuperSaw": the parameter and what it reads as, which is what makes a
                // hit explicable rather than something to be taken on trust.
                if (SnapshotTextScan.FirstMatch(file, text) is { } hit)
                    hits.Add(new DeepSearchHit(entry.FilePath, $"{hit.Path} = {hit.Value}", entry.Modified));
            }
            catch (Exception e)
            {
                UserActionLog.Failed($"search inside '{entry.FilePath}'", e.ToString());
                unreadable++;
            }

        return (hits, unreadable);
    }

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

        // The marks are not on the panel's own state, so nothing it holds has changed and it has no way to
        // know its note is stale.
        Editor.InitToneMarksChanged();
    }

    // ---- the commands -------------------------------------------------------------------------------------

    /// <summary>Write the panel's fields back into the file behind the given row, and re-read the folder so the
    /// list shows what the file now says rather than what was typed.
    ///
    /// <b>One write path.</b> Everything here goes through <c>SnapshotLibrary.WriteMetadata</c>, including the
    /// name -- which that method learned as one more field for exactly this editor. Nothing in this file opens a
    /// snapshot, and nothing in it writes one: it cannot rewrite a parameter value, because it never holds
    /// one.
    ///
    /// <b>Renaming changes the name inside the file and not the file's own name.</b> Moving a user's file under
    /// them is a bigger thing than editing a field, it breaks anything else pointing at the path, and the browser
    /// lists what is inside the file anyway.
    ///
    /// Synchronous work behind a <c>Task</c>: the callback is shaped for the panel, which awaits every one of
    /// them, and a write of a few hundred kilobytes is not worth a thread.</summary>
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

    /// <summary>Send the given snapshot to the instrument, through the window's own restore paths. A tone still
    /// refuses to load into a part holding a different engine, with the message it already gives -- that guard is
    /// <c>StudioSetSnapshotService.RestoreToneAsync</c>'s and this does not repeat it.</summary>
    private Task LoadAsync(LibraryEntryViewModel row) => _load(row.Entry);

    /// <summary>Send the given snapshot to the Compare tab, which fills whichever of its two slots is free. The
    /// comparison itself is that tab's job; this is only a way in from the list.</summary>
    private Task CompareAsync(LibraryEntryViewModel row) => _compare(row.Entry);

    /// <summary>Hear the given snapshot in the selected part, or stop hearing it. Nothing is decided here --
    /// see <see cref="_audition"/> for why the choice between the two belongs to the window.</summary>
    private Task AuditionRowAsync(LibraryEntryViewModel row) => _audition(row.Entry);

    /// <summary>Make the given entry the tone Init starts from for its engine. Stored as a file name
    /// relative to the library folder, so it follows the library if the folder moves.</summary>
    private void MarkAsInitTone(LibraryEntryViewModel row)
    {
        if (row.Entry.Head.ToneType is not { } toneType) return;

        _initTones[toneType] = Path.GetFileName(row.FilePath);
        try
        {
            LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(Folder, _initTones));
            _report($"Init Tone will start from {row.Name} for {toneType} tones.", false);
        }
        catch (Exception e)
        {
            // The in-memory map is left as it is: the user's intent is recorded for this session even
            // when the file could not be written, and the message says the setting will not survive.
            UserActionLog.Failed($"remember the init tone for '{toneType}'", e.ToString());
            _report($"Could not remember that: {e.Message} The mark applies until the application closes.",
                true);
        }

        // Over every row rather than this one: the mark is per engine, so giving it to this tone takes it from
        // whichever tone had it. It is also what tells the panel its note has gone stale.
        ApplyInitToneMarks();
    }

    /// <summary>Remove the given snapshot from the library, after asking. It still asks, even though
    /// <see cref="PatchHistory"/> now keeps a copy: the row leaves the library, the mark on it is cleared,
    /// and getting it back means knowing the history folder exists.
    ///
    /// A mark pointing at the file goes with it. <c>InitToneResolution</c> copes with a stale mark by falling
    /// back to the bundled tone and saying so, but a mark the user can no longer see or clear is a trap,
    /// and this is the moment it is cheapest to tidy.</summary>
    private async Task DeleteAsync(LibraryEntryViewModel selected)
    {
        // The one action's log line that did not move to the panel with its button: this is where the file is
        // actually removed, and where the user can still say no.
        UserActionLog.Action("button: Delete from library");

        if (!await _confirm($"Delete \"{selected.Name}\" from the library? " +
                            $"The file {Path.GetFileName(selected.FilePath)} is removed, but a copy is " +
                            "kept in the history folder beside your library.", "Delete")) return;

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

        // Before the refresh, so the row that replaces the selection is built against the marks as they now
        // are.
        ForgetInitToneMarks([selected.FilePath]);

        Refresh();
        _report($"Deleted {selected.Name} from the library.", false);
    }

    /// <summary>Drop any init-tone mark pointing at a file that has just been deleted.
    ///
    /// <b>Shared by both delete paths</b>, which is the whole reason it is a method: deleting one snapshot
    /// cleared its mark and deleting fourteen did not, so the same act left the settings in two different
    /// states depending on how many rows had been selected.
    ///
    /// A stale mark is survivable -- <c>InitToneResolution</c> falls back to the bundled tone and says so --
    /// but it is a mark the user can no longer see or clear, which is a trap, and this is the moment it is
    /// cheapest to tidy. A failure to write the settings is logged and carried past: the snapshots are
    /// already gone, so refusing would undo nothing.</summary>
    private void ForgetInitToneMarks(IEnumerable<string> deletedPaths)
    {
        var names = deletedPaths.Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var marked = _initTones.Where(m => names.Contains(m.Value)).Select(m => m.Key).ToList();
        if (marked.Count == 0) return;

        foreach (var engine in marked) _initTones.Remove(engine);

        try
        {
            LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(Folder, _initTones));
        }
        catch (Exception e)
        {
            UserActionLog.Failed("clear the init-tone marks of deleted snapshots", e.ToString());
        }
    }

    /// <summary>Put a kept copy back, after asking. The confirmation is not ceremony: restoring overwrites
    /// the file that is there now, and the user is by definition already having a bad day.</summary>
    private async Task RestoreVersionAsync(LibraryEntryViewModel row, PatchVersion version)
    {
        var when = version.Written.ToString("g", CultureInfo.CurrentCulture);
        if (!await _confirm($"Replace \"{row.Name}\" with the copy from {when}? " +
                            "What is there now is kept as a version, so this can be undone.", "Restore"))
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

    /// <summary>Apply one change to every selected snapshot, one file at a time.
    ///
    /// <b>A failure costs that file only.</b> A snapshot held open by a sync client must not abandon the
    /// other thirteen, so each is attempted and the failures are named at the end rather than thrown. Each
    /// write archives the previous copy through <see cref="PatchHistory"/>, so a bulk change is as
    /// recoverable as a single one.
    ///
    /// <b>The loop is off the UI thread, and at this scale it has to be.</b> Annotating one snapshot reads
    /// the whole file, parses all ~1,500 of its parameter values, archives a copy and writes it back --
    /// which is why <see cref="_compare"/> already says doing that for a <em>single</em> Studio Set on the
    /// click stalls the window visibly. Doing it for everything a user has selected while tidying a library
    /// would be a freeze with nothing on screen to explain it. The status line says what is happening
    /// before the work starts, because the freeze it replaces is exactly the thing a user reads as a
    /// hang.</summary>
    private async Task ApplyBulkChangeAsync(BulkChange change)
    {
        // Copied first: the write path refreshes the list, which rebuilds the very rows being iterated.
        var rows = SelectedEntries.ToList();
        if (rows.Count == 0) return;

        _report($"Updating {rows.Count} snapshots…", false);

        // Only the file names and the heads cross the thread, and both are immutable records.
        var failed = await Task.Run(() =>
        {
            List<string> problems = [];
            foreach (var row in rows)
            {
                try
                {
                    SnapshotLibrary.WriteMetadata(row.FilePath, BulkEdit.Apply(row.Entry.Head, change));
                }
                catch (Exception e)
                {
                    UserActionLog.Failed($"bulk edit '{row.FilePath}'", e.ToString());
                    problems.Add(row.Name);
                }
            }

            return problems;
        });

        // Back on the UI thread: the await above resumed on the context the command was invoked from, which
        // is what both of these need.
        _report(failed.Count == 0
            ? $"Updated {rows.Count} snapshots."
            : $"Updated {rows.Count - failed.Count} of {rows.Count} snapshots; " +
              $"{failed.Count} could not be written: {string.Join(", ", failed)}.", failed.Count > 0);

        Refresh();
    }

    /// <summary>Show the two selected snapshots side by side on the Compare tab.
    ///
    /// The order is the order they are listed in, so the left-hand slot is the row nearer the top of the
    /// list -- which is what a user pointing at two rows and asking to compare them will expect, whichever
    /// of the two they happened to click first.</summary>
    private async Task CompareSelectionAsync()
    {
        // Copied, and its order taken from the list rather than from the selection: SelectedEntries is
        // filled in click order, so without this the left slot would depend on which row was clicked first.
        var rows = SelectedEntries.ToList();
        if (rows.Count != 2) return;

        var inListOrder = Entries.Where(rows.Contains).ToList();
        if (inListOrder.Count != 2) return; // both are on screen, or there is nothing to show

        await _compareTwo(inListOrder[0].Entry, inListOrder[1].Entry);
    }

    /// <summary>Remove every selected snapshot, after asking once for all of them. Each is archived by
    /// <see cref="PatchHistory"/>, which is what makes one button able to remove fourteen files.</summary>
    private async Task DeleteSelectionAsync()
    {
        // Copied before the question, not only before the loop: awaiting the dialog gives the list a chance
        // to refresh under us, and a confirmation is about the rows the user was looking at when they asked.
        var rows = SelectedEntries.ToList();
        if (rows.Count == 0) return;

        if (!await _confirm($"Delete {rows.Count} snapshots from the library? " +
                            "A copy of each is kept in the history folder beside your library.",
                            "Delete")) return;

        _report($"Deleting {rows.Count} snapshots…", false);

        // Off the UI thread, for the reason ApplyBulkChangeAsync's remarks give: each delete archives a copy
        // of the file first, so this is a disk round trip per row rather than a flag being cleared.
        var (failed, deleted) = await Task.Run(() =>
        {
            List<string> problems = [];
            List<string> gone = [];
            foreach (var row in rows)
            {
                try
                {
                    SnapshotLibrary.Delete(row.FilePath);
                    gone.Add(row.FilePath);
                }
                catch (Exception e)
                {
                    UserActionLog.Failed($"bulk delete '{row.FilePath}'", e.ToString());
                    problems.Add(row.Name);
                }
            }

            return (problems, gone);
        });

        // Only the ones that actually went: a file that could not be deleted is still there, and its mark
        // still points at something real. On the UI thread, because it writes the settings the list reads.
        ForgetInitToneMarks(deleted);

        _report(failed.Count == 0
            ? $"Deleted {rows.Count} snapshots."
            : $"Deleted {rows.Count - failed.Count} of {rows.Count}; " +
              $"{failed.Count} could not be removed: {string.Join(", ", failed)}.", failed.Count > 0);

        Refresh();
    }

    // ---- duplicates ---------------------------------------------------------------------------------------

    /// <summary>Open the duplicate panel and start looking. One gesture rather than two, because the button
    /// says "find duplicates" and a panel that then sat there waiting to be told to find them would be
    /// answering a question the user has already asked.</summary>
    public void FindDuplicates()
    {
        UserActionLog.Action("button: Find duplicates in the library");
        _findingDuplicates = true;
        PanelChanged();

        // Nothing awaits it: a button binding cannot, and there is nothing to wait for -- the scan reports
        // its own outcome and logs its own failures, exactly as the deep search does.
        _ = RefreshAndScanAsync(Duplicates.Threshold);
    }

    /// <summary>Re-read the folder, then look through it for duplicates.
    ///
    /// <b>The two gestures that mean "tell me about the library as it is now" go through here</b> -- opening
    /// the panel and pressing Scan -- and they are the only two that pay for a listing. The other two callers
    /// refresh as part of what they were already doing: a delete has just changed the folder, and a folder
    /// change has just read the new one. The scan used to refresh for itself, which meant those two paid for
    /// two listings and got nothing for the second.</summary>
    private async Task RefreshAndScanAsync(int threshold)
    {
        Refresh();
        await ScanForDuplicatesAsync(threshold);
    }

    /// <summary>Put the editor back. The selection was never touched, so whatever was selected before is
    /// still selected and its panel is still describing it.</summary>
    public void CloseDuplicates()
    {
        _findingDuplicates = false;
        PanelChanged();
    }

    /// <summary>Look through the whole folder for snapshots that are the same sound saved more than once, and
    /// hand what is found to the panel.
    ///
    /// <b>The whole folder, unlike the deep search.</b> That one reads only what the other filters admit and
    /// the text did not, because a row already on screen needs no second reason. A duplicate is a relation
    /// between two files, so narrowing the folder would silently change the answer: filtering to one engine
    /// and scanning would report a patch as unique when its twin is two rows away under another category.
    /// What makes that affordable is <see cref="_vectors"/> -- a second scan reads only what has been
    /// written since the first.
    ///
    /// <b>The panel is emptied before a byte is read</b>, not when an answer arrives -- see
    /// <see cref="DuplicateScanViewModel.ScanStarted"/> for the deletion this prevents. Everything after that
    /// is about filling it again: an answer that turns out to be about the wrong folder is dropped, and
    /// whatever asked for it is served by the loop in <see cref="ScanForDuplicatesAsync"/>.
    ///
    /// <b>The listing is not refreshed here.</b> It is the caller's, because two of the four callers have
    /// just refreshed for their own reasons -- see <see cref="RefreshAndScanAsync"/>.
    ///
    /// <b>The walk, the reads and the grouping are all inside the <c>Task.Run</c></b>, for the reason the
    /// bulk loops give: this opens and parses every file in the library that has changed, and doing that on
    /// the click is a freeze with nothing on screen to explain it. Only the folder name, the cache and the
    /// threshold cross the thread, and only the groups -- lists of paths -- come back.
    ///
    /// <b>Everything is guarded</b>, for <see cref="SearchInsideAsync"/>'s reason: this is reached from a
    /// button binding and from two callers that cannot await it, so an exception anywhere in here would
    /// otherwise be silence.</summary>
    public async Task ScanForDuplicatesAsync(int threshold)
    {
        // Once, and then again for anything that asked while this was reading -- see _rescanWanted for the
        // stale panel a dropped request used to leave behind. Only the call that is doing the reading ever
        // loops: a declined one leaves _scanning set and falls straight out, having put its request in the
        // flag for this loop to find. Each pass clears the flag before it starts, so the loop ends unless
        // somebody is asking again during every scan.
        do
        {
            try
            {
                await LookForDuplicatesAsync(threshold);
            }
            catch (Exception e)
            {
                UserActionLog.Failed("look for duplicates in the library", e.ToString());
                _report($"Could not look for duplicates: {e.Message}", true);
            }

            // The threshold of the request being served now, which is not necessarily the one this call was
            // made with: the user may have changed the box while waiting, and pressing Scan is what asked.
            threshold = Duplicates.Threshold;
        } while (_rescanWanted && !_scanning);
    }

    /// <summary>The body of <see cref="ScanForDuplicatesAsync"/> -- a method of its own only so that the
    /// public one can be nothing but the guard around it.</summary>
    private async Task LookForDuplicatesAsync(int threshold)
    {
        // One scan at a time. The threshold box and the Scan button are both live while one is running, and
        // two scans would be two writers of one cache. The request is remembered rather than dropped: the
        // caller may have just changed the folder or deleted files, and the answer in flight cannot be about
        // either.
        if (_scanning)
        {
            _rescanWanted = true;
            _report("Still looking for duplicates…", false);
            return;
        }

        // Everything asked up to this moment is what the scan below is about to answer.
        _rescanWanted = false;

        UserActionLog.Action(
            $"library: scan for duplicates within {threshold} parameter{(threshold == 1 ? "" : "s")}");

        // Before anything is read, and this is the line that stops the panel acting on an answer about
        // another folder -- see DuplicateScanViewModel.ScanStarted.
        Duplicates.ScanStarted();

        // Both captured because both can move while this is reading, and an answer about another folder --
        // or about a folder that has been written to since -- is not an answer at all.
        var folder = Folder;
        var version = _libraryVersion;
        _report("Looking through the library for duplicates…", false);

        _scanning = true;
        IReadOnlyList<IReadOnlyList<string>> groups;
        int unreadable;
        try
        {
            (groups, unreadable) = await Task.Run(() => ScanFolder(folder, _vectors, threshold));
        }
        finally
        {
            // In a finally, and before the answer is adopted: an adoption that threw would otherwise leave
            // the flag set and the feature dead for the rest of the session.
            _scanning = false;
        }

        // Back on the UI thread, and this is an answer about a folder that has moved on. The folder itself
        // changed, or something wrote to it -- a delete from this very panel, a capture saved on another
        // tab. Either way the groups would name files by a state of the folder that no longer holds, so
        // they are dropped. The panel is already empty; all that is left is to take "Looking…" down, and
        // only when nothing is on its way to replace it.
        if (!string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase) ||
            version != _libraryVersion)
        {
            if (!_rescanWanted) Duplicates.ScanAbandoned();
            return;
        }

        // The panel is not on screen, so there is nothing to correct and nothing to say. Reopening it scans
        // again.
        if (!_findingDuplicates) return;

        // Built once per scan rather than searched per row: a group of four in a folder of a thousand would
        // otherwise be four walks of the listing. A folder cannot hold two files at one path, so nothing can
        // be lost to a later entry overwriting an earlier one.
        //
        // Keyed on the full path, and the walk produces full paths too, so that the two cannot spell one
        // file two ways. They are built from the same folder string but not by the same code, and a Folder
        // holding forward slashes -- which only a hand-edited settings file produces, but nothing rejects --
        // would otherwise give "C:/lib/a.json" here and "C:/lib\a.json" there, and every row would lose its
        // name, its date, its rating and its Compare button with nothing anywhere saying why.
        Dictionary<string, LibraryEntry> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _all) byPath[Path.GetFullPath(entry.FilePath)] = entry;

        Duplicates.Show(groups, threshold, byPath);

        // The panel's own words, so that the status bar and the panel cannot say two different things about
        // one scan.
        var missed = unreadable == 0
            ? ""
            : $" {unreadable} file{(unreadable == 1 ? "" : "s")} could not be read.";
        _report(Duplicates.Summary + missed, unreadable > 0);
    }

    /// <summary>Read the folder, reduce every file that has changed to its raw values, and group the ones
    /// that are nearly alike. Answers the groups and how many files could not be read.
    ///
    /// Static, and handed everything it needs, because it runs on a thread-pool thread: nothing in here may
    /// touch this view model's state. The cache is an argument for that reason.
    ///
    /// <b>One walk, and it is <c>DirectoryInfo</c>'s.</b> The staleness rule needs a last-write time and a
    /// length per file, and enumerating <c>FileInfo</c> hands both over with the name -- so asking is free,
    /// where <c>Directory.EnumerateFiles</c> followed by a <c>FileInfo</c> each would be a second question to
    /// the file system per file. <c>FullName</c> is taken rather than the folder string joined to the name,
    /// and the caller normalises the listing's paths the same way, so that the two cannot spell one file two
    /// ways -- a row's name, date, rating and Compare button all hang off that lookup.
    ///
    /// <b>A folder that is not there scans as empty</b>, matching <c>SnapshotLibrary.Read</c> -- that is the
    /// normal state of the default library folder until the first save. A folder that refuses to be
    /// enumerated throws, and is reported by the caller, for the same asymmetry's reason.
    ///
    /// <b>A file that cannot be read is counted as well as logged</b>, as the deep search's scan counts them:
    /// one snapshot held open by a sync client must not sink the scan, but a scan that quietly left a patch
    /// out is one that says two sounds are unique when they are not.</summary>
    private static (IReadOnlyList<IReadOnlyList<string>> Groups, int Unreadable) ScanFolder(string folder,
        SnapshotVectorCache cache, int threshold)
    {
        List<SnapshotFileStamp> files = [];
        if (Directory.Exists(folder))
            foreach (var file in new DirectoryInfo(folder)
                         .EnumerateFiles(SnapshotLibrary.FilePattern, SearchOption.TopDirectoryOnly))
                files.Add(new SnapshotFileStamp(file.FullName, file.LastWriteTime, file.Length));

        List<(string Path, RawVector? Vector)> read = [];
        var unreadable = 0;

        foreach (var file in cache.ToRead(files))
            try
            {
                using var stream = File.OpenRead(file.Path);
                // Null for anything that is not a snapshot, which the cache remembers as such -- see
                // SnapshotVectorCache.Vectors for why that is knowledge rather than a failure.
                read.Add((file.Path, SnapshotRawVector.Read(stream)));
            }
            catch (Exception e)
            {
                UserActionLog.Failed($"read '{file.Path}' while looking for duplicates", e.ToString());
                unreadable++;
            }

        return (DuplicateGroups.Find(cache.Vectors(files, read), threshold), unreadable);
    }

    /// <summary>Remove the snapshots ticked in the duplicate panel, after asking once for all of them.
    ///
    /// <b>The same three steps as every other delete here</b> -- archive through <c>SnapshotLibrary.Delete</c>,
    /// forget the init-tone marks of the files that actually went, re-read the folder -- and one more: look
    /// again. A panel still offering rows for files that are no longer there would be a panel one click from
    /// a second confirmation about nothing, and a family that has just lost all but one member is not a
    /// family any more. The second look is warm, so it opens no file: what is left was read minutes ago and
    /// has not been written since.
    ///
    /// <b>The outcome is reported after the rescan</b>, not before, because the rescan reports its own
    /// summary and the status bar shows the last thing said -- and what the user needs to read after
    /// pressing Delete is how many snapshots went.</summary>
    private async Task DeleteDuplicatesAsync(IReadOnlyList<string> paths, int emptiedGroups)
    {
        if (paths.Count == 0) return;

        if (!await _confirm(LibraryListing.DuplicateDeleteQuestion(paths.Count, emptiedGroups), "Delete"))
            return;

        // Plural where the bulk panel's own message is not, because one row is an ordinary thing to tick
        // here and a rare thing to have selected there.
        _report($"Deleting {paths.Count} snapshot{(paths.Count == 1 ? "" : "s")}…", false);

        // Off the UI thread, for the reason DeleteSelectionAsync gives: each delete archives a copy of the
        // file first, so this is a disk round trip per row rather than a flag being cleared.
        var (failed, deleted, missing) = await Task.Run(() =>
        {
            List<string> problems = [];
            List<string> gone = [];
            var alreadyGone = 0;

            foreach (var path in paths)
            {
                try
                {
                    // Asked before the delete rather than worked out from it. SnapshotLibrary.Delete treats
                    // a file that is not there as success, and rightly -- the folder ends in the state that
                    // was asked for -- but it means a count of the calls that returned is not a count of the
                    // files this removed. See LibraryListing.DuplicateDeleteOutcome for why saying it
                    // removed one it did not is worse here than anywhere else.
                    var wasThere = File.Exists(path);
                    SnapshotLibrary.Delete(path);
                    // Either way it is gone, so either way a mark pointing at it is stale.
                    gone.Add(path);
                    if (!wasThere) alreadyGone++;
                }
                catch (Exception e)
                {
                    UserActionLog.Failed($"delete the duplicate '{path}'", e.ToString());
                    problems.Add(Path.GetFileName(path));
                }
            }

            return (problems, gone, alreadyGone);
        });

        // Only the ones that actually went: a file that could not be deleted is still there, and its mark
        // still points at something real. On the UI thread, because it writes the settings the list reads.
        ForgetInitToneMarks(deleted);

        // The delete's own refresh, and the scan below does not repeat it. It also moves _libraryVersion,
        // which is what makes a scan that was already running when these files went discard its answer
        // instead of refilling the panel with rows for them.
        Refresh();

        // Rebuilds the panel too, so nothing on either side names a file that has gone. Warm: what is left
        // was read minutes ago and has not been written since. If a scan is still running this is remembered
        // rather than dropped, and runs the moment that one finishes -- see _rescanWanted.
        await ScanForDuplicatesAsync(Duplicates.Threshold);

        _report(LibraryListing.DuplicateDeleteOutcome(deleted.Count - missing, missing, failed),
            failed.Count > 0);
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

        // A duplicate report is a report about a folder, so the one on screen is about the wrong one now.
        // Looking again rather than closing the panel: being in it is the user saying they are tidying
        // duplicates, and changing folder in the middle of that is a request to tidy this one.
        if (_findingDuplicates) _ = ScanForDuplicatesAsync(Duplicates.Threshold);

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

    /// <summary>Put every filter back to "not asking". A single button because there are seven of them and
    /// finding the one that is hiding what you are looking for is the whole difficulty of a filtered
    /// list.</summary>
    public void ClearFilters()
    {
        UserActionLog.Action("button: Clear library filters");
        SearchText = "";
        KindLabel = LibraryListing.AnyKind;
        EngineLabel = LibraryListing.AnyEngine;
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
    /// rather than a file. Annotating an existing file is <see cref="SaveChangesAsync"/>, which is handed no
    /// snapshot at all -- see <c>SnapshotLibrary.WriteMetadata</c> for why that distinction is enforced by the
    /// signatures.
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
