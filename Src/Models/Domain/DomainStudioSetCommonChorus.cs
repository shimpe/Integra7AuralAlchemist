using System.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainStudioSetCommonChorus : DomainBase
{
    public DomainStudioSetCommonChorus(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses, Integra7Parameters parameters, SemaphoreSlim semaphore) 
    : base(integra7Api, startAddresses, parameters, 
        startAddressName:"Temporary Studio Set", 
        offsetAddressName:"Offset/Not Used", 
        offset2AddressName:"Offset2/Studio Set Common Chorus", 
        parameterNamePrefix:"Studio Set Common Chorus/",
        semaphore)
    {
    }
}
