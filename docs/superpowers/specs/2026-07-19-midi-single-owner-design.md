# A single owner for MIDI device access — design

**Date:** 2026-07-19
**Status:** approved, ready for planning
**Scope:** piece B of the device-access redesign. Piece A (reply matching) shipped as merge `6bd99e7`.
Piece C (multi-step conversations in callers) is a separate spec. See "Out of scope".

## Problem

Nothing owns the MIDI port. A `SemaphoreSlim` serialises *individual requests*, and two things escape
even that.

**Sending has two doors.** `AsyncMidiOutputWrapper.SafeSendAsync` takes the semaphore, per message.
`IMidiOut.SafeSend` takes nothing. `Integra7Api.ChangePresetAsync` uses the second one, three times:

```csharp
    public async Task ChangePresetAsync(byte Channel, int Msb, int Lsb, int Pc)
    {
        BankSelectMsb(Channel, Msb);
        BankSelectLsb(Channel, Lsb);
        ProgramChange(Channel, Pc - 1);
        MessageBus.Current.SendMessage(new UpdateResyncPart(Channel));
    }
```

Those three messages must arrive consecutively on the same channel. Another flow's request sysex can
land between the bank select and the program change.

**The lock covers a request, not a conversation.** Every multi-step sequence releases between steps:

- `WriteToneToUserMemory` — three steps, each acquiring separately. Step 1 has the side effect of
  selecting the new patch, so a read landing between steps 1 and 3 reads the *new* patch, and a preset
  change landing there writes the wrong name to the wrong slot.
- `AllNotesOffAsync` — sixteen sends, sixteen separate acquisitions. A read can be serviced between
  the messages for parts 3 and 4.
- `ChangePresetAsync` followed by a read — the settle delay and epoch guards are the only protection.

**The semaphore is threaded through code that never uses it.** It is a constructor parameter on
`Integra7Domain`, all ~30 `Domain*` classes, `PartViewModel`, `PartialViewModel` and the four partial
view models. Only `Integra7Api` and `AsyncMidiOutputWrapper` call `WaitAsync`/`Release` — verified by
`grep -rn "_semaphore\." Src/`, which returns six lines, all in those two files. Every other class
appears to participate in locking and does not.

## Goal

One owner for the port. Every read, write and burst passes through it, and a conversation holds it for
its whole duration rather than one message at a time. Bypassing it should be impossible to express,
not merely discouraged.

## Architecture

### The port and the lease

```csharp
public interface IMidiPort
{
    /// <summary>Take exclusive use of the port for one conversation. <paramref name="what"/> names it
    /// in logs — it is what a hang report will say was holding the port.</summary>
    Task<IMidiLease> AcquireAsync(string what);
}

public interface IMidiLease : IAsyncDisposable
{
    Task SendAsync(byte[] data);

    /// <summary>Send a request and wait for the reply <paramref name="expected"/> recognises. Anything
    /// else that arrives is kept and dispatched when the lease is released.</summary>
    Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected);

    /// <summary>Wait for another reply on a request already sent — the burst case.</summary>
    Task<byte[]> ReadNextAsync(IReplyMatcher expected);
}
```

**The lease owns one reader for its lifetime.** A lease installs a single `AsyncMidiInputWrapper` when
it is taken and hands the port back when it is released — not one per request. `RequestAsync` sends and
waits on that reader; `ReadNextAsync` only waits. This is why the burst needs no special case any more:
the burst reader staying installed across many replies, which piece A had to express as a separate
`WaitForMidiMessageAsyncExpectingMultipleInARow` with a `handBackPort: false` flag, is simply what a
lease does. It is also why deferred messages accumulate naturally across a whole conversation rather
than per request.

A conversation reads:

```csharp
    await using var port = await _midi.AcquireAsync("preset change");
    await port.SendAsync(BankSelectMsb(channel, msb));
    await port.SendAsync(BankSelectLsb(channel, lsb));
    await port.SendAsync(ProgramChange(channel, pc - 1));
```

Nothing can interleave, and the lease is released on dispose whether the conversation completed or
threw.

### Bypass becomes a compile error

`MidiPort` takes ownership of `IMidiOut` and `IMidiIn`. `Integra7Api` no longer holds either, so
`SafeSend` and the `AsyncMidiInputWrapper` constructor are unreachable from outside a lease. That is
the difference between this design and merely wrapping the known multi-step sequences: it prevents the
*next* unguarded sequence from being written, not just the ones we know about.

`AsyncMidiOutputWrapper` disappears. The port does the locking; its `Task.Run` offload moves inside.

`MidiHandlerOwnership` disappears too. It exists to catch a request/reply desync — a reader finding
that another reader owns the port — which a single owner makes structurally impossible.

### Reentrancy

An `AsyncLocal<LeaseState>` tracks the current async flow's lease. A nested `AcquireAsync` returns the
lease already held, and disposing that inner handle does not release the port; only the outermost
dispose does. Without this, any conversation that called a helper which also acquires would deadlock
on itself — and `RunRequestAsync` is reached from dozens of places, so that is not a hypothetical.

**The hazard is fire-and-forget.** `AsyncLocal` flows into a task started inside a lease, so that task
would appear to hold a lease the outer conversation has since released. A lease therefore carries a
validity flag, and using a released one throws `ObjectDisposedException` naming both conversations —
"lease for 'part 6 load' was released; used by 'throttled write'". A loud, immediate error, rather
than a write onto a port someone else owns.

**Where the AsyncLocal does not flow** is work handed to a *pre-existing* pump rather than started
inside the lease — notably `ThrottledParameterWriter`'s Rx subscription on `Scheduler.Default`. Work
reaching the port from there is a different async flow, so it queues behind the lease rather than
reusing it. That is correct. It does mean a conversation that held a lease while awaiting a throttled
write would deadlock. No current path does that; the plan must check each migrated method rather than
assume it.

