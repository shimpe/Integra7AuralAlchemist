using System;
using System.Globalization;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row of the library list: everything about a snapshot file that can be shown without opening it.
///
/// <b>Read-only, and rebuilt rather than updated.</b> Every property here is a projection of the
/// <see cref="LibraryEntry"/> it was handed, and an entry is a file's head as it was when the folder was last
/// read -- so when a file changes, the honest thing is to read the folder again and build new rows, which is what
/// <c>LibraryViewModel.Refresh</c> does after every write. A row that could update itself would be a row that can
/// disagree with the file, and there is nothing here worth that: the whole list is a few dozen small objects.
///
/// <b>One property is not a projection of the file, and it is the only <c>PropertyChanged</c> plumbing here.</b>
/// The init-tone mark is not in the snapshot -- it is in the settings -- so it cannot be read off the entry, and
/// it moves between two rows without either file changing (see <see cref="IsInitTone"/>).
///
/// <b>It carries the <see cref="Entry"/> itself</b>, not just its path, because loading a snapshot needs its kind
/// to know which of the two restore paths to take and the head is where that already is. Reading the file a
/// second time to ask would be a second answer that can differ from the one on screen.</summary>
public sealed partial class LibraryEntryViewModel : ViewModelBase
{
    public LibraryEntryViewModel(LibraryEntry entry)
    {
        Entry = entry;

        // The generated IsInitTone setter announces itself and knows nothing of InitMark, which is what the
        // list column binds to -- the same wiring SaveToLibraryViewModel needs for the same reason.
        this.WhenAnyValue(x => x.IsInitTone).Subscribe(_ => this.RaisePropertyChanged(nameof(InitMark)));
    }

    public LibraryEntry Entry { get; }

    public string FilePath => Entry.FilePath;

    /// <summary>What the snapshot calls itself, which is not necessarily what the file is called: the name lives
    /// inside the file (see <c>Integra7Snapshot.Name</c>), the file name is the user's, and renaming in the
    /// browser changes the first and leaves the second alone.</summary>
    public string Name => Entry.Head.Name;

    /// <summary>"Studio Set", or "Tone SN-S" -- the engine folded into the kind rather than given a column of its
    /// own. It is only ever set for a tone, it is the one fact that decides whether a tone can be loaded into the
    /// part the user has selected, and an eighth column for a word that is blank on half the rows would earn
    /// less than the width it cost.</summary>
    public string Kind
    {
        get
        {
            var kind = LibraryListing.KindLabel(Entry.Head.Kind);
            return string.IsNullOrEmpty(Entry.Head.ToneType) ? kind : $"{kind} {Entry.Head.ToneType}";
        }
    }

    /// <summary>Whether Init Tone starts from this snapshot for its engine. Not read from the file like
    /// every other property here: the mark lives in the settings, so the library sets it when it builds
    /// the row and again when the user moves it.</summary>
    [Reactive] private bool _isInitTone;

    /// <summary>What the Kind column adds when the mark is set. A word rather than a glyph: the two
    /// glyphs this list already uses mean favourite and rating, and a third would be one more thing to
    /// learn for a flag at most five rows in the library carry.</summary>
    public string InitMark => IsInitTone ? "init" : "";

    /// <summary>Blank for a Studio Set, which is sixteen parts each with a category of its own and has none.
    /// </summary>
    public string Category => Entry.Head.Category;

    public string Stars => LibraryListing.Stars(Entry.Head.Rating);

    /// <summary>A heart rather than a star, because the stars in the next column mean something else. A favourite
    /// is not a rating -- a sound can be one without being the best thing in the library -- and two meanings
    /// sharing one glyph in adjacent columns would read as a rating of six.</summary>
    public string FavouriteMark => Entry.Head.Favourite ? "♥" : "";

    public string Tags => LibraryListing.FormatTags(Entry.Head.Tags);

    /// <summary>The file's own last-write time, in the user's own format. "g" is the short date and short time,
    /// which is what a file listing shows anywhere else on the machine; a fixed pattern here would be this one
    /// list disagreeing with every other one the user reads.</summary>
    public string Modified => Entry.Modified.ToString("g", CultureInfo.CurrentCulture);
}
