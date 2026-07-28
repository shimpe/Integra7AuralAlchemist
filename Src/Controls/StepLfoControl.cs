using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The PCM Synth partial's sixteen LFO steps, as bars either side of a centre line.
///
/// <b>One stroke draws a sequence.</b> Pressing sets the bar under the pointer and dragging sets every bar
/// it passes, including ones a fast movement skipped over -- which is how a step sequencer behaves
/// everywhere else, and the only reason to build a control rather than put sixteen knobs in a row.
///
/// Sixteen separate styled properties rather than a list: it is what every other editor here does (see
/// <c>PmtZoneEditorControl</c>, which has thirty-two), the bindings are compiled and therefore checked at
/// build time, and a list would need change notification per element to be two-way at all.
///
/// The arithmetic is in <see cref="StepLfoGeometry"/>.</summary>
public class StepLfoControl : Control
{
    private const int Steps = 16;
    private const int MinValue = -36;
    private const int MaxValue = 36;

    private static StyledProperty<int> S(string name) =>
        AvaloniaProperty.Register<StepLfoControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Step1Property = S(nameof(Step1));
    public static readonly StyledProperty<int> Step2Property = S(nameof(Step2));
    public static readonly StyledProperty<int> Step3Property = S(nameof(Step3));
    public static readonly StyledProperty<int> Step4Property = S(nameof(Step4));
    public static readonly StyledProperty<int> Step5Property = S(nameof(Step5));
    public static readonly StyledProperty<int> Step6Property = S(nameof(Step6));
    public static readonly StyledProperty<int> Step7Property = S(nameof(Step7));
    public static readonly StyledProperty<int> Step8Property = S(nameof(Step8));
    public static readonly StyledProperty<int> Step9Property = S(nameof(Step9));
    public static readonly StyledProperty<int> Step10Property = S(nameof(Step10));
    public static readonly StyledProperty<int> Step11Property = S(nameof(Step11));
    public static readonly StyledProperty<int> Step12Property = S(nameof(Step12));
    public static readonly StyledProperty<int> Step13Property = S(nameof(Step13));
    public static readonly StyledProperty<int> Step14Property = S(nameof(Step14));
    public static readonly StyledProperty<int> Step15Property = S(nameof(Step15));
    public static readonly StyledProperty<int> Step16Property = S(nameof(Step16));

    /// <summary>The sixteen in order, so the pointer handlers can address a step by index instead of a
    /// sixteen-armed switch.</summary>
    private static readonly StyledProperty<int>[] StepProperties =
    [
        Step1Property, Step2Property, Step3Property, Step4Property,
        Step5Property, Step6Property, Step7Property, Step8Property,
        Step9Property, Step10Property, Step11Property, Step12Property,
        Step13Property, Step14Property, Step15Property, Step16Property,
    ];

