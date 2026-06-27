using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One WMT layer (1..4) of a PCM drum key: a velocity-switched wave plus its tweaks and
/// velocity range. Mirrors the PCM Synth partial's wave handling.</summary>
public sealed class PCMDrumWmtLayerViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public int Index { get; }                 // 1..4
    public string Title => $"WMT{Index}";

    public ParamBool WaveSwitch { get; }
    public ParamString WaveGroupType { get; }
    public ParamString WaveNumberL { get; }
    public ParamString WaveNumberR { get; }
    public ParamString WaveGain { get; }
    public ParamInt Level { get; }
    public ParamInt Pan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamBool FxmSwitch { get; }
    public ParamInt FxmColor { get; }
    public ParamInt FxmDepth { get; }
    public ParamBool TempoSync { get; }
    public ParamBool RandomPanSwitch { get; }
    public ParamString AlternatePanSwitch { get; }
    public ParamInt RangeLower { get; }
    public ParamInt RangeUpper { get; }
    public ParamInt FadeLower { get; }
    public ParamInt FadeUpper { get; }

    /// <summary>All wrapped params, for the parent's copy/paste set.</summary>
    public IReadOnlyList<IParam> Params { get; }

    public PCMDrumWmtLayerViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var pre = $"WMT{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[PP + pre + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[PP + pre + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[PP + pre + n], writer));

        WaveSwitch = PB("Wave Switch");
        WaveGroupType = PS("Wave Group Type");
        WaveNumberL = PS("Wave Number L (Mono)");
        WaveNumberR = PS("Wave Number R");
        WaveGain = PS("Wave Gain", new[] { "-6", "0", "6", "12" });
        Level = PI("Wave Level", 0, 127);
        Pan = PI("Wave Pan", -64, 63);
        CoarseTune = PI("Wave Coarse Tune", -48, 48);
        FineTune = PI("Wave Fine Tune", -50, 50);
        FxmSwitch = PB("Wave FXM Switch");
        FxmColor = PI("Wave FXM Color", 1, 4);
        FxmDepth = PI("Wave FXM Depth", 0, 16);
        TempoSync = PB("Wave Tempo Sync");
        RandomPanSwitch = PB("Random Pan Switch");
        AlternatePanSwitch = PS("Alternate Pan Switch");
        RangeLower = PI("Velocity Range Lower", 0, 127);
        RangeUpper = PI("Velocity Range Upper", 0, 127);
        FadeLower = PI("Velocity Fade Width Lower", 0, 127);
        FadeUpper = PI("Velocity Fade Width Upper", 0, 127);

        Params = new IParam[]
        {
            WaveSwitch, WaveGroupType, WaveNumberL, WaveNumberR, WaveGain, Level, Pan, CoarseTune,
            FineTune, FxmSwitch, FxmColor, FxmDepth, TempoSync, RandomPanSwitch, AlternatePanSwitch,
            RangeLower, RangeUpper, FadeLower, FadeUpper,
        };

        WaveSwitch.PropertyChanged += OnLayerSummaryChanged;
        WaveNumberL.PropertyChanged += OnLayerSummaryChanged;
    }

    /// <summary>Short label for the WMT selector (e.g. "WMT1: 808 Kick" / "WMT1: off").</summary>
    public string Summary => WaveSwitch.Value ? $"{Title}: {WaveNumberL.Value}" : $"{Title}: off";

    private void OnLayerSummaryChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ParamBool.Value) or nameof(ParamString.Value))) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(Summary)));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        WaveSwitch.PropertyChanged -= OnLayerSummaryChanged;
        WaveNumberL.PropertyChanged -= OnLayerSummaryChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
