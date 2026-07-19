using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a reader is waiting for. A conversation supplies one so a read can tell its own reply
/// from anything else the device happens to send while it is waiting -- a preset change made on the
/// front panel, most commonly. Without this a read returns whatever arrives first and parses it as the
/// address range it asked for.</summary>
public interface IReplyMatcher
{
    bool Matches(byte[] message);

    /// <summary>Logged when a message is deferred, so a matcher that is too strict shows up as traffic
    /// that never matched rather than as parameters that silently stopped updating.</summary>
    string Describe();
}

/// <summary>The matchers the four device conversations use. Pure: no MIDI, no tasks.</summary>
public static class ReplyMatchers
{
    private const int CommandIndex = 6;
    private const int DataSetCommand = 0x12;
    private const int AddressIndex = 7;
    private const int AddressLength = 4;

    /// <summary>A Roland data set (DT1) carrying <paramref name="address"/> -- the answer to a data
    /// request for it. Byte 2 is the device ID and is deliberately not matched: it varies with how the
    /// unit is configured.</summary>
    public static IReplyMatcher DataSetAt(byte[] address)
    {
        // Checked here rather than inside Matches: a wrong-sized address is a programmer error, and
        // this way it surfaces at the call site instead of as an IndexOutOfRangeException raised on
        // the MIDI callback thread, or -- worse -- as a matcher that quietly matches nothing and
        // leaves every read to time out.
        if (address.Length != AddressLength)
            throw new ArgumentException($"An address is {AddressLength} bytes, got {address.Length}.",
                nameof(address));

        return new DataSetMatcher(address);
    }

    /// <summary>The universal non-realtime identity reply, f0 7e &lt;dev&gt; 06 02 ...</summary>
    public static IReplyMatcher IdentityReply { get; } = new IdentityMatcher();

    /// <summary>A reply belonging to a name-list burst at <paramref name="address"/>. Delegates to the
    /// structural match the burst reader already uses.</summary>
    public static IReplyMatcher NameListReply(byte[] address) => new NameListMatcher(address);

    private sealed class DataSetMatcher(byte[] address) : IReplyMatcher
    {
        public bool Matches(byte[] message)
        {
            if (message.Length < AddressIndex + AddressLength) return false;
            if (message[0] != 0xf0 || message[1] != 0x41) return false;
            if (message[CommandIndex] != DataSetCommand) return false;

            for (var i = 0; i < AddressLength; i++)
                if (message[AddressIndex + i] != address[i])
                    return false;

            return true;
        }

        public string Describe() => $"a data set at {BitConverter.ToString(address)}";
    }

    private sealed class IdentityMatcher : IReplyMatcher
    {
        public bool Matches(byte[] message) =>
            message.Length >= 5 && message[0] == 0xf0 && message[1] == 0x7e &&
            message[3] == 0x06 && message[4] == 0x02;

        public string Describe() => "an identity reply";
    }

    private sealed class NameListMatcher(byte[] address) : IReplyMatcher
    {
        public bool Matches(byte[] message) => NameListEndMarker.IsNameListReply(message, address);

        public string Describe() => $"a name-list reply at {BitConverter.ToString(address)}";
    }
}
