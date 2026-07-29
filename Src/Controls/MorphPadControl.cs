using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Integra7AuralAlchemist.Controls;

/// <summary>The morph pad: a disc with two to seven library tones on its rim and a draggable point inside
/// it. The point's position is all this control knows; what a position means to the instrument belongs to
/// the view model.
///
/// <para>Like every visual editor here, it draws and it does not compute. The corner positions come from
/// <see cref="MorphWeights"/>, the mapping between the unit circle and pixels from
/// <see cref="MorphPadGeometry"/>, and every pixel's colour from <see cref="MorphPadFill"/> — all three
/// pure and covered by tests, because a control is the one layer this repository has no harness for.
/// Arithmetic that drifts in here is arithmetic nothing can check.</para>
///
/// <para>The fill is the expensive part and it is cached: see <see cref="EnsureFill"/>. A drag redraws
/// the markers and the point over a bitmap that was rendered once.</para></summary>
public class MorphPadControl : Control
{
    // ---- Fixed measurements ------------------------------------------------------------------------------

    /// <summary>The most corners the pad takes, and so how many colour properties it has. The engine's
    /// limit is the spec's, not the arithmetic's -- everything below generalises.</summary>
    private const int MaxCorners = 7;

    /// <summary>How much of the control the disc gives up around its edge. The disc otherwise fills the
    /// smaller dimension exactly, which would leave every corner marker drawn half outside the control and
    /// its number wholly outside it; this is the room those need. Wide enough for a marker, the gap after
    /// it and half a digit.</summary>
    private const double Inset = 20;

    private const double MarkerRadius = 7;
    private const double MarkerRingThickness = 1.5;

    /// <summary>Between a marker's edge and its number.</summary>
    private const double LabelGap = 3;

    private const double LabelSize = 11;

    /// <summary>The point: a ring with a cross through it, so it stays findable over a fill whose colour it
    /// cannot be chosen against.</summary>
    private const double PointRadius = 4;

    private const double PointArm = 9;
    private const double PointThickness = 1.5;

    // ---- Properties --------------------------------------------------------------------------------------
    //
    // Registered inline rather than through a helper method: the Avalonia analyser can only see that a
    // registration is safe when it sits in a static field initialiser, and the helper-method form is what the
    // AVP1001 warnings elsewhere in this folder are.

    public static readonly StyledProperty<int> CornerCountProperty =
        AvaloniaProperty.Register<MorphPadControl, int>(nameof(CornerCount), 3);

    /// <summary>The point, in unit-circle coordinates: the same space <see cref="MorphWeights"/> speaks, so
    /// the view model can weigh it without knowing how big the control is.</summary>
    public static readonly StyledProperty<Point> PointProperty =
        AvaloniaProperty.Register<MorphPadControl, Point>(nameof(Point), new Point(0, 0),
            defaultBindingMode: BindingMode.TwoWay);

    // The defaults are the App.axaml SnMorphCorner*Brush colours. A default inside a control is the one place
    // this codebase writes a colour outside App.axaml, and it earns it here: the view sets these from the
    // resources, and a pad whose brushes had not arrived yet would otherwise be a black disc.

