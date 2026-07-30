using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Searching inside patches: which files are worth opening, whether an answer is still evidence about
/// what is being asked, and which entries its hits admit.
///
/// <b>An answer is about the files it opened.</b> That is the decision these tests exist for. It is worth using
/// again exactly when every file that would have to be read now was read then and the text has not changed --
/// so a filter narrowed afterwards is free, while a filter widened, or a folder that has gained a snapshot,
/// asks about files nobody opened. Answer one of those with these hits and rows go missing with nothing on
/// screen to say so, which is the one failure a search must not have: the user cannot tell "there is nothing
/// there" from "I did not look".
///
/// The other half is that a path is not an identity over time. A restored version overwrites a file in place,
/// and a deleted snapshot frees its name for the next save, so a hit that outlived the bytes it was found in
/// would caption a stranger with a parameter it may not have.</summary>
public class DeepSearchTests
{
    private static readonly DateTime Read = new(2026, 7, 29, 19, 40, 0);

    private static LibraryEntry Tone(string name, string category = "", string[]? tags = null,
        string notes = "", int rating = 0, bool favourite = false, string engine = "SN-S",
        DateTime? modified = null) =>
        new($"{name}.json",
            new SnapshotHead(name, SnapshotKinds.Tone, engine, category, tags ?? [], notes, rating, favourite),
            modified ?? Read);

    private static DeepSearchHit Hit(string name, string reason = "Partial 1/OSC Wave = SuperSaw",
        DateTime? modified = null) =>
        new($"{name}.json", reason, modified ?? Read);

    /// <summary>An answer as the browser builds one: what was looked for, every file that was opened looking,
    /// and what was found. The files read default to the ones the filter would have chosen, because that is
    /// what a real search reads and the exceptions are what the tests below are about.</summary>
    private static DeepSearchAnswer Answer(LibraryFilter asked, IEnumerable<DeepSearchHit> hits,
        IReadOnlyList<LibraryEntry>? library = null) =>
        new(asked.Text.Trim(),
            DeepSearch.Candidates(asked, library ?? Everything).Select(e => e.FilePath).ToList(),
            hits.ToList());

    private static IReadOnlyList<string> Names(IEnumerable<LibraryEntry> entries) =>
        entries.Select(e => e.Head.Name).ToList();

    /// <summary>A library where nothing's metadata mentions a saw, so every row a deep search adds is one only
    /// the pass could have found.</summary>
    private static readonly LibraryEntry[] Everything =
    [
        Tone("Warm Rhodes", "E.Piano", ["warm"], "less bark", 4, true),
        Tone("Glass Bell", "Bell", rating: 2),
        Tone("Concert Grand", "Ac.Piano", engine: "SN-A", rating: 5),
        Tone("Old Pad", "Synth Pad", engine: "PCMS"),
    ];

    // ---- what is worth opening ------------------------------------------------------------------------

    /// <summary>The rows already on screen are not read: they are on screen whatever is inside them. Nor is
    /// anything the other axes excluded, at any price.</summary>
    [Test]
    public void Only_what_the_other_axes_admit_and_the_text_did_not_match_is_worth_reading()
    {
        var candidates = DeepSearch.Candidates(new LibraryFilter(Text: "rhodes"), Everything);

        Assert.That(Names(candidates), Is.EqualTo(new[] { "Glass Bell", "Concert Grand", "Old Pad" }),
            "everything the text did not already match");

        candidates = DeepSearch.Candidates(new LibraryFilter(Text: "rhodes", Engine: "SN-S"), Everything);

        Assert.That(Names(candidates), Is.EqualTo(new[] { "Glass Bell" }),
            "narrowing the other axes narrows the folder read with it");
    }

    [Test]
    public void Nothing_is_worth_reading_when_the_text_already_matches_everything_admitted()
    {
        Assert.That(DeepSearch.Candidates(new LibraryFilter(Text: "e", Engine: "SN-A"), Everything), Is.Empty,
            "the one SN-A tone matches \"e\" on its name, so opening it could only find a second reason");
    }

    // ---- whether an answer is still evidence -----------------------------------------------------------

