using System;
using System.IO;
using System.Linq;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Reading the shipped preset table.
///
/// <b>Why this exists at all.</b> The parser lived inside <c>MainWindowViewModel</c>, where nothing could
/// reach it: instantiating that view model needs an Avalonia application and a live device domain, so the
/// one piece of code that decides what 6,023 preset names are was the one piece with no test. It split each
/// line on every comma and indexed the result, which meant two things at once -- a name containing a comma
/// could not be written into the file at all, and a row that was malformed for any reason failed with an
/// <see cref="IndexOutOfRangeException"/> naming nothing.
///
/// <b>Both matter for the same reason.</b> Two factory tones really are called <c>Old,Warm OBX</c> and
/// <c>1,2,3,4! SRX</c> -- read back from the instrument -- and these names are not decoration: they feed
/// the preset grids, the morph tone picker and every DAW patch list the application exports.</summary>
public class PresetTableTests
{
    private const string Header = "\"Tone Type\",\"Tone Bank\",\"No.\",\"Tone Name\",\"MSB\",\"LSB\",\"PC\",\"Category\"";

    private static Stream Csv(params string[] rows) =>
        new MemoryStream(new UTF8Encoding(false).GetBytes(string.Join("\r\n", rows.Prepend(Header))));

    /// <summary>The case the old parser could not express. A quoted field's commas are part of the name,
    /// not separators, so every field after it still lands where it belongs -- which is what the assertions
    /// on MSB/LSB/PC are really checking.</summary>
    [Test]
    public void A_quoted_name_containing_commas_is_one_field()
    {
        var presets = PresetTable.Load(Csv("\"PCMS\",\"SRX07\",0408,\"Old,Warm OBX\",93,14,24,\"Synth PolyKey\""));

        Assert.That(presets, Has.Count.EqualTo(1));
        Assert.That(presets[0].Name, Is.EqualTo("Old,Warm OBX"));
        Assert.That(presets[0].Msb, Is.EqualTo(93));
        Assert.That(presets[0].Lsb, Is.EqualTo(14));
        Assert.That(presets[0].Pc, Is.EqualTo(24));
        Assert.That(presets[0].ToneBankStr, Is.EqualTo("SRX07"));
        Assert.That(presets[0].CategoryStr, Is.EqualTo("Synth PolyKey"));
    }

    /// <summary>Several commas in one name, which is the other real row: three of them, and the fields
    /// after it still parse. A parser that handled one comma by counting from the end would pass the test
    /// above and fail this one.</summary>
    [Test]
    public void A_name_may_contain_several_commas()
    {
        var presets = PresetTable.Load(Csv("\"PCMS\",\"SRX09\",0410,\"1,2,3,4! SRX\",93,22,26,\"Vox/Choir\""));

        Assert.That(presets[0].Name, Is.EqualTo("1,2,3,4! SRX"));
        Assert.That(presets[0].Pc, Is.EqualTo(26));
    }

    /// <summary>The ordinary row, which is 6,021 of the 6,023. Worth its own test because the fix for the
    /// two above is exactly the kind that quietly changes the common case -- a quoted field must still come
    /// back without its quotes.</summary>
    [Test]
    public void A_quoted_name_without_commas_is_read_as_written()
    {
        var presets = PresetTable.Load(Csv("\"SN-A\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\""));

        Assert.That(presets[0].Name, Is.EqualTo("Full Grand 1"));
        Assert.That(presets[0].ToneTypeStr, Is.EqualTo("SN-A"));
    }

    /// <summary>Internal double spaces survive. 27 names in the table carry them -- the instrument really
    /// does display "Kick 1  Menu" -- and a parser that trimmed or collapsed whitespace would undo a
    /// correction that was made by reading the hardware.</summary>
    [Test]
    public void Internal_double_spaces_are_preserved()
    {
        var presets = PresetTable.Load(Csv("\"PCMS\",\"SRX01\",0001,\"Kick 1  Menu\",93,0,1,\"Drums\""));

        Assert.That(presets[0].Name, Is.EqualTo("Kick 1  Menu"));
    }

    /// <summary>The real header line, skipped. It is quoted and has the same shape as a data row, so a
    /// parser that only skipped lines starting with a letter, or that counted fields, would read it as a
    /// preset called "Tone Name" -- and <c>Integra7Preset</c> would then throw on the tone type rather than
    /// on anything that named the header.</summary>
    [Test]
    public void The_header_row_is_skipped()
    {
        var presets = PresetTable.Load(Csv("\"SN-A\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\""));

        Assert.That(presets, Has.Count.EqualTo(1));
        Assert.That(presets.Any(p => p.Name == "Tone Name"), Is.False);
    }

