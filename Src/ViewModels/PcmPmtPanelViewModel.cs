using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Tone-level Partial Mix Table panel: the four key/velocity zones plus the per-pair
/// Structure/Booster and the PMT Velocity-Control switch.</summary>
public sealed class PcmPmtPanelViewModel : ViewModelBase, IDisposable
{
    private const string PMT = "PCM Synth Tone Partial Mix Table/";
    private readonly List<IDisposable> _wrappers = [];

    public PcmPmtZoneViewModel Zone1 { get; }
    public PcmPmtZoneViewModel Zone2 { get; }
    public PcmPmtZoneViewModel Zone3 { get; }
    public PcmPmtZoneViewModel Zone4 { get; }
    public IReadOnlyList<PcmPmtZoneViewModel> Zones { get; }

    public ParamString VelocityControl { get; }   // Off / On / Random / Cycle
    public ParamInt Structure12 { get; }
    public ParamString Booster12 { get; }
    public ParamInt Structure34 { get; }
    public ParamString Booster34 { get; }

    public PcmPmtPanelViewModel(DomainBase pmtDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        Zone1 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 1));
        Zone2 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 2));
        Zone3 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 3));
        Zone4 = Track(new PcmPmtZoneViewModel(pmtDomain, byPath, writer, 4));
        Zones = new[] { Zone1, Zone2, Zone3, Zone4 };

        VelocityControl = Track(new ParamString(pmtDomain, byPath[PMT + "PMT Velocity Control"], writer));
        Structure12 = Track(new ParamInt(pmtDomain, byPath[PMT + "Structure Type 1 & 2"], writer, 1, 10));
        Booster12 = Track(new ParamString(pmtDomain, byPath[PMT + "Booster 1 & 2"], writer, new[] { "0", "6", "12", "18" }));
        Structure34 = Track(new ParamInt(pmtDomain, byPath[PMT + "Structure Type 3 & 4"], writer, 1, 10));
        Booster34 = Track(new ParamString(pmtDomain, byPath[PMT + "Booster 3 & 4"], writer, new[] { "0", "6", "12", "18" }));
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }
    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
