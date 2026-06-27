using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly PCM Drum Kit editor for ONE part: header, an 88-note navigation rail with
/// click-to-audition, and the shared MFX panel. The Drum and Comp-EQ tab bodies are filled in later phases.</summary>
public sealed partial class PCMDrumKitEditorViewModel : ViewModelBase, IDisposable
{
    private const string CP = "PCM Drum Kit Common/";
    private const string C2P = "PCM Drum Kit Common 2/";

    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly Func<int, System.Threading.Tasks.Task>? _playNote;
    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _kitName;

    // --- Header ---
    public ParamInt KitLevel { get; }
    public ParamString PhraseNumber { get; }
    public ParamBool TfxSwitch { get; }

    public ObservableCollection<PCMDrumNoteViewModel> Notes { get; } = [];
    public MfxPanelViewModel Mfx { get; }

    public PCMDrumKitEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null, Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        _playNote = playNote;

        var common = domain.PCMDrumKitCommon(partNo);
        var common2 = domain.PCMDrumKitCommon2(partNo);
        var byPath = ToDict(common);
        var byPath2 = ToDict(common2);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], _writer, min, max));
        ParamString PS2(string n) => Track(new ParamString(common2, byPath2[C2P + n], _writer));
        ParamBool PB2(string n) => Track(new ParamBool(common2, byPath2[C2P + n], _writer));

        KitLevel = PI("Kit Level", 0, 127);
        PhraseNumber = PS2("Phrase Number");
        TfxSwitch = PB2("TFX Switch");

        _kitName = byPath[CP + "Kit Name"];
        _kitName.PropertyChanged += OnKitNameChanged;

        // The note rail: notes 108 (top) → 21 (bottom) so low notes sit at the bottom.
        for (var i = Constants.NO_OF_PARTIALS_PCM_DRUM - 1; i >= 0; i--)
            Notes.Add(Track(new PCMDrumNoteViewModel(domain.PCMDrumKitPartial(partNo, i), i)));
        _selectedNote = Notes.Count > 0 ? Notes[0] : null;
        RebuildDrumEditor();

        Mfx = new MfxPanelViewModel(domain.PCMDrumKitCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCMD-MFX", null));
    }

    public string KitName => _kitName.StringValue;

    private void OnKitNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(KitName)));
    }

    private PCMDrumNoteViewModel? _selectedNote;
    public PCMDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(value, _selectedNote)) return;
            this.RaiseAndSetIfChanged(ref _selectedNote, value);
            RebuildDrumEditor();
        }
    }

    private PCMDrumNoteEditorViewModel? _selectedDrumEditor;
    /// <summary>The Drum-tab editor for the selected note (rebuilt on each selection change).</summary>
    public PCMDrumNoteEditorViewModel? SelectedDrumEditor
    {
        get => _selectedDrumEditor;
        private set => this.RaiseAndSetIfChanged(ref _selectedDrumEditor, value);
    }

    /// <summary>Shared drum copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? DrumClipboard { get; set; }

    private void RebuildDrumEditor()
    {
        _selectedDrumEditor?.Dispose();
        SelectedDrumEditor = _selectedNote is { } n
            ? new PCMDrumNoteEditorViewModel(this, n.PartialDomain, _writer)
            : null;
    }

    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("PCMD-KIT", null);

    /// <summary>Audition a drum by sending its MIDI note (note-on/off handled by the host).</summary>
    public void PlayNote(int note)
    {
        if (_playNote is not null) _ = _playNote(note);
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        _kitName.PropertyChanged -= OnKitNameChanged;
        _selectedDrumEditor?.Dispose();
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
}
