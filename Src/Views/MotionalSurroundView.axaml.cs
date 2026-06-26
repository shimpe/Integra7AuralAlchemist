using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    public MotionalSurroundView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
        // Re-measure the stage when the bound view model changes, so a fresh VM (e.g. after a
        // reconnect) gets the current room-map size instead of keeping its 1x1 default, which
        // would otherwise stack every puck in the top-left corner until the next layout pass.
        DataContextChanged += (_, _) => UpdateStage();
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
        _puckHost.AddHandler(PointerCaptureLostEvent, OnPuckPointerCaptureLost, RoutingStrategies.Bubble);
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

    private void OnPuckPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindPuck(e.Source) is { DataContext: MotionalSurroundPartViewModel p } b)
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            _dragging = b;
            _dragVm = p;
            if (Vm != null) Vm.SelectedPart = p;
            e.Pointer.Capture(b);
            b.Focus();
            e.Handled = true;
        }
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
        _dragging = null;
        _dragVm = null;
    }

    private void OnPuckPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = null;
        _dragVm = null;
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
