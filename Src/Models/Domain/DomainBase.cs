using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using Serilog;

namespace Integra7AuralAlchemist.Models.Domain;

public class DomainBase
{
    private readonly List<FullyQualifiedParameter> _domainParameters = [];
    private readonly IIntegra7Api _integra7Api;
    private readonly Integra7Parameters _parameters;

    private readonly SemaphoreSlim _semaphore;
    private readonly Integra7StartAddresses _startAddresses;

    public DomainBase(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses, Integra7Parameters parameters,
        string startAddressName, string offsetAddressName, string offset2AddressName, string parameterNamePrefix,
        SemaphoreSlim semaphore)
    {
        _integra7Api = integra7Api;
        _startAddresses = startAddresses;
        _parameters = parameters;
        StartAddressName = startAddressName;
        OffsetAddressName = offsetAddressName;
        Offset2AddressName = offset2AddressName;
        _semaphore = semaphore;

        List<Integra7ParameterSpec> relevant = parameters.GetParametersWithPrefix(parameterNamePrefix);
        for (var i = 0; i < relevant.Count; i++)
            _domainParameters.Add(new FullyQualifiedParameter(startAddressName, offsetAddressName, offset2AddressName,
                relevant[i]));
    }

    public string StartAddressName { get; }

    public string OffsetAddressName { get; }

    public string Offset2AddressName { get; }

    public async Task ReadFromIntegraAsync()
    {
        Log.Debug(
            $"Reading range of parameters (start address:{_domainParameters[0].Start}, offset address: {_domainParameters[0].Offset}, offset2 address: {_domainParameters[0].Offset2}) between {_domainParameters[0].ParSpec.Path} and {_domainParameters.Last().ParSpec.Path} from integra.");
        var r = new FullyQualifiedParameterRange(_domainParameters[0].Start,
            _domainParameters[0].Offset,
            _domainParameters[0].Offset2,
            _domainParameters[0].ParSpec,
            _domainParameters.Last().ParSpec);
        await r.RetrieveFromIntegraAsync(_integra7Api, _startAddresses, _parameters);
        for (var i = 0; i < r.Range.Count; i++) _domainParameters[i].CopyParsedDataFrom(r.Range[i]);
        // Resolve bank-selected waveform names (no-op for domains without wave params).
        WaveNameResolution.Apply(_domainParameters, WaveformBanks.Default);
        // Filter the Wave Group ID (SRX board) options to the currently-loaded SRX boards.
        SrxGroupIdResolution.Apply(_domainParameters, LoadedSrxState.Default.Boards);
    }

    public async Task WriteToIntegraAsync()
    {
        Log.Debug(
            $"Writing range of parameters (start address:{_domainParameters[0].Start}, offset address: {_domainParameters[0].Offset}), offset2 address: {_domainParameters[0].Offset2} between {_domainParameters[0].ParSpec.Path} and {_domainParameters.Last().ParSpec.Path} to integra.");
        var r = new FullyQualifiedParameterRange(_domainParameters[0].Start,
            _domainParameters[0].Offset,
            _domainParameters[0].Offset2,
            _domainParameters[0].ParSpec,
            _domainParameters.Last().ParSpec);
        r.Initialize(_domainParameters);
        await r.WriteToIntegraAsync(_integra7Api, _startAddresses, _parameters);
    }

    public async Task<FullyQualifiedParameter?> ReadFromIntegraAsync(string parameterName)
    {
        Log.Debug(
            $"Reading single parameter {parameterName}, (start address:{_domainParameters[0].Start}, offset address: {_domainParameters[0].Offset}), offset2 address: {_domainParameters[0].Offset2}) from integra.");
        var found = false;
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);

        for (var i = 0; i < _domainParameters.Count && !found; i++)
        {
            var p = _domainParameters[i];
            if (p.ValidInContext(ctx) && p.ParSpec.Path == parameterName)
            {
                found = true;
                await p.RetrieveFromIntegraAsync(_integra7Api, _startAddresses, _parameters);
                p.DebugLog();
                return p;
            }
        }

