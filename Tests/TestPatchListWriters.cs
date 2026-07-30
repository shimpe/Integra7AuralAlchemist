using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One awkward patch list, shared by every writer's tests.
///
/// The names are the four things that break a text format -- an ampersand, a double quote, a comma and a
/// non-ASCII letter -- plus a newline, which is the one that breaks a format with no escaping at all.
/// Every writer is asked the same question so that the answers can be compared.</summary>
public static class AwkwardPatchList
{
    public static PatchList Build() => new(
        "INTEGRA-7",
        [
            new PatchBank(89, 64, "SN-A PRST",
            [
                new PatchEntry(0, "Rock & Roll", "SN-A", "E.Guitar", false),
                new PatchEntry(1, "The \"Big\" One", "SN-A", "Brass", false),
                new PatchEntry(2, "Strings, Warm", "SN-A", "Strings", false),
                new PatchEntry(3, "Café Piano", "SN-A", "Ac.Piano", false),
                new PatchEntry(4, "Split\nName", "SN-A", "FX", false),
            ]),
            new PatchBank(87, 0, "PCMS USER", [new PatchEntry(0, "Mine", "PCMS", "Synth Lead", true)]),
        ],
        [],
        []);
}

public class CsvPatchListWriterTests
{
    private static string Written() => new CsvPatchListWriter().Write(AwkwardPatchList.Build());

    /// <summary>A header, because this one is opened in a spreadsheet by a human rather than parsed by a
    /// DAW, and a column of bare numbers with no header is a puzzle.</summary>
    [Test]
    public void It_starts_with_a_header_row()
    {
        Assert.That(Written().Split("\r\n")[0], Is.EqualTo("MSB,LSB,Program,Bank,Name,Engine,Category,User"));
    }

    /// <summary>RFC 4180: a field containing a comma, a quote or a newline is quoted, and a quote inside is
    /// doubled. Excel and LibreOffice both read this; nothing else is portable.</summary>
    [Test]
    public void A_comma_a_quote_and_a_newline_are_quoted_and_doubled()
    {
        var rows = Written().Split("\r\n");

        Assert.That(rows[2], Does.Contain("\"The \"\"Big\"\" One\""));
        Assert.That(rows[3], Does.Contain("\"Strings, Warm\""));
        Assert.That(Written(), Does.Contain("\"Split\nName\""));
    }

    /// <summary>An ampersand and a non-ASCII letter are ordinary characters in CSV and are left alone --
    /// which is worth a test, because three of the four writers have to do something to them and copying
    /// that here would be the easy mistake.</summary>
    [Test]
    public void An_ampersand_and_an_accent_are_left_alone()
    {
        Assert.That(Written(), Does.Contain("Rock & Roll").And.Contain("Café Piano"));
    }

    /// <summary>Rows are separated by CRLF and the newline inside a name is a bare LF, so counting CRLFs
    /// counts rows -- which is the property a spreadsheet relies on and the reason the separator is not
    /// the LF this application uses everywhere else.</summary>
    [Test]
    public void A_newline_inside_a_name_does_not_make_a_second_row()
    {
        // One header and six patches: five in the first bank, one in the second.
        Assert.That(Written().TrimEnd().Split("\r\n").Length, Is.EqualTo(7));
    }

    [Test]
    public void User_memory_is_marked()
    {
        Assert.That(Written(), Does.Contain("87,0,0,PCMS USER,Mine,PCMS,Synth Lead,yes"));
    }

    /// <summary>And a factory patch is not, which needs its own test rather than trusting the one above:
    /// asserting only the row that says "yes" leaves the whole column pinned by nothing, and a writer that
    /// marked every row would pass. That writer would tell a user that all 6,023 factory tones are their
    /// own edits -- in the one file of the four they read by eye and believe.</summary>
    [Test]
    public void A_factory_row_is_not_marked()
    {
        Assert.That(Written().Split("\r\n")[1], Is.EqualTo("89,64,0,SN-A PRST,Rock & Roll,SN-A,E.Guitar,"));
    }

    /// <summary>The one format of the four that asks for a byte-order mark. Without it Excel opens the file
    /// in the system code page and the 84 factory names carrying a curly apostrophe come out as line noise,
    /// which is a failure the file itself gives no sign of.</summary>
    [Test]
    public void It_asks_for_a_byte_order_mark()
    {
        Assert.That(new CsvPatchListWriter().WantsByteOrderMark, Is.True);
    }
}

