using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Integra7AuralAlchemist.Controls;

/// <summary>A rotary knob with an editable value box and an optional unit: the drop-in replacement for
/// ValueSlider in the friendly editors. Same Minimum / Maximum / Value (TwoWay) surface, plus a Unit
/// and an AccentBrush used to colour the value strip so controls can be grouped by function.
///
/// The dial does the drawing and the dragging (RotaryKnobDial); this wraps it with the readout. The
/// text box commits on Enter or lost focus, clamped to the range -- it never streams keystrokes to the
/// hardware. The label stays outside the control, exactly where ValueSlider's label sat.</summary>
public partial class RotaryKnob : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RotaryKnob, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RotaryKnob, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RotaryKnob, double>(nameof(Maximum), 127);
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<RotaryKnob, string>(nameof(Unit), "");
    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        AvaloniaProperty.Register<RotaryKnob, IBrush>(nameof(AccentBrush),
            new SolidColorBrush(Color.Parse("#7FB6E0")));

    /// <summary>What the text box shows. Kept in step with Value except while the box has focus, so it
    /// does not fight the user mid-edit.</summary>
    public static readonly StyledProperty<string> EditTextProperty =
        AvaloniaProperty.Register<RotaryKnob, string>(nameof(EditText), "0");
    public static readonly StyledProperty<bool> HasUnitProperty =
        AvaloniaProperty.Register<RotaryKnob, bool>(nameof(HasUnit));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public IBrush AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public string EditText { get => GetValue(EditTextProperty); set => SetValue(EditTextProperty, value); }
    public bool HasUnit { get => GetValue(HasUnitProperty); set => SetValue(HasUnitProperty, value); }

    public RotaryKnob()
    {
        InitializeComponent();
        Editor.KeyDown += OnEditorKeyDown;
        Editor.LostFocus += OnEditorLostFocus;
        SyncEditText();
        HasUnit = !string.IsNullOrEmpty(Unit);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && !Editor.IsFocused) SyncEditText();
        else if (change.Property == UnitProperty) HasUnit = !string.IsNullOrEmpty(Unit);
    }

    private void SyncEditText() => EditText = ((int)Math.Round(Value)).ToString(CultureInfo.InvariantCulture);

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitEdit();
        e.Handled = true;
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => CommitEdit();

    private void CommitEdit()
    {
        if (int.TryParse(EditText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed))
            Value = Math.Clamp(typed, Minimum, Maximum);
        // Whether it parsed or not, snap the box back to the model value: a bad entry reverts, a good
        // one is normalised (clamped, no stray whitespace).
        SyncEditText();
    }
}
