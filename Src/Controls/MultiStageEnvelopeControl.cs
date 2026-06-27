using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// One 4-segment rate/level envelope (PCM TVP/TVF/TVA): 5 level points L0..L4 joined by 4 time
/// segments T1..T4. Each point is draggable; dragging a point sets its level (Y) and the time of
/// the segment before it (X). Pure geometry is delegated to <see cref="PcmEnvelopeMapping"/>.
/// </summary>
public class MultiStageEnvelopeControl : Control
{
    private const double HandleRadius = 7;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<MultiStageEnvelopeControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Time1Property = I(nameof(Time1));
    public static readonly StyledProperty<int> Time2Property = I(nameof(Time2));
    public static readonly StyledProperty<int> Time3Property = I(nameof(Time3));
    public static readonly StyledProperty<int> Time4Property = I(nameof(Time4));
    public static readonly StyledProperty<int> Level0Property = I(nameof(Level0));
    public static readonly StyledProperty<int> Level1Property = I(nameof(Level1));
    public static readonly StyledProperty<int> Level2Property = I(nameof(Level2));
    public static readonly StyledProperty<int> Level3Property = I(nameof(Level3));
    public static readonly StyledProperty<int> Level4Property = I(nameof(Level4));

    /// <summary>When true, levels are −63..+63 centered on a mid line (line only, no fill).</summary>
    public static readonly StyledProperty<bool> BipolarProperty =
        AvaloniaProperty.Register<MultiStageEnvelopeControl, bool>(nameof(Bipolar));

    /// <summary>When true, Level0 and Level4 are pinned (not draggable in Y).</summary>
    public static readonly StyledProperty<bool> FixedEndpointsProperty =
        AvaloniaProperty.Register<MultiStageEnvelopeControl, bool>(nameof(FixedEndpoints));

