using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What the library list is ordered by. Three, because they are the three questions a user asks of a
/// library: what is it called, how good is it, and what did I do last.</summary>
public enum LibrarySort
{
    Name,
    Rating,
    Modified,
}

/// <summary>The decisions a library browser makes that are not about files, controls or state: how the list is
/// ordered, what a rating looks like, what the filter drop-downs offer and what a chosen row of one of them
/// means, how a tag box's text becomes tags, and what the duplicate panel says a group is and asks before it
/// deletes one.
///
/// <b>Why any of this is here rather than in the view model.</b> Every one of them is a function of its
/// arguments, and every one of them is a decision somebody will want to change or will get wrong: that
/// "minimum rating: any" is 0 and not "unrated only" (which <see cref="LibraryFilter"/> also says, from the
/// other side), that a tag box splits on commas and drops blanks, that sorting descending by rating still
/// leaves equally-rated sounds in alphabetical order rather than in whatever order the file system offered
/// them. In a view model none of that is reachable by a test, because a view model here needs an Avalonia
/// application and a live device domain; here, all of it is. That is the same reason
/// <c>MixerFormatting</c>, <c>LayerMapFormatting</c> and <c>PmtZoneMapping</c> are in this folder.
///
/// <b>The filter drop-downs are strings.</b> Each offers a list of labels and answers what a label means, so
/// the browser can bind a plain <c>ComboBox</c> of strings to a plain string property -- no item template, no
/// value converter, and no choice record whose display binding the XAML compiler could only check at runtime.
/// The cost is that a label is both what is shown and what is stored; the gain is that the mapping from one to
/// the other is a pure function with a test, which is where the mistake would otherwise live.</summary>
public static class LibraryListing
{
    /// <summary>The "not filtering on this" row of each drop-down. First in every list, so a browser that opens
    /// on <c>[0]</c> opens on the whole library.</summary>
    public const string AnyKind = "Any kind";

    /// <inheritdoc cref="AnyKind"/>
    public const string AnyEngine = "Any engine";

    /// <inheritdoc cref="AnyKind"/>
    public const string AnyCategory = "Any category";

    /// <inheritdoc cref="AnyKind"/>
    public const string AnyRating = "Any rating";

    /// <summary>What a snapshot's <see cref="SnapshotKinds"/> string is called on screen. The stored strings are
    /// lower case and hyphenated because they are data (see <see cref="SnapshotKinds"/>); a list column is not
    /// the place to show that. An unrecognised kind -- a file from a build that knows one this does not -- shows
    /// itself verbatim rather than as "Unknown", because the actual word is the only useful thing to say about
    /// it, and <c>FromJson</c> will name it too when the file is opened.</summary>
    public static string KindLabel(string kind) => kind switch
    {
        SnapshotKinds.StudioSet => "Studio Set",
        SnapshotKinds.Tone => "Tone",
        _ => kind,
    };

    /// <summary>The kind drop-down: "any", then the two kinds, labelled as <see cref="KindLabel"/> labels
    /// them.</summary>
    public static IReadOnlyList<string> KindLabels { get; } =
        [AnyKind, KindLabel(SnapshotKinds.StudioSet), KindLabel(SnapshotKinds.Tone)];

    /// <summary>What a row of <see cref="KindLabels"/> means to <see cref="LibraryFilter.Kind"/>: one of the
    /// stored strings, or null for "not asking". Anything unrecognised is also null -- a filter that admitted
    /// nothing would be the worse answer to a label this method has not been taught.</summary>
    public static string? KindFromLabel(string? label) => label switch
    {
        not null when label == KindLabel(SnapshotKinds.StudioSet) => SnapshotKinds.StudioSet,
        not null when label == KindLabel(SnapshotKinds.Tone) => SnapshotKinds.Tone,
        _ => null,
    };

    /// <summary>The engine drop-down: "any", then the five engines a tone can be, in the order the instrument's
    /// own documentation lists them.
    ///
    /// <b>The codes themselves, not friendly names.</b> "SN-S" rather than "SuperNATURAL Synth", because that is
    /// what the Kind column of this same list already shows, what the part selectors show, and what the
    /// instrument prints on its own screen. A second vocabulary for the same five things would be one more thing
    /// to learn and one more place for the two to disagree. It also means <see cref="EngineFromLabel"/> is
    /// nearly the identity, which is the honest shape for a mapping that is one.</summary>
    public static IReadOnlyList<string> EngineLabels { get; } =
        [AnyEngine, "PCMS", "PCMD", "SN-S", "SN-A", "SN-D"];

