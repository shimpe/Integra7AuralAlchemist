using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Non-interactive one-cycle preview of an LFO shape. Shape geometry from <see cref="LfoWaveform"/>.</summary>
public class LfoWaveformControl : Control
{
    private const int Samples = 48;

    public static readonly StyledProperty<string> ShapeProperty =
        AvaloniaProperty.Register<LfoWaveformControl, string>(nameof(Shape), "Triangle");

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<LfoWaveformControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#4FB0A0")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public string Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    static LfoWaveformControl()
    {
        AffectsRender<LfoWaveformControl>(ShapeProperty, LineBrushProperty, BackgroundBrushProperty, AxisBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        context.DrawLine(new Pen(AxisBrush), new Point(0, h / 2), new Point(w, h / 2));

        var pts = LfoWaveform.Sample(Shape, Samples);
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            var first = true;
            foreach (var p in pts)
            {
                var pt = new Point(p.X * w, (1 - p.Y) * h);
                if (first) { c.BeginFigure(pt, false); first = false; } else c.LineTo(pt);
            }
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), geo);
    }
}
