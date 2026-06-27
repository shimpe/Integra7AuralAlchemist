using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Four WMT velocity lanes (WMT1..4, top→bottom). X is velocity 0..127. Each active lane shows
/// its range [lo..hi] with fade ramps; drag an edge to resize, the body to move, or click a lane to
/// select it (TwoWay <see cref="SelectedIndex"/>). Geometry is delegated to <see cref="WmtVelocityMapping"/>.</summary>
public class WmtVelocityMapControl : Control
{
    private const double HandleMargin = 6;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<WmtVelocityMapControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Wmt1LoProperty = I(nameof(Wmt1Lo));
    public static readonly StyledProperty<int> Wmt1HiProperty = I(nameof(Wmt1Hi));
    public static readonly StyledProperty<int> Wmt1FadeLoProperty = I(nameof(Wmt1FadeLo));
    public static readonly StyledProperty<int> Wmt1FadeHiProperty = I(nameof(Wmt1FadeHi));
    public static readonly StyledProperty<int> Wmt2LoProperty = I(nameof(Wmt2Lo));
    public static readonly StyledProperty<int> Wmt2HiProperty = I(nameof(Wmt2Hi));
    public static readonly StyledProperty<int> Wmt2FadeLoProperty = I(nameof(Wmt2FadeLo));
    public static readonly StyledProperty<int> Wmt2FadeHiProperty = I(nameof(Wmt2FadeHi));
    public static readonly StyledProperty<int> Wmt3LoProperty = I(nameof(Wmt3Lo));
    public static readonly StyledProperty<int> Wmt3HiProperty = I(nameof(Wmt3Hi));
    public static readonly StyledProperty<int> Wmt3FadeLoProperty = I(nameof(Wmt3FadeLo));
    public static readonly StyledProperty<int> Wmt3FadeHiProperty = I(nameof(Wmt3FadeHi));
    public static readonly StyledProperty<int> Wmt4LoProperty = I(nameof(Wmt4Lo));
    public static readonly StyledProperty<int> Wmt4HiProperty = I(nameof(Wmt4Hi));
    public static readonly StyledProperty<int> Wmt4FadeLoProperty = I(nameof(Wmt4FadeLo));
    public static readonly StyledProperty<int> Wmt4FadeHiProperty = I(nameof(Wmt4FadeHi));

