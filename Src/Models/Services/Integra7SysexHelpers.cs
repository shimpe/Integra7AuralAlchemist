using System;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public class Integra7SysexHelpers
{
    public enum SrxIdForLoad
    {
        Off = 0,
        Srx01 = 1,
        Srx02 = 2,
        Srx03 = 3,
        Srx04 = 4,
        Srx05 = 5,
        Srx06 = 6,
        Srx07 = 7,
        Srx08 = 8,
        Srx09 = 9,
        Srx10 = 10,
        Srx11 = 11,
        Srx12 = 12,
        ExSN1 = 13,
        ExSN2 = 14,
        ExSN3 = 15,
        ExSN4 = 16,
        ExSn5 = 17,
        ExSN6 = 18,
        HQPcm = 19
    }

    private static readonly byte[] EXCLUSIVE_STATUS = [0xF0];
    private static readonly byte[] UNIVERSAL_NON_RT = [0x7e];
    private static readonly byte[] SYSEX_GLOBAL_CH = [0x7f];
    private static readonly byte[] IDENTITY_GEN_INFO = [0x06];
    private static readonly byte[] IDENTITY_ID_REQ = [0x01];
    private static readonly byte[] IDENTITY_ID_REP = [0x02];
    private static readonly byte[] ROLAND_ID = [0x41];
    private static readonly byte[] ROLAND_DEVICE_FAMILY_CODE = [0x64, 0x02];
    private static readonly byte[] ROLAND_DEVICE_FAMILY_NUMBER_CODE = [0x00, 0x00];
    private static readonly byte[] ROLAND_DEVICE_FAMILY_SW_REV = [0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] MODEL_ID = [0x00, 0x00, 0x64];
    private static readonly byte[] END_OF_SYSEX = [0xF7];
    private static readonly byte[] DEVICE_ID = [0x10];
    private static readonly byte[] COMMAND_DATAREQ = [0x11];
    private static readonly byte[] COMMAND_DATASET = [0x12];

    public static byte[] IDENTITY_REQUEST = ByteUtils.Flatten(EXCLUSIVE_STATUS, UNIVERSAL_NON_RT, SYSEX_GLOBAL_CH,
        IDENTITY_GEN_INFO, IDENTITY_ID_REQ, END_OF_SYSEX);

    public static byte[] IDENTITY_REPLY = ByteUtils.Flatten(EXCLUSIVE_STATUS, UNIVERSAL_NON_RT, DEVICE_ID,
        IDENTITY_GEN_INFO, IDENTITY_ID_REP, ROLAND_ID, ROLAND_DEVICE_FAMILY_CODE, ROLAND_DEVICE_FAMILY_NUMBER_CODE,
        ROLAND_DEVICE_FAMILY_SW_REV, END_OF_SYSEX);


    public static byte[] MakeStopPreviewPhraseMsg(byte deviceId)
    {
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATASET,
            [0x0f, 00, 0x20, 00, 0x0, 0x51], END_OF_SYSEX);
    }

    public static byte[] MakePlayPreviewPhraseMsg(byte channel, byte deviceId)
    {
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATASET,
            [0x0f, 00, 0x20, 00, (byte)(channel + 1), (byte)(0x50 - channel)], END_OF_SYSEX);
    }

    public static byte[] MakeLoadSrxMsg(byte slot1, byte slot2, byte slot3, byte slot4, byte deviceId)
    {
        byte[] payload = [0x0F, 0x00, 0x30, 0x00, slot1, slot2, slot3, slot4];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeAskLoadedSrxMsg(byte deviceId)
    {
        byte[] payload = [0x0F, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWriteStudioSetMsg(byte deviceId, int studioSetId)
    {
        var payload =
            ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x55], ByteUtils.IntToBytes7_2(studioSetId), [0x7f]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWritePCMDrumKitMsg(byte deviceId, byte zeroBasedPartNo, int zeroBasedUserMemoryId)
    {
        var payload = ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x56], ByteUtils.IntToBytes7_2(zeroBasedUserMemoryId),
            [zeroBasedPartNo]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWritePCMSynthToneMsg(byte deviceId, byte zeroBasedPartNo, int zeroBasedUserMemoryId)
    {
        var payload = ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x57], ByteUtils.IntToBytes7_2(zeroBasedUserMemoryId),
            [zeroBasedPartNo]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWriteSuperNATURALDrumKitMsg(byte deviceId, byte zeroBasedPartNo, int zeroBasedUserMemoryId)
    {
        var payload = ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x58], ByteUtils.IntToBytes7_2(zeroBasedUserMemoryId),
            [zeroBasedPartNo]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWriteSuperNATURALAcousticToneMsg(byte deviceId, byte zeroBasedPartNo,
        int zeroBasedUserMemoryId)
    {
        var payload = ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x59], ByteUtils.IntToBytes7_2(zeroBasedUserMemoryId),
            [zeroBasedPartNo]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeWriteSuperNATURALSynthToneMsg(byte deviceId, byte zeroBasedPartNo,
        int zeroBasedUserMemoryId)
    {
        var payload = ByteUtils.Flatten([0x0F, 0x00, 0x10, 0x00, 0x5F], ByteUtils.IntToBytes7_2(zeroBasedUserMemoryId),
            [zeroBasedPartNo]);
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestStudioSetNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x03, 0x02, 0x55, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMDrumKitUserNames0to31Msg(byte deviceId)
    {
        byte noOfNames = 0x20;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x56, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMDrumKitPresetNames0to14Msg(byte deviceId)
    {
        byte noOfNames = 0x0e;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x56, 0x40, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMToneUserNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMToneUserNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x00, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMToneUserNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x01, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMToneUserNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x01, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x40, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x40, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x41, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x41, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames256to319Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x42, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames320to383Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x42, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames384to447Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x43, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames448to511Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x43, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames512to575Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x44, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames576to639Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x44, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames640to703Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x45, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames704to767Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x45, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames768to831Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x46, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetNames832to895Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x57, 0x46, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALDrumKitUserNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x58, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALDrumKitPresetNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x58, 0x40, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticToneUserNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticToneUserNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x00, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticToneUserNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x01, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticToneUserNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x01, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x40, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x40, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x41, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x41, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN1_0to17Msg(byte deviceId)
    {
        byte noOfNames = 0x11;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x60, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN2_0to17Msg(byte deviceId)
    {
        byte noOfNames = 0x11;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x61, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN3_0to50Msg(byte deviceId)
    {
        byte noOfNames = 0x32;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x62, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN4_0to12Msg(byte deviceId)
    {
        byte noOfNames = 0x0c;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x63, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN5_0to12Msg(byte deviceId)
    {
        byte noOfNames = 0x0c;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x64, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALAcousticTonePresetNamesExSN6_0to7Msg(byte deviceId)
    {
        byte noOfNames = 0x07;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x59, 0x65, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x00, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x00, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x01, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x01, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames256to319Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x02, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames320to383Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x02, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames384to447Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x03, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthToneUserNames448to511Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x03, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames0to63Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x40, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames64to127Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x40, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames128to191Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x41, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames192to255Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x41, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames256to319Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x42, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames320to383Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x42, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames384to447Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x43, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames448to511Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x43, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames512to575Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x44, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames576to639Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x44, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames640to703Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x45, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames704to767Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x45, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames768to831Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x46, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames832to895Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x46, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames896to959Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x47, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames960to1023Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x47, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames1024to1087Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x48, 0x00, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestSuperNATURALSynthTonePresetNames1088to1108Msg(byte deviceId)
    {
        byte noOfNames = 0x40;
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x5f, 0x48, 0x40, noOfNames];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMDrumKitPresetGM2Names0to32Msg(byte deviceId)
    {
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x78, 0x00, 0x00, 0x09];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetGM2Names0to127Msg(byte deviceId)
    {
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x79, 0x00, 0x00, 0x7F];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static byte[] MakeRequestPCMTonePresetGM2Names128to252Msg(byte deviceId)
    {
        byte[] payload = [0x0F, 0x00, 0x04, 0x02, 0x79, 0x01, 0x3b, 0x7F];
        return ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ,
            payload, [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
    }

    public static bool CheckIdentityReply(byte[] reply, out byte deviceId)
    {
        // Guard the trimmed message, not the raw one: TrimAfterEndOfSysex yields nothing at all when
        // the terminator is missing, so a reply long enough to check can still be too short to index.
        var trimmedReply = TrimAfterEndOfSysex(reply);
        if (trimmedReply.Length > 2)
        {
            deviceId = trimmedReply[2];
            IDENTITY_REPLY[2] = deviceId;
            if (!trimmedReply.SequenceEqual(IDENTITY_REPLY))
            {
                Debug.WriteLine("Identity check failed.");
                ByteStreamDisplay.Display("Expected: ", IDENTITY_REPLY);
                ByteStreamDisplay.Display("Actual: ", trimmedReply);
                return false;
            }

            return true;
        }

        deviceId = 0;
        return false;
    }

    /// <summary>Logs a Warning identifying a malformed inbound message by its length and leading bytes.
    /// Messages from hardware are not trustworthy input, so every guard below that rejects one logs
    /// through here rather than letting the caller crash or hang -- see FullyQualifiedParameter.
    /// ParseFromSysexReply for the existing precedent of logging instead of crashing on a bad length.</summary>
    private static void LogMalformedMessage(string reason, byte[] reply)
    {
        var previewLength = Math.Min(reply.Length, 8);
        Log.Warning("{Reason}. Length: {Length}. First bytes: {Bytes}", reason, reply.Length,
            BitConverter.ToString(reply, 0, previewLength));
    }

    public static bool CheckIsDataSetMsg(byte[] reply)
    {
        // device id will be ignored
        var expectedHeader = ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [0x10], MODEL_ID, COMMAND_DATASET);
        var len = expectedHeader.Length;
        if (reply.Length < len)
        {
            // Hardware sends more than data-set replies on this same handler -- program changes,
            // active sensing, short panel-change notifications. Anything shorter than the header
            // simply cannot be a data-set message.
            LogMalformedMessage("Message too short to hold a data-set header", reply);
            return false;
        }

        var header = ByteUtils.Slice(reply, 0, len);
        return header[0] == EXCLUSIVE_STATUS[0] && header[1] == ROLAND_ID[0] && header[3..6].SequenceEqual(MODEL_ID) &&
               header[6] == COMMAND_DATASET[0];
    }

    public static byte[] ExtractPayload(byte[] reply)
    {
        // device id will be ignored
        var expectedHeader = ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [0x10], MODEL_ID, COMMAND_DATASET);
        var len = expectedHeader.Length;
        var trimIdx = Array.IndexOf(reply, END_OF_SYSEX[0]);
        if (trimIdx == -1)
        {
            LogMalformedMessage("Sysex reply has no end-of-sysex (F7) marker; discarding payload", reply);
            return [];
        }

        var trimmedSysexReply = ByteUtils.Slice(reply, 0, trimIdx); // this already removes the END_OF_SYSEX byte
        if (trimmedSysexReply.Length < len + 1) // header plus at least a checksum byte
        {
            LogMalformedMessage("Sysex reply too short to hold header and checksum; discarding payload", reply);
            return [];
        }

        var payload =
            ByteUtils.Slice(trimmedSysexReply, len, trimmedSysexReply.Length - len - 1); // -1 to remove the checksum
        return payload;
    }

    public static byte[] TrimAfterEndOfSysex(byte[] reply)
    {
        var trimIdx = Array.IndexOf(reply, END_OF_SYSEX[0]);
        if (trimIdx == -1)
        {
            LogMalformedMessage("Sysex reply has no end-of-sysex (F7) marker; treating as empty", reply);
            return [];
        }

        return ByteUtils.Slice(reply, 0, trimIdx + 1);
    }

    public static byte[] MakeDataRequest(byte deviceId, byte[] address, long size)
    {
        var payload = ByteUtils.Flatten(address, ByteUtils.IntToBytes7_4(size));
        var data = ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATAREQ, payload,
            [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
        return data;
    }

    public static byte[] MakeDataSet(byte deviceId, byte[] address, byte[] data)
    {
        var payload = ByteUtils.Flatten(address, data);
        var msg = ByteUtils.Flatten(EXCLUSIVE_STATUS, ROLAND_ID, [deviceId], MODEL_ID, COMMAND_DATASET, payload,
            [ByteUtils.CheckSum(payload)], END_OF_SYSEX);
        return msg;
    }
}