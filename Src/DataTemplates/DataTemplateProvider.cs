using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Integra7AuralAlchemist.Controls;
using Integra7AuralAlchemist.Models.Data;
using ReactiveUI;

namespace Integra7AuralAlchemist.DataTemplates;

public static class DataTemplateProvider
{
    /// <summary>Renders a parameter with a slider for its numeric value. Used by the advanced raw
    /// parameter grid.</summary>
    public static FuncDataTemplate<FullyQualifiedParameter> ParameterValueTemplate { get; }
        = new(parameter => parameter is not null, BuildParameterValuePresenter);

    /// <summary>Renders a parameter with a rotary knob (and a read-only mapped readout) for its numeric
    /// value. Used by the friendly editors' FX / Instrument sections. Non-numeric parameters render
    /// exactly as in <see cref="ParameterValueTemplate"/>.</summary>
    public static FuncDataTemplate<FullyQualifiedParameter> ParameterKnobTemplate { get; }
        = new(parameter => parameter is not null, BuildParameterKnobPresenter);

    // Subscribes a control to the parameter's StringValue changes so the displayed value updates
    // when the value is read from the Integra-7 (FullyQualifiedParameter raises INotifyPropertyChanged).
    // The update is marshalled to the UI thread (reads can complete on a MIDI background thread) and is
    // unsubscribed when the control leaves the visual tree to avoid leaks. While applyFromModel runs it
    // sets a suppression flag so the control's own change handler does not echo the value back to hardware.
    private static void BindToModel(Control control, FullyQualifiedParameter p, Action applyFromModel,
        Func<bool> isSuppressed, Action<bool> setSuppressed)
    {
        void Apply()
        {
            setSuppressed(true);
            try { applyFromModel(); }
            finally { setSuppressed(false); }
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
            Dispatcher.UIThread.Post(Apply);
        };

        // Subscribe per attach (not once at build time): Avalonia transiently detaches/re-attaches
        // item containers during re-layout/re-sort (e.g. when a Refresh flows through the bound
        // collection). Subscribing once and unsubscribing on detach would leave a re-attached control
        // permanently unsubscribed, so it would silently stop tracking the parameter. Re-applying on
        // attach also catches any value that changed while the control was detached.
        control.AttachedToVisualTree += (_, _) =>
        {
            p.PropertyChanged -= handler; // guard against a double subscribe
            p.PropertyChanged += handler;
            Apply();
        };
        control.DetachedFromVisualTree += (_, _) => p.PropertyChanged -= handler;
    }

    private static Control BuildParameterValuePresenter(FullyQualifiedParameter p)
        => BuildNonNumericPresenter(p) ?? BuildSliderNumericPresenter(p);

    private static Control BuildParameterKnobPresenter(FullyQualifiedParameter p)
        => BuildNonNumericPresenter(p) ?? BuildKnobNumericPresenter(p);

    /// <summary>The text / combo / toggle editors, shared by both templates. Returns null when the
    /// parameter is numeric, so the caller renders the number its own way (slider or knob).</summary>
    private static Control? BuildNonNumericPresenter(FullyQualifiedParameter p)
    {
        var suppressPush = false;

        if (!p.IsNumeric && !p.IsDiscrete)
        {
            TextBox b = new() { Text = p.StringValue, HorizontalAlignment = HorizontalAlignment.Left };
            b.PropertyChanged += (s, e) =>
            {
                if (suppressPush) return;
                if (e.Property.Name == "Text")
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue}"), "ui2hw");
            };
            BindToModel(b, p, () => b.Text = p.StringValue, () => suppressPush, v => suppressPush = v);
            return b;
        }

        if (p.IsDiscrete)
        {
            ComboBox c = new();
            foreach (var el in p.ParSpec.Discrete) c.Items.Add(el.Item2);
            c.SelectedItem = p.StringValue;
            c.SelectionChanged += (s, e) =>
            {
                if (suppressPush) return;
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
            };
            BindToModel(c, p, () => c.SelectedItem = p.StringValue, () => suppressPush, v => suppressPush = v);
            return c;
        }

