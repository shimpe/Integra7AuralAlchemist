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
/// Y is velocity (0..127, loud at top). Drag a zone body to move it, drag an edge to resize.
///
/// <para>Geometry, hit-testing <b>and what a drag means</b> are all delegated to <see cref="PmtZoneMapping"/>.
/// The last of those was this control's own inline pixel arithmetic until the sixteen-lane
/// <see cref="LayerMapControl"/> arrived needing the same rules over the same four numbers; the two charts now
/// resolve a drag through one tested function, so an edge dragged past its opposite or a body dragged off the
/// end of the keyboard cannot mean one thing on the Set Part tab and another on the Layers tab.</para>
/// </summary>
public class PmtZoneEditorControl : Control
{
    private const double HandleMargin = 6;
    private const double KeyboardHeight = 30; // bottom strip reserved for the piano keyboard

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

    /// <summary>A fade width, in semitones or velocity steps, of the band *outside* one edge of a zone over
    /// which it fades in or out.
    ///
    /// <para>Registered one-way and defaulting to 0, unlike the range properties above. One-way because a fade
    /// has no handle on this chart — the eight range values are dragged, the eight fade widths are only read, so
    /// there is no path by which the control could write one back and a two-way binding would only be a write
    /// path nobody uses. Zero because both views that host this control predate the fades: a view that has not
    /// yet bound them gets no bands, which is exactly what it drew before.</para></summary>
    private static StyledProperty<int> F(string name) =>
        AvaloniaProperty.Register<PmtZoneEditorControl, int>(name);

    public static readonly StyledProperty<int> KeyFade1LoProperty = F(nameof(KeyFade1Lo));
    public static readonly StyledProperty<int> KeyFade1HiProperty = F(nameof(KeyFade1Hi));
    public static readonly StyledProperty<int> VelFade1LoProperty = F(nameof(VelFade1Lo));
    public static readonly StyledProperty<int> VelFade1HiProperty = F(nameof(VelFade1Hi));
    public static readonly StyledProperty<int> KeyFade2LoProperty = F(nameof(KeyFade2Lo));
    public static readonly StyledProperty<int> KeyFade2HiProperty = F(nameof(KeyFade2Hi));
    public static readonly StyledProperty<int> VelFade2LoProperty = F(nameof(VelFade2Lo));
    public static readonly StyledProperty<int> VelFade2HiProperty = F(nameof(VelFade2Hi));
    public static readonly StyledProperty<int> KeyFade3LoProperty = F(nameof(KeyFade3Lo));
    public static readonly StyledProperty<int> KeyFade3HiProperty = F(nameof(KeyFade3Hi));
    public static readonly StyledProperty<int> VelFade3LoProperty = F(nameof(VelFade3Lo));
    public static readonly StyledProperty<int> VelFade3HiProperty = F(nameof(VelFade3Hi));
    public static readonly StyledProperty<int> KeyFade4LoProperty = F(nameof(KeyFade4Lo));
    public static readonly StyledProperty<int> KeyFade4HiProperty = F(nameof(KeyFade4Hi));
    public static readonly StyledProperty<int> VelFade4LoProperty = F(nameof(VelFade4Lo));
    public static readonly StyledProperty<int> VelFade4HiProperty = F(nameof(VelFade4Hi));

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
    public static readonly StyledProperty<IBrush> WhiteKeyBrushProperty = B(nameof(WhiteKeyBrush), new SolidColorBrush(Color.Parse("#c8ccce")));
    public static readonly StyledProperty<IBrush> BlackKeyBrushProperty = B(nameof(BlackKeyBrush), new SolidColorBrush(Color.Parse("#15181a")));

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
    public int KeyFade1Lo { get => GetValue(KeyFade1LoProperty); set => SetValue(KeyFade1LoProperty, value); }
    public int KeyFade1Hi { get => GetValue(KeyFade1HiProperty); set => SetValue(KeyFade1HiProperty, value); }
    public int VelFade1Lo { get => GetValue(VelFade1LoProperty); set => SetValue(VelFade1LoProperty, value); }
    public int VelFade1Hi { get => GetValue(VelFade1HiProperty); set => SetValue(VelFade1HiProperty, value); }
    public int KeyFade2Lo { get => GetValue(KeyFade2LoProperty); set => SetValue(KeyFade2LoProperty, value); }
    public int KeyFade2Hi { get => GetValue(KeyFade2HiProperty); set => SetValue(KeyFade2HiProperty, value); }
    public int VelFade2Lo { get => GetValue(VelFade2LoProperty); set => SetValue(VelFade2LoProperty, value); }
    public int VelFade2Hi { get => GetValue(VelFade2HiProperty); set => SetValue(VelFade2HiProperty, value); }
    public int KeyFade3Lo { get => GetValue(KeyFade3LoProperty); set => SetValue(KeyFade3LoProperty, value); }
    public int KeyFade3Hi { get => GetValue(KeyFade3HiProperty); set => SetValue(KeyFade3HiProperty, value); }
    public int VelFade3Lo { get => GetValue(VelFade3LoProperty); set => SetValue(VelFade3LoProperty, value); }
    public int VelFade3Hi { get => GetValue(VelFade3HiProperty); set => SetValue(VelFade3HiProperty, value); }
    public int KeyFade4Lo { get => GetValue(KeyFade4LoProperty); set => SetValue(KeyFade4LoProperty, value); }
    public int KeyFade4Hi { get => GetValue(KeyFade4HiProperty); set => SetValue(KeyFade4HiProperty, value); }
    public int VelFade4Lo { get => GetValue(VelFade4LoProperty); set => SetValue(VelFade4LoProperty, value); }
    public int VelFade4Hi { get => GetValue(VelFade4HiProperty); set => SetValue(VelFade4HiProperty, value); }
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
    public IBrush WhiteKeyBrush { get => GetValue(WhiteKeyBrushProperty); set => SetValue(WhiteKeyBrushProperty, value); }
    public IBrush BlackKeyBrush { get => GetValue(BlackKeyBrushProperty); set => SetValue(BlackKeyBrushProperty, value); }

