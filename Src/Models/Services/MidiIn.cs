using System;
using System.Diagnostics;
using System.Linq;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Data;
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

public class MidiIn : IMidiIn
{
    private readonly IMidiAccess? _midiAccessManager;
    private readonly IMidiInput? _access;
    private readonly IMidiPortDetails? _midiPortDetails;

    private event EventHandler<MidiReceivedEventArgs> _lastEventHandler;
#if DEBUG
    public bool Verbose { get; set; } = true;
#else
    public bool Verbose { get; set; } = false;
#endif

    public MidiIn(string Name)
    {
        _midiAccessManager = MidiAccessManager.Default;
        _lastEventHandler = DefaultHandler;
        try
        {
            var inputs = _midiAccessManager?.Inputs.Where(x => x.Name.Contains(Name));
            if (!inputs.Any())
            {
                _midiPortDetails = null;
                _access = null;
            }
            else
            {
                _midiPortDetails = inputs.Last();
                // Bracketed because this blocks on an async open, and a driver left in a bad state can
                // make it never return -- which looks like the application hanging with no clue why.
                Log.Information("Opening MIDI input '{Port}'.", _midiPortDetails?.Name);
                _access = _midiAccessManager?.OpenInputAsync(_midiPortDetails?.Id).Result;
                Log.Information("MIDI input opened.");
            }

            if (_access != null)
            {
                Log.Debug("Configure default midi handler");
                _access.MessageReceived += _lastEventHandler;
            }
        }
        catch (InvalidOperationException)
        {
            _midiPortDetails = null;
        }
    }

    public void ConfigureDefaultHandler()
    {
        if (_access == null)
            return;

        Log.Debug("Remove customized midi handler");
        _access.MessageReceived -= _lastEventHandler;
        _lastEventHandler = DefaultHandler;
        Log.Debug("Restore default midi handler");
        _access.MessageReceived += _lastEventHandler;
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

        Log.Debug("Remove last configured midi handler");
        _access.MessageReceived -= _lastEventHandler;
        _lastEventHandler = handler;
        Log.Debug("Configure custom midi handler");
        _access.MessageReceived += _lastEventHandler;
    }

    private void DefaultHandler(object? sender, MidiReceivedEventArgs e)
    {
        var localCopy = new byte[e.Length];
        Debug.Assert(e.Length != 0);
        Array.Copy(e.Data, localCopy, e.Length);
        if (Verbose) ByteStreamDisplay.Display("Received (default handler): ", localCopy);
        DispatchUnsolicited(localCopy);
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