using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter found inside one snapshot: which file it was in, what to show as the reason, and how
/// that file stood when it was read.
///
/// <b><paramref name="Modified"/> is what makes the hit about a file's contents rather than about its name.</b>
/// A path is not an identity over time. A version restored through <see cref="PatchHistory"/> overwrites the
/// file in place, so the same path can hold a different sound; and <c>SnapshotLibrary.UniquePath</c> only
/// suffixes a name that is <i>currently</i> taken, so deleting a snapshot frees its name for the next save --
/// which is the delete-a-duplicate-then-save-again workflow this whole phase exists to support. Carrying the
/// last-write time along means a hit stops applying the moment the file it describes is no longer the file that
/// was read, rather than captioning a stranger with a parameter it may not have.</summary>
/// <param name="FilePath">The file the parameter was found in, as the listing spells it.</param>
/// <param name="Reason">What to show, as "parameter path = displayed value" -- see
/// <see cref="SnapshotTextScan"/>, which produces both halves.</param>
/// <param name="Modified">The file's last-write time as the listing had it when the file was read.</param>
public sealed record DeepSearchHit(string FilePath, string Reason, DateTime Modified);

/// <summary>What one deep search found, and the exact question it answers.
///
/// <b>The whole filter is the key, not the search text.</b> The files that were read were chosen by all seven
/// axes -- only the entries the other six admitted and the text did not are worth opening -- so the hits are
/// evidence about that set and no other. Keyed on the text alone, widening any other axis afterwards (engine
/// back to any, a lower minimum rating, a tag unticked) would leave the browser answering a bigger question
/// with a smaller search and silently omitting rows, which is the one failure a search must not have: the user
/// cannot tell it from "there is nothing there".
///
/// The consequence is deliberate: a <i>narrowing</i> also invalidates the answer, even though the hits would
/// still have been sound. That costs one more press of the button. Telling a narrowing from a widening means a
/// rule per axis, each of them a fresh chance to let a widening through unnoticed, and the failure would be
/// silent while the cost of strictness is visible and recoverable.</summary>
public sealed record DeepSearchAnswer(LibraryFilter Asked, IReadOnlyList<DeepSearchHit> Hits);

/// <summary>What a browser should show once a deep search's hits are folded into its filter.</summary>
/// <param name="Admitted">The entries to list, in the order they were given -- sorting is the browser's
/// business and it offers the user several.</param>
/// <param name="Reasons">Why a row is there, by file path, for the rows a hit was used for. Rows the metadata
/// admitted are simply absent from it.</param>
/// <param name="AnswersAnotherQuestion">The hits were found under a different filter and were therefore not
/// used at all. Answered rather than fixed here, because what to do about it -- drop the answer, say so -- is
/// the browser's to do and its user's to see.</param>
/// <param name="Changed">The files a hit was dropped for because they have been written since they were read.
/// Named rather than counted so that the caller can forget exactly those hits and stop saying it.</param>
public sealed record DeepSearchListing(
    IReadOnlyList<LibraryEntry> Admitted,
    IReadOnlyDictionary<string, string> Reasons,
    bool AnswersAnotherQuestion,
    IReadOnlyList<string> Changed);

/// <summary>The arithmetic of searching inside patches: which files are worth opening, whether an answer still
/// answers the question on screen, and which entries a set of hits admits.
///
/// <b>No file is opened here.</b> Reading one is <see cref="SnapshotTextScan"/>'s, and doing it for a folder
/// belongs to whoever can do it off the UI thread. What is left is set arithmetic over
/// <see cref="LibraryEntry"/> records and one comparison of two filters -- which is exactly the part that can
/// be got wrong quietly, so it is here, beside <see cref="LibraryFilter"/>, where a test can reach it.
///
/// <b>It widens the text axis and nothing else.</b> An entry is admitted when it passes every other axis and
/// the text matches its metadata <i>or</i> any of its parameter values. So looking inside patches can only ever
/// add rows to what <see cref="LibraryFilter"/> already admitted, which is what a user expects of a box that
/// says "look inside patches too", and <see cref="LibraryFilter"/> stays pure over heads and learns
/// nothing.</summary>
public static class DeepSearch
{
    /// <summary>Ordinal, ignoring case, wherever a path or a piece of text is compared -- <see
    /// cref="LibraryFilter"/>'s own rule, for its own reason: the same library must answer the same way on
    /// every machine.</summary>
    private static readonly StringComparer Loosely = StringComparer.OrdinalIgnoreCase;

    private static readonly IReadOnlyDictionary<string, string> NoReasons = new Dictionary<string, string>();

    /// <summary>The files worth opening for <paramref name="filter"/>: the entries its other axes admit, less
    /// the ones its text already matches.
    ///
    /// <b>The rows already on screen are not read.</b> They are on screen whatever is inside them, so opening
    /// them could only find a second reason for something that needs none -- and a row the kind or the engine
    /// axis excluded is not wanted at any price. So the narrower the other axes, the less there is to read,
    /// and a user who has narrowed to one engine has narrowed the folder read with it.</summary>
    public static IReadOnlyList<LibraryEntry> Candidates(LibraryFilter filter,
        IReadOnlyList<LibraryEntry> entries)
    {
        var byMetadata = filter.Apply(entries).Select(e => e.FilePath).ToHashSet(Loosely);
        return (filter with { Text = "" }).Apply(entries)
            .Where(entry => !byMetadata.Contains(entry.FilePath))
            .ToList();
    }

