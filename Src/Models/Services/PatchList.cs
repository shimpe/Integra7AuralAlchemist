using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One patch as a DAW addresses it: two control changes and a program change, with a name to put
/// in a dropdown.
///
/// <b>Program is the number that goes on the wire</b>, 0 to 127 -- not the 1 to 128 the instrument's own
/// tone list is printed with. The conversion happens once, in <see cref="PatchListSource"/>, because four
/// writers each subtracting one is three chances to forget.</summary>
/// <param name="Engine">The engine code, kept because a patch list is also read by a human deciding which
/// of two similarly named sounds they want.</param>
/// <param name="UserMemory">Whether this came from the instrument's user memory rather than the factory
/// data. What makes a bank's name honest -- see <see cref="PatchListSource"/>.</param>
public sealed record PatchEntry(int Program, string Name, string Engine, string Category, bool UserMemory);

/// <summary>Every patch reachable at one bank-select address.</summary>
public sealed record PatchBank(int Msb, int Lsb, string Name, IReadOnlyList<PatchEntry> Patches);

/// <summary>A whole instrument's worth of patches, and what could not be represented faithfully.
///
/// <b>The two lists of prose are part of the answer, not diagnostics.</b> A patch list that silently
/// dropped a patch would look exactly like a correct one, and the user would find out when a track played
/// the wrong sound. So what was left out and what shares an address are carried back to whoever asked, to
/// be said out loud.</summary>
/// <param name="Device">What the file calls the instrument.</param>
/// <param name="Collisions">Addresses carrying more than one patch, in words. The instrument's own data
/// has one: MSB 121 / LSB 0 / PC 116 is both Woodblock and Castanets.</param>
/// <param name="Skipped">Patches left out because their program cannot go on the wire.</param>
public sealed record PatchList(
    string Device,
    IReadOnlyList<PatchBank> Banks,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Skipped);
