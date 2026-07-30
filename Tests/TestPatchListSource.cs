using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Turning the instrument's presets into something addressable by bank select and program change.
/// </summary>
public class PatchListSourceTests
{
    // Integra7Preset's constructor validates every one of its strings against the instrument's own
    // vocabulary and throws MidiException otherwise, so the defaults here are real values rather than
    // plausible-looking ones. A fixture that threw would read as a product bug for as long as it took to
    // look at the stack trace.
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

    /// <summary>A factory bank is named for the engine and the bank it came from.</summary>
    [Test]
    public void A_factory_bank_is_named_for_its_engine_and_bank()
    {
        var list = PatchListSource.From([Preset("A", 89, 96, 1, bank: "ExSN1")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("SN-A ExSN1"));
    }

    /// <summary>A user bank is named for being one. Its ToneBankStr says "PRST" -- the source marks that
    /// wrong and it is -- so naming from the bank string alone would label the user's own tones as factory
    /// ones, which is the one label that must not be wrong in a patch list.</summary>
    [Test]
    public void A_user_bank_says_it_is_user_memory()
    {
        var list = PatchListSource.From([Preset("Mine", 87, 0, 1, type: "PCMS", usage: "USR")]);

        Assert.That(list.Banks[0].Name, Is.EqualTo("PCMS USER"));
    }

    /// <summary>Two patches at one address is in the instrument's own data: MSB 121 / LSB 0 / PC 116 is
    /// both Woodblock and Castanets. Both are kept -- a patch list that quietly drops one to look tidy is
    /// worse than one that reports the truth -- and the collision is named so the export can say so.
    /// </summary>
    [Test]
    public void Two_patches_at_one_address_are_both_kept_and_reported()
    {
        var list = PatchListSource.From([
            Preset("Woodblock", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion"),
            Preset("Castanets", 121, 0, 116, type: "PCMS", bank: "GM2/GM2#", category: "Percussion")]);

        Assert.That(list.Banks[0].Patches, Has.Count.EqualTo(2));
        Assert.That(list.Collisions, Has.Count.EqualTo(1));
        Assert.That(list.Collisions[0], Does.Contain("Woodblock").And.Contain("Castanets"));
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

    [Test]
    public void No_presets_is_an_empty_list_rather_than_a_failure()
    {
        var list = PatchListSource.From([]);

        Assert.That(list.Banks, Is.Empty);
        Assert.That(list.Collisions, Is.Empty);
    }
}
