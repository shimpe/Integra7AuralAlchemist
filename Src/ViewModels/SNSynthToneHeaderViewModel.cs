using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Tone-wide (Common) controls shown in the editor header.</summary>
public sealed class SNSynthToneHeaderViewModel : ViewModelBase, IDisposable
{
    private const string CP = "SuperNATURAL Synth Tone Common/";

    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _toneName;

    public ParamInt ToneLevel { get; }
    public ParamBool IsMono { get; }       // ON = Mono, OFF = Poly
    public ParamBool Legato { get; }
    public ParamBool Portamento { get; }
    public ParamInt PortamentoTime { get; }
    public ParamBool Unison { get; }
    public ParamString UnisonSize { get; }
    public ParamInt AnalogFeel { get; }
    public ParamString Ring { get; }       // Off/Reserved/On (combo for the slice)

    public SNSynthToneHeaderViewModel(DomainBase common,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], writer, min, max));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], writer));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(common, byPath[CP + n], writer, o));

        ToneLevel = PI("Tone Level", 0, 127);
        IsMono = PB("Mono Switch");
        Legato = PB("Legato Switch");
        Portamento = PB("Portamento Switch");
        PortamentoTime = PI("Portamento Time", 0, 127);
        Unison = PB("Unison Switch");
        UnisonSize = PS("Unison Size", new[] { "2", "4", "6", "8" });
        AnalogFeel = PI("Analog Feel", 0, 127);
        Ring = PS("Ring Switch");

        _toneName = byPath[CP + "Tone Name"];
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
