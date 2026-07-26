using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One part's key and velocity zone: the eight <c>Studio Set Part</c> parameters the layer map draws,
/// wrapped in the same <see cref="ParamInt"/> every friendly editor uses.
///
/// <para>The shape is <see cref="MixerStripViewModel"/>'s, one layer down, and for the same reasons: the wrapper
/// gives live writes through <see cref="ThrottledParameterWriter"/>, the echo of a front-panel move, and an
/// <c>EditJournal</c> entry per edit, none of which is re-implemented here. Every one of those eight parameters
/// is already in memory — <c>PartViewModel</c> reads all sixteen parts' blocks at startup — so sixteen of these
/// cost no reads and need no load state.</para>
///
/// <para>It also wraps the same eight parameters <see cref="StudioSetPartEditorViewModel"/> does, which is not a
/// duplication to tidy away: both wrap the <i>same</i> <see cref="FullyQualifiedParameter"/> objects out of the
/// same domain, so an edit on either page reaches the other through the model's own
/// <c>PropertyChanged</c> without either knowing the other exists. That is how the mixer and the part tabs
/// already agree, and it is why <see cref="SnapshotChanged"/> below has to fire for a device-side change as well
/// as for a drag.</para>
///
/// <para>The tone name is pushed in from outside rather than resolved here. <see cref="LayerMapViewModel"/> takes
/// it from the part's own <c>SelectedPreset</c>, which is what the rest of the application treats as the answer
/// to "which patch is this part holding"; deriving it a second way here would be a second answer to disagree
/// with.</para></summary>
public sealed class LayerZoneViewModel : ViewModelBase, IDisposable
{
    /// <summary>Parameter path prefix. The eight paths below are the ones
    /// <see cref="StudioSetPartEditorViewModel"/> already resolves out of this same domain — a missing path is a
    /// <c>KeyNotFoundException</c> at construction and not a compile error, so matching an existing caller
    /// verbatim is the check.</summary>
    private const string P = "Studio Set Part/";

    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<IDisposable> _wrappers = [];

    /// <summary>The eight wrappers this view model watches, kept so <see cref="Dispose"/> can detach
    /// <see cref="_onParamChanged"/> from each. They are the same objects as <see cref="_wrappers"/> holds; the
    /// second list exists because that one is typed for disposal and this one for unsubscribing.</summary>
    private readonly List<ParamInt> _watched = [];

    /// <summary>One handler over all eight parameters, kept in a field so it can be detached again. Filtered on
    /// <c>Value</c>: a <see cref="ParamInt"/> raises nothing else today, but an unfiltered handler would turn any
    /// property added to it into a chart rebuild.</summary>
    private readonly PropertyChangedEventHandler _onParamChanged;

    /// <summary>Zero-based, like every part number this feature passes around. The "+ 1" happens where the label
    /// is built and nowhere else.</summary>
    public int PartNo { get; }

    /// <summary>What the chart writes in this part's lane: its one-based number.</summary>
    public string Label { get; }

    // The four range values, and the four fade widths beside them. All eight are 0..127 in the parameter
    // database (Studio Set Part offsets 0x1d..0x24), and all eight are wrapped here rather than only the four the
    // map can drag: the chart *draws* the fades, so a snapshot without them would draw every part as a hard
    // split.

    public ParamInt KeyLo { get; }
    public ParamInt KeyHi { get; }
    public ParamInt VelLo { get; }
    public ParamInt VelHi { get; }
    public ParamInt KeyFadeLo { get; }
    public ParamInt KeyFadeHi { get; }
    public ParamInt VelFadeLo { get; }
    public ParamInt VelFadeHi { get; }

    /// <summary>Any of the nine things a snapshot carries has moved — one of the eight values, or the tone name.
    ///
    /// <para>One event over eight parameters, so the parent subscribes to sixteen things rather than a hundred
    /// and twenty-eight, and so the knowledge of which parameters make up a zone stays in the class that holds
    /// them. It fires for a drag, for an edit made on the part's own Set Part tab, and for a front-panel move,
    /// because all three arrive the same way: as a change on the wrapped parameter.</para>
    ///
    /// <para>Raised on the UI thread. A device-side change reaches <see cref="ParamInt"/> off-thread and is
    /// posted to the dispatcher there, before the value moves — so a handler that raises
    /// <c>PropertyChanged</c> for something a control is bound to, which is exactly what the parent does, does
    /// not have to marshal again.</para>
    ///
    /// <para>Named for the snapshot rather than called <c>Changed</c>, which is taken:
    /// <c>ReactiveObject.Changed</c> is an observable of property changes, and an event of this name would hide
    /// it — a warning today and a genuine trap later, since anything reaching for the ReactiveUI member on a
    /// zone would silently get this instead.</para></summary>
    public event EventHandler? SnapshotChanged;

    private string _toneName = "";

