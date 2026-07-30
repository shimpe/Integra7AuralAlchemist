using System.Collections.Generic;
using System.Linq;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What an export is called, what bytes it is made of, and what the status bar says afterwards.
///
/// <b>These sentences are the only place the truth about an export ever surfaces.</b> A patch list that
/// dropped a patch, that put two patches at one address, or that was written while the instrument's user
/// memory was still arriving, produces a file that looks exactly like a correct one -- the user finds out
/// when a track plays the wrong sound, or when the tone they saved yesterday is not in the menu. So the
/// wording is tested here rather than composed in a view model, where nothing could reach it.</summary>
public class PatchListExportTests
{
    private static PatchEntry Patch(int program, string name, bool user = false) =>
        new(program, name, "SN-A", "Ac.Piano", user);

    private static PatchList List(IReadOnlyList<PatchEntry> patches,
        IReadOnlyList<string>? collisions = null, IReadOnlyList<string>? skipped = null) =>
        new("INTEGRA-7", [new PatchBank(89, 64, "SN-A PRST (89/64)", patches)],
            collisions ?? [], skipped ?? []);

    /// <summary>The ordinary case: everything the instrument has, read and written.</summary>
    private static string Outcome(PatchList list, string fileName = "a.csv",
        bool userMemoryComplete = true) =>
        PatchListExport.Outcome(list, fileName, userMemoryComplete);

    [Test]
    public void A_clean_export_says_how_many_patches_went_where()
    {
        var said = Outcome(List([Patch(0, "Full Grand 1"), Patch(1, "Full Grand 2")]),
            "INTEGRA-7.reabank");

        Assert.That(said, Does.StartWith("Exported 2 patches to INTEGRA-7.reabank."));
    }

    /// <summary>Six thousand of anything is a number nobody reads as a quantity without the separator, and
    /// the separator is the invariant one rather than the machine's, so that the same export is described in
    /// the same words wherever it happens. Run under a culture that groups with a full stop, because this
    /// machine's own culture groups with a comma and would let <c>CurrentCulture</c> pass unnoticed.</summary>
    [Test]
    [SetCulture("de-DE")]
    public void A_large_count_is_grouped_the_same_way_on_every_machine()
    {
        var patches = Enumerable.Range(0, 1234).Select(n => Patch(n % 128, $"Tone {n}")).ToList();

        Assert.That(Outcome(List(patches)), Does.Contain("1,234 patches"));
    }

    /// <summary>One patch is not "1 patches". The instrument has banks with a single tone in them, so this
    /// is the ordinary case for a hand-built list rather than an edge one.</summary>
    [Test]
    public void One_patch_is_not_plural()
    {
        Assert.That(Outcome(List([Patch(0, "Only One")])),
            Does.Contain("1 patch to").And.Not.Contain("1 patches"));
    }

    // ---- what is in the file, and what is not -----------------------------------------------------------

    /// <summary>The whole point of exporting from a connected instrument rather than from the shipped CSV:
    /// the user's own sounds are in the file, and the sentence says how many, so "my tones are missing" is
    /// answerable without opening it.</summary>
    [Test]
    public void The_user_s_own_tones_are_counted_separately()
    {
        var said = Outcome(List([Patch(0, "Full Grand 1"), Patch(1, "Mine", user: true)]));

        Assert.That(said, Does.StartWith("Exported 2 patches to a.csv."));
        Assert.That(said, Does.Contain("1 of them are your own"));
    }

    /// <summary>Nothing plugged in. The factory tones are still worth exporting -- they are what the
    /// instrument can be sent to either way -- but a file described only as "6,023 patches" would let a user
    /// who has 256 user tones believe they were in it.</summary>
    [Test]
    public void No_user_memory_at_all_is_said_rather_than_left_to_be_noticed()
    {
        var said = Outcome(List([Patch(0, "Full Grand 1")]), userMemoryComplete: false);

        Assert.That(said, Does.Contain("No user-memory tones"));
        Assert.That(said, Does.Contain("none had been read from the instrument"));
    }

    /// <summary>Pressed while the names are still arriving, or after a rescan cancelled the sweep partway --
    /// which leaves the same partial list behind for the rest of the session. Some of the user's tones are
    /// there and some are not, and that is the one state a confident count would misdescribe.</summary>
    [Test]
    public void A_user_memory_still_being_read_is_flagged_as_incomplete()
    {
        var said = Outcome(List([Patch(0, "Full Grand 1"), Patch(1, "Mine", user: true)]),
            userMemoryComplete: false);

        Assert.That(said, Does.Contain("1 of them are your own"));
        Assert.That(said, Does.Contain("still being read"));
    }

    /// <summary>Complete and none found is not the same as not having looked, and must not be said the same
    /// way: one means the user memory is empty, the other means nobody asked.</summary>
    [Test]
    public void A_read_that_found_no_user_tones_does_not_read_as_never_having_looked()
    {
        var said = Outcome(List([Patch(0, "Full Grand 1")]));

        Assert.That(said, Does.Contain("None of them are from the instrument's user memory."));
        Assert.That(said, Does.Not.Contain("had not been read"));
    }

    // ---- what could not be represented ------------------------------------------------------------------

