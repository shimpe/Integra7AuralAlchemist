using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Searching inside patches: which files are worth opening, whether an answer still answers the
/// question on screen, and which entries its hits admit.
///
/// <b>The question a deep search answers is the whole filter, not the search text.</b> That is the decision
/// these tests exist for. The files that get opened are chosen by every axis -- only the entries the other six
/// admit and the text does not are worth reading -- so a hit is evidence about that set and no other. Answer a
/// <i>wider</i> question with it and rows go missing with nothing on screen to say so, which is the one failure
/// a search must not have: the user cannot tell "there is nothing there" from "I did not look".
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

    // ---- whether an answer still answers ---------------------------------------------------------------

    /// <summary>The trap that would have made the whole feature look as though it found nothing: the tags are a
    /// list, records compare lists by reference, and a browser builds a fresh one every time it filters. Order,
    /// padding, duplicates and case are all nothing to do with what the filter admits, so they are nothing to
    /// do with whether it is the same question.</summary>
    [Test]
    public void Two_filters_built_separately_with_the_same_tags_are_the_same_question()
    {
        var asked = new LibraryFilter(Text: "saw", Tags: new List<string> { "warm", "trio gig" });
        var asking = new LibraryFilter(Text: "saw", Tags: new List<string> { "trio gig", " WARM " });

        Assert.That(asked == asking, Is.False, "record equality compares the tag list by reference");
        Assert.That(DeepSearch.SameQuestion(asked, asking), Is.True);
        Assert.That(DeepSearch.SameQuestion(asked, asked with { Tags = ["warm", "warm", "trio gig"] }), Is.True,
            "a tag asked for twice is asked for once");
    }

    [Test]
    public void A_tag_added_or_dropped_is_another_question()
    {
        var asked = new LibraryFilter(Text: "saw", Tags: ["warm"]);

        Assert.Multiple(() =>
        {
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Tags = ["warm", "gig"] }), Is.False);
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Tags = [] }), Is.False,
                "unticking the last tag widens the question, which is exactly the case that must not slip");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Tags = null }), Is.False);
        });
    }

    /// <summary>Text is matched trimmed and ignoring case, so it is compared that way: correcting the capitals
    /// of a word does not change which patches contain it, and a search box that had to be re-run for a
    /// capital would be a search box that felt broken.</summary>
    [Test]
    public void Text_is_the_same_question_trimmed_and_case_folded_and_no_further()
    {
        var asked = new LibraryFilter(Text: "saw");

        Assert.Multiple(() =>
        {
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Text = " SAW " }), Is.True);
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Text = "sawtooth" }), Is.False);
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Text = "" }), Is.False);
        });
    }

    [Test]
    public void Null_and_empty_are_one_value_on_the_axes_that_are_compared_exactly()
    {
        var asked = new LibraryFilter(Text: "saw", Kind: null, Category: "", Engine: null);

        Assert.That(DeepSearch.SameQuestion(asked, asked with { Kind = "", Category = null, Engine = "" }),
            Is.True, "both spellings mean \"not asking\" to LibraryFilter, so both are the same question");
    }

    /// <summary>The finding this class was written for. Every one of these is a filter being <i>widened</i>
    /// after a search, which asks about files that were never opened.</summary>
    [Test]
    public void Widening_any_other_axis_is_another_question()
    {
        var asked = new LibraryFilter("saw", SnapshotKinds.Tone, "E.Piano", 4, true, ["warm"], "SN-S");

        Assert.Multiple(() =>
        {
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Engine = null }), Is.False, "any engine");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { MinimumRating = 0 }), Is.False,
                "a lower minimum rating");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { FavouritesOnly = false }), Is.False,
                "favourites unticked");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Category = null }), Is.False,
                "any category");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { Kind = null }), Is.False, "any kind");
            Assert.That(DeepSearch.SameQuestion(asked, asked with { }), Is.True,
                "and the same seven axes are the same question, however the record was built");
        });
    }

    // ---- what the hits admit ---------------------------------------------------------------------------

    [Test]
    public void A_hit_adds_a_row_and_carries_the_reason_it_was_found_for()
    {
        var asking = new LibraryFilter(Text: "saw");
        var answer = new DeepSearchAnswer(asking, [Hit("Old Pad")]);

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
        var answer = new DeepSearchAnswer(asking, [Hit("Old Pad"), Hit("Glass Bell")]);

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
        var answer = new DeepSearchAnswer(asking, [Hit("Warm Rhodes"), Hit("Warm Rhodes", "Tone Name = Rhodes")]);

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
        var answer = new DeepSearchAnswer(narrow, [Hit("Glass Bell")]);

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
        var answer = new DeepSearchAnswer(new LibraryFilter(Text: "saw", Engine: "SN-S"), []);

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
        var answer = new DeepSearchAnswer(asking, [Hit("Old Pad")]);
        LibraryEntry[] rewritten =
        [
            Tone("Old Pad", "Synth Pad", engine: "PCMS", modified: Read.AddMinutes(1)),
        ];

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
        var answer = new DeepSearchAnswer(asking, [Hit("Deleted Pad")]);

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

        var listing = DeepSearch.Widen(asking, entries, new DeepSearchAnswer(asking, [Hit("Old Pad")]));

        Assert.That(entries, Has.Count.EqualTo(Everything.Length));
        Assert.That(listing.Admitted, Is.Not.SameAs(entries));
        Assert.That(DeepSearch.Widen(asking, entries, null).Admitted, Is.Empty,
            "and no answer at all is simply the filter, which is what an unticked box asks for");
    }
}
