using System;
using System.Collections.Generic;
using System.Reactive;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly Studio Set Part editor for ONE part: how the part sits in the mix, how it is
/// tuned and voiced, the tone offsets it applies on top of its patch, the key/velocity zone it
/// answers to, its scale tuning, its place in the surround field and which MIDI messages it accepts.
/// The full raw parameter list stays one button away.</summary>
public sealed partial class StudioSetPartEditorViewModel : ViewModelBase, IDisposable
{
    private const string P = "Studio Set Part/"; // parameter path prefix

    private static readonly string[] NoteNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // Label shown in the editor -> parameter name. The receive switches are one uniform list.
    private static readonly (string Label, string Param)[] ReceiveSwitchNames =
    [
        ("Program Change", "Receive Program Change"),
        ("Bank Select", "Receive Bank Select"),
        ("Pitch Bend", "Receive Pitch Bend"),
        ("Poly Key Pressure", "Receive Polyphonic Key Pressure"),
        ("Channel Pressure", "Receive Channel Pressure"),
        ("Modulation", "Receive Modulation"),
        ("Volume", "Receive Volume"),
        ("Pan", "Receive Pan"),
        ("Expression", "Receive Expression"),
        ("Hold-1", "Receive Hold-1"),
    ];

    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<IDisposable> _wrappers = [];
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Action<string, int?>? _navigateToRawTab;

    public int PartNo { get; }
    public string Title => $"Part {PartNo + 1}";

    // --- Mix / routing ---
    public ParamInt ReceiveChannel { get; }   // 1..16
    public ParamBool ReceiveSwitch { get; }
    public ParamBool MuteSwitch { get; }
    public ParamInt Level { get; }
    public ParamInt Pan { get; }              // -64..63 (L..R)
    public ParamString OutputAssign { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }

    // --- Pitch / voicing ---
    public ParamInt OctaveShift { get; }      // -3..3
    public ParamInt CoarseTune { get; }       // -48..48 semitones
    public ParamInt FineTune { get; }         // -50..50 cents
    public ParamString MonoPoly { get; }
    public ParamString LegatoSwitch { get; }
    public ParamString PitchBendRange { get; }
    public ParamString PortamentoSwitch { get; }
    public ParamInt PortamentoTime { get; }   // 0..128

    // --- Tone offsets (applied on top of the part's own patch) ---
    public ParamInt CutoffOffset { get; }
    public ParamInt ResonanceOffset { get; }
    public ParamInt AttackOffset { get; }
    public ParamInt DecayOffset { get; }
    public ParamInt ReleaseOffset { get; }
    public ParamInt VibratoRate { get; }
    public ParamInt VibratoDepth { get; }
    public ParamInt VibratoDelay { get; }
    public ParamInt VelocitySensOffset { get; } // -63..63
    public ParamString VelocityCurveType { get; }

    // --- Key / velocity zone ---
    public ParamInt KeyRangeLower { get; }
    public ParamInt KeyRangeUpper { get; }
    public ParamInt KeyFadeLower { get; }
    public ParamInt KeyFadeUpper { get; }
    public ParamInt VelocityRangeLower { get; }
    public ParamInt VelocityRangeUpper { get; }
    public ParamInt VelocityFadeLower { get; }
    public ParamInt VelocityFadeUpper { get; }

    // --- Scale tune ---
    public ParamString ScaleTuneType { get; }
    public ParamString ScaleTuneKey { get; }
    public IReadOnlyList<LabelledNumber> ScaleTunes { get; }

    // --- Motional Surround (the same values the Motional Surround tab moves) ---
    public ParamInt SurroundLeftRight { get; }
    public ParamInt SurroundFrontBack { get; }
    public ParamInt SurroundWidth { get; }      // 0..32
    public ParamInt SurroundAmbienceSend { get; }

    // --- MIDI receive switches ---
    public IReadOnlyList<LabelledSwitch> ReceiveSwitches { get; }

    // --- Which patch the part holds. Read-only here: the preset list is what selects it. ---
    public ParamInt ToneBankMsb { get; }
    public ParamInt ToneBankLsb { get; }
    public ParamInt ToneProgramChange { get; }
    public string ToneBankSummary => $"MSB {ToneBankMsb.Value} · LSB {ToneBankLsb.Value} · PC {ToneProgramChange.Value + 1}";

    public StudioSetPartEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        PartNo = partNo;
        _navigateToRawTab = navigateToRawTab;

