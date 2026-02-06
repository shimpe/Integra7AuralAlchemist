using System.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainPCMDrumKitPartial : DomainBase
{
    public DomainPCMDrumKitPartial(int zeroBasedPart, int zeroBasedPartial, IIntegra7Api integra7Api,
        Integra7StartAddresses startAddresses, Integra7GzipJsonRepository parameters, SemaphoreSlim semaphore)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary PCM Drum Kit",
            $"Offset2/PCM Drum Kit Partial {zeroBasedPartial + 1}",
            "PCM Drum Kit Partial/",
            semaphore)
    {
    }
}