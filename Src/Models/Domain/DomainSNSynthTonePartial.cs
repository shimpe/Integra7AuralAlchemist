using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainSNSynthTonePartial : DomainBase
{
    public DomainSNSynthTonePartial(int zeroBasedPart, int zeroBasedPartial, IIntegra7Api integra7Api,
        Integra7StartAddresses startAddresses, Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary SuperNATURAL Synth Tone",
            $"Offset2/SuperNATURAL Synth Tone Partial {zeroBasedPartial + 1}",
            "SuperNATURAL Synth Tone Partial/")
    {
    }
}