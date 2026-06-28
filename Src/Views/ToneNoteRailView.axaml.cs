using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class ToneNoteRailView : UserControl
{
    public ToneNoteRailView()
    {
        InitializeComponent();
    }

    // Clicking a row auditions that note.
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ToneNoteViewModel note } &&
            DataContext is ToneNoteRailViewModel vm)
            vm.PlayNote(note.Note);
    }
}
