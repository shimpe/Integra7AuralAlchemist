using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly System editor: the settings that belong to the instrument rather than to a Studio
/// Set — overall tuning and level, where the tempo and the control assignments come from, which MIDI
/// messages the instrument as a whole accepts, and how it drives its outputs.</summary>
public sealed partial class SystemEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly List<IDisposable> _wrappers = [];
    private readonly Action<string>? _navigateToRawTab;

    // --- Master ---
    /// <summary>Master Tune is the one parameter here that steps in tenths of a cent, so it is rendered
    /// straight from its parameter definition (knob + mapped readout) instead of through an int wrapper.</summary>
    public FullyQualifiedParameter MasterTune { get; }
    public ParamInt MasterKeyShift { get; }   // -24..24 semitones
    public ParamInt MasterLevel { get; }      // 0..127
    public ParamBool ScaleTuneSwitch { get; }

    // --- Tempo & clock ---
    public ParamInt SystemTempo { get; }      // 20..250 bpm
    public ParamString ClockSource { get; }   // MIDI / USB
    public ParamString TempoAssignSource { get; }

    // --- Control ---
    public ParamString ControlSource { get; }             // SYSTEM / STUDIO SET
    public ParamString StudioSetControlChannel { get; }
    public IReadOnlyList<LabelledChoice> ControlSources { get; }

    // --- MIDI receive ---
    public ParamBool ReceiveProgramChange { get; }
    public ParamBool ReceiveBankSelect { get; }

    // --- Output ---
    public ParamString OutputMode { get; }    // SPEAKER / PHONES
    public ParamBool CenterSpeaker { get; }
    public ParamBool SubWooferSpeaker { get; }

    public SystemEditorViewModel(Integra7Domain domain, Action<string>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var system = domain.System;
        var all = system.GetRelevantParameters(true, true);
        FullyQualifiedParameter ByName(string name) => all.First(p => p.ParSpec.Name == name);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(system, ByName(n), _writer, min, max));
        ParamString PS(string n) => Track(new ParamString(system, ByName(n), _writer));
        ParamBool PB(string n) => Track(new ParamBool(system, ByName(n), _writer));

        MasterTune = ByName("Master Tune");
        MasterKeyShift = PI("Master Key Shift", -24, 24);
        MasterLevel = PI("Master Level", 0, 127);
        // Scale Tune Switch has no OFF/ON names in the parameter database — it reads back as plain 0/1.
        ScaleTuneSwitch = Track(new ParamBool(system, ByName("Scale Tune Switch"), _writer, "1", "0"));

        SystemTempo = PI("System Tempo", 20, 250);
        ClockSource = PS("System Clock Source");
        TempoAssignSource = PS("Tempo Assign Source");

        ControlSource = PS("Control Source");
        StudioSetControlChannel = PS("Studio Set Control Channel");
        ControlSources = Enumerable.Range(1, 4)
            .Select(i => new LabelledChoice($"Control {i}", PS($"System Control {i} Source")))
            .ToList();

        ReceiveProgramChange = PB("Receive Program Change");
        ReceiveBankSelect = PB("Receive Bank Select");

        OutputMode = PS("2CH Output mode");
        CenterSpeaker = PB("5.1CH Center Speaker");
        SubWooferSpeaker = PB("5.1CH Sub Woofer Speaker");
    }

    // Open the raw System grid for the full parameter set.
    public void AdvancedSystem() => _navigateToRawTab?.Invoke("COMMON-SYSTEM");

    // Hand-written rather than generated: ReactiveUI.SourceGenerators has no release that supports
    // ReactiveUI 24, and what it emits names the core's RxVoid-flavoured ReactiveCommand fully
    // qualified, so no alias can redirect it.
    private ReactiveUI.Reactive.ReactiveCommand<Unit, Unit>? _advancedSystemCommand;
    public ReactiveUI.Reactive.ReactiveCommand<Unit, Unit> AdvancedSystemCommand =>
        _advancedSystemCommand ??= ReactiveCommand.Create(AdvancedSystem);

    private T Track<T>(T wrapper) where T : IDisposable
    {
        _wrappers.Add(wrapper);
        return wrapper;
    }

    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