    // ---- Drag state --------------------------------------------------------------------------------------
    //
    // In parameter values, not pixels. This used to hold the pointer's press position as a Point and do its own
    // arithmetic on the pixel delta in OnPointerMoved; it holds keys and velocity steps now so that the whole of
    // what a drag *means* can be PmtZoneMapping.ResolveDrag's, shared with the sixteen-lane layer map, and
    // covered by tests. There is no headless-Avalonia harness in this repository, so anything left in this file
    // is arithmetic nothing can check -- and two charts that draw the same key x velocity zone must not disagree
    // about what dragging one does.
    //
    // Values also make a drag survive a resize mid-gesture: a remembered key still means the same key when the
    // chart is narrower, where a remembered X means a different one.

    /// <summary>Which zone (1..4) is being dragged, or -1 when no drag is in progress.</summary>
    private int _dragZone = -1;

    /// <summary>Which part of it was grabbed, and so what the movement will mean.</summary>
    private PmtZoneMapping.Handle _dragHandle;

    /// <summary>The dragged zone's four bounds as they were when the pointer went down. Every move resolves from
    /// these rather than from the previous move, so the drag cannot accumulate rounding drift and bringing the
    /// pointer back to where it started restores exactly the values that were there.</summary>
    private int _origLo, _origHi, _origVlo, _origVhi;

    /// <summary>Where the press landed, in keys and in velocity steps: the reference a <c>Body</c> drag measures
    /// its shift from. Quantised once, at press, so a slow drag across a single key accumulates rather than
    /// rounding to nothing on every move.</summary>
    private int _dragKeyAtPress, _dragVelAtPress;

    private int _tipNote = int.MinValue;             // last note shown in the hover tooltip