    /// <summary>The finding this class was written for, one axis at a time. Every one of these is a filter
    /// being <i>widened</i> after a search, which asks about files that were never opened: the browser would
    /// have listed the one patch it happened to have read and silently left out every other patch that says
    /// the same thing inside.</summary>
    [Test]
    public void An_answer_does_not_cover_a_filter_widened_after_it()
    {
        Assert.Multiple(() =>
        {
            AssertWidening(new LibraryFilter("saw", Engine: "SN-S"), f => f with { Engine = null },
                "any engine");
            AssertWidening(new LibraryFilter("saw", MinimumRating: 4), f => f with { MinimumRating = 0 },
                "a lower minimum rating");
            AssertWidening(new LibraryFilter("saw", FavouritesOnly: true), f => f with { FavouritesOnly = false },
                "favourites unticked");
            AssertWidening(new LibraryFilter("saw", Category: "E.Piano"), f => f with { Category = null },
                "any category");
            AssertWidening(new LibraryFilter("saw", Tags: ["warm"]), f => f with { Tags = [] },
                "the last tag unticked");
        });
    }

    /// <summary>And the other direction is free, which is what keying on the files read rather than on the
    /// filter buys. Narrowing after a search is what a user does with a result -- twelve found, now show me
    /// the SN-S ones -- and a rule that re-read the folder for it would punish the gesture the feature is
    /// for.</summary>
    [Test]
    public void An_answer_covers_a_filter_narrowed_after_it()
    {
        var asked = new LibraryFilter(Text: "saw", Engine: "SN-S");
        var answer = Answer(asked, [Hit("Glass Bell")]);

        Assert.Multiple(() =>
        {
            Assert.That(DeepSearch.Answers(answer, asked with { MinimumRating = 4 }, Everything), Is.True,
                "a higher minimum rating");
            Assert.That(DeepSearch.Answers(answer, asked with { Category = "Bell" }, Everything), Is.True,
                "a category chosen");
            Assert.That(DeepSearch.Answers(answer, asked with { Tags = ["warm"] }, Everything), Is.True,
                "a tag ticked");
            Assert.That(DeepSearch.Answers(answer, asked with { FavouritesOnly = true }, Everything), Is.True,
                "favourites only");
            Assert.That(DeepSearch.Answers(answer, asked, Everything), Is.True, "and no change at all");
        });
    }

    /// <summary>The price of letting a narrowing keep the answer, and the reason Widen re-applies the other
    /// axes to every hit: these hits were found under a wider filter, so some of them are for patches the
    /// narrowed one excludes. Narrowing to one engine must not bring the other engines back through the very
    /// hits that made narrowing worth doing.</summary>
    [Test]
    public void A_narrowed_filter_still_narrows_the_rows_its_own_hits_would_have_added()
    {
        var asked = new LibraryFilter(Text: "saw");
        var answer = Answer(asked, [Hit("Glass Bell"), Hit("Concert Grand")]);

        var listing = DeepSearch.Widen(asked with { Engine = "SN-A" }, Everything, answer);

        Assert.That(listing.AnswersAnotherQuestion, Is.False, "the answer still covers the narrower filter");
        Assert.That(Names(listing.Admitted), Is.EqualTo(new[] { "Concert Grand" }));
        Assert.That(listing.Reasons.ContainsKey("Glass Bell.json"), Is.False,
            "and the hit that is no longer admitted explains nothing, since there is no row to explain");
    }

    /// <summary>The rule is about files and not about labels, which is why this is allowed: every snapshot in
    /// this folder is a tone, so clearing the kind asks about nothing that was not already read. Comparing
    /// filters would have refused it and re-read the folder to produce the same answer.</summary>
    [Test]
    public void A_filter_widened_where_the_folder_has_nothing_new_to_read_is_still_covered()
    {
        var asked = new LibraryFilter(Text: "saw", Kind: SnapshotKinds.Tone);

        Assert.That(DeepSearch.Answers(Answer(asked, []), asked with { Kind = null }, Everything), Is.True);
    }

    /// <summary>The half a filter cannot see: the folder itself changing. A snapshot saved or dropped in while
    /// the box is ticked is a file nobody has looked inside, and the filter that asks about it is identical to
    /// the one that did not.</summary>
    [Test]
    public void An_answer_does_not_cover_a_folder_that_has_gained_a_file()
    {
        var asked = new LibraryFilter(Text: "saw");
        var answer = Answer(asked, [Hit("Old Pad")]);
        LibraryEntry[] afterARefresh = [..Everything, Tone("New Arrival", "Synth Lead")];

        Assert.That(DeepSearch.Answers(answer, asked, afterARefresh), Is.False);
        Assert.That(DeepSearch.Widen(asked, afterARefresh, answer).AnswersAnotherQuestion, Is.True);
    }

