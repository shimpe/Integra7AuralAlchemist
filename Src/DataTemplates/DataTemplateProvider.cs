using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Integra7AuralAlchemist.Models.Data;
using ReactiveUI;

namespace Integra7AuralAlchemist.DataTemplates;

public static class DataTemplateProvider
{
    public static FuncDataTemplate<FullyQualifiedParameter> ParameterValueTemplate { get; }
        = new(parameter => parameter is not null, BuildParameterValuePresenter);

    private static Control BuildParameterValuePresenter(FullyQualifiedParameter p)
    {
        if (!p.IsNumeric && !p.IsDiscrete)
        {
            TextBox b = new() { Text = p.StringValue, HorizontalAlignment = HorizontalAlignment.Left };
            b.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Text")
                    //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.OldValue}\" to \"{e.NewValue}\"");
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue}"), "ui2hw");
            };
            return b;
        }

        if (p.IsDiscrete)
        {
            ComboBox c = new();
            foreach (var el in p.ParSpec.Discrete) c.Items.Add(el.Item2);
            c.SelectedItem = p.StringValue;
            c.SelectionChanged += (s, e) =>
            {
                //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.RemovedItems[0]}\" to \"{e.AddedItems[0]}\"");
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
            };
            return c;
        }

        if (p.ParSpec.Repr != null)
        {
            if (p.ParSpec.Repr.Count == 2 && p.ParSpec.Repr[0].ToUpper() == "OFF" &&
                p.ParSpec.Repr[1].ToUpper() == "ON")
            {
                ToggleSwitch c = new();
                c.IsChecked = p.StringValue == p.ParSpec.Repr[1];
                c.IsCheckedChanged += (s, e) =>
                {
                    if (s is ToggleSwitch checkBox)
                    {
                        var msg = "OFF";
                        if (checkBox?.IsChecked ?? false) msg = "ON";
                        //Debug.WriteLine($"ToggleSwitch {p.ParSpec.Path} changed to {msg}");
                        MessageBus.Current.SendMessage(new UpdateMessageSpec(p, msg), "ui2hw");
                    }
                };
                return c;
            }
            else
            {
                ComboBox c = new();
                foreach (var el in p.ParSpec.Repr) c.Items.Add(el.Value);
                c.SelectedItem = p.StringValue;
                c.SelectionChanged += (s, e) =>
                {
                    //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.RemovedItems[0]}\" to \"{e.AddedItems[0]}\"");
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
                };
                return c;
            }
        }

        if (!float.IsNaN(p.ParSpec.OMin2) && !float.IsNaN(p.ParSpec.OMax2))
        {
            Slider s = new()
            {
                Minimum = p.ParSpec.OMin2,
                Maximum = p.ParSpec.OMax2,
                Width = 200,
                Orientation = Orientation.Horizontal,
                IsSnapToTickEnabled = true,
                Ticks = p.ParSpec.Ticks
            };
            if (p.ParSpec.Ticks.First() > p.ParSpec.Ticks.Last())
            {
                //s.IsDirectionReversed = true;
                s.Minimum = p.ParSpec.OMax2;
                s.Maximum = p.ParSpec.OMin2;
            }

            if (double.TryParse(p.StringValue, out var LValue))
                s.Value = Math.Round(LValue, 2);
            else
                s.Value = p.ParSpec.OMin2;
            s.ValueChanged += (s, e) =>
            {
                //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.OldValue}\" to \"{e.NewValue}\"");
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
            };
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

        if (p.ParSpec.OMin != -20000)
        {
            Slider s = new()
            {
                Minimum = p.ParSpec.OMin,
                Maximum = p.ParSpec.OMax,
                Width = 200,
                Orientation = Orientation.Horizontal,
                IsSnapToTickEnabled = true,
                Ticks = p.ParSpec.Ticks
            };
            if (p.ParSpec.Ticks.First() > p.ParSpec.Ticks.Last())
            {
                //s.IsDirectionReversed = true;
                s.Minimum = p.ParSpec.OMax;
                s.Maximum = p.ParSpec.OMin;
            }

            if (double.TryParse(p.StringValue, out var LValue))
                s.Value = Math.Round(LValue, 2);
            else
                s.Value = p.ParSpec.OMin;
            s.ValueChanged += (s, e) =>
            {
                //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.OldValue}\" to \"{e.NewValue}\"");
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
            };
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
        else
        {
            Slider s = new()
            {
                Minimum = 0,
                Maximum = 127,
                Width = 200,
                Orientation = Orientation.Horizontal,
                IsSnapToTickEnabled = true,
                Ticks = p.ParSpec.Ticks
            };
            if (double.TryParse(p.StringValue, out var LValue))
                s.Value = (long)Math.Round(LValue);
            else
                s.Value = 0;
            s.ValueChanged += (s, e) =>
            {
                //Debug.WriteLine($"{p.ParSpec.Path} changed from \"{e.OldValue}\" to \"{e.NewValue}\"");
                MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.NewValue:0.##}"), "ui2hw");
            };
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
    }
}