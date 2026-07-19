using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainSNDrumKitCommon : DomainBase
{
    public DomainSNDrumKitCommon(int zeroBasedPart, IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary SuperNATURAL Drum Kit",
            "Offset2/SuperNATURAL Drum Kit Common",
            "SuperNATURAL Drum Kit Common/")
    {
    }
}