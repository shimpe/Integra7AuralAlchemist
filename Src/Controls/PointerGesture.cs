using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>An <see cref="EditGesture"/> held for the length of a pointer drag, closed by whichever of the
/// release and the loss of capture comes first.
///
/// The capture-lost half is why this is a class rather than two handlers. Avalonia declares
/// <c>PointerCaptureLostEvent</c> as a <b>Direct</b> routed event (asserted in
/// <c>EditRecordingTests.The_pointer_events_the_slider_gestures_hang_off_route_as_this_view_needs</c>), so it
/// is only ever delivered to the element that held the capture -- a handler on an ancestor can never run. The
/// element to hang it on is known at press time, though: <c>MouseDevice.MouseDown</c> captures the hit-tested
/// element before it raises <c>PointerPressed</c>, and neither <c>Slider</c> nor <c>Thumb</c> moves that
/// capture afterwards (neither calls <c>Pointer.Capture</c> at all), so the captured element reports the end
/// of the drag however the drag ends. A caller that does its own capturing passes whatever it captured.
///
/// Without that, a drag interrupted rather than released -- the window losing activation while the button is
/// down -- would leave the scope open and fold every later edit into that one step until
/// <see cref="EditJournal.StaleGestureWindow"/> gave up on it, which is containment and not a fix. Worse, the
/// depth counter it leaks is on the ambient journal, so the damage is not confined to the control that leaked
/// it: every edit anywhere in the application keeps the wider window until the process restarts.
///
/// Lived as a private class inside <c>MotionalSurroundView</c> until <see cref="SliderGesture"/> needed the
/// same thing and, written independently, got it wrong -- it hung the capture-lost handler on the Slider,
/// where a Direct event never arrives. One implementation, so the next caller inherits the reasoning above
/// rather than rediscovering it.
///
/// UI thread only, like the pointer handlers that drive it.</summary>
public sealed class PointerGesture
{
    private readonly EditGesture _gesture = new();
    private Interactive? _captureTarget;
    private Action? _onEnd;

    /// <param name="captureTarget">The element that will report the end of the drag: what
    /// <c>e.Pointer.Captured</c> names at press time, or whatever the caller captured itself.</param>
    /// <param name="onEnd">Run when the drag ends, however it ends. The Motional Surround pucks keep drag
    /// state of their own that has to be cleared on an interrupted drag as well as a released one -- left
    /// set, it makes the next pointer move over the room map drag the puck with no button held.</param>
    public void Begin(Interactive captureTarget, Action? onEnd = null)
    {
        End();
        _captureTarget = captureTarget;
        _onEnd = onEnd;
        captureTarget.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct);
        _gesture.Begin();
    }

    /// <summary>Idempotent, which is what lets the release, the capture loss and the next press all call it
    /// without any of them closing a gesture that is not theirs.</summary>
    public void End()
    {
        if (_captureTarget is { } t)
        {
            _captureTarget = null;
            t.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        }

        _gesture.End();
        var onEnd = _onEnd;
        _onEnd = null;
        onEnd?.Invoke();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => End();
}
