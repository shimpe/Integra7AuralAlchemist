using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class ToneNoteRailView : UserControl
{
    /// <summary>Middle C. The rail spans all 128 notes, so without this it would open on note 127 —
    /// far above anything playable.</summary>
    private const int MiddleC = 60;

    private int? _activeNote;
    private bool _centered;

    public ToneNoteRailView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Only the initial view is centred; re-attaching (switching tabs) must not throw away
        // wherever the player had scrolled to.
        if (!_centered) Dispatcher.UIThread.Post(CenterOnMiddleC, DispatcherPriority.Loaded);
    }

    private void CenterOnMiddleC()
    {
        if (DataContext is not ToneNoteRailViewModel vm || NoteList.Scroll is not { } scroll) return;

        var count = vm.Notes.Count;
        // Deferred to DispatcherPriority.Loaded, but if the rail still has not been measured there is
        // nothing to scroll within; leave _centered false so the next attach tries again.
        if (count == 0 || scroll.Extent.Height <= 0) return;

        var index = -1;
        for (var i = 0; i < count; i++)
            if (vm.Notes[i].Note == MiddleC)
            {
                index = i;
                break;
            }

        if (index < 0) return;

        var y = RailScrollMapping.CenterOffset(index, count, scroll.Extent.Height, scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, y);
        _centered = true;
    }

    // Press-and-hold: pointer-down sounds the note (and captures the pointer so the release reaches us
    // even if the cursor leaves the row); pointer-up / capture-lost stops it. The capture guard prevents
    // a stuck note if the window loses focus mid-press. How far along the row the press landed sets the
    // velocity — left edge is soft, right edge is hard.
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ToneNoteViewModel note } row &&
            DataContext is ToneNoteRailViewModel vm)
        {
            _activeNote = note.Note;
            vm.NoteDown(note.Note, VelocityMapping.FromPointerX(e.GetPosition(row).X, row.Bounds.Width));
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
