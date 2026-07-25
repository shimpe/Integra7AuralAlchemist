using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Windowing;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;

namespace Integra7AuralAlchemist.Views;

public partial class MainWindow : FAAppWindow, IViewFor<MainWindowViewModel>
{
    private static readonly FilePickerFileType SnapshotFileType =
        new("Studio Set snapshot") { Patterns = ["*.json"] };

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
        {
            action(ViewModel!.ShowSaveUserToneDialog.RegisterHandler(DoShowDialogAsync));
            action(ViewModel!.ShowSaveSnapshotDialog.RegisterHandler(DoShowSaveSnapshotDialogAsync));
            action(ViewModel!.ShowOpenSnapshotDialog.RegisterHandler(DoShowOpenSnapshotDialogAsync));
        });
    }

    private async Task DoShowDialogAsync(IInteractionContext<SaveUserToneViewModel,
        UserToneToSave?> interaction)
    {
        var dialog = new SaveUserToneDialog();
        dialog.DataContext = interaction.Input;

        var result = await dialog.ShowDialog<UserToneToSave?>(this);
        interaction.SetOutput(result);
    }

    private async Task DoShowSaveSnapshotDialogAsync(IInteractionContext<string, string?> interaction)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Studio Set Snapshot",
            SuggestedFileName = interaction.Input,
            DefaultExtension = "json",
            FileTypeChoices = [SnapshotFileType]
        });

        // null only for an actual cancellation. A picked file with no local path (cloud/virtual
        // storage) is reported as "" rather than collapsed into null, so the command does not mistake
        // "picked but unusable" for "cancelled" -- see ShowSaveSnapshotDialog's doc comment.
        interaction.SetOutput(file is null ? null : file.TryGetLocalPath() ?? "");
    }

    private async Task DoShowOpenSnapshotDialogAsync(IInteractionContext<Unit, string?> interaction)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Studio Set Snapshot",
            AllowMultiple = false,
            FileTypeFilter = [SnapshotFileType]
        });

        // Same null-vs-"" distinction as DoShowSaveSnapshotDialogAsync.
        interaction.SetOutput(files.Count == 0 ? null : files[0].TryGetLocalPath() ?? "");
    }
}