        var part = domain.StudioSetPart(partNo);
        var byPath = ToDict(part);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(part, byPath[P + n], _writer, min, max));
        ParamString PS(string n) => Track(new ParamString(part, byPath[P + n], _writer));
        ParamBool PB(string n) => Track(new ParamBool(part, byPath[P + n], _writer));

        ReceiveChannel = PI("Receive Channel", 1, 16);
        ReceiveSwitch = PB("Receive Switch");
        // Mute is an OFF/MUTE parameter rather than the usual OFF/ON, so its two words are given here.
        MuteSwitch = Track(new ParamBool(part, byPath[P + "Mute Switch"], _writer, "Mute On", "Mute Off"));
        Level = PI("Part Level", 0, 127);
        Pan = PI("Part Pan", -64, 63);
        OutputAssign = PS("Part Output Assign");
        ChorusSend = PI("Part Chorus Send Level", 0, 127);
        ReverbSend = PI("Part Reverb Send Level", 0, 127);

        OctaveShift = PI("Part Octave Shift", -3, 3);
        CoarseTune = PI("Part Coarse Tune", -48, 48);
        FineTune = PI("Part Fine Tune", -50, 50);
        MonoPoly = PS("Part Mono-Poly");
        LegatoSwitch = PS("Part Legato Switch");
        PitchBendRange = PS("Part Pitch Bend Range");
        PortamentoSwitch = PS("Part Portamento Switch");
        PortamentoTime = PI("Part Portamento Time", 0, 128);

        CutoffOffset = PI("Part Cutoff Offset", -64, 63);
        ResonanceOffset = PI("Part Resonance Offset", -64, 63);
        AttackOffset = PI("Part Attack Time Offset", -64, 63);
        DecayOffset = PI("Part Decay Time Offset", -64, 63);
        ReleaseOffset = PI("Part Release Time Offset", -64, 63);
        VibratoRate = PI("Part Vibrato Rate", -64, 63);
        VibratoDepth = PI("Part Vibrato Depth", -64, 63);
        VibratoDelay = PI("Part Vibrato Delay", -64, 63);
        VelocitySensOffset = PI("Part Velocity Sens Offset", -63, 63);
        VelocityCurveType = PS("Velocity Curve Type");

        KeyRangeLower = PI("Keyboard Range Lower", 0, 127);
        KeyRangeUpper = PI("Keyboard Range Upper", 0, 127);
        KeyFadeLower = PI("Keyboard Fade Width Lower", 0, 127);
        KeyFadeUpper = PI("Keyboard Fade Width Upper", 0, 127);
        VelocityRangeLower = PI("Velocity Range Lower", 0, 127);
        VelocityRangeUpper = PI("Velocity Range Upper", 0, 127);
        VelocityFadeLower = PI("Velocity Fade Width Lower", 0, 127);
        VelocityFadeUpper = PI("Velocity Fade Width Upper", 0, 127);

        ScaleTuneType = PS("Part Scale Tune Type");
        ScaleTuneKey = PS("Part Scale Tune Key");
        var tunes = new List<LabelledNumber>(NoteNames.Length);
        foreach (var note in NoteNames)
            tunes.Add(new LabelledNumber(note, PI($"Part Scale Tune for {note}", -64, 63)));
        ScaleTunes = tunes;

        SurroundLeftRight = PI("Motional Surround L-R", -64, 63);
        SurroundFrontBack = PI("Motional Surround F-B", -64, 63);
        SurroundWidth = PI("Motional Surround Width", 0, 32);
        SurroundAmbienceSend = PI("Motional Surround Ambience Send Level", 0, 127);

        var switches = new List<LabelledSwitch>(ReceiveSwitchNames.Length);
        foreach (var (label, param) in ReceiveSwitchNames)
            switches.Add(new LabelledSwitch(label, PB(param)));
        ReceiveSwitches = switches;

        ToneBankMsb = PI("Tone Bank Select MSB", 0, 127);
        ToneBankLsb = PI("Tone Bank Select LSB", 0, 127);
        ToneProgramChange = PI("Tone Bank Program Number (PC)", 0, 127);
        Bridge(ToneBankMsb, nameof(ToneBankSummary));
        Bridge(ToneBankLsb, nameof(ToneBankSummary));
        Bridge(ToneProgramChange, nameof(ToneBankSummary));
    }

    private void Bridge(ParamInt p, string derivedProperty) =>
        _subscriptions.Add(p.WhenAnyValue(x => x.Value)
            .Subscribe(_ => this.RaisePropertyChanged(derivedProperty)));

    // Open the raw Studio Set Part grid for the full parameter set (including the reserved slots).
    public void AdvancedPart() => _navigateToRawTab?.Invoke("SET-PART", null);

    // Hand-written rather than generated: ReactiveUI.SourceGenerators has no release that supports
    // ReactiveUI 24, and what it emits names the core's RxVoid-flavoured ReactiveCommand fully
    // qualified, so no alias can redirect it.
    private ReactiveUI.Reactive.ReactiveCommand<Unit, Unit>? _advancedPartCommand;
    public ReactiveUI.Reactive.ReactiveCommand<Unit, Unit> AdvancedPartCommand =>
        _advancedPartCommand ??= ReactiveCommand.Create(AdvancedPart);

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
        foreach (var s in _subscriptions) s.Dispose();
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
