using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

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
        private int _released;

        /// <summary>The last send queued on this lease. Sends chain onto it so their order does not
        /// depend on the caller awaiting each one -- a single missed await would otherwise let a bank
        /// select and its program change race, which is the very thing this port exists to prevent.</summary>
        private Task _tail = Task.CompletedTask;

        public Lease(MidiPort port, string what)
        {
            _port = port;
            _what = what;
        }

        public Task SendAsync(byte[] data)
        {
            ThrowIfReleased();
            // Offloaded: SafeSend blocks on the driver, and callers are often on the UI thread.
            var send = _tail.ContinueWith(
                _ => _port._midiOut.SafeSend(data),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            _tail = send;
            return send;
        }

        public async ValueTask DisposeAsync()
        {
            // Guarded: a second dispose must not release the gate again, which would let two
            // conversations onto the port at once. Claimed atomically: two threads disposing the
            // same lease must not both see themselves as the first.
            if (Interlocked.Exchange(ref _released, 1) != 0) return;

            try
            {
                // Waited for so the gate is never handed to the next conversation while a send
                // queued on this one is still in flight.
                await _tail;
            }
            catch (Exception ex)
            {
                // A faulted send already surfaced its fault to whoever awaited it; disposal must
                // still release the gate.
                Log.Warning(ex, "A send queued on the lease for '{What}' faulted before disposal.", _what);
            }

            _port.Release();
        }

        private void ThrowIfReleased()
        {
            if (Volatile.Read(ref _released) != 0)
                throw new ObjectDisposedException(nameof(IMidiLease),
                    $"The lease for '{_what}' was released. An async flow started inside that " +
                    "conversation is still using it, which would write onto a port another " +
                    "conversation now owns.");
        }
    }
}
