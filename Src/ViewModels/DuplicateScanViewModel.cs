using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One snapshot in a group of near-identical ones, with the tick box that decides its fate.
///
/// <b>The name is the least useful thing about it</b>, which is why this row is not
/// <see cref="LibraryEntryViewModel"/>. A family of duplicates is usually one sound saved four times under
/// one name, so what tells the four apart is the file name, the date and the rating -- and those are what a
/// user needs in order to decide which one to keep. The list's own row shows the name first and the file name
/// not at all, which is right there and useless here.
///
/// <b>The entry can be missing, and the row still exists.</b> A scan reads the folder itself rather than
/// taking the listing's word for it, and the two are taken at different moments: the listing is read before
/// the walk starts and the walk runs for as long as the reading takes, so a file saved during a scan is in
/// the walk and not in the listing. So is one that was locked when the listing was read and free when the
/// scan reached it -- <c>SnapshotLibrary.Read</c> leaves a file it cannot open out of the listing entirely.
/// Dropping such a row would shrink a group without saying so, and a group that quietly lost a member is the
/// one failure a duplicate report must not have. So it is shown, named after its file, it says on itself
/// that it is not in the list (see <see cref="Note"/>), and it is the one row that cannot be sent to the
/// Compare tab -- which needs the head to know which of the two restore paths a snapshot takes.
///
/// No ToolTip, per the rule this branch keeps for anything clicked repeatedly.</summary>
public sealed class DuplicateRowViewModel : ViewModelBase
{
    private readonly Action _ticked;
    private bool _isTicked;

    internal DuplicateRowViewModel(string filePath, LibraryEntry? entry, Action ticked)
    {
        FilePath = filePath;
        Entry = entry;
        _ticked = ticked;
    }

    public string FilePath { get; }

    /// <summary>What the Compare tab needs, or null for a file the listing does not know -- see the remarks
    /// above.</summary>
    public LibraryEntry? Entry { get; }

    /// <summary>What the snapshot calls itself, or what the file is called when it does not say.
    ///
    /// <b>Never blank</b>, because a blank row is one the user cannot tell from the one above it, and this is
    /// a panel whose whole job is telling near-identical rows apart. Two things reach the fallback, not one:
    /// a file the listing does not know at all, and one whose head carries no name -- <c>SnapshotHead</c>
    /// reads a missing or null name as "" on purpose, so that such a file is listed rather than hidden, and
    /// testing the entry alone would let that "" through.</summary>
    public string Name => Entry?.Head.Name is { Length: > 0 } named
        ? named
        : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>The file's own name, which is the thing that actually differs between four saves of one
    /// sound: "Warm Rhodes.json", "Warm Rhodes (2).json", "Warm Rhodes (3).json".
    ///
    /// Blank, and collapsed by the view, for a row the listing does not know: there <see cref="Name"/> is
    /// already the file's own name, and the two lines would be the same word twice with ".json" added.
    /// </summary>
    public string FileName => Entry is null ? "" : Path.GetFileName(FilePath);

    /// <summary>Why this row shows less than the ones around it, or "" when it shows everything.
    ///
    /// Said on the row rather than beside the Compare button, and for two reasons. It is constant for the
    /// life of the row, so it cannot move anything while the user is working down a column of tick boxes --
    /// which is the whole reason the buttons above are disabled rather than hidden. And it explains the blank
    /// date and rating in the same breath as the disabled button, which is what a user looking at this row is
    /// actually puzzled by.</summary>
    public string Note => Entry is null ? "Not in the list; cannot be compared." : "";

    /// <summary>When it was written, in the user's own format -- <see cref="LibraryEntryViewModel.Modified"/>'s
    /// format and for its reason. Blank for a file the listing does not know, since its time was not read.
    /// </summary>
    public string Modified => Entry is { } entry
        ? entry.Modified.ToString("g", CultureInfo.CurrentCulture)
        : "";

    /// <summary>The rating, which is often the only thing the user has already said about which copy is the
    /// good one.</summary>
    public string Stars => Entry is { } entry ? LibraryListing.Stars(entry.Head.Rating) : "";