        if (!found) Log.Error($"parameter {parameterName} does not exist, or is not valid in the current context.");

        return null;
    }

    public async Task WriteToIntegraAsync(string parameterName)
    {
        Log.Debug(
            $"Writing single parameter {parameterName}, (start address:{_domainParameters[0].Start}, offset address: {_domainParameters[0].Offset}), offset2 address: {_domainParameters[0].Offset2}) to integra.");
        var found = false;
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);
        for (var i = 0; i < _domainParameters.Count && !found; i++)
        {
            var p = _domainParameters[i];
            if (p.ValidInContext(ctx) && p.ParSpec.Path == parameterName)
            {
                found = true;
                await p.WriteToIntegraAsync(_integra7Api, _startAddresses, _parameters);
                p.DebugLog();
            }
        }

        if (!found) Log.Error($"parameter {parameterName} does not exist, or is not valid in the current context.");
    }

    public async Task WriteToIntegraAsync(string parameterName, string displayedValue)
    {
        ModifySingleParameterDisplayedValue(parameterName, displayedValue);
        await WriteToIntegraAsync(parameterName);
    }

    public string LookupSingleParameterDisplayedValue(string parameterName)
    {
        Log.Debug($"Look up value of parameter {parameterName}");
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);

        for (var i = 0; i < _domainParameters.Count; i++)
        {
            var p = _domainParameters[i];
            if (p.ValidInContext(ctx) && p.ParSpec.Path == parameterName)
            {
                var v = p.StringValue;
                Log.Debug($"Value found to be {v}");
                return v;
            }
        }

        Log.Error($"Could not find the value of parameter {parameterName}");
        return "";
    }

    public void ModifySingleParameterDisplayedValue(string parameterName, string displayedValue)
    {
        var found = false;
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);

        for (var i = 0; i < _domainParameters.Count && !found; i++)
        {
            var p = _domainParameters[i];
            if (p.ValidInContext(ctx) && p.ParSpec.Path == parameterName)
            {
                found = true;
                DisplayValueToRawValueConverter.UpdateFromDisplayedValue(displayedValue, p);
                p.DebugLog();
            }
        }

        if (!found)
            // A queued/throttled write can legitimately land after its parameter became invalid in the
            // current context — e.g. an MFX per-type parameter whose effect type (or a sub-switch such
            // as a delay's ms/Note selector) changed before the debounced write fired. Skip it
            // gracefully and log (matching WriteToIntegraAsync(parameterName)) rather than asserting,
            // which would terminate a Debug build on a benign race.
            Log.Error($"Parameter {parameterName} does not exist or is not valid in the current context; skipping stale write.");
    }

    private List<string> GetParameterNames(bool IncludeReserved = false, bool IncludeInvalidIncontext = false)
    {
        List<string> names = [];
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);

        for (var i = 0; i < _domainParameters.Count; i++)
        {
            var p = _domainParameters[i].ParSpec;
            if (_domainParameters[i].ValidInContext(ctx) || IncludeInvalidIncontext)
                if ((p.Reserved && IncludeReserved) || !p.Reserved)
                    names.Add(p.Path);
        }

        return names;
    }

    public List<FullyQualifiedParameter> GetRelevantParameters(bool IncludeReserved = false,
        bool IncludeInvalidIncontext = false)
    {
        var ctx = new ParserContext();
        ctx.InitializeFromExistingData(_domainParameters);
        List<FullyQualifiedParameter> pars = [];
        for (var i = 0; i < _domainParameters.Count; i++)
        {
            var p = _domainParameters[i].ParSpec;
            if (_domainParameters[i].ValidInContext(ctx) || IncludeInvalidIncontext)
                if ((p.Reserved && IncludeReserved) || !p.Reserved)
                    pars.Add(_domainParameters[i]);
        }

        return pars;
    }
}