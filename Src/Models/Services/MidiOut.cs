using System;
using System.Linq;
using OwnAudio.Midi.IO;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public interface IMidiOut
{
    public bool ConnectionOk();
    public void SafeSend(byte[] data);
}

public sealed class MidiOut : IMidiOut, IDisposable
{
    /// <summary>Guards the handle: a rescan disposes this object while a conversation may still be
    /// sending through it.</summary>
    private readonly object _lock = new();
    private IMidiOutputPort? _access;
    private string? _portName;
    private bool _disposed;
#if DEBUG
    public bool Verbose { get; set; } = true;
#else
    public bool Verbose { get; set; } = false;
#endif
    public MidiOut(string Name)
    {
        try
        {
            var portNames = MidiPortFactory.GetOutputPortNames();
            _portName = portNames.LastOrDefault(x => x.Contains(Name));
            if (_portName is null)
                Log.Information("No MIDI output matches '{Name}'; available: {Ports}.", Name, portNames);
        }
        catch (Exception e)
        {
            // A missing native library ends up here; the application carries on without a device.
            Log.Error(e, "Could not list the MIDI outputs while looking for '{Name}'.", Name);
            _portName = null;
        }
    }

    public bool ConnectionOk()
    {
        return _portName != null;
    }

    public void SafeSend(byte[] data)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                Log.Warning("No MIDI message sent because the port was closed by a rescan.");
                return;
            }

            try
            {
                if (_access is null)
                {
                    if (_portName is null)
                    {
                        Log.Error("No MIDI message sent because no Integra-7 hardware found.");
                        return;
                    }

                    _access = MidiPortFactory.OpenOutput(_portName);
                }

                // OwnAudio sends SysEx and short messages through different calls, so an array holding
                // a bank select and a program change -- or any other run of messages -- is taken apart.
                foreach (var message in MidiWire.SplitMessages(data))
                {
                    if (message[0] == 0xF0)
                        _access.SendSysEx(message);
                    else
                        _access.Send(new MidiMessage(message[0],
                            message.Count > 1 ? message[1] : (byte)0,
                            message.Count > 2 ? message[2] : (byte)0));
                }

                if (Verbose) ByteStreamDisplay.Display("Sent: ", data);
            }
            catch (ArgumentException e)
            {
                // OwnAudio reports a port that is not there (any more) as an ArgumentException, so the
                // device really is gone.
                LogSendFailure(e, data, "the port is gone");
                _portName = null;
                DropHandle();
            }
            catch (Exception e)
            {
                // Anything else is about THIS message, not the port. A malformed message must not
                // condemn the device for the rest of the session. The handle is dropped so the next send
                // reopens, but the port name stays, so ConnectionOk still reports a device.
                LogSendFailure(e, data, "keeping the port and reopening on the next send");
                DropHandle();
            }
        }
    }

    private void DropHandle()
    {
        try
        {
            _access?.Dispose();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Closing a failed MIDI output handle failed as well.");
        }

        _access = null;
    }

    /// <summary>Close the port so a rescan can open the device again.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_access is not null)
                Log.Information("Closing MIDI output '{Port}'.", _access.Name);
            DropHandle();
        }
    }

    private static void LogSendFailure(Exception e, byte[] data, string outcome)
    {
        Log.Error(e, "MIDI send failed ({Length} bytes, starting {Bytes}); {Outcome}.",
            data.Length, BitConverter.ToString(data, 0, Math.Min(data.Length, 8)), outcome);
    }
}