    /// <summary>Whether this row is one of the ones the two buttons act on. Nothing is ticked by default:
    /// this panel deletes files, and a panel that opened with rows already ticked would be one press away
    /// from deleting a selection nobody made.</summary>
    public bool IsTicked
    {
        get => _isTicked;
        set
        {
            if (_isTicked == value) return;
            this.RaiseAndSetIfChanged(ref _isTicked, value);
            _ticked();
        }
    }
}

/// <summary>One family of near-identical snapshots.
///
/// A class of its own rather than a bare list because the panel shows a caption above each group, and because
/// what a group means is not obvious enough to leave unsaid anywhere it is shown -- see
/// <see cref="LibraryListing.DuplicateSummary"/>.</summary>
public sealed class DuplicateGroupViewModel
{
    internal DuplicateGroupViewModel(IReadOnlyList<DuplicateRowViewModel> rows)
    {
        Rows = rows;
    }

    /// <summary>The members, in <see cref="DuplicateGroups"/>' own order -- by path, and stable across two
    /// scans of one folder. Nothing here re-orders them: the order the user sees is the order "Compare these
    /// two" takes its left and right slots from.</summary>
    public IReadOnlyList<DuplicateRowViewModel> Rows { get; }

    /// <summary>How many of the rows are ticked. Half of what <see cref="LibraryListing.GroupsEmptiedBy"/>
    /// is asked, and it is a count rather than the rows themselves because that method has to live where a
    /// test can call it -- see its remarks for what is at stake in the number it produces.</summary>
    public int TickedRows => Rows.Count(row => row.IsTicked);

    /// <summary>The count and nothing else. Saying what the group <i>is</i> belongs to the summary at the top
    /// of the panel, once, where it is written by a tested method and quotes the threshold -- and "nearly the
    /// same" repeated over every group would be false at a threshold of nought, where they are identical.
    /// </summary>
    public string Caption => $"{Rows.Count} snapshots";
}

/// <summary>The panel that finds the patches saved more than once, and offers to remove the spare copies.
///
/// <b>A third panel beside the editor and the bulk form</b>, in the same place and shown instead of them --
/// see <c>LibraryViewModel.ShowsDuplicates</c> for what decides which of the three is up. It is opened by a
/// button rather than by a selection, because unlike the other two it is not about what is selected: it is a
/// question about the whole folder.
///
/// <b>It opens no file and deletes nothing.</b> The folder read, the grouping and every write belong to
/// <c>LibraryViewModel</c>, which owns the folder, the cache and the refresh -- the same split
/// <see cref="LibraryBulkEditViewModel"/> makes. What is here is the threshold, the rows, which of them are
/// ticked, and the words.
///
/// <b>The summary quotes the threshold the results were found with, not the one in the box.</b> Changing the
/// box without pressing Scan would otherwise relabel a set of groups with a number that had nothing to do
/// with them -- and since the sentence is what tells the user how far apart two members may be, that
/// relabelling would be a false promise about files they are about to delete. It is the same discipline
/// <see cref="DeepSearchAnswer"/> keeps for the deep search: an answer carries the question it answers.
/// </summary>
public sealed partial class DuplicateScanViewModel : ViewModelBase
{
    private readonly Func<int, Task> _scan;

    /// <summary>Remove these files, knowing how many groups would be emptied outright. The count travels with
    /// the paths because it is part of the question the user is asked, and this panel is the only thing that
    /// can work it out -- the owner sees a list of paths and has no idea which family each came from.</summary>
    private readonly Func<IReadOnlyList<string>, int, Task> _delete;

    /// <summary>The library's own pair compare, handed straight through. Two snapshots is two snapshots
    /// whether they were picked out of the list or out of a duplicate group.</summary>
    private readonly Func<LibraryEntry, LibraryEntry, Task> _compareTwo;

    private readonly Action _close;

