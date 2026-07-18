namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Recognises the sentinel reply that closes a burst of name-list replies.
///
/// Every name-list request (address 0f 00 04 02) is answered by one reply per name, followed by a
/// final reply whose payload is entirely zero — an empty name. Spotting it lets a reader stop
/// immediately instead of sitting out the inactivity timeout, which is the only other way to learn
/// that the burst has ended.
///
/// The match is structural rather than a comparison against the literal 34 bytes, so it survives a
/// device configured with a different device ID (byte 2).</summary>
public static class NameListEndMarker
{
    // f0 41 <dev> 00 00 64 12 | 0f 00 04 02 | 21 zero payload bytes | checksum | f7
    private const int MessageLength = 34;
    private const int CommandIndex = 6;
    private const int DataSetCommand = 0x12;
    private const int AddressIndex = 7;
    private const int PayloadIndex = 11;
    private const int PayloadLength = 21;

    private static readonly byte[] NameListAddress = [0x0f, 0x00, 0x04, 0x02];

    /// <summary>True when <paramref name="message"/> is the final, empty-name reply of a name-list burst.</summary>
    public static bool IsEndOfBurst(byte[]? message)
    {
        if (message is null || message.Length != MessageLength) return false;
        if (message[0] != 0xf0 || message[1] != 0x41 || message[^1] != 0xf7) return false;
        if (message[CommandIndex] != DataSetCommand) return false;

        for (var i = 0; i < NameListAddress.Length; i++)
            if (message[AddressIndex + i] != NameListAddress[i])
                return false;

        // An all-zero payload is what makes this reply the terminator rather than a name.
        for (var i = PayloadIndex; i < PayloadIndex + PayloadLength; i++)
            if (message[i] != 0x00)
                return false;

        return true;
    }
}
