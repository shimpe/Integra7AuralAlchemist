using System;
using System.Collections.Generic;
using System.Reactive;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>What the instrument's current sound should be called in the library, and what should be said about it:
/// a name, a category, tags and a rating, asked once, before anything is read off the wire.
///
/// <b>Asked before the capture, not after.</b> A Studio Set capture is 53 blocks and a drum kit 92, and a user who
/// cancelled at the end of that would have paid for it for nothing. The name is also what the file is called (see
/// <c>SnapshotLibrary.FileNameFor</c>) and what the snapshot itself records, so asking first means one answer
/// reaches both rather than a captured name being overwritten by a typed one.
///
/// <b>Answers a <see cref="SnapshotMetadata"/>, or null for a cancellation</b> -- the same shape as
/// <c>SaveUserToneViewModel</c>, whose commands the caller likewise reads as "this or nothing". Handing back the
/// model record rather than five properties is what lets the caller pass it straight to the one write path.
///
/// <b>No character limit on the name</b>, unlike Save User Tone's twelve. That limit is the instrument's: a user
/// slot holds twelve characters. This name is a file's, and the tone's own name parameter travels inside the
/// snapshot's parameter data where the restore puts it back untouched.</summary>
public sealed partial class SaveToLibraryViewModel : ViewModelBase
{
    /// <param name="what">"Studio Set" or "tone" -- what is being saved, for the window's own prompt. Passed in
    /// rather than derived from <paramref name="hasCategory"/>, because those two happen to line up today and are
    /// not the same fact.</param>
    /// <param name="suggestedName">What the sound already calls itself: the Studio Set's name, or the part's
    /// preset name. The user is free to replace it, and most will keep it.</param>
    /// <param name="hasCategory">Whether to offer a category at all. False for a Studio Set, which is sixteen
    /// parts each with a category of its own and has no single one to name.</param>
    /// <param name="suggestedCategory">The category to start on -- for a tone, the one the instrument itself
    /// gives the preset in that part, which is right far more often than "none" and comes from the same
    /// vocabulary the drop-down offers.</param>
    /// <param name="folder">Where the file will go, shown but not editable: the library folder is changed in the
    /// library tab, and a save dialog that could quietly redirect one file somewhere else would make "the
    /// library" mean two things.</param>
    public SaveToLibraryViewModel(string what, string suggestedName, bool hasCategory, string suggestedCategory,
        string folder)
    {
        What = what;
        HasCategory = hasCategory;
        Folder = folder;
        _name = suggestedName;
        _categoryLabel = hasCategory
            ? LibraryListing.EditLabelForCategory(suggestedCategory)
            : LibraryListing.NoCategory;

        // Parameterless, like Save User Tone's: a ReactiveCommand<Unit, T> invoked from a button with no
        // CommandParameter is handed null, and casting null to Unit -- a struct -- throws where nothing catches it.
        CancelCommand = ReactiveCommand.Create(() => (SnapshotMetadata?)null);
        SaveCommand = ReactiveCommand.Create(() => (SnapshotMetadata?)new SnapshotMetadata(
            LibraryListing.CategoryToWrite(CategoryLabel),
            LibraryListing.ParseTags(Tags),
            // No notes here, deliberately. Notes are what you write after you have played the thing again; the
            // library's editor is where they belong, and one more multi-line box in front of a capture is one
            // more thing between the user and the sound they just made.
            Notes: "",
            Rating.Value,
            Favourite,
            Name.Trim()));

        // The generated Name setter announces itself and knows nothing of CanSave, so without this the Save
        // button would stay as it was when the dialog opened -- the same wiring SaveUserToneViewModel needs for
        // the same reason.
        this.WhenAnyValue(x => x.Name).Subscribe(_ => this.RaisePropertyChanged(nameof(CanSave)));
    }

    /// <summary>"Studio Set" or "tone", for the prompt.</summary>
    public string What { get; }

    public bool HasCategory { get; }

    public string Folder { get; }

    [Reactive] private string _name = "";
    [Reactive] private string _categoryLabel = LibraryListing.NoCategory;
    [Reactive] private string _tags = "";
    [Reactive] private bool _favourite;

    public RatingViewModel Rating { get; } = new();

    public IReadOnlyList<string> CategoryLabels => LibraryListing.EditCategoryLabels;

    /// <summary>Whether Save can do anything. A snapshot with no name is a row in the library the user cannot tell
    /// from the one above it, and the file would be called "Snapshot.json" -- so this is refused here rather than
    /// at the write, where it is refused too but where the user has already lost the capture.</summary>
    public bool CanSave => Name.Trim().Length > 0;

    public ReactiveCommand<Unit, SnapshotMetadata?> SaveCommand { get; }

    /// <summary>Answers null, which the caller reads as "cancelled" -- exactly as Save User Tone's does.</summary>
    public ReactiveCommand<Unit, SnapshotMetadata?> CancelCommand { get; }
}