    /// <summary>When true, render as a non-interactive preview: no handles, no pointer input.</summary>
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<MultiStageEnvelopeControl, bool>(nameof(Preview));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<MultiStageEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty = B(nameof(FillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> FocusBrushProperty = B(nameof(FocusBrush), Brushes.Orange);

    public int Time1 { get => GetValue(Time1Property); set => SetValue(Time1Property, value); }
    public int Time2 { get => GetValue(Time2Property); set => SetValue(Time2Property, value); }
    public int Time3 { get => GetValue(Time3Property); set => SetValue(Time3Property, value); }
    public int Time4 { get => GetValue(Time4Property); set => SetValue(Time4Property, value); }
    public int Level0 { get => GetValue(Level0Property); set => SetValue(Level0Property, value); }
    public int Level1 { get => GetValue(Level1Property); set => SetValue(Level1Property, value); }
    public int Level2 { get => GetValue(Level2Property); set => SetValue(Level2Property, value); }
    public int Level3 { get => GetValue(Level3Property); set => SetValue(Level3Property, value); }
    public int Level4 { get => GetValue(Level4Property); set => SetValue(Level4Property, value); }
    public bool Bipolar { get => GetValue(BipolarProperty); set => SetValue(BipolarProperty, value); }
    public bool FixedEndpoints { get => GetValue(FixedEndpointsProperty); set => SetValue(FixedEndpointsProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public IBrush FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    // Index (0..4) of the point being dragged (−1 = none) and the focused handle for outlining.
    private int _drag = -1, _focused = 0;

    static MultiStageEnvelopeControl()
    {
        AffectsRender<MultiStageEnvelopeControl>(
            Time1Property, Time2Property, Time3Property, Time4Property,
            Level0Property, Level1Property, Level2Property, Level3Property, Level4Property,
            BipolarProperty, FixedEndpointsProperty, PreviewProperty,
            LineBrushProperty, FillBrushProperty, BackgroundBrushProperty,
            GridBrushProperty, AxisBrushProperty, HandleBrushProperty, FocusBrushProperty);
        FocusableProperty.OverrideDefaultValue<MultiStageEnvelopeControl>(true);
    }

    private PcmEnvelopeMapping.Point[] Points(double w, double h) =>
        PcmEnvelopeMapping.ComputePoints(Time1, Time2, Time3, Time4, Level0, Level1, Level2, Level3, Level4, w, h, Bipolar);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);

        // Horizontal grid lines every 16 levels (0..127), with a short axis tick at the left.
        for (var level = 0; level <= 127; level += 16)
        {
            var y = PcmEnvelopeMapping.LevelToY(level, h, false);
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            context.DrawLine(axisPen, new Point(0, y), new Point(6, y));
        }

        // Left axis and bottom axis.
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        context.DrawLine(axisPen, new Point(0, h), new Point(w, h));

        // Bipolar: faint center line.
        if (Bipolar)
            context.DrawLine(axisPen, new Point(0, h / 2.0), new Point(w, h / 2.0));

        var p = Points(w, h);

        // Fill (unipolar only).
        if (!Bipolar)
        {
            var fillGeo = new StreamGeometry();
            using (var c = fillGeo.Open())
            {
                c.BeginFigure(new Point(p[0].X, h), true);
                c.LineTo(new Point(p[0].X, p[0].Y));
                c.LineTo(new Point(p[1].X, p[1].Y));
                c.LineTo(new Point(p[2].X, p[2].Y));
                c.LineTo(new Point(p[3].X, p[3].Y));
                c.LineTo(new Point(p[4].X, p[4].Y));
                c.LineTo(new Point(p[4].X, h));
                c.EndFigure(true);
            }
            context.DrawGeometry(FillBrush, null, fillGeo);
        }

        // Line through the 5 points.
        var lineGeo = new StreamGeometry();
        using (var c = lineGeo.Open())
        {
            c.BeginFigure(new Point(p[0].X, p[0].Y), false);
            c.LineTo(new Point(p[1].X, p[1].Y));
            c.LineTo(new Point(p[2].X, p[2].Y));
            c.LineTo(new Point(p[3].X, p[3].Y));
            c.LineTo(new Point(p[4].X, p[4].Y));
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), lineGeo);

        if (Preview) return;
        for (var i = 0; i < p.Length; i++)
            DrawHandle(context, p[i], _focused == i);
    }

    private void DrawHandle(DrawingContext ctx, PcmEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? new Pen(FocusBrush, 2) : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return; // previews are non-interactive
        Focus();
        var pos = e.GetPosition(this);
        var hit = PcmEnvelopeMapping.NearestHandle(pos.X, pos.Y, Points(Bounds.Width, Bounds.Height), HandleRadius * 2);
        if (hit < 0) return;
        _drag = hit;
        _focused = hit;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        var pos = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        var seg = PcmEnvelopeMapping.SegmentMax(w);
        var pts = Points(w, h);
        int i = _drag;

        // Y → level (skip pinned endpoints when FixedEndpoints).
        if (!(FixedEndpoints && (i == 0 || i == 4)))
            SetLevel(i, PcmEnvelopeMapping.LevelFromY(pos.Y, h, Bipolar));

        // X → time of the segment ending at this point (i≥1).
        if (i >= 1)
            SetTime(i, PcmEnvelopeMapping.TimeFromX(pos.X, pts[i - 1].X, seg));

        e.Handled = true;

        void SetLevel(int idx, int v)
        {
            switch (idx)
            {
                case 0: Level0 = v; break;
                case 1: Level1 = v; break;
                case 2: Level2 = v; break;
                case 3: Level3 = v; break;
                case 4: Level4 = v; break;
            }
        }

        void SetTime(int idx, int v)
        {
            switch (idx)
            {
                case 1: Time1 = v; break;
                case 2: Time2 = v; break;
                case 3: Time3 = v; break;
                case 4: Time4 = v; break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        e.Pointer.Capture(null);
        _drag = -1;
        e.Handled = true;
    }
}
