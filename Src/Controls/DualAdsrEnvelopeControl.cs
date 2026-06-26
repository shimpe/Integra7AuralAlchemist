using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Two ADSR envelopes (Amp + Filter) overlaid on ONE shared axis (same time/level mapping), so
/// their sonic interaction is visible. Both are draggable; the active envelope is drawn on top
/// and edited by the keyboard. A drag grabs the nearest handle of either envelope.
/// </summary>
public class DualAdsrEnvelopeControl : Control
{
    private const double HandleRadius = 7;
    private const double SustainWidth = 40;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> AmpAttackProperty = I(nameof(AmpAttack));
    public static readonly StyledProperty<int> AmpDecayProperty = I(nameof(AmpDecay));
    public static readonly StyledProperty<int> AmpSustainProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(AmpSustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> AmpReleaseProperty = I(nameof(AmpRelease));
    public static readonly StyledProperty<int> FilterAttackProperty = I(nameof(FilterAttack));
    public static readonly StyledProperty<int> FilterDecayProperty = I(nameof(FilterDecay));
    public static readonly StyledProperty<int> FilterSustainProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(FilterSustain), 127, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> FilterReleaseProperty = I(nameof(FilterRelease));

    /// <summary>0 = Amp (front/keyboard), 1 = Filter.</summary>
    public static readonly StyledProperty<int> ActiveEnvelopeProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, int>(nameof(ActiveEnvelope), 0, defaultBindingMode: BindingMode.TwoWay);

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> AmpLineBrushProperty = B(nameof(AmpLineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> AmpFillBrushProperty = B(nameof(AmpFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
    public static readonly StyledProperty<IBrush> FilterLineBrushProperty = B(nameof(FilterLineBrush), new SolidColorBrush(Color.Parse("#E0A23D")));
    public static readonly StyledProperty<IBrush> FilterFillBrushProperty = B(nameof(FilterFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xb0, 0x7a, 0x1e)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> HandleBrushProperty = B(nameof(HandleBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> FocusBrushProperty = B(nameof(FocusBrush), Brushes.Orange);

    /// <summary>When true, render as a non-interactive preview: both envelopes shown equally,
    /// no handles, no active/inactive dimming.</summary>
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<DualAdsrEnvelopeControl, bool>(nameof(Preview));

    public int AmpAttack { get => GetValue(AmpAttackProperty); set => SetValue(AmpAttackProperty, value); }
    public int AmpDecay { get => GetValue(AmpDecayProperty); set => SetValue(AmpDecayProperty, value); }
    public int AmpSustain { get => GetValue(AmpSustainProperty); set => SetValue(AmpSustainProperty, value); }
    public int AmpRelease { get => GetValue(AmpReleaseProperty); set => SetValue(AmpReleaseProperty, value); }
    public int FilterAttack { get => GetValue(FilterAttackProperty); set => SetValue(FilterAttackProperty, value); }
    public int FilterDecay { get => GetValue(FilterDecayProperty); set => SetValue(FilterDecayProperty, value); }
    public int FilterSustain { get => GetValue(FilterSustainProperty); set => SetValue(FilterSustainProperty, value); }
    public int FilterRelease { get => GetValue(FilterReleaseProperty); set => SetValue(FilterReleaseProperty, value); }
    public int ActiveEnvelope { get => GetValue(ActiveEnvelopeProperty); set => SetValue(ActiveEnvelopeProperty, value); }
    public IBrush AmpLineBrush { get => GetValue(AmpLineBrushProperty); set => SetValue(AmpLineBrushProperty, value); }
    public IBrush AmpFillBrush { get => GetValue(AmpFillBrushProperty); set => SetValue(AmpFillBrushProperty, value); }
    public IBrush FilterLineBrush { get => GetValue(FilterLineBrushProperty); set => SetValue(FilterLineBrushProperty, value); }
    public IBrush FilterFillBrush { get => GetValue(FilterFillBrushProperty); set => SetValue(FilterFillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public IBrush FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }

    // 0=Attack,1=Decay,2=Release. _dragEnv/_dragHandle while dragging; _focusedHandle for keyboard.
    private int _dragEnv = -1, _dragHandle = -1, _focusedHandle = 0;

    static DualAdsrEnvelopeControl()
    {
        AffectsRender<DualAdsrEnvelopeControl>(
            AmpAttackProperty, AmpDecayProperty, AmpSustainProperty, AmpReleaseProperty,
            FilterAttackProperty, FilterDecayProperty, FilterSustainProperty, FilterReleaseProperty,
            ActiveEnvelopeProperty, AmpLineBrushProperty, AmpFillBrushProperty,
            FilterLineBrushProperty, FilterFillBrushProperty, BackgroundBrushProperty,
            GridBrushProperty, AxisBrushProperty, HandleBrushProperty, FocusBrushProperty, PreviewProperty);
        FocusableProperty.OverrideDefaultValue<DualAdsrEnvelopeControl>(true);
    }

    private SnsEnvelopeMapping.EnvPoints AmpPoints(double w, double h) =>
        SnsEnvelopeMapping.ComputePoints(AmpAttack, AmpDecay, AmpSustain, AmpRelease, w, h, SustainWidth);
    private SnsEnvelopeMapping.EnvPoints FilterPoints(double w, double h) =>
        SnsEnvelopeMapping.ComputePoints(FilterAttack, FilterDecay, FilterSustain, FilterRelease, w, h, SustainWidth);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));
        EnvelopeAxes.Draw(context, w, h, SustainWidth, new Pen(GridBrush), new Pen(AxisBrush));

        if (Preview)
        {
            // Both envelopes shown equally, no handles (the card thumbnail).
            DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h, 0.9, 1.5, drawHandles: false);
            DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h, 0.9, 1.5, drawHandles: false);
            return;
        }

        // Inactive first (dim), then active on top.
        if (ActiveEnvelope == 0)
        {
            DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h, 0.4, 1.5, drawHandles: false);
            DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h, 1.0, 2, drawHandles: true);
        }
        else
        {
            DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h, 0.4, 1.5, drawHandles: false);
            DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h, 1.0, 2, drawHandles: true);
        }
    }