    // The whole zone move or resize is one undo step -- up to four bounds, however slowly it is dragged.
    private readonly EditGesture _gesture = new();

    static PmtZoneEditorControl()
    {
        AffectsRender<PmtZoneEditorControl>(
            Key1LoProperty, Key1HiProperty, Vel1LoProperty, Vel1HiProperty,
            Key2LoProperty, Key2HiProperty, Vel2LoProperty, Vel2HiProperty,
            Key3LoProperty, Key3HiProperty, Vel3LoProperty, Vel3HiProperty,
            Key4LoProperty, Key4HiProperty, Vel4LoProperty, Vel4HiProperty,
            KeyFade1LoProperty, KeyFade1HiProperty, VelFade1LoProperty, VelFade1HiProperty,
            KeyFade2LoProperty, KeyFade2HiProperty, VelFade2LoProperty, VelFade2HiProperty,
            KeyFade3LoProperty, KeyFade3HiProperty, VelFade3LoProperty, VelFade3HiProperty,
            KeyFade4LoProperty, KeyFade4HiProperty, VelFade4LoProperty, VelFade4HiProperty,
            Partial1OnProperty, Partial2OnProperty, Partial3OnProperty, Partial4OnProperty,
            PreviewProperty,
            Zone1BrushProperty, Zone2BrushProperty, Zone3BrushProperty, Zone4BrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty, LabelBrushProperty,
            WhiteKeyBrushProperty, BlackKeyBrushProperty);
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

    /// <summary>One zone's four fade widths. Kept apart from <see cref="Zone"/> rather than swelling its tuple
    /// to ten elements, because the two are read by different halves of the control: the ranges are dragged and
    /// hit-tested, the fades are only drawn. Nothing below <see cref="Render"/> has any use for these.</summary>
    private (int keyLo, int keyHi, int velLo, int velHi) Fades(int i) => i switch
    {
        1 => (KeyFade1Lo, KeyFade1Hi, VelFade1Lo, VelFade1Hi),
        2 => (KeyFade2Lo, KeyFade2Hi, VelFade2Lo, VelFade2Hi),
        3 => (KeyFade3Lo, KeyFade3Hi, VelFade3Lo, VelFade3Hi),
        4 => (KeyFade4Lo, KeyFade4Hi, VelFade4Lo, VelFade4Hi),
        _ => (0, 0, 0, 0),
    };

    private PmtZoneMapping.Rect RectOf(int i, double w, double h)
    {
        var z = Zone(i);
        return PmtZoneMapping.ToRect(z.lo, z.hi, z.vlo, z.vhi, w, h);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var mapH = h - KeyboardHeight; // velocity axis area; the keyboard strip sits below it
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        // Vertical key grid lines every 12 keys (one octave).
        for (var k = 0; k <= 127; k += 12)
        {
            var x = PmtZoneMapping.KeyToX(k, w);
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, mapH));
        }

        // Horizontal velocity grid lines + value ticks every 16 (loud at top).
        for (var v = 0; v <= 127; v += 16)
        {
            var y = PmtZoneMapping.VelToY(v, mapH);
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            var vt = new FormattedText(v.ToString(culture), culture, FlowDirection.LeftToRight,
                Typeface.Default, 9, AxisBrush);
            context.DrawText(vt, new Point(2, y + 1));
        }

