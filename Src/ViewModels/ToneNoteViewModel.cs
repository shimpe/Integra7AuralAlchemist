using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row in a tone editor's playable note rail: a MIDI note and its name/key styling.
/// Immutable — no per-row state changes, so no INotifyPropertyChanged needed.</summary>
public sealed class ToneNoteViewModel
{
    public int Note { get; }   // MIDI note 0..127

    public ToneNoteViewModel(int note) => Note = note;

    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    /// <summary>True for the 88-key piano range (A0..C8); out-of-range notes are shown de-emphasized.</summary>
    public bool InPianoRange => Note is >= 21 and <= 108;
}
