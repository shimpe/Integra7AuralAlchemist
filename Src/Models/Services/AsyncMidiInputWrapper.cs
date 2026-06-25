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

    public AsyncMidiInputWrapper(IMidiIn midiIn)
    {
        _midiInput = midiIn;
        _midiInput.ConfigureHandler(OnMidiMessageReceived);
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
            _midiInput.ConfigureDefaultHandler();
            return message;
        }

        await cts.CancelAsync();
        _midiInput.ConfigureDefaultHandler();
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
        _midiInput.ConfigureDefaultHandler();
        _midiInput.RestoreAutomaticHandling();
        _channel.Writer.TryComplete();
    }
}