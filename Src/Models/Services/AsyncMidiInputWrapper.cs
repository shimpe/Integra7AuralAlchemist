using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public class AsyncMidiInputWrapper
{
    private const double inactivityTimespan = 1.5;
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private readonly IMidiIn _midiInput;

    /// <summary>This reader's handler, kept so the port can be handed back by identity. Restoring the
    /// default handler unconditionally would detach whichever reader happens to be installed, not
    /// necessarily this one.</summary>
    private readonly EventHandler<MidiReceivedEventArgs> _handler;

    /// <summary>What this conversation is waiting for.</summary>
    private readonly IReplyMatcher _expected;

    /// <summary>Messages that arrived while waiting and were not the reply. This reader owns the MIDI
    /// input for its whole duration, so it is the only thing that can see them; dropping them would
    /// lose real device traffic, such as a preset change made on the front panel.</summary>
    private readonly List<byte[]> _deferred = [];

    public AsyncMidiInputWrapper(IMidiIn midiIn, IReplyMatcher expected)
    {
        _midiInput = midiIn;
        _expected = expected;
        _handler = OnMidiMessageReceived;
        _midiInput.ConfigureHandler(_handler);
    }

    private void OnMidiMessageReceived(object? sender, MidiReceivedEventArgs e)
    {
        var localCopy = new byte[e.Length];
        Buffer.BlockCopy(e.Data, 0, localCopy, 0, e.Length);
        ByteStreamDisplay.Display($"Received {localCopy.Length} bytes (async): ", localCopy);
        _channel.Writer.TryWrite(localCopy);
    }

    /// <summary>Messages that arrived while this read was waiting and were not its reply, in arrival
    /// order. Taking them clears the list; the caller delivers them once the port is free.</summary>
    public IReadOnlyList<byte[]> TakeDeferred()
    {
        var taken = _deferred.ToArray();
        _deferred.Clear();
        return taken;
    }

    /// <summary>True when this chunk carries the reply. A single MIDI event can hold several
    /// concatenated messages, so the chunk is split and accepted whole if any fragment matches -- which
    /// is the shape the parsers downstream already expect.</summary>
    private bool IsExpected(byte[] chunk)
    {
        foreach (var fragment in ByteUtils.SplitAfterF7(chunk))
            if (fragment is not null && _expected.Matches(fragment))
                return true;

        return false;
    }

    private void Defer(byte[] message)
    {
        _deferred.Add(message);
        Log.Warning(
            "Deferring a message that is not {Expected}: {Length} byte(s), starting {Bytes}. It will be dispatched when the port is free.",
            _expected.Describe(), message.Length,
            BitConverter.ToString(message, 0, Math.Min(message.Length, 8)));
    }

    public async Task<byte[]> WaitForMidiMessageAsync()
    {
        var cts = new CancellationTokenSource();
        // The deadline runs from the request, not from the last message: traffic this read is not
        // waiting for must not be able to hold it open indefinitely.
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        while (true)
        {
            var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
            if (await Task.WhenAny(waitForTimeout, waitForData) != waitForData) break;

            var message = await _channel.Reader.ReadAsync(cts.Token);
            if (IsExpected(message))
            {
                await cts.CancelAsync();
                _midiInput.RemoveHandler(_handler);
                return message;
            }

            Defer(message);
        }

        await cts.CancelAsync();
        _midiInput.RemoveHandler(_handler);
        return [];
    }

    public async Task<byte[]> WaitForMidiMessageAsyncExpectingMultipleInARow()
    {
        var cts = new CancellationTokenSource();
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        while (true)
        {
            var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
            if (await Task.WhenAny(waitForTimeout, waitForData) != waitForData) break;

            var message = await _channel.Reader.ReadAsync(cts.Token);
            if (IsExpected(message))
            {
                // Deliberately does NOT hand the port back: the burst reader stays installed across
                // every reply until its caller decides the burst has ended.
                await cts.CancelAsync();
                return message;
            }

            Defer(message);
        }

        await cts.CancelAsync();
        return [];
    }

    public void CleanupAfterTimeOut()
    {
        _midiInput.RemoveHandler(_handler);
        _midiInput.RestoreAutomaticHandling();
        _channel.Writer.TryComplete();
    }
}
