using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Editor for ONE PCM drum key (the Drum tab): Setup (identity/routing) + Amp (TVA).
/// Built fresh for the selected rail note. Wave/Pitch/Filter tabs are filled in later phases.</summary>
public sealed partial class PCMDrumNoteEditorViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly PCMDrumKitEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    // --- Setup (identity & routing) ---
    public ParamString PartialName { get; }
    public ParamString AssignType { get; }
    public ParamString MuteGroup { get; }
    public ParamString EnvMode { get; }
    public ParamBool OneShot { get; }
    public ParamInt PitchBendRange { get; }
    public ParamBool ReceiveExpression { get; }
    public ParamBool ReceiveSustain { get; }
    public ParamInt Pan { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamString RandomPitchDepth { get; }
    public ParamInt RandomPanDepth { get; }
    public ParamInt AlternatePanDepth { get; }
    public ParamString OutputAssign { get; }
    public ParamInt OutputLevel { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    // --- Amp (TVA) ---
    public ParamInt Level { get; }
    public ParamString TvaVeloCurve { get; }
    public ParamInt TvaVeloSens { get; }
    public ParamInt TvaEnvTime1VeloSens { get; }
    public ParamInt TvaEnvTime4VeloSens { get; }
    public ParamInt TvaEnvTime1 { get; }
    public ParamInt TvaEnvTime2 { get; }
    public ParamInt TvaEnvTime3 { get; }
    public ParamInt TvaEnvTime4 { get; }
    public ParamInt TvaEnvLevel1 { get; }
    public ParamInt TvaEnvLevel2 { get; }
    public ParamInt TvaEnvLevel3 { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public PCMDrumNoteEditorViewModel(PCMDrumKitEditorViewModel parent, DomainBase partialDomain,
        ThrottledParameterWriter writer)
    {
        _parent = parent;
        var byPath = ToDict(partialDomain);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n) => Track(new ParamString(partialDomain, byPath[PP + n], writer));
        ParamBool PB(string n) => Track(new ParamBool(partialDomain, byPath[PP + n], writer));

        // Setup
        PartialName = PS("Partial Name");
        AssignType = PS("Assign Type");
        MuteGroup = PS("Mute Group");
        EnvMode = PS("Partial Env Mode");
        OneShot = PB("One Shot Mode");
        PitchBendRange = PI("Partial Pitch Bend Range", 0, 48);
        ReceiveExpression = PB("Partial Receive Expression");
        ReceiveSustain = PB("Partial Receive Sustain");
        Pan = PI("Partial Pan", -64, 63);
        CoarseTune = PI("Partial Coarse Tune", 0, 127);
        FineTune = PI("Partial Fine Tune", -50, 50);
        RandomPitchDepth = PS("Partial Random Pitch Depth");
        RandomPanDepth = PI("Partial Random Pan Depth", 0, 63);
        AlternatePanDepth = PI("Partial Alternate Pan Depth", -63, 63);
        OutputAssign = PS("Partial Output Assign");
        OutputLevel = PI("Partial Output Level", 0, 127);
        ChorusSend = PI("Partial Chorus Send Level", 0, 127);
        ReverbSend = PI("Partial Reverb Send Level", 0, 127);

        // Amp (TVA)
        Level = PI("Partial Level", 0, 127);
        TvaVeloCurve = PS("TVA Velocity Curve");
        TvaVeloSens = PI("TVA Velocity Sens", -63, 63);
        TvaEnvTime1VeloSens = PI("TVA Env Time 1 Velocity Sens", -63, 63);
        TvaEnvTime4VeloSens = PI("TVA Env Time 4 Velocity Sens", -63, 63);
        TvaEnvTime1 = PI("TVA Env Time 1", 0, 127);
        TvaEnvTime2 = PI("TVA Env Time 2", 0, 127);
        TvaEnvTime3 = PI("TVA Env Time 3", 0, 127);
        TvaEnvTime4 = PI("TVA Env Time 4", 0, 127);
        TvaEnvLevel1 = PI("TVA Env Level 1", 0, 127);
        TvaEnvLevel2 = PI("TVA Env Level 2", 0, 127);
        TvaEnvLevel3 = PI("TVA Env Level 3", 0, 127);

        _editable = new IParam[]
        {
            PartialName, AssignType, MuteGroup, EnvMode, OneShot, PitchBendRange,
            ReceiveExpression, ReceiveSustain, Pan, CoarseTune, FineTune, RandomPitchDepth,
            RandomPanDepth, AlternatePanDepth, OutputAssign, OutputLevel, ChorusSend, ReverbSend,
            Level, TvaVeloCurve, TvaVeloSens, TvaEnvTime1VeloSens, TvaEnvTime4VeloSens,
            TvaEnvTime1, TvaEnvTime2, TvaEnvTime3, TvaEnvTime4, TvaEnvLevel1, TvaEnvLevel2, TvaEnvLevel3,
        };
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    // Copy / Paste / Init (shared clipboard lives on the parent kit editor).
    [ReactiveCommand] public void CopyDrum() => _parent.DrumClipboard = SnsPartialClipboard.Snapshot(_editable);
    [ReactiveCommand] public void PasteDrum() { if (_parent.DrumClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    [ReactiveCommand] public void InitDrum() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset of the continuous tweaks (leaves the envelope, enums, name, velocity sens).
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Partial Level"] = "100",
        [PP + "Partial Pan"] = "0",
        [PP + "Partial Coarse Tune"] = "64",
        [PP + "Partial Fine Tune"] = "0",
        [PP + "Partial Output Level"] = "127",
        [PP + "Partial Chorus Send Level"] = "0",
        [PP + "Partial Reverb Send Level"] = "0",
        [PP + "Partial Alternate Pan Depth"] = "0",
        [PP + "Partial Random Pan Depth"] = "0",
    };

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
