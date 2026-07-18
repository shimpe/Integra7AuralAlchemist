using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly SuperNATURAL Acoustic editor for ONE part: header/character wrappers, the
/// instrument detail section (family -> instrument picker + per-instrument modify params), and MFX.</summary>
public sealed partial class SNAcousticToneEditorViewModel : ViewModelBase, IDisposable
{
    private const string CP = "SuperNATURAL Acoustic Tone Common/"; // common path prefix

    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly List<IDisposable> _wrappers = [];
    private readonly Action _onLoadedSrxChanged;

    // --- Header ---
    public ParamString ToneName { get; }       // display-only ASCII name
    public ParamInt ToneLevel { get; }
    public ParamString MonoPoly { get; }       // Mono / Poly (enum)
    public ParamInt PortamentoOffset { get; }
    public ParamInt OctaveShift { get; }
    public ParamString Category { get; }

    // --- Character (bipolar offsets + vibrato) ---
    public ParamInt CutoffOffset { get; }
    public ParamInt ResonanceOffset { get; }
    public ParamInt AttackOffset { get; }
    public ParamInt ReleaseOffset { get; }
    public ParamInt VibratoRate { get; }
    public ParamInt VibratoDepth { get; }
    public ParamInt VibratoDelay { get; }

    // --- Phrase / TFX ---
    public ParamString PhraseNumber { get; }
    public ParamInt PhraseOctaveShift { get; }
    public ParamBool TfxSwitch { get; }

    // --- Instrument detail section (shared component) + MFX ---
    public DiscriminatedParamSectionViewModel Instrument { get; }
    public MfxPanelViewModel Mfx { get; }
    public ToneNoteRailViewModel NoteRail { get; }

    public SNAcousticToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null,
        Func<int, int, System.Threading.Tasks.Task>? noteOn = null, Func<int, System.Threading.Tasks.Task>? noteOff = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(noteOn, noteOff);

        var common = domain.SNAcousticToneCommon(partNo);
        var byPath = ToDict(common);

        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], writer: _writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(common, byPath[CP + n], _writer, o));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], _writer));

        // Header
        ToneName = PS("Tone Name");
        ToneLevel = PI("Tone Level", 0, 127);
        MonoPoly = PS("Mono-Poly");
        PortamentoOffset = PI("Portamento Time Offset", -64, 63);
        OctaveShift = PI("Octave Shift", -3, 3);
        Category = PS("Category");

        // Character
        CutoffOffset = PI("Cutoff Offset", -64, 63);
        ResonanceOffset = PI("Resonance Offset", -64, 63);
        AttackOffset = PI("Attack Time Offset", -64, 63);
        ReleaseOffset = PI("Release Time Offset", -64, 63);
        VibratoRate = PI("Vibrato Rate", -64, 63);
        VibratoDepth = PI("Vibrato Depth", -64, 63);
        VibratoDelay = PI("Vibrato Delay", -64, 63);

        // Phrase / TFX
        PhraseNumber = PS("Phrase Number");
        PhraseOctaveShift = PI("Phrase Octave Shift", -3, 3);
        TfxSwitch = PB("TFX Switch");

        // The full instrument-name list (INSTRUMENT_VARIATIONS order) drives ExSN board parsing.
        // The Instrument param is discrete, so ParSpec.Discrete is non-null here.
        var instrumentNames = byPath[CP + "Instrument"].ParSpec.Discrete!.Select(d => d.Item2).ToList();
        var familyNames = InstrumentCatalog.Families.Select(f => f.Name).ToList();
        var notLoaded = SrxGroupIdResolution.NotLoadedSuffix;

        Instrument = Track(new DiscriminatedParamSectionViewModel(common, _writer,
            "Instrument", "/Modify Parameter ",
            familyNames,
            InstrumentCatalog.FamilyOf,
            family => family == ExSnInstrumentFilter.ExpansionFamily
                ? ExSnInstrumentFilter.LoadedExpansionIndices(
                    InstrumentCatalog.ValuesIn(ExSnInstrumentFilter.ExpansionFamily), instrumentNames,
                    LoadedSrxState.Default.ExSnBoards)
                : InstrumentCatalog.ValuesIn(family),
            ConditionalParamLabels.FriendlyNames,
            familiesSupplier: () => ExSnInstrumentFilter.VisibleFamilies(familyNames, LoadedSrxState.Default.ExSnBoards),
            displayName: (_, name) => ExSnInstrumentFilter.DisplayName(name, LoadedSrxState.Default.ExSnBoards),
            toRealValue: d => d.EndsWith(notLoaded) ? d[..^notLoaded.Length] : d));

        // Live-refresh the instrument picker when expansions are (re)loaded.
        _onLoadedSrxChanged = () => Dispatcher.UIThread.Post(Instrument.Reproject);
        LoadedSrxState.Default.Changed += _onLoadedSrxChanged;

        Mfx = Track(new MfxPanelViewModel(domain.SNAcousticToneCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("SN-A-MFX", null)));
    }

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

    // Open the raw SN-A Tone tab for the full parameter set. The friendly Editor tab owns Tag "SN-A"
    // (so it's the default on preset load), so the raw Tone tab uses the suffixed "SN-A-TONE".
    [ReactiveCommand] public void AdvancedAcoustic() => _navigateToRawTab?.Invoke("SN-A-TONE", null);

    public void Dispose()
    {
        LoadedSrxState.Default.Changed -= _onLoadedSrxChanged;
        foreach (var w in _wrappers) w.Dispose();
        NoteRail.Dispose();
        _writer.Dispose();
    }
}
