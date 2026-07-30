using System.Linq;
using System.Text;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as a spreadsheet.
///
/// <b>Not a DAW format, and that is the point.</b> It is the honest fallback for the DAW nobody wrote a
/// writer for, and it is the only one of the four a user can read, sort and check by eye. A librarian
/// looking for "which bank is that pad in" is better served by this than by any of the others.
///
/// <b>RFC 4180 quoting</b>: a field carrying a comma, a quote or a newline is wrapped in quotes, and a
/// quote inside it is doubled. Excel and LibreOffice both read that; anything else is one spreadsheet's
/// habit.</summary>
public sealed class CsvPatchListWriter : IPatchListWriter
{
    /// <summary>CRLF, and deliberately, even though nothing else this application writes uses it: RFC 4180
    /// says CRLF, and a spreadsheet on Windows opening a LF-only file is the one place this would be
    /// noticed.
    ///
    /// <b>It is not what keeps a name containing a newline from becoming a second row.</b> Nothing about
    /// the row separator does that: a name carrying a CRLF of its own would end the line just as neatly.
    /// What protects the file is the quoting in <see cref="Field"/> -- a newline inside quotes is legal
    /// CSV, and Excel and LibreOffice both honour it -- which is exactly why RFC 4180 allows the character
    /// in a field at all. Worth saying plainly, because the next format in this feature has <i>no</i>
    /// escaping to fall back on and has to flatten newlines instead (see <c>ReabankPatchListWriter</c>).
    /// Whoever writes that one should not arrive believing a stray newline is somehow self-solving.
    /// </summary>
    private const string RowEnd = "\r\n";

    public string Label => "Spreadsheet (.csv)";
    public string Extension => "csv";

    /// <summary>The one format here that wants a byte-order mark, and the reason the flag exists.
    ///
    /// 84 factory tone names carry a curly apostrophe -- "60’s LeadORG", "‘76 Pure", "‘73 Tine" -- three
    /// bytes of UTF-8 apiece. Excel opening a BOM-less .csv by double-click does not sniff the encoding: it
    /// falls back to the system code page, each of those bytes becomes a character of its own, and the user
    /// is shown two pieces of line noise where the apostrophe was. Every version before the recent 365
    /// builds does this. The file itself is not corrupt, which is worse rather than better -- it looks
    /// innocent to everything except the one program the user opened it with.
    ///
    /// The other three formats are read by parsers that either declare their encoding in the document or
    /// assume UTF-8, and at least two of them take a leading BOM as part of the first token. So this stays
    /// a per-format answer rather than one decision the save path makes for all four.</summary>
    public bool WantsByteOrderMark => true;

    public string Write(PatchList list)
    {
        var text = new StringBuilder();
        text.Append("MSB,LSB,Program,Bank,Name,Engine,Category,User").Append(RowEnd);

        foreach (var bank in list.Banks)
        foreach (var patch in bank.Patches)
            text.Append(string.Join(',',
                    bank.Msb, bank.Lsb, patch.Program, Field(bank.Name), Field(patch.Name),
                    Field(patch.Engine), Field(patch.Category), patch.UserMemory ? "yes" : ""))
                .Append(RowEnd);

        return text.ToString();
    }

    /// <summary>Quoted only when it has to be, because a file where every field is quoted is harder for the
    /// human this format exists for to read, and the two spell the same thing to a parser.</summary>
    private static string Field(string value) =>
        value.Any(c => c is ',' or '"' or '\r' or '\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
