using System;
using System.Collections.Generic;
using System.Linq;
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

    public SNAcousticToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

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

        Instrument = Track(new DiscriminatedParamSectionViewModel(common, _writer,
            "Instrument", "/Modify Parameter ",
            InstrumentCatalog.Families.Select(f => f.Name).ToList(),
            InstrumentCatalog.FamilyOf, InstrumentCatalog.ValuesIn,
            ConditionalParamLabels.FriendlyNames));

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

    // Open the raw SN-A Tone tab for the full parameter set.
    [ReactiveCommand] public void AdvancedAcoustic() => _navigateToRawTab?.Invoke("SN-A", null);

    public void Dispose()
    {
        foreach (var w in _wrappers) w.Dispose();
        _writer.Dispose();
    }
}
