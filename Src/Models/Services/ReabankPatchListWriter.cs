using System;
using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as a Reaper bank file.
///
/// <b>The format has no escaping mechanism at all</b>, which is what makes this the writer to be careful
/// in. It is lines: "Bank &lt;msb&gt; &lt;lsb&gt; &lt;name&gt;", then "&lt;program&gt; &lt;name&gt;" for
/// each patch, and a name runs to the end of its line. So a name carrying a newline does not produce a
/// broken entry -- it produces a second line whose first word Reaper reads as a program number, and every
/// patch after it is wrong in a file that still loads. Anything that could end a line is flattened to a
/// space.
///
/// <b>And nothing else is touched.</b> Quotes, ampersands, angle brackets and accented letters have no
/// meaning here, so sanitising them would be mangling a user's patch names to protect against a syntax the
/// format does not have -- and the instrument really does ship names like "W&lt;RED&gt;-Bass" and
/// "Roll &gt; Klang". The CSV writer beside this one quotes and doubles because RFC 4180 gives it somewhere
/// to put the character; here there is nowhere, so the only honest move is to change the one character that
/// would change the file's structure and leave the rest of the name as the instrument spells it.
///
/// <b>A comment is "//", not ";".</b> Checked against REAPER's own factory <c>Data/GM.reabank</c>, which
/// opens "// .reabank files define MIDI bank/program (patch) information" and contains no semicolon-led
/// line at all; Reaticulate, the most faithful third-party parser, recognises "//" and its own "//!" and
/// nothing else. The ";" convention belongs to REAPER's theme and langpack files, and this writer used it
/// for one commit because the plan's sketch of the format did. A line neither parser recognises is dropped
/// silently, so the header was harmless -- it sits before the first Bank line -- but it was not a comment.
///
/// <b>Order is the whole of the meaning.</b> A patch line says nothing about which bank it is in; it
/// belongs to whichever Bank line came before it. 68 of this instrument's 75 banks have a patch at program
/// 0, so most pairs of banks produce lines that are individually indistinguishable: there is no repairing a
/// file whose order went wrong, and nothing in it looks wrong either.</summary>
public sealed class ReabankPatchListWriter : IPatchListWriter
{
    public string Label => "Reaper (.reabank)";
    public string Extension => "reabank";

    public string Write(PatchList list)
    {
        var text = new StringBuilder();
        text.Append($"// {list.Device} patch names\n");
        text.Append("// Written by Integra-7 Aural Alchemist\n");

        foreach (var bank in list.Banks)
        {
            text.Append($"\nBank {bank.Msb} {bank.Lsb} {OneLine(bank.Name)}\n");
            foreach (var patch in bank.Patches)
                text.Append($"{patch.Program} {OneLine(patch.Name)}\n");
        }

        return text.ToString();
    }

    /// <summary>Everything that could end a line, flattened; runs of space collapsed, because two names
    /// that differed only by a tab would otherwise read as the same name with a gap in it.
    ///
    /// <b>The empty answer is replaced rather than allowed through.</b> A name that flattens away to
    /// nothing -- the instrument will hand back a user slot padded with spaces -- would leave a line that is
    /// a program number and a trailing space, which Reaper accepts and shows as a blank row the user cannot
    /// tell from the row above it or point at to say what is wrong.</summary>
    private static string OneLine(string value)
    {
        var flattened = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var collapsed = string.Join(' ', flattened.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? "(unnamed)" : collapsed;
    }
}
