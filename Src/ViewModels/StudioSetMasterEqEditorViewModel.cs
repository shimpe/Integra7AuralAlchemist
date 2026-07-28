using System;
using System.Reactive;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly Studio Set Master EQ editor: the shared 3-band EQ panel — this one is always in
/// circuit, so it has no bypass switch — plus the link to the raw parameter grid.</summary>
public sealed partial class StudioSetMasterEqEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string>? _navigateToRawTab;

    public string Title => "Master EQ — the whole Studio Set, after everything else";
    public ThreeBandEqPanelViewModel Eq { get; }

    public StudioSetMasterEqEditorViewModel(Integra7Domain domain, Action<string>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;
        Eq = new ThreeBandEqPanelViewModel(domain.StudioSetCommonMasterEQ, _writer, hasSwitch: false);
    }

    // Open the raw Studio Set Master EQ grid for the full parameter set.
    public void AdvancedMasterEq() => _navigateToRawTab?.Invoke("COMMON-MASTER-EQ");

    // Hand-written rather than generated: ReactiveUI.SourceGenerators has no release that supports
    // ReactiveUI 24, and what it emits names the core's RxVoid-flavoured ReactiveCommand fully
    // qualified, so no alias can redirect it.
    private ReactiveUI.Reactive.ReactiveCommand<Unit, Unit>? _advancedMasterEqCommand;
    public ReactiveUI.Reactive.ReactiveCommand<Unit, Unit> AdvancedMasterEqCommand =>
        _advancedMasterEqCommand ??= ReactiveCommand.Create(AdvancedMasterEq);

    public void Dispose()
    {
        Eq.Dispose();
        _writer.Dispose();
    }
}