    /// <summary>What a row of <see cref="EngineLabels"/> means to <see cref="LibraryFilter.Engine"/>: the code
    /// itself, or null for "not asking". Anything unrecognised is null, matching
    /// <see cref="KindFromLabel"/>: a filter that admitted nothing is the worse answer to a label this has not
    /// been taught.</summary>
    public static string? EngineFromLabel(string? label) =>
        label is not null && label != AnyEngine && ToneDomainNames.IsKnownToneType(label) ? label : null;

    /// <summary>The category drop-down: "any", then the instrument's own 34, in the instrument's own order.
    ///
    /// Not alphabetical, deliberately. It is the order the preset grids and the front panel present them in, so
    /// a user reaching for "E.Piano" reaches to the same place they always do; sorting them here would make this
    /// one list the odd one out.</summary>
    public static IReadOnlyList<string> CategoryLabels { get; } =
        [AnyCategory, ..Integra7Preset.ToneCategories];

    /// <summary>What a row of <see cref="CategoryLabels"/> means to <see cref="LibraryFilter.Category"/>: the
    /// category itself, or null for "not asking".</summary>
    public static string? CategoryFromLabel(string? label) =>
        label is null || label == AnyCategory ? null : label;

    /// <summary>The "this sound has no category" row of the editor's drop-down. A different word from
    /// <see cref="AnyCategory"/> and deliberately so: filtering by "any" and setting "none" are opposite
    /// operations that would otherwise share a label, and the one place a user meets both is this browser -- the
    /// filter bar above the editor. It is also what every Studio Set is: sixteen parts each with a category of
    /// its own and no single one to name.</summary>
    public const string NoCategory = "(none)";

    /// <summary>The category drop-down of the metadata editor: "none", then the instrument's own 34. Same order
    /// and same reasoning as <see cref="CategoryLabels"/>; only the first row differs.</summary>
    public static IReadOnlyList<string> EditCategoryLabels { get; } =
        [NoCategory, ..Integra7Preset.ToneCategories];

    /// <summary>What a row of <see cref="EditCategoryLabels"/> means to <c>SnapshotMetadata.Category</c>: the
    /// category, or "" for none. Empty rather than null, because that is what a snapshot with no category stores
    /// and what <see cref="SnapshotHead"/> reads back for one.</summary>
    public static string CategoryToWrite(string? label) =>
        label is null || label == NoCategory ? "" : label;

    /// <summary>And back: which row of <see cref="EditCategoryLabels"/> a stored category is.
    ///
    /// A category the drop-down does not offer -- a hand-edited file, or one written by a build that knew a
    /// category this one does not -- shows as "none" rather than being added to the list. That loses it on the
    /// next save, which is the lesser of the two evils: the alternative is a drop-down whose contents depend on
    /// which file is selected, where the same row means different things from one click to the next.</summary>
    public static string EditLabelForCategory(string? category) =>
        string.IsNullOrEmpty(category) || !Integra7Preset.ToneCategories.Contains(category)
            ? NoCategory
            : category;

    /// <summary>The minimum-rating drop-down. "Any rating" rather than "0 stars or more" because the number
    /// reads both ways and the wrong reading -- "unrated only" -- is the one a user would notice by their
    /// library emptying (see <see cref="LibraryFilter.MinimumRating"/>).</summary>
    private static readonly string[] Ratings =
        [AnyRating, "1 star or more", "2 stars or more", "3 stars or more", "4 stars or more", "5 stars"];

    /// <inheritdoc cref="Ratings"/>
    public static IReadOnlyList<string> RatingLabels => Ratings;

    /// <summary>What a row of <see cref="RatingLabels"/> means to <see cref="LibraryFilter.MinimumRating"/>.
    /// Answered by position rather than by parsing the label, so the words above are free to be rewritten
    /// without this having to be rewritten to match. Backed by an array, not by a collection expression typed as
    /// the interface: what a collection expression builds for <c>IReadOnlyList</c> is an implementation detail
    /// and casting it to <c>List</c> to ask for an index is exactly the kind of thing that compiles and then
    /// throws.</summary>
    public static int MinimumRatingFromLabel(string? label)
    {
        var index = label is null ? -1 : Array.IndexOf(Ratings, label);
        return index < 0 ? 0 : index;
    }