        var repr = p.EffectiveRepr ?? p.ParSpec.Repr;
        if (repr != null)
        {
            if (repr.Count == 2
                && repr.TryGetValue(0, out var off) && off.ToUpper() == "OFF"
                && repr.TryGetValue(1, out var on) && on.ToUpper() == "ON")
            {
                ToggleSwitch c = new();
                c.IsChecked = p.StringValue == repr[1];
                c.IsCheckedChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    if (s is ToggleSwitch checkBox)
                    {
                        var msg = "OFF";
                        if (checkBox?.IsChecked ?? false) msg = "ON";
                        MessageBus.Current.SendMessage(new UpdateMessageSpec(p, msg), "ui2hw");
                    }
                };
                BindToModel(c, p, () => c.IsChecked = p.StringValue == repr[1],
                    () => suppressPush, v => suppressPush = v);
                return c;
            }

            ComboBox c2 = new();
            foreach (var el in repr) c2.Items.Add(el.Value);
            c2.SelectedItem = p.StringValue;
            c2.SelectionChanged += (s, e) =>
            {
                if (suppressPush) return;
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
            };
            BindToModel(c2, p, () =>
            {
                var cur = p.EffectiveRepr ?? p.ParSpec.Repr;
                if (cur != null)
                {
                    var want = cur.Select(kv => kv.Value).ToList();
                    var have = c2.Items.Cast<object?>().Select(o => o?.ToString()).ToList();
                    if (!have.SequenceEqual(want))
                    {
                        c2.Items.Clear();
                        foreach (var v in want) c2.Items.Add(v);
                    }
                }
                c2.SelectedItem = p.StringValue;
            }, () => suppressPush, v => suppressPush = v);
            return c2;
        }

        return null; // numeric
    }

    private static Control BuildSliderNumericPresenter(FullyQualifiedParameter p)
    {
        var suppressPush = false;

        if (!float.IsNaN(p.ParSpec.OMin2) && !float.IsNaN(p.ParSpec.OMax2))
        {
            var ticks = p.ParSpec.Ticks; // compute once (Ticks allocates a fresh list per access)
            Slider s = new()
            {
                Minimum = p.ParSpec.OMin2,
                Maximum = p.ParSpec.OMax2,
                Width = 200,
                Orientation = Orientation.Horizontal,
                IsSnapToTickEnabled = true,
                Ticks = ticks
            };
            if (ticks.First() > ticks.Last())
            {
                s.Minimum = p.ParSpec.OMax2;
                s.Maximum = p.ParSpec.OMin2;
            }

            ApplyStringValueToSlider(s, p, p.ParSpec.OMin2);
            s.ValueChanged += (s, e) =>
            {
                if (suppressPush) return;
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
            };
            BindToModel(s, p, () => ApplyStringValueToSlider(s, p, p.ParSpec.OMin2),
                () => suppressPush, v => suppressPush = v);
            return BuildSliderPanel(s, p);
        }

        if (p.ParSpec.OMin != -20000)
        {
            var ticks = p.ParSpec.Ticks; // compute once (Ticks allocates a fresh list per access)
            Slider s = new()
            {
                Minimum = p.ParSpec.OMin,
                Maximum = p.ParSpec.OMax,
                Width = 200,
                Orientation = Orientation.Horizontal,
                IsSnapToTickEnabled = true,
                Ticks = ticks
            };
            if (ticks.First() > ticks.Last())
            {
                s.Minimum = p.ParSpec.OMax;
                s.Maximum = p.ParSpec.OMin;
            }

            ApplyStringValueToSlider(s, p, p.ParSpec.OMin);
            s.ValueChanged += (s, e) =>
            {
                if (suppressPush) return;
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
            };
            BindToModel(s, p, () => ApplyStringValueToSlider(s, p, p.ParSpec.OMin),
                () => suppressPush, v => suppressPush = v);
            return BuildSliderPanel(s, p);
        }

        Slider sd = new()
        {
            Minimum = 0,
            Maximum = 127,
            Width = 200,
            Orientation = Orientation.Horizontal,
            IsSnapToTickEnabled = true,
            Ticks = p.ParSpec.Ticks
        };
        ApplyStringValueToSlider(sd, p, 0);
        sd.ValueChanged += (s, e) =>
        {
            if (suppressPush) return;
            MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
        };
        BindToModel(sd, p, () => ApplyStringValueToSlider(sd, p, 0),
            () => suppressPush, v => suppressPush = v);
        return BuildSliderPanel(sd, p);
    }

    /// <summary>A rotary knob over the parameter's displayed value, snapping to the allowed steps. It
    /// operates in the same displayed-value space the slider used, and sends the same displayed value on
    /// the ui2hw bus, so the write behaviour is identical -- only the control differs.</summary>
    private static Control BuildKnobNumericPresenter(FullyQualifiedParameter p)
    {
        var suppressPush = false;

        var ticks = p.ParSpec.Ticks;
        if (ticks.Count == 0) for (var i = 0; i <= 127; i++) ticks.Add(i);
        var snap = ticks.ToList();
        double dmin = snap.Min(), dmax = snap.Max();

        RotaryKnobDial dial = new()
        {
            Minimum = dmin,
            Maximum = dmax,
            SnapValues = snap,
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            AccentBrush = FxAccentBrush()
        };

        ApplyStringValueToDial(dial, p, dmin);
        dial.PropertyChanged += (_, e) =>
        {
            if (suppressPush) return;
            if (e.Property == RotaryKnobDial.ValueProperty)
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{dial.Value:0.##}"), "ui2hw");
        };
        BindToModel(dial, p, () => ApplyStringValueToDial(dial, p, dmin),
            () => suppressPush, v => suppressPush = v);

        return BuildKnobPanel(dial, p);
    }

    private static void ApplyStringValueToSlider(Slider s, FullyQualifiedParameter p, double fallback)
    {
        if (double.TryParse(p.StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ||
            double.TryParse(p.StringValue, out value))
            s.Value = Math.Round(value, 2);
        else
            s.Value = fallback;
    }

    private static void ApplyStringValueToDial(RotaryKnobDial d, FullyQualifiedParameter p, double fallback)
    {
        if (double.TryParse(p.StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ||
            double.TryParse(p.StringValue, out value))
            d.Value = Math.Round(value, 2);
        else
            d.Value = fallback;
    }

    private static IBrush FxAccentBrush()
        => Application.Current?.TryFindResource("KnobFxBrush", out var b) == true && b is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse("#B0895F"));

    private static StackPanel BuildSliderPanel(Slider s, FullyQualifiedParameter p)
    {
        TextBlock v = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding
            {
                Source = s,
                Path = nameof(s.Value),
                StringFormat = "0.##"
            }
        };
        StackPanel pan = new()
        {
            Orientation = Orientation.Horizontal
        };
        pan.Children.Add(s);
        pan.Children.Add(v);
        if (p.ParSpec.Unit != "")
        {
            TextBlock u = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = " [" + p.ParSpec.Unit + "]"
            };
            pan.Children.Add(u);
        }

        return pan;
    }

    private static StackPanel BuildKnobPanel(RotaryKnobDial d, FullyQualifiedParameter p)
    {
        TextBlock v = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            [!TextBlock.TextProperty] = new Binding
            {
                Source = d,
                Path = nameof(d.Value),
                StringFormat = "0.##"
            }
        };
        StackPanel readout = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        readout.Children.Add(v);
        if (p.ParSpec.Unit != "")
            readout.Children.Add(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10,
                Opacity = 0.7,
                Text = p.ParSpec.Unit
            });

        StackPanel pan = new() { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left };
        pan.Children.Add(d);
        pan.Children.Add(readout);
        return pan;
    }
}
