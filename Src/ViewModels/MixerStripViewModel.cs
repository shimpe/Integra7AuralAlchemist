using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>What a strip is: a part, the external audio input, or the master level. They differ in which
/// controls exist, not in how any of them works, so the flags below are what the view binds visibility to.
/// </summary>
public enum MixerStripKind
{
    Part,
    External,
    Master,
}

/// <summary>One channel strip. Wraps the live <see cref="FullyQualifiedParameter"/>s of its own domain
/// through the same wrappers every friendly editor uses, which is what gives it live writes, front-panel
/// echo and undo recording without a line of new machinery: the wrapper's setter writes through
/// <see cref="ThrottledParameterWriter"/> and records to the journal, and the model's PropertyChanged brings
/// a device-side change back the other way.
///
/// Nullable properties are the ones a given kind of strip does not have -- the external input has no pan and
/// no output assignment, and the master strip has nothing but a level. The view binds their visibility to the
/// Has* flags rather than testing for null, so an accidental null on a part strip shows as a missing control
/// rather than as a silently skipped binding.</summary>
public sealed partial class MixerStripViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<IDisposable> _wrappers = [];
    private readonly Action<int>? _openPart;

    /// <summary>Ask the mixer to toggle solo on this strip's part. A callback rather than the view
    /// reaching the mixer through an ancestor binding: solo is one shared parameter, so the strip cannot
    /// own it, but <c>{Binding $parent[ItemsControl].((vm:MixerViewModel)DataContext).ToggleSolo}</c> would
    /// resolve at runtime only, silently doing nothing if any part of that path were wrong -- and it does
    /// not exist at all for the External and Master strips, which are single ContentControls with no
    /// ItemsControl above them. This is the same shape as <see cref="_openPart"/>, which the view already
    /// reaches as a plain method binding.</summary>
    private readonly Action<int>? _toggleSolo;

    /// <summary>Show one of the Common tab's friendly editors, named by the <c>Tag</c> its TabItem carries in
    /// <c>MainWindow.axaml</c>. A strip's two send knobs say how much of this part is fed to the shared
    /// chorus and reverb; what those units then do is one setting each for the whole Studio Set, so the
    /// buttons under the knobs go to the same two editors from every strip. That is not a redundancy to
    /// tidy away -- it is one bus per effect, and the knob and the button are the two halves of it.</summary>
    private readonly Action<string>? _openCommonTab;

    public MixerStripKind Kind { get; }

    /// <summary>Zero-based part number, or -1 for the external and master strips.</summary>
    public int PartNo { get; }

    /// <summary>What the strip is called: "1".."16", "Ext", "Master".</summary>
    public string Label { get; }

    public ParamInt Level { get; private set; } = null!;
    public ParamInt? Pan { get; private set; }
    public ParamInt? ChorusSend { get; private set; }
    public ParamInt? ReverbSend { get; private set; }
    public ParamString? OutputAssign { get; private set; }
    public ParamBool? Mute { get; private set; }

    public bool HasPan => Pan is not null;
    public bool HasSends => ChorusSend is not null;
    public bool HasOutput => OutputAssign is not null;
    public bool HasMute => Mute is not null;
    public bool IsPart => Kind == MixerStripKind.Part;

    /// <summary>Which tone the part holds, for the strip's caption. Pushed in by
    /// <see cref="MixerViewModel"/> from the part's own <c>SelectedPreset</c> rather than derived here: that
    /// property is what the rest of the application treats as the answer, and it raises PropertyChanged, so
    /// a preset change or a user-tone rename reaches the strip by itself.</summary>
    [Reactive] private string _toneName = "";

    /// <summary>Whether this strip is the one the instrument is soloing. Set by
    /// <see cref="MixerViewModel"/>, which owns the single Solo Part parameter the strips share.</summary>
    [Reactive] private bool _isSoloed;

    /// <summary>Pan as the instrument labels it, recomputed whenever the value moves.</summary>
    public string PanLabel => Pan is null ? "" : MixerFormatting.PanLabel(Pan.Value);

    private MixerStripViewModel(MixerStripKind kind, int partNo, string label, Action<int>? openPart,
        Action<int>? toggleSolo, Action<string>? openCommonTab)
    {
        Kind = kind;
        PartNo = partNo;
        Label = label;
        _openPart = openPart;
        _toggleSolo = toggleSolo;
        _openCommonTab = openCommonTab;
    }

    /// <summary>A part strip, over that part's own Studio Set Part block.</summary>
    public static MixerStripViewModel ForPart(Integra7Domain domain, int zeroBasedPartNo,
        Action<int>? openPart, Action<int>? toggleSolo, Action<string>? openCommonTab)
    {
        const string p = "Studio Set Part/";
        var d = domain.StudioSetPart(zeroBasedPartNo);
        var byPath = ToDict(d);
        var vm = new MixerStripViewModel(MixerStripKind.Part, zeroBasedPartNo,
            (zeroBasedPartNo + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), openPart,
            toggleSolo, openCommonTab);

        vm.Level = vm.Track(new ParamInt(d, byPath[p + "Part Level"], vm._writer, 0, 127));
        vm.Pan = vm.Track(new ParamInt(d, byPath[p + "Part Pan"], vm._writer, -64, 63));
        vm.ChorusSend = vm.Track(new ParamInt(d, byPath[p + "Part Chorus Send Level"], vm._writer, 0, 127));
        vm.ReverbSend = vm.Track(new ParamInt(d, byPath[p + "Part Reverb Send Level"], vm._writer, 0, 127));
        vm.OutputAssign = vm.Track(new ParamString(d, byPath[p + "Part Output Assign"], vm._writer));
        // Mute reads OFF/MUTE rather than the usual OFF/ON, so its two words are given explicitly -- the
        // same call StudioSetPartEditorViewModel makes.
        vm.Mute = vm.Track(new ParamBool(d, byPath[p + "Mute Switch"], vm._writer, "Mute On", "Mute Off"));
        vm.WatchPan();
        return vm;
    }

    /// <summary>The external input's strip, over Studio Set Common. No pan and no output assignment: the
    /// parameters do not exist.</summary>
    public static MixerStripViewModel ForExternal(Integra7Domain domain, Action<string>? openCommonTab)
    {
        const string p = "Studio Set Common/";
        var d = domain.StudioSetCommon;
        var byPath = ToDict(d);
        // It does get the effect buttons: the input is fed to the same shared chorus and reverb as the parts.
        var vm = new MixerStripViewModel(MixerStripKind.External, -1, "Ext", null, null, openCommonTab);

        vm.Level = vm.Track(new ParamInt(d, byPath[p + "Ext Part Level"], vm._writer, 0, 127));
        vm.ChorusSend =
            vm.Track(new ParamInt(d, byPath[p + "Ext Part Chorus Send Level"], vm._writer, 0, 127));
        vm.ReverbSend =
            vm.Track(new ParamInt(d, byPath[p + "Ext Part Reverb Send Level"], vm._writer, 0, 127));
        vm.Mute = vm.Track(new ParamBool(d, byPath[p + "Ext Part Mute Switch"], vm._writer));
        vm.ToneName = "External input";
        return vm;
    }

    /// <summary>The master level, which is a System parameter and therefore global: it is not part of the
    /// Studio Set, it survives a Studio Set change, and a snapshot does not carry it. The view says so; this
    /// exists so the mixer can show the level everything else is going through.</summary>
    public static MixerStripViewModel ForMaster(Integra7Domain domain)
    {
        var d = domain.System;
        var vm = new MixerStripViewModel(MixerStripKind.Master, -1, "Master", null, null, null);

        // By parameter Name, not by path -- the System domain's paths are prefixed "System Common/" rather
        // than "System/", and SystemEditorViewModel resolves this domain by Name for exactly that reason.
        // Following it keeps one idiom for one domain.
        var byName = d.GetRelevantParameters(true, true).First(p => p.ParSpec.Name == "Master Level");
        vm.Level = vm.Track(new ParamInt(d, byName, vm._writer, 0, 127));
        vm.ToneName = "System, not Studio Set";
        return vm;
    }

    /// <summary>Open this part's own tab. The strip is a summary; everything it does not show is one click
    /// away, which is the whole point of the click-through.</summary>
    public void OpenPart() => _openPart?.Invoke(PartNo);

    /// <summary>Solo this part, or clear solo if it is already the soloed one. The mixer does the work --
    /// see <see cref="_toggleSolo"/> for why the strip carries the call rather than the view reaching the
    /// mixer itself. Parameterless, so the view binds it as a command with no CommandParameter and the
    /// XAML compiler type-checks it.</summary>
    public void ToggleSolo() => _toggleSolo?.Invoke(PartNo);

    /// <summary>Show the friendly Chorus editor — what the chorus this strip is sending to actually does.
    /// The tag is the one on that TabItem in <c>MainWindow.axaml</c>; passing tags as strings is how the
    /// friendly editors' "Advanced …" buttons already navigate.</summary>
    public void OpenChorus() => _openCommonTab?.Invoke("COMMON-CHORUS-FRIENDLY");

    /// <summary>Show the friendly Reverb editor. See <see cref="OpenChorus"/>.</summary>
    public void OpenReverb() => _openCommonTab?.Invoke("COMMON-REVERB-FRIENDLY");

    /// <summary>Keep <see cref="PanLabel"/> in step with the value. A derived string over a wrapper that
    /// raises its own PropertyChanged still has to be told to re-read.</summary>
    private void WatchPan()
    {
        if (Pan is null) return;
        Pan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParamInt.Value)) this.RaisePropertyChanged(nameof(PanLabel));
        };
    }

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

    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
