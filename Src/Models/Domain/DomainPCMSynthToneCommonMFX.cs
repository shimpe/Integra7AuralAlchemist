using System.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainPCMSynthToneCommonMFX : DomainBase
{
    public DomainPCMSynthToneCommonMFX(int zeroBasedPart, IIntegra7Api integra7Api,
        Integra7StartAddresses startAddresses, Integra7GzipJsonRepository parameters, SemaphoreSlim semaphore)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Common MFX",
            "PCM Synth Tone Common MFX/",
            semaphore)
    {
    }
}