    /// <summary>The sort drop-down, in <see cref="LibrarySort"/>'s own order. "Date" rather than "Modified":
    /// the column it sorts shows a date, and that is what the user is looking at.</summary>
    private static readonly string[] Sorts = ["Name", "Rating", "Date"];

    /// <inheritdoc cref="Sorts"/>
    public static IReadOnlyList<string> SortLabels => Sorts;

    /// <summary>What a row of <see cref="SortLabels"/> means, by position -- same reasoning as
    /// <see cref="MinimumRatingFromLabel"/>. Anything unrecognised sorts by name, which is the one order that
    /// is always meaningful.</summary>
    public static LibrarySort SortFromLabel(string? label)
    {
        var index = label is null ? -1 : Array.IndexOf(Sorts, label);
        return index < 0 ? LibrarySort.Name : (LibrarySort)index;
    }

    /// <summary>Ordinal, ignoring case -- <see cref="LibraryFilter"/>'s <c>Loosely</c>, and for the same reason:
    /// the same library must order the same way on every machine, and nobody browsing their own sounds is
    /// thinking about capitals.</summary>
    private static readonly StringComparer ByName = StringComparer.OrdinalIgnoreCase;

    /// <summary><paramref name="entries"/> in the order the browser should show them.
    ///
    /// <b>Ties break by name and then by path, in both directions.</b> Most of a library is unrated and saved
    /// within the same minute, so the primary key is equal far more often than not -- and an order that fell
    /// back on whatever <c>Directory.EnumerateFiles</c> offered would rearrange itself on a refresh, which is
    /// the kind of thing that makes a list feel broken without ever being wrong. Reversing only the primary key
    /// is what keeps "worst first" alphabetical rather than reverse-alphabetical within each rating.
    ///
    /// The path is the last resort because it is the one thing that cannot tie: two snapshots can share
    /// everything else, and two files cannot share a path.</summary>
    public static IReadOnlyList<LibraryEntry> Sort(IEnumerable<LibraryEntry> entries, LibrarySort sort,
        bool descending)
    {
        IOrderedEnumerable<LibraryEntry> ordered = sort switch
        {
            LibrarySort.Rating => descending
                ? entries.OrderByDescending(e => e.Head.Rating)
                : entries.OrderBy(e => e.Head.Rating),
            LibrarySort.Modified => descending
                ? entries.OrderByDescending(e => e.Modified)
                : entries.OrderBy(e => e.Modified),
            _ => descending
                ? entries.OrderByDescending(e => e.Head.Name, ByName)
                : entries.OrderBy(e => e.Head.Name, ByName),
        };

        return ordered.ThenBy(e => e.Head.Name, ByName).ThenBy(e => e.FilePath, ByName).ToList();
    }

    /// <summary>A rating as five stars, filled up to <paramref name="rating"/> -- or nothing at all when it is
    /// unrated.
    ///
    /// Five glyphs rather than <paramref name="rating"/> of them so that three and four are told apart by which
    /// stars are filled rather than by the width of a column; the eye is much better at the first. Unrated is
    /// blank rather than five hollow stars because most of a fresh library is unrated, and a column of forty
    /// identical ghost ratings is noise standing in for information.</summary>
    public static string Stars(int rating)
    {
        if (rating <= 0) return "";
        var filled = Math.Min(rating, 5);
        return new string('★', filled) + new string('☆', 5 - filled);
    }

    /// <summary>Every tag anywhere in <paramref name="entries"/>, once each, in alphabetical order -- what a tag
    /// filter offers.
    ///
    /// <b>Gathered from the library rather than from a list of known tags</b>, because there is no such list:
    /// tags are free text and live in the files (see <see cref="Integra7Snapshot.Tags"/>). So a tag exists
    /// exactly as long as something carries it, a folder copied from another machine brings its own vocabulary
    /// with it, and removing the last use of a tag removes the tag.
    ///
    /// Trimmed and de-duplicated without regard to case, matching how <see cref="LibraryFilter"/> compares them
    /// -- otherwise "Warm" and "warm" would be two rows in the filter that select the same entries. The spelling
    /// kept is the first one met in alphabetical order, which is arbitrary but stable.</summary>
    public static IReadOnlyList<string> AllTags(IEnumerable<LibraryEntry> entries) =>
        entries.SelectMany(e => e.Head.Tags)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(ByName)
            .OrderBy(tag => tag, ByName)
            .ToList();

