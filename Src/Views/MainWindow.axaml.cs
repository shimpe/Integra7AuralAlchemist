using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using FluentAvalonia.UI.Windowing;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;

namespace Integra7AuralAlchemist.Views;

public partial class MainWindow : FAAppWindow, IViewFor<MainWindowViewModel>
{
    private MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        // FluentAvalonia 3.0 removed TitleBarHitTestType, and its built-in title-bar
        // hit-testing doesn't make the extended content area draggable here, so we
        // move the window ourselves from the title strip via BeginMoveDrag (see
        // TitleBarDragStrip_PointerPressed). Resize is still handled by the window edges.
    }

    private void TitleBarDragStrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            // Double-click toggles maximize/restore, like a normal title bar.
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    public MainWindowViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
        }
    }

    object IViewFor.ViewModel
    {
        get => ViewModel;
        set => ViewModel = (MainWindowViewModel)value;
    }

    public void RegisterDialogHandler()
    {
        this.WhenActivated(action =>
            action(ViewModel!.ShowSaveUserToneDialog.RegisterHandler(DoShowDialogAsync)));
    }

    private async Task DoShowDialogAsync(IInteractionContext<SaveUserToneViewModel,
        UserToneToSave?> interaction)
    {
        var dialog = new SaveUserToneDialog();
        dialog.DataContext = interaction.Input;

        var result = await dialog.ShowDialog<UserToneToSave?>(this);
        interaction.SetOutput(result);
    }
}