using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The dial half of the rotary knob: an owner-drawn arc with a colored value strip and a
/// pointer, changed by dragging up and down. It carries no text -- <see cref="RotaryKnob"/> wraps it
/// with the editable readout. Follows the other owner-drawn controls here (the envelope panels,
/// LfoWaveformControl): styled properties with AffectsRender, a Render override, pointer handlers that
/// move Value.
///
/// The value is kept integer-rounded because every friendly parameter is an integer MIDI value.</summary>
public class RotaryKnobDial : Control
{
    private const double ArcThickness = 5.0;
    private const double Gap = 4.0;

    // Dragging this many pixels vertically covers the whole range; Shift divides the step for fine work.
    private const double PixelsForFullRange = 200.0;
    private const double FineFactor = 0.2;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RotaryKnobDial, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RotaryKnobDial, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RotaryKnobDial, double>(nameof(Maximum), 127);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<RotaryKnobDial, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        B(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        B(nameof(TrackBrush), new SolidColorBrush(Color.FromArgb(0x33, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> KnobFillBrushProperty =
        B(nameof(KnobFillBrush), new SolidColorBrush(Color.Parse("#2A2F34")));
    public static readonly StyledProperty<IBrush> PointerBrushProperty =
        B(nameof(PointerBrush), new SolidColorBrush(Colors.White));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush KnobFillBrush { get => GetValue(KnobFillBrushProperty); set => SetValue(KnobFillBrushProperty, value); }
    public IBrush PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }

    private bool _dragging;
    private double _dragStartY;
    private double _dragStartValue;

    static RotaryKnobDial()
    {
        AffectsRender<RotaryKnobDial>(ValueProperty, MinimumProperty, MaximumProperty,
            AccentBrushProperty, TrackBrushProperty, KnobFillBrushProperty, PointerBrushProperty);
    }

    public RotaryKnobDial()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    private void Commit(double raw)
    {
        var clamped = Math.Clamp(Math.Round(raw), Minimum, Maximum);
        if (Math.Abs(clamped - Value) > double.Epsilon) Value = clamped;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            // Reset: bipolar parameters reset to their centre, unipolar ones to their floor.
            Commit(KnobGeometry.IsBipolar(Minimum, Maximum) ? 0 : Minimum);
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragStartY = e.GetPosition(this).Y;
        _dragStartValue = Value;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        var dy = _dragStartY - e.GetPosition(this).Y;   // up is positive
        var perPixel = (Maximum - Minimum) / PixelsForFullRange;
        var fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? FineFactor : 1.0;
        Commit(_dragStartValue + dy * perPixel * fine);
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        Commit(Value + Math.Sign(e.Delta.Y) * step);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var cx = w / 2;
        var cy = h / 2;
        var radius = Math.Min(w, h) / 2 - ArcThickness / 2;
        if (radius <= 0) return;

        // The dim full-sweep track, then the accent value arc on top of it.
        context.DrawGeometry(null, new Pen(TrackBrush, ArcThickness, lineCap: PenLineCap.Round),
            ArcGeometry(0, 1, cx, cy, radius));

        var (from, to) = KnobGeometry.FillRange(Value, Minimum, Maximum);
        if (to - from > 1e-6)
            context.DrawGeometry(null, new Pen(AccentBrush, ArcThickness, lineCap: PenLineCap.Round),
                ArcGeometry(from, to, cx, cy, radius));

        // The knob body sits inside the arc.
        var bodyRadius = radius - ArcThickness / 2 - Gap;
        if (bodyRadius > 0)
        {
            context.DrawEllipse(KnobFillBrush, new Pen(TrackBrush, 1), new Point(cx, cy),
                bodyRadius, bodyRadius);

            var t = KnobGeometry.ValueFraction(Value, Minimum, Maximum);
            var (px, py) = KnobGeometry.PointFor(t, cx, cy, bodyRadius);
            // The pointer runs from a little out of the centre to the rim, so the middle stays clean.
            var (ix, iy) = KnobGeometry.PointFor(t, cx, cy, bodyRadius * 0.35);
            context.DrawLine(new Pen(PointerBrush, 2, lineCap: PenLineCap.Round),
                new Point(ix, iy), new Point(px, py));
        }
    }

    private static StreamGeometry ArcGeometry(double from, double to, double cx, double cy, double radius)
    {
        const int fullSweepSegments = 96;
        var segments = Math.Max(1, (int)Math.Ceiling((to - from) * fullSweepSegments));
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        for (var i = 0; i <= segments; i++)
        {
            var f = from + (to - from) * i / segments;
            var (x, y) = KnobGeometry.PointFor(f, cx, cy, radius);
            var pt = new Point(x, y);
            if (i == 0) ctx.BeginFigure(pt, false);
            else ctx.LineTo(pt);
        }
        ctx.EndFigure(false);
        return geo;
    }
}
