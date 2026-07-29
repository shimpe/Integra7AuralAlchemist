using System;
using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Both commands answer the chosen entry -- or null -- and both close the window with it, the shape
/// <see cref="ConfirmDialog"/> and <see cref="SaveToLibraryDialog"/> already use.
///
/// Double-clicking a row chooses it, because a list of things to pick one of that only responds to a button
/// reads as broken. It goes through the same command as the button rather than closing the window itself, so
/// there is one definition of what choosing is and it is the one that knows Choose is disabled with nothing
/// selected.</summary>
public partial class TonePickerDialog : ReactiveWindow<TonePickerViewModel>
{
    public TonePickerDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // A lambda rather than a method group: Close takes object? and LibraryEntry? is a reference type here,
        // so this one would in fact convert -- but the two dialogs beside this one cannot (see ConfirmDialog),
        // and three call sites spelled two ways is a difference that means nothing.
        this.WhenActivated(action => action(ViewModel!.ChooseCommand.Subscribe(result => Close(result))));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(result => Close(result))));
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Execute rather than Close: the command is disabled while nothing is selected, and a double-tap on
        // the empty space below the rows is exactly that case.
        if (ViewModel is { } vm) vm.ChooseCommand.Execute().Subscribe(_ => { });
    }
}