    /// <param name="scan">Look through the folder at this threshold and hand the answer back through
    /// <see cref="Show"/>.</param>
    /// <param name="delete">See <see cref="_delete"/>.</param>
    /// <param name="compareTwo">See <see cref="_compareTwo"/>.</param>
    /// <param name="close">Put the editor back.</param>
    public DuplicateScanViewModel(Func<int, Task> scan, Func<IReadOnlyList<string>, int, Task> delete,
        Func<LibraryEntry, LibraryEntry, Task> compareTwo, Action close)
    {
        _scan = scan;
        _delete = delete;
        _compareTwo = compareTwo;
        _close = close;
    }

    /// <summary>How many parameter values two snapshots may differ in and still count as the same sound.
    ///
    /// <b>Five, because the complaint this answers is the sound saved four times while it was being
    /// edited</b>, and those differ by a handful of values rather than by none at all -- see
    /// <see cref="DuplicateGroups"/>. Nought is a perfectly good setting and means "identical", which is the
    /// other thing a user comes here for: the file copied in twice.</summary>
    [Reactive] private int _threshold = 5;

    /// <summary>What was found, in the panel's own words. Also what the status bar says when a scan finishes,
    /// so that the two cannot disagree.</summary>
    [Reactive] private string _summary =
        "Press Scan to look through the library for patches saved more than once.";

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    /// <summary>Whether anything was found. The groups and the two buttons collapse when nothing was, rather
    /// than leaving an empty box and two dead buttons under a sentence saying there is nothing there.</summary>
    public bool HasGroups => Groups.Count > 0;

    /// <summary>The ticked rows, in the order they are on screen: by group, and within a group in
    /// <see cref="DuplicateGroups"/>' own order.
    ///
    /// <b>That order is the whole reason this is not a selection.</b> <c>LibraryViewModel.CompareSelectionAsync
    /// </c> has to re-order its two rows against the list, because Avalonia fills a selection in click order;
    /// tick boxes have no click order to leak, so reading them off in screen order is both the simplest thing
    /// and the right one.</summary>
    private List<DuplicateRowViewModel> Ticked() =>
        [.. Groups.SelectMany(group => group.Rows).Where(row => row.IsTicked)];

    /// <summary>Whether the two ticked snapshots can be shown side by side. <b>Exactly two</b>, for
    /// <see cref="LibraryBulkEditViewModel.CanCompare"/>'s reason: the Compare tab has two slots, and with
    /// three ticked there is no answer to which pair was meant. Both must also be files the listing knows --
    /// see <see cref="DuplicateRowViewModel.Entry"/>.</summary>
    public bool CanCompare => Ticked() is [{ Entry: not null }, { Entry: not null }];

    public bool CanDelete => Ticked().Count > 0;

    /// <summary>The count is on the button rather than only in the dialog, because this is the action that
    /// takes files out of the folder -- <see cref="LibraryBulkEditViewModel.DeleteLabel"/>'s rule. The words
    /// are <see cref="LibraryListing.DuplicateDeleteLabel"/>'s, because they are the last thing a user reads
    /// before a deletion and belong where a test can check them.</summary>
    public string DeleteLabel => LibraryListing.DuplicateDeleteLabel(Ticked().Count);

    /// <summary>Raised by a row when its tick box moves, because the three members above are computed and
    /// nothing else knows they depend on it. The rows' own callback, handed to them when they are built.
    /// </summary>
    private void TicksChanged()
    {
        this.RaisePropertyChanged(nameof(CanCompare));
        this.RaisePropertyChanged(nameof(CanDelete));
        this.RaisePropertyChanged(nameof(DeleteLabel));
    }

    /// <summary>A scan has begun: forget what the last one found.
    ///
    /// <b>This is what stops the panel deleting from a folder nobody is looking at.</b> Rows used to survive
    /// from one scan to the next, because only <see cref="Show"/> touched them and that runs when a scan
    /// <i>finishes</i> -- so a scan that was discarded, or one still running, left the previous answer on
    /// screen with a live Delete button under it. Change the library folder while a cold scan is reading and
    /// the list on the left is the new folder while the groups on the right are the old one; the
    /// confirmation counts files and never names them, so nothing in the dialog would give it away. The same
    /// shape without a folder change: a twin deleted through the editor leaves the group still promising a
    /// spare copy, and deleting on that promise takes the last one.
    ///
    /// Emptying at the start rather than trying to keep the rows valid is the only version of this that
    /// cannot be got wrong: while a scan is running there is no answer, and a panel with no answer must not
    /// offer to act on one.</summary>
    public void ScanStarted() => Reset("Looking through the library for duplicates…");

