using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The playable note rail shared by the friendly tone editors: 128 note rows (note 127 at
/// top → 0 at bottom, so low notes sit at the bottom) and a click-to-audition callback.</summary>
public sealed class ToneNoteRailViewModel : ViewModelBase, IDisposable
{
    private readonly Func<int, Task>? _playNote;

    public IReadOnlyList<ToneNoteViewModel> Notes { get; }

    public ToneNoteRailViewModel(Func<int, Task>? playNote = null)
    {
        _playNote = playNote;
        var notes = new List<ToneNoteViewModel>(128);
        for (var n = 127; n >= 0; n--) notes.Add(new ToneNoteViewModel(n));
        Notes = notes;
    }

    /// <summary>Audition a note (note-on/off handled by the host callback). Best-effort.</summary>
    public void PlayNote(int note)
    {
        if (_playNote is not null) _ = _playNote(note);
    }

    public void Dispose() { }
}
