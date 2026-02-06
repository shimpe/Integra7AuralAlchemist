using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using Serilog;

namespace Integra7AuralAlchemist.Models.Domain;

public class Integra7Domain
{
    private readonly Dictionary<Tuple<string, string, string>, DomainBase>
        _parameterMapper; // (start addr name, offset addr name, offset2 addr name) -> DomainBase

    private readonly Dictionary<long, List<FullyQualifiedParameter>>
        _sysexAddressMapper; // (long)address -> (DomainBase, parameter name)

    private IIntegra7Api _integra7Api;
    private Integra7GzipJsonRepository _integra7Parameters;
    private Integra7StartAddresses _integra7StartAddresses;


    public Integra7Domain(IIntegra7Api integra7Api, Integra7StartAddresses i7startAddresses,
        Integra7GzipJsonRepository i7parameters, SemaphoreSlim semaphore)
    {
        _integra7StartAddresses = i7startAddresses;
        _integra7Parameters = i7parameters;
        _integra7Api = integra7Api;

        _parameterMapper = [];

        DomainBase setup = new DomainSetup(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setup.StartAddressName, setup.OffsetAddressName,
                setup.Offset2AddressName)] = setup;

        DomainBase sys = new DomainSystem(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(sys.StartAddressName, sys.OffsetAddressName,
                sys.Offset2AddressName)] = sys;

