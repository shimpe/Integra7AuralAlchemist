using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One patch-list format.
///
/// <b>Text in, text out, no file.</b> Writing the bytes belongs to whoever asked and can report a failure
/// to the user; what is here is the format and nothing else, which is what makes all four of them testable
/// against the same awkward list.</summary>
public interface IPatchListWriter
{
    /// <summary>What to call this format in the picker, including the extension the user will recognise.
    /// </summary>
    string Label { get; }

    /// <summary>The extension without its dot, for the save dialog and the suggested file name.</summary>
    string Extension { get; }

    /// <summary>Whether the file this writer's text goes into should start with a UTF-8 byte-order mark.
    ///
    /// <b>It is a per-format answer, which is why it is here and not in the save path.</b> One save path
    /// writing all four files would otherwise have to pick one rule for four formats that disagree: Reaper
    /// and several midnam readers take a leading BOM as part of the first token, and the symptom is a bank
    /// that simply does not appear; Excel opening a BOM-less UTF-8 .csv falls back to the system code page
    /// and mangles the 84 factory names that contain a curly apostrophe. Both failures are silent and
    /// neither is visible in the file to anything but the program that chokes on it.
    ///
    /// <b>The default is no mark</b>, because that is right for three of the four and because a format that
    /// has not thought about the question is likelier to be one whose parser is strict than one whose
    /// reader is Excel.</summary>
    bool WantsByteOrderMark => false;

    string Write(PatchList list);
}

/// <summary>Every format offered, in the order the picker shows them.
///
/// <b>Reaper first, the spreadsheet last.</b> Reaper is the format this feature was asked for, and the
/// spreadsheet is not a DAW format at all -- it is the honest fallback for the DAW nobody wrote a writer for,
/// and the only one of the four a human reads. Putting it first would offer it as the answer to a question
/// nobody asked.
///
/// <b>One list, and it is the only one.</b> The picker, the save dialog's file type and the byte-order-mark
/// decision all read a writer out of here, so adding a fifth format is adding a line to this list and nothing
/// else -- as opposed to a list in the dialog and a switch in the command, which is the arrangement where a
/// new format shows up in the picker and writes the previous one's bytes.
///
/// The instances are stateless and shared: a writer is a pure function over a <see cref="PatchList"/>, so
/// there is nothing for two exports to tread on.</summary>
public static class PatchListWriters
{
    public static IReadOnlyList<IPatchListWriter> All { get; } =
    [
        new ReabankPatchListWriter(), new CubasePatchListWriter(),
        new MidnamPatchListWriter(), new CsvPatchListWriter(),
    ];
}
