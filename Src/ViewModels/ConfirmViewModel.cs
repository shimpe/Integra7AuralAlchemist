using System.Reactive;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>A question with two answers. The application's first yes/no dialog, built as one reusable
/// window rather than one per command: Init and Paste both need exactly this, and a third caller will
/// too.
///
/// Both commands answer a bool and both close the window with it, the shape
/// <c>SaveToLibraryViewModel</c> established -- which is what lets the caller read the result without a
/// second flag to keep in step.</summary>
public sealed class ConfirmViewModel : ViewModelBase
{
    public ConfirmViewModel(string message, string confirmLabel = "Continue")
    {
        Message = message;
        ConfirmLabel = confirmLabel;

        // Parameterless, for the reason SaveToLibraryViewModel gives: a ReactiveCommand<Unit, T> invoked
        // from a button with no CommandParameter is handed null, and casting null to Unit throws.
        ConfirmCommand = ReactiveCommand.Create(() => true);
        CancelCommand = ReactiveCommand.Create(() => false);
    }

    public string Message { get; }

    /// <summary>What the affirmative button says. "Continue" for a replacement the user asked for;
    /// a caller with something more specific to say passes it.</summary>
    public string ConfirmLabel { get; }

    public ReactiveCommand<Unit, bool> ConfirmCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
}
