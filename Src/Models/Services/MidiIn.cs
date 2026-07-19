using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    public byte[] GetReply();
    public void AnnounceIntentionToManuallyHandleReply();
    public void RestoreAutomaticHandling();

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
    private bool _manualReplyHandling;

    public bool ManualReplyHandling
    {
        get => _manualReplyHandling;
        set
        {
            Log.Debug($"Set manual MIDI reply handling to {value}.");
            _manualReplyHandling = value;
        }
    }

    private event EventHandler<MidiReceivedEventArgs> _lastEventHandler;
    private static readonly ManualResetEvent _replyReady = new(false);
    private byte[] _replyData = [];
#if DEBUG
    public bool Verbose { get; set; } = true;
#else
    public bool Verbose { get; set; } = false;
#endif

    public MidiIn(string Name)
    {
        _midiAccessManager = MidiAccessManager.Default;
        _lastEventHandler = DefaultHandler;
        _manualReplyHandling = false;
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
                _access = _midiAccessManager?.OpenInputAsync(_midiPortDetails?.Id).Result;
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
        _manualReplyHandling = false;
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
        _manualReplyHandling = false;
    }

    private void DefaultHandler(object? sender, MidiReceivedEventArgs e)
    {
        _replyReady.Reset();
        _replyData = new byte[e.Length];
        var localCopy = new byte[e.Length];
        Debug.Assert(e.Length != 0);
        Array.Copy(e.Data, localCopy, e.Length);
        Array.Copy(localCopy, _replyData, e.Length);
        if (Verbose) ByteStreamDisplay.Display("Received (default handler): ", localCopy);
        if (!_manualReplyHandling)
        {
            DispatchUnsolicited(localCopy);
        }
        else
        {
            Log.Debug(
                "Received MIDI msg that will not be dispatched for ui update because manual reply handling is active.");
        }

        _manualReplyHandling = false;
        _replyReady.Set();
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

    public byte[] GetReply()
    {
        if (_replyReady.WaitOne(500))
        {
            _replyReady.Reset();
            return _replyData;
        }

        // if no reply after 5 seconds, most likely no reply will come anymore...
        _replyReady.Reset();
        _manualReplyHandling = false;
        return [];
    }

    public void AnnounceIntentionToManuallyHandleReply()
    {
        _replyReady.Reset();
        _manualReplyHandling = true;
    }

    public void RestoreAutomaticHandling()
    {
        _replyReady.Set();
        _manualReplyHandling = false;
    }
}