    /// <summary>Whether two filters ask the same question, so that an answer to one is an answer to the other.
    ///
    /// <b>Not <c>==</c>, and this is the reason it is a method.</b> <see cref="LibraryFilter"/> is a record, so
    /// its generated equality compares <see cref="LibraryFilter.Tags"/> with
    /// <c>EqualityComparer&lt;IReadOnlyList&lt;string&gt;&gt;.Default</c> -- reference equality for a list --
    /// and a browser builds a fresh list of ticked tags every time it filters. Two identical questions would
    /// therefore compare unequal, and a cache keyed on <c>==</c> would never be used at all: the feature would
    /// look as though it had found nothing rather than as though it were broken.
    ///
    /// Each axis is compared the way <see cref="LibraryFilter.Admits"/> uses it, and no more strictly. Text is
    /// trimmed and compared ignoring case, because that is how it is matched -- correcting the capitals of a
    /// word does not change which patches contain it. Null and empty are one value on the three exact axes,
    /// because both mean "not asking" there. Tags are a set: they are AND-ed and matched trimmed and without
    /// regard to case, so their order, their padding and their duplicates change nothing about the
    /// answer.</summary>
    public static bool SameQuestion(LibraryFilter a, LibraryFilter b) =>
        (a.Text ?? "").Trim().Equals((b.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        && SameAxis(a.Kind, b.Kind)
        && SameAxis(a.Engine, b.Engine)
        && SameAxis(a.Category, b.Category)
        && a.MinimumRating == b.MinimumRating
        && a.FavouritesOnly == b.FavouritesOnly
        && TagSet(a.Tags).SetEquals(TagSet(b.Tags));

    /// <summary>Fold <paramref name="answer"/> into what <paramref name="asking"/> admits, and say what could
    /// not be used.
    ///
    /// A hit is used when the answer was given to this same question, the file is still in the listing, it has
    /// not been written since it was read, and it still passes the axes other than the text. Anything else is
    /// left out rather than guessed at -- an answer that might be about a different sound is worse than no
    /// answer, because the reason shown beside the row is what the user is being invited to trust.</summary>
    public static DeepSearchListing Widen(LibraryFilter asking, IReadOnlyList<LibraryEntry> entries,
        DeepSearchAnswer? answer)
    {
        var matched = asking.Apply(entries);
        if (answer is null) return new DeepSearchListing(matched, NoReasons, false, []);
        // Before the hits are counted, not after: an answer that found nothing is still an answer about a set
        // of files, and "nothing inside these matches" says nothing at all about the ones never opened.
        if (!SameQuestion(answer.Asked, asking))
            return new DeepSearchListing(matched, NoReasons, true, []);

        // The listing is the authority on what is in the folder and how it stands. Built as a loop rather than
        // with ToDictionary because two entries sharing a path would throw there, and a browser must not fall
        // over on whatever a folder turns out to hold.
        Dictionary<string, LibraryEntry> byPath = new(Loosely);
        foreach (var entry in entries) byPath[entry.FilePath] = entry;

        var others = asking with { Text = "" };
        var admitted = matched.Select(e => e.FilePath).ToHashSet(Loosely);
        Dictionary<string, string> reasons = new(Loosely);
        List<string> changed = [];

        foreach (var hit in answer.Hits)
        {
            // Gone from the folder: not a row, and not a complaint either. A file the user has deleted is not
            // something to be told about again.
            if (!byPath.TryGetValue(hit.FilePath, out var entry)) continue;

            if (entry.Modified != hit.Modified)
            {
                changed.Add(hit.FilePath);
                continue;
            }

            // Belt and braces. SameQuestion means this entry passed the other axes when it was chosen to be
            // read, and a head that changed since would have moved the timestamp above with it -- but what is
            // admitted must not rest on that second argument holding, because loosening the staleness rule
            // above would then quietly loosen this too.
            if (!others.Admits(entry)) continue;

            reasons[entry.FilePath] = hit.Reason;
            admitted.Add(entry.FilePath);
        }

        // Filtered out of the listing rather than appended, so that the answer is in the order it was given
        // -- LibraryFilter's own contract -- and so that two hits for one path cannot become two rows.
        return new DeepSearchListing(
            entries.Where(entry => admitted.Contains(entry.FilePath)).ToList(), reasons, false, changed);
    }

    /// <summary>One of the three axes that are compared exactly, where null and empty both mean "not
    /// asking".</summary>
    private static bool SameAxis(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>The tags a filter is actually asking for: trimmed, blanks dropped, case folded -- exactly what
    /// <see cref="LibraryFilter.Admits"/> asks of them.</summary>
    private static HashSet<string> TagSet(IReadOnlyList<string>? tags) =>
        (tags ?? []).Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToHashSet(Loosely);
}
