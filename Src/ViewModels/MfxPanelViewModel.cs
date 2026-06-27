using System;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>
/// Friendly, tone-wide Multi-Effect panel. Hosts the shared <see cref="DiscriminatedParamSectionViewModel"/>
/// (family -> type picker + dynamic per-type parameter grid over the "MFX Type" discriminator) and adds
/// the MFX-specific extras: Bypass (Thru), Chorus/Reverb send faders, and the advanced-tab link.
/// Engine-agnostic: pass ANY engine's "Common MFX" <see cref="DomainBase"/> — the MFX parameter set is
/// identical across engines, only the path prefix differs.
/// </summary>
public sealed class MfxPanelViewModel : ViewModelBase, IDisposable
{
    private const string Thru = "Thru";

    private readonly IDisposable _typeSub;
    private string _lastEffectType = "Equalizer";

    /// <summary>Family -> type picker + dynamic per-type parameter grid (shared component).</summary>
    public DiscriminatedParamSectionViewModel Section { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }
    public ReactiveCommand<Unit, Unit> AdvancedMfxCommand { get; }

    public MfxPanelViewModel(DomainBase mfxDomain, ThrottledParameterWriter writer, Action navigateToAdvanced)
    {
        // Look up sends by leaf name so the panel works for ANY engine's Common MFX domain.
        var all = mfxDomain.GetRelevantParameters(true, true);
        FullyQualifiedParameter ByName(string name) => all.First(p => p.ParSpec.Name == name);

        Section = new DiscriminatedParamSectionViewModel(mfxDomain, writer,
            "MFX Type", "/MFX Parameter ",
            MfxCatalog.Families.Select(f => f.Name).ToList(),
            MfxCatalog.FamilyOf, MfxCatalog.TypesIn,
            ConditionalParamLabels.FriendlyNames);

        ChorusSend = new ParamInt(mfxDomain, ByName("MFX Chorus Send Level"), writer, 0, 127);
        ReverbSend = new ParamInt(mfxDomain, ByName("MFX Reverb Send Level"), writer, 0, 127);

        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Bypass is a view over the discriminator (MFX Type == Thru); re-raise it on any type change.
        _typeSub = Section.Discriminator.WhenAnyValue(d => d.Value)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Bypass)));
    }

    /// <summary>Bypass = the effect is Thru. Toggling on remembers the last real effect; off restores it.</summary>
    public bool Bypass
    {
        get => Section.Discriminator.Value == Thru;
        set
        {
            if (value == Bypass) return;
            if (value)
            {
                if (Section.Discriminator.Value != Thru) _lastEffectType = Section.Discriminator.Value;
                Section.Discriminator.Value = Thru;
            }
            else
            {
                Section.Discriminator.Value = _lastEffectType;
            }
        }
    }

    public void Dispose()
    {
        _typeSub.Dispose();
        AdvancedMfxCommand.Dispose();
        Section.Dispose();
        ChorusSend.Dispose();
        ReverbSend.Dispose();
    }
}