    /// <summary>Losing one is not the same as gaining one: there is nothing left to read, so the answer still
    /// covers the question. Deleting a snapshot must not throw away a search of the fifty that are left.
    /// </summary>
    [Test]
    public void An_answer_still_covers_a_folder_that_has_lost_a_file()
    {
        var asked = new LibraryFilter(Text: "saw");
        var answer = Answer(asked, [Hit("Old Pad")]);

        Assert.That(DeepSearch.Answers(answer, asked, Everything.Where(e => e.Head.Name != "Glass Bell")
            .ToList()), Is.True);
    }

    /// <summary>Text is matched trimmed and ignoring case, so it is compared that way: correcting the capitals
    /// of a word does not change which patches contain it, and a search box that had to be re-run for a
    /// capital would be a search box that felt broken.</summary>
    [Test]
    public void An_answer_covers_the_same_text_trimmed_and_case_folded_and_no_other()
    {
        var asked = new LibraryFilter(Text: "saw");
        var answer = Answer(asked, [Hit("Old Pad")]);

        Assert.Multiple(() =>
        {
            Assert.That(DeepSearch.Answers(answer, asked with { Text = " SAW " }, Everything), Is.True);
            Assert.That(DeepSearch.Answers(answer, asked with { Text = "sawtooth" }, Everything), Is.False,
                "a longer word is a different search, whatever it starts with");
            Assert.That(DeepSearch.Answers(answer, asked with { Text = "" }, Everything), Is.False);
        });
    }

    /// <summary>One widening, asserted from both sides: the answer stops covering the question, and the
    /// listing says so rather than quietly answering with what it has.</summary>
    private static void AssertWidening(LibraryFilter asked, Func<LibraryFilter, LibraryFilter> widen,
        string what)
    {
        var answer = Answer(asked, [Hit("Warm Rhodes")]);
        var wider = widen(asked);

        Assert.That(DeepSearch.Answers(answer, asked, Everything), Is.True, $"{what}: covered before");
        Assert.That(DeepSearch.Answers(answer, wider, Everything), Is.False, $"{what}: not after");
        Assert.That(DeepSearch.Widen(wider, Everything, answer).Admitted, Is.Empty,
            $"{what}: and no row is admitted on the strength of it");
    }

    // ---- what the hits admit ---------------------------------------------------------------------------

    [Test]
    public void A_hit_adds_a_row_and_carries_the_reason_it_was_found_for()
    {
        var asking = new LibraryFilter(Text: "saw");
        var answer = Answer(asking, [Hit("Old Pad")]);

        var listing = DeepSearch.Widen(asking, Everything, answer);

        Assert.That(Names(listing.Admitted), Is.EqualTo(new[] { "Old Pad" }));
        Assert.That(listing.Reasons["Old Pad.json"], Is.EqualTo("Partial 1/OSC Wave = SuperSaw"));
        Assert.That(listing.AnswersAnotherQuestion, Is.False);
        Assert.That(listing.Changed, Is.Empty);
    }

    /// <summary>Looking inside patches can only ever add rows. The hits here are for files the text axis had
    /// already admitted and for one it had not, and every row the plain filter admitted is still there.
    /// </summary>
    [Test]
    public void Hits_only_ever_add_rows()
    {
        var asking = new LibraryFilter(Text: "rhodes");
        var answer = Answer(asking, [Hit("Old Pad"), Hit("Glass Bell")]);

        var listing = DeepSearch.Widen(asking, Everything, answer);

        Assert.That(Names(listing.Admitted),
            Is.EqualTo(new[] { "Warm Rhodes", "Glass Bell", "Old Pad" }),
            "the metadata match kept, the two hits added, and all of it in the order the folder was given in");
    }

    /// <summary>A hit for a row the metadata already matched still explains itself -- the reason is a fact
    /// about the file -- and does not become a second row.</summary>
    [Test]
    public void A_hit_for_a_row_already_admitted_is_a_reason_and_not_a_duplicate()
    {
        var asking = new LibraryFilter(Text: "rhodes");
        var answer = Answer(asking,
            [Hit("Warm Rhodes"), Hit("Warm Rhodes", "Tone Name = Rhodes")]);

        var listing = DeepSearch.Widen(asking, Everything, answer);

        Assert.That(Names(listing.Admitted), Is.EqualTo(new[] { "Warm Rhodes" }));
        Assert.That(listing.Reasons, Has.Count.EqualTo(1));
    }

