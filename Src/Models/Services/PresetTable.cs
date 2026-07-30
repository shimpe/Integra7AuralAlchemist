using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A row of <c>Assets/Presets.csv</c> that cannot be read, named by its line.
///
/// <b>Naming the line is the whole point.</b> The table is 6,024 lines of build asset, and the person who
/// sees this is the person who just edited it. What this replaces was an
/// <see cref="IndexOutOfRangeException"/> from indexing a short split, which says nothing about which row
/// was wrong or what was wrong with it.</summary>
public sealed class PresetTableFormatException : Exception
{
    public PresetTableFormatException(string message) : base(message) { }
    public PresetTableFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Reads the shipped preset table: a stream in, the application's preset list out.
///
/// <b>Why it is here rather than in the view model.</b> It used to be a private method on
/// <c>MainWindowViewModel</c>, which needs an Avalonia application and a live device domain to construct --
/// so the one piece of code that decides what 6,023 preset names are had no test at all. Those names are
/// not decoration: they fill the preset grids, the morph tone picker, and every DAW patch list the
/// application exports, so a name read wrong here is a name wrong in the user's Reaper or Ardour session.
///
/// <b>Quoted fields are honoured, and that is not tidiness.</b> Two factory tones are called
/// <c>Old,Warm OBX</c> (SRX07) and <c>1,2,3,4! SRX</c> (SRX09) -- read back from a connected INTEGRA-7 --
/// and a parser that split on every comma could not represent them: the commas would shift MSB, LSB and PC
/// one field each and the row would fail to parse at all. The table carried substitutes (a space and
/// hyphens) for exactly as long as this parser could not read the real thing.
///
/// <b>What it deliberately does not do is normalise.</b> No trimming, no collapsing of internal spaces. 27
/// factory names carry a double space the instrument really displays -- "Kick 1  Menu", "2  0  8  0" --
/// and they are in the file because someone read them off the hardware. Tidying them here would silently
/// undo that.</summary>
public static class PresetTable
{
    /// <summary>The eight columns, in the order the file writes them.</summary>
    private const int FieldCount = 8;

    public static List<Integra7Preset> Load(Stream csv)
    {
        // Left open is wrong here: every caller opens the stream purely to hand it over, and the asset
        // loader's stream has no other owner. Disposing it is what the old code did via StreamReader.
        using var reader = new StreamReader(csv);
        return Load(reader);
    }

    public static List<Integra7Preset> Load(TextReader reader)
    {
        List<Integra7Preset> presets = [];
        // Line 1 is the header. Read and discarded rather than skipped by content: it is quoted and has
        // the same eight-field shape as a data row, so nothing about it is recognisable later.
        reader.ReadLine();

        var line = 1;
        string? text;
        while ((text = reader.ReadLine()) != null)
        {
            line++;
            // A file edited by hand acquires a trailing newline sooner or later; failing on it would be
            // failing on nothing.
            if (string.IsNullOrWhiteSpace(text)) continue;

            var fields = SplitCsvLine(text);
            if (fields.Count != FieldCount)
                throw new PresetTableFormatException(
                    $"Presets.csv line {line} has {fields.Count} fields, expected {FieldCount}: {text}");

            try
            {
                presets.Add(new Integra7Preset(presets.Count, "INT", fields[0], fields[1],
                    Int(fields[2], line, "number"), fields[3], Int(fields[4], line, "MSB"),
                    Int(fields[5], line, "LSB"), Int(fields[6], line, "PC"), fields[7]));
            }
            catch (Exception e) when (e is not PresetTableFormatException)
            {
                // Integra7Preset validates the tone type, the bank, the category and INT/USR, and throws
                // MidiException naming the value but not the row. The row is what the reader needs.
                throw new PresetTableFormatException($"Presets.csv line {line}: {e.Message}", e);
            }
        }

        return presets;
    }

    private static int Int(string value, int line, string what) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new PresetTableFormatException(
                $"Presets.csv line {line}: {what} is \"{value}\", which is not a number.");

    /// <summary>One line into its fields. A field may be wrapped in double quotes, in which case commas
    /// inside it are part of the value and a doubled quote is one literal quote (RFC 4180).
    ///
    /// Deliberately small: the file is written by this project's own tooling and every field it emits is
    /// quoted or numeric, so the cases a general CSV reader carries -- embedded newlines, quotes appearing
    /// mid-field without doubling -- are not reachable from it. What it must get right is the one case that
    /// broke the old parser, which is a comma inside quotes.</summary>
    internal static List<string> SplitCsvLine(string line)
    {
        List<string> fields = [];
        var value = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                // A doubled quote inside a quoted field is one literal quote.
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(c);
            }
        }

        fields.Add(value.ToString());
        return fields;
    }
}
