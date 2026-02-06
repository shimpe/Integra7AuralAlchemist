using System.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainStudioSetPartEQ : DomainBase
{
    public DomainStudioSetPartEQ(int ZeroBasedPartNo, IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7GzipJsonRepository parameters, SemaphoreSlim semaphore)
        : base(integra7Api, startAddresses, parameters,
            "Temporary Studio Set",
            "Offset/Not Used",
            $"Offset2/Studio Set Part EQ {ZeroBasedPartNo + 1}",
            "Studio Set Part EQ/",
            semaphore)
    {
    }
}