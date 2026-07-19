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
        catch (Exception e)
        {
            // Every failure, not just the two managed ones that used to be listed here: the WinMM
            // backend throws Win32Exception, which escaped and took startup down with it. A send that
            // fails means the port is no longer usable, so drop it -- ConnectionOk reports the device
            // as gone, and the application carries on without one rather than dying.
            //
            // Logged, which the previous catches did not do: a send failing silently is why this took
            // a crash to notice.
            Log.Error(e, "MIDI send failed ({Length} bytes, starting {Bytes}); treating the port as gone.",
                data.Length, BitConverter.ToString(data, 0, Math.Min(data.Length, 8)));
            _midiPortDetails = null;
            _access = null;
        }
    }
}