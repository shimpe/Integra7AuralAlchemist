using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Draggable ADSR envelope graph. A/D/S/R are 0..127 StyledProperties (TwoWay by default) so a VM
/// can bind them to throttled parameter wrappers. Pointer drag and arrow-key edits adjust the
/// focused handle; the View also provides numeric inputs for full keyboard accessibility.
/// All value↔pixel math lives in <see cref="SnsEnvelopeMapping"/>.
/// </summary>
public class AdsrEnvelopeControl : Control
{
    private const double HandleRadius = 7;
    private const double SustainWidth = 40;

    public static readonly StyledProperty<int> AttackProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Attack), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> DecayProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Decay), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> SustainProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Sustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> ReleaseProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(nameof(Release), 0, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>When true, render as a non-interactive preview: dimmer line/fill and no draggable handles.</summary>
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, bool>(nameof(Preview));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, IBrush>(nameof(FillBrush),
            new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));

    public int Attack { get => GetValue(AttackProperty); set => SetValue(AttackProperty, value); }
    public int Decay { get => GetValue(DecayProperty); set => SetValue(DecayProperty, value); }
    public int Sustain { get => GetValue(SustainProperty); set => SetValue(SustainProperty, value); }
    public int Release { get => GetValue(ReleaseProperty); set => SetValue(ReleaseProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    private enum Handle { None, Attack, Decay, Release }
    private Handle _active = Handle.None;
    private Handle _focused = Handle.Attack;

    private static readonly IBrush Bg = new SolidColorBrush(Color.Parse("#1b1f22"));
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)), 1);
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)), 1);
    private static readonly IPen FocusPen = new Pen(Brushes.Orange, 2);

    // Axis ticks every 16 parameter units (0..127).
    private const int TickStep = 16;

    // Preview (mini card) palette: dimmer so it reads as a non-editable thumbnail, not a control.
    private static readonly IBrush PreviewBg = new SolidColorBrush(Color.Parse("#15181a"));

    static AdsrEnvelopeControl()
    {
        AffectsRender<AdsrEnvelopeControl>(AttackProperty, DecayProperty, SustainProperty, ReleaseProperty,
            PreviewProperty, LineBrushProperty, FillBrushProperty);
        FocusableProperty.OverrideDefaultValue<AdsrEnvelopeControl>(true);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var preview = Preview;
        context.FillRectangle(preview ? PreviewBg : Bg, new Rect(0, 0, w, h));
        if (preview)
            context.DrawLine(GridPen, new Point(0, h - 1), new Point(w, h - 1));
        else
            EnvelopeAxes.Draw(context, w, h, SustainWidth, GridPen, AxisPen);

        var p = SnsEnvelopeMapping.ComputePoints(Attack, Decay, Sustain, Release, w, h, SustainWidth);

        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(p.Start.X, p.Start.Y), true);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
            c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
            c.LineTo(new Point(p.End.X, p.End.Y));
            c.LineTo(new Point(p.End.X, h));
            c.LineTo(new Point(p.Start.X, h));
            c.EndFigure(true);
        }
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
            c.LineTo(new Point(p.Peak.X, p.Peak.Y));
            c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
            c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
            c.LineTo(new Point(p.End.X, p.End.Y));
            c.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, preview ? 1 : 2), line);

        if (preview) return; // previews show no draggable handles

        DrawHandle(context, p.Peak, _focused == Handle.Attack);
        DrawHandle(context, p.SustainStart, _focused == Handle.Decay);
        DrawHandle(context, p.End, _focused == Handle.Release);
    }

    private static void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? FocusPen : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var p = SnsEnvelopeMapping.ComputePoints(Attack, Decay, Sustain, Release, Bounds.Width, Bounds.Height, SustainWidth);
        _active = Nearest(pos, p);
        if (_active != Handle.None)
        {
            _focused = _active;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_active == Handle.None) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.SegmentMax(Bounds.Width, SustainWidth);
        switch (_active)
        {
            case Handle.Attack:
                Attack = SnsEnvelopeMapping.AttackFromX(pos.X, seg);
                break;
            case Handle.Decay:
                var aW = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
                Decay = SnsEnvelopeMapping.DecayFromX(pos.X, aW, seg);
                Sustain = SnsEnvelopeMapping.LevelFromY(pos.Y, Bounds.Height);
                break;
            case Handle.Release:
                var aW2 = SnsEnvelopeMapping.TimeToWidth(Attack, seg);
                var dW2 = SnsEnvelopeMapping.TimeToWidth(Decay, seg);
                Release = SnsEnvelopeMapping.ReleaseFromX(pos.X, aW2, dW2, SustainWidth, seg);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_active == Handle.None) return;
        e.Pointer.Capture(null);
        _active = Handle.None;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Tab:
                // Cycle Attack->Decay->Release on Tab, but let focus LEAVE the control at the end
                // (and on Shift+Tab) so keyboard users are never trapped on the graph. The numeric
                // up/down inputs next to the graph already provide full keyboard editing.
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _focused == Handle.Release)
                    break; // not handled -> focus traverses out normally
                _focused = _focused == Handle.Attack ? Handle.Decay : Handle.Release;
                e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        switch (_focused)
        {
            case Handle.Attack: if (!vertical) Attack = SnsEnvelopeMapping.Clamp(Attack + delta); break;
            case Handle.Decay:
                if (vertical) Sustain = SnsEnvelopeMapping.Clamp(Sustain + delta);
                else Decay = SnsEnvelopeMapping.Clamp(Decay + delta);
                break;
            case Handle.Release: if (!vertical) Release = SnsEnvelopeMapping.Clamp(Release + delta); break;
        }
    }

    private static Handle Nearest(Point pos, SnsEnvelopeMapping.EnvPoints p)
    {
        var best = Handle.None;
        var bestD = HandleRadius * HandleRadius * 4;
        Check(Handle.Attack, p.Peak);
        Check(Handle.Decay, p.SustainStart);
        Check(Handle.Release, p.End);
        return best;

        void Check(Handle h, SnsEnvelopeMapping.Point pt)
        {
            var dx = pos.X - pt.X; var dy = pos.Y - pt.Y; var d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = h; }
        }
    }
}