public class ReabankPatchListWriterTests
{
    private static string Written() => new ReabankPatchListWriter().Write(AwkwardPatchList.Build());

    [Test]
    public void A_bank_is_its_address_and_its_name()
    {
        Assert.That(Written(), Does.Contain("Bank 89 64 SN-A PRST"));
    }

    [Test]
    public void A_patch_is_its_program_and_its_name()
    {
        Assert.That(Written(), Does.Contain("\n0 Rock & Roll\n"));
    }

    /// <summary>The format has no escaping at all, so a newline inside a name would end the line and the
    /// next word would be read as a program number -- a patch list that is wrong from that point on, in a
    /// file that still loads. Flattened to a space instead.</summary>
    [Test]
    public void A_newline_in_a_name_becomes_a_space()
    {
        Assert.That(Written(), Does.Contain("4 Split Name"));
        Assert.That(Written().Split('\n').Any(line => line.Trim() == "Name"), Is.False);
    }

    /// <summary>Quotes, ampersands and accents are ordinary characters here: the format has no syntax for
    /// them to break. Sanitising more than the line ending would be mangling names for no reason.</summary>
    [Test]
    public void Nothing_else_is_altered()
    {
        Assert.That(Written(), Does.Contain("1 The \"Big\" One").And.Contain("3 Café Piano"));
    }

    /// <summary>A name that sanitises away to nothing still needs a name, or the line is a program number
    /// with nothing after it and Reaper shows a blank entry the user cannot identify.</summary>
    [Test]
    public void A_name_that_is_only_whitespace_gets_one()
    {
        var list = new PatchList("INTEGRA-7",
            [new PatchBank(89, 64, "SN-A PRST", [new PatchEntry(0, "  \t ", "SN-A", "FX", false)])], [], []);

        Assert.That(new ReabankPatchListWriter().Write(list), Does.Contain("0 (unnamed)"));
    }

    /// <summary>The whole body of the file, in order, because in this format order is the whole of the
    /// meaning: a patch line carries no back-reference to its bank, it belongs to whichever Bank line came
    /// before it. Every other test here asks whether one line is present somewhere, and a writer that
    /// emitted the second bank first, or all the banks and then all the patches, or only the first patch of
    /// each bank, would satisfy every one of them -- and both fixture banks have a patch at program 0, so
    /// the result would be a short, plausible, wrong list that Reaper loads without complaint.</summary>
    [Test]
    public void Bank_lines_and_patch_lines_come_out_in_the_order_that_assigns_them()
    {
        var lines = Written().Split('\n').Where(line => line.Length > 0 && !line.StartsWith(";")).ToList();

        Assert.That(lines, Is.EqualTo(new[]
        {
            "Bank 89 64 SN-A PRST", "0 Rock & Roll", "1 The \"Big\" One", "2 Strings, Warm", "3 Café Piano",
            "4 Split Name", "Bank 87 0 PCMS USER", "0 Mine",
        }));
    }

    /// <summary>No mark, and it matters more here than anywhere else in this feature: Reaper's parser reads
    /// a leading BOM as part of the first token, so the first line stops being a comment, and what the user
    /// sees is a bank that is simply absent with nothing said about it.
    ///
    /// Asked through the interface because the answer is a default interface member and this writer takes
    /// the default -- which is the point of the test, and is invisible on the concrete type.</summary>
    [Test]
    public void It_asks_for_no_byte_order_mark()
    {
        IPatchListWriter writer = new ReabankPatchListWriter();

        Assert.That(writer.WantsByteOrderMark, Is.False);
    }
}

/// <summary>Steinberg's exported MIDI device setup, which is a generic object graph rather than a schema
/// about patches: everything is an &lt;obj class="..." ID="..."&gt; carrying &lt;string&gt;, &lt;int&gt;,
/// &lt;list&gt; and &lt;bin&gt; members, and the patch list is what you get by nesting the right classes.
/// These tests are written against a real exported file -- see <c>CubasePatchListWriter</c> for which one
/// and where it came from -- so they pin the shape as Cubase itself writes it, not a shape that reads
/// nicely.</summary>
public class CubasePatchListWriterTests
{
    private static string Written() => new CubasePatchListWriter().Write(AwkwardPatchList.Build());

    private static XDocument Parsed() => XDocument.Parse(Written());

