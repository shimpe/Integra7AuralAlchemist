using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Integra7AuralAlchemist.Controls;

/// <summary>A slider that also shows its current numeric value (and an optional unit), so the friendly
/// editors read like the advanced parameter tabs. Drop-in for a bare Slider: same
/// Minimum / Maximum / Value (TwoWay) properties.</summary>
public partial class ValueSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 127);
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<ValueSlider, string>(nameof(Unit), "");
    public static readonly StyledProperty<string> ValueTextProperty =
        AvaloniaProperty.Register<ValueSlider, string>(nameof(ValueText), "0");

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>The formatted readout (value + unit) — bound by the template; not set externally.</summary>
    public string ValueText { get => GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }

    public ValueSlider()
    {
        InitializeComponent();
        UpdateText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == UnitProperty) UpdateText();
    }

    private void UpdateText()
    {
        var v = (int)Math.Round(Value);
        ValueText = string.IsNullOrEmpty(Unit) ? v.ToString() : $"{v} {Unit}";
    }
}
