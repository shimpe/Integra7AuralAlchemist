using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>What the Save User Tone dialog came back with: which user slot to overwrite, and the name
/// to give it.
///
/// <see cref="Preset" /> is the slot the user picked, carried along so the caller can rename that exact
/// object once the write succeeds instead of counting its way back to it. <see cref="ZeroBasedMemoryId" />
/// is the hardware slot number of that preset, computed by <see cref="Models.Services.UserToneSlots" />
/// over the full preset list -- never a row index from the dialog's filtered grid.</summary>
public class UserToneToSave(string newName, int zeroBasedMemoryId, Integra7Preset preset)
{
    public int ZeroBasedMemoryId => zeroBasedMemoryId;
    public string NewName => newName;
    public Integra7Preset Preset => preset;
}
