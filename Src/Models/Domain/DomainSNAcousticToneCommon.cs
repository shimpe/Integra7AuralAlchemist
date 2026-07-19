using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainSNAcousticToneCommon : DomainBase
{
    public DomainSNAcousticToneCommon(int zeroBasedPart, IIntegra7Api integra7Api,
        Integra7StartAddresses startAddresses, Integra7Parameters parameters)
        : base(integra7Api, startAddresses, parameters,
            $"Temporary Tone Part {zeroBasedPart + 1}",
            "Offset/Temporary SuperNATURAL Acoustic Tone",
            "Offset2/SuperNATURAL Acoustic Tone Common",
            "SuperNATURAL Acoustic Tone Common/")
    {
    }
}