    /// <summary>The values of one named &lt;string&gt; member, in document order. Every user-visible name in
    /// this format is one of these, so this is how a test asks what the user will be shown.</summary>
    private static List<string> Strings(XDocument document, string member) =>
        document.Descendants("string")
            .Where(s => (string?)s.Attribute("name") == member)
            .Select(s => s.Attribute("value")!.Value)
            .ToList();

    [Test]
    public void It_is_well_formed_xml()
    {
        Assert.DoesNotThrow(() => XDocument.Parse(Written()));
    }

    /// <summary>The five characters XML reserves, in a patch name. An ampersand alone is what makes an
    /// unescaped document fail to parse at all, which is the failure a user sees as "the import did
    /// nothing".</summary>
    [Test]
    public void Xml_entities_are_escaped()
    {
        Assert.That(Written(), Does.Contain("Rock &amp; Roll"));
        Assert.That(Written(), Does.Not.Contain("Rock & Roll"));
    }

    /// <summary>Read back rather than matched as text: what matters is that a parser sees the original
    /// name, not which of the legal escapes was used to write it. The newline is the interesting one -- an
    /// attribute value is normalised on the way in, so a literal newline would come back as a space, and
    /// only the numeric escape survives.</summary>
    [Test]
    public void A_parser_reads_the_names_back_unchanged()
    {
        var names = Strings(Parsed(), "Name");

        Assert.That(names, Does.Contain("The \"Big\" One"));
        Assert.That(names, Does.Contain("Café Piano"));
        Assert.That(names, Does.Contain("Split\nName"));
    }

    /// <summary>A patch is a name and the messages that select it, and the messages are raw MIDI bytes:
    /// bank select MSB, bank select LSB, program change. 89 is 0x59 and 64 is 0x40, and the program is the
    /// wire value, so the first patch of the fixture's first bank is B0 00 59, B0 20 40, C0 00.
    ///
    /// <b>The Creator each message cites is asserted too, because the bytes alone do not say what they
    /// are.</b> Cubase resolves a message against the filter its Creator points at; a message whose bytes
    /// are a bank select and whose Creator says "Program Change" is a document that parses, imports, and
    /// selects the wrong sound -- and swapping the two control changes is invisible in the bytes unless
    /// something says which is which.</summary>
    [Test]
    public void Every_patch_carries_its_two_control_changes_and_its_program()
    {
        var document = Parsed();
        var patch = document.Descendants("obj").First(o => (string?)o.Attribute("class") == "PMidiPreset");
        var filters = document.Descendants("obj")
            .Where(o => (string?)o.Attribute("class") == "MidiStandardMessageFilter")
            .ToDictionary(o => o.Attribute("ID")!.Value,
                o => o.Elements("string").First(s => (string?)s.Attribute("name") == "Info")
                    .Attribute("value")!.Value);

        var messages = patch.Descendants("obj")
            .Where(o => (string?)o.Attribute("class") == "MidiSimpleKnownMessage")
            .Select(o => (
                Creator: filters[o.Elements("obj").First(c => (string?)c.Attribute("name") == "Creator")
                    .Attribute("ID")!.Value],
                Bytes: o.Elements("bin").First(b => (string?)b.Attribute("name") == "Message").Value))
            .ToList();

        Assert.That(messages, Is.EqualTo(new[]
        {
            ("CC: BankSelect MSB", "B00059"), ("CC: BankSelect LSB", "B02040"), ("Program Change", "C000"),
        }));
    }

