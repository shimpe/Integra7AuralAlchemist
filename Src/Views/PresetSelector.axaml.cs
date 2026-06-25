using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
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

    // add an argument "SelectedPresetIndex" to the user control
    public static readonly StyledProperty<int> SelectedPresetIndexProperty =
        AvaloniaProperty.Register<PresetSelector, int>(nameof(SelectedPresetIndex));

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
        set
        {
            SetValue(SelectedPresetProperty, value);
            PresetDataGrid.ScrollIntoView(value, null);
        }
    }

    public int SelectedPresetIndex
    {
        get => GetValue(SelectedPresetIndexProperty);
        set => SetValue(SelectedPresetIndexProperty, value);
    }

    public void PresetDataGrid_CellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs? args)
    {
        SelectedPreset = (Integra7Preset)args.Row.DataContext;
        SelectedPresetIndex = args.Row.Index;
    }
}