using System;
using System.Threading;
using System.Threading.Tasks;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The one owner of the MIDI port. Everything that talks to the device takes a lease first,
/// and a lease covers a whole conversation rather than a single message -- so a sequence that must
/// arrive together, like a bank select followed by a program change, cannot be split.
///
/// A lease is never acquired while one is held. There is no reentrancy: a write to an AsyncLocal
/// inside an async method is not visible to the caller, so the lease cannot be carried implicitly,
/// and acquiring twice in one flow would simply block on the gate. Public API methods acquire;
/// helpers take a lease.</summary>
public interface IMidiPort
{
    /// <summary>Take exclusive use of the port for one conversation. <paramref name="what"/> names it
    /// in logs: it is what a hang report will say was holding the port.</summary>
    Task<IMidiLease> AcquireAsync(string what);
}

/// <summary>Exclusive use of the port, for as long as it is held.</summary>
public interface IMidiLease : IAsyncDisposable
{
    Task SendAsync(byte[] data);
}

public sealed class MidiPort : IMidiPort
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IMidiOut _midiOut;
    private readonly IMidiIn _midiIn;

    public MidiPort(IMidiOut midiOut, IMidiIn midiIn)
    {
        _midiOut = midiOut;
        _midiIn = midiIn;
    }

    public async Task<IMidiLease> AcquireAsync(string what)
    {
        await _gate.WaitAsync();
        return new Lease(this, what);
    }

    private void Release() => _gate.Release();

    private sealed class Lease : IMidiLease
    {
        private readonly MidiPort _port;
        private readonly string _what;
        private bool _released;

        public Lease(MidiPort port, string what)
        {
            _port = port;
            _what = what;
        }

        public Task SendAsync(byte[] data)
        {
            ThrowIfReleased();
            // Offloaded: SafeSend blocks on the driver, and callers are often on the UI thread.
            return Task.Run(() => _port._midiOut.SafeSend(data));
        }

        public ValueTask DisposeAsync()
        {
            // Guarded: a second dispose must not release the gate again, which would let two
            // conversations onto the port at once.
            if (_released) return ValueTask.CompletedTask;

            _released = true;
            _port.Release();
            return ValueTask.CompletedTask;
        }

        private void ThrowIfReleased()
        {
            if (_released)
                throw new ObjectDisposedException(nameof(IMidiLease),
                    $"The lease for '{_what}' was released. An async flow started inside that " +
                    "conversation is still using it, which would write onto a port another " +
                    "conversation now owns.");
        }
    }
}