    [Test]
    public void The_document_declares_utf8()
    {
        Assert.That(Written(), Does.StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>"));
    }

    /// <summary>All sixteen channels reach the patches. A part on this instrument can be on any of them,
    /// and a device whose names only exist on channel 1 looks perfectly correct to whoever exported it --
    /// they are on channel 1 -- while showing bare program numbers to the user who moved their part.
    /// </summary>
    [Test]
    public void Every_channel_is_offered_the_patches()
    {
        var channels = Parsed().Descendants("obj")
            .Where(o => o.Elements("int").Any(i => (string?)i.Attribute("name") == "IsChannelNode"))
            .ToList();

        Assert.That(channels, Has.Count.EqualTo(16));
        Assert.That(channels.All(c => c.Elements("list").Any(l => (string?)l.Attribute("name") == "Banks")),
            Is.True);
    }

    /// <summary>A bank keeps its name and its own patches in order. Every other test here looks at one
    /// patch or at the document as a whole, and a writer that put every patch in the first bank, or lost
    /// all but the first of each bank, would satisfy all of them.</summary>
    [Test]
    public void Each_bank_keeps_its_name_and_its_own_patches()
    {
        var banks = Parsed().Descendants("obj")
            .Where(o => (string?)o.Attribute("class") == "PSoundscriptBank"
                        && o.Elements("list").Any(l => (string?)l.Attribute("name") == "Presets"))
            .Select(o => (
                Name: o.Elements("string").First(s => (string?)s.Attribute("name") == "PresetBankName")
                    .Attribute("value")!.Value,
                Patches: o.Descendants("obj").Where(p => (string?)p.Attribute("class") == "PMidiPreset")
                    .Select(p => p.Elements("string").First(s => (string?)s.Attribute("name") == "Name")
                        .Attribute("value")!.Value).ToList()))
            .ToList();

        Assert.That(banks.Select(b => b.Name), Is.EqualTo(new[] { "SN-A PRST", "PCMS USER" }));
        Assert.That(banks[0].Patches, Is.EqualTo(new[]
            { "Rock & Roll", "The \"Big\" One", "Strings, Warm", "Café Piano", "Split\nName" }));
        Assert.That(banks[1].Patches, Is.EqualTo(new[] { "Mine" }));
    }

    /// <summary>Nothing points at an object that is not there. This format is an object graph written flat:
    /// a bank the other fifteen channels share is written once and cited by ID, and every message cites the
    /// filter that says what its bytes mean. A citation that resolves to nothing is the characteristic
    /// failure of writing a graph by hand, and Cubase's answer to it is to import the document and show
    /// nothing rather than to complain.</summary>
    [Test]
    public void Every_object_reference_resolves()
    {
        var document = Parsed();
        var defined = document.Descendants("obj")
            .Where(o => o.Attribute("class") is not null && o.Attribute("ID") is not null)
            .Select(o => o.Attribute("ID")!.Value).ToHashSet();

        var cited = document.Descendants("obj")
            .Where(o => o.Attribute("class") is null && o.Attribute("ID") is not null)
            .Select(o => o.Attribute("ID")!.Value)
            .Concat(document.Descendants("item")
                .Where(i => (string?)i.Parent!.Attribute("type") == "obj" && i.Attribute("value") is not null)
                .Select(i => i.Attribute("value")!.Value))
            .ToList();

        Assert.That(cited, Is.Not.Empty);
        Assert.That(cited.Where(id => !defined.Contains(id)), Is.Empty);
    }

    /// <summary>No mark. The document declares its own encoding, which is what an XML parser is required to
    /// believe, and a byte-order mark in front of the declaration is a token some readers hand to the
    /// parser rather than eat.</summary>
    [Test]
    public void It_asks_for_no_byte_order_mark()
    {
        IPatchListWriter writer = new CubasePatchListWriter();

        Assert.That(writer.WantsByteOrderMark, Is.False);
    }
}

/// <summary>The MMA MIDINameDocument, which Ardour and Mixbus read. Unlike the Cubase format this one is a
/// published DTD and a schema about patches, so the shape is not in doubt; what these tests pin is the two
/// numbers a patch carries and the fact that all sixteen channels can reach it.</summary>
public class MidnamPatchListWriterTests
{
    private static string Written() => new MidnamPatchListWriter().Write(AwkwardPatchList.Build());

    [Test]
    public void It_is_well_formed_and_declares_the_mma_doctype()
    {
        Assert.That(Written(), Does.Contain("<!DOCTYPE MIDINameDocument PUBLIC"));
        Assert.DoesNotThrow(() => XDocument.Parse(Written()));
    }

    [Test]
    public void A_bank_carries_its_two_control_changes()
    {
        var bank = XDocument.Parse(Written()).Descendants("PatchBank").First();
        var changes = bank.Descendants("ControlChange")
            .Select(c => (c.Attribute("Control")!.Value, c.Attribute("Value")!.Value)).ToList();

        // The fixture's own first bank is 89/64 -- it is built by hand, not through PatchListSource, so it
        // is in the order it is written in rather than in address order.
        Assert.That(changes, Is.EqualTo(new[] { ("0", "89"), ("32", "64") }));
    }

