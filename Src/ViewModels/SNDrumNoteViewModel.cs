using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row in the SN-Drums note rail: the MIDI note + key styling, and the drum sound's
/// display name read live from the partial's Inst Number (kept light — no per-row options list).</summary>
public sealed class SNDrumNoteViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Partial/";
    private readonly FullyQualifiedParameter _instNumber;

    /// <summary>The note's partial domain — used to build the full editor when this note is selected.</summary>
    public DomainBase PartialDomain { get; }
    public int Index { get; }                 // 0..61
    public int Note { get; }                  // MIDI note 27..88
    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    public SNDrumNoteViewModel(DomainBase partialDomain, int index)
    {
        PartialDomain = partialDomain;
        Index = index;
        Note = Constants.FIRST_PARTIAL_SN_DRUM + index;
        var byPath = ToDict(partialDomain);
        _instNumber = byPath[PP + "Inst Number"];
        _instNumber.PropertyChanged += OnInstChanged;
    }

    public string InstName => _instNumber.StringValue;

    private void OnInstChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(InstName)));
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    public void Dispose() => _instNumber.PropertyChanged -= OnInstChanged;
}
