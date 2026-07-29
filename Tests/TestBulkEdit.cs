using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What one bulk change means for one snapshot.
///
/// Every rule here is a decision a user will assume one way or the other, and getting one wrong is not a
/// crash but a library quietly annotated wrongly in fourteen places at once. That is why the decisions are
/// in a pure function with tests rather than in the loop that calls it.</summary>
public class BulkEditTests
{
    private static SnapshotHead Head(string name, string category = "E.Piano",
        string[]? tags = null, string notes = "notes", int rating = 3, bool favourite = true) =>
        new(name, SnapshotKinds.Tone, "SN-S", category, tags ?? ["warm", "trio gig"], notes, rating,
            favourite);

    /// <summary>A change that says nothing changes nothing. This is what makes the batch loop safe to run
    /// over a selection where some fields were never touched.</summary>
    [Test]
    public void An_empty_change_leaves_every_field_as_it_was()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange());

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo("E.Piano"));
            Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
            Assert.That(result.Notes, Is.EqualTo("notes"));
            Assert.That(result.Rating, Is.EqualTo(3));
            Assert.That(result.Favourite, Is.True);
        });
    }

    /// <summary>The name is never touched by a bulk change: a rename cannot be bulk, and null is what
    /// SnapshotMetadata reads as "leave the name alone".</summary>
    [Test]
    public void The_name_is_never_part_of_a_bulk_change()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Rating: 5)).Name, Is.Null);
    }

    /// <summary>Notes are not a bulk field either -- one note pasted over fourteen sounds is not something
    /// anybody wants -- so they have to survive a change that sets something else.</summary>
    [Test]
    public void Notes_survive_a_change_to_another_field()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Favourite: false)).Notes,
            Is.EqualTo("notes"));
    }

    [Test]
    public void Setting_a_field_replaces_only_that_field()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Category: "Organ"));

        Assert.That(result.Category, Is.EqualTo("Organ"));
        Assert.That(result.Rating, Is.EqualTo(3), "and leaves the rest alone");
    }

    /// <summary>An empty category is a real value -- "this sound has no category" -- and has to be
    /// distinguishable from "do not touch the category", which is null.</summary>
    [Test]
    public void An_empty_category_clears_it_rather_than_meaning_leave_it_alone()
    {
        Assert.That(BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Category: "")).Category, Is.Empty);
    }

    /// <summary>Added tags join the ones already there. Replacing would wipe each patch's own vocabulary,
    /// which is the thing tags exist to hold.</summary>
    [Test]
    public void Added_tags_join_the_ones_already_there_in_the_order_they_were_in()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["bright"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig", "bright" }));
    }

    [Test]
    public void Adding_a_tag_a_snapshot_already_carries_changes_nothing()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["WARM"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }),
            "matched without regard to case, and the spelling already there is kept");
    }

    [Test]
    public void Removing_a_tag_takes_it_off_whatever_its_case()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(RemoveTags: ["Trio Gig"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm" }));
    }

    [Test]
    public void Removing_a_tag_a_snapshot_does_not_carry_changes_nothing()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(RemoveTags: ["loud"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
    }

    /// <summary>A tag in both lists is a mistake either way, so the answer only has to be one a user can
    /// predict: removal is applied after addition, so removal wins.</summary>
    [Test]
    public void A_tag_both_added_and_removed_is_removed()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"),
            new BulkChange(AddTags: ["bright"], RemoveTags: ["bright"]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig" }));
    }

    /// <summary>Blank entries are what a half-typed tag box contributes, and are not a request for an empty
    /// tag. Whitespace is trimmed on both sides, matching LibraryListing.ParseTags and LibraryFilter.
    /// </summary>
    [Test]
    public void Blank_and_padded_tags_are_tidied_rather_than_stored()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(AddTags: ["  ", " bright "]));

        Assert.That(result.TagList, Is.EqualTo(new[] { "warm", "trio gig", "bright" }));
    }

    /// <summary>A Studio Set has no category -- sixteen parts each with one of their own -- so a bulk
    /// category applied across a mixed selection must not invent one for it. The caller filters the
    /// selection; this is the half that makes the filtering visible as a rule rather than a coincidence.
    /// </summary>
    [Test]
    public void A_studio_set_never_takes_a_category()
    {
        var studioSet = new SnapshotHead("World Pop", SnapshotKinds.StudioSet, null, "", [], "", 0, false);

        Assert.That(BulkEdit.Apply(studioSet, new BulkChange(Category: "Organ")).Category, Is.Empty);
    }

    [Test]
    public void A_rating_and_a_favourite_are_set_outright()
    {
        var result = BulkEdit.Apply(Head("Warm Rhodes"), new BulkChange(Rating: 0, Favourite: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.Rating, Is.Zero, "zero is a rating, not 'leave it alone'");
            Assert.That(result.Favourite, Is.False);
        });
    }
}
