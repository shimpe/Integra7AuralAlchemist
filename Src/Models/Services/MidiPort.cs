using System;
using System.Collections.Generic;
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

    /// <summary>Send a request and wait for the reply <paramref name="expected"/> recognises. Anything
    /// else that arrives is kept and dispatched when the lease is released.</summary>
    Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected);

    /// <summary>Wait for another reply to a request already sent -- the burst case.</summary>
    Task<byte[]> ReadNextAsync(IReplyMatcher expected);
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

        /// <summary>Guards the release flag and the chaining of sends together. Both must move under
        /// one lock: a send that read the tail just before a concurrent disposal claimed the lease
        /// would otherwise chain onto a tail that disposal had already snapshotted, and the gate would
        /// be handed on with that send still in flight.</summary>
        private readonly object _sync = new();

        private bool _released;

        /// <summary>The last send queued on this lease. Sends chain onto it so their order does not
        /// depend on the caller awaiting each one -- a single missed await would otherwise let a bank
        /// select and its program change race, which is the very thing this port exists to prevent.</summary>
        private Task _tail = Task.CompletedTask;

        /// <summary>This conversation's reader, installed once and kept for the whole lease. A reader
        /// per request would hand the port back between the replies of a burst.</summary>
        private AsyncMidiInputWrapper? _reader;

        /// <summary>Messages that arrived during this conversation and were not a reply to any of its
        /// reads. Dispatched once, after the gate is released.</summary>
        private readonly List<byte[]> _deferred = [];

        /// <summary>True while a read is in flight. Reads are not chained the way sends are -- a read's
        /// caller needs the reply, so a fire-and-forget read is not the plausible mistake an unawaited
        /// send is. But two at once would overwrite the reader's matcher mid-flight and race over one
        /// channel, matching a reply against the wrong matcher, so say so instead of misbehaving.</summary>
        private bool _reading;

        public Lease(MidiPort port, string what)
        {
            _port = port;
            _what = what;
        }

        public Task SendAsync(byte[] data)
        {
            lock (_sync)
            {
                ThrowIfReleased();

                // Offloaded: SafeSend blocks on the driver, and callers are often on the UI thread.
                var send = _tail.ContinueWith(
                    previous =>
                    {
                        // Chaining onto a faulted task marks that fault observed, so a send that
                        // failed mid-chain would otherwise vanish -- no rethrow, no unobserved-task
                        // event, nothing. Only whoever awaited that particular send would ever know,
                        // and the point of chaining is that callers need not await each one.
                        if (previous.IsFaulted)
                            Log.Warning(previous.Exception!.GetBaseException(),
                                "An earlier send on the lease for '{What}' faulted.", _what);

                        _port._midiOut.SafeSend(data);
                    },
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

                _tail = send;
                return send;
            }
        }

        public async Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected)
        {
            // The reader is installed BEFORE the request goes out: a reply arriving promptly would
            // otherwise land before anything was listening for it.
            var reader = Reader(expected);
            await SendAsync(request);
            return await ReadFrom(reader, expected);
        }

        public Task<byte[]> ReadNextAsync(IReplyMatcher expected) => ReadFrom(Reader(expected), expected);

        private AsyncMidiInputWrapper Reader(IReplyMatcher expected)
        {
            lock (_sync)
            {
                ThrowIfReleased();
                return _reader ??= new AsyncMidiInputWrapper(_port._midiIn, expected);
            }
        }

        private async Task<byte[]> ReadFrom(AsyncMidiInputWrapper reader, IReplyMatcher expected)
        {
            lock (_sync)
            {
                if (_reading)
                    throw new InvalidOperationException(
                        $"A read is already in flight on the lease for '{_what}'. Reads on one lease " +
                        "must be awaited before the next is issued; two at once would race over the " +
                        "reader's matcher.");

                _reading = true;
            }

            try
            {
                var reply = await reader.WaitForAsync(expected);
                lock (_sync)
                {
                    _deferred.AddRange(reader.TakeDeferred());
                }

                return reply;
            }
            finally
            {
                lock (_sync)
                {
                    _reading = false;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task tail;
            AsyncMidiInputWrapper? reader;
            byte[][] deferred;
            lock (_sync)
            {
                // A second dispose must not release the gate again, which would let two conversations
                // onto the port at once.
                if (_released) return;

                _released = true;
                tail = _tail;
                reader = _reader;
                deferred = _deferred.ToArray();
                _deferred.Clear();
            }

            try
            {
                // Waited for so the gate is never handed to the next conversation while a send
                // queued on this one is still in flight.
                await tail;
            }
            catch (Exception ex)
            {
                // A faulted send already surfaced its fault to whoever awaited it; disposal must
                // still release the gate.
                Log.Warning(ex, "A send queued on the lease for '{What}' faulted before disposal.", _what);
            }

            reader?.Detach();
            _port.Release();

            // After the release, deliberately: a deferred message reaching the UI can trigger a
            // resync, which takes the port, and it must find it free.
            foreach (var m in deferred)
            {
                // Guarded per message: one malformed deferred message must not strand the rest.
                try
                {
                    _port._midiIn.DispatchUnsolicited(m);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to dispatch a message deferred during '{What}'.", _what);
                }
            }
        }

        /// <summary>Call only while holding <see cref="_sync"/>.</summary>
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
