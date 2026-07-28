using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>
/// Friendly editor for one of the Studio Set's two send effects. Chorus and Reverb have the same
/// shape — a type that decides which further parameters exist, a send level and an output routing —
/// so one view model serves both; <see cref="ForChorus"/> and <see cref="ForReverb"/> name the parts.
///
/// The type-specific parameters come from the shared <see cref="DiscriminatedParamSectionViewModel"/>,
/// the same component the MFX panel uses. Both effects have a handful of types and no meaningful
/// grouping, so they are handed to it as a single family and the family combo hides itself.
/// </summary>
public sealed partial class StudioSetEffectEditorViewModel : ViewModelBase, IDisposable
{
    private const string Off = "Off";

    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string>? _navigateToRawTab;
    private readonly string _rawTabTag;
    private readonly IDisposable _typeSub;

    public string Title { get; }
    public string OutputLabel { get; }
    public string AdvancedLabel { get; }

    /// <summary>Type picker + the parameters that type brings with it.</summary>
    public DiscriminatedParamSectionViewModel Section { get; }
    public ParamInt Level { get; }
    public ParamString Output { get; }

    /// <summary>The effect is switched out. Its remaining controls stay usable — the hardware keeps
    /// them — but nothing is being heard from it.</summary>
    public bool IsOff => Section.Discriminator.Value == Off;

    public static StudioSetEffectEditorViewModel ForChorus(Integra7Domain domain,
        Action<string>? navigateToRawTab = null) =>
        new(domain.StudioSetCommonChorus, "Chorus", "Chorus Type", "/Chorus Parameter ",
            "Chorus Level", "Chorus Output Select", "Send to", "COMMON-CHORUS",
            "Advanced chorus parameters…", navigateToRawTab);

    public static StudioSetEffectEditorViewModel ForReverb(Integra7Domain domain,
        Action<string>? navigateToRawTab = null) =>
        new(domain.StudioSetCommonReverb, "Reverb", "Reverb Type", "/Reverb Parameter ",
            "Reverb Level", "Reverb Output Assign", "Output", "COMMON-REVERB",
            "Advanced reverb parameters…", navigateToRawTab);

    private StudioSetEffectEditorViewModel(DomainBase domain, string title,
        string typeLeafName, string gridPathSegment, string levelLeafName, string outputLeafName,
        string outputLabel, string rawTabTag, string advancedLabel, Action<string>? navigateToRawTab)
    {
        Title = title;
        OutputLabel = outputLabel;
        AdvancedLabel = advancedLabel;
        _rawTabTag = rawTabTag;
        _navigateToRawTab = navigateToRawTab;

        var all = domain.GetRelevantParameters(true, true);
        FullyQualifiedParameter ByName(string name) => all.First(p => p.ParSpec.Name == name);

        // One family holding every type: neither effect has enough types to group, and the section
        // hides a family combo that would only ever offer one choice.
        var typeCount = ByName(typeLeafName).ParSpec.Repr?.Count ?? 0;
        IReadOnlyList<int> allTypes = Enumerable.Range(0, typeCount).ToList();

        Section = new DiscriminatedParamSectionViewModel(domain, _writer,
            typeLeafName, gridPathSegment,
            [title], _ => title, _ => allTypes,
            ConditionalParamLabels.FriendlyNames);

        Level = new ParamInt(domain, ByName(levelLeafName), _writer, 0, 127);
        Output = new ParamString(domain, ByName(outputLeafName), _writer);

        _typeSub = Section.Discriminator.WhenAnyValue(d => d.Value)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsOff)));
    }

    // Open the raw grid for the full parameter set, reserved slots and all.
    public void Advanced() => _navigateToRawTab?.Invoke(_rawTabTag);

    // Hand-written rather than generated: ReactiveUI.SourceGenerators has no release that supports
    // ReactiveUI 24, and what it emits names the core's RxVoid-flavoured ReactiveCommand fully
    // qualified, so no alias can redirect it.
    private ReactiveUI.Reactive.ReactiveCommand<Unit, Unit>? _advancedCommand;
    public ReactiveUI.Reactive.ReactiveCommand<Unit, Unit> AdvancedCommand =>
        _advancedCommand ??= ReactiveCommand.Create(Advanced);

    public void Dispose()
    {
        _typeSub.Dispose();
        Section.Dispose();
        Level.Dispose();
        Output.Dispose();
        _writer.Dispose();
    }
}
