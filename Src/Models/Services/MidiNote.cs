namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure MIDI note-number helpers. Convention: note 0 = C-1, 60 = C4, 127 = G9.</summary>
public static class MidiNote
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly bool[] BlackKey = { false, true, false, true, false, false, true, false, true, false, true, false };

    public static int Clamp(int note) => note < 0 ? 0 : note > 127 ? 127 : note;

    /// <summary>Note name with octave, e.g. "C-1", "C4", "G9".</summary>
    public static string Name(int note)
    {
        note = Clamp(note);
        return $"{Names[note % 12]}{note / 12 - 1}";
    }

    /// <summary>True for accidental (black) keys: C#, D#, F#, G#, A#.</summary>
    public static bool IsBlack(int note) => BlackKey[Clamp(note) % 12];

    /// <summary>True when the note is a C (an octave boundary).</summary>
    public static bool IsC(int note) => Clamp(note) % 12 == 0;
}
