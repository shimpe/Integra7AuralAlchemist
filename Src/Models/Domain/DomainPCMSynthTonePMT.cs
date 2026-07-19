using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainPCMSynthTonePMT : DomainBase
{
    public DomainPCMSynthTonePMT(int zeroBasedPart, IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Partial Mix Table",
            "PCM Synth Tone Partial Mix Table/")
    {
    }
}