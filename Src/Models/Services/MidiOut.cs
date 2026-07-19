using System;
using System.Linq;
using Commons.Music.Midi;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public interface IMidiOut
{
    public bool ConnectionOk();
    public void SafeSend(byte[] data);
}

public class MidiOut : IMidiOut
{
    private readonly IMidiAccess? _midiAccessManager;
    private IMidiOutput? _access;
    private IMidiPortDetails? _midiPortDetails;
#if DEBUG
    public bool Verbose { get; set; } = true;
#else
    public bool Verbose { get; set; } = false;
#endif
    public MidiOut(string Name)
    {
        _midiAccessManager = MidiAccessManager.Default;
        try
        {
            var outputs = _midiAccessManager?.Outputs.Where(x => x.Name.Contains(Name));
            if (!outputs.Any())
                _midiPortDetails = null;
            else
                _midiPortDetails = outputs.Last();
        }
        catch (InvalidOperationException)
        {
            _midiPortDetails = null;
        }
    }

    public bool ConnectionOk()
    {
        return _midiPortDetails != null;
    }

    public void SafeSend(byte[] data)
    {
        try
        {
            if (_access is null)
            {
                if (_midiPortDetails is null)
                {
                    Log.Error("No MIDI message sent because no Integra-7 hardware found.");
                    return;
                }

                _access = _midiAccessManager?.OpenOutputAsync(_midiPortDetails?.Id).Result;
            }

            _access?.Send(data, 0, data.Length, 0);
            if (Verbose)
            {
                if (_access is null) Log.Debug("_access is null... cannot complete the following: ");

                ByteStreamDisplay.Display("Sent: ", data);
            }
        }
        catch (ArgumentException e)
        {
            // These two say the handle itself is bad, so the port really is gone.
            LogSendFailure(e, data, "the port is gone");
            _midiPortDetails = null;
            _access = null;
        }
        catch (NullReferenceException e)
        {
            LogSendFailure(e, data, "the port is gone");
            _midiPortDetails = null;
            _access = null;
        }
        catch (Exception e)
        {
            // Anything else -- notably the Win32Exception the WinMM backend throws -- is about THIS
            // message, not the port. A malformed message must not condemn the device for the rest of
            // the session, which is what dropping the port here did. The handle is dropped so the next
            // send reopens, but the port details stay, so ConnectionOk still reports a device.
            LogSendFailure(e, data, "keeping the port and reopening on the next send");
            _access = null;
        }
    }

    private static void LogSendFailure(Exception e, byte[] data, string outcome)
    {
        Log.Error(e, "MIDI send failed ({Length} bytes, starting {Bytes}); {Outcome}.",
            data.Length, BitConverter.ToString(data, 0, Math.Min(data.Length, 8)), outcome);
    }
}