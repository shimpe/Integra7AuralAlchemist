using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Two 4-segment rate/level envelopes (Amp + Filter) overlaid on ONE shared axis as a non-interactive
/// preview thumbnail (the PCM-Synth partial card). The multi-stage analogue of
/// <see cref="DualAdsrEnvelopeControl"/>; pure geometry is delegated to <see cref="PcmEnvelopeMapping"/>.
/// </summary>
public class DualMultiStageEnvelopeControl : Control
{
    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<DualMultiStageEnvelopeControl, int>(name);

    public static readonly StyledProperty<int> AmpTime1Property = I(nameof(AmpTime1));
    public static readonly StyledProperty<int> AmpTime2Property = I(nameof(AmpTime2));
    public static readonly StyledProperty<int> AmpTime3Property = I(nameof(AmpTime3));
    public static readonly StyledProperty<int> AmpTime4Property = I(nameof(AmpTime4));
    public static readonly StyledProperty<int> AmpLevel0Property = I(nameof(AmpLevel0));
    public static readonly StyledProperty<int> AmpLevel1Property = I(nameof(AmpLevel1));
    public static readonly StyledProperty<int> AmpLevel2Property = I(nameof(AmpLevel2));
    public static readonly StyledProperty<int> AmpLevel3Property = I(nameof(AmpLevel3));
    public static readonly StyledProperty<int> AmpLevel4Property = I(nameof(AmpLevel4));
    public static readonly StyledProperty<int> FilterTime1Property = I(nameof(FilterTime1));
    public static readonly StyledProperty<int> FilterTime2Property = I(nameof(FilterTime2));
    public static readonly StyledProperty<int> FilterTime3Property = I(nameof(FilterTime3));
    public static readonly StyledProperty<int> FilterTime4Property = I(nameof(FilterTime4));
    public static readonly StyledProperty<int> FilterLevel0Property = I(nameof(FilterLevel0));
    public static readonly StyledProperty<int> FilterLevel1Property = I(nameof(FilterLevel1));
    public static readonly StyledProperty<int> FilterLevel2Property = I(nameof(FilterLevel2));
    public static readonly StyledProperty<int> FilterLevel3Property = I(nameof(FilterLevel3));
    public static readonly StyledProperty<int> FilterLevel4Property = I(nameof(FilterLevel4));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<DualMultiStageEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> AmpLineBrushProperty = B(nameof(AmpLineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> AmpFillBrushProperty = B(nameof(AmpFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
    public static readonly StyledProperty<IBrush> FilterLineBrushProperty = B(nameof(FilterLineBrush), new SolidColorBrush(Color.Parse("#E0A23D")));
    public static readonly StyledProperty<IBrush> FilterFillBrushProperty = B(nameof(FilterFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xb0, 0x7a, 0x1e)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public int AmpTime1 { get => GetValue(AmpTime1Property); set => SetValue(AmpTime1Property, value); }
    public int AmpTime2 { get => GetValue(AmpTime2Property); set => SetValue(AmpTime2Property, value); }
    public int AmpTime3 { get => GetValue(AmpTime3Property); set => SetValue(AmpTime3Property, value); }
    public int AmpTime4 { get => GetValue(AmpTime4Property); set => SetValue(AmpTime4Property, value); }
    public int AmpLevel0 { get => GetValue(AmpLevel0Property); set => SetValue(AmpLevel0Property, value); }
    public int AmpLevel1 { get => GetValue(AmpLevel1Property); set => SetValue(AmpLevel1Property, value); }
    public int AmpLevel2 { get => GetValue(AmpLevel2Property); set => SetValue(AmpLevel2Property, value); }
    public int AmpLevel3 { get => GetValue(AmpLevel3Property); set => SetValue(AmpLevel3Property, value); }
    public int AmpLevel4 { get => GetValue(AmpLevel4Property); set => SetValue(AmpLevel4Property, value); }
    public int FilterTime1 { get => GetValue(FilterTime1Property); set => SetValue(FilterTime1Property, value); }
    public int FilterTime2 { get => GetValue(FilterTime2Property); set => SetValue(FilterTime2Property, value); }
    public int FilterTime3 { get => GetValue(FilterTime3Property); set => SetValue(FilterTime3Property, value); }
    public int FilterTime4 { get => GetValue(FilterTime4Property); set => SetValue(FilterTime4Property, value); }
    public int FilterLevel0 { get => GetValue(FilterLevel0Property); set => SetValue(FilterLevel0Property, value); }
    public int FilterLevel1 { get => GetValue(FilterLevel1Property); set => SetValue(FilterLevel1Property, value); }
    public int FilterLevel2 { get => GetValue(FilterLevel2Property); set => SetValue(FilterLevel2Property, value); }
    public int FilterLevel3 { get => GetValue(FilterLevel3Property); set => SetValue(FilterLevel3Property, value); }
    public int FilterLevel4 { get => GetValue(FilterLevel4Property); set => SetValue(FilterLevel4Property, value); }
    public IBrush AmpLineBrush { get => GetValue(AmpLineBrushProperty); set => SetValue(AmpLineBrushProperty, value); }
    public IBrush AmpFillBrush { get => GetValue(AmpFillBrushProperty); set => SetValue(AmpFillBrushProperty, value); }
    public IBrush FilterLineBrush { get => GetValue(FilterLineBrushProperty); set => SetValue(FilterLineBrushProperty, value); }
    public IBrush FilterFillBrush { get => GetValue(FilterFillBrushProperty); set => SetValue(FilterFillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    static DualMultiStageEnvelopeControl()
    {
        AffectsRender<DualMultiStageEnvelopeControl>(
            AmpTime1Property, AmpTime2Property, AmpTime3Property, AmpTime4Property,
            AmpLevel0Property, AmpLevel1Property, AmpLevel2Property, AmpLevel3Property, AmpLevel4Property,
            FilterTime1Property, FilterTime2Property, FilterTime3Property, FilterTime4Property,
            FilterLevel0Property, FilterLevel1Property, FilterLevel2Property, FilterLevel3Property, FilterLevel4Property,
            AmpLineBrushProperty, AmpFillBrushProperty, FilterLineBrushProperty, FilterFillBrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty);
    }

    private PcmEnvelopeMapping.Point[] AmpPoints(double w, double h) =>
        PcmEnvelopeMapping.ComputePoints(AmpTime1, AmpTime2, AmpTime3, AmpTime4,
            AmpLevel0, AmpLevel1, AmpLevel2, AmpLevel3, AmpLevel4, w, h, false);

    private PcmEnvelopeMapping.Point[] FilterPoints(double w, double h) =>
        PcmEnvelopeMapping.ComputePoints(FilterTime1, FilterTime2, FilterTime3, FilterTime4,
            FilterLevel0, FilterLevel1, FilterLevel2, FilterLevel3, FilterLevel4, w, h, false);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        // Grid + axes, matching MultiStageEnvelopeControl (unipolar, a line every 16 levels).
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        for (var level = 0; level <= PcmEnvelopeMapping.Max; level += 16)
        {
            var y = PcmEnvelopeMapping.LevelToY(level, h, false);
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            context.DrawLine(axisPen, new Point(0, y), new Point(6, y));
        }
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        context.DrawLine(axisPen, new Point(0, h), new Point(w, h));

        // Amp behind, filter in front — same z-order as the SN-S DualAdsrEnvelopeControl preview, so the
        // two editors' overlays look identical. Both fills are semi-transparent, so both stay visible.
        DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h);
        DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h);
    }

    private static void DrawEnvelope(DrawingContext ctx, PcmEnvelopeMapping.Point[] p, IBrush line, IBrush fill, double h)
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
        ctx.DrawGeometry(fill, null, fillGeo);

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
        ctx.DrawGeometry(null, new Pen(line, 1.5), lineGeo);
    }
}