    /// <summary>Number is a label and ProgramChange is the wire value, and they are not the same number.
    ///
    /// <b>The reason is not the one it is easy to assume.</b> Ardour does not display Number at all -- its
    /// own parser says so in a comment and skips the attribute -- so writing the wire value there would be
    /// invisible in the reader this format was chosen for, and would still be wrong: the DTD wants Number
    /// unique within its list, other readers show it as the patch's label, and Roland's own printed tone
    /// list counts from 1. Roland_SonicCell.midnam, shipped with Ardour and describing an instrument
    /// addressed exactly like this one, writes Number="001" against ProgramChange="0".</summary>
    [Test]
    public void The_display_number_and_the_program_change_are_not_the_same_number()
    {
        var patch = XDocument.Parse(Written()).Descendants("Patch").First();

        Assert.That(patch.Attribute("ProgramChange")!.Value, Is.EqualTo("0"));
        Assert.That(patch.Attribute("Number")!.Value, Is.EqualTo("1"));
    }

    /// <summary>And it restarts in each bank, because it labels a position in one bank's list rather than a
    /// position in the instrument. A single counter running through all 6,023 patches would satisfy the test
    /// above -- the first patch of the first bank is 1 either way -- and would then label the second bank's
    /// first patch 6 in a fixture, or 130 in the real data, against a program change of 0.</summary>
    [Test]
    public void The_display_number_restarts_in_each_bank()
    {
        var banks = XDocument.Parse(Written()).Descendants("PatchBank").ToList();

        Assert.That(banks[0].Descendants("Patch").Select(p => p.Attribute("Number")!.Value),
            Is.EqualTo(new[] { "1", "2", "3", "4", "5" }));
        Assert.That(banks[1].Descendants("Patch").Select(p => p.Attribute("Number")!.Value),
            Is.EqualTo(new[] { "1" }));
    }

    [Test]
    public void Every_channel_is_offered_the_name_set()
    {
        var doc = XDocument.Parse(Written());

        Assert.That(doc.Descendants("AvailableChannel").Count(), Is.EqualTo(16));
        Assert.That(doc.Descendants("ChannelNameSetAssign").Count(), Is.EqualTo(16));
    }

    [Test]
    public void A_parser_reads_the_names_back_unchanged()
    {
        var names = XDocument.Parse(Written())
            .Descendants("Patch").Select(p => p.Attribute("Name")!.Value).ToList();

        Assert.That(names, Does.Contain("Rock & Roll").And.Contain("Café Piano"));
    }

    /// <summary>Each bank keeps its name and its own patches. Everything above looks at the first bank or at
    /// the document as a whole, and a writer that put all six patches in the first bank -- or wrote one bank
    /// per patch -- would pass all of it.</summary>
    [Test]
    public void Each_bank_keeps_its_name_and_its_own_patches()
    {
        var banks = XDocument.Parse(Written()).Descendants("PatchBank")
            .Select(b => (Name: b.Attribute("Name")!.Value,
                Patches: b.Descendants("Patch").Select(p => p.Attribute("Name")!.Value).ToList()))
            .ToList();

        Assert.That(banks.Select(b => b.Name), Is.EqualTo(new[] { "SN-A PRST", "PCMS USER" }));
        Assert.That(banks[0].Patches, Is.EqualTo(new[]
            { "Rock & Roll", "The \"Big\" One", "Strings, Warm", "Café Piano", "Split\nName" }));
        Assert.That(banks[1].Patches, Is.EqualTo(new[] { "Mine" }));
    }

    /// <summary>The name set the channels are assigned to is the one that exists. The assignment is by name,
    /// so a mismatch between the two spellings is a document that parses, validates and offers every channel
    /// a name set that is not there -- which is the whole patch list gone, silently.</summary>
    [Test]
    public void The_channels_are_assigned_a_name_set_that_exists()
    {
        var doc = XDocument.Parse(Written());
        var sets = doc.Descendants("ChannelNameSet").Select(s => s.Attribute("Name")!.Value).ToHashSet();
        var assigned = doc.Descendants("ChannelNameSetAssign")
            .Select(a => a.Attribute("NameSet")!.Value).Distinct().ToList();

        Assert.That(assigned, Is.Not.Empty);
        Assert.That(assigned.Where(name => !sets.Contains(name)), Is.Empty);
    }

    /// <summary>No mark -- several midnam readers take a leading byte-order mark as part of the first token,
    /// and what the user sees is a file that does not load at all.</summary>
    [Test]
    public void It_asks_for_no_byte_order_mark()
    {
        IPatchListWriter writer = new MidnamPatchListWriter();

        Assert.That(writer.WantsByteOrderMark, Is.False);
    }
}
