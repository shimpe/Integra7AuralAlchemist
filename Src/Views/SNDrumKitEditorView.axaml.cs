using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class SNDrumKitEditorView : UserControl
{
    public SNDrumKitEditorView()
    {
        InitializeComponent();
    }

    // Clicking a rail row auditions that drum note (in addition to selecting it).
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SNDrumNoteViewModel note } &&
            DataContext is SNDrumKitEditorViewModel vm)
            vm.PlayNote(note.Note);
    }
}
