using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The decisions the library browser makes that are not about files, controls or state: the order of the
/// list, the look of a rating, what each filter drop-down offers and what a chosen row of one means, and how a
/// comma-separated box becomes tags.
///
/// <b>Why these are worth a fixture at all.</b> There is no headless Avalonia harness in this repo, so a view
/// model cannot be constructed in a test -- which means anything a view model decides is untested by
/// construction. Everything below was therefore put on the far side of one, in <c>LibraryListing</c>, and this is
/// what that bought. Each test pins a decision somebody will want to change: that "any rating" is 0 rather than
/// "unrated only", that a tag box splits on commas and not on spaces, that reversing a rating sort leaves equally
/// rated sounds alphabetical.</summary>
public class LibraryListingTests
{
    /// <summary>A listed entry, with only the fields a given test is about. A tone unless told otherwise, since a
    /// tone is the kind that carries every field; a Studio Set carries no engine and no category, exactly as one
    /// read off a real file would not.</summary>
    private static LibraryEntry Entry(string name, int rating = 0, string category = "", string kind = "",
        DateTime? modified = null, params string[] tags)
    {
        var isTone = kind.Length == 0 || kind == SnapshotKinds.Tone;
        return new LibraryEntry($@"C:\Library\{name}.json",
            new SnapshotHead(name, isTone ? SnapshotKinds.Tone : kind, isTone ? "SN-S" : null, category, tags, "",
                rating, false),
            modified ?? new DateTime(2026, 7, 1, 12, 0, 0));
    }

    // ---- the vocabulary -----------------------------------------------------------------------------------

    /// <summary>The instrument's own 34, and the constructor that used to hold the second copy of them still
    /// accepts every one. That is the whole point of the list existing: the category drop-down, the preset grids
    /// and <c>Integra7Preset</c>'s own validation now read from one place, and this is what says they agree.
    /// </summary>
    [Test]
    public void The_thirty_four_tone_categories_are_the_ones_a_preset_may_carry()
    {
        Assert.That(Integra7Preset.ToneCategories, Has.Count.EqualTo(34));
        Assert.That(Integra7Preset.ToneCategories, Is.Unique);

        foreach (var category in Integra7Preset.ToneCategories)
            Assert.That(() => new Integra7Preset(0, "INT", "SN-S", "PRST", 1, "n", 0, 0, 1, category),
                Throws.Nothing, $"'{category}' must be a category a preset can carry");
    }

    /// <summary>And a category that is not one of them is still refused, by name. The list replaced a 34-arm
    /// switch, so this is the half of that switch's behaviour that was not about mapping.</summary>
    [Test]
    public void A_category_the_instrument_does_not_have_is_still_refused()
    {
        Assert.That(() => new Integra7Preset(0, "INT", "SN-S", "PRST", 1, "n", 0, 0, 1, "Bagpipes"),
            Throws.Exception.With.Message.Contains("Bagpipes"));
    }

    [Test]
    public void Every_filter_drop_down_offers_not_asking_first_and_means_it()
    {
        Assert.That(LibraryListing.KindLabels[0], Is.EqualTo(LibraryListing.AnyKind));
        Assert.That(LibraryListing.CategoryLabels[0], Is.EqualTo(LibraryListing.AnyCategory));
        Assert.That(LibraryListing.RatingLabels[0], Is.EqualTo(LibraryListing.AnyRating));

        Assert.That(LibraryListing.KindFromLabel(LibraryListing.AnyKind), Is.Null);
        Assert.That(LibraryListing.CategoryFromLabel(LibraryListing.AnyCategory), Is.Null);
        Assert.That(LibraryListing.MinimumRatingFromLabel(LibraryListing.AnyRating), Is.EqualTo(0),
            "0 is no minimum, not unrated only");

        // And the whole library is what those admit, which is the property the browser opens on.
        var everything = new[] { Entry("Warm Rhodes", 4), Entry("World Pop", kind: SnapshotKinds.StudioSet) };
        var filter = new LibraryFilter("", LibraryListing.KindFromLabel(LibraryListing.AnyKind),
            LibraryListing.CategoryFromLabel(LibraryListing.AnyCategory),
            LibraryListing.MinimumRatingFromLabel(LibraryListing.AnyRating));
        Assert.That(filter.Apply(everything), Has.Count.EqualTo(2));
    }

