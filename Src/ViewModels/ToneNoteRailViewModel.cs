using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The playable note rail shared by the friendly tone editors: 128 note rows (note 127 at
/// top → 0 at bottom, so low notes sit at the bottom). Press-and-hold: <see cref="NoteDown"/> on
/// pointer-down, <see cref="NoteUp"/> on pointer-up, so notes sustain for as long as the button is held.</summary>
public sealed class ToneNoteRailViewModel : ViewModelBase, IDisposable
{
    private readonly Func<int, Task>? _noteOn;
    private readonly Func<int, Task>? _noteOff;

    public IReadOnlyList<ToneNoteViewModel> Notes { get; }

    public ToneNoteRailViewModel(Func<int, Task>? noteOn = null, Func<int, Task>? noteOff = null)
    {
        _noteOn = noteOn;
        _noteOff = noteOff;
        var notes = new List<ToneNoteViewModel>(128);
        for (var n = 127; n >= 0; n--) notes.Add(new ToneNoteViewModel(n));
        Notes = notes;
    }

    /// <summary>Start sounding a note (pointer-down). Best-effort.</summary>
    public void NoteDown(int note)
    {
        if (_noteOn is not null) _ = _noteOn(note);
    }

    /// <summary>Stop sounding a note (pointer-up / capture lost). Best-effort.</summary>
    public void NoteUp(int note)
    {
        if (_noteOff is not null) _ = _noteOff(note);
    }

    public void Dispose() { }
}
