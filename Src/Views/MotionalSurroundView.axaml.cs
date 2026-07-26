using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Integra7AuralAlchemist.Controls;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class MotionalSurroundView : UserControl
{
    private const double PuckRadius = 14; // half of the 28px puck

    // Faint guide rings are drawn at these L-R/F-B radii (multiples of 8, up to the 64 half-span).
    private static readonly int[] RingRadii = [8, 16, 24, 32, 40, 48, 56, 64];
    private static readonly IBrush RingBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    private ItemsControl? _puckHost;
    private Canvas? _ringCanvas;
    private Border? _dragging;
    private MotionalSurroundPartViewModel? _dragVm;

    // A puck drag is one undo step however long the user takes over it, and so is a drag on any of this
    // view's nine Sliders (Depth, the four Ambience values, and the selected part's L-R, F-B, Width and
    // Ambience). A step is a gesture, and only the pointer handlers know where one begins and ends -- the
    // clock cannot, because a slow, careful drag is seconds between changes. The same reason the eight
    // controls in Src/Controls hold one; see EditGesture.
    private readonly PointerGesture _puckGesture = new();
    private readonly PointerGesture _sliderGesture = new();

    /// <summary>An <see cref="EditGesture"/> held for the length of a pointer drag, closed by whichever of
    /// the release and the loss of capture comes first.
    ///
    /// The capture-lost half is why this is a class rather than two handlers. Avalonia declares
    /// <c>PointerCaptureLostEvent</c> as a <b>Direct</b> routed event (asserted in
    /// <c>EditRecordingTests.The_pointer_events_the_slider_gestures_hang_off_route_as_this_view_needs</c>),
    /// so it is only ever delivered to the element that held the capture -- a handler on an ancestor,
    /// which is what this file had for the pucks, can never run. The element to hang it on is known at
    /// press time, though: <c>MouseDevice.MouseDown</c> captures the hit-tested element before it raises
    /// <c>PointerPressed</c>, and neither <c>Slider</c> nor <c>Thumb</c> moves that capture afterwards
    /// (neither calls <c>Pointer.Capture</c> at all), so the captured element reports the end of the drag
    /// however the drag ends. For a puck this view does the capturing itself and passes the Border.
    ///
    /// Without that, a drag interrupted rather than released -- the window losing activation while the
    /// button is down -- would leave the scope open and fold every later edit into that one step until
    /// <see cref="EditJournal.StaleGestureWindow"/> gave up on it, which is containment and not a fix.</summary>
    private sealed class PointerGesture
    {
        private readonly EditGesture _gesture = new();
        private Interactive? _captureTarget;
        private Action? _onEnd;

        /// <param name="onEnd">Run when the drag ends, however it ends. The pucks keep drag state of their
        /// own that has to be cleared on an interrupted drag as well as a released one -- left set, it
        /// makes the next pointer move over the room map drag the puck with no button held.</param>
        public void Begin(Interactive captureTarget, Action? onEnd = null)
        {
            End();
            _captureTarget = captureTarget;
            _onEnd = onEnd;
            captureTarget.AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct);
            _gesture.Begin();
        }

        /// <summary>Idempotent, which is what lets the release, the capture loss and the next press all
        /// call it without any of them closing a gesture that is not theirs.</summary>
        public void End()
        {
            if (_captureTarget is { } t)
            {
                _captureTarget = null;
                t.RemoveHandler(PointerCaptureLostEvent, OnCaptureLost);
            }
            _gesture.End();
            var onEnd = _onEnd;
            _onEnd = null;
            onEnd?.Invoke();
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => End();
    }

    public MotionalSurroundView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
        // Re-measure the stage when the bound view model changes, so a fresh VM (e.g. after a
        // reconnect) gets the current room-map size instead of keeping its 1x1 default, which
        // would otherwise stack every puck in the top-left corner until the next layout pass.
        DataContextChanged += (_, _) => UpdateStage();

        // Slider gestures, opened and closed once here rather than nine times in the markup. Both halves
        // reach us despite the capture living on a template part inside the Slider: a captured pointer's
        // press and release are raised on the capture target and routed through its visual ancestors, of
        // which this view is one. Tunnel so that a Slider marking them handled cannot hide them, and
        // handledEventsToo on the release because a close must never be missed.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private MotionalSurroundViewModel? Vm => DataContext as MotionalSurroundViewModel;

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (_puckHost != null) return;
        _puckHost = this.FindControl<ItemsControl>("PuckHost");
        _ringCanvas = this.FindControl<Canvas>("RingCanvas");
        if (_puckHost is null) return;
        _puckHost.AddHandler(PointerPressedEvent, OnPuckPointerPressed, RoutingStrategies.Tunnel);
        _puckHost.AddHandler(PointerMovedEvent, OnPuckPointerMoved, RoutingStrategies.Tunnel);
        _puckHost.AddHandler(PointerReleasedEvent, OnPuckPointerReleased, RoutingStrategies.Tunnel);
        // No capture-lost handler here: it used to be registered on this host and could never have fired,
        // because PointerCaptureLost is a Direct event and the capture lives on the puck. It is registered
        // on the puck itself, at press time -- see PointerGesture and OnPuckPointerPressed.
        _puckHost.AddHandler(KeyDownEvent, OnPuckKeyDown, RoutingStrategies.Bubble);
        _puckHost.PropertyChanged += (_, ev) =>
        {
            if (ev.Property != BoundsProperty) return;
            UpdateStage();
            DrawRings();
        };
        UpdateStage();
        DrawRings();
    }

    private void UpdateStage()
    {
        if (Vm is null || _puckHost is null) return;
        var b = _puckHost.Bounds;
        if (b.Width > 2 * PuckRadius) Vm.StageWidth = b.Width - 2 * PuckRadius;
        if (b.Height > 2 * PuckRadius) Vm.StageHeight = b.Height - 2 * PuckRadius;
    }

    // Draw faint concentric guide rings whose radii are multiples of 8 in L-R/F-B units, centred on
    // the (0,0) axis crossing. A value-radius r is an ellipse on screen because the stage's X and Y
    // scales differ (the room map is rarely square) — this keeps the rings aligned with the pucks,
    // which use those same per-axis scales. A part exactly r units from centre sits on ring r.
    private void DrawRings()
    {
        if (_ringCanvas is null || _puckHost is null) return;
        _ringCanvas.Children.Clear();
        var w = _puckHost.Bounds.Width;
        var h = _puckHost.Bounds.Height;
        if (w <= 2 * PuckRadius || h <= 2 * PuckRadius) return;

        var stageW = w - 2 * PuckRadius;
        var stageH = h - 2 * PuckRadius;
        var cx = w / 2.0;
        var cy = h / 2.0;
        foreach (var r in RingRadii)
        {
            var rx = r / (2.0 * MotionalSurroundMapping.LrFbHalfSpan) * stageW;
            var ry = r / (2.0 * MotionalSurroundMapping.LrFbHalfSpan) * stageH;
            var ring = new Ellipse
            {
                Width = 2 * rx,
                Height = 2 * ry,
                Stroke = RingBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, cx - rx);
            Canvas.SetTop(ring, cy - ry);
            _ringCanvas.Children.Add(ring);
        }
    }

    private static Border? FindPuck(object? src)
    {
        var cur = src as Visual;
        while (cur != null)
        {
            if (cur is Border b && b.DataContext is MotionalSurroundPartViewModel) return b;
            cur = cur.GetVisualParent();
        }
        return null;
    }

    /// <summary>True if <paramref name="src"/> is a <see cref="Slider"/> or sits inside one. Walks the
    /// visual tree, which crosses the template boundary, so the thumb and the track a press actually lands
    /// on both resolve to their Slider. Stops at this view rather than running on to the window.</summary>
    private bool IsInsideSlider(object? src)
    {
        var cur = src as Visual;
        while (cur != null && !ReferenceEquals(cur, this))
        {
            if (cur is Slider) return true;
            cur = cur.GetVisualParent();
        }
        return false;
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a press heading for a Slider opens the slider gesture; a puck press is served by
        // OnPuckPointerPressed and opens its own. Everything else in the tab -- buttons, the ToggleSwitch,
        // the combo, the NumericUpDowns' spinners -- has no drag to delimit and keeps the journal's time
        // window, like every other non-pointer edit in the application.
        if (!IsInsideSlider(e.Source)) return;
        // Avalonia has already captured the element this press hit, in MouseDevice.MouseDown, before
        // raising the event -- so this is the element that will report the end of the drag. A press with no
        // capture (nothing does that today, but nothing promises not to) is left to the journal's time
        // window rather than given a gesture that might never be closed.
        if (e.Pointer.Captured is Interactive captured) _sliderGesture.Begin(captured);
    }

    private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e) => _sliderGesture.End();

    private void OnPuckPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindPuck(e.Source) is { DataContext: MotionalSurroundPartViewModel p } b)
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            if (Vm != null) Vm.SelectedPart = p;
            e.Pointer.Capture(b);
            b.Focus();
            // Opened once the drag is certain (a right-click returned above) and after the capture has
            // moved to the puck: the element the press landed on loses its own capture at that moment, and
            // a handler attached to the puck any earlier could be woken by that rather than by the end of
            // this drag.
            _puckGesture.Begin(b, EndPuckDrag);
            // After Begin, not before: Begin closes any gesture still held from an earlier press, which
            // runs that gesture's EndPuckDrag -- and that would clear the drag this one is starting.
            _dragging = b;
            _dragVm = p;
            e.Handled = true;
        }
    }

    /// <summary>Forget the drag in progress. Reached from the release and, through the gesture, from a
    /// capture loss -- an interrupted drag has to clear this too, or the next pointer move over the room
    /// map would go on dragging the puck with no button held.</summary>
    private void EndPuckDrag()
    {
        _dragging = null;
        _dragVm = null;
    }

    private void OnPuckPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || _dragVm is null || Vm is null || _puckHost is null) return;
        var pos = e.GetPosition(_puckHost);
        var nx = (pos.X - PuckRadius) / Vm.StageWidth;
        var ny = (pos.Y - PuckRadius) / Vm.StageHeight;
        // Centred inverse mapping: keep in sync with CanvasX/CanvasY in MotionalSurroundPartViewModel.
        _dragVm.Lr = MotionalSurroundMapping.NormalizedToLrFb(nx);
        // Vertical axis is inverted to match the Integra-7's built-in editor: bottom = Front (-64),
        // top = Back (+63). The 1-ny mirrors the 1-normalized in CanvasY.
        _dragVm.Fb = MotionalSurroundMapping.NormalizedToLrFb(1 - ny);
    }

    private void OnPuckPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is null) return;
        e.Pointer.Capture(null);
        // Closes the gesture and, through it, clears the drag state. Idempotent, so it does not matter
        // that releasing the capture just above has already reached the same code through capture-lost.
        _puckGesture.End();
    }

    private void OnPuckKeyDown(object? sender, KeyEventArgs e)
    {
        if (FindPuck(e.Source) is { DataContext: MotionalSurroundPartViewModel p })
        {
            switch (e.Key)
            {
                case Key.Left: p.Lr -= 1; e.Handled = true; break;
                case Key.Right: p.Lr += 1; e.Handled = true; break;
                case Key.Up: p.Fb += 1; e.Handled = true; break;   // up = toward Back (+), bottom = Front
                case Key.Down: p.Fb -= 1; e.Handled = true; break;
            }
        }
    }

    private void CenterSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is { } p) { p.Lr = 0; p.Fb = 0; }
    }
}
