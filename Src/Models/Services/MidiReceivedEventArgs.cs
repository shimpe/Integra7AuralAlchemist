using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One complete message from the MIDI input: a SysEx from F0 through F7, or a short message
/// of as many bytes as its status calls for. The array belongs to the receiver; nothing reuses it.</summary>
public sealed class MidiReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}
