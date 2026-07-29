using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One kept copy, as a row in the version list. A type rather than a formatted string because the
/// list has to hand the file path back when one is chosen.</summary>
public sealed class PatchVersionViewModel(PatchVersion version)
{
    public PatchVersion Version { get; } = version;

    /// <summary>The user's own short date and time, which is what a file listing shows everywhere else on
    /// the machine -- a fixed pattern here would be this one list disagreeing with all of them.</summary>
    public string Written => Version.Written.ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>The panel beside the library list: what the selected snapshot says about itself, and the
/// things that can be done to it.
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
    private readonly Func<LibraryEntryViewModel, PatchVersion, Task> _restore;
    private readonly Func<LibraryEntryViewModel, Task> _audition;

    /// <param name="save">Write the edited metadata back. Takes the row as well as the metadata so that
    /// the caller, which owns the folder and the refresh, does not have to ask what is selected.</param>
    /// <param name="load">Send this snapshot to the instrument.</param>
    /// <param name="compare">Hand this snapshot to the Compare tab.</param>
    /// <param name="delete">Remove it from the library, after asking.</param>
    /// <param name="markAsInitTone">Make it the tone Init starts from for its engine.</param>
    /// <param name="restore">Put a kept copy back over the row's file, after asking.</param>
    /// <param name="audition">Hear this snapshot in the selected part, or stop hearing it. Which of the two
    /// it does is the caller's decision and not this panel's: it is the caller that knows what is playing,
    /// and this only knows what is on screen.</param>
    public LibraryEditorViewModel(
        Func<LibraryEntryViewModel, SnapshotMetadata, Task> save,
        Func<LibraryEntryViewModel, Task> load,
        Func<LibraryEntryViewModel, Task> compare,
        Func<LibraryEntryViewModel, Task> delete,
        Action<LibraryEntryViewModel> markAsInitTone,
        Func<LibraryEntryViewModel, PatchVersion, Task> restore,
        Func<LibraryEntryViewModel, Task> audition)
    {
        _save = save;
        _load = load;
        _compare = compare;
        _delete = delete;
        _markAsInitTone = markAsInitTone;
        _restore = restore;
        _audition = audition;

        // The seven flags the buttons and the panel bind to are not raised by the generated setters of the
        // properties they read, so they are raised together whenever any of the inputs changes.
        this.WhenAnyValue(x => x.Selected, x => x.EditName, x => x.SelectedVersion,
                (_, _, _) => System.Reactive.Unit.Default)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(SelectedIsTone));
                this.RaisePropertyChanged(nameof(CanSaveChanges));
                this.RaisePropertyChanged(nameof(CanMarkAsInitTone));
                this.RaisePropertyChanged(nameof(InitToneNote));
                this.RaisePropertyChanged(nameof(CanRestore));
                this.RaisePropertyChanged(nameof(CanAudition));
            });

        // Its own subscription rather than a fourth term above: the caller assigns this one, and it changes
        // for a reason none of the others do -- the audition it describes started or ended somewhere else.
        this.WhenAnyValue(x => x.IsAuditioning)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(AuditionLabel)));

        this.WhenAnyValue(x => x.Selected).Subscribe(_ => ShowSelected());
    }

    /// <summary>Which row the panel is describing, or null. Assigned by the list.</summary>
    [Reactive] private LibraryEntryViewModel? _selected;

    /// <summary>The kept copies of the selected snapshot, newest first, as dates a user reads. Rebuilt when
    /// the selection changes and after a restore, because a restore adds one.</summary>
    public ObservableCollection<PatchVersionViewModel> Versions { get; } = [];

    [Reactive] private PatchVersionViewModel? _selectedVersion;

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

    public bool HasVersions => Versions.Count > 0;

    public bool CanRestore => HasSelection && SelectedVersion is not null;

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
        ShowVersions();
    }

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

    /// <summary>Whether the row on screen is the one being heard. Assigned by the caller, which owns the
    /// session -- <b>per row, not per session</b>: while something is playing, its own row offers Stop and
    /// every other row offers Audition, so selecting a different tone and pressing the button plays that one
    /// instead of stopping. That is what browsing is.</summary>
    [Reactive] private bool _isAuditioning;

    /// <summary>What the audition button says. One button rather than two, because Stop is only ever
    /// wanted for the session this same panel started.</summary>
    public string AuditionLabel => IsAuditioning ? "Stop auditioning" : "Audition";

    /// <summary>Only for a tone. A Studio Set replaces all sixteen parts, which is not something to do to
    /// somebody who wanted to hear a patch.</summary>
    public bool CanAudition => SelectedIsTone;

    public async Task AuditionAsync()
    {
        UserActionLog.Action(IsAuditioning
            ? "button: Stop auditioning (library)"
            : "button: Audition (library)");
        if (Selected is { } row) await _audition(row);
    }

    public async Task CompareThisAsync()
    {
        UserActionLog.Action("button: Compare this");
        if (Selected is { } row) await _compare(row);
    }

    /// <summary>The log line for this one stays with the deletion in <see cref="LibraryViewModel"/>, unlike
    /// the four above: that is where the file is removed and where the user can still say no.</summary>
    public async Task DeleteSelectedAsync()
    {
        if (Selected is { } row) await _delete(row);
    }

    /// <summary>Put the chosen version back. Confirmed by the caller, which owns the dialog: this is the
    /// second time today the same sound is being overwritten, and the first time was the accident.</summary>
    public async Task RestoreVersionAsync()
    {
        UserActionLog.Action("button: Restore version (library)");
        if (Selected is { } row && SelectedVersion is { } version)
            await _restore(row, version.Version);
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
