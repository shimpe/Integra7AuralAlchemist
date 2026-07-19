using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainStudioSetCommonReverb : DomainBase
{
    public DomainStudioSetCommonReverb(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            "Temporary Studio Set",
            "Offset/Not Used",
            "Offset2/Studio Set Common Reverb",
            "Studio Set Common Reverb/")
    {
    }
}