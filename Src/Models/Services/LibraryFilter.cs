using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which entries a browse is asking to see. Pure: it reads <see cref="LibraryEntry"/> records and
/// answers with the ones admitted, touching no file and holding no state.
///
/// <b>Six axes, and they narrow together.</b> Free text, a kind, a category, a minimum rating, a favourites
/// toggle and a set of tags. Every one of them is a separate question about the same entry and an entry has to
/// answer all of them, because that is what a user does with filters -- adds one to see less. Each axis has a
/// value that means "not asking", and the default of every one is that value, so <see cref="None"/> admits the
/// whole library and the browser can open on it.
///
/// <b>Free text is the wide one.</b> It matches the name, the notes, the category <i>and</i> the tags, because
/// somebody typing "rhodes" does not know or care which field they put it in -- and if they had to know, the
/// box would be useless for the one thing a search box is for. It is one substring rather than a set of words
/// matched independently, which is a real limitation and a deliberate one: word matching is a better search
/// and a bigger change, and it wants the browser in front of it to judge. <c>TestLibraryFilter</c> pins the
/// limitation so that it stays a decision.
///
/// <b>Tags are the narrow one, and they are AND.</b> Choosing "warm" and "gig" means the entries that are
/// both. A user will assume it one way or the other, so it is written down here, in the tests, and in the
/// commit that introduced it. OR is what the search box already does across fields; two controls that both
/// widen would leave the user nothing that narrows.
///
/// <b>Category is exact, and case-sensitively so.</b> The vocabulary is fixed -- the instrument's own 34 tone
/// categories, as <c>Integra7Preset</c> parses them -- and the control is a drop-down of them rather than a
/// text box, so the value being compared came from that list rather than from a keyboard. A loose match would
/// conflate "Piano" with "Ac.Piano" for no gain. A Studio Set has no category, so it is admitted by "any
/// category" and by nothing else, which is the right answer for a file that is sixteen parts each with a
/// category of its own.</summary>
/// <param name="Text">A substring of a name, a note, a category or a tag. Blank means no text filter, and it
/// is trimmed before use -- a trailing space from a paste or a half-deleted word must not empty the
/// library.</param>
/// <param name="Kind">One of <see cref="SnapshotKinds"/>, or null for both. Compared exactly: these are the
/// strings the format itself writes, not anything a user typed.</param>
/// <param name="Category">One of the instrument's own tone categories, or null for any. Empty is also "any",
/// which means there is no filter for "the ones I have not categorised yet" -- the kind filter does that job
/// for Studio Sets, and for tones it has not been asked for.</param>
/// <param name="MinimumRating">Admit entries rated this or higher. <b>0 is "no minimum", not "unrated
/// only"</b>: the same number reads both ways, and the second reading would empty the list the moment the user
/// dragged the stars back down to none.</param>
/// <param name="FavouritesOnly">When set, admit only favourites. When clear, not a filter for the rest.</param>
/// <param name="Tags">Tags an entry must carry -- <b>all</b> of them. Null or empty is no tag filter. A tag is
/// matched whole, because it is picked from a list rather than typed, and without regard to case, because
/// "Warm" and "warm" are one tag to anybody using this.</param>
public sealed record LibraryFilter(
    string Text = "",
    string? Kind = null,
    string? Category = null,
    int MinimumRating = 0,
    bool FavouritesOnly = false,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>Asking for nothing, which admits everything. What the browser opens on, and what every "clear
    /// the filters" button should assign.</summary>
    public static readonly LibraryFilter None = new();

    /// <summary>Ordinal, ignoring case, everywhere text is compared loosely. Ordinal rather than the current
    /// culture so that the same library filters the same way on every machine -- a search that found a sound in
    /// one locale and not another would be impossible to report and worse to explain -- and ignoring case
    /// because nobody searching their own sounds is thinking about capitals. Ordinal ignore-case still folds
    /// case across the whole of Unicode, so a tag or a note in any language the user writes in still
    /// matches.</summary>
    private const StringComparison Loosely = StringComparison.OrdinalIgnoreCase;

    /// <summary>The entries admitted, in the order they were given -- sorting is the browser's business and it
    /// offers the user several. A new list rather than a lazy sequence: the caller is a list on screen that
    /// re-filters on every keystroke, and a deferred query it enumerated twice would do this work twice.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Apply(IEnumerable<LibraryEntry> entries) =>
        entries.Where(Admits).ToList();

    /// <summary>Whether one entry survives all six axes. Public because a browser holding one selected entry
    /// wants to ask about it without re-filtering the folder.</summary>
    public bool Admits(LibraryEntry entry)
    {
        var head = entry.Head;

        if (head.Rating < MinimumRating) return false;
        if (FavouritesOnly && !head.Favourite) return false;
        // Null and empty both mean "not asking". Empty matters as much as null: a drop-down's "any" row is
        // most naturally an empty string, and a kind filter that took "" literally would admit nothing at all,
        // since no snapshot's kind is empty.
        if (!string.IsNullOrEmpty(Kind) && !string.Equals(head.Kind, Kind, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(Category) && !string.Equals(head.Category, Category, StringComparison.Ordinal))
            return false;

        // All of them, not any -- see the note on the record. A blank entry in the selection is skipped rather
        // than failing everything: it is what an empty tag box contributes, and it is not something the user
        // asked for. Both sides are trimmed, and it has to be both: trimming only what the filter carries
        // would mean a stored tag that arrived with a stray space could never be selected, not even from a
        // list of tags gathered out of the library itself.
        foreach (var tag in Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var wanted = tag.Trim();
            if (!head.Tags.Any(carried => string.Equals(carried.Trim(), wanted, Loosely))) return false;
        }

        var text = Text?.Trim() ?? "";
        if (text.Length == 0) return true;

        return head.Name.Contains(text, Loosely)
               || head.Notes.Contains(text, Loosely)
               || head.Category.Contains(text, Loosely)
               || head.Tags.Any(tag => tag.Contains(text, Loosely));
    }
}
