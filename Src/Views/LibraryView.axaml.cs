using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Enter in the search box looks inside the patches.
    ///
    /// <b>Code-behind rather than a KeyBinding on the box.</b> Nothing in this application uses KeyBinding,
    /// and the comment at the top of MainWindow.axaml records why the one place that wanted one did not get
    /// it. A KeyBinding is also not part of the visual tree, so whether its Command binding sees this view's
    /// DataContext is precisely the kind of thing that fails at runtime with the build still green -- and on
    /// this branch the build is the only check a binding gets. A handler named in the XAML is checked by the
    /// compiler on both sides.
    ///
    /// <b>Nothing is awaited</b>, because a key handler cannot: the search reports its own outcome on the
    /// status bar and logs its own failures, so there is no answer for this to wait for. That is what the
    /// button beside the box does too, through its command.
    ///
    /// The key is marked handled either way: a search box is single-line, so Enter has nothing else to
    /// mean, and leaving it unhandled would let it reach whatever default button a future dialog puts
    /// around this view. Nothing is tested here first -- whether the box is ticked, whether there is
    /// anything to look for -- because the view model answers every one of those cases on the status bar,
    /// which is what stops a handled key from being a dead one.</summary>
    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LibraryViewModel library) return;

        e.Handled = true;
        _ = library.SearchInsideAsync();
    }
}
