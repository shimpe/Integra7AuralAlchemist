using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Integra7AuralAlchemist.Views;

public partial class SNDrumNoteEditorView : UserControl
{
    public SNDrumNoteEditorView()
    {
        InitializeComponent();
    }

    // The drum-sound field is searchable; the browse arrow clears the filter and opens the full list.
    private void BrowseInst(object? sender, RoutedEventArgs e)
    {
        InstBox.Text = string.Empty;
        InstBox.Focus();
        InstBox.IsDropDownOpen = true;
    }
}
