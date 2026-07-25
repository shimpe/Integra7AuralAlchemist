using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly Studio Set Common editor: what the Studio Set is called and how fast it runs,
/// which of its effects are switched in at all, where the drum Comp-EQ sends its six units, the
/// external part's mix, what the four tone controls are wired to, and how the 128 voices are reserved
/// across the parts.</summary>
public sealed partial class StudioSetCommonEditorViewModel : ViewModelBase, IDisposable
{
    private const int PartCount = 16;

    /// <summary>What the INTEGRA-7 has to hand out. Reserving more than this over-commits it.</summary>
    private const int VoiceBudget = 64;

    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<IDisposable> _wrappers = [];
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Action<string>? _navigateToRawTab;

    // --- Identity / tempo ---
    /// <summary>Shown, not edited: the preset list and the raw grid are where a Studio Set is named.</summary>
    public ParamString StudioSetName { get; }
    public ParamInt Tempo { get; }            // 20..250 bpm
    public ParamString SoloPart { get; }

    // --- Which effects are switched in ---
    public ParamBool ReverbSwitch { get; }
    public ParamBool ChorusSwitch { get; }
    public ParamBool MasterEqSwitch { get; }
    public ParamBool DrumCompEqSwitch { get; }

    // --- Drum Comp-EQ routing ---
    public ParamInt DrumCompEqPart { get; }   // 1..16
    public IReadOnlyList<LabelledChoice> DrumCompEqOutputs { get; }

    // --- External part ---
    public ParamInt ExtLevel { get; }
    public ParamInt ExtChorusSend { get; }
    public ParamInt ExtReverbSend { get; }
    public ParamBool ExtMute { get; }

    // --- Tone controls ---
    public IReadOnlyList<LabelledChoice> ToneControlSources { get; }

    // --- Voice reserve ---
    public IReadOnlyList<LabelledNumber> VoiceReserves { get; }
    public int VoiceReserveTotal => VoiceReserves.Sum(v => v.Param.Value);
    public bool VoiceReserveOverBudget => VoiceReserveTotal > VoiceBudget;
    public string VoiceReserveSummary => $"{VoiceReserveTotal} of {VoiceBudget} reserved";

    public StudioSetCommonEditorViewModel(Integra7Domain domain, Action<string>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var common = domain.StudioSetCommon;
        var all = common.GetRelevantParameters(true, true);
        FullyQualifiedParameter ByName(string name) => all.First(p => p.ParSpec.Name == name);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, ByName(n), _writer, min, max));
        ParamString PS(string n) => Track(new ParamString(common, ByName(n), _writer));
        ParamBool PB(string n) => Track(new ParamBool(common, ByName(n), _writer));

        StudioSetName = PS("Studio Set Name");
        Tempo = PI("Studio Set Tempo", 20, 250);
        SoloPart = PS("Solo Part");

        ReverbSwitch = PB("Reverb Switch");
        ChorusSwitch = PB("Chorus Switch");
        MasterEqSwitch = PB("Master EQ Switch");
        DrumCompEqSwitch = PB("Drum Comp-EQ Switch");

        DrumCompEqPart = PI("Drum Comp-EQ Part", 1, 16);
        DrumCompEqOutputs = Enumerable.Range(1, 6)
            .Select(i => new LabelledChoice($"Unit {i}", PS($"Drum Comp-EQ {i} Output Assign")))
            .ToList();

        ExtLevel = PI("Ext Part Level", 0, 127);
        ExtChorusSend = PI("Ext Part Chorus Send Level", 0, 127);
        ExtReverbSend = PI("Ext Part Reverb Send Level", 0, 127);
        ExtMute = PB("Ext Part Mute Switch");

        ToneControlSources = Enumerable.Range(1, 4)
            .Select(i => new LabelledChoice($"Control {i}", PS($"Tone Control {i} Source")))
            .ToList();

        VoiceReserves = Enumerable.Range(1, PartCount)
            .Select(i => new LabelledNumber($"Part {i}", PI($"Voice Reserve {i}", 0, VoiceBudget)))
            .ToList();

        // The reserve only makes sense as a total, so the running sum follows every one of the sixteen.
        foreach (var reserve in VoiceReserves)
            _subscriptions.Add(reserve.Param.WhenAnyValue(x => x.Value).Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(VoiceReserveTotal));
                this.RaisePropertyChanged(nameof(VoiceReserveOverBudget));
                this.RaisePropertyChanged(nameof(VoiceReserveSummary));
            }));
    }

    // Open the raw Studio Set Common grid for the full parameter set.
    [ReactiveCommand] public void AdvancedStudioSet() => _navigateToRawTab?.Invoke("COMMON-STUDIO-SET");

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
