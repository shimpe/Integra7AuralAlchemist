using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class ToneNoteRailView : UserControl
{
    private int? _activeNote;

    public ToneNoteRailView()
    {
        InitializeComponent();
    }

    // Press-and-hold: pointer-down sounds the note (and captures the pointer so the release reaches us
    // even if the cursor leaves the row); pointer-up / capture-lost stops it. The capture guard prevents
    // a stuck note if the window loses focus mid-press.
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ToneNoteViewModel note } row &&
            DataContext is ToneNoteRailViewModel vm)
        {
            _activeNote = note.Note;
            vm.NoteDown(note.Note);
            e.Pointer.Capture(row);
            e.Handled = true;
        }
    }

    private void NoteRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        StopActiveNote();
        e.Pointer.Capture(null);
    }

    private void NoteRow_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => StopActiveNote();

    private void StopActiveNote()
    {
        if (_activeNote is int n && DataContext is ToneNoteRailViewModel vm)
        {
            vm.NoteUp(n);
            _activeNote = null;
        }
    }
}
