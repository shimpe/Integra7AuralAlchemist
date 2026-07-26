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
/// <item><description><b>Capture-lost is not handled here at all</b> -- <see cref="PointerGesture"/> attaches
/// it to the element that actually holds the capture, which for a fader drag is the Thumb and never the
/// Slider. <c>PointerCaptureLostEvent</c> is Direct, so a handler on the Slider could not fire however it
/// were registered. This file's first version got that wrong, and the cost of getting it wrong is not a
/// missed refinement: an interrupted drag leaves the gesture open, and the depth counter it leaks lives on
/// the ambient journal, so every edit anywhere in the application keeps the wider merge window until the
/// process restarts.</description></item>
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

    /// <summary>The gesture this slider is holding. One per slider, kept on the slider itself so nothing
    /// here has to track a lifetime the visual tree already tracks.
    ///
    /// A <see cref="PointerGesture"/> and not a bare <c>EditGesture</c>, which is the whole of what review
    /// found wrong with the first version of this file. A Slider never captures the pointer itself: the
    /// capture belongs to whatever was hit-tested, which for a fader drag is the Thumb inside the Slider's
    /// template. <c>PointerCaptureLostEvent</c> is Direct, so it is delivered only to that element and a
    /// handler here on the Slider could never fire -- and a drag interrupted rather than released would
    /// leave the gesture open, folding every later edit anywhere in the application into one undo step for
    /// the rest of the session. PointerGesture takes the captured element at press time and hangs the
    /// handler there, which is what Motional Surround's sliders have always done.</summary>
    private static readonly AttachedProperty<PointerGesture?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<SliderGesture, Slider, PointerGesture?>("Scope");

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

        // Ends a gesture the old handlers had open, so disabling mid-drag cannot leak one. PointerGesture
        // detaches its own capture-lost handler as it closes.
        slider.GetValue(ScopeProperty)?.End();
        slider.SetValue(ScopeProperty, null);

        if (e.NewValue is not true) return;

        slider.SetValue(ScopeProperty, new PointerGesture());
        slider.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        slider.AddHandler(InputElement.PointerReleasedEvent, OnReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider s) return;

        // Avalonia has already captured the element this press hit, in MouseDevice.MouseDown, before
        // raising the event -- so that is the element which will report the end of the drag. A press with no
        // capture is left to the journal's time window rather than given a gesture nothing would close.
        if (e.Pointer.Captured is Interactive captured) s.GetValue(ScopeProperty)?.Begin(captured);
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) s.GetValue(ScopeProperty)?.End();
    }
}
