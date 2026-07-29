using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One change to make to many snapshots at once. <b>Null means "leave this alone"</b>, which is
/// what lets one type describe every button on the bulk panel: each sets a single field and leaves the rest
/// null.
///
/// Notes and the name are absent on purpose. One note pasted over fourteen sounds is not something anybody
/// wants, and a rename cannot be bulk at all.</summary>
/// <param name="Category">The category to set, or null to leave it. <b>Empty is a value</b> -- "this sound
/// has no category" -- and is not the same as null.</param>
/// <param name="Rating">0 to 5, or null to leave it. <b>Zero is a rating</b>, meaning unrated.</param>
/// <param name="Favourite">Set or clear, or null to leave it.</param>
/// <param name="AddTags">Tags to add to whatever is already there.</param>
/// <param name="RemoveTags">Tags to take off. Applied after <paramref name="AddTags"/>, so a tag in both
/// lists ends up removed -- a mistake either way, but a predictable one.</param>
public sealed record BulkChange(
    string? Category = null,
    int? Rating = null,
    bool? Favourite = null,
    IReadOnlyList<string>? AddTags = null,
    IReadOnlyList<string>? RemoveTags = null);

/// <summary>What a <see cref="BulkChange"/> means for one snapshot.
///
/// <b>Apart from the loop that applies it</b> for the reason every decision in this folder is apart from its
/// caller: a view model cannot be constructed in a test under ReactiveUI 24, and these rules are not
/// arithmetic anybody can check by reading. Getting one wrong is not a crash -- it is a library quietly
/// annotated wrongly in fourteen places at once, which is exactly the kind of mistake bulk editing exists to
/// make possible.
///
/// It answers a whole <see cref="SnapshotMetadata"/> rather than a delta, because that is what
/// <see cref="SnapshotLibrary.WriteMetadata"/> takes and that method replaces every field it is given. A
/// caller assembling one by hand would be one field away from wiping a note.</summary>
public static class BulkEdit
{
    /// <summary>Ordinal, ignoring case -- <see cref="LibraryFilter"/>'s rule for tags, and for its reason:
    /// "Warm" and "warm" are one tag to anybody using this.</summary>
    private static readonly StringComparer Loosely = StringComparer.OrdinalIgnoreCase;

    public static SnapshotMetadata Apply(SnapshotHead head, BulkChange change) =>
        new(
            // A Studio Set is sixteen parts each with a category of its own and has none; a bulk category
            // applied across a mixed selection must not invent one for it.
            head.Kind == SnapshotKinds.Tone ? change.Category ?? head.Category : head.Category,
            Tags(head, change),
            head.Notes,
            change.Rating ?? head.Rating,
            change.Favourite ?? head.Favourite);

    private static List<string> Tags(SnapshotHead head, BulkChange change)
    {
        // The order already there is kept and additions go on the end: a tag list is something the user has
        // read before, and resorting it on every bulk edit would make it unrecognisable.
        var tags = head.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        foreach (var tag in (change.AddTags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0))
            if (!tags.Contains(tag, Loosely))
                tags.Add(tag);

        // After the additions, so a tag in both lists is removed. See the note on BulkChange.
        var unwanted = (change.RemoveTags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (unwanted.Count > 0) tags.RemoveAll(tag => unwanted.Contains(tag, Loosely));

        return tags;
    }
}
