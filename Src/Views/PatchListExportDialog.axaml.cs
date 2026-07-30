using System;
using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Both commands answer a writer -- or null -- and both close the window with it, the shape
/// <see cref="ConfirmDialog"/> and <see cref="TonePickerDialog"/> already use. A window dismissed any other
/// way (the title bar's X, Escape) answers null, which is the same as Cancel and is the safe side: nothing
/// has been written at the point this window is up.
///
/// Double-tapping a format chooses it, for the reason <see cref="TonePickerDialog"/> gives -- a list of
/// things to pick one of that only responds to a button reads as broken.</summary>
public partial class PatchListExportDialog : ReactiveWindow<PatchListExportViewModel>
{
    public PatchListExportDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // A lambda rather than a method group, spelled as the two dialogs beside this one spell it -- see
        // ConfirmDialog, where the conversion genuinely does not exist and three call sites written two ways
        // would be a difference that means nothing.
        this.WhenActivated(action => action(ViewModel!.ExportCommand.Subscribe(result => Close(result))));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(result => Close(result))));
    }

    private void OnFormatDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Execute rather than Close: the command is disabled while nothing is selected, and a double-tap on
        // the empty space below the rows is exactly that case.
        if (ViewModel is { } vm) vm.ExportCommand.Execute().Subscribe(_ => { });
    }
}