    /// <summary>The collision the instrument's own data has, said out loud. The wording quotes the builder's
    /// own sentence, so the address and the program number in the status line are the same ones the file
    /// carries -- program 115, not the 116 the printed tone list shows.
    ///
    /// <b>It does not claim the DAW will hide one of them.</b> Three of the four formats can name two patches
    /// at one address and their readers will list both; what those two share is the program change, so which
    /// sound arrives is the instrument's decision. A message that told the user to expect one entry, in a
    /// menu that then shows two, is a message they will decide is wrong about everything else too.</summary>
    [Test]
    public void A_collision_is_named_and_what_it_costs_is_said()
    {
        var said = Outcome(
            List([Patch(115, "Woodblock"), Patch(115, "Castanets")],
                collisions: ["MSB 121 LSB 0 program 115: Woodblock, Castanets"]),
            "INTEGRA-7.reabank");

        Assert.That(said, Does.Contain("MSB 121 LSB 0 program 115: Woodblock, Castanets"));
        Assert.That(said, Does.Contain("same program change"));
        Assert.That(said, Does.Not.Contain("will show one of them"));
    }

    /// <summary>More than one, and the first is still named. A count on its own tells the user something
    /// happened and gives them nothing to look at; a list of all of them would fill a status bar that is one
    /// line high.</summary>
    [Test]
    public void Several_collisions_are_counted_and_the_first_is_named()
    {
        var said = Outcome(List([Patch(0, "A")], collisions: ["first collision", "second collision"]));

        Assert.That(said, Does.Contain("2 addresses"));
        Assert.That(said, Does.Contain("first collision"));
        Assert.That(said, Does.Not.Contain("second collision"));
    }

    /// <summary>A patch the wire cannot carry is left out by the builder, and a file quietly one patch short
    /// is the failure this whole feature exists to prevent.
    ///
    /// <b>The leading count is asserted here and not only in the clean case.</b> "How many were written" and
    /// "how many presets made it this far" are the two numbers it is natural to confuse, and subtracting the
    /// skipped ones from the count would describe a file that has two patches in it as having one -- a
    /// mistake every substring assertion in this fixture would otherwise sail past.</summary>
    [Test]
    public void A_patch_that_was_left_out_is_named_without_being_taken_off_the_count()
    {
        var said = Outcome(List([Patch(0, "Fine"), Patch(1, "Also fine")],
            skipped: ["Impossible (program 200)"]));

        Assert.That(said, Does.StartWith("Exported 2 patches to a.csv."));
        Assert.That(said, Does.Contain("Impossible (program 200)"));
        Assert.That(said, Does.Contain("left out"));
    }

    [Test]
    public void Several_skipped_patches_are_counted_and_the_first_is_named()
    {
        var said = Outcome(List([Patch(0, "Fine")],
            skipped: ["one (program 200)", "two (program 201)"]));

        Assert.That(said, Does.StartWith("Exported 1 patch to a.csv."));
        Assert.That(said, Does.Contain("2 patches were left out"));
        Assert.That(said, Does.Contain("one (program 200)"));
        Assert.That(said, Does.Not.Contain("two (program 201)"));
    }

    /// <summary>Both at once is not an either/or: the two lists answer different questions and a file can
    /// have both problems.</summary>
    [Test]
    public void A_collision_and_a_skip_are_both_reported()
    {
        var said = Outcome(List([Patch(0, "A")], collisions: ["a collision"], skipped: ["a skip"]));

        Assert.That(said, Does.Contain("a collision").And.Contain("a skip"));
    }

    /// <summary>An empty file is written rather than refused -- the writers all produce something valid for
    /// an empty list -- but it must not be described as an export of nought patches in the same breath as a
    /// success, because that is what a broken preset load looks like and the user can do something about
    /// it.</summary>
    [Test]
    public void Nothing_to_export_says_so_rather_than_quoting_a_nought()
    {
        var said = Outcome(new PatchList("INTEGRA-7", [], [], []), "empty.csv");

        Assert.That(said, Does.Contain("empty.csv").And.Contain("no patches"));
        Assert.That(said, Does.Not.Contain("Exported 0"));
    }

    // ---- the file's name and its bytes ------------------------------------------------------------------

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

    /// <summary>Excel opening a BOM-less UTF-8 .csv by double-click falls back to the system code page and
    /// mangles the 84 factory names that carry a curly apostrophe. The mark is three bytes at the front and
    /// there is no other way to see it.</summary>
    [Test]
    public void The_spreadsheet_s_bytes_begin_with_a_byte_order_mark()
    {
        var bytes = PatchListExport.BytesFor(new CsvPatchListWriter(), AwkwardPatchList.Build());

        Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    /// <summary>And the other three must not have one: Reaper's parser and several midnam readers take a
    /// leading mark as part of the first token, and the symptom is a bank that simply does not appear.
    ///
    /// <b>This is the assertion that was impossible while the encoding was chosen in a view model.</b> Every
    /// writer's <c>WantsByteOrderMark</c> was pinned, and the one line that joined them to a file was the one
    /// line no test could reach -- so hardcoding either answer there left the whole suite green.</summary>
    [Test]
    public void Every_other_format_s_bytes_begin_with_its_own_first_character()
    {
        foreach (var writer in PatchListWriters.All.Where(w => !w.WantsByteOrderMark))
        {
            var bytes = PatchListExport.BytesFor(writer, AwkwardPatchList.Build());

            Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }),
                $"{writer.Label} must not be written with a byte-order mark");
        }
    }

    /// <summary>Past the mark, the bytes are the writer's own text and nothing else -- no re-encoding and no
    /// line endings rewritten. Every writer, because each settled its own line endings for its own reasons
    /// and this is the step that could quietly undo any of them.</summary>
    [Test]
    public void The_bytes_are_the_writer_s_text_in_utf8()
    {
        foreach (var writer in PatchListWriters.All)
        {
            var bytes = PatchListExport.BytesFor(writer, AwkwardPatchList.Build());
            var skip = writer.WantsByteOrderMark ? 3 : 0;

            Assert.That(new UTF8Encoding(false).GetString(bytes.Skip(skip).ToArray()),
                Is.EqualTo(writer.Write(AwkwardPatchList.Build())), writer.Label);
        }
    }
}
