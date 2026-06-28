using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using Serilog;

namespace Integra7AuralAlchemist.Models.Data;

public class FullyQualifiedParameter : INotifyPropertyChanged
{
    private long _rawNumericValue;

    private string _stringValue = "";

    public FullyQualifiedParameter(string start, string offset, string offset2, Integra7ParameterSpec parspec)
    {
        Start = start;
        Offset = offset;
        Offset2 = offset2;
        ParSpec = parspec;
        IsNumeric = parspec.Type == Integra7ParameterSpec.SpecType.NUMERIC;
        IsDiscrete = parspec.Type == Integra7ParameterSpec.SpecType.DISCRETE;
    }

    public FullyQualifiedParameter(string start, string offset, string offset2, Integra7ParameterSpec parspec,
        long rawNumericValue, string stringValue)
    {
        Start = start;
        Offset = offset;
        Offset2 = offset2;
        ParSpec = parspec;
        IsNumeric = parspec.Type == Integra7ParameterSpec.SpecType.NUMERIC;
        IsDiscrete = parspec.Type == Integra7ParameterSpec.SpecType.DISCRETE;
        _rawNumericValue = rawNumericValue;
        _stringValue = stringValue;
    }

    public string Start { get; }

    public string Offset { get; }

    public string Offset2 { get; }

    public Integra7ParameterSpec ParSpec { get; }

    public bool IsNumeric { get; private set; }

    public bool IsDiscrete { get; }