        DomainBase setcommon = new DomainStudioSetCommon(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setcommon.StartAddressName, setcommon.OffsetAddressName,
                setcommon.Offset2AddressName)] = setcommon;

        DomainBase setchorus = new DomainStudioSetCommonChorus(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setchorus.StartAddressName, setchorus.OffsetAddressName,
                setchorus.Offset2AddressName)] = setchorus;

        DomainBase setreverb = new DomainStudioSetCommonReverb(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setreverb.StartAddressName, setreverb.OffsetAddressName,
                setreverb.Offset2AddressName)] = setreverb;

        DomainBase setsurround =
            new DomainStudioSetCommonMotionalSurround(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setsurround.StartAddressName, setsurround.OffsetAddressName,
                setsurround.Offset2AddressName)] = setsurround;

        DomainBase setmastereq = new DomainStudioSetMasterEQ(integra7Api, i7startAddresses, i7parameters, semaphore);
        _parameterMapper[
            new Tuple<string, string, string>(setmastereq.StartAddressName, setmastereq.OffsetAddressName,
                setmastereq.Offset2AddressName)] = setmastereq;

        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            DomainBase setmidi = new DomainStudioSetMIDI(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(setmidi.StartAddressName, setmidi.OffsetAddressName,
                    setmidi.Offset2AddressName)] = setmidi;

            DomainBase setpart = new DomainStudioSetPart(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(setpart.StartAddressName, setpart.OffsetAddressName,
                    setpart.Offset2AddressName)] = setpart;

            DomainBase setparteq = new DomainStudioSetPartEQ(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(setparteq.StartAddressName, setparteq.OffsetAddressName,
                    setparteq.Offset2AddressName)] = setparteq;

            DomainBase pcmsynthtone =
                new DomainPCMSynthToneCommon(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmsynthtone.StartAddressName, pcmsynthtone.OffsetAddressName,
                    pcmsynthtone.Offset2AddressName)] = pcmsynthtone;

            DomainBase pcmsynthtone2 =
                new DomainPCMSynthToneCommon2(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper
                    [new Tuple<string, string, string>(pcmsynthtone2.StartAddressName, pcmsynthtone2.OffsetAddressName, pcmsynthtone2.Offset2AddressName)] =
                pcmsynthtone2;

            DomainBase pcmsynthtonemfx =
                new DomainPCMSynthToneCommonMFX(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmsynthtonemfx.StartAddressName,
                    pcmsynthtonemfx.OffsetAddressName, pcmsynthtonemfx.Offset2AddressName)] = pcmsynthtonemfx;

            DomainBase pcmsynthtonepmt =
                new DomainPCMSynthTonePMT(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmsynthtonepmt.StartAddressName,
                    pcmsynthtonepmt.OffsetAddressName, pcmsynthtonepmt.Offset2AddressName)] = pcmsynthtonepmt;

            for (var j = 0; j < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; j++)
            {
                DomainBase part =
                    new DomainPCMSynthTonePartial(i, j, integra7Api, i7startAddresses, i7parameters, semaphore);
                _parameterMapper[
                    new Tuple<string, string, string>(part.StartAddressName, part.OffsetAddressName,
                        part.Offset2AddressName)] = part;
            }

            DomainBase pcmdrumkit =
                new DomainPCMDrumKitCommon(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmdrumkit.StartAddressName, pcmdrumkit.OffsetAddressName,
                    pcmdrumkit.Offset2AddressName)] = pcmdrumkit;

            DomainBase pcmdrumkit2 =
                new DomainPCMDrumKitCommon2(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper
                    [new Tuple<string, string, string>(pcmdrumkit2.StartAddressName, pcmdrumkit2.OffsetAddressName, pcmdrumkit2.Offset2AddressName)] =
                pcmdrumkit2;

            DomainBase pcmdrumkitmfx =
                new DomainPCMDrumKitCommonMFX(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmdrumkitmfx.StartAddressName,
                    pcmdrumkitmfx.OffsetAddressName, pcmdrumkitmfx.Offset2AddressName)] = pcmdrumkitmfx;

            DomainBase pcmdrumkitcompeq =
                new DomainPCMDrumKitCommonCompEQ(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(pcmdrumkitcompeq.StartAddressName,
                    pcmdrumkitcompeq.OffsetAddressName, pcmdrumkitcompeq.Offset2AddressName)] = pcmdrumkitcompeq;

            for (var j = 0; j < Constants.NO_OF_PARTIALS_PCM_DRUM; j++)
            {
                DomainBase part =
                    new DomainPCMDrumKitPartial(i, j, integra7Api, i7startAddresses, i7parameters, semaphore);
                _parameterMapper[
                    new Tuple<string, string, string>(part.StartAddressName, part.OffsetAddressName,
                        part.Offset2AddressName)] = part;
            }

            DomainBase snstonecommon =
                new DomainSNSynthToneCommon(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(snstonecommon.StartAddressName,
                    snstonecommon.OffsetAddressName, snstonecommon.Offset2AddressName)] = snstonecommon;

            DomainBase snstonemfx =
                new DomainSNSynthToneCommonMFX(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(snstonemfx.StartAddressName, snstonemfx.OffsetAddressName,
                    snstonemfx.Offset2AddressName)] = snstonemfx;

            for (var j = 0; j < Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; j++)
            {
                DomainBase part =
                    new DomainSNSynthTonePartial(i, j, integra7Api, i7startAddresses, i7parameters, semaphore);
                _parameterMapper[
                    new Tuple<string, string, string>(part.StartAddressName, part.OffsetAddressName,
                        part.Offset2AddressName)] = part;
            }

            DomainBase snatonecommon =
                new DomainSNAcousticToneCommon(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(snatonecommon.StartAddressName,
                    snatonecommon.OffsetAddressName, snatonecommon.Offset2AddressName)] = snatonecommon;

            DomainBase snatonemfx =
                new DomainSNAcousticToneCommonMFX(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(snatonemfx.StartAddressName, snatonemfx.OffsetAddressName,
                    snatonemfx.Offset2AddressName)] = snatonemfx;

            DomainBase sndcommon = new DomainSNDrumKitCommon(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(sndcommon.StartAddressName,
                    sndcommon.OffsetAddressName, sndcommon.Offset2AddressName)] = sndcommon;

            DomainBase sndcommonmfx =
                new DomainSNDrumKitCommonMFX(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(sndcommonmfx.StartAddressName, sndcommonmfx.OffsetAddressName,
                    sndcommonmfx.Offset2AddressName)] = sndcommonmfx;

            DomainBase sndcommoncompeq =
                new DomainSNDrumKitCommonCompEQ(i, integra7Api, i7startAddresses, i7parameters, semaphore);
            _parameterMapper[
                new Tuple<string, string, string>(sndcommoncompeq.StartAddressName, sndcommoncompeq.OffsetAddressName,
                    sndcommoncompeq.Offset2AddressName)] = sndcommoncompeq;

            for (var j = 0; j < Constants.NO_OF_PARTIALS_SN_DRUM; j++)
            {
                DomainBase part =
                    new DomainSNDrumKitPartial(i, j, integra7Api, i7startAddresses, i7parameters, semaphore);
                _parameterMapper[
                    new Tuple<string, string, string>(part.StartAddressName, part.OffsetAddressName,
                        part.Offset2AddressName)] = part;
            }
        }

        _sysexAddressMapper = [];
        foreach (KeyValuePair<Tuple<string, string, string>, DomainBase> entry in _parameterMapper)
        {
            var b = entry.Value;
            List<FullyQualifiedParameter> ps = b.GetRelevantParameters(true, true);
            foreach (var p in ps)
            {
                var completeAddress = ByteUtils.Bytes7ToInt(p.CompleteAddress(i7startAddresses, i7parameters));
                if (_sysexAddressMapper.ContainsKey(completeAddress))
                    //Debug.WriteLine($"parameter {p.ParSpec.Path} shares address of {_sysexAddressMapper[CompleteAddress].First().ParSpec.Path}");
                    _sysexAddressMapper[completeAddress].Add(p);
                else
                    _sysexAddressMapper[completeAddress] = [p];
            }
        }
    }

    public DomainBase Setup => _parameterMapper[new Tuple<string, string, string>(
        "Setup", "Offset/Not Used", "Offset2/Setup Sound Mode")];

    public DomainBase StudioSetCommon => _parameterMapper[new Tuple<string, string, string>(
        "Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common")];

    public DomainBase StudioSetCommonChorus => _parameterMapper[new Tuple<string, string, string>(
        "Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common Chorus")];

    public DomainBase StudioSetCommonReverb => _parameterMapper[new Tuple<string, string, string>(
        "Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common Reverb")];

    public DomainBase StudioSetCommonMotionalSurround => _parameterMapper[new Tuple<string, string, string>(
        "Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common Motional Surround")];

    public DomainBase StudioSetCommonMasterEQ => _parameterMapper[new Tuple<string, string, string>(
        "Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Master EQ")];

    public DomainBase System => _parameterMapper[new Tuple<string, string, string>(
        "System", "Offset/Not Used", "Offset2/System Common")];

    public DomainBase StudioSetMidi(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            "Temporary Studio Set", "Offset/Not Used", $"Offset2/Studio Set MIDI Channel {zeroBasedPartNo + 1}")];
    }

    public DomainBase StudioSetPart(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            "Temporary Studio Set", "Offset/Not Used", $"Offset2/Studio Set Part {zeroBasedPartNo + 1}")];
    }

    public DomainBase StudioSetPartEQ(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            "Temporary Studio Set", "Offset/Not Used", $"Offset2/Studio Set Part EQ {zeroBasedPartNo + 1}")];
    }

    public DomainBase PCMSynthToneCommon(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Common")];
    }

    public DomainBase PCMSynthToneCommon2(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Common 2")];
    }

    public DomainBase PCMSynthToneCommonMFX(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Common MFX")];
    }

    public DomainBase PCMSynthTonePMT(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Synth Tone",
            "Offset2/PCM Synth Tone Partial Mix Table")];
    }

    public DomainBase PCMSynthTonePartial(int zeroBasedPartNo, int zeroBasedPartial)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Synth Tone",
            $"Offset2/PCM Synth Tone Partial {zeroBasedPartial + 1}")];
    }

    public DomainBase PCMDrumKitCommon(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Drum Kit",
            "Offset2/PCM Drum Kit Common")];
    }

    public DomainBase PCMDrumKitCommon2(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Drum Kit",
            "Offset2/PCM Drum Kit Common 2")];
    }

    public DomainBase PCMDrumKitCommonMFX(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Drum Kit",
            "Offset2/PCM Drum Kit Common MFX")];
    }

    public DomainBase PCMDrumKitCompEQ(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Drum Kit",
            "Offset2/PCM Drum Kit Common Comp-EQ")];
    }

    public DomainBase PCMDrumKitPartial(int zeroBasedPartNo, int zeroBasedPartial)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary PCM Drum Kit",
            $"Offset2/PCM Drum Kit Partial {zeroBasedPartial + 1}")];
    }

    public DomainBase SNSynthToneCommon(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common")];
    }

    public DomainBase SNSynthToneCommonMFX(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Synth Tone",
            "Offset2/SuperNATURAL Synth Tone Common MFX")];
    }

    public DomainBase SNSynthTonePartial(int zeroBasedPartNo, int zeroBasedPartial)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Synth Tone",
            $"Offset2/SuperNATURAL Synth Tone Partial {zeroBasedPartial + 1}")];
    }

    public DomainBase SNAcousticToneCommon(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Acoustic Tone",
            "Offset2/SuperNATURAL Acoustic Tone Common")];
    }

    public DomainBase SNAcousticToneCommonMFX(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Acoustic Tone",
            "Offset2/SuperNATURAL Acoustic Tone Common MFX")];
    }

    public DomainBase SNDrumKitCommon(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Drum Kit",
            "Offset2/SuperNATURAL Drum Kit Common")];
    }

    public DomainBase SNDrumKitCommonMFX(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Drum Kit",
            "Offset2/SuperNATURAL Drum Kit Common MFX")];
    }

    public DomainBase SNDrumKitCompEQ(int zeroBasedPartNo)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Drum Kit",
            "Offset2/SuperNATURAL Drum Kit Common Comp-EQ")];
    }

    public DomainBase SNDrumKitPartial(int zeroBasedPartNo, int zeroBasedPartial)
    {
        return _parameterMapper[new Tuple<string, string, string>(
            $"Temporary Tone Part {zeroBasedPartNo + 1}", "Offset/Temporary SuperNATURAL Drum Kit",
            $"Offset2/SuperNATURAL Drum Kit Partial {zeroBasedPartial + 1}")];
    }

    public FullyQualifiedParameter? LookupAddress(byte[] address)
    {
        var completeAddress = ByteUtils.Bytes7ToInt(address);
        if (_sysexAddressMapper.ContainsKey(completeAddress))
        {
            List<FullyQualifiedParameter> ps = _sysexAddressMapper[completeAddress];
            foreach (var par in ps)
            {
                var b = GetDomain(par);
                ParserContext ctx = new();
                ctx.InitializeFromExistingData(b.GetRelevantParameters());
                if (par.ValidInContext(ctx)) return par;
            }
        }

        return null;
    }

    public async Task WriteSingleParameterToIntegraAsync(FullyQualifiedParameter p)
    {
        Tuple<string, string, string> key = new(p.Start, p.Offset, p.Offset2);
        if (!_parameterMapper.ContainsKey(key))
        {
            Log.Error(
                $"Error. Integra7 doesn't know parameters with start address {p.Start} and offset address {p.Offset}. Please extend or fix.");
            return;
        }

        var b = _parameterMapper[key];
        await b.WriteToIntegraAsync(p.ParSpec.Path, p.StringValue);
    }

    public DomainBase GetDomain(FullyQualifiedParameter p)
    {
        return GetDomain(p.Start, p.Offset, p.Offset2);
    }

    public DomainBase GetDomain(string StartAddressName, string OffsetAddressName, string Offset2AddressName)
    {
        Tuple<string, string, string> key = new(StartAddressName, OffsetAddressName, Offset2AddressName);
        if (!_parameterMapper.ContainsKey(key))
        {
            Log.Error(
                $"Error. Integra7 doesn't know parameters with start address {StartAddressName}, offset address {OffsetAddressName} and offset2 address {Offset2AddressName}. Please extend or fix.");
            return _parameterMapper.First().Value;
        }

        return _parameterMapper[key];
    }
}