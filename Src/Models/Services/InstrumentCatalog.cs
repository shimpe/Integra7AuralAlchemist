using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Groups the SN-Acoustic INSTRUMENT_VARIATIONS into musical families for a two-step picker.
/// Indices are positions in the Instrument param's option list (= INSTRUMENT_VARIATIONS order).
/// The list (ParameterDefinitions.cs) holds 127 instruments: INT 001..077 grouped by family,
/// followed by the ExSN1..ExSN5 expansion entries, all collected under "Expansion".
/// </summary>
public static class InstrumentCatalog
{
    public sealed record Family(string Name, int[] Indices);

    /// <summary>Families in display order; the union of Indices covers 0..Count-1 exactly once.</summary>
    public static IReadOnlyList<Family> Families { get; } = new List<Family>
    {
        new("Pianos",               Enumerable.Range(0, 9).ToArray()),    // 0..8   INT 001..009
        new("E.Pianos",             Enumerable.Range(9, 6).ToArray()),    // 9..14  INT 010..015
        new("Clav",                 Enumerable.Range(15, 8).ToArray()),   // 15..22 INT 016..023
        new("Mallets & Bells",      Enumerable.Range(23, 5).ToArray()),   // 23..27 INT 024..028
        new("Organ",                Enumerable.Range(28, 1).ToArray()),   // 28     INT 029
        new("Reeds & Accordion",    Enumerable.Range(29, 4).ToArray()),   // 29..32 INT 030..033
        new("Guitars",              Enumerable.Range(33, 7).ToArray()),   // 33..39 INT 034..040
        new("Basses",               Enumerable.Range(40, 4).ToArray()),   // 40..43 INT 041..044
        new("Strings & Orchestral", Enumerable.Range(44, 10).ToArray()),  // 44..53 INT 045..054
        new("Choir",                Enumerable.Range(54, 2).ToArray()),   // 54..55 INT 055..056
        new("Brass",                Enumerable.Range(56, 5).ToArray()),   // 56..60 INT 057..061
        new("Sax",                  Enumerable.Range(61, 4).ToArray()),   // 61..64 INT 062..065
        new("Woodwind",             Enumerable.Range(65, 7).ToArray()),   // 65..71 INT 066..072
        new("Ethnic",               Enumerable.Range(72, 5).ToArray()),   // 72..76 INT 073..077
        new("Expansion",            Enumerable.Range(77, 50).ToArray()),  // 77..126 ExSN1..ExSN5
    };

    /// <summary>Total instrument count = sum of family index counts.</summary>
    public static int Count => Families.Sum(f => f.Indices.Length);

    /// <summary>Family name containing the given instrument index, or "" if out of range.</summary>
    public static string FamilyOf(int index)
        => Families.FirstOrDefault(f => f.Indices.Contains(index))?.Name ?? "";

    /// <summary>Instrument indices in a family (empty if the family name is unknown).</summary>
    public static IReadOnlyList<int> ValuesIn(string family)
        => Families.FirstOrDefault(f => f.Name == family)?.Indices ?? Array.Empty<int>();
}
