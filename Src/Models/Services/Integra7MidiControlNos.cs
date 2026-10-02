namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Channel message status bytes (channel 0; add the channel) and the controller numbers the
/// application sends.</summary>
public class Integra7MidiControlNos
{
    public const byte NoteOff = 0x80;
    public const byte NoteOn = 0x90;
    public const byte ControlChange = 0xB0;
    public const byte ProgramChange = 0xC0;
    public const byte AllNotesOff = 123;
}
