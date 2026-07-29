using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Choosing one tone out of the library, in a dialog.
///
/// <b>Why this exists rather than a file dialog.</b> The morph pad picked its corners through the operating
/// system's own open dialog, which filters by extension and nothing else -- so a user choosing the second
/// corner of an SN-S pad had a list of file names and no way to tell which of them were SN-S. Everything
/// needed to say so is already in the library's own listing, so this shows it: the name the snapshot calls
/// itself, its engine, its category and its rating.
///
/// <b>Tones only, and no drum kits.</b> A Studio Set is not something any caller of this wants to be handed
/// one of, and a kit is 62 or 88 independently edited notes, so blending them produces a kit that is no longer
/// one -- which is why the morph pad refuses them. Both are left out of the list rather than refused after the
/// click, because a picker whose whole purpose is to show what can be chosen must not offer what cannot.
/// The engine drop-down leaves the two kit engines out for the same reason.
///
/// <b>The engine can be locked.</b> A morph pad's first corner decides the engine and every later corner has
/// to match it, so a locked picker shows only that engine and says so instead of offering a drop-down that
/// can only be set one way. Unlocked, the drop-down is the library's own -- the same labels, the same
/// meanings, see <see cref="LibraryListing.EngineLabels"/>.
///
/// <b>It does not read any file.</b> It works entirely off the heads the library listing already has, and
/// answers the entry it was given. Whoever asked opens it, which is also where the engine and the kind are
/// checked a second time -- a pad loaded from disk names files this dialog never showed, so the refusals
/// cannot live here.</summary>
public sealed partial class TonePickerViewModel : ViewModelBase
{
    /// <param name="entries">The library as it was last listed. Not re-read here: the caller has it, and a
    /// second read would be a second answer that can differ from the list behind the dialog.</param>
    /// <param name="title">What the dialog is for, in the user's words -- "Choose a tone for corner 2".</param>
    /// <param name="engine">The engine every answer must be, or null to let the user narrow it themselves.
    /// </param>
    public TonePickerViewModel(IReadOnlyList<LibraryEntry> entries, string title, string? engine)
    {
        Title = title;
        LockedEngine = engine;
        // Narrowed once, here, rather than on every keystroke: what a kit is does not depend on any filter.
        _all = MorphCandidates.In(entries);

        // A locked picker starts on that engine and offers no other; an unlocked one starts on everything.
        EngineLabel = engine ?? LibraryListing.AnyEngine;

        var canChoose = this.WhenAnyValue(x => x.SelectedEntry).Select(selected => selected is not null);

        // Parameterless, for the reason ConfirmViewModel gives: a command invoked from a button with no
        // CommandParameter is handed null, and casting null to Unit throws.
        ChooseCommand = ReactiveCommand.Create(() => SelectedEntry?.Entry, canChoose);
        CancelCommand = ReactiveCommand.Create(() => (LibraryEntry?)null);

        this.WhenAnyValue(x => x.SearchText, x => x.EngineLabel, (_, _) => Unit.Default)
            .Subscribe(_ => ApplyFilter());

        ApplyFilter();
    }

    private readonly IReadOnlyList<LibraryEntry> _all;

    public string Title { get; }

    /// <summary>The engine the answer has to be, or null when the user may choose. Held as well as applied,
    /// because the view asks it which of the two headings to show.</summary>
    public string? LockedEngine { get; }

    public bool EngineIsLocked => LockedEngine is not null;

    /// <summary>A sentence rather than a caption beside a value, so that the reason the list is short is on
    /// screen next to the short list. The unlocked case says what choosing will do, because in a morph pad it
    /// is the choice that locks every later one.</summary>
    public string EngineNote => LockedEngine is { } locked
        ? $"Showing {locked} tones only, because that is what this pad holds."
        : "Any engine. The one you pick here decides what the rest of the pad has to be.";

    [Reactive] private string _searchText = "";

    /// <summary>Ignored while the engine is locked -- the view does not show the drop-down then -- but still
    /// the single place the filter reads its engine from, so there is no second path to keep in step.</summary>
    [Reactive] private string _engineLabel = LibraryListing.AnyEngine;

    public IReadOnlyList<string> EngineLabels => MorphCandidates.EngineLabels;

    public ObservableCollection<LibraryEntryViewModel> Entries { get; } = [];

    [Reactive] private LibraryEntryViewModel? _selectedEntry;

    /// <summary>How many the filters admit out of how many there are. The same thing the library's own summary
    /// says, and for the same reason: "this library is empty" and "these filters admit nothing" look identical
    /// on screen and have opposite remedies.</summary>
    [Reactive] private string _summary = "";

    public ReactiveUI.Reactive.ReactiveCommand<Unit, LibraryEntry?> ChooseCommand { get; }
    public ReactiveUI.Reactive.ReactiveCommand<Unit, LibraryEntry?> CancelCommand { get; }

    private void ApplyFilter()
    {
        // The engine is the locked one when there is one, so it cannot be widened by anything the user does in
        // here. The kind and the kits were settled when the list was taken.
        var filter = new LibraryFilter(
            SearchText,
            Engine: LockedEngine ?? LibraryListing.EngineFromLabel(EngineLabel));

        var admitted = LibraryListing.Sort(filter.Apply(_all), LibrarySort.Name, descending: false);

        // The selection is dropped rather than carried across: a row that the filter has just hidden is not a
        // row the user can see they have chosen, and Choose would then answer something invisible.
        SelectedEntry = null;
        Entries.Clear();
        foreach (var entry in admitted) Entries.Add(new LibraryEntryViewModel(entry));

        Summary = MorphCandidates.Summary(Entries.Count, _all.Count);
    }
}
