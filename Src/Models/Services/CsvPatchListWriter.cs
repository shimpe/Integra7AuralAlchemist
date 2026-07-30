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
    /// It is also what keeps a name carrying a newline from becoming a second row: that newline is a bare
    /// LF, so a reader splitting on CRLF -- which is every reader of this format -- still counts one row
    /// per patch.</summary>
    private const string RowEnd = "\r\n";

    public string Label => "Spreadsheet (.csv)";
    public string Extension => "csv";

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
