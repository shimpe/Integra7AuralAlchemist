using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Integra7AuralAlchemist.Behaviors;

/// <summary>
/// Attached behaviour to select a <see cref="TabControl"/> tab by a stable <see cref="TabItem.Tag"/>
/// value instead of a brittle, layout-order-dependent index. Bind <c>SelectTabByTag</c> to a key
/// (e.g. the current tone type or an "Advanced" sub-tab tag); the tab whose <c>Tag</c> equals that
/// value is selected — at any nesting depth, opening each containing tab on the way. It is a no-op
/// when the value is null/empty or no tab matches.
///
/// After selecting, it also repairs any nested <see cref="TabControl"/> whose currently-selected tab
/// has become hidden (e.g. an "Advanced" sub-tab left selected when the preset's engine changed), so
/// a hidden tab's stale content can never be shown (Avalonia #16879).
/// </summary>
public sealed class TabControlBehaviors
{
    private TabControlBehaviors()
    {
    }

    public static readonly AttachedProperty<string?> SelectTabByTagProperty =
        AvaloniaProperty.RegisterAttached<TabControlBehaviors, TabControl, string?>("SelectTabByTag");

    public static void SetSelectTabByTag(TabControl control, string? value) =>
        control.SetValue(SelectTabByTagProperty, value);

    public static string? GetSelectTabByTag(TabControl control) =>
        control.GetValue(SelectTabByTagProperty);

    /// <summary>Bind to a value that changes whenever which tabs are visible changes (e.g. a part's
    /// tone type). Each change repairs nested selections without navigating anywhere, for the case
    /// where the selection is stale but the user is not asking to be taken to a particular tab —
    /// opening a part whose engine differs from whichever sub-tab happened to be selected.</summary>
    public static readonly AttachedProperty<string?> RepairSelectionsProperty =
        AvaloniaProperty.RegisterAttached<TabControlBehaviors, TabControl, string?>("RepairSelections");

    public static void SetRepairSelections(TabControl control, string? value) =>
        control.SetValue(RepairSelectionsProperty, value);

    public static string? GetRepairSelections(TabControl control) =>
        control.GetValue(RepairSelectionsProperty);

    static TabControlBehaviors()
    {
        SelectTabByTagProperty.Changed.AddClassHandler<TabControl>(OnSelectTabByTagChanged);
        RepairSelectionsProperty.Changed.AddClassHandler<TabControl>(OnRepairSelectionsChanged);
    }

    private static void OnRepairSelectionsChanged(TabControl control, AvaloniaPropertyChangedEventArgs e)
    {
        // Deferred like the repair after a tag selection, so the per-engine IsVisible bindings have
        // settled before the selections are inspected.
        Dispatcher.UIThread.Post(() => RepairHiddenSelections(control));
    }

    private static void OnSelectTabByTagChanged(TabControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string tag || string.IsNullOrEmpty(tag))
            return;

        SelectByTag(control, tag);

        // Reselect a visible tab in any nested control whose selected sub-tab is now hidden (e.g. the
        // "Advanced" sub-tabs after an engine change). Deferred so the per-engine IsVisible bindings
        // have settled before we read them.
        Dispatcher.UIThread.Post(() => RepairHiddenSelections(control));
    }

    /// <summary>Select the TabItem whose Tag matches, searching nested TabControls, and select each
    /// ancestor TabItem so the containing ("Advanced") tab is opened too. Returns true if found.</summary>
    private static bool SelectByTag(TabControl control, string tag)
    {
        foreach (var item in control.Items)
        {
            if (item is not TabItem tab)
                continue;

            if (string.Equals(tab.Tag as string, tag, StringComparison.Ordinal))
            {
                control.SelectedItem = tab;
                return true;
            }

            if (tab.Content is TabControl nested && SelectByTag(nested, tag))
            {
                control.SelectedItem = tab; // open the ancestor that contains the match
                return true;
            }
        }

        return false;
    }

    /// <summary>For this control and every nested TabControl, if the selected TabItem is hidden,
    /// reselect the first visible TabItem (or null if none).</summary>
    private static void RepairHiddenSelections(TabControl control)
    {
        if (control.SelectedItem is TabItem { IsVisible: false })
            control.SelectedItem = control.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);

        foreach (var item in control.Items)
            if (item is TabItem { Content: TabControl nested })
                RepairHiddenSelections(nested);
    }
}
