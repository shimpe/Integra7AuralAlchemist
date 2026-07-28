using System;
using System.Reactive;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly Studio Set Part EQ editor for ONE part: the shared 3-band EQ panel (this one has
/// a bypass switch) plus the link to the raw parameter grid.</summary>
public sealed partial class StudioSetPartEqEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;

    public int PartNo { get; }
    public string Title => $"Part {PartNo + 1} EQ";
    public ThreeBandEqPanelViewModel Eq { get; }

    public StudioSetPartEqEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        PartNo = partNo;
        _navigateToRawTab = navigateToRawTab;
        Eq = new ThreeBandEqPanelViewModel(domain.StudioSetPartEQ(partNo), _writer, hasSwitch: true);
    }

    // Open the raw Studio Set Part EQ grid for the full parameter set.
    public void AdvancedPartEq() => _navigateToRawTab?.Invoke("SET-PART-EQ", null);

    // Hand-written rather than generated: ReactiveUI.SourceGenerators has no release that supports
    // ReactiveUI 24, and what it emits names the core's RxVoid-flavoured ReactiveCommand fully
    // qualified, so no alias can redirect it.
    private ReactiveUI.Reactive.ReactiveCommand<Unit, Unit>? _advancedPartEqCommand;
    public ReactiveUI.Reactive.ReactiveCommand<Unit, Unit> AdvancedPartEqCommand =>
        _advancedPartEqCommand ??= ReactiveCommand.Create(AdvancedPartEq);

    public void Dispose()
    {
        Eq.Dispose();
        _writer.Dispose();
    }
}