### Deferred messages drain on release

Piece A gave reads the ability to keep a message that is not their reply. Today `RunRequestAsync` and
`GetListOfNamesHelper` each drain separately, with duplicated per-message `try`/`catch`.

With a lease spanning a conversation, messages deferred by *any* read in it accumulate on the lease and
dispatch once, in `DisposeAsync`, after the port is released. Two copies collapse into one, and the
"drain only when the port is free" rule is enforced by where the code lives rather than by ordering
discipline at each call site.

### A diagnostic for hangs

`AcquireAsync` records the conversation's name and the time it was taken. When another conversation
waits longer than five seconds, the port logs at Warning:

> `waiting 12s to acquire for 'part 6 load' — held by 'user tone names 448-511' since 12s ago`

Every hang chased during this work presented as a frozen UI with no indication of who held what. This
turns that into one line naming both sides.

## What migrates

Every device-touching method on `Integra7Api`: `MakeDataRequestAsync`, `CheckIdentityAsync`,
`GetLoadedSrxAsync`, `GatherNamesAsync`, `MakeDataTransmissionAsync`, `NoteOnAsync`, `NoteOffAsync`,
`AllNotesOffAsync`, `ChangePresetAsync`, `SendStopPreviewPhraseMsgAsync`,
`SendPlayPreviewPhraseMsgAsync`, `SendLoadSrxAsync`, `WriteToneToUserMemory`.

Three change behaviour as a consequence, all intended:

| method | today | with a lease |
|---|---|---|
| `ChangePresetAsync` | 3 raw sends, no lock | 3 sends in one lease — cannot be split |
| `AllNotesOffAsync` | 16 sends, 16 acquisitions | one lease |
| `WriteToneToUserMemory` | 3 steps, each acquiring | one lease |

`ChangePresetAsync` also posts `UpdateResyncPart`. That post **moves after the lease is released**.
Holding the port while posting a message whose subscriber will try to acquire it is needless — it is
not a deadlock, since the post is async and throttled, but the subscriber would block until dispose.

`RunRequestAsync`, added by piece A, becomes a thin wrapper over acquire-request-dispose. Its shape —
acquire, converse, release, then deliver what arrived meanwhile — is what this generalises.

## The dead-semaphore cleanup

The `SemaphoreSlim` constructor parameter is removed from `Integra7Domain`, every `Domain*` class,
`DomainBase`, `PartViewModel`, `PartialViewModel` and the four partial view models, along with the
unused `_semaphore` fields in `DomainBase` and `PartialViewModel` and `PartViewModel`.
`MainWindowViewModel` stops creating one; it creates the `MidiPort` instead and passes that to
`Integra7Api`.

This is mechanical but wide — roughly 36 files. It belongs here because leaving it would mean classes
that appear to participate in locking while the real lock lives somewhere else entirely, which is the
confusion this whole piece exists to remove.

The plan must re-verify the "never used" claim before deleting, with
`grep -rn "_semaphore\." Src/`, rather than trusting this document.

## Error handling

A lease is released by `await using` on every path, including exceptions. The drain in `DisposeAsync`
guards each message individually, so one malformed deferred message cannot strand the rest — the same
rule piece A established.

A conversation that throws propagates to its caller unchanged. The port itself throws only
`ObjectDisposedException`, for a released lease.

## Testing

`MidiPort` is testable against fake `IMidiIn`/`IMidiOut` — `Tests/TestAsyncMidiInputWrapper.cs`
already has a `FakeMidiIn` to model on.

- **Exclusivity** — two concurrent acquires; the second does not proceed until the first disposes.
- **Reentrancy** — a nested acquire returns the same lease; disposing the inner handle does not release
  the port; the outermost dispose does.
- **The fire-and-forget hazard** — a task started inside a lease, holding the inherited `AsyncLocal`,
  throws `ObjectDisposedException` once the outer conversation has disposed.
- **Drain on release** — messages deferred by any read in the conversation dispatch exactly once, and
  only after the port is free.
- **Release on exception** — a conversation that throws still releases, and the next acquire succeeds.
- **The long-hold warning** fires with both conversation names.

The existing 387 tests must stay green. Build and test with the user-local SDK:
`"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"` — the system `dotnet` is 8/9 and too old.

## Hardware verification

1. **Change a preset.** The bank-select and program-change messages are not split, and the log shows
   one lease for the conversation.
2. **Save a user tone.** The three-step write holds one lease start to finish.
3. **Panic (all notes off) during a part load.** Sixteen messages, one lease, and the load resumes.
4. **Startup with no interaction.** No acquire warnings, and user preset names load — the regression
   fixed in `8c021eb`.
5. **Open a part while the startup name burst is running.** This should produce the long-hold warning
   naming both conversations. It is the positive test of the new diagnostic, and the first time this
   application will have said out loud what it is waiting for.

## Out of scope

**Piece C — multi-step conversations in callers**, which will still take one lease per step after this
change: `SynthParam`'s write-then-`WaveOutOfRangeReset`-then-read, `RestoreAuditionAsync` followed by a
program change, and `MotionalSurroundPartViewModel`'s L-R-then-F-B pair. Each is better off than today
but not yet atomic. They need callers to acquire a lease and pass it down, which is a wider change than
this one.

**The legacy synchronous reply path** — `_manualReplyHandling`,
`AnnounceIntentionToManuallyHandleReply`, `RestoreAutomaticHandling`, `GetReply` — is untouched.

**Rescanning MIDI devices** builds a second `Integra7Api` against new ports while the old one may still
be mid-conversation. A single owner narrows this but does not close it; the port would have to outlive
the API instance to fix it properly. Noted, not addressed.
