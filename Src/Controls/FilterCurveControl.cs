using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Interactive, approximate filter-response graph. Drag (or arrow-keys) set Cutoff (horizontal)
/// and Resonance (vertical); the curve shape follows Mode + Slope. A non-graphical fallback
/// (numeric boxes) lives next to it in the view. Curve math is in <see cref="FilterCurve"/>.
/// </summary>
public class FilterCurveControl : Control
{
    private const double HandleRadius = 7;
    private const int TickStep = 16;

    public static readonly StyledProperty<string> ModeProperty =
        AvaloniaProperty.Register<FilterCurveControl, string>(nameof(Mode), "Low pass");
    public static readonly StyledProperty<bool> SteepProperty =
        AvaloniaProperty.Register<FilterCurveControl, bool>(nameof(Steep));
    public static readonly StyledProperty<int> CutoffProperty =
        AvaloniaProperty.Register<FilterCurveControl, int>(nameof(Cutoff), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> ResonanceProperty =
        AvaloniaProperty.Register<FilterCurveControl, int>(nameof(Resonance), 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<FilterCurveControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#E0A23D")));
    public static readonly StyledProperty<IBrush> FillBrushProperty = B(nameof(FillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xb0, 0x7a, 0x1e)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);

    public string Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool Steep { get => GetValue(SteepProperty); set => SetValue(SteepProperty, value); }
    public int Cutoff { get => GetValue(CutoffProperty); set => SetValue(CutoffProperty, value); }
    public int Resonance { get => GetValue(ResonanceProperty); set => SetValue(ResonanceProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }

    private bool _dragging;

    static FilterCurveControl()
    {
        AffectsRender<FilterCurveControl>(ModeProperty, SteepProperty, CutoffProperty, ResonanceProperty,
            LineBrushProperty, FillBrushProperty, BackgroundBrushProperty, GridBrushProperty,
            AxisBrushProperty, HandleBrushProperty);
        FocusableProperty.OverrideDefaultValue<FilterCurveControl>(true);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        DrawGrid(context, w, h);

        var pts = FilterCurve.Sample(Mode, Steep, Cutoff, Resonance);

        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(0, h), true);
            foreach (var p in pts) c.LineTo(new Point(p.X * w, (1 - p.Y) * h));
            c.LineTo(new Point(w, h));
            c.EndFigure(true);
        }
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            var first = true;
            foreach (var p in pts)
            {
                var pt = new Point(p.X * w, (1 - p.Y) * h);
                if (first) { c.BeginFigure(pt, false); first = false; } else c.LineTo(pt);
            }
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), line);

        // Handle sits on the curve peak at the cutoff.
        var hx = Cutoff / 127.0 * w;
        var hy = (1 - FilterCurve.PeakLevel(Resonance)) * h;
        context.DrawEllipse(HandleBrush, new Pen(LineBrush, 2), new Point(hx, hy), HandleRadius, HandleRadius);
    }

    private void DrawGrid(DrawingContext ctx, double w, double h)
    {
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        for (var v = 0; v <= 127; v += TickStep)
        {
            var y = (1 - v / 127.0) * h;
            ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            ctx.DrawLine(axisPen, new Point(0, y), new Point(6, y));
            var x = v / 127.0 * w;
            ctx.DrawLine(axisPen, new Point(x, h - 6), new Point(x, h));
        }
        ctx.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        ctx.DrawLine(axisPen, new Point(0, h), new Point(w, h));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        Apply(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        Apply(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Apply(Point pos)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Cutoff = SnsEnvelopeMapping.Clamp((int)System.Math.Round(pos.X / Bounds.Width * 127));
        // Resonance maps to the top half of the graph (peak level 0.5..1.0).
        var peakTarget = 1 - pos.Y / Bounds.Height;
        var res01 = (peakTarget - FilterCurve.PassLevel) / (1.0 - FilterCurve.PassLevel);
        Resonance = SnsEnvelopeMapping.Clamp((int)System.Math.Round(res01 * 127));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: Cutoff = SnsEnvelopeMapping.Clamp(Cutoff - 1); e.Handled = true; break;
            case Key.Right: Cutoff = SnsEnvelopeMapping.Clamp(Cutoff + 1); e.Handled = true; break;
            case Key.Up: Resonance = SnsEnvelopeMapping.Clamp(Resonance + 1); e.Handled = true; break;
            case Key.Down: Resonance = SnsEnvelopeMapping.Clamp(Resonance - 1); e.Handled = true; break;
        }
    }
}
