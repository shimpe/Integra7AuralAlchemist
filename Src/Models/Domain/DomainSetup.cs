using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;


namespace Integra7AuralAlchemist.Models.Domain;

public class DomainSetup : DomainBase
{
    public DomainSetup(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses, Integra7Parameters parameters) 
    : base(integra7Api, startAddresses, parameters, "Setup", "Offset/Setup Sound Mode", "Setup/")
    {
    }
}