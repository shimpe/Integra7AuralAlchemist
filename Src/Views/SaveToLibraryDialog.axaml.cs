using System;
using Avalonia.Controls;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Both commands answer a result and both close the window with it -- the metadata for Save, null for
/// Cancel -- which is the shape <see cref="SaveUserToneDialog"/> established and what lets the caller read null as
/// "cancelled" without a second flag to keep in step. The design-mode guard is there for the same reason it is
/// there: the previewer has no ViewModel to activate against.</summary>
public partial class SaveToLibraryDialog : ReactiveWindow<SaveToLibraryViewModel>
{
    public SaveToLibraryDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.WhenActivated(action => action(ViewModel!.SaveCommand.Subscribe(Close)));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(Close)));
    }
}
