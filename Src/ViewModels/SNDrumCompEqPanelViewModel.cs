using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The kit's Comp-EQ section: six compressor/EQ units, one shown at a time.</summary>
public sealed class SNDrumCompEqPanelViewModel : ViewModelBase, IDisposable
{
    private const int UnitCount = 6;
    private readonly List<IDisposable> _wrappers = [];

    public IReadOnlyList<SNDrumCompEqUnitViewModel> Units { get; }

    private SNDrumCompEqUnitViewModel _selectedUnit;
    public SNDrumCompEqUnitViewModel SelectedUnit
    {
        get => _selectedUnit;
        set { if (ReferenceEquals(value, _selectedUnit) || value is null) return; this.RaiseAndSetIfChanged(ref _selectedUnit, value); }
    }

    public SNDrumCompEqPanelViewModel(DomainBase compEqDomain, ThrottledParameterWriter writer)
    {
        var byPath = ToDict(compEqDomain);
        var units = new List<SNDrumCompEqUnitViewModel>(UnitCount);
        for (var i = 1; i <= UnitCount; i++) units.Add(Track(new SNDrumCompEqUnitViewModel(compEqDomain, byPath, writer, i)));
        Units = units;
        _selectedUnit = units[0];
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
