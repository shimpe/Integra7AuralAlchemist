using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which of the library's snapshots may sit on a morph pad's corner, and how to say how many.
///
/// <b>Apart from the picker for the reason every visual editor's arithmetic is apart from its control:</b> a
/// view model cannot be tested here -- since ReactiveUI 24 it cannot even be constructed without an
/// initialised application -- and these three rules are exactly the part that can be wrong. They are also the
/// part that decides what a user is allowed to click, which is worse to get wrong than anything the dialog
/// draws.
///
/// <b>The rules are exclusions, and each removes a refusal the user would otherwise meet after clicking.</b>
/// A Studio Set is not a tone. A drum kit is 62 or 88 independently edited notes, so blending them produces a
/// kit that is no longer one -- the pad refuses them, and offering one would be inviting that refusal. And an
/// engine drop-down row that can only ever empty the list is worse than no row at all.</summary>
public static class MorphCandidates
{
    /// <summary>The entries a pad could use, out of everything the library listed. Order is the caller's to
    /// decide -- <see cref="LibraryListing.Sort"/> does that -- so this preserves what it was given.</summary>
    public static IReadOnlyList<LibraryEntry> In(IEnumerable<LibraryEntry> entries) =>
        entries.Where(e => e.Head.Kind == SnapshotKinds.Tone
                           && e.Head.ToneType is { } type
                           && ToneDomainNames.IsKnownToneType(type)
                           && !ToneDomainNames.IsDrumKit(type))
            .ToList();

    /// <summary>The library's own engine rows without the two kit engines. Same labels and same meanings as
    /// <see cref="LibraryListing.EngineLabels"/>, so <see cref="LibraryListing.EngineFromLabel"/> still reads
    /// them -- this is that list with rows removed, not a second vocabulary.</summary>
    public static IReadOnlyList<string> EngineLabels { get; } =
        [.. LibraryListing.EngineLabels.Where(label => label == LibraryListing.AnyEngine
                                                       || !ToneDomainNames.IsDrumKit(label))];

    /// <summary>How many the filters admit out of how many a pad could use. "This library holds nothing usable"
    /// and "these filters admit nothing" look identical on screen and have opposite remedies, so the empty case
    /// is a sentence rather than "0 of 0" -- and it names the kits, because a library that is mostly kits is
    /// the way this is usually reached.</summary>
    public static string Summary(int shown, int usable) => usable == 0
        ? "The library holds no tones a pad can use. Drum kits cannot be morphed, so they are not listed."
        : $"{shown} of {usable} tones.";
}
