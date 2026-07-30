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
}
