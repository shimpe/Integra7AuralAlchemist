using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Four key×velocity zone rectangles (one per PCM partial). X is the MIDI key (0..127, left→right),
/// Y is velocity (0..127, loud at top). Drag a zone body to move it, drag an edge to resize. Pure
/// geometry and hit-testing are delegated to <see cref="PmtZoneMapping"/>.
/// </summary>
public class PmtZoneEditorControl : Control
{
    private const double HandleMargin = 6;

    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<PmtZoneEditorControl, int>(name, 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> Key1LoProperty = I(nameof(Key1Lo));
    public static readonly StyledProperty<int> Key1HiProperty = I(nameof(Key1Hi));
    public static readonly StyledProperty<int> Vel1LoProperty = I(nameof(Vel1Lo));
    public static readonly StyledProperty<int> Vel1HiProperty = I(nameof(Vel1Hi));
    public static readonly StyledProperty<int> Key2LoProperty = I(nameof(Key2Lo));
    public static readonly StyledProperty<int> Key2HiProperty = I(nameof(Key2Hi));
    public static readonly StyledProperty<int> Vel2LoProperty = I(nameof(Vel2Lo));
    public static readonly StyledProperty<int> Vel2HiProperty = I(nameof(Vel2Hi));
    public static readonly StyledProperty<int> Key3LoProperty = I(nameof(Key3Lo));
    public static readonly StyledProperty<int> Key3HiProperty = I(nameof(Key3Hi));
    public static readonly StyledProperty<int> Vel3LoProperty = I(nameof(Vel3Lo));
    public static readonly StyledProperty<int> Vel3HiProperty = I(nameof(Vel3Hi));
    public static readonly StyledProperty<int> Key4LoProperty = I(nameof(Key4Lo));
    public static readonly StyledProperty<int> Key4HiProperty = I(nameof(Key4Hi));
    public static readonly StyledProperty<int> Vel4LoProperty = I(nameof(Vel4Lo));
    public static readonly StyledProperty<int> Vel4HiProperty = I(nameof(Vel4Hi));

    public static readonly StyledProperty<bool> Partial1OnProperty =
        AvaloniaProperty.Register<PmtZoneEditorControl, bool>(nameof(Partial1On));
    public static readonly StyledProperty<bool> Partial2OnProperty =
        AvaloniaProperty.Register<PmtZoneEditorControl, bool>(nameof(Partial2On));
    public static readonly StyledProperty<bool> Partial3OnProperty =
        AvaloniaProperty.Register<PmtZoneEditorControl, bool>(nameof(Partial3On));
    public static readonly StyledProperty<bool> Partial4OnProperty =
        AvaloniaProperty.Register<PmtZoneEditorControl, bool>(nameof(Partial4On));

    /// <summary>When true, render as a non-interactive preview: no labels, no pointer input.</summary>
    public static readonly StyledProperty<bool> PreviewProperty =
        AvaloniaProperty.Register<PmtZoneEditorControl, bool>(nameof(Preview));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<PmtZoneEditorControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> Zone1BrushProperty = B(nameof(Zone1Brush), new SolidColorBrush(Color.Parse("#6b8dff")));
    public static readonly StyledProperty<IBrush> Zone2BrushProperty = B(nameof(Zone2Brush), new SolidColorBrush(Color.Parse("#ff9e6b")));
    public static readonly StyledProperty<IBrush> Zone3BrushProperty = B(nameof(Zone3Brush), new SolidColorBrush(Color.Parse("#7ad19a")));
    public static readonly StyledProperty<IBrush> Zone4BrushProperty = B(nameof(Zone4Brush), new SolidColorBrush(Color.Parse("#d18ad1")));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> LabelBrushProperty = B(nameof(LabelBrush), Brushes.White);

    public int Key1Lo { get => GetValue(Key1LoProperty); set => SetValue(Key1LoProperty, value); }
    public int Key1Hi { get => GetValue(Key1HiProperty); set => SetValue(Key1HiProperty, value); }
    public int Vel1Lo { get => GetValue(Vel1LoProperty); set => SetValue(Vel1LoProperty, value); }
    public int Vel1Hi { get => GetValue(Vel1HiProperty); set => SetValue(Vel1HiProperty, value); }
    public int Key2Lo { get => GetValue(Key2LoProperty); set => SetValue(Key2LoProperty, value); }
    public int Key2Hi { get => GetValue(Key2HiProperty); set => SetValue(Key2HiProperty, value); }
    public int Vel2Lo { get => GetValue(Vel2LoProperty); set => SetValue(Vel2LoProperty, value); }
    public int Vel2Hi { get => GetValue(Vel2HiProperty); set => SetValue(Vel2HiProperty, value); }
    public int Key3Lo { get => GetValue(Key3LoProperty); set => SetValue(Key3LoProperty, value); }
    public int Key3Hi { get => GetValue(Key3HiProperty); set => SetValue(Key3HiProperty, value); }
    public int Vel3Lo { get => GetValue(Vel3LoProperty); set => SetValue(Vel3LoProperty, value); }
    public int Vel3Hi { get => GetValue(Vel3HiProperty); set => SetValue(Vel3HiProperty, value); }
    public int Key4Lo { get => GetValue(Key4LoProperty); set => SetValue(Key4LoProperty, value); }
    public int Key4Hi { get => GetValue(Key4HiProperty); set => SetValue(Key4HiProperty, value); }
    public int Vel4Lo { get => GetValue(Vel4LoProperty); set => SetValue(Vel4LoProperty, value); }
    public int Vel4Hi { get => GetValue(Vel4HiProperty); set => SetValue(Vel4HiProperty, value); }
    public bool Partial1On { get => GetValue(Partial1OnProperty); set => SetValue(Partial1OnProperty, value); }
    public bool Partial2On { get => GetValue(Partial2OnProperty); set => SetValue(Partial2OnProperty, value); }
    public bool Partial3On { get => GetValue(Partial3OnProperty); set => SetValue(Partial3OnProperty, value); }
    public bool Partial4On { get => GetValue(Partial4OnProperty); set => SetValue(Partial4OnProperty, value); }
    public bool Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public IBrush Zone1Brush { get => GetValue(Zone1BrushProperty); set => SetValue(Zone1BrushProperty, value); }
    public IBrush Zone2Brush { get => GetValue(Zone2BrushProperty); set => SetValue(Zone2BrushProperty, value); }
    public IBrush Zone3Brush { get => GetValue(Zone3BrushProperty); set => SetValue(Zone3BrushProperty, value); }
    public IBrush Zone4Brush { get => GetValue(Zone4BrushProperty); set => SetValue(Zone4BrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private int _dragZone = -1;
    private PmtZoneMapping.Handle _dragHandle;
    private Point _lastPos;

    static PmtZoneEditorControl()
    {
        AffectsRender<PmtZoneEditorControl>(
            Key1LoProperty, Key1HiProperty, Vel1LoProperty, Vel1HiProperty,
            Key2LoProperty, Key2HiProperty, Vel2LoProperty, Vel2HiProperty,
            Key3LoProperty, Key3HiProperty, Vel3LoProperty, Vel3HiProperty,
            Key4LoProperty, Key4HiProperty, Vel4LoProperty, Vel4HiProperty,
            Partial1OnProperty, Partial2OnProperty, Partial3OnProperty, Partial4OnProperty,
            PreviewProperty,
            Zone1BrushProperty, Zone2BrushProperty, Zone3BrushProperty, Zone4BrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty, LabelBrushProperty);
        FocusableProperty.OverrideDefaultValue<PmtZoneEditorControl>(true);
    }

    private (int lo, int hi, int vlo, int vhi, bool on, IBrush brush) Zone(int i) => i switch
    {
        1 => (Key1Lo, Key1Hi, Vel1Lo, Vel1Hi, Partial1On, Zone1Brush),
        2 => (Key2Lo, Key2Hi, Vel2Lo, Vel2Hi, Partial2On, Zone2Brush),
        3 => (Key3Lo, Key3Hi, Vel3Lo, Vel3Hi, Partial3On, Zone3Brush),
        4 => (Key4Lo, Key4Hi, Vel4Lo, Vel4Hi, Partial4On, Zone4Brush),
        _ => (0, 0, 0, 0, false, Zone1Brush),
    };

    private PmtZoneMapping.Rect RectOf(int i, double w, double h)
    {
        var z = Zone(i);
        return PmtZoneMapping.ToRect(z.lo, z.hi, z.vlo, z.vhi, w, h);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);

        // Vertical key grid lines every 12 keys (one octave).
        for (var k = 0; k <= 127; k += 12)
        {
            var x = PmtZoneMapping.KeyToX(k, w);
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
        }

        // Horizontal velocity grid lines every 16.
        for (var v = 0; v <= 127; v += 16)
        {
            var y = PmtZoneMapping.VelToY(v, h);
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        // Left axis and bottom axis.
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        context.DrawLine(axisPen, new Point(0, h), new Point(w, h));

        for (var i = 1; i <= 4; i++)
        {
            var z = Zone(i);
            if (!z.on) continue;
            var r = RectOf(i, w, h);
            var rect = new Rect(r.X, r.Y, r.W, r.H);
            using (context.PushOpacity(0.22))
                context.FillRectangle(z.brush, rect);
            context.DrawRectangle(null, new Pen(z.brush, 2), rect);

            if (!Preview && r.W >= 40 && r.H >= 18)
            {
                var text = $"P{i}  vel {z.vlo}-{z.vhi}  key {z.lo}-{z.hi}";
                var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                context.DrawText(ft, new Point(r.X + 4, r.Y + 3));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return; // previews are non-interactive
        Focus();
        var pos = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        for (var i = 4; i >= 1; i--)
        {
            var z = Zone(i);
            if (!z.on) continue;
            var hit = PmtZoneMapping.HitRect(pos.X, pos.Y, RectOf(i, w, h), HandleMargin);
            if (hit != PmtZoneMapping.Handle.None)
            {
                _dragZone = i;
                _dragHandle = hit;
                _lastPos = pos;
                e.Pointer.Capture(this);
                e.Handled = true;
                InvalidateVisual();
                return;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragZone < 1) return;
        var pos = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        int z = _dragZone;
        var cur = Zone(z);

        switch (_dragHandle)
        {
            case PmtZoneMapping.Handle.Left:
                SetKeyLo(z, Math.Min(PmtZoneMapping.XToKey(pos.X, w), cur.hi));
                break;
            case PmtZoneMapping.Handle.Right:
                SetKeyHi(z, Math.Max(PmtZoneMapping.XToKey(pos.X, w), cur.lo));
                break;
            case PmtZoneMapping.Handle.Top:
                SetVelHi(z, Math.Max(PmtZoneMapping.YToVel(pos.Y, h), cur.vlo));
                break;
            case PmtZoneMapping.Handle.Bottom:
                SetVelLo(z, Math.Min(PmtZoneMapping.YToVel(pos.Y, h), cur.vhi));
                break;
            case PmtZoneMapping.Handle.Body:
                var dKey = PmtZoneMapping.XToKey(pos.X, w) - PmtZoneMapping.XToKey(_lastPos.X, w);
                var dVel = PmtZoneMapping.YToVel(pos.Y, h) - PmtZoneMapping.YToVel(_lastPos.Y, h);
                // clamp dKey/dVel so the zone stays fully within 0..127 without resizing
                if (cur.lo + dKey < 0) dKey = -cur.lo;
                if (cur.hi + dKey > 127) dKey = 127 - cur.hi;
                if (cur.vlo + dVel < 0) dVel = -cur.vlo;
                if (cur.vhi + dVel > 127) dVel = 127 - cur.vhi;
                SetKeyLo(z, cur.lo + dKey); SetKeyHi(z, cur.hi + dKey);
                SetVelLo(z, cur.vlo + dVel); SetVelHi(z, cur.vhi + dVel);
                break;
        }

        _lastPos = pos;
        e.Handled = true;

        void SetKeyLo(int i, int v) { switch (i) { case 1: Key1Lo = v; break; case 2: Key2Lo = v; break; case 3: Key3Lo = v; break; case 4: Key4Lo = v; break; } }
        void SetKeyHi(int i, int v) { switch (i) { case 1: Key1Hi = v; break; case 2: Key2Hi = v; break; case 3: Key3Hi = v; break; case 4: Key4Hi = v; break; } }
        void SetVelLo(int i, int v) { switch (i) { case 1: Vel1Lo = v; break; case 2: Vel2Lo = v; break; case 3: Vel3Lo = v; break; case 4: Vel4Lo = v; break; } }
        void SetVelHi(int i, int v) { switch (i) { case 1: Vel1Hi = v; break; case 2: Vel2Hi = v; break; case 3: Vel3Hi = v; break; case 4: Vel4Hi = v; break; } }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragZone < 1) return;
        e.Pointer.Capture(null);
        _dragZone = -1;
        e.Handled = true;
    }
}
