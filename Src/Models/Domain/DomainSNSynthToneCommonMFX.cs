using System.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainSNSynthToneCommonMFX : DomainBase
{
    public DomainSNSynthToneCommonMFX(int zeroBasedPart, IIntegra7Api integra7Api,
        Integra7StartAddresses startAddresses, Integra7GzipJsonRepository parameters, SemaphoreSlim semaphore) :
        base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common MFX",
            "SuperNATURAL Synth Tone Common MFX/",
            semaphore)
    {
    }
}