    public static readonly StyledProperty<IBrush> Corner1BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner1Brush),
            new SolidColorBrush(Color.Parse("#C9724A")));

    public static readonly StyledProperty<IBrush> Corner2BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner2Brush),
            new SolidColorBrush(Color.Parse("#B39A44")));

    public static readonly StyledProperty<IBrush> Corner3BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner3Brush),
            new SolidColorBrush(Color.Parse("#5FAF57")));

    public static readonly StyledProperty<IBrush> Corner4BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner4Brush),
            new SolidColorBrush(Color.Parse("#49AE93")));

    public static readonly StyledProperty<IBrush> Corner5BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner5Brush),
            new SolidColorBrush(Color.Parse("#4E93C4")));

    public static readonly StyledProperty<IBrush> Corner6BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner6Brush),
            new SolidColorBrush(Color.Parse("#8A6FC6")));

    public static readonly StyledProperty<IBrush> Corner7BrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(Corner7Brush),
            new SolidColorBrush(Color.Parse("#C25E9B")));

    /// <summary>The circle around the disc. The envelope charts' axis colour by default, so the pad reads as
    /// one of them.</summary>
    public static readonly StyledProperty<IBrush> RimBrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(RimBrush),
            new SolidColorBrush(Color.Parse("#55FFFFFF")));

    /// <summary>The ring around each corner marker, the numbers and the point. One brush, because all three
    /// have to stay legible over any of the seven corner colours and white is the only choice that does.
    /// </summary>
    public static readonly StyledProperty<IBrush> MarkerBrushProperty =
        AvaloniaProperty.Register<MorphPadControl, IBrush>(nameof(MarkerBrush), Brushes.White);

    /// <summary>The corner brushes by index, which is how everything below reaches them: a pad with a
    /// variable number of corners cannot name its colours one at a time.</summary>
    private static readonly StyledProperty<IBrush>[] CornerBrushes =
    [
        Corner1BrushProperty, Corner2BrushProperty, Corner3BrushProperty, Corner4BrushProperty,
        Corner5BrushProperty, Corner6BrushProperty, Corner7BrushProperty,
    ];

    public int CornerCount { get => GetValue(CornerCountProperty); set => SetValue(CornerCountProperty, value); }
    public Point Point { get => GetValue(PointProperty); set => SetValue(PointProperty, value); }
    public IBrush Corner1Brush { get => GetValue(Corner1BrushProperty); set => SetValue(Corner1BrushProperty, value); }
    public IBrush Corner2Brush { get => GetValue(Corner2BrushProperty); set => SetValue(Corner2BrushProperty, value); }
    public IBrush Corner3Brush { get => GetValue(Corner3BrushProperty); set => SetValue(Corner3BrushProperty, value); }
    public IBrush Corner4Brush { get => GetValue(Corner4BrushProperty); set => SetValue(Corner4BrushProperty, value); }
    public IBrush Corner5Brush { get => GetValue(Corner5BrushProperty); set => SetValue(Corner5BrushProperty, value); }
    public IBrush Corner6Brush { get => GetValue(Corner6BrushProperty); set => SetValue(Corner6BrushProperty, value); }
    public IBrush Corner7Brush { get => GetValue(Corner7BrushProperty); set => SetValue(Corner7BrushProperty, value); }
    public IBrush RimBrush { get => GetValue(RimBrushProperty); set => SetValue(RimBrushProperty, value); }
    public IBrush MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    static MorphPadControl()
    {
        AffectsRender<MorphPadControl>(CornerCountProperty, PointProperty, RimBrushProperty,
            MarkerBrushProperty, Corner1BrushProperty, Corner2BrushProperty, Corner3BrushProperty,
            Corner4BrushProperty, Corner5BrushProperty, Corner6BrushProperty, Corner7BrushProperty);
    }

    // ---- The cached fill ---------------------------------------------------------------------------------

    /// <summary>The fill, rendered once and blitted afterwards.</summary>
    private WriteableBitmap? _fill;

    private int _fillCorners;
    private Size _fillSize;

    /// <summary>Whether a drag is in progress. The gesture below cannot be asked -- it is idempotent by
    /// design and says nothing about whether it is open.</summary>
    private bool _dragging;

    /// <summary>One undo step per drag, closed by the release or by the loss of capture, whichever comes
    /// first. Nothing on this screen writes to the journal today (see the Morph tab's view model), but a
    /// drag that leaked an open scope would widen every later edit in the application, so the gesture is
    /// held properly rather than skipped.</summary>
    private readonly PointerGesture _gesture = new();

    /// <summary>Two corners at least, seven at most. The view model offers exactly that range, so this is
    /// only a guard -- but it is the guard that keeps a mis-set binding from indexing past the seventh
    /// colour or asking for the weights of a disc with one corner on it.</summary>
    private int EffectiveCornerCount => Math.Clamp(CornerCount, 2, MaxCorners);

    /// <summary>The disc's box, inset from the control's bounds by the room the markers and numbers need.
    /// Every reader of it goes through here, so the drawing and the drags cannot disagree about where the
    /// disc is.</summary>
    private Rect DiscBounds() => new Rect(Bounds.Size).Deflate(Inset);

    /// <summary>Rebuild the fill if it is not already the picture wanted.
    ///
    /// <para>The weight field depends only on the corner count and the control's size, never on the
    /// pointer, so re-rendering it while dragging would burn a quarter of a million pixel evaluations per
    /// frame to draw the same picture. It is rebuilt when the count, the colours or the bounds change; a
    /// drag redraws two markers over it.</para></summary>
    private void EnsureFill(MorphPadGeometry geometry, int count)
    {
        var pixelWidth = (int)Math.Ceiling(Bounds.Width);
        var pixelHeight = (int)Math.Ceiling(Bounds.Height);
        if (pixelWidth <= 0 || pixelHeight <= 0) return;
        if (_fill is not null && _fillCorners == count && _fillSize == Bounds.Size) return;

        var corners = MorphWeights.Corners(count);
        var colours = CornerColours(count);

        // Repainted into the surface already there whenever it is the right size, which is what a corner
        // count or a colour change is -- writing into a locked buffer between frames is what a
        // WriteableBitmap is for. Only a resize needs a new one, and the one it replaces is disposed
        // rather than left to the collector: it holds a native surface the size of this control.
        var size = new PixelSize(pixelWidth, pixelHeight);
        WriteableBitmap bitmap;
        if (_fill is { } existing && existing.PixelSize == size)
        {
            bitmap = existing;
        }
        else
        {
            bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _fill?.Dispose();
        }

        // Written into a plain array and copied in, rather than into the locked buffer through a pointer.
        // One extra allocation per rebuild -- which happens on a resize and a corner change, not per frame --
        // buys not having to turn <AllowUnsafeBlocks> on for the whole project.
        var pixels = new byte[pixelWidth * pixelHeight * 4];

        for (var y = 0; y < pixelHeight; y++)
        for (var x = 0; x < pixelWidth; x++)
        {
            var unit = geometry.ToUnit(new Point(x, y));

            // Outside the disc: left as the zeroes the array came with, which in premultiplied alpha is
            // transparent. The inset margin the markers and numbers are drawn in is this same nothing.
            if (unit.X * unit.X + unit.Y * unit.Y > 1.0) continue;

            var (r, g, b) = MorphPadFill.ColourAt(MorphWeights.For(unit, corners), colours);
            var i = (y * pixelWidth + x) * 4;
            pixels[i] = (byte)b;
            pixels[i + 1] = (byte)g;
            pixels[i + 2] = (byte)r;
            pixels[i + 3] = 255;
        }

        using (var buffer = bitmap.Lock())
        {
            // A row at a time, because a locked buffer's stride is not promised to be the row's own width:
            // one copy of the lot would shear the picture wherever the platform padded it.
            for (var y = 0; y < pixelHeight; y++)
                Marshal.Copy(pixels, y * pixelWidth * 4, IntPtr.Add(buffer.Address, y * buffer.RowBytes),
                    pixelWidth * 4);
        }

        _fill = bitmap;
        _fillCorners = count;
        _fillSize = Bounds.Size;
    }

    /// <summary>The corner brushes as the numbers <see cref="MorphPadFill"/> mixes.</summary>
    private IReadOnlyList<(double R, double G, double B)> CornerColours(int count)
    {
        var colours = new (double R, double G, double B)[count];
        for (var i = 0; i < count; i++)
        {
            // A gradient set by a style has no single colour to mix with, and drawing that corner's
            // territory black would read as a fault in the pad rather than in the brush.
            var colour = GetValue(CornerBrushes[i]) is ISolidColorBrush solid ? solid.Color : Colors.White;
            colours[i] = (colour.R, colour.G, colour.B);
        }

        return colours;
    }

    /// <summary>The cache is keyed on the corner count and the size, because those are what the weight field
    /// depends on -- but the colours are baked into the same bitmap, and the view sets the seven brushes
    /// from resources, which can land after the first render. So a colour change invalidates the picture by
    /// hand.
    ///
    /// The key is spoiled rather than the bitmap dropped, so that the surface is repainted instead of
    /// reallocated: a count no disc can have is one no comparison in <see cref="EnsureFill"/> can
    /// accidentally match.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CornerCountProperty || Array.IndexOf(CornerBrushes, change.Property) >= 0)
            _fillCorners = -1;
    }

    // ---- Drawing -----------------------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var disc = DiscBounds();
        if (disc.Width <= 0 || disc.Height <= 0) return; // smaller than its own margins: nothing to draw

        var count = EffectiveCornerCount;
        var geometry = new MorphPadGeometry(disc);
        EnsureFill(geometry, count);

        if (_fill is not null) context.DrawImage(_fill, new Rect(Bounds.Size));
        context.DrawEllipse(null, new Pen(RimBrush), geometry.Centre, geometry.Radius, geometry.Radius);

        var corners = MorphWeights.Corners(count);
        for (var i = 0; i < count; i++) DrawCorner(context, geometry, corners[i], i);

        DrawPoint(context, geometry);
    }

    private void DrawCorner(DrawingContext context, MorphPadGeometry geometry, Point corner, int index)
    {
        var at = geometry.ToControl(corner);
        context.DrawEllipse(GetValue(CornerBrushes[index]), new Pen(MarkerBrush, MarkerRingThickness), at,
            MarkerRadius, MarkerRadius);

        // The number sits outside its marker, on the line out from the centre. A corner is already a unit
        // vector, so that direction is the corner itself and needs no measuring; the disc is a circle, so
        // the same vector points the same way in control space.
        var away = at + new Vector(corner.X, corner.Y) * (MarkerRadius + LabelGap);
        var text = new FormattedText((index + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, LabelSize, MarkerBrush);
        context.DrawText(text, new Point(away.X - text.Width / 2, away.Y - text.Height / 2));
    }

    /// <summary>Clamped as it is drawn, so a position restored from a saved pad that somehow sits outside
    /// the disc is still shown where a drag would have to put it.</summary>
    private void DrawPoint(DrawingContext context, MorphPadGeometry geometry)
    {
        var at = geometry.ToControl(MorphPadGeometry.Clamp(Point));
        var pen = new Pen(MarkerBrush, PointThickness);

        context.DrawLine(pen, new Point(at.X - PointArm, at.Y), new Point(at.X + PointArm, at.Y));
        context.DrawLine(pen, new Point(at.X, at.Y - PointArm), new Point(at.X, at.Y + PointArm));
        context.DrawEllipse(null, pen, at, PointRadius, PointRadius);
    }

    // ---- The drag ----------------------------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Left button only, for the reason the layer map and the Motional Surround pucks check: a right or
        // middle press has nothing to mean here, and opening a drag on a button whose release this control
        // has no promise of seeing would leave the pad following the pointer with nothing held down.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A press while already dragging is a second button going down, not a new drag: IsLeftButtonPressed
        // stays true for as long as the left button is held. Restarting the gesture would split one
        // continuous drag into two.
        if (_dragging) return;

        var disc = DiscBounds();
        if (disc.Width <= 0 || disc.Height <= 0) return;

        // Captured on the control itself, so a drag that leaves the disc keeps sliding around the rim rather
        // than stopping at the edge. Captured *before* the gesture opens: moving the capture makes whoever
        // held it lose it, and when that is already this control, a capture-lost handler attached any earlier
        // would be woken by this press instead of by the end of this drag.
        e.Pointer.Capture(this);
        _gesture.Begin(this, EndDrag);

        // After Begin and not before: Begin closes any gesture still held from an earlier press, and closing
        // one runs its EndDrag -- which would clear the drag being set up here.
        _dragging = true;

        MoveTo(e.GetPosition(this), disc);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        MoveTo(e.GetPosition(this), DiscBounds());
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        // Releasing the capture reaches EndDrag through the gesture's own capture-lost handler; End() is
        // idempotent, so it does not matter which arrives first. There is deliberately no
        // OnPointerCaptureLost override here -- PointerGesture holds that half, and a second hand-rolled
        // copy of it is what went wrong the last time.
        e.Pointer.Capture(null);
        _gesture.End();
        e.Handled = true;
    }

    private void EndDrag() => _dragging = false;

    /// <summary>Where the pointer is, in the unit circle, kept inside the disc. All of that arithmetic is
    /// <see cref="MorphPadGeometry"/>'s; this is a lookup and an assignment.</summary>
    private void MoveTo(Point position, Rect disc)
    {
        if (disc.Width <= 0 || disc.Height <= 0) return;
        Point = MorphPadGeometry.Clamp(new MorphPadGeometry(disc).ToUnit(position));
    }
}
