using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Turning the instrument's presets into something addressable by bank select and program change.
/// </summary>
public class PatchListSourceTests
{
    // Integra7Preset's constructor checks four of its five strings -- tone type, tone bank, category and
    // INT/USR -- against the instrument's own vocabulary and throws MidiException otherwise, so the
    // defaults here are real values rather than plausible-looking ones. A fixture that threw would read as
    // a product bug for as long as it took to look at the stack trace. (Name is stored unchecked, which is
    // why the awkward-name cases live in the writers' tests rather than here.)
    private static Integra7Preset Preset(string name, int msb, int lsb, int pc,
        string type = "SN-A", string bank = "PRST", string usage = "INT", string category = "Ac.Piano") =>
        new(0, usage, type, bank, pc, name, msb, lsb, pc, category);

    /// <summary>The CSV counts programs from 1 because that is how Roland prints a tone list; every DAW
    /// format wants the byte that goes on the wire. The conversion happens here, once.</summary>
    [Test]
    public void Programs_are_numbered_from_nought()
    {
        var list = PatchListSource.From([Preset("Full Grand 1", 89, 64, 1)]);

        Assert.That(list.Banks[0].Patches[0].Program, Is.EqualTo(0));
    }

    [Test]
    public void One_bank_per_address()
    {
        var list = PatchListSource.From([
            Preset("A", 89, 64, 1), Preset("B", 89, 64, 2), Preset("C", 89, 65, 1)]);

        Assert.That(list.Banks, Has.Count.EqualTo(2));
        Assert.That(list.Banks[0].Patches, Has.Count.EqualTo(2));
    }

    /// <summary>Banks in address order and patches in program order, so that two exports of one instrument
    /// are the same file and a diff of them means something.</summary>
    [Test]
    public void Banks_and_patches_are_in_a_stable_order()
    {
        var list = PatchListSource.From([
            Preset("second", 89, 65, 1), Preset("later", 89, 64, 9), Preset("first", 89, 64, 2)]);

        Assert.That(list.Banks.Select(b => (b.Msb, b.Lsb)), Is.EqualTo(new[] { (89, 64), (89, 65) }));
        Assert.That(list.Banks[0].Patches.Select(p => p.Name), Is.EqualTo(new[] { "first", "later" }));
    }

