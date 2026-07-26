using System;
using System.Collections.Generic;
using System.ComponentModel;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Every part's key and velocity range on one chart: sixteen <see cref="LayerZoneViewModel"/>s, the
/// snapshot list the chart draws, the selected part and its readout, and the three things a press on the chart
/// can ask the rest of the application to do.
///
/// <para>Costs no reads, for the same reason <see cref="MixerViewModel"/> does: all sixteen <c>Studio Set
/// Part</c> blocks are already in memory, read at startup because <c>SelectedPreset</c>, the Motional Surround
/// pucks and Save User Tone need them for parts nobody has opened. So a front-panel move, an edit on a part's own
/// Set Part tab and a drag on this chart all reach every other view of the same value by themselves, and this
/// page needs no spinner and no load state.</para>
///
/// <para>It knows nothing about <c>LayerMapControl</c>. Everything the control's three events carry lives in
/// <c>Models/Services</c> beside the geometry rather than in the control — a <see cref="LayerZone"/>, a
/// <see cref="PmtZoneMapping.Handle"/>, plain integers — so the view's code-behind unpacks each event and calls
/// the matching method here. That keeps
/// the one direction of dependency this codebase has (views know view models; view models know neither views nor
/// controls) and means nothing in this file would have to change if the chart were replaced.</para></summary>
public sealed class LayerMapViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<PartViewModel> _parts;
    private readonly Action<int> _openPart;
    private readonly Action<int> _openFades;
    private readonly Action<int, int, int> _audition;

    /// <summary>The sixteen zones, indexed by zero-based part number. Built in order and never reordered, so the
    /// index *is* the part — which is what lets a press, an edit and the readout all find a part's zone without
    /// searching.</summary>
    private readonly List<LayerZoneViewModel> _zones = [];

    private readonly List<INotifyPropertyChanged> _watchedPresets = [];
    private readonly PropertyChangedEventHandler _onPresetPropertyChanged;
    private readonly EventHandler _onZoneChanged;

    /// <summary>Set while sixteen tone names are being pushed in at once, so the chart is rebuilt once at the end
    /// instead of sixteen times over. See <see cref="RefreshToneNames"/>.</summary>
    private bool _rebuildDeferred;

    private IReadOnlyList<LayerZone> _zoneSnapshots = [];
    private int _selectedPart = -1;

    /// <param name="parts">The part view models, for their tone names. Index 0 is the Common tab, so part
    /// <c>i</c> is <c>parts[i + 1]</c> — the same off-by-one every caller of <c>PartViewModels</c> lives
    /// with.</param>
    /// <param name="openPart">Show a part's own tab: what a double-click on a zone means, and what the readout's
    /// first button does.</param>
    /// <param name="openFades">Show the part's Set Part tab, where the four fade-width knobs are. The map draws
    /// fades but does not drag them — a second handle on an edge a few pixels wide, four times per part, was the
    /// alternative — so this is the whole of how a user gets from seeing a crossfade to changing it, and it is
    /// separate from <paramref name="openPart"/> because it must land on a particular tab and not merely on the
    /// part.</param>
    /// <param name="audition">Sound a note on a part: the part, the note and the velocity the press resolved to.
    /// Passed on exactly as pressed, including a press outside the part's own range — the part ignores it and the
    /// silence is the chart answering "no, not here".</param>
    public LayerMapViewModel(Integra7Domain domain, IReadOnlyList<PartViewModel> parts, Action<int> openPart,
        Action<int> openFades, Action<int, int, int> audition)
    {
        _parts = parts;
        _openPart = openPart;
        _openFades = openFades;
        _audition = audition;

        // Assigned before the zones are built, because each one is subscribed with it as it is created.
        _onZoneChanged = (_, _) => RebuildZones();

        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            var zone = new LayerZoneViewModel(domain, i);
            zone.SnapshotChanged += _onZoneChanged;
            _zones.Add(zone);
        }

        // One handler for every part view model and every preset, kept so it can be detached. A part's tone name
        // follows its SelectedPreset, which raises PropertyChanged both when the preset changes and when a user
        // tone is renamed under it. Copied from MixerViewModel, filter included:
        //
        // only these two property names. A PartViewModel raises many properties, several of them repeatedly
        // during a part load, and this handler *re-subscribes* as it runs -- so an unfiltered version would
        // re-enter itself constantly, detaching and reattaching sixteen parts' worth of handlers for changes that
        // have nothing to do with which patch a part holds. SelectedPreset means "watch a different object now";
        // Name means the object being watched was renamed.
        _onPresetPropertyChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(PartViewModel.SelectedPreset) or nameof(Integra7Preset.Name))
                RefreshToneNames();
        };

        // Fills in the sixteen tone names and, at the end of it, builds the snapshot list for the first time.
        RefreshToneNames();
    }

    /// <summary>What the chart draws: one immutable snapshot per part.
    ///
    /// <para>Replaced wholesale whenever any value moves, never mutated in place. Sixteen small structs are
    /// cheaper to rebuild than to observe: a struct in a list cannot raise anything when its fields change, and
    /// the control is handed snapshots precisely so that what it is drawing cannot shift underneath it. So the
    /// new list *is* the notification — <c>RaiseAndSetIfChanged</c> compares by reference, a fresh list is always
    /// a different reference, and Avalonia's <c>AffectsRender</c> on the control's <c>Zones</c> property turns
    /// that into a redraw.</para></summary>
    public IReadOnlyList<LayerZone> Zones
    {
        get => _zoneSnapshots;
        private set => this.RaiseAndSetIfChanged(ref _zoneSnapshots, value);
    }

    /// <summary>The part whose numbers the readout is showing, zero-based, or -1 for none.
    ///
    /// <para>Two-way with the chart, and the chart is the side that usually discovers it: a press lands in a lane
    /// and that lane is the selection. Settable from here as well, so a view can preselect a part or clear the
    /// selection.</para></summary>
    public int SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (_selectedPart == value) return;
            this.RaiseAndSetIfChanged(ref _selectedPart, value);
            // The five readout properties are derived from the selection *and* from the selected part's values,
            // so they have to be re-raised from both ends. This is the selection end; RebuildZones is the other.
            RaiseReadout();
        }
    }

    /// <summary>Whether anything is selected. The view binds the readout's visibility to this rather than testing
    /// the strings for emptiness, so a genuinely empty tone name cannot be mistaken for "nothing selected".
    /// </summary>
    public bool HasSelection => SelectedSnapshot is not null;

    /// <summary>The selected part and the patch it holds: <c>Part 3 · Ac.Piano 1</c>.</summary>
    public string SelectionTitle => SelectedSnapshot is { } z ? LayerMapFormatting.Title(z) : "";

    /// <summary>The selected part's key range, as note names with the raw numbers beside them.</summary>
    public string SelectionKeyRange => SelectedSnapshot is { } z ? LayerMapFormatting.KeyRange(z) : "";

    /// <summary>The selected part's velocity range.</summary>
    public string SelectionVelocityRange => SelectedSnapshot is { } z ? LayerMapFormatting.VelocityRange(z) : "";

    /// <summary>The selected part's four fade widths, which the chart draws and does not edit — so this is also
    /// what tells the user whether <see cref="EditSelectedFades"/> has anything to show them.</summary>
    public string SelectionFades => SelectedSnapshot is { } z ? LayerMapFormatting.Fades(z) : "";

    /// <summary>Write a dragged zone's values to the instrument.
    ///
    /// <para>Takes the <see cref="LayerZone"/> and the handle out of the chart's <c>ZoneEdited</c> event rather
    /// than the event arguments themselves, so this class stays clear of the control's namespace: the view's
    /// code-behind writes <c>vm.ApplyEdit(e.Zone, e.Handle)</c>. The zone carries its own part number, because
    /// the drag was captured on one part and stays on it however far the pointer wanders — re-deriving the part
    /// from the pointer's current lane is exactly the bug the control's comments warn about, so the part travels
    /// with the values.</para>
    ///
    /// <para>Only the values that actually changed are written, and only those the handle owns; see
    /// <see cref="LayerZoneViewModel.Apply"/> and <see cref="LayerZoneChanges"/> for why both halves are needed
    /// and why neither is left to the wrapper's own no-op guard.</para>
    ///
    /// <para><paramref name="handle"/> comes straight off the event, and passing it on is not optional: the zone
    /// carries the drag's <i>press-time</i> values in the seven fields it is not moving, so without the handle to
    /// confine the write, a value the instrument changed mid-drag would be reverted to what it was when the
    /// pointer went down. <see cref="LayerZoneChanges.FieldsFor"/> spells the scenario out.</para></summary>
    public void ApplyEdit(LayerZone zone, PmtZoneMapping.Handle handle)
    {
        // A snapshot for a part outside 0..15 cannot be written anywhere sensible. It should not arrive -- the
        // control only ever raises zones it was given -- but the alternative to checking is an index out of
        // range in the middle of a drag.
        if (zone.PartNo < 0 || zone.PartNo >= _zones.Count) return;
        _zones[zone.PartNo].Apply(zone, handle);
    }

    /// <summary>Show a part's own tab: what a double-click on its lane means.</summary>
    public void OpenPart(int zeroBasedPartNo)
    {
        if (zeroBasedPartNo < 0 || zeroBasedPartNo >= _zones.Count) return;
        _openPart(zeroBasedPartNo);
    }

    /// <summary>Sound a note on a part, so the user can hear whether it answers where they pressed. Both numbers
    /// are passed on as pressed — see the constructor's <c>audition</c> parameter.</summary>
    public void Audition(int zeroBasedPartNo, int note, int velocity)
    {
        if (zeroBasedPartNo < 0 || zeroBasedPartNo >= _zones.Count) return;
        _audition(zeroBasedPartNo, note, velocity);
    }

    /// <summary>Show the selected part's own tab. Parameterless so the view binds it as a command with no
    /// <c>CommandParameter</c> and the XAML compiler type-checks the binding — the same reason
    /// <see cref="MixerStripViewModel.ToggleSolo"/> takes no argument.</summary>
    public void OpenSelectedPart() => OpenPart(SelectedPart);

    /// <summary>Show the selected part's Set Part tab, where the four fade-width knobs are. Parameterless for the
    /// reason <see cref="OpenSelectedPart"/> is.</summary>
    public void EditSelectedFades()
    {
        if (SelectedPart < 0 || SelectedPart >= _zones.Count) return;
        _openFades(SelectedPart);
    }

    /// <summary>The selected part's values as of now, or null when nothing is selected. Taken fresh rather than
    /// read out of <see cref="Zones"/>: the two agree, but this way the readout cannot show stale numbers if it
    /// is ever asked between a value moving and the list being rebuilt.</summary>
    private LayerZone? SelectedSnapshot
        => _selectedPart >= 0 && _selectedPart < _zones.Count ? _zones[_selectedPart].Snapshot() : null;

    /// <summary>Take sixteen fresh snapshots and publish them. Called whenever any zone reports a change, from
    /// any of the three directions a value can move: a drag on this chart, an edit on the part's own tab, or the
    /// instrument's front panel.</summary>
    private void RebuildZones()
    {
        if (_rebuildDeferred) return;

        var list = new List<LayerZone>(_zones.Count);
        foreach (var zone in _zones) list.Add(zone.Snapshot());
        Zones = list;

        // The readout's four strings and HasSelection are derived from the selected part's values, so a value
        // moving has to re-raise them -- otherwise dragging a selected part's edge would move the box on the
        // chart and leave the numbers beneath it showing where the edge used to be.
        RaiseReadout();
    }

    /// <summary>Copy each part's preset name onto its zone, and re-subscribe to the presets themselves so a later
    /// rename arrives. Called on construction and whenever any watched object changes — a preset *change* means
    /// the object to watch is now a different one.</summary>
    private void RefreshToneNames()
    {
        foreach (var p in _watchedPresets) p.PropertyChanged -= _onPresetPropertyChanged;
        _watchedPresets.Clear();

        // Sixteen names are pushed in one pass and each push raises SnapshotChanged, so without deferring, one refresh
        // rebuilds the snapshot list sixteen times and redraws the chart sixteen times -- and since a part load
        // raises SelectedPreset more than once, a single load would do that repeatedly. Rebuilt once, at the end,
        // where the values are all in place.
        _rebuildDeferred = true;
        try
        {
            foreach (var zone in _zones)
            {
                // parts[0] is the Common tab.
                var pvm = zone.PartNo + 1 < _parts.Count ? _parts[zone.PartNo + 1] : null;
                var preset = pvm?.SelectedPreset;
                zone.ToneName = preset?.Name?.Trim() ?? "";

                if (preset is not null)
                {
                    preset.PropertyChanged += _onPresetPropertyChanged;
                    _watchedPresets.Add(preset);
                }

                if (pvm is not null)
                {
                    pvm.PropertyChanged += _onPresetPropertyChanged;
                    _watchedPresets.Add(pvm);
                }
            }
        }
        finally
        {
            // In a finally so a throw part-way through cannot leave the chart permanently frozen: a stuck flag
            // would silently stop every later value change from ever reaching the control.
            _rebuildDeferred = false;
        }

        RebuildZones();
    }

    private void RaiseReadout()
    {
        this.RaisePropertyChanged(nameof(HasSelection));
        this.RaisePropertyChanged(nameof(SelectionTitle));
        this.RaisePropertyChanged(nameof(SelectionKeyRange));
        this.RaisePropertyChanged(nameof(SelectionVelocityRange));
        this.RaisePropertyChanged(nameof(SelectionFades));
    }

    public void Dispose()
    {
        // Everything this class attached, in the reverse of the order it was attached. The preset handlers reach
        // objects that outlive the map -- PartViewModels and their presets survive a tab being rebuilt -- so
        // leaving them attached would keep a disposed map alive and have it go on rebuilding a snapshot list
        // nothing draws. MainWindowViewModel disposes this wherever it disposes MixerVm, the rescan included, for
        // exactly that reason.
        foreach (var p in _watchedPresets) p.PropertyChanged -= _onPresetPropertyChanged;
        _watchedPresets.Clear();

        foreach (var zone in _zones)
        {
            zone.SnapshotChanged -= _onZoneChanged;
            zone.Dispose();
        }

        _zones.Clear();
    }
}
