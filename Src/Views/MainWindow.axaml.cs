using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions in this namespace; without
// it the call below does not compile.
using Avalonia.Input.Platform;
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

    /// <summary>What the Morph tab's two pickers filter by. The same <c>*.json</c>, under a name that
    /// covers a tone and a morph pad both: "Studio Set snapshot" over a pad file would simply be
    /// wrong.</summary>
    private static readonly FilePickerFileType JsonFileType =
        new("Snapshot or morph pad") { Patterns = ["*.json"] };

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
            action(ViewModel!.ShowSaveTextDialog.RegisterHandler(DoShowSaveTextDialogAsync));
            action(ViewModel!.ShowCopyToClipboard.RegisterHandler(DoCopyToClipboardAsync));
            action(ViewModel!.ShowOpenJsonDialog.RegisterHandler(DoShowOpenJsonDialogAsync));
            action(ViewModel!.ShowSaveJsonDialog.RegisterHandler(DoShowSaveJsonDialogAsync));
        });
    }

    /// <summary>Where a picker should open, or null when it should open wherever it last was. Only asked
    /// for a folder that exists: <c>TryGetFolderFromPathAsync</c> over a path that is not there answers
    /// null, which is the same as not asking, and skipping the call keeps this from depending on that
    /// being true of every backend. See <see cref="DoShowPickLibraryFolderDialogAsync"/>, which does the
    /// same for the same reason.</summary>
    private async Task<IStorageFolder?> StartAtAsync(string folder) =>
        Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;

    /// <summary>Open a JSON file, pointed somewhere and titled by the caller. What the Morph tab reaches
    /// for a corner's tone and for a saved pad; the title is the only thing telling the user which.</summary>
    private async Task DoShowOpenJsonDialogAsync(IInteractionContext<FilePickerRequest, string?> interaction)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = interaction.Input.Title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartAtAsync(interaction.Input.Folder),
            FileTypeFilter = [JsonFileType]
        });

        // Same null-vs-"" distinction as DoShowSaveSnapshotDialogAsync.
        interaction.SetOutput(files.Count == 0 ? null : files[0].TryGetLocalPath() ?? "");
    }

    private async Task DoShowSaveJsonDialogAsync(IInteractionContext<FilePickerRequest, string?> interaction)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = interaction.Input.Title,
            SuggestedFileName = interaction.Input.SuggestedName,
            SuggestedStartLocation = await StartAtAsync(interaction.Input.Folder),
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType]
        });

        // Same null-vs-"" distinction as DoShowSaveSnapshotDialogAsync.
        interaction.SetOutput(file is null ? null : file.TryGetLocalPath() ?? "");
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

    private async Task DoShowSaveTextDialogAsync(IInteractionContext<string, string?> interaction)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Comparison",
            SuggestedFileName = interaction.Input,
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
        });

        // "" for a picked file with no local path, null only for a cancellation -- see
        // DoShowSaveSnapshotDialogAsync, which answers the same three ways for the same reason.
        interaction.SetOutput(file is null ? null : file.TryGetLocalPath() ?? "");
    }

    /// <summary>The clipboard belongs to the top level, not to the view model, which is why this is an
    /// interaction. A null clipboard is possible on a platform that has none; saying so is better than a
    /// silent no-op.</summary>
    private async Task DoCopyToClipboardAsync(IInteractionContext<string, Unit> interaction)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) throw new InvalidOperationException("This platform has no clipboard.");

        await clipboard.SetTextAsync(interaction.Input);
        interaction.SetOutput(Unit.Default);
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