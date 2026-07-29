using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The panel beside the library list: what the selected snapshot says about itself, and the four
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

        // The five flags the buttons and the panel bind to are not raised by the generated setters of the
        // properties they read, so they are raised together whenever either input changes.
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

    /// <summary>The log line for this one stays with the deletion in <see cref="LibraryViewModel"/>, unlike
    /// the four above: that is where the file is removed and where the user can still say no.</summary>
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
