using System;
using Avalonia.Controls;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Both commands answer a bool and both close the window with it, exactly as
/// <see cref="SaveToLibraryDialog"/> does with its metadata. Cancel is the default button because
/// everything that asks this question is about to overwrite something.</summary>
public partial class ConfirmDialog : ReactiveWindow<ConfirmViewModel>
{
    public ConfirmDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // A lambda rather than the method group SaveToLibraryDialog passes: Close takes object?, and a
        // method group only converts to Action<bool> if bool converts to object? by reference -- boxing
        // does not count, so `Subscribe(Close)` is a compile error for a command answering a struct.
        this.WhenActivated(action => action(ViewModel!.ConfirmCommand.Subscribe(result => Close(result))));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(result => Close(result))));
    }
}
