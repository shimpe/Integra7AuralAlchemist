using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainStudioSetMIDI : DomainBase
{
    public DomainStudioSetMIDI(int ZeroBasedPartNo, IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            "Temporary Studio Set",
            "Offset/Not Used",
            $"Offset2/Studio Set MIDI Channel {ZeroBasedPartNo + 1}",
            "Studio Set MIDI/")
    {
    }
}