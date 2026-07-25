using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Interactive, approximate 3-band EQ response graph. Each band has a handle: drag it sideways to
/// change the band frequency and up/down to change its gain; double-click one to flatten that band.
/// The frequency written back is continuous — the view model snaps it to the nearest value the
/// hardware allows and the handle follows, so the graph and the combo boxes always agree. Curve
/// maths lives in <see cref="EqCurve"/>.
/// </summary>
public class EqCurveControl : Control
{
    private const double HandleRadius = 7;
    private const double GrabDistance = 0.07;   // in normalized X/Y units
    private const int GainLimit = 15;           // hardware range per band, in dB
    private const double LabelStripHeight = 14; // bottom strip reserved for frequency labels

    private static StyledProperty<double> D(string name, double def) =>
        AvaloniaProperty.Register<EqCurveControl, double>(name, def, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<EqCurveControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<EqCurveControl, IBrush>(name, def);

    public static readonly StyledProperty<double> LowHzProperty = D(nameof(LowHz), 200);
    public static readonly StyledProperty<double> MidHzProperty = D(nameof(MidHz), 1000);
    public static readonly StyledProperty<double> HighHzProperty = D(nameof(HighHz), 4000);
    public static readonly StyledProperty<int> LowGainProperty = I(nameof(LowGain));
    public static readonly StyledProperty<int> MidGainProperty = I(nameof(MidGain));
    public static readonly StyledProperty<int> HighGainProperty = I(nameof(HighGain));

    /// <summary>Mid band Q. Shapes the bell but is not draggable — it is a five-value enum.</summary>
    public static readonly StyledProperty<double> MidQProperty =
        AvaloniaProperty.Register<EqCurveControl, double>(nameof(MidQ), 1.0);

    /// <summary>When false the EQ is bypassed: the curve is dimmed and a flat line is drawn over it.</summary>
    public static readonly StyledProperty<bool> IsOnProperty =
        AvaloniaProperty.Register<EqCurveControl, bool>(nameof(IsOn), true);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty = B(nameof(FillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3D, 0x7E, 0xAA)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> LowBandBrushProperty = B(nameof(LowBandBrush), new SolidColorBrush(Color.Parse("#6b8dff")));
    public static readonly StyledProperty<IBrush> MidBandBrushProperty = B(nameof(MidBandBrush), new SolidColorBrush(Color.Parse("#7ad19a")));
    public static readonly StyledProperty<IBrush> HighBandBrushProperty = B(nameof(HighBandBrush), new SolidColorBrush(Color.Parse("#ff9e6b")));
    public static readonly StyledProperty<IBrush> LabelBrushProperty = B(nameof(LabelBrush), Brushes.White);

    public double LowHz { get => GetValue(LowHzProperty); set => SetValue(LowHzProperty, value); }
    public double MidHz { get => GetValue(MidHzProperty); set => SetValue(MidHzProperty, value); }
    public double HighHz { get => GetValue(HighHzProperty); set => SetValue(HighHzProperty, value); }
    public int LowGain { get => GetValue(LowGainProperty); set => SetValue(LowGainProperty, value); }
    public int MidGain { get => GetValue(MidGainProperty); set => SetValue(MidGainProperty, value); }
    public int HighGain { get => GetValue(HighGainProperty); set => SetValue(HighGainProperty, value); }
    public double MidQ { get => GetValue(MidQProperty); set => SetValue(MidQProperty, value); }
    public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush LowBandBrush { get => GetValue(LowBandBrushProperty); set => SetValue(LowBandBrushProperty, value); }
    public IBrush MidBandBrush { get => GetValue(MidBandBrushProperty); set => SetValue(MidBandBrushProperty, value); }
    public IBrush HighBandBrush { get => GetValue(HighBandBrushProperty); set => SetValue(HighBandBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private static readonly double[] LabelledFrequencies = [50, 100, 200, 500, 1000, 2000, 5000, 10000];

    private int _dragBand = -1;

    static EqCurveControl()
    {
        AffectsRender<EqCurveControl>(LowHzProperty, MidHzProperty, HighHzProperty,
            LowGainProperty, MidGainProperty, HighGainProperty, MidQProperty, IsOnProperty,
            LineBrushProperty, FillBrushProperty, BackgroundBrushProperty, GridBrushProperty,
            AxisBrushProperty, LowBandBrushProperty, MidBandBrushProperty, HighBandBrushProperty,
            LabelBrushProperty);
        FocusableProperty.OverrideDefaultValue<EqCurveControl>(true);
    }

    private EqBands Bands => new(LowHz, LowGain, MidHz, MidGain, MidQ, HighHz, HighGain);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var plotH = Math.Max(1, h - LabelStripHeight); // the curve area; labels sit under it
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        DrawGrid(context, w, plotH);

        // A bypassed EQ still shows its settings, greyed out, so the knobs stay meaningful.
        using (context.PushOpacity(IsOn ? 1.0 : 0.35))
        {
            var pts = EqCurve.Sample(Bands);
            var zeroY = EqCurve.Y01(0) * plotH;

            var fill = new StreamGeometry();
            using (var c = fill.Open())
            {
                c.BeginFigure(new Point(0, zeroY), true);
                foreach (var p in pts) c.LineTo(new Point(p.X * w, EqCurve.Y01(p.Db) * plotH));
                c.LineTo(new Point(w, zeroY));
                c.EndFigure(true);
            }
            context.DrawGeometry(FillBrush, null, fill);

            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                var first = true;
                foreach (var p in pts)
                {
                    var pt = new Point(p.X * w, EqCurve.Y01(p.Db) * plotH);
                    if (first) { c.BeginFigure(pt, false); first = false; } else c.LineTo(pt);
                }
                c.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(LineBrush, 2), line);

            DrawHandle(context, w, plotH, LowHz, LowGain, LowBandBrush, "L");
            DrawHandle(context, w, plotH, MidHz, MidGain, MidBandBrush, "M");
            DrawHandle(context, w, plotH, HighHz, HighGain, HighBandBrush, "H");
        }
    }

    private void DrawHandle(DrawingContext ctx, double w, double plotH, double hz, int gain, IBrush brush, string tag)
    {
        var p = new Point(EqCurve.XFor(hz) * w, EqCurve.Y01(gain) * plotH);
        ctx.DrawEllipse(brush, new Pen(Brushes.Black, 1), p, HandleRadius, HandleRadius);
        var ft = new FormattedText(tag, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 9, Brushes.Black);
        ctx.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
    }

    private void DrawGrid(DrawingContext ctx, double w, double plotH)
    {
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var culture = CultureInfo.CurrentCulture;

        // Horizontal gain lines every 5 dB over the hardware range, labelled at the left.
        for (var db = -GainLimit; db <= GainLimit; db += 5)
        {
            var y = EqCurve.Y01(db) * plotH;
            ctx.DrawLine(db == 0 ? axisPen : gridPen, new Point(0, y), new Point(w, y));
            var ft = new FormattedText($"{db:+#;-#;0}", culture, FlowDirection.LeftToRight,
                Typeface.Default, 9, AxisBrush);
            ctx.DrawText(ft, new Point(2, y - ft.Height - 1));
        }

        // Vertical frequency lines at the usual decade/half-decade points, labelled below the plot.
        foreach (var hz in LabelledFrequencies)
        {
            var x = EqCurve.XFor(hz) * w;
            ctx.DrawLine(gridPen, new Point(x, 0), new Point(x, plotH));
            var text = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
            var ft = new FormattedText(text, culture, FlowDirection.LeftToRight, Typeface.Default, 9, AxisBrush);
            ctx.DrawText(ft, new Point(Math.Min(x + 2, w - ft.Width), plotH + 1));
        }

        ctx.DrawLine(axisPen, new Point(0, 0), new Point(0, plotH));
        ctx.DrawLine(axisPen, new Point(0, plotH), new Point(w, plotH));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var band = BandAt(e.GetPosition(this));
        if (band < 0) return;
        Focus();

        // Double-click flattens the band it lands on — the quickest way back to neutral.
        if (e.ClickCount >= 2)
        {
            SetGain(band, 0);
            e.Handled = true;
            return;
        }

        _dragBand = band;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragBand < 0) return;
        Apply(_dragBand, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragBand < 0) return;
        _dragBand = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private int BandAt(Point pos)
    {
        double w = Bounds.Width, plotH = Bounds.Height - LabelStripHeight;
        if (w <= 0 || plotH <= 0) return -1;
        return EqCurve.NearestBand(pos.X / w, pos.Y / plotH, Bands, GrabDistance);
    }

    private void Apply(int band, Point pos)
    {
        double w = Bounds.Width, plotH = Bounds.Height - LabelStripHeight;
        if (w <= 0 || plotH <= 0) return;
        SetHz(band, EqCurve.HzAt(pos.X / w));
        SetGain(band, (int)Math.Round(Math.Clamp(EqCurve.DbAtY01(pos.Y / plotH), -GainLimit, GainLimit)));
    }

    private void SetHz(int band, double hz)
    {
        switch (band)
        {
            case 0: LowHz = hz; break;
            case 1: MidHz = hz; break;
            case 2: HighHz = hz; break;
        }
    }

    private void SetGain(int band, int gain)
    {
        switch (band)
        {
            case 0: LowGain = gain; break;
            case 1: MidGain = gain; break;
            case 2: HighGain = gain; break;
        }
    }
}