    /// <summary>A scan ended with nothing to show -- the folder moved, or the library was written to while
    /// it was being read. Usually another scan is already on its way and this is never seen; when none is,
    /// it is what stops the panel saying "Looking…" for the rest of the session.</summary>
    public void ScanAbandoned() =>
        Reset("The library changed while it was being read. Press Scan to look again.");

    /// <summary>Empty the panel and say why. Every tick goes with the rows, which is the point: a tick is a
    /// decision about a file in a family, and there is no family on screen any more.</summary>
    private void Reset(string summary)
    {
        Groups.Clear();
        Summary = summary;
        this.RaisePropertyChanged(nameof(HasGroups));
        TicksChanged();
    }

    /// <summary>Show what a scan found: the groups as paths, the threshold they were found at, and the
    /// listing to name them from.
    ///
    /// <b>The rows are rebuilt, not updated</b> -- <see cref="LibraryEntryViewModel"/>'s rule, and here it
    /// also disposes of every tick. That is deliberate: a tick is a decision about a particular file in a
    /// particular family, and a scan that has just re-read the folder may have put that file in another
    /// family or found it gone. Carrying ticks across would mean a Delete button acting on a decision the
    /// user made about a list they are no longer looking at.</summary>
    /// <param name="groups">The families, as paths, in <see cref="DuplicateGroups"/>' order.</param>
    /// <param name="threshold">What they were found at, which is what the summary quotes.</param>
    /// <param name="byPath">The listing, keyed the way <paramref name="groups"/> spells its paths -- both
    /// full paths, normalised by the caller, so that one file cannot be spelt two ways between them. A path
    /// that is not in here gets a row all the same; see <see cref="DuplicateRowViewModel"/>.</param>
    public void Show(IReadOnlyList<IReadOnlyList<string>> groups, int threshold,
        IReadOnlyDictionary<string, LibraryEntry> byPath)
    {
        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(new DuplicateGroupViewModel(
                [.. group.Select(path =>
                    new DuplicateRowViewModel(path, byPath.GetValueOrDefault(path), TicksChanged))]));

        // Built from the threshold the scan ran at rather than read off the box later, which is what keeps
        // the sentence true after the user has changed the box without pressing Scan.
        Summary = LibraryListing.DuplicateSummary(Groups.Count, Groups.Sum(group => group.Rows.Count),
            threshold);

        this.RaisePropertyChanged(nameof(HasGroups));
        TicksChanged();
    }

    public async Task ScanAsync()
    {
        UserActionLog.Action("button: Scan for duplicates (library)");
        await _scan(Threshold);
    }

    /// <summary>Show the two ticked snapshots side by side. The left slot is the one nearer the top of the
    /// panel; see <see cref="Ticked"/>.</summary>
    public async Task CompareTickedAsync()
    {
        UserActionLog.Action("button: Compare the two ticked duplicates (library)");
        if (Ticked() is not [{ Entry: { } left }, { Entry: { } right }]) return;

        await _compareTwo(left, right);
    }

    /// <summary>Remove the ticked snapshots, after the owner has asked.
    ///
    /// <b>The count of groups that would be emptied is worked out here</b>, because it is a fact about the
    /// families on screen. It is what the confirmation warns about: ticking every row of a family is the
    /// natural gesture when four rows look alike, and it is the one that loses the sound rather than its
    /// spare copies.</summary>
    public async Task DeleteTickedAsync()
    {
        UserActionLog.Action("button: Delete the ticked duplicates (library)");
        var ticked = Ticked();
        if (ticked.Count == 0) return;

        var emptied = LibraryListing.GroupsEmptiedBy(
            Groups.Select(group => (group.Rows.Count, group.TickedRows)));
        await _delete([.. ticked.Select(row => row.FilePath)], emptied);
    }

    public void Close()
    {
        UserActionLog.Action("button: Close the duplicate panel (library)");
        _close();
    }
}
