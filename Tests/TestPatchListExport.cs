using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What the status bar says after a patch list has been written.
///
/// <b>These sentences are the only place the two lists of prose ever surface.</b> A patch list that dropped a
/// patch, or that put two patches at one address, produces a file that looks exactly like a correct one -- the
/// user finds out when a track plays the wrong sound. So the wording is tested here rather than composed in a
/// view model, where nothing could reach it.</summary>
public class PatchListExportTests
{
    private static PatchEntry Patch(int program, string name) =>
        new(program, name, "SN-A", "Ac.Piano", false);

    private static PatchList List(IReadOnlyList<PatchEntry> patches,
        IReadOnlyList<string>? collisions = null, IReadOnlyList<string>? skipped = null) =>
        new("INTEGRA-7", [new PatchBank(89, 64, "SN-A PRST (89/64)", patches)],
            collisions ?? [], skipped ?? []);

    [Test]
    public void A_clean_export_says_how_many_patches_went_where()
    {
        var said = PatchListExport.Outcome(List([Patch(0, "Full Grand 1"), Patch(1, "Full Grand 2")]),
            "INTEGRA-7.reabank");

        Assert.That(said, Is.EqualTo("Exported 2 patches to INTEGRA-7.reabank."));
    }

    /// <summary>Six thousand of anything is a number nobody reads as a quantity without the separator, and
    /// the separator is the invariant one rather than the machine's: the same export on two machines has to
    /// be describable in the same words, and a test that passed only under an English locale would be a test
    /// that fails on the user's own machine and nowhere else.</summary>
    [Test]
    public void A_large_count_is_grouped_so_it_can_be_read()
    {
        var patches = Enumerable.Range(0, 1234).Select(n => Patch(n % 128, $"Tone {n}")).ToList();

        Assert.That(PatchListExport.Outcome(List(patches), "a.csv"), Does.Contain("1,234 patches"));
    }

    /// <summary>One patch is not "1 patches". The instrument has banks with a single tone in them, so this
    /// is the ordinary case for a hand-built list rather than an edge one.</summary>
    [Test]
    public void One_patch_is_not_plural()
    {
        Assert.That(PatchListExport.Outcome(List([Patch(0, "Only One")]), "a.csv"),
            Does.Contain("1 patch to").And.Not.Contain("1 patches"));
    }

    /// <summary>The collision the instrument's own data has, said out loud. The wording quotes the builder's
    /// own sentence, so the address and the program number in the status line are the same ones the file
    /// carries -- program 115, not the 116 the printed tone list shows.</summary>
    [Test]
    public void A_collision_is_named_and_what_it_costs_is_said()
    {
        var said = PatchListExport.Outcome(
            List([Patch(115, "Woodblock"), Patch(115, "Castanets")],
                collisions: ["MSB 121 LSB 0 program 115: Woodblock, Castanets"]),
            "INTEGRA-7.reabank");

        Assert.That(said, Does.Contain("MSB 121 LSB 0 program 115: Woodblock, Castanets"));
        Assert.That(said, Does.Contain("your DAW will show one of them"));
    }

    /// <summary>More than one, and the first is still named. A count on its own tells the user something
    /// happened and gives them nothing to look at; a list of all of them would fill a status bar that is one
    /// line high.</summary>
    [Test]
    public void Several_collisions_are_counted_and_the_first_is_named()
    {
        var said = PatchListExport.Outcome(
            List([Patch(0, "A")], collisions: ["first collision", "second collision"]),
            "a.reabank");

        Assert.That(said, Does.Contain("2 addresses"));
        Assert.That(said, Does.Contain("first collision"));
        Assert.That(said, Does.Not.Contain("second collision"));
    }

    /// <summary>A patch the wire cannot carry is left out by the builder, and a file quietly one patch short
    /// is the failure this whole feature exists to prevent.</summary>
    [Test]
    public void A_patch_that_was_left_out_is_named()
    {
        var said = PatchListExport.Outcome(
            List([Patch(0, "Fine")], skipped: ["Impossible (program 200)"]), "a.csv");

        Assert.That(said, Does.Contain("Impossible (program 200)"));
        Assert.That(said, Does.Contain("left out"));
    }

    [Test]
    public void Several_skipped_patches_are_counted_and_the_first_is_named()
    {
        var said = PatchListExport.Outcome(
            List([Patch(0, "Fine")], skipped: ["one (program 200)", "two (program 201)"]), "a.csv");

        Assert.That(said, Does.Contain("2 patches"));
        Assert.That(said, Does.Contain("one (program 200)"));
        Assert.That(said, Does.Not.Contain("two (program 201)"));
    }

    /// <summary>Both at once is not an either/or: the two lists answer different questions and a file can
    /// have both problems.</summary>
    [Test]
    public void A_collision_and_a_skip_are_both_reported()
    {
        var said = PatchListExport.Outcome(
            List([Patch(0, "A")], collisions: ["a collision"], skipped: ["a skip"]), "a.csv");

        Assert.That(said, Does.Contain("a collision").And.Contain("a skip"));
    }

    /// <summary>An empty file is written rather than refused -- the writers all produce something valid for
    /// an empty list -- but it must not be described as an export of nought patches in the same breath as a
    /// success, because that is what a broken preset load looks like and the user can do something about
    /// it.</summary>
    [Test]
    public void Nothing_to_export_says_so_rather_than_quoting_a_nought()
    {
        var said = PatchListExport.Outcome(new PatchList("INTEGRA-7", [], [], []), "empty.csv");

        Assert.That(said, Does.Contain("empty.csv").And.Contain("no patches"));
        Assert.That(said, Does.Not.Contain("Exported 0"));
    }

    [Test]
    public void The_suggested_file_name_carries_the_format_s_extension()
    {
        Assert.That(PatchListExport.FileNameFor("INTEGRA-7", "reabank"), Is.EqualTo("INTEGRA-7.reabank"));
    }

    /// <summary>A file called ".csv" is, on Windows with extensions hidden, a file with no name at all.
    /// </summary>
    [Test]
    public void A_device_with_no_name_still_gets_a_file_name()
    {
        Assert.That(PatchListExport.FileNameFor("   ", "csv"), Is.EqualTo("Patch list.csv"));
    }
}
