using System;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI.SourceGenerators;

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
    [ReactiveCommand] public void AdvancedPartEq() => _navigateToRawTab?.Invoke("SET-PART-EQ", null);

    public void Dispose()
    {
        Eq.Dispose();
        _writer.Dispose();
    }
}
