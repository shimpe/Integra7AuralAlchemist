using System;
using System.Diagnostics;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using OwnAudio.Midi.IO;
using ReactiveUI;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public interface IMidiIn
{
    public void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler);
    public void ConfigureDefaultHandler();

    /// <summary>Hand the port back, but only if <paramref name="handler"/> is still the one installed.
    /// A reader that finishes late must not detach a handler another reader has since installed.</summary>
    public void RemoveHandler(EventHandler<MidiReceivedEventArgs> handler);

    /// <summary>Route a message nobody requested: a data set becomes a UI update, a preset change
    /// becomes a resync, anything else is logged. Called both by the default handler and by a reader
    /// draining what arrived while it was waiting, so the two cannot diverge.</summary>
    public void DispatchUnsolicited(byte[] message);
}

public sealed class MidiIn : IMidiIn, IDisposable
{
    private readonly IMidiInputPort? _access;

    /// <summary>Who receives the next message. The port's own events stay subscribed for the life of the
    /// port and forward here, so installing a reader is a single assignment.</summary>
    private volatile EventHandler<MidiReceivedEventArgs> _lastEventHandler;
#if DEBUG
    public bool Verbose { get; set; } = true;
#else
    public bool Verbose { get; set; } = false;
#endif

    public MidiIn(string Name)
    {
        _lastEventHandler = DefaultHandler;
        IMidiInputPort? access = null;
        try
        {
            var portNames = MidiPortFactory.GetInputPortNames();
            var portName = portNames.LastOrDefault(x => x.Contains(Name));
            if (portName is null)
            {
                // Names differ per platform and per driver, so the log says what there was to choose from.
                Log.Information("No MIDI input matches '{Name}'; available: {Ports}.", Name, portNames);
                return;
            }

            // Bracketed because a driver left in a bad state can make the open never return -- which
            // looks like the application hanging with no clue why.
            Log.Information("Opening MIDI input '{Port}'.", portName);
            access = MidiPortFactory.OpenInput(portName);
            Log.Debug("Configure default midi handler");
            access.MessageReceived += OnMessageReceived;
            access.SysExReceived += OnSysExReceived;
            access.Start();
            _access = access;
            Log.Information("MIDI input opened.");
        }
        catch (Exception e)
        {
            // A missing native library as well as a port that vanished between listing and opening:
            // either way the application carries on without a device, as it does when none is found.
            Log.Error(e, "Could not open the MIDI input matching '{Name}'.", Name);
            access?.Dispose();
        }
    }

    private void OnMessageReceived(MidiMessage message) =>
        Deliver(MidiWire.FromShortMessage(message.Status, message.Data1, message.Data2));

    // The span is only valid during the callback, so it is copied before anything else sees it.
    private void OnSysExReceived(ReadOnlySpan<byte> data) => Deliver(data.ToArray());

    /// <summary>Hand a message to the current reader. This runs on the native core's thread, called
    /// straight from unmanaged code: an exception escaping here would take the whole process down, so
    /// it is logged and the message dropped instead.</summary>
    private void Deliver(byte[] message)
    {
        try
        {
            _lastEventHandler(this, new MidiReceivedEventArgs(message));
        }
        catch (Exception e)
        {
            Log.Error(e, "MIDI input handler failed on {Length} byte(s), starting {Bytes}.", message.Length,
                BitConverter.ToString(message, 0, Math.Min(message.Length, 8)));
        }
    }

    /// <summary>Close the port. WinMM opens an input for one client only, so a rescan has to close the
    /// old one before it can open the device again.</summary>
    public void Dispose()
    {
        if (_access is null)
            return;

        Log.Information("Closing MIDI input '{Port}'.", _access.Name);
        _access.Dispose();
    }

    public void ConfigureDefaultHandler()
    {
        if (_access == null)
            return;

        Log.Debug("Restore default midi handler");
        _lastEventHandler = DefaultHandler;
    }

    /// <summary>Restore the default handler on behalf of <paramref name="handler"/>. Ignored when it is
    /// not the handler currently installed: MidiPort's single lease means that should never happen, but
    /// the check stays because a reader that finishes late must still not detach a handler another
    /// reader has since installed.</summary>
    public void RemoveHandler(EventHandler<MidiReceivedEventArgs> handler)
    {
        if (_access == null)
            return;

        if (!Equals(_lastEventHandler, handler))
            return;

        ConfigureDefaultHandler();
    }

    public void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler)
    {
        if (_access == null)
        {
            Log.Information("No midi handler configured because no Integra-7 hardware found.");
            return;
        }

        // Installing over another reader is the condition that used to break the pairing, so say so.
        if (!Equals(_lastEventHandler, (EventHandler<MidiReceivedEventArgs>)DefaultHandler))
            Log.Warning("Installing a MIDI reader while another reader is still waiting for its reply.");

        Log.Debug("Configure custom midi handler");
        _lastEventHandler = handler;
    }

    private void DefaultHandler(object? sender, MidiReceivedEventArgs e)
    {
        Debug.Assert(e.Data.Length != 0);
        if (Verbose) ByteStreamDisplay.Display("Received (default handler): ", e.Data);
        DispatchUnsolicited(e.Data);
    }

    public void DispatchUnsolicited(byte[] message)
    {
        if (Integra7SysexHelpers.CheckIsDataSetMsg(message))
        {
            Log.Debug("Request UpdateSysexSpec");
            MessageBus.Current.SendMessage(new UpdateFromSysexSpec(message), "hw2ui");
        }
        else if (Integra7Api.CheckIsPartOfPresetChange(message, out var midiChannel))
        {
            Log.Debug($"Request UpdateSetPresetandResyncPart for channel {midiChannel}");
            MessageBus.Current.SendMessage(new UpdateSetPresetAndResyncPart(midiChannel));
        }
        else
        {
            Log.Debug("Received MIDI msg that will not be dispatched for ui update.");
            ByteStreamDisplay.Display("The message was: ", message);
        }
    }

}
