using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Bipolar Attack/Decay pitch envelope. Center line = no pitch change; up = sharp, down = flat.
/// Drag the peak handle (X = Attack, Y = Depth) or the decay handle (X = Decay). Arrow keys + a
/// non-interactive Preview mode. Math is in <see cref="SnsEnvelopeMapping"/>.
/// </summary>
public class PitchEnvelopeControl : Control
{
    private const double HandleRadius = 7;

    public static readonly StyledProperty<int> AttackProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Attack), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DecayProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Decay), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, int>(nameof(Depth), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<PitchEnvelopeControl, bool>(nameof(Preview));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<PitchEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> LineBrushProperty = B(nameof(LineBrush), new SolidColorBrush(Color.Parse("#9C7BE0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty = B(nameof(FillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x6a, 0x4f, 0xb0)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> FocusBrushProperty = B(nameof(FocusBrush), Brushes.Orange);

    public int Attack { get => GetValue(AttackProperty); set => SetValue(AttackProperty, value); }
    public int Decay { get => GetValue(DecayProperty); set => SetValue(DecayProperty, value); }
    public int Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public IBrush FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    private int _drag = -1, _focused = 0; // 0 = peak (attack/depth), 1 = decay

    static PitchEnvelopeControl()
    {
        AffectsRender<PitchEnvelopeControl>(AttackProperty, DecayProperty, DepthProperty, PreviewProperty,
            LineBrushProperty, FillBrushProperty, BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty,
            HandleBrushProperty, FocusBrushProperty);
        FocusableProperty.OverrideDefaultValue<PitchEnvelopeControl>(true);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var cy = h / 2.0;
        context.DrawLine(gridPen, new Point(0, h * 0.25), new Point(w, h * 0.25));
        context.DrawLine(gridPen, new Point(0, h * 0.75), new Point(w, h * 0.75));
        context.DrawLine(axisPen, new Point(0, cy), new Point(w, cy)); // center = no pitch change
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));

        var p = SnsEnvelopeMapping.ComputePitchPoints(Attack, Decay, Depth, w, h);

        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(p.Start.X, cy), true);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.End.X, cy));
            c.EndFigure(true);
        }
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.End.X, p.End.Y));
            c.LineTo(new Point(w, cy));
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2), line);

        if (Preview) return;
        DrawHandle(context, p.Peak, _focused == 0);
        DrawHandle(context, p.End, _focused == 1);
    }

    private void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? new Pen(FocusBrush, 2) : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return;
        Focus();
        var pos = e.GetPosition(this);
        _drag = Nearest(pos, SnsEnvelopeMapping.ComputePitchPoints(Attack, Decay, Depth, Bounds.Width, Bounds.Height));
        if (_drag < 0) return;
        _focused = _drag;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.PitchSegmentMax(Bounds.Width);
        if (_drag == 0)
        {
            Attack = SnsEnvelopeMapping.PitchAttackFromX(pos.X, seg);
            Depth = SnsEnvelopeMapping.BipolarFromY(pos.Y, Bounds.Height);
        }
        else
        {
            var aW = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
            Decay = SnsEnvelopeMapping.PitchDecayFromX(pos.X, aW, seg);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        e.Pointer.Capture(null);
        _drag = -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Preview) return;
        switch (e.Key)
        {
            case Key.Tab:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _focused == 1) break;
                _focused = 1; e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        if (_focused == 0)
        {
            if (vertical) Depth = SnsEnvelopeMapping.ClampBipolar(Depth + delta);
            else Attack = SnsEnvelopeMapping.Clamp(Attack + delta);
        }
        else if (!vertical)
        {
            Decay = SnsEnvelopeMapping.Clamp(Decay + delta);
        }
    }

    private static int Nearest(Point pos, SnsEnvelopeMapping.PitchPoints p)
    {
        var best = -1;
        var bestD = HandleRadius * HandleRadius * 4;
        Check(0, p.Peak);
        Check(1, p.End);
        return best;

        void Check(int i, SnsEnvelopeMapping.Point pt)
        {
            var dx = pos.X - pt.X; var dy = pos.Y - pt.Y; var d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
    }
}
