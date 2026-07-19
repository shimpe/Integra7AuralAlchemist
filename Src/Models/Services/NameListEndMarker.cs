namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Recognises replies belonging to a burst of name-list replies, and the sentinel reply
/// that closes one.
///
/// A name-list request is answered by one reply per name, followed by a
/// final reply whose payload is entirely zero — an empty name. <see cref="IsNameListReply"/> matches
/// the shared shape of both; <see cref="IsEndOfBurst"/> additionally requires the all-zero payload,
/// so it recognises only the terminator. The reader that owns the MIDI input for the duration of a
/// burst sees every message the device sends, including unsolicited ones (e.g. a sysex emitted when
/// the user changes presets on the device's own front panel); <see cref="IsNameListReply"/> is what
/// tells those apart from an actual reply, so a caller can drop them instead of misreading them as a
/// name too short to contain one.
///
/// The match is structural rather than a comparison against the literal 34 bytes, so it survives a
/// device configured with a different device ID (byte 2).</summary>
public static class NameListEndMarker
{
    // f0 41 <dev> 00 00 64 12 | 0f 00 04 02 | 21 payload bytes | checksum | f7
    private const int MessageLength = 34;
    private const int CommandIndex = 6;
    private const int DataSetCommand = 0x12;
    private const int AddressIndex = 7;
    private const int AddressLength = 4;
    private const int PayloadIndex = 11;
    private const int PayloadLength = 21;

    /// <summary>The address most name-list requests use. Studio Set names use 0f 00 03 02 instead, so
    /// the address is a parameter rather than a constant: hardcoding this one would reject every reply
    /// to that request, turning a missing name list into a silent empty one.</summary>
    public static readonly byte[] ToneNameListAddress = [0x0f, 0x00, 0x04, 0x02];

    /// <summary>The four address bytes a name-list request asks for, which its replies carry back.
    /// Both request and reply hold the address at the same offset.</summary>
    public static byte[] AddressOf(byte[] request) =>
        request.Length < AddressIndex + AddressLength
            ? []
            : request[AddressIndex..(AddressIndex + AddressLength)];

    /// <summary>True when <paramref name="message"/> has the shape of a reply to a name-list request
    /// for <paramref name="expectedAddress"/>: the right length, the Roland sysex header, the DT1
    /// (data set) command, and that address. This does not distinguish an actual name from the
    /// empty-name terminator — see <see cref="IsEndOfBurst"/> for that.</summary>
    /// <summary>As above, for the address the tone name lists use.</summary>
    public static bool IsNameListReply(byte[]? message) => IsNameListReply(message, ToneNameListAddress);

    public static bool IsNameListReply(byte[]? message, byte[] expectedAddress)
    {
        if (message is null || message.Length != MessageLength) return false;
        if (expectedAddress.Length != AddressLength) return false;
        if (message[0] != 0xf0 || message[1] != 0x41 || message[^1] != 0xf7) return false;
        if (message[CommandIndex] != DataSetCommand) return false;

        for (var i = 0; i < AddressLength; i++)
            if (message[AddressIndex + i] != expectedAddress[i])
                return false;

        return true;
    }

    /// <summary>True when <paramref name="message"/> is the final, empty-name reply of a burst
    /// answering a request for <paramref name="expectedAddress"/>.</summary>
    /// <summary>As above, for the address the tone name lists use.</summary>
    public static bool IsEndOfBurst(byte[]? message) => IsEndOfBurst(message, ToneNameListAddress);

    public static bool IsEndOfBurst(byte[]? message, byte[] expectedAddress)
    {
        if (!IsNameListReply(message, expectedAddress)) return false;

        // An all-zero payload is what makes this reply the terminator rather than a name.
        for (var i = PayloadIndex; i < PayloadIndex + PayloadLength; i++)
            if (message![i] != 0x00)
                return false;

        return true;
    }
}