    /// <summary>A factory bank is named for the engine, the bank it came from, and where it answers.
    /// </summary>
    [Test]
    public void A_factory_bank_is_named_for_its_engine_its_bank_and_its_address()
    {
        var list = PatchListSource.From([Preset("A", 89, 96, 1, bank: "ExSN1")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("SN-A ExSN1 (89/96)"));
    }

    /// <summary>Engine and bank do not identify a bank: one (engine, bank) spans up to ten addresses in the
    /// factory data, and 51 of the 75 banks would otherwise share a name. Only the CSV survives that, by
    /// printing MSB and LSB as columns of their own; the other three show a name and nothing else, so the
    /// user would be given nine identical "SN-S PRST" entries and no way to tell which held their tone.
    /// </summary>
    [Test]
    public void Two_banks_of_one_engine_and_bank_are_told_apart()
    {
        var list = PatchListSource.From([
            Preset("First", 95, 64, 1, type: "SN-S"), Preset("Second", 95, 65, 1, type: "SN-S")]);

        Assert.That(list.Banks.Select(b => b.Name),
            Is.EqualTo(new[] { "SN-S PRST (95/64)", "SN-S PRST (95/65)" }));
    }

    /// <summary>A user bank is named for being one. Its ToneBankStr says "PRST" -- the source marks that
    /// wrong and it is -- so naming from the bank string alone would label the user's own tones as factory
    /// ones, which is the one label that must not be wrong in a patch list.</summary>
    [Test]
    public void A_user_bank_says_it_is_user_memory()
    {
        var list = PatchListSource.From([Preset("Mine", 87, 0, 1, type: "PCMS", usage: "USR")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("PCMS USER (87/0)"));
    }

    /// <summary>Two patches at one address. The fixture is the pair that was in the shipped data until
    /// 2026-07-30 -- MSB 121 / LSB 0, printed by Roland as PC 116 and therefore program 115 on the wire,
    /// listed as both Woodblock and Castanets -- kept as the fixture because it is what a real one looks
    /// like. <c>Presets.csv</c> now has Castanets at LSB 1, where GM2 puts a variation, so nothing in the
    /// factory data collides; <see cref="PatchList"/> records why the handling stays anyway. The
    /// collision is reported with the wire number, because that is the number every other part of this
    /// feature uses and a report in the other numbering would disagree with the file it describes.
    ///
    /// Both patches are kept -- a patch list that quietly drops one to look tidy is worse than one that
    /// reports the truth -- and the collision is named so the export can say so.</summary>
    [Test]
    public void Two_patches_at_one_address_are_both_kept_and_reported()
    {
        var list = PatchListSource.From([
            Preset("Woodblock", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion"),
            Preset("Castanets", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion")]);

        Assert.That(list.Banks[0].Patches, Has.Count.EqualTo(2));
        Assert.That(list.Collisions, Has.Count.EqualTo(1));
        Assert.That(list.Collisions[0], Does.Contain("Woodblock").And.Contain("Castanets"));
        Assert.That(list.Collisions[0], Does.Contain("program 115"));
    }

    /// <summary>Document order decides which of two patches at one address comes first, because it is the
    /// order the instrument's own list is printed in and the only order a user could recognise.</summary>
    [Test]
    public void A_collision_keeps_the_order_the_presets_were_given_in()
    {
        var list = PatchListSource.From([
            Preset("Woodblock", 121, 0, 116), Preset("Castanets", 121, 0, 116)]);

        Assert.That(list.Banks[0].Patches.Select(p => p.Name),
            Is.EqualTo(new[] { "Woodblock", "Castanets" }));
    }

    /// <summary>A program the wire cannot carry is left out rather than written wrong: a file that names
    /// the patch at a program the DAW will never send is a file that lies about every one after it.
    /// </summary>
    [Test]
    public void A_program_outside_the_wire_range_is_left_out_and_reported()
    {
        var list = PatchListSource.From([Preset("Impossible", 89, 64, 200), Preset("Fine", 89, 64, 1)]);

        Assert.That(list.Banks[0].Patches.Select(p => p.Name), Is.EqualTo(new[] { "Fine" }));
        Assert.That(list.Skipped, Has.Count.EqualTo(1));
        Assert.That(list.Skipped[0], Does.Contain("Impossible"));
    }

    /// <summary>PC 128 is the top of the instrument's own numbering and it must survive, because it is
    /// where the factory data's biggest banks end: 41 of the 75 carry a PC 128 row. A range check written
    /// against the incoming number rather than the converted one loses exactly those 41 patches, one from
    /// the end of each of those banks, and passes every other test in this file while doing it -- there is
    /// nothing to see in the output but a list one patch shorter than the instrument's.</summary>
    [Test]
    public void The_last_program_the_wire_can_carry_is_kept()
    {
        var list = PatchListSource.From([Preset("Last", 89, 64, 128)]);

        Assert.That(list.Banks[0].Patches.Select(p => p.Program), Is.EqualTo(new[] { 127 }));
        Assert.That(list.Skipped, Is.Empty);
    }

    /// <summary>The two values just outside the range, which is where an off-by-one lives and nowhere else.
    ///
    /// PC 0 does not occur in the factory data, whose range is 1 to 128 -- it is guarded because the
    /// alternative is a program of -1, and a writer would put that in the file as readily as any other
    /// number: ".reabank" would gain a line reading "-1 Some Name", which loads, and Reaper would then be
    /// showing a patch no program change can ever select.</summary>
    [Test]
    public void The_programs_just_outside_the_wire_range_are_left_out()
    {
        var list = PatchListSource.From([Preset("Past the end", 89, 64, 129), Preset("Before the start", 89, 64, 0)]);

        Assert.That(list.Banks, Is.Empty);
        Assert.That(list.Skipped, Has.Count.EqualTo(2));
    }

    [Test]
    public void No_presets_is_an_empty_list_rather_than_a_failure()
    {
        var list = PatchListSource.From([]);

        Assert.That(list.Banks, Is.Empty);
        Assert.That(list.Collisions, Is.Empty);
    }
}
