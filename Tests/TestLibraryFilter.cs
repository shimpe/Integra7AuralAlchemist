using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which entries a search, a kind, a category, a minimum rating, a favourites toggle and a set of
/// tags admit.
///
/// This is the one piece of the library with no file system in it, which is why nearly all of the library's
/// tests are here. What they are pinning is mostly not arithmetic but choices a user will assume one way or
/// the other -- whether two tags mean both or either, whether a minimum rating of none means unrated, whether
/// searching a name also searches a note. Each of those is written down as a test because the alternative is
/// that it gets discovered, once, by somebody who cannot find a sound they know they saved.</summary>
public class LibraryFilterTests
{
    private static LibraryEntry Tone(string name, string category = "", string[]? tags = null,
        string notes = "", int rating = 0, bool favourite = false, string engine = "SN-S") =>
        new($"{name}.json",
            new SnapshotHead(name, SnapshotKinds.Tone, engine, category, tags ?? [], notes, rating, favourite),
            new DateTime(2026, 7, 26, 19, 40, 0));

    /// <summary>A Studio Set: sixteen parts, each with a category of its own, so the file has none. That is a
    /// decision of the format's rather than a gap in these tests -- see the fixture on the category rule
    /// below.</summary>
    private static LibraryEntry StudioSet(string name, string[]? tags = null, string notes = "",
        int rating = 0, bool favourite = false) =>
        new($"{name}.json",
            new SnapshotHead(name, SnapshotKinds.StudioSet, null, "", tags ?? [], notes, rating, favourite),
            new DateTime(2026, 7, 26, 19, 40, 0));

    private static IReadOnlyList<string> Names(IEnumerable<LibraryEntry> entries) =>
        entries.Select(e => e.Head.Name).ToList();

    /// <summary>The library as it will actually look: a couple of tones, a Studio Set, some annotated and some
    /// not. Every test filters this same list, so that "what is admitted" is always a statement about the same
    /// folder.</summary>
    private static readonly LibraryEntry[] Everything =
    [
        Tone("Warm Rhodes", "E.Piano", ["warm", "trio gig"], "less bark", 4, true),
        Tone("Bright Rhodes", "E.Piano", ["bright"], "for the loud half", 2),
        Tone("Church Organ", "Organ", ["warm"], "", 5, true),
        Tone("Nameless Pad", "Synth Pad"),
        StudioSet("World Pop Set", ["trio gig", "warm"], "second half only", 3),
        StudioSet("Empty Set"),
    ];

    /// <summary>The default, and the state the browser opens in. Everything is admitted -- including the entry
    /// with no rating, no tags and no notes, which is what most of a real library looks like before anybody
    /// has annotated any of it.</summary>
    [Test]
    public void An_empty_filter_admits_everything()
    {
        Assert.That(Names(LibraryFilter.None.Apply(Everything)), Is.EqualTo(Names(Everything)),
            "and in the order it was given, because sorting is the browser's job and not this one's");
    }

    /// <summary>The rule that has to be written down somewhere: 0 is "no minimum", not "unrated only". They
    /// are the same number and opposite meanings, and a filter that took the second reading would empty the
    /// list the moment the user dragged the stars back down to none.</summary>
    [Test]
    public void A_minimum_rating_of_none_admits_unrated_entries_rather_than_only_them()
    {
        var admitted = Names(new LibraryFilter(MinimumRating: 0).Apply(Everything));

        Assert.That(admitted, Is.EqualTo(Names(Everything)));
        Assert.That(admitted, Does.Contain("Nameless Pad").And.Contain("Warm Rhodes"));
    }

