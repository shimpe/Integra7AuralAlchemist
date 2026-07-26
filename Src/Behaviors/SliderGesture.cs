using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Integra7AuralAlchemist.Controls;

namespace Integra7AuralAlchemist.Behaviors;

/// <summary>Gives a <see cref="Slider"/> an undo-journal gesture: everything one drag changes becomes a
/// single undo step, however long the user takes over it. Set <c>SliderGesture.Enabled="True"</c> on the
/// slider.
///
/// Without it a slow drag is one undo step per <c>EditJournal.CoalesceWindow</c>, because the journal's clock
/// is all it has to go on when nothing tells it where a gesture began and ended -- see
/// <see cref="EditGesture"/> and the comment on <c>EditJournal.CoalesceWindow</c>. <c>RotaryKnobDial</c> does
/// this in its own pointer handlers; a stock Slider has nowhere to put them, hence a behaviour.
///
/// Two routing details, both learned the hard way in this codebase and neither optional:
///
/// <list type="bullet">
/// <item><description><b>Pressed and released are handled on the tunnel.</b> A Slider's Thumb handles the
/// press itself and marks it handled, so a bubbling handler on the Slider never sees it.</description></item>
/// <item><description><b>PointerCaptureLost is a Direct event.</b> Registering it with Bubble or Tunnel means
/// it never fires at all -- which is how an interrupted Motional Surround drag once left its puck following
/// the mouse. Capture is lost without a release whenever the window is deactivated mid-drag, and the gesture
/// has to close there or every later edit folds into it.</description></item>
/// </list>
///
/// Keyboard and wheel changes are deliberately not covered: they arrive as single changes with no press or
/// release to delimit, so they coalesce on the clock exactly as they do for a knob's arrow keys.</summary>
public sealed class SliderGesture
{
    private SliderGesture()
    {
    }

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<SliderGesture, Slider, bool>("Enabled");

    public static void SetEnabled(Slider control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Slider control) => control.GetValue(EnabledProperty);

    /// <summary>The gesture scope this slider is holding. One per slider, kept on the slider itself so
    /// nothing here has to track a lifetime the visual tree already tracks.</summary>
    private static readonly AttachedProperty<EditGesture?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<SliderGesture, Slider, EditGesture?>("Scope");

    static SliderGesture()
    {
        EnabledProperty.Changed.AddClassHandler<Slider>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(Slider slider, AvaloniaPropertyChangedEventArgs e)
    {
        // Removed unconditionally first, so toggling the property twice cannot leave two sets of handlers
        // attached. Static handler methods, so the delegates compare equal and RemoveHandler matches.
        slider.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        slider.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        slider.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);

        // Ends a gesture the old handlers had open, so disabling mid-drag cannot leak one.
        slider.GetValue(ScopeProperty)?.End();
        slider.SetValue(ScopeProperty, null);

        if (e.NewValue is not true) return;

        slider.SetValue(ScopeProperty, new EditGesture());
        slider.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        slider.AddHandler(InputElement.PointerReleasedEvent, OnReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        slider.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Slider s) s.GetValue(ScopeProperty)?.Begin();
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) s.GetValue(ScopeProperty)?.End();
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is Slider s) s.GetValue(ScopeProperty)?.End();
    }
}
