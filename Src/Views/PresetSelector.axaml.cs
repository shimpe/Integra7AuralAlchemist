using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Views;

public partial class PresetSelector : UserControl
{
    // add an argument "SearchText" to the user control
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<PresetSelector, string>(nameof(SearchText));

    // add an argument "Presets" to the user control
    public static readonly StyledProperty<ReadOnlyObservableCollection<Integra7Preset>> PresetsProperty =
        AvaloniaProperty.Register<PresetSelector, ReadOnlyObservableCollection<Integra7Preset>>(nameof(Presets));

    // add an argument "SelectedPreset" to the user control
    public static readonly StyledProperty<Integra7Preset> SelectedPresetProperty =
        AvaloniaProperty.Register<PresetSelector, Integra7Preset>(nameof(SelectedPreset));

    public PresetSelector()
    {
        InitializeComponent();
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public ReadOnlyObservableCollection<Integra7Preset> Presets
    {
        get => GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public Integra7Preset SelectedPreset
    {
        get => (Integra7Preset)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    /// <summary>Reports the clicked preset, and only the preset. This control used to publish the row
    /// index alongside it; nothing may reintroduce that. The rows here are a filtered, re-sorted view of
    /// the preset list, so a row index is an index into a subset -- and the one caller that consumed it
    /// used it as a user memory slot number, which meant a non-empty search box made Save User Tone
    /// overwrite a different saved sound. See <c>UserToneSlots</c> for how that number is derived now.</summary>
    public void PresetDataGrid_CellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs? args)
    {
        SelectedPreset = (Integra7Preset)args.Row.DataContext;
    }

    /// <summary>Keep the selected preset visible. Reacting to the property *change* (rather than to the
    /// CLR setter) is what makes this work when the values arrive through bindings — switching part tabs
    /// rebinds Presets/SelectedPreset via SetValue, which never runs the CLR setter.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedPresetProperty || change.Property == PresetsProperty)
            ScrollToSelected();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The template may only be realized after the bindings have already delivered their values.
        ScrollToSelected();
    }

    private void ScrollToSelected()
    {
        // Deferred: the DataGrid can only scroll to a row once it has been measured and its rows exist.
        Dispatcher.UIThread.Post(() =>
        {
            var selected = SelectedPreset;
            // Scroll by identity, never by index: the list is filtered (search text, unloaded SRX banks)
            // and re-sorted, so row indices are not stable. The selection can also be filtered out of the
            // visible list entirely while remaining the selected preset — nothing to scroll to then.
            if (selected is null || Presets is null || !Presets.Contains(selected)) return;
            PresetDataGrid.ScrollIntoView(selected, null);
        }, DispatcherPriority.Loaded);
    }
}