    [Test]
    public void A_minimum_rating_admits_that_rating_and_everything_above_it()
    {
        Assert.That(Names(new LibraryFilter(MinimumRating: 3).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Church Organ", "World Pop Set" }));
        Assert.That(Names(new LibraryFilter(MinimumRating: 5).Apply(Everything)),
            Is.EqualTo(new[] { "Church Organ" }));
    }

    /// <summary>Free text searches every field a user might have put the word in, because they do not remember
    /// which one they put it in. "rhodes" is a name here, "bark" is a note, "Organ" is a category and "gig" is
    /// a tag; all four are the same search box.</summary>
    [Test]
    public void Free_text_matches_a_name_a_note_a_category_or_a_tag()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Names(new LibraryFilter(Text: "rhodes").Apply(Everything)),
                Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes" }), "the name");
            Assert.That(Names(new LibraryFilter(Text: "bark").Apply(Everything)),
                Is.EqualTo(new[] { "Warm Rhodes" }), "a note");
            Assert.That(Names(new LibraryFilter(Text: "organ").Apply(Everything)),
                Is.EqualTo(new[] { "Church Organ" }), "a category -- and a name, here, which is the point");
            Assert.That(Names(new LibraryFilter(Text: "gig").Apply(Everything)),
                Is.EqualTo(new[] { "Warm Rhodes", "World Pop Set" }), "a tag");
        });
    }

    [Test]
    public void Free_text_ignores_case_in_both_directions()
    {
        Assert.That(Names(new LibraryFilter(Text: "RHODES").Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes" }));
        Assert.That(Names(new LibraryFilter(Text: "e.piano").Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes" }));
    }

    /// <summary>A limitation, pinned so that it is a decision rather than a surprise: the text is one substring
    /// and it has to fall inside one field. "rhodes bark" therefore finds nothing, even though the entry named
    /// "Warm Rhodes" has "less bark" in its notes -- both words are on the same entry and neither field holds
    /// the pair. Splitting the text into words that each have to match somewhere is a better search and a
    /// bigger one; it belongs in a change of its own, with the browser in front of it.</summary>
    [Test]
    public void Free_text_is_one_substring_and_not_a_word_search()
    {
        Assert.That(Names(new LibraryFilter(Text: "rhodes").Apply(Everything)), Does.Contain("Warm Rhodes"));
        Assert.That(Names(new LibraryFilter(Text: "bark").Apply(Everything)), Does.Contain("Warm Rhodes"));
        Assert.That(new LibraryFilter(Text: "rhodes bark").Apply(Everything), Is.Empty,
            "both words are on that one entry, and no single field holds them together");
        Assert.That(Names(new LibraryFilter(Text: "arm Rhod").Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes" }), "one substring, wherever it falls inside one field");
    }

    /// <summary>Text that is only spaces is not a filter. It is what a search box holds after a word is deleted
    /// with one keystroke too few, or after a paste that brought its own whitespace, and an empty list at that
    /// moment reads as a library that has lost its contents.</summary>
    [Test]
    public void Text_that_is_blank_or_only_padding_filters_nothing_and_is_trimmed()
    {
        Assert.That(Names(new LibraryFilter(Text: "   ").Apply(Everything)), Is.EqualTo(Names(Everything)));
        Assert.That(Names(new LibraryFilter(Text: "  rhodes ").Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes" }));
    }

    /// <summary>Tags are AND, not OR. This is the one a user will assume either way, so it is written down and
    /// tested rather than left to be found out: asking for "warm" and "trio gig" means the sounds that are
    /// both, which is how a tag filter earns its keep -- narrowing. OR is what the search box already does
    /// across fields, and two controls that both widen would leave nothing that narrows.</summary>
    [Test]
    public void Two_tags_mean_both_of_them_and_not_either()
    {
        Assert.That(Names(new LibraryFilter(Tags: ["warm", "trio gig"]).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "World Pop Set" }));
        Assert.That(Names(new LibraryFilter(Tags: ["warm"]).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Church Organ", "World Pop Set" }),
            "each on its own admits more than the pair does");
        Assert.That(new LibraryFilter(Tags: ["warm", "bright"]).Apply(Everything), Is.Empty,
            "and a pair nothing carries admits nothing, rather than everything carrying either");
    }

    /// <summary>A tag is matched whole and without regard to case. Whole, because a tag is a thing the user
    /// picked from a list rather than a substring they typed -- the search box is where typing part of a word
    /// belongs. Without regard to case, because "Warm" and "warm" are one tag as far as anybody using this is
    /// concerned, and the search box already treats them that way.</summary>
    [Test]
    public void A_tag_is_matched_whole_and_case_insensitively()
    {
        Assert.That(Names(new LibraryFilter(Tags: ["WARM"]).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Church Organ", "World Pop Set" }));
        Assert.That(new LibraryFilter(Tags: ["war"]).Apply(Everything), Is.Empty,
            "part of a tag is not that tag");
        Assert.That(new LibraryFilter(Tags: ["gig"]).Apply(Everything), Is.Empty,
            "\"trio gig\" is one tag, not two");
    }

    /// <summary>Padding on either side is not part of a tag. Both sides are trimmed, and it has to be both: a
    /// tag stored with a stray space -- from a paste, or from a file edited by hand -- would otherwise be
    /// impossible to select even from a list of tags gathered out of the library itself, which is where the
    /// browser's tag chips come from.</summary>
    [Test]
    public void Padding_is_not_part_of_a_tag_on_either_side()
    {
        LibraryEntry[] padded = [Tone("Padded", tags: [" warm "])];

        Assert.That(new LibraryFilter(Tags: ["warm"]).Apply(padded), Has.Count.EqualTo(1));
        Assert.That(new LibraryFilter(Tags: [" warm "]).Apply(padded), Has.Count.EqualTo(1));
        Assert.That(new LibraryFilter(Tags: [" warm "]).Apply(Everything).Select(e => e.Head.Name),
            Is.EqualTo(new[] { "Warm Rhodes", "Church Organ", "World Pop Set" }));
    }

    [Test]
    public void An_empty_tag_selection_is_no_tag_filter_at_all()
    {
        Assert.That(Names(new LibraryFilter(Tags: []).Apply(Everything)), Is.EqualTo(Names(Everything)));
        Assert.That(Names(new LibraryFilter(Tags: ["  "]).Apply(Everything)), Is.EqualTo(Names(Everything)),
            "and neither is a blank one, which is what an empty tag box would otherwise contribute");
    }

    [Test]
    public void The_kind_filter_separates_studio_sets_from_tones()
    {
        Assert.That(Names(new LibraryFilter(Kind: SnapshotKinds.StudioSet).Apply(Everything)),
            Is.EqualTo(new[] { "World Pop Set", "Empty Set" }));
        Assert.That(Names(new LibraryFilter(Kind: SnapshotKinds.Tone).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes", "Church Organ", "Nameless Pad" }));
        Assert.That(Names(new LibraryFilter(Kind: null).Apply(Everything)), Is.EqualTo(Names(Everything)),
            "no kind chosen is both kinds");
    }

    /// <summary>The engine axis, on a list of its own rather than on the shared one, so that adding it did not
    /// have to move every other test's expectations.
    ///
    /// The Studio Set in it is the case worth pinning: it carries no engine at all, so asking for one drops it.
    /// A morph pad is the reason this axis exists and its corners must be tones, so that is the answer wanted
    /// -- but it is also the answer somebody will one day read as a bug, which is why it is a test.</summary>
    [Test]
    public void The_engine_filter_picks_one_engine_and_leaves_out_what_has_none()
    {
        LibraryEntry[] mixed =
        [
            Tone("Warm Rhodes", engine: "SN-S"),
            Tone("Concert Grand", engine: "SN-A"),
            Tone("Old Pad", engine: "PCMS"),
            Tone("Glass Bell", engine: "SN-S"),
            StudioSet("World Pop Set"),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Names(new LibraryFilter(Engine: "SN-S").Apply(mixed)),
                Is.EqualTo(new[] { "Warm Rhodes", "Glass Bell" }));
            Assert.That(Names(new LibraryFilter(Engine: "PCMS").Apply(mixed)),
                Is.EqualTo(new[] { "Old Pad" }));
            Assert.That(Names(new LibraryFilter(Engine: null).Apply(mixed)), Is.EqualTo(Names(mixed)),
                "no engine chosen is every engine, and the Studio Set is back");
            Assert.That(new LibraryFilter(Engine: "").Apply(mixed), Has.Count.EqualTo(mixed.Length),
                "and empty means the same as null, which is what a drop-down's blank row would send");
        });
    }

    /// <summary>Narrowing by engine and by something else at once, which is what the axis is for: the pad's
    /// picker asks for one engine and the user types into the same search box they always do.</summary>
    [Test]
    public void The_engine_narrows_together_with_the_other_axes()
    {
        LibraryEntry[] mixed =
        [
            Tone("Warm Rhodes", "E.Piano", rating: 4, engine: "SN-S"),
            Tone("Warm Pad", "Synth Pad", rating: 4, engine: "SN-S"),
            Tone("Warm Grand", "Ac.Piano", rating: 4, engine: "SN-A"),
        ];

        Assert.That(Names(new LibraryFilter(Text: "warm", MinimumRating: 4, Engine: "SN-S").Apply(mixed)),
            Is.EqualTo(new[] { "Warm Rhodes", "Warm Pad" }));
    }

    /// <summary>Category matching is exact, because the vocabulary is fixed: these are the instrument's own 34
    /// tone categories, the ones <c>Integra7Preset</c> parses and the preset grids already show, and the filter
    /// is a drop-down of them rather than a text box. "Piano" is not a loose match for "Ac.Piano" -- it is a
    /// different entry in a list the user picked from, and a substring rule would quietly conflate the two
    /// while also making "Organ" match nothing predictable.</summary>
    [Test]
    public void Category_matching_is_exact()
    {
        Assert.That(Names(new LibraryFilter(Category: "E.Piano").Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Bright Rhodes" }));
        Assert.That(new LibraryFilter(Category: "Piano").Apply(Everything), Is.Empty,
            "not a substring of the real category");
        Assert.That(new LibraryFilter(Category: "e.piano").Apply(Everything), Is.Empty,
            "and not a case-insensitive match either: this value came from the instrument's own list");
    }

    /// <summary>A Studio Set has no category at all -- it is sixteen parts each with one -- so it is admitted
    /// by "any category" and by nothing else. That is the whole rule, and the second assertion is the half
    /// worth pinning: choosing a category is choosing tones, and a Studio Set quietly appearing under
    /// "E.Piano" because its category is empty and empty matches everything would be nonsense.</summary>
    [Test]
    public void A_studio_set_has_no_category_and_is_admitted_only_by_any_category()
    {
        Assert.That(Names(LibraryFilter.None.Apply(Everything)), Does.Contain("World Pop Set"));
        Assert.That(Names(new LibraryFilter(Category: "E.Piano").Apply(Everything)),
            Does.Not.Contain("World Pop Set"));
    }

    [Test]
    public void Favourites_only_admits_the_favourites_and_nothing_else()
    {
        Assert.That(Names(new LibraryFilter(FavouritesOnly: true).Apply(Everything)),
            Is.EqualTo(new[] { "Warm Rhodes", "Church Organ" }));
        Assert.That(Names(new LibraryFilter(FavouritesOnly: false).Apply(Everything)),
            Is.EqualTo(Names(Everything)), "the toggle off is not a filter for the rest");
    }

    /// <summary>Every axis at once, and they narrow together. Each of the six is a separate question about the
    /// same entry, so an entry has to answer all of them -- which is what makes the tag rule above the odd one
    /// out and worth its own test: it is the only axis whose *members* also mean AND.</summary>
    [Test]
    public void The_axes_combine_and_all_of_them_have_to_be_satisfied()
    {
        var narrow = new LibraryFilter(Text: "warm", Kind: SnapshotKinds.Tone, Category: "E.Piano",
            MinimumRating: 4, FavouritesOnly: true, Tags: ["warm"]);

        Assert.That(Names(narrow.Apply(Everything)), Is.EqualTo(new[] { "Warm Rhodes" }));
        Assert.That(narrow with { MinimumRating = 5 }, Is.Not.Null.And.Matches<LibraryFilter>(
            f => !f.Apply(Everything).Any()), "one axis raised is enough to exclude it");
    }

    /// <summary>Nothing here reads the file, so this is what "pure" means in practice: the same entries and the
    /// same filter give the same answer, and the answer is a new list rather than a rearrangement of the one
    /// passed in. The browser holds the unfiltered list and re-filters it on every keystroke, which is only
    /// safe because of this.</summary>
    [Test]
    public void Filtering_does_not_touch_the_list_it_was_given()
    {
        var entries = Everything.ToList();

        var admitted = new LibraryFilter(Text: "rhodes").Apply(entries);

        Assert.That(entries, Has.Count.EqualTo(Everything.Length));
        Assert.That(admitted, Is.Not.SameAs(entries));
        Assert.That(Names(new LibraryFilter(Text: "rhodes").Apply(entries)), Is.EqualTo(Names(admitted)));
    }
}
