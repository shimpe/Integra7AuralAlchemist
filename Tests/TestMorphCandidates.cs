using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What the morph pad's corner picker is allowed to offer.
///
/// Every rule here removes a refusal the user would otherwise meet after clicking, which is the whole reason
/// the picker replaced an operating system's file dialog. They live apart from the dialog because a view model
/// cannot be constructed in a test -- ReactiveUI 24 wants an initialised application before WhenAnyValue works
/// -- and because this is the part of a picker that can be wrong in a way the drawing cannot.</summary>
public class MorphCandidatesTests
{
    private static LibraryEntry Tone(string name, string engine) =>
        new($"{name}.json",
            new SnapshotHead(name, SnapshotKinds.Tone, engine, "Synth Pad/Strings", [], "", 0, false),
            new DateTime(2026, 7, 29, 8, 31, 0));

    private static LibraryEntry StudioSet(string name) =>
        new($"{name}.json",
            new SnapshotHead(name, SnapshotKinds.StudioSet, null, "", [], "", 0, false),
            new DateTime(2026, 7, 29, 8, 31, 0));

    private static readonly LibraryEntry[] Library =
    [
        Tone("Soft Pad 1", "SN-S"),
        Tone("128voicePno", "PCMS"),
        Tone("Full Grand 1", "SN-A"),
        Tone("Pop DrumSet 1", "PCMD"),
        Tone("Session Kit", "SN-D"),
        StudioSet("World Pop Set"),
    ];

    /// <summary>A kit is 62 or 88 independently edited notes; blending them produces a kit that is no longer
    /// one, so the pad refuses them and the picker must not offer them. A Studio Set is not a tone at
    /// all.</summary>
    [Test]
    public void Drum_kits_and_studio_sets_are_not_candidates()
    {
        var usable = MorphCandidates.In(Library).Select(e => e.Head.Name).ToList();

        Assert.That(usable, Is.EqualTo(new[] { "Soft Pad 1", "128voicePno", "Full Grand 1" }),
            "and in the order they were given, because sorting is the caller's business");
    }

    /// <summary>An engine this build does not know cannot be blended either -- there is no block list for it --
    /// and a file from a later build can carry one.</summary>
    [Test]
    public void An_unknown_engine_is_not_a_candidate()
    {
        Assert.That(MorphCandidates.In([Tone("From The Future", "SN-X")]), Is.Empty);
    }

    [Test]
    public void The_engine_rows_are_the_librarys_own_without_the_kits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MorphCandidates.EngineLabels,
                Is.EqualTo(new[] { LibraryListing.AnyEngine, "PCMS", "SN-S", "SN-A" }));
            // Still the library's own labels, so the library's own reader still understands them: this is that
            // list with rows removed, not a second vocabulary.
            Assert.That(LibraryListing.EngineFromLabel(MorphCandidates.EngineLabels[1]), Is.EqualTo("PCMS"));
        });
    }

    /// <summary>"This library holds nothing usable" and "these filters admit nothing" look identical on screen
    /// and have opposite remedies, so the empty case is a sentence and names the kits -- a library that is
    /// mostly kits is how it is usually reached.</summary>
    [Test]
    public void The_summary_separates_an_empty_library_from_an_empty_filter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MorphCandidates.Summary(1, 3), Is.EqualTo("1 of 3 tones."));
            Assert.That(MorphCandidates.Summary(0, 3), Is.EqualTo("0 of 3 tones."),
                "nothing admitted out of three usable is a filter to widen");
            Assert.That(MorphCandidates.Summary(0, 0), Does.Contain("Drum kits cannot be morphed"));
        });
    }
}
