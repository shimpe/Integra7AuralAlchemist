using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Windowing;
using Integra7AuralAlchemist.Models.Services;
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
            action(ViewModel!.ShowSaveToLibraryDialog.RegisterHandler(DoShowSaveToLibraryDialogAsync));
            action(ViewModel!.ShowPickLibraryFolderDialog.RegisterHandler(DoShowPickLibraryFolderDialogAsync));
            action(ViewModel!.ShowConfirmDialog.RegisterHandler(DoShowConfirmDialogAsync));
            action(ViewModel!.ShowRandomiseToneDialog.RegisterHandler(DoShowRandomiseToneDialogAsync));
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
            // Deliberately not "Save Studio Set Snapshot": both the Studio Set and the tone commands
            // share this picker, and the suggested file name already says which one is being saved.
            Title = "Save Snapshot",
            SuggestedFileName = interaction.Input,
            DefaultExtension = "json",
            FileTypeChoices = [SnapshotFileType]
        });

        // null only for an actual cancellation. A picked file with no local path (cloud/virtual
        // storage) is reported as "" rather than collapsed into null, so the command does not mistake
        // "picked but unusable" for "cancelled" -- see ShowSaveSnapshotDialog's doc comment.
        interaction.SetOutput(file is null ? null : file.TryGetLocalPath() ?? "");
    }

    /// <summary>Ask what a snapshot about to be saved into the library should be called and what should be said
    /// about it. Modelled on <see cref="DoShowDialogAsync"/> down to the shape of its result: the dialog's own
    /// commands answer the metadata or null, and null is what the caller reads as a cancellation.</summary>
    private async Task DoShowSaveToLibraryDialogAsync(
        IInteractionContext<SaveToLibraryViewModel, SnapshotMetadata?> interaction)
    {
        var dialog = new SaveToLibraryDialog { DataContext = interaction.Input };
        interaction.SetOutput(await dialog.ShowDialog<SnapshotMetadata?>(this));
    }

    /// <summary>A yes/no question. The window closes with the answer, and a window closed any other way
    /// -- the title bar's X, Escape -- answers false, which is the safe side for every caller: all of
    /// them are about to replace something.</summary>
    private async Task DoShowConfirmDialogAsync(IInteractionContext<ConfirmViewModel, bool> interaction)
    {
        var dialog = new ConfirmDialog { DataContext = interaction.Input };
        interaction.SetOutput(await dialog.ShowDialog<bool>(this));
    }

    private async Task DoShowRandomiseToneDialogAsync(
        IInteractionContext<RandomiseToneViewModel, bool> interaction)
    {
        var dialog = new RandomiseToneDialog { DataContext = interaction.Input };
        interaction.SetOutput(await dialog.ShowDialog<bool>(this));
    }

    private async Task DoShowPickLibraryFolderDialogAsync(IInteractionContext<string, string?> interaction)
    {
        // Start where the library already is, when that is somewhere the picker can be pointed at. Only if the
        // folder exists: TryGetFolderFromPathAsync over a path that is not there answers null, which is the same
        // as not asking, and skipping the call keeps this from depending on that being true of every backend.
        IStorageFolder? start = null;
        if (Directory.Exists(interaction.Input))
            start = await StorageProvider.TryGetFolderFromPathAsync(interaction.Input);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the snapshot library folder",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        // Same null-vs-"" distinction as the snapshot pickers: null is a cancellation, "" is a folder that was
        // chosen but has no local path this application can read or write (a cloud or virtual location).
        interaction.SetOutput(folders.Count == 0 ? null : folders[0].TryGetLocalPath() ?? "");
    }

    private async Task DoShowOpenSnapshotDialogAsync(IInteractionContext<Unit, string?> interaction)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            // Shared by the Studio Set and tone commands; each refuses a file of the wrong kind with a
            // message naming the button that would have worked.
            Title = "Open Snapshot",
            AllowMultiple = false,
            FileTypeFilter = [SnapshotFileType]
        });

        // Same null-vs-"" distinction as DoShowSaveSnapshotDialogAsync.
        interaction.SetOutput(files.Count == 0 ? null : files[0].TryGetLocalPath() ?? "");
    }
}