using System;
using Avalonia.Controls;
using ReactiveUI.Avalonia;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;

namespace Integra7AuralAlchemist.Views;

public partial class SaveUserToneDialog : ReactiveWindow<SaveUserToneViewModel>
{
    public SaveUserToneDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.WhenActivated(action =>
            action(ViewModel!.CancelCommand.Subscribe(Close)));
        this.WhenActivated(action =>
            action(ViewModel!.SaveCommand.Subscribe(Close)));
    }
}