using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class MotionalSurroundView : UserControl
{
    private const double PuckRadius = 14; // half of the 28px puck

    private ItemsControl? _puckHost;
    private Border? _dragging;
    private MotionalSurroundPartViewModel? _dragVm;

    public MotionalSurroundView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
    }

    private MotionalSurroundViewModel? Vm => DataContext as MotionalSurroundViewModel;

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (_puckHost != null) return;
        _puckHost = this.FindControl<ItemsControl>("PuckHost");
        if (_puckHost is null) return;
        _puckHost.AddHandler(PointerPressedEvent, OnPuckPointerPressed, RoutingStrategies.Tunnel);
        _puckHost.AddHandler(PointerMovedEvent, OnPuckPointerMoved, RoutingStrategies.Tunnel);
        _puckHost.AddHandler(PointerReleasedEvent, OnPuckPointerReleased, RoutingStrategies.Tunnel);
        _puckHost.AddHandler(PointerCaptureLostEvent, OnPuckPointerCaptureLost, RoutingStrategies.Bubble);
        _puckHost.AddHandler(KeyDownEvent, OnPuckKeyDown, RoutingStrategies.Bubble);
        _puckHost.PropertyChanged += (_, ev) => { if (ev.Property == BoundsProperty) UpdateStage(); };
        UpdateStage();
    }

    private void UpdateStage()
    {
        if (Vm is null || _puckHost is null) return;
        var b = _puckHost.Bounds;
        if (b.Width > 2 * PuckRadius) Vm.StageWidth = b.Width - 2 * PuckRadius;
        if (b.Height > 2 * PuckRadius) Vm.StageHeight = b.Height - 2 * PuckRadius;
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
        _dragVm.Lr = MotionalSurroundMapping.FromNormalized(nx,
            MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
        _dragVm.Fb = MotionalSurroundMapping.FromNormalized(ny,
            MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
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
                case Key.Up: p.Fb -= 1; e.Handled = true; break;   // up = toward Front (-)
                case Key.Down: p.Fb += 1; e.Handled = true; break;
            }
        }
    }

    private void CenterSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is { } p) { p.Lr = 0; p.Fb = 0; }
    }
}