    public long RawNumericValue
    {
        get => _rawNumericValue;
        set
        {
            _rawNumericValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawNumericValue)));
        }
    }

    public string StringValue
    {
        get => _stringValue;

        set
        {
            //Debug.Write($"changing _stringValue from {_stringValue} to {value}.");
            _stringValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StringValue)));
        }
    }

    /// <summary>When set, the name list to use instead of <see cref="ParSpec"/>.Repr (e.g. a wave bank
    /// selected by sibling Group Type/ID). Null for ordinary parameters. Callers use
    /// EffectiveRepr ?? ParSpec.Repr. Set by the domain's post-read resolution pass.</summary>
    public IDictionary<int, string>? EffectiveRepr { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ValidInContext(ParserContext ctx)
    {
        if (ParSpec.ParentCtrl != "")
        {
            if (ctx.Contains(ParSpec.ParentCtrl))
            {
                var value = ctx.Lookup(ParSpec.ParentCtrl);
                var stillValid = ParSpec.ParentCtrlDispValue == value;
                if (stillValid)
                {
                    if (ParSpec.ParentCtrl2 != "")
                    {
                        // StillValid, but a second level dependency also must be fulfilled
                        if (ctx.Contains(ParSpec.ParentCtrl2))
                        {
                            var value2 = ctx.Lookup(ParSpec.ParentCtrl2);
                            return value2 == ParSpec.ParentCtrlDispValue2;
                        }

                        Debug.Assert(false,
                            $"Cannot parse {ParSpec.Path} without context {ParSpec.ParentCtrl2}. Did you forget to set isparent==true in {ParSpec.ParentCtrl}?");
                        return false;
                    }

                    return true; // StillValid and no need to check second level dependency
                }

                return false; // no longer valid, no need to check deeper
            }

            Debug.Assert(false, $"Cannot parse {ParSpec.Path} without context {ParSpec.ParentCtrl}");
            return false;
        }

        return true;
    }

    public byte[] CompleteAddress(Integra7StartAddresses startAddresses, Integra7Parameters parameters)
    {
        var startAddr = startAddresses.Lookup(Start).Address;
        var offsetAddr = startAddresses.Lookup(Offset).Address;
        var offset2Addr = startAddresses.Lookup(Offset2).Address;
        var parameterAddr = ParSpec.Address;
        var totalAddr = ByteUtils.AddressWithOffset(startAddr, offsetAddr, offset2Addr, parameterAddr);
        return totalAddr;
    }

    public async Task RetrieveFromIntegraAsync(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
    {
        var totalAddr = CompleteAddress(startAddresses, parameters);
        var reply = await integra7Api.MakeDataRequestAsync(totalAddr, ParSpec.Bytes);
        ParseFromSysexReply(reply, parameters);
    }

    public async Task WriteToIntegraAsync(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters)
    {
        var totalAddr = CompleteAddress(startAddresses, parameters);
        var data = GetSysexDataFragment();
        await integra7Api.MakeDataTransmissionAsync(totalAddr, data);
    }

    public void ParseFromSysexReply(byte[] reply, Integra7Parameters parameters,
        Integra7ParameterSpec? firstParameterInSysexReply = null)
    {
        firstParameterInSysexReply ??= ParSpec;

        const int SYSEX_DATA_REPLY_HEADER_LENGTH = 11;
        List<Integra7ParameterSpec> parametersInSysexReply =
            parameters.GetParametersFromTo(firstParameterInSysexReply.Value.Path, ParSpec.Path);
        var dataToSkip = SYSEX_DATA_REPLY_HEADER_LENGTH;
        var gap = ParameterListSysexSizeCalculator.CalculateSysexGapBetweenFirstAndLast(parametersInSysexReply);
        dataToSkip += gap;
        if (reply.Length > dataToSkip + ParSpec.Bytes)
        {
            var parResult = ByteUtils.Slice(reply, dataToSkip, ParSpec.Bytes);
            // Route through the properties so INotifyPropertyChanged fires; DynamicData's
            // AutoRefresh relies on this to re-evaluate filters/visibility after a hardware read.
            SysexParameterValueInterpreter.Interpret(parResult, ParSpec, out var rawValue, out var stringValue);
            RawNumericValue = rawValue;
            StringValue = stringValue;
        }
        else
        {
            Log.Error(
                $"Sysex msg out of data while trying to parse {ParSpec.Path} from sysex reply. Are we looking at the wrong reply?");
        }
    }

    public byte[] GetSysexDataFragment()
    {
        if (IsNumeric)
        {
            var sysex = new byte[ParSpec.Bytes];
            if (ParSpec.PerNibble)
            {
                sysex = ByteUtils.IntToNibbled(_rawNumericValue, ParSpec.Bytes);
            }
            else
            {
                if (ParSpec.Bytes == 1)
                    sysex = ByteUtils.IntToBytes7_1(_rawNumericValue);
                else if (ParSpec.Bytes == 2)
                    sysex = ByteUtils.IntToBytes7_2(_rawNumericValue);
                else if (ParSpec.Bytes == 4)
                    sysex = ByteUtils.IntToBytes7_4(_rawNumericValue);
                else
                    Debug.Assert(false);
            }

            return sysex;
        }

        if (IsDiscrete) return (byte[]) [(byte)((_rawNumericValue >> 8) & 0x7f), (byte)(_rawNumericValue & 0x7f)];

        if (_stringValue.Length > ParSpec.Bytes) _stringValue = _stringValue[..ParSpec.Bytes]; // clip to max length

        return ByteUtils.PadString(Encoding.ASCII.GetBytes(_stringValue), ParSpec.Bytes);
    }

    public void CopyParsedDataFrom(FullyQualifiedParameter other)
    {
        IsNumeric = other.IsNumeric;
        // Route through the properties so INotifyPropertyChanged fires (see AutoRefresh above).
        RawNumericValue = other.RawNumericValue;
        StringValue = other.StringValue;
    }

    public void DebugLog()
    {
        var hex = new StringBuilder(ParSpec.Address.Length * 2);
        for (var i = 0; i < ParSpec.Address.Length; i++) hex.AppendFormat("{0:x2} ", ParSpec.Address[i]);
        var address = "[ " + hex + "]";
        var Wrn = "";
        if (ParSpec.Reserved) Wrn = " (reserved!)";
        var unit = "";
        if (ParSpec.Unit != "") unit = "[" + ParSpec.Unit + "]";
        if (IsNumeric)
        {
            var mapped = Mapping.linlin(RawNumericValue, ParSpec.IMin, ParSpec.IMax, ParSpec.OMin, ParSpec.OMax);
            if (!float.IsNaN(ParSpec.IMin2) && !float.IsNaN(ParSpec.IMax2) && !float.IsNaN(ParSpec.OMin2) &&
                !float.IsNaN(ParSpec.OMax2))
                mapped = Mapping.linlin(mapped, ParSpec.IMin2, ParSpec.IMax2, ParSpec.OMin2, ParSpec.OMax2);
            Log.Debug(
                $"{Wrn} parameter {ParSpec.Path} at parameter address {address} has value raw {RawNumericValue}, mapped {Math.Round(mapped, 2)}, (meaning: {StringValue}{unit})");
        }
        else if (IsDiscrete)
        {
            Log.Debug(
                $"{Wrn} parameter {ParSpec.Path} at parameter address {address} has value raw {RawNumericValue}, (meaning: {StringValue}{unit})");
        }
        else
        {
            Log.Debug($"{Wrn} parameter {ParSpec.Path} at parameter address {address} has value \"{StringValue}\"");
        }
    }
}