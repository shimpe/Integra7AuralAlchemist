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
    private readonly Func<Task> _compareBoth;

    /// <param name="apply">Write one change across the selection.</param>
    /// <param name="delete">Remove the selection from the library, after asking.</param>
    /// <param name="compareBoth">Show the two selected snapshots side by side on the Compare tab.</param>
    public LibraryBulkEditViewModel(Func<BulkChange, Task> apply, Func<Task> delete,
        Func<Task> compareBoth)
    {
        _apply = apply;
        _delete = delete;
        _compareBoth = compareBoth;
    }

    /// <summary>How many rows the panel is acting on. Set by the list; shown on every button that acts, so
    /// that pressing one is never a guess about how much it does.</summary>
    [Reactive] private int _count;

    public string Summary => $"{Count} snapshots selected.";

    public string DeleteLabel => $"Delete {Count} snapshots…";

    /// <summary>Whether the two selected snapshots can be shown side by side. <b>Exactly two</b>, because
    /// the Compare tab has two slots: with three selected there is no answer to which pair was meant, and
    /// choosing one silently would be worse than not offering the button.</summary>
    public bool CanCompare => Count == 2;

    /// <summary>Raised by the list when the selection changes, because the two strings above are computed
    /// and the generated setter for Count does not know about them.</summary>
    public void CountChanged()
    {
        this.RaisePropertyChanged(nameof(Summary));
        this.RaisePropertyChanged(nameof(DeleteLabel));
        this.RaisePropertyChanged(nameof(CanCompare));
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

    public async Task CompareBothAsync()
    {
        UserActionLog.Action("button: Compare the two selected (library)");
        await _compareBoth();
    }

    public async Task DeleteAsync()
    {
        UserActionLog.Action("button: Delete selected (library)");
        await _delete();
    }
}
