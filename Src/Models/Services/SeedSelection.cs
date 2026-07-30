using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What to sweep.
///
/// <b>Engines and banks are sets rather than flags</b>, because the screen is a list of tick boxes over the
/// vocabulary the preset table actually uses and a bool per engine would have to be extended by hand every
/// time the table gains one. Empty means "none selected", not "all": a sweep is an hour of the user's
/// instrument and it starts from nothing ticked being nothing swept.</summary>
/// <param name="Engines">Tone types to include -- "SN-A", "SN-S", "PCMS", "PCMD", "SN-D".</param>
/// <param name="Banks">Bank strings as the table spells them -- "PRST", "SRX07", "ExSN1", "GM2/GM2#".</param>
/// <param name="IncludeInternal">Factory presets.</param>
/// <param name="IncludeUser">The instrument's user slots. They report their bank as "PRST" like the factory
/// ones, so this is the only thing that separates the two sides.</param>
/// <param name="ZeroBasedPartNo">The part the sweep borrows. Its tone is overwritten once per patch and the
/// Studio Set is restored at the end, so which part it is matters only to what the user hears while it
/// runs.</param>
public sealed record SeedSelection(
    IReadOnlyCollection<string> Engines,
    IReadOnlyCollection<string> Banks,
    bool IncludeInternal = true,
    bool IncludeUser = true,
    int ZeroBasedPartNo = 0);
