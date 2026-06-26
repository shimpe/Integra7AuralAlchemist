using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class SNAcousticToneEditorView : UserControl
{
    public SNAcousticToneEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
