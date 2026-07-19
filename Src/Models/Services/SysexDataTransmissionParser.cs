using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using Serilog;

public class SysexDataTransmissionParser
{
    public static List<UpdateMessageSpec> ConvertSysexToParameterUpdates(byte[] sysexMsg, Integra7Domain? i7)
    {
        List<UpdateMessageSpec> result = [];
        byte[][] sysexMsgList = ByteUtils.SplitAfterF7(sysexMsg);
        foreach (var s in sysexMsgList)
            if (s != null)
                if (Integra7SysexHelpers.CheckIsDataSetMsg(s))
                {
                    var payload = Integra7SysexHelpers.ExtractPayload(s);

                    if (payload.Length < 4)
                    {
                        // Too short to even hold an address. ExtractPayload already logged why the
                        // payload looks the way it does; this is just the "and so we can't parse it"
                        // consequence, so a note (not a warning) is enough here.
                        Log.Debug(
                            "Data-set payload too short to hold an address ({Length} byte(s)); nothing to parse.",
                            payload.Length);
                        continue;
                    }

                    var currentLocation = 0;
                    var address = ByteUtils.Slice(payload, currentLocation, 4);
                    currentLocation += 4; // skip address
                    while (currentLocation < payload.Length)
                    {
                        var p = i7?.LookupAddress(address);
                        if (p is null)
                        {
                            // An address this build does not know. Looping again would not advance
                            // (address and currentLocation would stay put), so stop here rather than
                            // hang -- keep whatever parameters were already understood.
                            Log.Warning(
                                "Unknown parameter address in data-set payload at offset {Offset}; stopping with {Count} parameter(s) understood so far.",
                                currentLocation, result.Count);
                            break;
                        }

                        var bytes = p.ParSpec.Bytes;
                        if (currentLocation + bytes > payload.Length)
                        {
                            // A truncated message declaring more parameter bytes than it actually
                            // carries. Slicing would read past the end of payload.
                            Log.Warning(
                                "Data-set payload truncated: parameter {Path} needs {Needed} byte(s) but only {Available} remain; stopping with {Count} parameter(s) understood so far.",
                                p.ParSpec.Path, bytes, payload.Length - currentLocation, result.Count);
                            break;
                        }

                        var parResult = ByteUtils.Slice(payload, currentLocation, bytes);
                        SysexParameterValueInterpreter.Interpret(parResult, p.ParSpec, out var rawVal,
                            out var displayValue);
                        result.Add(new UpdateMessageSpec(p, displayValue));

                        currentLocation += bytes; // skip parameter data
                        address = ByteUtils.IntToBytes7_4(ByteUtils.Bytes7ToInt(address) + bytes);
                    }
                }

        return result;
    }
}