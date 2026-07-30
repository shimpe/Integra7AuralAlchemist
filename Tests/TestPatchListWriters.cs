using System.Linq;
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
