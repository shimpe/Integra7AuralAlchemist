using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Domain;

public class Integra7Domain
{
    private Integra7StartAddresses _integra7StartAddresses;
    private Integra7Parameters _integra7Parameters;
    private IIntegra7Api _integra7Api;
    private Dictionary<Tuple<string, string>, DomainBase> _parameterMapper; // (start addr name, offset addr name) -> DomainBase
    private Dictionary<long, FullyQualifiedParameter> _sysexAddressMapper; // (long)address -> (DomainBase, parameter name)

    public DomainBase StudioSetCommon
    {
        get => _parameterMapper[new Tuple<string, string>("Temporary Studio Set", "Offset/Studio Set Common")];
    }

    public DomainBase StudioSetCommonChorus
    {
        get => _parameterMapper[new Tuple<string, string>("Temporary Studio Set", "Offset/Studio Set Common Chorus")];
    }

    private const int NO_OF_PARTS = 16;


    public Integra7Domain(IIntegra7Api integra7Api, Integra7StartAddresses i7startAddresses, Integra7Parameters i7parameters)
    {
        _integra7StartAddresses = i7startAddresses;
        _integra7Parameters = i7parameters;
        _integra7Api = integra7Api;

        _parameterMapper = [];

        DomainBase setup = new DomainSetup(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setup.StartAddressName, setup.OffsetAddressName)] = setup;

        DomainBase sys = new DomainSystem(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(sys.StartAddressName, sys.OffsetAddressName)] = sys;

        DomainBase setcommon = new DomainStudioSetCommon(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setcommon.StartAddressName, setcommon.OffsetAddressName)] = setcommon;

        DomainBase setchorus = new DomainStudioSetCommonChorus(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setchorus.StartAddressName, setchorus.OffsetAddressName)] = setchorus;

        DomainBase setreverb = new DomainStudioSetCommonReverb(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setreverb.StartAddressName, setreverb.OffsetAddressName)] = setreverb;

        DomainBase setsurround = new DomainStudioSetCommonMotionalSurround(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setsurround.StartAddressName, setsurround.OffsetAddressName)] = setsurround;

        DomainBase setmastereq = new DomainStudioSetMasterEQ(integra7Api, i7startAddresses, i7parameters);
        _parameterMapper[new Tuple<string, string>(setmastereq.StartAddressName, setmastereq.OffsetAddressName)] = setmastereq;

        for (int i = 0; i < NO_OF_PARTS; i++)
        {
            DomainBase setmidi = new DomainStudioSetMIDI(i, integra7Api, i7startAddresses, i7parameters);
            _parameterMapper[new Tuple<string, string>(setmidi.StartAddressName, setmidi.OffsetAddressName)] = setmidi;

            DomainBase setpart = new DomainStudioSetPart(i, integra7Api, i7startAddresses, i7parameters);
            _parameterMapper[new Tuple<string, string>(setpart.StartAddressName, setpart.OffsetAddressName)] = setpart;

            DomainBase setparteq = new DomainStudioSetPartEQ(i, integra7Api, i7startAddresses, i7parameters);
            _parameterMapper[new Tuple<string, string>(setparteq.StartAddressName, setparteq.OffsetAddressName)] = setparteq;

            DomainBase pcmsynthtone = new DomainPCMSynthToneCommon(i, integra7Api, i7startAddresses, i7parameters);
            _parameterMapper[new Tuple<string, string>(pcmsynthtone.StartAddressName, pcmsynthtone.OffsetAddressName)] = pcmsynthtone;

            DomainBase pcmsynthtonemfx = new DomainPCMSynthToneCommonMFX(i, integra7Api, i7startAddresses, i7parameters);
            _parameterMapper[new Tuple<string, string>(pcmsynthtonemfx.StartAddressName, pcmsynthtonemfx.OffsetAddressName)] = pcmsynthtonemfx;
        }

        _sysexAddressMapper = [];
        foreach (KeyValuePair<Tuple<string, string>, DomainBase> entry in _parameterMapper)
        {
            DomainBase b = entry.Value;
            List<FullyQualifiedParameter> ps = b.GetRelevantParameters(true, true);
            foreach (FullyQualifiedParameter p in ps)
            {
                long CompleteAddress = ByteUtils.Bytes7ToInt(p.CompleteAddress(i7startAddresses, i7parameters));
                _sysexAddressMapper[CompleteAddress] = p;
            }
        }
    }

    public void WriteSingleParameterToIntegra(FullyQualifiedParameter p)
    {
        Tuple<string, string> key = new(p.Start, p.Offset);
        if (!_parameterMapper.ContainsKey(key))
        {
            Debug.WriteLine($"Error. Integra7 doesn't know parameters with start address {p.Start} and offset address {p.Offset}. Please extend or fix.");
            return;
        }
        DomainBase b = _parameterMapper[key];
        b.WriteToIntegra(p.ParSpec.Path, p.StringValue);
    }

    public DomainBase GetDomain(FullyQualifiedParameter p)
    {
        Tuple<string, string> key = new(p.Start, p.Offset);
        if (!_parameterMapper.ContainsKey(key))
        {

            Debug.WriteLine($"Error. Integra7 doesn't know parameters with start address {p.Start} and offset address {p.Offset}. Please extend or fix.");
            return _parameterMapper.First().Value;
        }
        return _parameterMapper[key];
    }


}