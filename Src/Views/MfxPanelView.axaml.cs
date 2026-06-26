using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class MfxPanelView : UserControl
{
    public MfxPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
