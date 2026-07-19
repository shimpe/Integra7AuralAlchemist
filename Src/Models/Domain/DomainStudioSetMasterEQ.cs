using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainStudioSetMasterEQ : DomainBase
{
    public DomainStudioSetMasterEQ(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            "Temporary Studio Set",
            "Offset/Not Used",
            "Offset2/Studio Set Master EQ",
            "Studio Set Master EQ/")
    {
    }
}