    /// <summary>Which patch the part holds, for the label drawn beside the part number. Pushed in by
    /// <see cref="LayerMapViewModel"/>; see the class comment for why it is not resolved here.</summary>
    public string ToneName
    {
        get => _toneName;
        set
        {
            if (_toneName == value) return;
            this.RaiseAndSetIfChanged(ref _toneName, value);
            // Part of the snapshot, so the chart has to be rebuilt for it as much as for a value -- otherwise a
            // preset change would relabel the mixer and leave the map naming the previous patch.
            OnChanged();
        }
    }

    public LayerZoneViewModel(Integra7Domain domain, int zeroBasedPartNo)
    {
        PartNo = zeroBasedPartNo;
        Label = (zeroBasedPartNo + 1).ToString(CultureInfo.InvariantCulture);

        // Assigned before the wrappers are built, because Watch subscribes with it.
        _onParamChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ParamInt.Value)) OnChanged();
        };

        var part = domain.StudioSetPart(zeroBasedPartNo);
        var byPath = ToDict(part);

        // Every one of the eight is 0..127, so unlike the mixer's strip there is nothing to vary per parameter
        // and the local takes only the name.
        ParamInt PI(string n) => Watch(Track(new ParamInt(part, byPath[P + n], _writer, 0, 127)));

        KeyLo = PI("Keyboard Range Lower");
        KeyHi = PI("Keyboard Range Upper");
        KeyFadeLo = PI("Keyboard Fade Width Lower");
        KeyFadeHi = PI("Keyboard Fade Width Upper");
        VelLo = PI("Velocity Range Lower");
        VelHi = PI("Velocity Range Upper");
        VelFadeLo = PI("Velocity Fade Width Lower");
        VelFadeHi = PI("Velocity Fade Width Upper");
    }

    /// <summary>This part's contribution to the chart, as of now.
    ///
    /// <para>A fresh struct on every call, not a cached one kept in step: the control is handed immutable
    /// snapshots precisely so that nothing it draws can change under it mid-render, and the way to keep that
    /// promise is for a snapshot to be a reading and never a view. Nine field reads, so the parent can afford to
    /// take sixteen of them whenever anything moves.</para></summary>
    public LayerZone Snapshot() => new(
        PartNo,
        KeyLo.Value, KeyHi.Value,
        VelLo.Value, VelHi.Value,
        KeyFadeLo.Value, KeyFadeHi.Value,
        VelFadeLo.Value, VelFadeHi.Value,
        Label, ToneName);

    /// <summary>Write the values in <paramref name="target"/> that differ from the ones held now, and only
    /// those.
    ///
    /// <para><c>LayerMapControl</c> raises the whole zone on every pointer move that resolves to new
    /// values — deliberately, so every rule about what a drag means lives in one tested function — which makes
    /// this end responsible for not writing the seven values the drag did not touch. Each write is a sysex round
    /// trip and an undo entry, so a key drag that also rewrote the velocity range would spend traffic on values
    /// that did not move and leave the user pressing Undo twice for one gesture.
    /// <see cref="LayerZoneChanges.Between"/> names the difference, in a class a test can reach; its own comment
    /// explains why leaving the job to <see cref="ParamInt"/>'s no-op guard is not the same thing.</para></summary>
    public void Apply(LayerZone target)
    {
        var changed = LayerZoneChanges.Between(Snapshot(), target);
        if (changed == LayerZoneField.None) return;

        // Written in the order the fields are declared, which has no significance: these are eight independent
        // parameters at eight addresses, each throttled under its own key, and none of them constrains another
        // on this side. ResolveDrag has already guaranteed lo <= hi, so there is no intermediate state here that
        // a device could object to.
        if (changed.HasFlag(LayerZoneField.KeyLo)) KeyLo.Value = target.KeyLo;
        if (changed.HasFlag(LayerZoneField.KeyHi)) KeyHi.Value = target.KeyHi;
        if (changed.HasFlag(LayerZoneField.VelLo)) VelLo.Value = target.VelLo;
        if (changed.HasFlag(LayerZoneField.VelHi)) VelHi.Value = target.VelHi;
        if (changed.HasFlag(LayerZoneField.KeyFadeLo)) KeyFadeLo.Value = target.KeyFadeLo;
        if (changed.HasFlag(LayerZoneField.KeyFadeHi)) KeyFadeHi.Value = target.KeyFadeHi;
        if (changed.HasFlag(LayerZoneField.VelFadeLo)) VelFadeLo.Value = target.VelFadeLo;
        if (changed.HasFlag(LayerZoneField.VelFadeHi)) VelFadeHi.Value = target.VelFadeHi;
    }

    private void OnChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T wrapper) where T : IDisposable
    {
        _wrappers.Add(wrapper);
        return wrapper;
    }

    private ParamInt Watch(ParamInt p)
    {
        p.PropertyChanged += _onParamChanged;
        _watched.Add(p);
        return p;
    }

    public void Dispose()
    {
        // Detached explicitly rather than left to the wrappers being dropped. The wrappers are this object's
        // own, so the handlers would die with them either way -- but a disposed view model that can still raise
        // SnapshotChanged is a trap for the next reader, and the parent's own Dispose relies on this one being
        // final.
        foreach (var p in _watched) p.PropertyChanged -= _onParamChanged;
        _watched.Clear();
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