    private void DrawEnvelope(DrawingContext context, SnsEnvelopeMapping.EnvPoints p, IBrush line, IBrush fill,
        double h, double opacity, double lineWidth, bool drawHandles)
    {
        using (context.PushOpacity(opacity))
        {
            var fillGeo = new StreamGeometry();
            using (var c = fillGeo.Open())
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
            context.DrawGeometry(fill, null, fillGeo);

            var lineGeo = new StreamGeometry();
            using (var c = lineGeo.Open())
            {
                c.BeginFigure(new Point(p.Start.X, p.Start.Y), false);
                c.LineTo(new Point(p.Peak.X, p.Peak.Y));
                c.LineTo(new Point(p.SustainStart.X, p.SustainStart.Y));
                c.LineTo(new Point(p.SustainEnd.X, p.SustainEnd.Y));
                c.LineTo(new Point(p.End.X, p.End.Y));
                c.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(line, lineWidth), lineGeo);

            if (!drawHandles) return;
            DrawHandle(context, p.Peak, _focusedHandle == 0);
            DrawHandle(context, p.SustainStart, _focusedHandle == 1);
            DrawHandle(context, p.End, _focusedHandle == 2);
        }
    }

    private void DrawHandle(DrawingContext ctx, SnsEnvelopeMapping.Point pt, bool focused)
        => ctx.DrawEllipse(HandleBrush, focused ? new Pen(FocusBrush, 2) : null, new Point(pt.X, pt.Y), HandleRadius, HandleRadius);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return; // previews are non-interactive
        Focus();
        var pos = e.GetPosition(this);
        var hit = SnsEnvelopeMapping.NearestHandle(pos.X, pos.Y, AmpPoints(Bounds.Width, Bounds.Height),
            FilterPoints(Bounds.Width, Bounds.Height), ActiveEnvelope, HandleRadius * 2);
        if (hit.Env < 0) return;
        if (hit.Env != ActiveEnvelope) ActiveEnvelope = hit.Env; // grabbing the other env activates it
        _dragEnv = hit.Env;
        _dragHandle = hit.Handle;
        _focusedHandle = hit.Handle;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragEnv < 0) return;
        var pos = e.GetPosition(this);
        var seg = SnsEnvelopeMapping.SegmentMax(Bounds.Width, SustainWidth);
        var (a, d) = _dragEnv == 0 ? (AmpAttack, AmpDecay) : (FilterAttack, FilterDecay);
        switch (_dragHandle)
        {
            case 0: SetVal(0, SnsEnvelopeMapping.AttackFromX(pos.X, seg)); break;
            case 1:
                var aW = SnsEnvelopeMapping.TimeToWidth(a, seg);
                SetVal(1, SnsEnvelopeMapping.DecayFromX(pos.X, aW, seg));
                SetVal(2, SnsEnvelopeMapping.LevelFromY(pos.Y, Bounds.Height));
                break;
            case 2: // Release handle
                var aW2 = SnsEnvelopeMapping.TimeToWidth(a, seg);
                var dW2 = SnsEnvelopeMapping.TimeToWidth(d, seg);
                SetVal(3, SnsEnvelopeMapping.ReleaseFromX(pos.X, aW2, dW2, SustainWidth, seg));
                break;
        }
        e.Handled = true;

        // map slot (0=Attack,1=Decay,2=Sustain,3=Release) to the active drag-env's setters
        void SetVal(int slot, int value)
        {
            if (_dragEnv == 0)
                switch (slot) { case 0: AmpAttack = value; break; case 1: AmpDecay = value; break; case 2: AmpSustain = value; break; case 3: AmpRelease = value; break; }
            else
                switch (slot) { case 0: FilterAttack = value; break; case 1: FilterDecay = value; break; case 2: FilterSustain = value; break; case 3: FilterRelease = value; break; }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragEnv < 0) return;
        e.Pointer.Capture(null);
        _dragEnv = -1; _dragHandle = -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Preview) return; // previews are non-interactive
        switch (e.Key)
        {
            case Key.Tab:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _focusedHandle == 2) break;
                _focusedHandle++; e.Handled = true; InvalidateVisual(); break;
            case Key.Left: Adjust(-1, vertical: false); e.Handled = true; break;
            case Key.Right: Adjust(+1, vertical: false); e.Handled = true; break;
            case Key.Up: Adjust(+1, vertical: true); e.Handled = true; break;
            case Key.Down: Adjust(-1, vertical: true); e.Handled = true; break;
        }
    }

    private void Adjust(int delta, bool vertical)
    {
        var amp = ActiveEnvelope == 0;
        switch (_focusedHandle)
        {
            case 0:
                if (!vertical) { if (amp) AmpAttack = SnsEnvelopeMapping.Clamp(AmpAttack + delta); else FilterAttack = SnsEnvelopeMapping.Clamp(FilterAttack + delta); }
                break;
            case 1:
                if (vertical) { if (amp) AmpSustain = SnsEnvelopeMapping.Clamp(AmpSustain + delta); else FilterSustain = SnsEnvelopeMapping.Clamp(FilterSustain + delta); }
                else { if (amp) AmpDecay = SnsEnvelopeMapping.Clamp(AmpDecay + delta); else FilterDecay = SnsEnvelopeMapping.Clamp(FilterDecay + delta); }
                break;
            case 2:
                if (!vertical) { if (amp) AmpRelease = SnsEnvelopeMapping.Clamp(AmpRelease + delta); else FilterRelease = SnsEnvelopeMapping.Clamp(FilterRelease + delta); }
                break;
        }
    }
}
