using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Editor for ONE SN-Drums note (the Drum tab): the searchable drum sound + its tweaks.
/// Built fresh for the selected rail note, so the heavy Inst Number options list lives only here.</summary>
public sealed partial class SNDrumNoteEditorViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Partial/";
    private readonly SNDrumKitEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public ParamString InstNumber { get; }   // searchable drum sound (~513)
    public ParamInt Level { get; }
    public ParamInt Pan { get; }
    public ParamInt Tune { get; }            // cents, -1200..1200
    public ParamInt Attack { get; }          // 0..100 %
    public ParamInt Decay { get; }           // -63..0
    public ParamInt Brilliance { get; }      // -15..12
    public ParamString Variation { get; }
    public ParamInt DynamicRange { get; }    // 0..63
    public ParamInt StereoWidth { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }
    public ParamString OutputAssign { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public SNDrumNoteEditorViewModel(SNDrumKitEditorViewModel parent, DomainBase partialDomain,
        ThrottledParameterWriter writer)
    {
        _parent = parent;
        var byPath = ToDict(partialDomain);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n) => Track(new ParamString(partialDomain, byPath[PP + n], writer));

        InstNumber = PS("Inst Number");
        Level = PI("Level", 0, 127);
        Pan = PI("Pan", -64, 63);
        Tune = PI("Tune", -1200, 1200);
        Attack = PI("Attack", 0, 100);
        Decay = PI("Decay", -63, 0);
        Brilliance = PI("Brilliance", -15, 12);
        Variation = PS("Variation");
        DynamicRange = PI("Dynamic Range", 0, 63);
        StereoWidth = PI("Stereo Width", 0, 127);
        ChorusSend = PI("Chorus Send Level", 0, 127);
        ReverbSend = PI("Reverb Send Level", 0, 127);
        OutputAssign = PS("Output Assign");

        _editable = new IParam[]
        {
            InstNumber, Level, Pan, Tune, Attack, Decay, Brilliance, Variation,
            DynamicRange, StereoWidth, ChorusSend, ReverbSend, OutputAssign,
        };
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    // Copy / Paste / Init (edit-buffer only; shared clipboard lives on the parent kit editor).
    [ReactiveCommand] public void CopyDrum() => _parent.DrumClipboard = SnsPartialClipboard.Snapshot(_editable);
    [ReactiveCommand] public void PasteDrum() { if (_parent.DrumClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    [ReactiveCommand] public void InitDrum() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset of the tweaks (leaves the drum sound, Variation and Output Assign).
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Level"] = "100",
        [PP + "Pan"] = "0",
        [PP + "Tune"] = "0",
        [PP + "Attack"] = "0",
        [PP + "Decay"] = "0",
        [PP + "Brilliance"] = "0",
        [PP + "Dynamic Range"] = "0",
        [PP + "Stereo Width"] = "0",
        [PP + "Chorus Send Level"] = "0",
        [PP + "Reverb Send Level"] = "0",
    };

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
