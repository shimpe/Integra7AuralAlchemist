using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Tone-wide (Common + Common 2) controls shown in the PCM Synth editor header.</summary>
public sealed class PCMSynthToneHeaderViewModel : ViewModelBase, IDisposable
{
    private const string CP = "PCM Synth Tone Common/";
    private const string C2 = "PCM Synth Tone Common 2/";

    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _toneName;

    public ParamInt ToneLevel { get; }
    public ParamInt Pan { get; }
    public ParamString MonoPoly { get; }     // Mono / Poly (enum)
    public ParamBool Legato { get; }
    public ParamBool Portamento { get; }
    public ParamInt PortamentoTime { get; }
    public ParamInt AnalogFeel { get; }
    public ParamInt OctaveShift { get; }
    public ParamInt CoarseTune { get; }
    public ParamInt FineTune { get; }
    public ParamInt PitchBendUp { get; }
    public ParamInt PitchBendDown { get; }

    // Character (tone-wide offsets)
    public ParamInt CutoffOffset { get; }
    public ParamInt ResonanceOffset { get; }
    public ParamInt AttackOffset { get; }
    public ParamInt ReleaseOffset { get; }

    // Common 2
    public ParamString Category { get; }
    public ParamString PhraseNumber { get; }
    public ParamInt PhraseOctaveShift { get; }
    public ParamBool TfxSwitch { get; }

    public PCMSynthToneHeaderViewModel(DomainBase common,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath,
        DomainBase common2, IReadOnlyDictionary<string, FullyQualifiedParameter> by2,
        ThrottledParameterWriter writer)
    {
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], writer, min, max));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], writer));
        ParamString PS(string n) => Track(new ParamString(common, byPath[CP + n], writer));
        ParamInt PI2(string n, int min, int max) => Track(new ParamInt(common2, by2[C2 + n], writer, min, max));
        ParamBool PB2(string n) => Track(new ParamBool(common2, by2[C2 + n], writer));
        ParamString PS2(string n) => Track(new ParamString(common2, by2[C2 + n], writer));

        ToneLevel = PI("PCM Synth Tone Level", 0, 127);
        Pan = PI("PCM Synth Tone Pan", -64, 63);
        MonoPoly = PS("Mono-Poly");
        Legato = PB("Legato Switch");
        Portamento = PB("Portamento Switch");
        PortamentoTime = PI("Portamento Time", 0, 127);
        AnalogFeel = PI("Analog Feel", 0, 127);
        OctaveShift = PI("Octave Shift", -3, 3);
        CoarseTune = PI("PCM Synth Tone Coarse Tune", -48, 48);
        FineTune = PI("PCM Synth Tone Fine Tune", -50, 50);
        PitchBendUp = PI("Pitch Bend Range Up", 0, 48);
        PitchBendDown = PI("Pitch Bend Range Down", 0, 48);

        CutoffOffset = PI("Cutoff Offset", -63, 63);
        ResonanceOffset = PI("Resonance Offset", -63, 63);
        AttackOffset = PI("Attack Time Offset", -63, 63);
        ReleaseOffset = PI("Release Time Offset", -63, 63);

        Category = PS2("Tone Category");
        PhraseNumber = PS2("Phrase Number");
        PhraseOctaveShift = PI2("Phrase Octave Shift", -3, 3);
        TfxSwitch = PB2("TFX Switch");

        _toneName = byPath[CP + "PCM Synth Tone Name"];
        _toneName.PropertyChanged += OnToneNameChanged;
    }

    public string ToneName => _toneName.StringValue;

    private void OnToneNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(ToneName)));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        _toneName.PropertyChanged -= OnToneNameChanged;
        foreach (var w in _wrappers) w.Dispose();
    }
}
