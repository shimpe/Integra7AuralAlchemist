using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Commons.Music.Midi;

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

    public AsyncMidiInputWrapper(IMidiIn midiIn)
    {
        _midiInput = midiIn;
        _handler = OnMidiMessageReceived;
        _midiInput.ConfigureHandler(_handler);
    }

    private void OnMidiMessageReceived(object? sender, MidiReceivedEventArgs e)
    {
        // Set the result of the TaskCompletionSource
        var localCopy = new byte[e.Length];
        Buffer.BlockCopy(e.Data, 0, localCopy, 0, e.Length);
        ByteStreamDisplay.Display($"Received {localCopy.Length} bytes (async): ", localCopy);
        //ByteStreamDisplay.Display($"Writing {localCopy.Length} bytes into channel: ", localCopy);
        //Log.Debug($"Writing {localCopy.Length} bytes into channel");
        _channel.Writer.TryWrite(localCopy);
    }

    public async Task<byte[]> WaitForMidiMessageAsync()
    {
        var cts = new CancellationTokenSource();
        var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        if (await Task.WhenAny(waitForTimeout, waitForData) == waitForData)
        {
            var message = await _channel.Reader.ReadAsync(cts.Token);
            await cts.CancelAsync();
            _midiInput.RemoveHandler(_handler);
            return message;
        }

        await cts.CancelAsync();
        _midiInput.RemoveHandler(_handler);
        return [];
    }

    public async Task<byte[]> WaitForMidiMessageAsyncExpectingMultipleInARow()
    {
        var cts = new CancellationTokenSource();
        var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        if (await Task.WhenAny(waitForTimeout, waitForData) == waitForData)
        {
            var message = await _channel.Reader.ReadAsync(cts.Token);
            await cts.CancelAsync();
            return message;
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