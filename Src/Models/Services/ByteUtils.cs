using System;
using System.Diagnostics;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;
public class ByteUtils
{
    // helper method to flatten sysex fragments into one long sysex byte array
    public static byte[] Flatten(params byte[][] arrays)
    {
        byte[] rv = new byte[arrays.Sum(a => a.Length)];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            System.Buffer.BlockCopy(array, 0, rv, offset, array.Length);
            offset += array.Length;
        }
        return rv;
    }

    public static byte LtlEnd_FirstByte7(long value)
    {
        Debug.Assert(value < 0x80000000);
        return (byte)(value & 0x7f);
    }

    public static byte LtlEnd_SecondByte7(long value)
    {
        Debug.Assert(value < 0x80000000);
        return (byte)((value >> 7) & 0x7f);
    }

    public static byte LtlEnd_ThirdByte7(long value)
    {
        Debug.Assert(value < 0x80000000);
        return (byte)((value >> 14) & 0x7f);
    }

    public static byte LtlEnd_FourthByte7(long value)
    {
        Debug.Assert(value < 0x80000000);
        return (byte)((value >> 21) & 0x7f);
    }

    public static byte[] IntToBytes7(long value)
    {
        Debug.Assert(value < 0x80000000);
        return [LtlEnd_FourthByte7(value), LtlEnd_ThirdByte7(value),
                LtlEnd_SecondByte7(value), LtlEnd_FirstByte7(value)];
    }

    public static byte[] AddressWithOffset(byte[] StartAddress, byte[] Offset)
    {
        byte[] result = new byte[StartAddress.Length];
        Array.Copy(StartAddress, result, StartAddress.Length);
        Debug.Assert(StartAddress.Length >= Offset.Length);
        for (int i = 0; i < Offset.Length; i++)
        {
            result[StartAddress.Length - 1 - i] = (byte)(result[StartAddress.Length - 1 - i] + Offset[Offset.Length - 1 - i]);
        }
        return result;
    }

    public static long Bytes7ToInt(byte[] data)
    {
        Debug.Assert(data.Length == 4);
        return ((((((long)data[0] << 7) + (long)data[1]) << 7) + (long)data[2]) << 7) + (long)data[3];
    }

    public static byte CheckSum(byte[] data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
            sum &= 0xff;
        }
        int remainder = (sum % 128);
        int checksum = 128 - remainder;
        return (byte)checksum;
    }
}