using System;
using Avalonia.Controls;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Answers true for Randomise and false for Cancel, the shape <see cref="ConfirmDialog"/>
/// uses. The settings themselves stay on the view model, which the caller keeps -- so a second press
/// starts where the first left off.</summary>
public partial class RandomiseToneDialog : ReactiveWindow<RandomiseToneViewModel>
{
    public RandomiseToneDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // Lambdas rather than the method group, for the reason ConfirmDialog gives: Close takes object?
        // and a bool only reaches it by boxing, which a method-group conversion will not do.
        this.WhenActivated(action =>
            action(ViewModel!.RandomiseCommand.Subscribe(result => Close(result))));
        this.WhenActivated(action =>
            action(ViewModel!.CancelCommand.Subscribe(result => Close(result))));
    }
}
