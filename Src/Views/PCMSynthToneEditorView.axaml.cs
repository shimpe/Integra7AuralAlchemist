using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Integra7AuralAlchemist.Views;

public partial class PCMSynthToneEditorView : UserControl
{
    public PCMSynthToneEditorView()
    {
        InitializeComponent();
    }

    // The wave fields are searchable AutoCompleteBoxes; the "browse" arrow next to each opens the
    // full list. Clearing Text first drops any active filter so every wave is shown; ParamString
    // ignores a null/empty assignment, so the transient empty field never writes a bad value.
    private void BrowseWaveL(object? sender, RoutedEventArgs e) => OpenForBrowse(WaveLBox);
    private void BrowseWaveR(object? sender, RoutedEventArgs e) => OpenForBrowse(WaveRBox);

    private static void OpenForBrowse(AutoCompleteBox box)
    {
        box.Text = string.Empty;
        box.Focus();
        box.IsDropDownOpen = true;
    }
}
