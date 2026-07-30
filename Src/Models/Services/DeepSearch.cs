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

/// <summary>What one deep search found, the text it was looking for, and every file it opened looking.
///
/// <b>The answer is about the files that were read, and that is what it carries.</b> An answer is worth using
/// again exactly when every file that would have to be read <i>now</i> was read <i>then</i> and the text has
/// not changed (see <see cref="DeepSearch.Answers"/>). That one rule covers every way a browse can move: a
/// filter narrowed asks about fewer files, so the answer still covers it and costs nothing; a filter widened --
/// the engine back to any, a lower minimum rating, a tag unticked, a category cleared -- asks about files that
/// were never opened, and so does a folder that has gained a snapshot since. Answering any of those with these
/// hits omits rows silently, which is the one failure a search must not have: the user cannot tell it from
/// "there is nothing there".
///
/// <b>Keyed on what was read rather than on the filter that chose it</b>, for two reasons. It is stronger: the
/// filter being identical says nothing about a file that has appeared in the folder since, and a refresh is
/// how one gets there. And it is one rule rather than seven -- a per-axis "is this a narrowing" test would be
/// seven fresh chances to let a widening through, and the failure would be the silent one.
///
/// (Comparing two <see cref="LibraryFilter"/> records with <c>==</c> would not have worked in any case:
/// <see cref="LibraryFilter.Tags"/> is a list, records compare lists by reference, and a browser builds a
/// fresh one every time it filters. Two identical questions would have compared unequal and the answer would
/// never have been used at all -- which looks exactly like a search that finds nothing.)</summary>
/// <param name="Text">What was searched for, as it was searched for: trimmed, and matched ignoring case.</param>
/// <param name="Read">Every file that was opened. The set the hits are evidence about; without it, the hits
/// are only evidence about themselves.</param>
/// <param name="Hits">The files something was found in, and what.</param>
public sealed record DeepSearchAnswer(string Text, IReadOnlyList<string> Read,
    IReadOnlyList<DeepSearchHit> Hits);

/// <summary>What a browser should show once a deep search's hits are folded into its filter.</summary>
/// <param name="Admitted">The entries to list, in the order they were given -- sorting is the browser's
/// business and it offers the user several.</param>
/// <param name="Reasons">Why a row is there, by file path, for the rows a hit was used for. Rows the metadata
/// admitted are simply absent from it.</param>
/// <param name="AnswersAnotherQuestion">The answer did not cover what is being asked -- a different text, a
/// widened filter, or a folder that has gained a file -- so none of its hits was used. Answered rather than
/// acted on here, because what to do about it -- drop the answer, say so -- is the browser's to do and its
/// user's to see.</param>
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
/// <see cref="LibraryEntry"/> records -- which is exactly the part that can be got wrong quietly, so it is
/// here, beside <see cref="LibraryFilter"/>, where a test can reach it.
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
        IReadOnlyList<LibraryEntry> entries) =>
        Candidates(filter, entries, filter.Apply(entries));

    /// <summary>Whether <paramref name="answer"/> still answers what <paramref name="asking"/> is asking of
    /// <paramref name="entries"/>: the same text, and nothing to read that was not read.
    ///
    /// <b>The subset is the whole rule</b>, and <see cref="DeepSearchAnswer"/> says why it is that rather than
    /// a comparison of filters. A narrowing asks about fewer files and is therefore free -- which matters,
    /// because narrowing after a search is what a user does with a result: found twelve, now show me the SN-S
    /// ones. A widening, and a folder that has gained a file, ask about files nobody opened, and are refused.
    ///
    /// The text is compared trimmed and ignoring case because that is how it is matched: correcting the
    /// capitals of a word does not change which patches contain it, and a search box that had to be re-run for
    /// a capital would be a search box that felt broken.</summary>
    public static bool Answers(DeepSearchAnswer answer, LibraryFilter asking,
        IReadOnlyList<LibraryEntry> entries) =>
        Answers(answer, asking, entries, asking.Apply(entries));

    /// <summary>Fold <paramref name="answer"/> into what <paramref name="asking"/> admits, and say what could
    /// not be used.
    ///
    /// A hit is used when the answer covers what is being asked, the file is still in the listing, it has not
    /// been written since it was read, and it still passes the axes other than the text. Anything else is left
    /// out rather than guessed at -- an answer that might be about a different sound is worse than no answer,
    /// because the reason shown beside the row is what the user is being invited to trust.</summary>
    public static DeepSearchListing Widen(LibraryFilter asking, IReadOnlyList<LibraryEntry> entries,
        DeepSearchAnswer? answer)
    {
        var matched = asking.Apply(entries);
        if (answer is null) return new DeepSearchListing(matched, NoReasons, false, []);
        // Before the hits are counted, not after: an answer that found nothing is still an answer about a set
        // of files, and "nothing inside these matches" says nothing at all about the ones never opened.
        if (!Answers(answer, asking, entries, matched))
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

            // Load-bearing, and the price of letting a narrowing keep the answer: these hits were found under
            // a filter this one is allowed to be narrower than, so some of them are for entries the axes now
            // exclude. Without this, narrowing to one engine would bring back the other engines' patches
            // through the very hits that made narrowing worth doing.
            if (!others.Admits(entry)) continue;

            reasons[entry.FilePath] = hit.Reason;
            admitted.Add(entry.FilePath);
        }

        // Filtered out of the listing rather than appended, so that the answer is in the order it was given
        // -- LibraryFilter's own contract -- and so that two hits for one path cannot become two rows.
        return new DeepSearchListing(
            entries.Where(entry => admitted.Contains(entry.FilePath)).ToList(), reasons, false, changed);
    }

    /// <summary>The two questions that are asked once each per filtering, taking the metadata matches they both
    /// need rather than working them out twice. The browser re-filters on every keystroke, and this runs inside
    /// that.</summary>
    private static IReadOnlyList<LibraryEntry> Candidates(LibraryFilter filter,
        IReadOnlyList<LibraryEntry> entries, IReadOnlyList<LibraryEntry> matched)
    {
        var byMetadata = matched.Select(entry => entry.FilePath).ToHashSet(Loosely);
        return (filter with { Text = "" }).Apply(entries)
            .Where(entry => !byMetadata.Contains(entry.FilePath))
            .ToList();
    }

    /// <inheritdoc cref="Answers(DeepSearchAnswer, LibraryFilter, IReadOnlyList{LibraryEntry})"/>
    private static bool Answers(DeepSearchAnswer answer, LibraryFilter asking,
        IReadOnlyList<LibraryEntry> entries, IReadOnlyList<LibraryEntry> matched)
    {
        if (!answer.Text.Trim().Equals((asking.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var read = answer.Read.ToHashSet(Loosely);
        return Candidates(asking, entries, matched).All(entry => read.Contains(entry.FilePath));
    }
}
