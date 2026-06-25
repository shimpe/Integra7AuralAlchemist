using System;
using Avalonia;
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Behaviors;

/// <summary>
/// Attached behaviour to select a <see cref="TabControl"/> tab by a stable <see cref="TabItem.Tag"/>
/// value instead of a brittle, layout-order-dependent index. Bind <c>SelectTabByTag</c> to a key
/// (e.g. the current tone type); the tab whose <c>Tag</c> equals that value is selected. It is a no-op
/// when the value is null/empty or no tab matches, so tabs without a Tag are simply left alone.
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

    static TabControlBehaviors()
    {
        SelectTabByTagProperty.Changed.AddClassHandler<TabControl>(OnSelectTabByTagChanged);
    }

    private static void OnSelectTabByTagChanged(TabControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string tag || string.IsNullOrEmpty(tag))
            return;

        foreach (var item in control.Items)
            if (item is TabItem tab && string.Equals(tab.Tag as string, tag, StringComparison.Ordinal))
            {
                control.SelectedItem = tab;
                return;
            }
    }
}
