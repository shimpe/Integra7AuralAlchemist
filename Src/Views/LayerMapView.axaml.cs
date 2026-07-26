using Avalonia.Controls;
using Integra7AuralAlchemist.Controls;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

/// <summary>The Layers page: <see cref="LayerMapControl"/> over the whole tab, and the selected part's numbers
/// and its two buttons beneath it.
///
/// <para>The chart's three events are wired here by hand, the way <c>MotionalSurroundView</c> wires its pucks,
/// rather than bound in the markup. They carry event-argument classes that live beside the control in
/// <c>Controls</c>, and unpacking them here — <c>vm.ApplyEdit(e.Zone, e.Handle)</c> and not
/// <c>vm.ApplyEdit(e)</c> — is what keeps <see cref="LayerMapViewModel"/> free of any reference to the control
/// that draws it. That is the one direction of dependency this codebase has: views know view models, and view
/// models know neither views nor controls.</para></summary>
public partial class LayerMapView : UserControl
{
    public LayerMapView()
    {
        InitializeComponent();

        // Attached once, for the life of the view. The view model is read at each event instead of being
        // captured here, so a DataContext replaced later — a reconnect or a rescan builds a fresh
        // LayerMapViewModel — is picked up without re-wiring anything.
        Map.ZoneEdited += OnZoneEdited;
        Map.ZoneActivated += OnZoneActivated;
        Map.AuditionRequested += OnAuditionRequested;
    }

    private LayerMapViewModel? Vm => DataContext as LayerMapViewModel;

    /// <summary>A drag resolved to new values. The handle travels with them and is passed on: the zone carries
    /// the drag's press-time values in the seven fields it is not moving, so the handle is what tells the view
    /// model which single field this gesture is entitled to write. See <c>LayerZoneEditedEventArgs.Handle</c>.
    /// </summary>
    private void OnZoneEdited(object? sender, LayerZoneEditedEventArgs e) => Vm?.ApplyEdit(e.Zone, e.Handle);

    /// <summary>A zone was double-clicked: show that part's own tab.</summary>
    private void OnZoneActivated(object? sender, LayerPartEventArgs e) => Vm?.OpenPart(e.PartNo);

    /// <summary>A press in a lane: sound that part at the note and velocity the press resolved to. Passed on
    /// exactly as pressed, including a press outside the part's own range — the part ignores it and the silence
    /// is the chart answering "no, not here".</summary>
    private void OnAuditionRequested(object? sender, LayerAuditionEventArgs e)
        => Vm?.Audition(e.PartNo, e.Note, e.Velocity);
}