    /// <summary>The tags in a comma-separated box: trimmed, blanks dropped, duplicates dropped, in the order
    /// they were typed.
    ///
    /// <b>Commas, and only commas.</b> A tag is meant to be able to say "for the trio gig", so splitting on
    /// spaces would make three tags out of one phrase and there would be no way to write the phrase at all.
    /// Blanks are dropped because "warm, , gig" and a trailing comma are what a box being edited looks like
    /// half the time, and neither is a request for an empty tag. Order is kept rather than sorted because it is
    /// the user's own text and they will look at it again.</summary>
    public static IReadOnlyList<string> ParseTags(string? text) =>
        (text ?? "").Split(',')
        .Select(tag => tag.Trim())
        .Where(tag => tag.Length > 0)
        .Distinct(ByName)
        .ToList();

    /// <summary>Tags as one line of text, which is what <see cref="ParseTags"/> reads back and what a list
    /// column shows. ", " rather than "," so that a long list wraps somewhere.</summary>
    public static string FormatTags(IEnumerable<string> tags) => string.Join(", ", tags);

    /// <summary>What a duplicate scan found, and -- in the same breath -- what a group actually promises.
    ///
    /// <b>The second sentence is not decoration.</b> <see cref="DuplicateGroups"/> is transitive on purpose:
    /// a group is every patch reachable from every other by steps of at most the threshold, so two members at
    /// opposite ends of one may differ by a great deal more than the number in the drop-down. The only honest
    /// thing to say is "at least one other", and a panel that let the user read "these are all within 5 of
    /// each other" would be inviting them to delete on a promise nobody made.
    ///
    /// <b>A threshold of nothing gets its own sentence</b> rather than a nought dropped into the general one:
    /// "differs in at most 0 parameters" is a puzzle where "identical" is a fact. And finding nothing says
    /// what was looked for, because "no duplicates" alone leaves a user unable to tell a tidy library from a
    /// threshold set too tight -- which is the one thing they can do something about.</summary>
    public static string DuplicateSummary(int groups, int patches, int threshold)
    {
        if (groups == 0)
            return threshold == 0
                ? "No two snapshots here are identical."
                : $"No two snapshots here differ in {threshold} " +
                  $"{(threshold == 1 ? "parameter" : "parameters")} or fewer.";

        var promise = threshold == 0
            ? "Each of these is identical to at least one other in its group."
            : $"Each of these differs in at most {threshold} " +
              $"{(threshold == 1 ? "parameter" : "parameters")} from at least one other in its group.";

        return $"{groups} {(groups == 1 ? "group" : "groups")}, {patches} snapshots. {promise}";
    }

    /// <summary>What the duplicate panel asks before it removes the ticked snapshots.
    ///
    /// The history sentence is <c>LibraryViewModel</c>'s own, word for word, because it is the same promise
    /// about the same folder and a user who reads it twice should not have to work out whether the two mean
    /// the same thing.
    ///
    /// <b><paramref name="emptiedGroups"/> is the warning this panel needs and the others do not.</b>
    /// Everywhere else a user deletes snapshots they chose one at a time; here they are working through
    /// families of near-identical sounds with a tick box, and ticking a whole family -- four rows that all
    /// look alike -- is the natural gesture and the one that loses the sound. The copies in the history folder
    /// are still there, but nobody tidying a library is thinking about the history folder.</summary>
    public static string DuplicateDeleteQuestion(int ticked, int emptiedGroups)
    {
        var question = ticked == 1
            ? "Delete 1 snapshot from the library? A copy is kept in the history folder beside your library."
            : $"Delete {ticked} snapshots from the library? A copy of each is kept in the history folder " +
              "beside your library.";

        if (emptiedGroups <= 0) return question;

        return emptiedGroups == 1
            ? question + " That empties one of the groups, so nothing of that sound would be left in the " +
              "library."
            : question + $" That empties {emptiedGroups} of the groups, so nothing of those sounds would be " +
              "left in the library.";
    }
}
