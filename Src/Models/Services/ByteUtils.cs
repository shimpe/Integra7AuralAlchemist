using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

public class ByteUtils
{
    // helper method to flatten sysex fragments into one long sysex byte array
    public static byte[] Flatten(params byte[][] arrays)
    {
        var rv = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var array in arrays)
        {
            Buffer.BlockCopy(array, 0, rv, offset, array.Length);
            offset += array.Length;
        }

        return rv;
    }

    public static int CountByte(byte[] array, byte value)
    {
        var count = 0;
        foreach (var b in array)
            if (b == value)
                count++;

        return count;
    }

    public static int FindByte(byte[] array, byte value)
    {
        var idx = -1;
        foreach (var b in array)
        {
            idx++;
            if (b == value)
                return idx;
        }

        return -1;
    }

    /// <summary>Split a chunk into complete sysex messages, each ending in its own F7. Bytes left over
    /// after the last terminator are an incomplete message: they get a trailing null slot, which every
    /// caller skips (and <see cref="SysexDataTransmissionParser"/> reports). A chunk that ends exactly
    /// at an F7 leaves nothing over and so gets no null slot — it used to get one regardless, which had
    /// the parser announce a truncated fragment for every well-formed message it was handed.</summary>
    public static byte[][] SplitAfterF7(byte[] reply)
    {
        var no_of_f7 = CountByte(reply, 0xf7);
        List<byte[]> result = new(no_of_f7 + 1);
        for (var i = 0; i < no_of_f7; i++)
        {
            var idx = FindByte(reply, 0xf7);
            if (idx == -1) break;
            var msg = Slice(reply, 0, idx + 1);
            result.Add(msg);
            reply = Slice(reply, msg.Length, reply.Length - idx - 1);
        }

        if (reply.Length > 0) result.Add(null!); // an unterminated tail (or a chunk with no F7 at all)
        return result.ToArray();
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

    public static byte[] IntToBytes7_1(long value)
    {
        Debug.Assert(value < 0x80);
        return [LtlEnd_FirstByte7(value)];
    }

    public static byte[] IntToBytes7_2(long value)
    {
        Debug.Assert(value < 0x8000);
        return [LtlEnd_SecondByte7(value), LtlEnd_FirstByte7(value)];
    }

    public static byte[] IntToBytes7_3(long value)
    {
        Debug.Assert(value < 0x800000);
        return [LtlEnd_ThirdByte7(value), LtlEnd_SecondByte7(value), LtlEnd_FirstByte7(value)];
    }

    public static byte[] IntToBytes7_4(long value)
    {
        Debug.Assert(value < 0x80000000);
        return
        [
            LtlEnd_FourthByte7(value), LtlEnd_ThirdByte7(value),
            LtlEnd_SecondByte7(value), LtlEnd_FirstByte7(value)
        ];
    }

    public static byte[] AddressWithOffset(byte[] startAddress, byte[] offset)
    {
        Debug.Assert(startAddress.Length >= offset.Length);
        var saddr = Bytes7ToInt(startAddress);
        var offs = Bytes7ToInt(offset);
        var sum = saddr + offs;
        var tempresult = IntToBytes7_4(sum);
        var toremove = tempresult.Length - startAddress.Length;
        if (toremove == 0) return tempresult;
        var result = new byte[startAddress.Length];
        Array.Copy(tempresult, toremove, result, 0, result.Length);
        return result;
    }

    public static byte[] AddressWithOffset(byte[] startAddress, byte[] offset, byte[] offset2, byte[] parameterAddress)
    {
        return AddressWithOffset(AddressWithOffset(AddressWithOffset(startAddress, offset), offset2), parameterAddress);
    }

    public static long Bytes7ToInt(byte[] data)
    {
        long total = 0;
        for (var i = 0; i < data.Length; i++)
        {
            total <<= 7;
            total += data[i];
        }

        return total;
    }

    public static byte CheckSum(byte[] data)
    {
        var sum = 0;
        // & 0x7f is the same as modulo 128 and (a mod N + b mod N + ... + n mod N) mod N = (a + b + ... + n) mod N
        for (var i = 0; i < data.Length; i++)
        {
            sum += data[i]; // (should should be data[i] mod 128 or data[i] & 0x7f but data[i] is 7-bit so this will always yield back data[i])
            sum &= 0x7f;
        }

        var checksum = (0x80 - sum) & 0x7f;
        return (byte)checksum;
    }

    public static byte[] IntToNibbled(long value, int noOfNibbles)
    {
        var result = new byte[noOfNibbles];
        for (var i = 0; i < noOfNibbles; i++)
        {
            var nibble = (byte)(value & 0x0f);
            result[noOfNibbles - 1 - i] = nibble;
            value >>= 4;
        }

        return result;
    }

    public static long NibbledToInt(byte[] data)
    {
        long result = 0;
        for (var i = 0; i < data.Length; i++) result += data[data.Length - 1 - i] << (i * 4);
        return result;
    }

    public static byte[] Concat(byte[] data1, byte[] data2)
    {
        var result = new byte[data1.Length + data2.Length];
        Buffer.BlockCopy(data1, 0, result, 0, data1.Length);
        Buffer.BlockCopy(data2, 0, result, data1.Length, data2.Length);
        return result;
    }

    public static byte[] Slice(byte[] data, int from, int length)
    {
        Debug.Assert(from + length <= data.Length);

        var result = new byte[length];
        Buffer.BlockCopy(data, from, result, 0, length);
        return result;
    }

    public static byte[] Zeros(int noOfZeros)
    {
        Debug.Assert(noOfZeros >= 0);
        if (noOfZeros == 0)
            return [];
        var result = new byte[noOfZeros];
        return result;
    }

    public static byte[] Pad(byte[] data, long desiredLength)
    {
        Debug.Assert(desiredLength >= data.Length);
        if (desiredLength == 0) return data;
        var result = new byte[desiredLength];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        return result;
    }

    public static byte[] PadString(byte[] data, long desiredLength)
    {
        Debug.Assert(desiredLength >= data.Length);
        if (desiredLength == 0) return data;
        var result = new byte[desiredLength];
        for (var i = 0; i < result.Length; i++) result[i] = 32; // space
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        return result;
    }
}