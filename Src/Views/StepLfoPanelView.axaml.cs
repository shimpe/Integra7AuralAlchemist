using Avalonia.Controls;
using Avalonia.Input;

namespace Integra7AuralAlchemist.Views;

public partial class StepLfoPanelView : UserControl
{
    public StepLfoPanelView()
    {
        InitializeComponent();
    }

    /// <summary>Enter in one of the sixteen value boxes commits what was typed.
    ///
    /// The boxes write on losing focus, not on every keystroke -- typing "-12" would otherwise send "-1"
    /// to the instrument on the way past. That leaves Enter meaning nothing, which is not what anyone
    /// expects of a number they have just typed, so Enter moves focus off the box and the commit falls out
    /// of the same rule rather than needing a second path that could disagree with it.
    ///
    /// Handled on the grid rather than per box: the event bubbles, and sixteen identical subscriptions
    /// would be sixteen chances for one of them to be missed.</summary>
    private void OnStepBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control grid) return;

        grid.Focus();
        e.Handled = true;
    }
}