    [Test]
    public void A_kind_label_says_what_the_stored_string_means_and_answers_back_with_it()
    {
        Assert.That(LibraryListing.KindLabel(SnapshotKinds.StudioSet), Is.EqualTo("Studio Set"));
        Assert.That(LibraryListing.KindLabel(SnapshotKinds.Tone), Is.EqualTo("Tone"));
        Assert.That(LibraryListing.KindFromLabel("Studio Set"), Is.EqualTo(SnapshotKinds.StudioSet));
        Assert.That(LibraryListing.KindFromLabel("Tone"), Is.EqualTo(SnapshotKinds.Tone));
        // A kind from a build that knows one this does not shows itself rather than "Unknown": the actual word is
        // the only useful thing to say about it.
        Assert.That(LibraryListing.KindLabel("rhythm-set"), Is.EqualTo("rhythm-set"));
        Assert.That(LibraryListing.KindFromLabel("rhythm-set"), Is.Null, "and filters nothing out");
    }

    /// <summary>The engine drop-down offers the five engine codes verbatim rather than friendly names, because
    /// that is what the Kind column of the same list and the instrument's own screen both show.</summary>
    [Test]
    public void An_engine_label_is_the_engine_code_itself()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryListing.EngineLabels.First(), Is.EqualTo(LibraryListing.AnyEngine),
                "so a browser opening on row 0 opens on the whole library");
            Assert.That(LibraryListing.EngineLabels.Skip(1),
                Is.EqualTo(new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" }));
            Assert.That(LibraryListing.EngineFromLabel("SN-S"), Is.EqualTo("SN-S"));
            Assert.That(LibraryListing.EngineFromLabel(LibraryListing.AnyEngine), Is.Null);
            Assert.That(LibraryListing.EngineFromLabel(null), Is.Null);
            // Not merely "not the any row": a label this has not been taught filters nothing out rather than
            // emptying the library, which is what KindFromLabel does with an unknown kind.
            Assert.That(LibraryListing.EngineFromLabel("SN-X"), Is.Null);
        });
    }

    [Test]
    public void A_minimum_rating_is_the_row_that_was_chosen()
    {
        Assert.That(LibraryListing.RatingLabels, Has.Count.EqualTo(6), "any, then one through five");
        for (var stars = 1; stars <= 5; stars++)
            Assert.That(LibraryListing.MinimumRatingFromLabel(LibraryListing.RatingLabels[stars]),
                Is.EqualTo(stars));
        Assert.That(LibraryListing.MinimumRatingFromLabel("four-ish"), Is.EqualTo(0),
            "and a label this has not been taught asks for nothing rather than for everything");
    }

    /// <summary>The editor's category list is the filter's with a different first row, and the two mean opposite
    /// things -- "any" narrows nothing, "none" clears the field. They are deliberately worded apart because the
    /// one place a user meets both is the filter bar above the editor.</summary>
    [Test]
    public void The_editor_offers_no_category_rather_than_any_category()
    {
        Assert.That(LibraryListing.EditCategoryLabels[0], Is.EqualTo(LibraryListing.NoCategory));
        Assert.That(LibraryListing.NoCategory, Is.Not.EqualTo(LibraryListing.AnyCategory));
        Assert.That(LibraryListing.EditCategoryLabels, Has.Count.EqualTo(35));

        Assert.That(LibraryListing.CategoryToWrite(LibraryListing.NoCategory), Is.EqualTo(""),
            "empty, which is what a snapshot with no category stores");
        Assert.That(LibraryListing.CategoryToWrite("E.Piano"), Is.EqualTo("E.Piano"));
        Assert.That(LibraryListing.EditLabelForCategory("E.Piano"), Is.EqualTo("E.Piano"));
        Assert.That(LibraryListing.EditLabelForCategory(""), Is.EqualTo(LibraryListing.NoCategory));
        Assert.That(LibraryListing.EditLabelForCategory(null), Is.EqualTo(LibraryListing.NoCategory));
        // A category the drop-down does not offer shows as "none" rather than being added to it: a list whose
        // contents depend on which row is selected is one where the same row means different things.
        Assert.That(LibraryListing.EditLabelForCategory("Bagpipes"), Is.EqualTo(LibraryListing.NoCategory));
    }

    // ---- the order ----------------------------------------------------------------------------------------

    [Test]
    public void Sorting_by_name_ignores_case_and_reverses()
    {
        LibraryEntry[] entries = [Entry("world pop"), Entry("Ambient Pad"), Entry("brass Section")];

        Assert.That(LibraryListing.Sort(entries, LibrarySort.Name, false).Select(e => e.Head.Name),
            Is.EqualTo(new[] { "Ambient Pad", "brass Section", "world pop" }));
        Assert.That(LibraryListing.Sort(entries, LibrarySort.Name, true).Select(e => e.Head.Name),
            Is.EqualTo(new[] { "world pop", "brass Section", "Ambient Pad" }));
    }

    /// <summary>The tie-break is the one that matters. Most of a library is unrated and saved within the same
    /// minute, so the primary key is equal far more often than not -- and an order that fell back on whatever the
    /// file system offered would rearrange itself on every refresh, which makes a list feel broken without ever
    /// being wrong. Reversing only the primary key is what keeps "worst first" alphabetical within each
    /// rating.</summary>
    [Test]
    public void Entries_that_tie_stay_in_alphabetical_order_in_both_directions()
    {
        LibraryEntry[] entries = [Entry("Zither", 3), Entry("Aeolian", 3), Entry("Marimba", 5)];

        Assert.That(LibraryListing.Sort(entries, LibrarySort.Rating, true).Select(e => e.Head.Name),
            Is.EqualTo(new[] { "Marimba", "Aeolian", "Zither" }));
        Assert.That(LibraryListing.Sort(entries, LibrarySort.Rating, false).Select(e => e.Head.Name),
            Is.EqualTo(new[] { "Aeolian", "Zither", "Marimba" }));
    }

    [Test]
    public void Sorting_by_date_is_the_file_own_date()
    {
        var older = Entry("Older", modified: new DateTime(2026, 1, 1));
        var newer = Entry("Newer", modified: new DateTime(2026, 7, 20));

        Assert.That(LibraryListing.Sort([older, newer], LibrarySort.Modified, true).First(), Is.EqualTo(newer));
        Assert.That(LibraryListing.Sort([older, newer], LibrarySort.Modified, false).First(), Is.EqualTo(older));
    }

    [Test]
    public void A_sort_label_is_the_row_that_was_chosen()
    {
        Assert.That(LibraryListing.SortLabels, Has.Count.EqualTo(3));
        Assert.That(LibraryListing.SortFromLabel("Name"), Is.EqualTo(LibrarySort.Name));
        Assert.That(LibraryListing.SortFromLabel("Rating"), Is.EqualTo(LibrarySort.Rating));
        Assert.That(LibraryListing.SortFromLabel("Date"), Is.EqualTo(LibrarySort.Modified));
        Assert.That(LibraryListing.SortFromLabel("Colour"), Is.EqualTo(LibrarySort.Name),
            "a label this has not been taught sorts by the one order that is always meaningful");
    }

    // ---- what a row shows ---------------------------------------------------------------------------------

    [Test]
    public void A_rating_is_five_stars_filled_up_to_it_and_nothing_at_all_when_unrated()
    {
        Assert.That(LibraryListing.Stars(0), Is.EqualTo(""), "most of a fresh library is unrated");
        Assert.That(LibraryListing.Stars(1), Is.EqualTo("★☆☆☆☆"));
        Assert.That(LibraryListing.Stars(3), Is.EqualTo("★★★☆☆"));
        Assert.That(LibraryListing.Stars(5), Is.EqualTo("★★★★★"));
        // A hand-edited file can say anything; the list shows it rather than throwing, because judging a file is
        // FromJson's job and it will refuse this one when it is opened.
        Assert.That(LibraryListing.Stars(7), Is.EqualTo("★★★★★"));
        Assert.That(LibraryListing.Stars(-1), Is.EqualTo(""));
    }

    // ---- tags ---------------------------------------------------------------------------------------------

    [Test]
    public void A_tag_box_splits_on_commas_only()
    {
        // Commas and not spaces: a tag has to be able to say "for the trio gig", and splitting on spaces would
        // make three tags out of it with no way to write the phrase at all.
        Assert.That(LibraryListing.ParseTags("warm, for the trio gig"),
            Is.EqualTo(new[] { "warm", "for the trio gig" }));
        // What a box being edited looks like half the time.
        Assert.That(LibraryListing.ParseTags("warm, , gig,"), Is.EqualTo(new[] { "warm", "gig" }));
        Assert.That(LibraryListing.ParseTags(""), Is.Empty);
        Assert.That(LibraryListing.ParseTags(null), Is.Empty);
        // Once each, without regard to case, matching how LibraryFilter compares them -- two rows that select the
        // same entries would be two ways to ask the same question.
        Assert.That(LibraryListing.ParseTags("warm, Warm"), Is.EqualTo(new[] { "warm" }));
        // The order is the user's own, because they will look at the box again.
        Assert.That(LibraryListing.ParseTags("zither, aeolian"), Is.EqualTo(new[] { "zither", "aeolian" }));
    }

    [Test]
    public void Tags_round_trip_through_the_box()
    {
        Assert.That(LibraryListing.FormatTags(["warm", "trio gig"]), Is.EqualTo("warm, trio gig"));
        Assert.That(LibraryListing.ParseTags(LibraryListing.FormatTags(["warm", "trio gig"])),
            Is.EqualTo(new[] { "warm", "trio gig" }));
        Assert.That(LibraryListing.FormatTags([]), Is.EqualTo(""));
    }

    /// <summary>The tag filter offers what the files carry, because there is no list of known tags anywhere else:
    /// tags are free text and live in the snapshots. So a tag exists exactly as long as something carries it, and
    /// a folder copied from another machine brings its own vocabulary with it.</summary>
    [Test]
    public void The_tags_offered_are_the_ones_the_library_carries_once_each()
    {
        LibraryEntry[] entries =
        [
            Entry("Warm Rhodes", tags: ["warm", "trio gig"]),
            Entry("Brass Section", tags: ["Warm", " trio gig ", "loud"]),
            Entry("Untagged"),
        ];

        Assert.That(LibraryListing.AllTags(entries), Is.EqualTo(new[] { "loud", "trio gig", "warm" }));
    }

    [Test]
    public void A_library_with_no_tags_offers_none()
    {
        Assert.That(LibraryListing.AllTags([Entry("Untagged")]), Is.Empty);
        Assert.That(LibraryListing.AllTags([]), Is.Empty);
    }

    /// <summary>What the duplicate panel says a group is. It has to be said in the panel's own words rather
    /// than left to the user, because <c>DuplicateGroups</c> is transitive: every member is near <i>some</i>
    /// other member, and no more than that is promised.</summary>
    [Test]
    public void The_duplicate_summary_says_what_a_group_means()
    {
        Assert.That(LibraryListing.DuplicateSummary(3, 8, 5), Is.EqualTo(
            "3 groups, 8 snapshots. Each of these differs in at most 5 parameters from at least one other " +
            "in its group."));
    }

    /// <summary>A threshold of nothing is a different sentence, not a sentence with a nought in it: "differs
    /// in at most 0 parameters" is a puzzle where "identical" is a fact.</summary>
    [Test]
    public void A_threshold_of_nothing_is_worded_as_identical()
    {
        Assert.That(LibraryListing.DuplicateSummary(1, 2, 0), Is.EqualTo(
            "1 group, 2 snapshots. Each of these is identical to at least one other in its group."));
    }

    [Test]
    public void One_group_and_one_parameter_are_singular()
    {
        Assert.That(LibraryListing.DuplicateSummary(1, 4, 1), Is.EqualTo(
            "1 group, 4 snapshots. Each of these differs in at most 1 parameter from at least one other " +
            "in its group."));
    }

    /// <summary>Nothing found says what was asked as well as what was answered: "no duplicates" alone would
    /// leave the user unable to tell a clean library from a threshold set too tight.</summary>
    [Test]
    public void Finding_nothing_says_what_was_looked_for()
    {
        Assert.That(LibraryListing.DuplicateSummary(0, 0, 5),
            Is.EqualTo("No two snapshots here differ in 5 parameters or fewer."));
        Assert.That(LibraryListing.DuplicateSummary(0, 0, 1),
            Is.EqualTo("No two snapshots here differ in 1 parameter or fewer."));
        Assert.That(LibraryListing.DuplicateSummary(0, 0, 0),
            Is.EqualTo("No two snapshots here are identical."));
    }

    /// <summary>The question the duplicate panel asks before it deletes. The counts are in it because this is
    /// the one place in the library where a user is deliberately ticking many rows they mean to lose.
    /// </summary>
    [Test]
    public void The_duplicate_delete_question_counts_what_goes()
    {
        Assert.That(LibraryListing.DuplicateDeleteQuestion(3, 0), Is.EqualTo(
            "Delete 3 snapshots from the library? A copy of each is kept in the history folder beside your " +
            "library."));
        Assert.That(LibraryListing.DuplicateDeleteQuestion(1, 0), Is.EqualTo(
            "Delete 1 snapshot from the library? A copy is kept in the history folder beside your library."));
    }

    /// <summary>The number the warning below is built from, and the reason it is here rather than in the
    /// panel: if it were wrong the user would read "that empties 2 of the groups" while three families were
    /// about to go, or read nothing at all while one was -- and the warning exists precisely for the
    /// destructive case.</summary>
    [Test]
    public void A_group_is_emptied_only_when_every_one_of_its_rows_is_ticked()
    {
        // Two groups of three: the first fully ticked, the second one short of it.
        Assert.That(LibraryListing.GroupsEmptiedBy([(3, 3), (3, 2)]), Is.EqualTo(1));
        Assert.That(LibraryListing.GroupsEmptiedBy([(3, 3), (2, 2)]), Is.EqualTo(2));
        Assert.That(LibraryListing.GroupsEmptiedBy([(4, 0), (4, 3)]), Is.EqualTo(0));
        Assert.That(LibraryListing.GroupsEmptiedBy([]), Is.EqualTo(0));
    }

    /// <summary>A group with no rows in it is not a group that has been emptied by anything the user did.
    /// <c>All</c> over an empty sequence answers true, so the obvious spelling of this counts a group that
    /// was never there -- and the number it feeds is a warning about deletion.</summary>
    [Test]
    public void A_group_with_no_rows_is_not_counted_as_emptied()
    {
        Assert.That(LibraryListing.GroupsEmptiedBy([(0, 0)]), Is.EqualTo(0));
    }

    /// <summary>What the Delete button says before it is pressed. It carries the count for the reason the
    /// bulk panel's does: pressing it must never be a guess about how much it does.</summary>
    [Test]
    public void The_duplicate_delete_label_counts_the_ticks()
    {
        // Nothing ticked: the button is there but disabled, so it says what it would do rather than "0".
        Assert.That(LibraryListing.DuplicateDeleteLabel(0), Is.EqualTo("Delete the ticked snapshots…"));
        Assert.That(LibraryListing.DuplicateDeleteLabel(1), Is.EqualTo("Delete the ticked snapshot…"));
        Assert.That(LibraryListing.DuplicateDeleteLabel(4), Is.EqualTo("Delete the 4 ticked snapshots…"));
    }

    /// <summary>What is said afterwards. A file that was already gone must not be counted as one this
    /// removed: "Deleted 3 snapshots" about three files nobody touched would send a user looking for copies
    /// in the history folder that were never put there.</summary>
    [Test]
    public void The_duplicate_delete_outcome_separates_removed_from_already_gone()
    {
        Assert.That(LibraryListing.DuplicateDeleteOutcome(3, 0, []),
            Is.EqualTo("Deleted 3 snapshots from the library."));
        Assert.That(LibraryListing.DuplicateDeleteOutcome(1, 0, []),
            Is.EqualTo("Deleted 1 snapshot from the library."));
        Assert.That(LibraryListing.DuplicateDeleteOutcome(2, 1, []),
            Is.EqualTo("Deleted 2 snapshots from the library. 1 had already gone."));
        Assert.That(LibraryListing.DuplicateDeleteOutcome(0, 2, []),
            Is.EqualTo("Nothing was deleted. 2 had already gone."));
    }

    [Test]
    public void The_duplicate_delete_outcome_names_what_could_not_be_removed()
    {
        Assert.That(LibraryListing.DuplicateDeleteOutcome(2, 0, ["Warm Rhodes.json"]),
            Is.EqualTo("Deleted 2 snapshots from the library. " +
                       "1 could not be removed: Warm Rhodes.json."));
        Assert.That(LibraryListing.DuplicateDeleteOutcome(0, 0, ["a.json", "b.json"]),
            Is.EqualTo("Nothing was deleted. 2 could not be removed: a.json, b.json."));
    }

    /// <summary>The trap this panel invites, and the only warning against it: ticking every row of a group
    /// deletes every copy of that sound. The history folder still has them, but the library does not, and a
    /// user tidying duplicates is not thinking about that.</summary>
    [Test]
    public void The_duplicate_delete_question_warns_when_a_whole_group_would_go()
    {
        Assert.That(LibraryListing.DuplicateDeleteQuestion(4, 1), Is.EqualTo(
            "Delete 4 snapshots from the library? A copy of each is kept in the history folder beside your " +
            "library. That empties one of the groups, so nothing of that sound would be left in the library."));
        Assert.That(LibraryListing.DuplicateDeleteQuestion(9, 2), Is.EqualTo(
            "Delete 9 snapshots from the library? A copy of each is kept in the history folder beside your " +
            "library. That empties 2 of the groups, so nothing of those sounds would be left in the " +
            "library."));
    }
}
