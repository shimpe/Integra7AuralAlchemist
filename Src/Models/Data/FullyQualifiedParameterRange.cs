using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using Serilog;

namespace Integra7AuralAlchemist.Models.Data;

public class FullyQualifiedParameterRange
{
    private readonly Integra7ParameterSpec _firstPar;
    private readonly Integra7ParameterSpec _lastPar;
    private readonly string _offset;
    private readonly string _offset2;
    private readonly List<FullyQualifiedParameter> _range;
    private readonly string _start;

    public FullyQualifiedParameterRange(string start, string offset, string offset2, Integra7ParameterSpec firstPar,
        Integra7ParameterSpec lastPar)
    {
        _start = start;
        _offset = offset;
        _offset2 = offset2;
        _firstPar = firstPar;
        _lastPar = lastPar;
        _range = new List<FullyQualifiedParameter>();
    }

    public List<FullyQualifiedParameter> Range => _range ?? [];

    public void Initialize(List<FullyQualifiedParameter> parameters)
    {
        _range.Clear();
        for (var i = 0; i < parameters.Count; i++)
            _range.Add(new FullyQualifiedParameter(_start, _offset, _offset2, parameters[i].ParSpec,
                parameters[i].RawNumericValue, parameters[i].StringValue));
    }

    public async Task WriteToIntegraAsync(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
    {
        var startAddr = startAddresses.Lookup(_start).Address;
        var offsetAddr = startAddresses.Lookup(_offset).Address;
        var offset2Addr = startAddresses.Lookup(_offset2).Address;
        var firstParameterAddr = _firstPar.Address;
        var totalAddr = ByteUtils.AddressWithOffset(startAddr, offsetAddr, offset2Addr, firstParameterAddr);
        byte[] data = [];
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_range);
        for (var i = 0; i < _range.Count; i++)
        {
            var p = _range[i];
            if (p.ValidInContext(ctx)) data = ByteUtils.Flatten(data, p.GetSysexDataFragment());
        }

        await integra7Api.MakeDataTransmissionAsync(totalAddr, data);
    }

    /// <summary>Reads the range from the device. Returns false when no reply arrived, in which case
    /// <see cref="Range"/> holds freshly constructed, unparsed parameters — callers must not treat
    /// those as values read from the device.</summary>
    public async Task<bool> RetrieveFromIntegraAsync(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
    {
        var startAddr = startAddresses.Lookup(_start).Address;
        var offsetAddr = startAddresses.Lookup(_offset).Address;
        var offset2Addr = startAddresses.Lookup(_offset2).Address;
        var firstParameterAddr = _firstPar.Address;
        var totalAddr = ByteUtils.AddressWithOffset(startAddr, offsetAddr, offset2Addr, firstParameterAddr);
        List<Integra7ParameterSpec> allRelevantPars = parameters.GetParametersFromTo(_firstPar.Path, _lastPar.Path);
        _range.Clear();
        for (var i = 0; i < allRelevantPars.Count; i++)
            // range must contain all possible FullyQualifiedParameters between the first and last one for parsing.
            // This includes all copies needed for data dependencies.
            _range.Add(new FullyQualifiedParameter(_start, _offset, _offset2, allRelevantPars[i]));
        // size, however, must not count duplicates needed for data dependencies multiple times since only one of them
        // will be actually used during parsing (based on which value was read for its master control)
        long size = ParameterListSysexSizeCalculator.CalculateSysexSize(allRelevantPars);
        var reply = await integra7Api.MakeDataRequestAsync(totalAddr, size);
        if (reply.Length == 0)
        {
            Log.Error(
                "Unfortunately, no reply received after making a sysex data request. This may indicate a bug in the program, e.g. requesting parameters for a PCM synth tone if no PCM synth patch is active or having multiple instances of the application running at the same time.");
            return false;
        }

        ParseFromSysexReply(reply, parameters, _firstPar);
        return true;
    }

    public void ParseFromSysexReply(byte[] reply, Integra7Parameters parameters,
        Integra7ParameterSpec? firstParameterInSysexReply = null)
    {
        var ctx = new ParserContext();
        for (var i = 0; i < _range.Count; i++)
        {
            var p = _range[i];
            if (p.ValidInContext(ctx))
            {
                p.ParseFromSysexReply(reply, parameters, firstParameterInSysexReply);
                if (p.ParSpec.IsParent) ctx.Register(p.ParSpec.Path, p.StringValue);
                p.DebugLog();
            }
        }
    }
}