using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Every part's place in the mix on one page: sixteen part strips, the external input and the
/// master level.
///
/// Costs no reads. Every Studio Set Part block is already in memory -- PartViewModel reads all sixteen at
/// startup because SelectedPreset, the Motional Surround pucks and Save User Tone need them for parts nobody
/// has opened -- and so are Studio Set Common and System. This binds to those same live parameters, so a
/// front-panel move, a preset change and an edit on a part's own tab all reach the strips by themselves.
/// </summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private readonly ParamString _soloPart;
    private readonly List<IDisposable> _wrappers = [];
    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<INotifyPropertyChanged> _watchedPresets = [];
    private readonly PropertyChangedEventHandler _onPresetPropertyChanged;
    private readonly IReadOnlyList<PartViewModel> _parts;

    public ObservableCollection<MixerStripViewModel> PartStrips { get; } = [];
    public MixerStripViewModel ExternalStrip { get; }
    public MixerStripViewModel MasterStrip { get; }

    /// <param name="parts">The part view models, for their tone names. Index 0 is the Common tab, so part
    /// <c>i</c> is <c>parts[i + 1]</c> -- the same off-by-one every caller of PartViewModels lives with.</param>
    /// <param name="openPart">Take the user to a part's own tab. Zero-based part number.</param>
    /// <param name="openCommonTab">Show one of the Common tab's friendly editors, by the Tag on its TabItem.
    /// The strips' send knobs feed one shared chorus and one shared reverb, so their buttons all lead to the
    /// same two editors — one bus per effect.</param>
    public MixerViewModel(Integra7Domain domain, IReadOnlyList<PartViewModel> parts, Action<int> openPart,
        Action<string> openCommonTab)
    {
        _parts = parts;

        // Each part strip is handed both callbacks: openPart takes the user to the part's own tab, and
        // ToggleSolo writes the one shared Solo Part parameter this class owns. Handing solo down the same
        // way keeps the view's binding a plain method on the strip -- see MixerStripViewModel's _toggleSolo.
        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
            PartStrips.Add(Track(MixerStripViewModel.ForPart(domain, i, openPart, ToggleSolo, openCommonTab)));

        ExternalStrip = Track(MixerStripViewModel.ForExternal(domain, openCommonTab));
        MasterStrip = Track(MixerStripViewModel.ForMaster(domain));

        var common = domain.StudioSetCommon;
        var soloFqp = common.GetRelevantParameters(true, true)
            .First(p => p.ParSpec.Path == "Studio Set Common/Solo Part");
        _soloPart = new ParamString(common, soloFqp, _writer);
        _wrappers.Add(_soloPart);
        _soloPart.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParamString.Value)) ApplySoloToStrips();
        };

        // One handler for every preset, kept so it can be detached: a strip's caption follows its part's
        // SelectedPreset, which raises PropertyChanged both when the preset changes and when a user tone is
        // renamed under it.
        _onPresetPropertyChanged = (_, e) =>
        {
            // Only these two. A PartViewModel raises many properties, several of them repeatedly during a
            // part load, and this handler re-subscribes -- so an unfiltered version would re-enter itself
            // constantly for nothing. SelectedPreset means "watch a different object now"; Name means the
            // object being watched was renamed.
            if (e.PropertyName is nameof(PartViewModel.SelectedPreset) or nameof(Integra7Preset.Name))
                RefreshToneNames();
        };
        RefreshToneNames();
        ApplySoloToStrips();
    }

    /// <summary>Solo the given part, or clear solo if it is already the soloed one. One parameter, sixteen
    /// buttons -- see <see cref="SoloPartMapping"/>.</summary>
    public void ToggleSolo(int zeroBasedPartNo)
    {
        // Only a real part can be soloed. Every caller today is a part strip's own PartNo, which is 0..15
        // by construction, but SoloPartMapping.ValueForPart is deliberately unbounded and its failure mode
        // is the silent kind: a display string the parameter does not offer becomes raw 0 with no
        // diagnostic in Release (see ParamString's write), so an out-of-range number would quietly clear
        // solo instead of doing nothing. The External and Master strips carry PartNo -1.
        if (zeroBasedPartNo < 0 || zeroBasedPartNo >= Constants.NO_OF_PARTS) return;

        var soloed = SoloPartMapping.SoloedPart(_soloPart.Value);
        _soloPart.Value = soloed == zeroBasedPartNo
            ? SoloPartMapping.OffValue(_soloPart.Options)
            : SoloPartMapping.ValueForPart(zeroBasedPartNo);
    }

    private void ApplySoloToStrips()
    {
        var soloed = SoloPartMapping.SoloedPart(_soloPart.Value);
        foreach (var s in PartStrips) s.IsSoloed = s.PartNo == soloed;
    }

    /// <summary>Copy each part's preset name onto its strip, and re-subscribe to the presets themselves so a
    /// later rename arrives. Called on construction and whenever any watched preset changes -- a preset
    /// *change* means the object to watch is a different one.</summary>
    private void RefreshToneNames()
    {
        foreach (var p in _watchedPresets) p.PropertyChanged -= _onPresetPropertyChanged;
        _watchedPresets.Clear();

        foreach (var strip in PartStrips)
        {
            // parts[0] is the Common tab.
            var pvm = strip.PartNo + 1 < _parts.Count ? _parts[strip.PartNo + 1] : null;
            var preset = pvm?.SelectedPreset;
            strip.ToneName = preset?.Name?.Trim() ?? "";

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

    private T Track<T>(T disposable) where T : IDisposable
    {
        _wrappers.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        foreach (var p in _watchedPresets) p.PropertyChanged -= _onPresetPropertyChanged;
        _watchedPresets.Clear();
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