    /// <summary>Ids are assigned in file order from zero, because <c>UserToneSlots</c> derives the
    /// instrument's user memory slot number from ascending Id and a wrong one overwrites a different saved
    /// sound.</summary>
    [Test]
    public void Ids_are_assigned_in_file_order_from_zero()
    {
        var presets = PresetTable.Load(Csv(
            "\"SN-A\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\"",
            "\"SN-A\",\"PRST\",0002,\"Full Grand 2\",89,64,2,\"Ac.Piano\""));

        Assert.That(presets.Select(p => p.Id), Is.EqualTo(new[] { 0, 1 }));
    }

    /// <summary>A short row names the line and says what it found. The old parser answered this with
    /// <see cref="IndexOutOfRangeException"/>, which says nothing about which of 6,023 rows was wrong --
    /// and this is a build asset, so whoever sees it is the person who just edited it.</summary>
    [Test]
    public void A_row_with_too_few_fields_names_the_line()
    {
        var e = Assert.Throws<PresetTableFormatException>(() =>
            PresetTable.Load(Csv("\"SN-A\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\"",
                "\"SN-A\",\"PRST\",0002,\"Truncated\"")));

        Assert.That(e!.Message, Does.Contain("line 3"));
        Assert.That(e.Message, Does.Contain("4"));      // how many fields it actually had
        Assert.That(e.Message, Does.Contain("Truncated"));
    }

    /// <summary>A non-numeric MSB likewise. This is the shape a broken quote produces -- the name swallows
    /// the following field and a number lands where text was -- so it is the error a bad edit to the table
    /// most plausibly causes.</summary>
    [Test]
    public void A_row_whose_numbers_do_not_parse_names_the_line()
    {
        var e = Assert.Throws<PresetTableFormatException>(() =>
            PresetTable.Load(Csv("\"SN-A\",\"PRST\",0001,\"Full Grand 1\",eighty-nine,64,1,\"Ac.Piano\"")));

        Assert.That(e!.Message, Does.Contain("line 2"));
    }

    /// <summary>A value <c>Integra7Preset</c> refuses -- an unknown engine -- is reported against its line
    /// too, rather than as a bare MidiException from somewhere inside the constructor.</summary>
    [Test]
    public void A_row_the_preset_constructor_rejects_names_the_line()
    {
        var e = Assert.Throws<PresetTableFormatException>(() =>
            PresetTable.Load(Csv("\"XX-Y\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\"")));

        Assert.That(e!.Message, Does.Contain("line 2"));
        Assert.That(e.InnerException, Is.Not.Null);
    }

    /// <summary>Blank lines are not rows. A file edited by hand acquires a trailing newline sooner or
    /// later, and failing on it would be failing on nothing.</summary>
    [Test]
    public void Blank_lines_are_ignored()
    {
        var presets = PresetTable.Load(Csv("\"SN-A\",\"PRST\",0001,\"Full Grand 1\",89,64,1,\"Ac.Piano\"", "", ""));

        Assert.That(presets, Has.Count.EqualTo(1));
    }

    /// <summary>The shipped table itself, end to end: it parses, it is the size the rest of the
    /// application assumes, and the two names that motivated the quote handling really are in it. Reading
    /// the real asset rather than a fixture is the point -- this is the test that would have caught the
    /// comma being written into the file before the parser could read it.</summary>
    [Test]
    public void The_shipped_table_parses()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "Presets.csv");
        using var file = File.OpenRead(path);

        var presets = PresetTable.Load(file);

        Assert.That(presets, Has.Count.EqualTo(6023));
        Assert.That(presets.Select(p => (p.Msb, p.Lsb)).Distinct().Count(), Is.EqualTo(75));
        Assert.That(presets.Any(p => p.Name == "Old,Warm OBX"), Is.True,
            "the SRX07 tone whose name carries a comma");
        Assert.That(presets.Any(p => p.Name == "1,2,3,4! SRX"), Is.True,
            "the SRX09 tone whose name carries three");
    }

    /// <summary>The curly apostrophes that are left, and why they are left.
    ///
    /// 84 rows used U+2018/U+2019 where the instrument stores ASCII 0x27. 65 of them were corrected by
    /// reading the hardware. The other 19 are in the GM2 and ExPCM banks, which expose no temporary tone in
    /// any board configuration -- verified over 20 presets at three time points with positive controls --
    /// so their names could not be read back and were left alone rather than corrected by analogy.
    ///
    /// <b>This is pinned rather than left as a comment</b> because the obvious tidy-up is a global
    /// find-and-replace over the file, and that would silently change 19 names nobody has ever compared
    /// against the instrument. If those banks ever become readable, correct them from the device and change
    /// this number; do not sweep them.</summary>
    [Test]
    public void The_only_curly_apostrophes_left_are_in_the_banks_that_could_not_be_read()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "Presets.csv");
        using var file = File.OpenRead(path);

        var curly = PresetTable.Load(file)
            .Where(p => p.Name.Contains('‘') || p.Name.Contains('’')).ToList();

        Assert.That(curly, Has.Count.EqualTo(19));
        Assert.That(curly.Select(p => p.ToneBankStr).Distinct(),
            Is.EquivalentTo(new[] { "GM2/GM2#", "ExPCM" }));
    }
}