    public int Step1 { get => GetValue(Step1Property); set => SetValue(Step1Property, value); }
    public int Step2 { get => GetValue(Step2Property); set => SetValue(Step2Property, value); }
    public int Step3 { get => GetValue(Step3Property); set => SetValue(Step3Property, value); }
    public int Step4 { get => GetValue(Step4Property); set => SetValue(Step4Property, value); }
    public int Step5 { get => GetValue(Step5Property); set => SetValue(Step5Property, value); }
    public int Step6 { get => GetValue(Step6Property); set => SetValue(Step6Property, value); }
    public int Step7 { get => GetValue(Step7Property); set => SetValue(Step7Property, value); }
    public int Step8 { get => GetValue(Step8Property); set => SetValue(Step8Property, value); }
    public int Step9 { get => GetValue(Step9Property); set => SetValue(Step9Property, value); }
    public int Step10 { get => GetValue(Step10Property); set => SetValue(Step10Property, value); }
    public int Step11 { get => GetValue(Step11Property); set => SetValue(Step11Property, value); }
    public int Step12 { get => GetValue(Step12Property); set => SetValue(Step12Property, value); }
    public int Step13 { get => GetValue(Step13Property); set => SetValue(Step13Property, value); }
    public int Step14 { get => GetValue(Step14Property); set => SetValue(Step14Property, value); }
    public int Step15 { get => GetValue(Step15Property); set => SetValue(Step15Property, value); }
    public int Step16 { get => GetValue(Step16Property); set => SetValue(Step16Property, value); }

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<StepLfoControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> BarBrushProperty =
        B(nameof(BarBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> AxisBrushProperty =
        B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    /// <summary>The step the pointer last set, so a fast drag can fill in the ones between rather than
    /// leaving gaps. -1 when no drag is in progress.</summary>
    private int _lastStep = -1;

    /// <summary>The whole stroke is one undo step, however slowly it is drawn. A <see cref="PointerGesture"/>
    /// and not a bare <see cref="EditGesture"/>, because the step also has to close when the drag is
    /// interrupted rather than released -- the window losing activation with the button still down -- and the
    /// <c>PointerCaptureLost</c> event that says so is Direct, so it reaches only the element that held the
    /// capture. That class already holds both halves; a second hand-rolled copy is what went wrong the last
    /// time, and a leaked gesture is not confined to the control that leaked it.</summary>
    private readonly PointerGesture _gesture = new();

    static StepLfoControl()
    {
        AffectsRender<StepLfoControl>(
            Step1Property, Step2Property, Step3Property, Step4Property,
            Step5Property, Step6Property, Step7Property, Step8Property,
            Step9Property, Step10Property, Step11Property, Step12Property,
            Step13Property, Step14Property, Step15Property, Step16Property,
            BarBrushProperty, BackgroundBrushProperty, AxisBrushProperty);
    }

    private StepLfoGeometry Geometry() =>
        new(Bounds.Width, Bounds.Height, Steps, MinValue, MaxValue);

    public override void Render(DrawingContext context)
    {
        var g = Geometry();
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // The centre line first, so a resting bar sits on top of it rather than being lost in it.
        context.DrawLine(new Pen(AxisBrush), new Point(0, g.CentreY), new Point(Bounds.Width, g.CentreY));

        for (var step = 0; step < Steps; step++)
            context.FillRectangle(BarBrush, g.BarFor(step, GetValue(StepProperties[step])));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Left button only. A right or middle press has nothing to mean here, and letting one open a drag
        // would hand an undo gesture to a button whose release this control has no promise of seeing.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A stroke already in progress owns the pointer until it ends. IsLeftButtonPressed stays true while
        // the left button is held, so a second button pressed mid-drag arrives here looking legitimate;
        // falling through would close the open undo step and open a fresh one, splitting one stroke in two.
        if (_lastStep >= 0) return;

        var position = e.GetPosition(this);
        if (Geometry().StepAt(position.X) is not { } step) return;

        // Captured before the gesture opens, because moving the capture makes whoever held it lose it -- and
        // a capture-lost handler attached any earlier would be woken by this press rather than by the end of
        // this stroke.
        e.Pointer.Capture(this);
        _gesture.Begin(this, EndStroke);

        // After Begin and not before: Begin closes any gesture still held from an earlier press, and closing
        // one runs EndStroke, which would clear the stroke being set up here.
        _lastStep = step;
        SetStep(step, Geometry().ValueAt(position.Y));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastStep < 0) return;

        var position = e.GetPosition(this);
        var g = Geometry();
        var value = g.ValueAt(position.Y);

        // A pointer that has left the control sideways keeps drawing at the end it left by, which is what
        // a user dragging past the edge means; one that is still inside sets the bar it is over.
        var step = g.StepAt(position.X) ?? (position.X < 0 ? 0 : Steps - 1);

        // Every bar between the last one and this one, so a fast drag leaves no gaps behind it.
        var direction = step >= _lastStep ? 1 : -1;
        for (var s = _lastStep; s != step + direction; s += direction) SetStep(s, value);

        _lastStep = step;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Releasing the capture reaches EndStroke through the gesture's own capture-lost handler; End() is
        // idempotent, so it does not matter which of the two arrives first. There is no OnPointerCaptureLost
        // override here on purpose -- PointerGesture holds that half.
        e.Pointer.Capture(null);
        _gesture.End();
    }

    /// <summary>Forget the stroke in progress. Reached from the release and -- through the gesture -- from a
    /// capture loss, because an interrupted stroke has to clear this as well as a released one: left set, the
    /// next pointer move across the bars would go on drawing with no button held.</summary>
    private void EndStroke() => _lastStep = -1;

    private void SetStep(int step, int value)
    {
        if (step < 0 || step >= Steps) return;

        SetValue(StepProperties[step], value);
    }
}
