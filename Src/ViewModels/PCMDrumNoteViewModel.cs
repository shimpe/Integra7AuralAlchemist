using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row in the PCM-Drums note rail: the MIDI note + key styling, and the drum key's
/// display name read live from its Partial Name (kept light — no per-row parameter wrappers).</summary>
public sealed class PCMDrumNoteViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly FullyQualifiedParameter _partialName;

    /// <summary>The note's partial domain — used to build the full editor when this note is selected.</summary>
    public DomainBase PartialDomain { get; }
    public int Index { get; }                 // 0..87
    public int Note { get; }                  // MIDI note 21..108
    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    public PCMDrumNoteViewModel(DomainBase partialDomain, int index)
    {
        PartialDomain = partialDomain;
        Index = index;
        Note = Constants.FIRST_PARTIAL_PCM_DRUM + index;
        var byPath = ToDict(partialDomain);
        _partialName = byPath[PP + "Partial Name"];
        _partialName.PropertyChanged += OnNameChanged;
    }

    public string InstName => _partialName.StringValue;

    private void OnNameChanged(object? s, PropertyChangedEventArgs e)
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

    public void Dispose() => _partialName.PropertyChanged -= OnNameChanged;
}
