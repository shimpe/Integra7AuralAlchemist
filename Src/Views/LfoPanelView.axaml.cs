using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Integra7AuralAlchemist.Views;

public partial class LfoPanelView : UserControl
{
    public LfoPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
