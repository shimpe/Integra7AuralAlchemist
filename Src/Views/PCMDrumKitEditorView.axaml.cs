using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class PCMDrumKitEditorView : UserControl
{
    public PCMDrumKitEditorView()
    {
        InitializeComponent();
    }

    // Clicking a rail row auditions that drum note (in addition to selecting it). How far along the row
    // the click landed sets the velocity — left edge is soft, right edge is hard.
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PCMDrumNoteViewModel note } row &&
            DataContext is PCMDrumKitEditorViewModel vm)
            vm.PlayNote(note.Note, VelocityMapping.FromPointerX(e.GetPosition(row).X, row.Bounds.Width));
    }
}