        // Frame the map area (left / right / top / bottom).
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, mapH));
        context.DrawLine(axisPen, new Point(w, 0), new Point(w, mapH));
        context.DrawLine(axisPen, new Point(0, 0), new Point(w, 0));
        context.DrawLine(axisPen, new Point(0, mapH), new Point(w, mapH));

        DrawKeyboard(context, w, mapH, culture);

        for (var i = 1; i <= 4; i++)
        {
            var z = Zone(i);
            if (!z.on) continue;
            var r = RectOf(i, w, mapH);
            var rect = new Rect(r.X, r.Y, r.W, r.H);

            // The four crossfade bands, before the body so they sit under it and under its outline. Each lies
            // *outside* the range — the lower key band below Key Lo, the lower velocity band below Vel Lo, which
            // on this chart means under the box because loud is up — and each is already clipped to the chart by
            // the geometry, so a twelve-semitone fade on a zone starting at key 3 is a three-semitone band and
            // nothing here clamps or second-guesses it.
            //
            // This is what stops two partials that crossfade across a break from looking like a hard split. The
            // same bands, from the same code, as the Layers tab draws for the sixteen parts: see ZoneShading,
            // which owns the alphas, the floor and the dash pattern so the two charts cannot teach the user two
            // different things about the same parameter.
            var f = Fades(i);
            var zoneColor = ZoneShading.ColorOf(z.brush);
            ZoneShading.DrawFade(context,
                PmtZoneMapping.KeyFadeLowerRect(z.lo, z.hi, z.vlo, z.vhi, f.keyLo, w, mapH),
                zoneColor, ZoneShading.FadeSide.Left);
            ZoneShading.DrawFade(context,
                PmtZoneMapping.KeyFadeUpperRect(z.lo, z.hi, z.vlo, z.vhi, f.keyHi, w, mapH),
                zoneColor, ZoneShading.FadeSide.Right);
            ZoneShading.DrawFade(context,
                PmtZoneMapping.VelFadeLowerRect(z.lo, z.hi, z.vlo, z.vhi, f.velLo, w, mapH),
                zoneColor, ZoneShading.FadeSide.Below);
            ZoneShading.DrawFade(context,
                PmtZoneMapping.VelFadeUpperRect(z.lo, z.hi, z.vlo, z.vhi, f.velHi, w, mapH),
                zoneColor, ZoneShading.FadeSide.Above);

            using (context.PushOpacity(ZoneShading.FillOpacity))
                context.FillRectangle(z.brush, rect);
            context.DrawRectangle(null, new Pen(z.brush, 2), rect);

            if (!Preview && r.W >= 40 && r.H >= 18)
            {
                int klo = Math.Min(z.lo, z.hi), khi = Math.Max(z.lo, z.hi);
                int vmin = Math.Min(z.vlo, z.vhi), vmax = Math.Max(z.vlo, z.vhi);
                var text = $"P{i}  vel {vmin}-{vmax}  key {klo} ({MidiNote.Name(klo)})-{khi} ({MidiNote.Name(khi)})";
                var ft = new FormattedText(text, culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
                context.DrawText(ft, new Point(r.X + 4, r.Y + 3));
            }
        }
    }

    // A 0..127 piano keyboard in the bottom strip: white background, dark accidental keys, an octave
    // divider + "C{octave}" label at every C.
    private void DrawKeyboard(DrawingContext context, double w, double mapH, System.Globalization.CultureInfo culture)
    {
        var top = mapH;
        var kbH = KeyboardHeight;
        context.FillRectangle(WhiteKeyBrush, new Rect(0, top, w, kbH));
        var octavePen = new Pen(BlackKeyBrush);
        for (var n = 0; n <= 127; n++)
        {
            var x = PmtZoneMapping.KeyToX(n, w);
            var xNext = Math.Min(PmtZoneMapping.KeyToX(n + 1, w), w);
            if (MidiNote.IsBlack(n))
                context.FillRectangle(BlackKeyBrush, new Rect(x, top, Math.Max(1, xNext - x), kbH * 0.6));
            if (MidiNote.IsC(n))
            {
                context.DrawLine(octavePen, new Point(x, top), new Point(x, top + kbH));
                var lt = new FormattedText(MidiNote.Name(n), culture, FlowDirection.LeftToRight,
                    Typeface.Default, 9, BlackKeyBrush);
                context.DrawText(lt, new Point(x + 1, top + kbH - 12));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview) return; // previews are non-interactive
        Focus();
        var pos = e.GetPosition(this);
        double w = Bounds.Width, mapH = Bounds.Height - KeyboardHeight;
        for (var i = 4; i >= 1; i--)
        {
            var z = Zone(i);
            if (!z.on) continue;
            var hit = PmtZoneMapping.HitRect(pos.X, pos.Y, RectOf(i, w, mapH), HandleMargin);
            if (hit != PmtZoneMapping.Handle.None)
            {
                // Only inside the hit: a press that lands on no zone falls out of the loop and never
                // sees a release, so a gesture opened there would stay open.
                _gesture.Begin();
                _dragZone = i;
                _dragHandle = hit;
                _origLo = z.lo; _origHi = z.hi; _origVlo = z.vlo; _origVhi = z.vhi;

                // The press in the units the rules speak, quantised here and only here. Everything the drag does
                // from now on is measured against these two numbers, so the pixels-to-values mapping happens once
                // per gesture rather than once per pointer move.
                _dragKeyAtPress = PmtZoneMapping.XToKey(pos.X, w);
                _dragVelAtPress = PmtZoneMapping.YToVel(pos.Y, mapH);
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
        var pos = e.GetPosition(this);
        double w = Bounds.Width, mapH = Bounds.Height - KeyboardHeight;

        // Hover tooltip: show the note name under the cursor, updating as it crosses keys.
        var note = PmtZoneMapping.XToKey(pos.X, w);
        if (note != _tipNote)
        {
            _tipNote = note;
            ToolTip.SetTip(this, MidiNote.Name(note));
        }

        if (_dragZone < 1) return;
        int z = _dragZone;

        // A lookup, a call and four setters. What the movement means -- which of the four values moves, where it
        // stops, what happens when an edge meets its opposite -- is entirely PmtZoneMapping.ResolveDrag's, and
        // there is deliberately no arithmetic here to disagree with it. This block used to compute a pixel delta,
        // round it, and clamp it against the press-time bounds by hand; the sixteen-lane layer map resolved the
        // same gesture through the geometry, and one application drawing the same chart two ways with two
        // slightly different answers is worse than either answer alone.
        var moved = PmtZoneMapping.ResolveDrag(_origLo, _origHi, _origVlo, _origVhi, _dragHandle,
            PmtZoneMapping.XToKey(pos.X, w), PmtZoneMapping.YToVel(pos.Y, mapH),
            _dragKeyAtPress, _dragVelAtPress);

        // ResolveDrag returns all four values whatever was grabbed, but only the ones this handle owns get
        // written -- the same ownership LayerZoneChanges.FieldsFor spells out for the layer map, and the same
        // one this switch already expressed by calling one setter per edge.
        //
        // It is not merely tidiness. The other three values are the ones the *press* saw, and they go stale the
        // moment anything else edits the partial: a front-panel tweak, or a tone change, which rewrites all four
        // partials at once. Writing them back would push a press-time number over what the instrument had just
        // reported, for a value the user never touched. Each of these properties is bound TwoWay straight to a
        // ParamInt, so a write is a sysex round trip and an undo-journal entry, not a field assignment.
        switch (_dragHandle)
        {
            case PmtZoneMapping.Handle.Left:
                SetKeyLo(z, moved.KeyLo);
                break;
            case PmtZoneMapping.Handle.Right:
                SetKeyHi(z, moved.KeyHi);
                break;
            case PmtZoneMapping.Handle.Top:
                SetVelHi(z, moved.VelHi);
                break;
            case PmtZoneMapping.Handle.Bottom:
                SetVelLo(z, moved.VelLo);
                break;
            case PmtZoneMapping.Handle.Body:
                SetKeyLo(z, moved.KeyLo); SetKeyHi(z, moved.KeyHi);
                SetVelLo(z, moved.VelLo); SetVelHi(z, moved.VelHi);
                break;
        }

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
        _gesture.End();
        e.Handled = true;
    }

    /// <summary>Capture goes away without a release when the window is deactivated mid-drag: end the drag
    /// (which used to stay live) and close the undo step with it.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragZone = -1;
        _gesture.End();
    }
}