    public static readonly StyledProperty<bool> Wmt1OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt1On));
    public static readonly StyledProperty<bool> Wmt2OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt2On));
    public static readonly StyledProperty<bool> Wmt3OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt3On));
    public static readonly StyledProperty<bool> Wmt4OnProperty = AvaloniaProperty.Register<WmtVelocityMapControl, bool>(nameof(Wmt4On));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WmtVelocityMapControl, int>(nameof(SelectedIndex), 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<WmtVelocityMapControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> Lane1BrushProperty = B(nameof(Lane1Brush), new SolidColorBrush(Color.Parse("#6b8dff")));
    public static readonly StyledProperty<IBrush> Lane2BrushProperty = B(nameof(Lane2Brush), new SolidColorBrush(Color.Parse("#ff9e6b")));
    public static readonly StyledProperty<IBrush> Lane3BrushProperty = B(nameof(Lane3Brush), new SolidColorBrush(Color.Parse("#7ad19a")));
    public static readonly StyledProperty<IBrush> Lane4BrushProperty = B(nameof(Lane4Brush), new SolidColorBrush(Color.Parse("#d18ad1")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> LabelBrushProperty = B(nameof(LabelBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> HighlightBrushProperty = B(nameof(HighlightBrush), Brushes.Orange);

    public int Wmt1Lo { get => GetValue(Wmt1LoProperty); set => SetValue(Wmt1LoProperty, value); }
    public int Wmt1Hi { get => GetValue(Wmt1HiProperty); set => SetValue(Wmt1HiProperty, value); }
    public int Wmt1FadeLo { get => GetValue(Wmt1FadeLoProperty); set => SetValue(Wmt1FadeLoProperty, value); }
    public int Wmt1FadeHi { get => GetValue(Wmt1FadeHiProperty); set => SetValue(Wmt1FadeHiProperty, value); }
    public int Wmt2Lo { get => GetValue(Wmt2LoProperty); set => SetValue(Wmt2LoProperty, value); }
    public int Wmt2Hi { get => GetValue(Wmt2HiProperty); set => SetValue(Wmt2HiProperty, value); }
    public int Wmt2FadeLo { get => GetValue(Wmt2FadeLoProperty); set => SetValue(Wmt2FadeLoProperty, value); }
    public int Wmt2FadeHi { get => GetValue(Wmt2FadeHiProperty); set => SetValue(Wmt2FadeHiProperty, value); }
    public int Wmt3Lo { get => GetValue(Wmt3LoProperty); set => SetValue(Wmt3LoProperty, value); }
    public int Wmt3Hi { get => GetValue(Wmt3HiProperty); set => SetValue(Wmt3HiProperty, value); }
    public int Wmt3FadeLo { get => GetValue(Wmt3FadeLoProperty); set => SetValue(Wmt3FadeLoProperty, value); }
    public int Wmt3FadeHi { get => GetValue(Wmt3FadeHiProperty); set => SetValue(Wmt3FadeHiProperty, value); }
    public int Wmt4Lo { get => GetValue(Wmt4LoProperty); set => SetValue(Wmt4LoProperty, value); }
    public int Wmt4Hi { get => GetValue(Wmt4HiProperty); set => SetValue(Wmt4HiProperty, value); }
    public int Wmt4FadeLo { get => GetValue(Wmt4FadeLoProperty); set => SetValue(Wmt4FadeLoProperty, value); }
    public int Wmt4FadeHi { get => GetValue(Wmt4FadeHiProperty); set => SetValue(Wmt4FadeHiProperty, value); }
    public bool Wmt1On { get => GetValue(Wmt1OnProperty); set => SetValue(Wmt1OnProperty, value); }
    public bool Wmt2On { get => GetValue(Wmt2OnProperty); set => SetValue(Wmt2OnProperty, value); }
    public bool Wmt3On { get => GetValue(Wmt3OnProperty); set => SetValue(Wmt3OnProperty, value); }
    public bool Wmt4On { get => GetValue(Wmt4OnProperty); set => SetValue(Wmt4OnProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public IBrush Lane1Brush { get => GetValue(Lane1BrushProperty); set => SetValue(Lane1BrushProperty, value); }
    public IBrush Lane2Brush { get => GetValue(Lane2BrushProperty); set => SetValue(Lane2BrushProperty, value); }
    public IBrush Lane3Brush { get => GetValue(Lane3BrushProperty); set => SetValue(Lane3BrushProperty, value); }
    public IBrush Lane4Brush { get => GetValue(Lane4BrushProperty); set => SetValue(Lane4BrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    private int _dragLane = -1;
    private WmtVelocityMapping.Handle _dragHandle;
    private double _dragOrigX;
    private int _origLo, _origHi;

    static WmtVelocityMapControl()
    {
        AffectsRender<WmtVelocityMapControl>(
            Wmt1LoProperty, Wmt1HiProperty, Wmt1FadeLoProperty, Wmt1FadeHiProperty,
            Wmt2LoProperty, Wmt2HiProperty, Wmt2FadeLoProperty, Wmt2FadeHiProperty,
            Wmt3LoProperty, Wmt3HiProperty, Wmt3FadeLoProperty, Wmt3FadeHiProperty,
            Wmt4LoProperty, Wmt4HiProperty, Wmt4FadeLoProperty, Wmt4FadeHiProperty,
            Wmt1OnProperty, Wmt2OnProperty, Wmt3OnProperty, Wmt4OnProperty, SelectedIndexProperty,
            Lane1BrushProperty, Lane2BrushProperty, Lane3BrushProperty, Lane4BrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty, LabelBrushProperty, HighlightBrushProperty);
        FocusableProperty.OverrideDefaultValue<WmtVelocityMapControl>(true);
    }

    private (int lo, int hi, int fadeLo, int fadeHi, bool on, IBrush brush) Lane(int i) => i switch
    {
        0 => (Wmt1Lo, Wmt1Hi, Wmt1FadeLo, Wmt1FadeHi, Wmt1On, Lane1Brush),
        1 => (Wmt2Lo, Wmt2Hi, Wmt2FadeLo, Wmt2FadeHi, Wmt2On, Lane2Brush),
        2 => (Wmt3Lo, Wmt3Hi, Wmt3FadeLo, Wmt3FadeHi, Wmt3On, Lane3Brush),
        3 => (Wmt4Lo, Wmt4Hi, Wmt4FadeLo, Wmt4FadeHi, Wmt4On, Lane4Brush),
        _ => (0, 0, 0, 0, false, Lane1Brush),
    };

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var culture = CultureInfo.CurrentCulture;

        for (var v = 0; v <= 127; v += 16)
        {
            var x = WmtVelocityMapping.VelToX(v, w);
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
            var vt = new FormattedText(v.ToString(culture), culture, FlowDirection.LeftToRight, Typeface.Default, 9, AxisBrush);
            context.DrawText(vt, new Point(x + 2, h - 12));
        }

        for (var i = 0; i < WmtVelocityMapping.LaneCount; i++)
        {
            var z = Lane(i);
            var lane = WmtVelocityMapping.LaneRect(i, w, h);
            context.DrawLine(axisPen, new Point(0, lane.Y + lane.H + 3), new Point(w, lane.Y + lane.H + 3));

            var opacity = z.on ? 1.0 : 0.25;
            using (context.PushOpacity(opacity))
            {
                if (z.on)
                {
                    var loX = WmtVelocityMapping.VelToX(Math.Min(z.lo, z.hi), w);
                    var hiX = WmtVelocityMapping.VelToX(Math.Max(z.lo, z.hi), w);
                    var fadeLoX = WmtVelocityMapping.VelToX(Math.Max(0, Math.Min(z.lo, z.hi) - z.fadeLo), w);
                    var fadeHiX = WmtVelocityMapping.VelToX(Math.Min(127, Math.Max(z.lo, z.hi) + z.fadeHi), w);
                    var top = lane.Y;
                    var bot = lane.Y + lane.H;

                    var geo = new StreamGeometry();
                    using (var c = geo.Open())
                    {
                        c.BeginFigure(new Point(fadeLoX, bot), true);
                        c.LineTo(new Point(loX, top));
                        c.LineTo(new Point(hiX, top));
                        c.LineTo(new Point(fadeHiX, bot));
                        c.EndFigure(true);
                    }
                    using (context.PushOpacity(0.30)) context.DrawGeometry(z.brush, null, geo);
                    context.DrawGeometry(null, new Pen(z.brush, 2), geo);

                    var label = $"WMT{i + 1}  vel {Math.Min(z.lo, z.hi)}-{Math.Max(z.lo, z.hi)}";
                    var ft = new FormattedText(label, culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                    context.DrawText(ft, new Point(loX + 4, top + 2));
                }
                else
                {
                    var ft = new FormattedText($"WMT{i + 1}  (off)", culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                    context.DrawText(ft, new Point(4, lane.Y + 2));
                }
            }

            if (i == SelectedIndex)
                context.DrawRectangle(null, new Pen(HighlightBrush, 1.5), new Rect(lane.X, lane.Y, lane.W, lane.H));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        var lane = WmtVelocityMapping.LaneAt(pos.Y, h);
        if (lane < 0) return;

        SelectedIndex = lane;

        var z = Lane(lane);
        if (z.on)
        {
            var band = WmtVelocityMapping.BandRect(z.lo, z.hi, lane, w, h);
            var hit = WmtVelocityMapping.HitBand(pos.X, pos.Y, band, HandleMargin);
            if (hit != WmtVelocityMapping.Handle.None)
            {
                _dragLane = lane;
                _dragHandle = hit;
                _dragOrigX = pos.X;
                _origLo = z.lo; _origHi = z.hi;
                e.Pointer.Capture(this);
            }
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragLane < 0) return;
        var pos = e.GetPosition(this);
        double w = Bounds.Width;
        var z = Lane(_dragLane);

        switch (_dragHandle)
        {
            case WmtVelocityMapping.Handle.Left:
                SetLo(_dragLane, Math.Min(WmtVelocityMapping.XToVel(pos.X, w), z.hi));
                break;
            case WmtVelocityMapping.Handle.Right:
                SetHi(_dragLane, Math.Max(WmtVelocityMapping.XToVel(pos.X, w), z.lo));
                break;
            case WmtVelocityMapping.Handle.Body:
                var d = (int)Math.Round((pos.X - _dragOrigX) / w * 127.0, MidpointRounding.AwayFromZero);
                if (_origLo + d < 0) d = -_origLo;
                if (_origHi + d > 127) d = 127 - _origHi;
                SetLo(_dragLane, _origLo + d); SetHi(_dragLane, _origHi + d);
                break;
        }
        e.Handled = true;

        void SetLo(int i, int v) { switch (i) { case 0: Wmt1Lo = v; break; case 1: Wmt2Lo = v; break; case 2: Wmt3Lo = v; break; case 3: Wmt4Lo = v; break; } }
        void SetHi(int i, int v) { switch (i) { case 0: Wmt1Hi = v; break; case 1: Wmt2Hi = v; break; case 2: Wmt3Hi = v; break; case 3: Wmt4Hi = v; break; } }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragLane < 0) return;
        e.Pointer.Capture(null);
        _dragLane = -1;
        e.Handled = true;
    }
}