    /// <summary>The headline case. The hits were found while the engine axis was narrowed to SN-S, so they are
    /// evidence about the SN-S tones and about nothing else; asking with the engine cleared is a bigger
    /// question, and answering it with these hits would list the one SN-S patch that mentions a saw and quietly
    /// omit every PCM and SN-A patch that does.</summary>
    [Test]
    public void A_hit_found_under_a_narrower_filter_is_not_evidence_about_a_wider_one()
    {
        var narrow = new LibraryFilter(Text: "saw", Engine: "SN-S");
        var answer = Answer(narrow, [Hit("Glass Bell")]);

        var listing = DeepSearch.Widen(narrow with { Engine = null }, Everything, answer);

        Assert.That(listing.AnswersAnotherQuestion, Is.True);
        Assert.That(listing.Admitted, Is.Empty, "nothing's metadata mentions a saw, and no hit was used");
        Assert.That(listing.Reasons, Is.Empty);
        Assert.That(DeepSearch.Widen(narrow, Everything, answer).AnswersAnotherQuestion, Is.False,
            "and the same answer to the same question is used, so this is not simply refusing everything");
    }

    /// <summary>An answer that found nothing is still an answer about a set of files: "nothing inside these
    /// mentions a saw" says nothing at all about the ones that were never opened.</summary>
    [Test]
    public void An_answer_with_no_hits_still_answers_only_its_own_question()
    {
        var answer = Answer(new LibraryFilter(Text: "saw", Engine: "SN-S"), []);

        Assert.That(DeepSearch.Widen(new LibraryFilter(Text: "saw"), Everything, answer)
            .AnswersAnotherQuestion, Is.True);
        Assert.That(DeepSearch.Widen(new LibraryFilter(Text: "saw", Engine: "SN-S"), Everything, answer)
            .AnswersAnotherQuestion, Is.False);
    }

    /// <summary>The other half of the identity question: same path, different file. A version restored through
    /// the history panel overwrites the snapshot in place, and deleting a snapshot frees its name for the next
    /// save -- so a hit is used only while the file it describes is still the file that was read.</summary>
    [Test]
    public void A_hit_for_a_file_written_since_it_was_read_is_dropped_and_named()
    {
        var asking = new LibraryFilter(Text: "saw");
        LibraryEntry[] rewritten =
        [
            Tone("Old Pad", "Synth Pad", engine: "PCMS", modified: Read.AddMinutes(1)),
        ];
        var answer = Answer(asking, [Hit("Old Pad")], rewritten);

        var listing = DeepSearch.Widen(asking, rewritten, answer);

        Assert.That(listing.Admitted, Is.Empty, "the file that matched is not the file that is there now");
        Assert.That(listing.Reasons, Is.Empty);
        Assert.That(listing.Changed, Is.EqualTo(new[] { "Old Pad.json" }),
            "named, so the browser can forget exactly that hit and stop saying it");
    }

    /// <summary>A file the user has deleted is not a row and not a complaint either: the listing is the
    /// authority on what is in the folder, and being told about a snapshot you deleted yourself is noise.
    /// </summary>
    [Test]
    public void A_hit_for_a_file_that_has_left_the_folder_is_neither_a_row_nor_a_complaint()
    {
        var asking = new LibraryFilter(Text: "saw");
        var answer = Answer(asking, [Hit("Deleted Pad")]);

        var listing = DeepSearch.Widen(asking, Everything, answer);

        Assert.That(listing.Admitted, Is.Empty);
        Assert.That(listing.Changed, Is.Empty);
    }

    /// <summary>Nothing here reads a file or touches the list it was given, which is what lets the browser call
    /// it on every keystroke.</summary>
    [Test]
    public void Widening_does_not_touch_the_list_it_was_given()
    {
        var entries = Everything.ToList();
        var asking = new LibraryFilter(Text: "saw");

        var listing = DeepSearch.Widen(asking, entries, Answer(asking, [Hit("Old Pad")]));

        Assert.That(entries, Has.Count.EqualTo(Everything.Length));
        Assert.That(listing.Admitted, Is.Not.SameAs(entries));
        Assert.That(DeepSearch.Widen(asking, entries, null).Admitted, Is.Empty,
            "and no answer at all is simply the filter, which is what an unticked box asks for");
    }
}
