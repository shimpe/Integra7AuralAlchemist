using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The byte shapes MIDI messages take on the wire. The rest of the application speaks in raw
/// byte arrays, while OwnAudio.Midi hands out a short message as a status plus two data bytes and
/// wants SysEx sent separately from everything else; this is the translation between the two.</summary>
public static class MidiWire
{
    /// <summary>How many bytes a message starting with <paramref name="status"/> occupies, status byte
    /// included. Zero for a data byte, which cannot start a message.</summary>
    public static int ShortMessageLength(byte status) => status switch
    {
        < 0x80 => 0,
        < 0xC0 => 3, // note off, note on, poly aftertouch, control change
        < 0xE0 => 2, // program change, channel pressure
        < 0xF0 => 3, // pitch bend
        0xF1 or 0xF3 => 2, // MTC quarter frame, song select
        0xF2 => 3, // song position
        _ => 1 // tune request, real-time messages, and the undefined ones
    };

    /// <summary>A received short message as the bytes it was on the wire. The input port always reports
    /// two data bytes; only as many as the status calls for are kept, so a program change comes out as
    /// the two bytes the parsers expect rather than three.</summary>
    public static byte[] FromShortMessage(byte status, byte data1, byte data2) =>
        ShortMessageLength(status) switch
        {
            3 => [status, data1, data2],
            2 => [status, data1],
            _ => [status]
        };

    /// <summary>Split an outgoing byte array into the messages it holds: each SysEx from its F0 through
    /// its F7, and each short message by the length its status implies. A SysEx with no terminator
    /// runs to the end of the array.</summary>
    /// <exception cref="FormatException">A data byte where a message should start: the array is not
    /// a sequence of whole messages (running status is not used here).</exception>
    public static List<ArraySegment<byte>> SplitMessages(byte[] data)
    {
        List<ArraySegment<byte>> messages = [];
        var i = 0;
        while (i < data.Length)
        {
            int length;
            if (data[i] == 0xF0)
            {
                var end = Array.IndexOf(data, (byte)0xF7, i);
                length = (end < 0 ? data.Length : end + 1) - i;
            }
            else
            {
                length = ShortMessageLength(data[i]);
                if (length == 0)
                    throw new FormatException($"Data byte 0x{data[i]:x2} at offset {i} where a MIDI message should start.");
                length = Math.Min(length, data.Length - i);
            }

            messages.Add(new ArraySegment<byte>(data, i, length));
            i += length;
        }

        return messages;
    }
}
