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

    string Write(PatchList list);
}
