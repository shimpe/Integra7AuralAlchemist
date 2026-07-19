# MIDI reply matching — design

**Date:** 2026-07-19
**Status:** approved, ready for planning
**Scope:** piece A of the device-access redesign. Pieces B (a single owner for all device traffic) and
C (explicit multi-request conversations) are separate specs. See "Out of scope".

## Problem

A read waits for *the next message to arrive*, whatever it is.

`AsyncMidiInputWrapper` installs itself as the single MIDI input handler in its constructor
(`AsyncMidiInputWrapper.cs:24`), which detaches `MidiIn.DefaultHandler`. Its handler writes every
message it receives into a channel (`:27-36`), and `WaitForMidiMessageAsync` returns the first one
(`:38-55`). Nothing checks that the message is a reply to the request that was just sent.

Two consequences, both silent:

1. **An unsolicited message is parsed as the reply.** If the user changes a preset on the
   INTEGRA-7's front panel while a read is in flight, that sysex is returned from
   `MakeDataRequestAsync` (`Integra7Api.cs:110`) and handed to
   `FullyQualifiedParameterRange.ParseFromSysexReply`, which interprets it as the address range that
   was requested. The values are wrong and nothing reports it.
2. **The unsolicited message is lost.** Having been consumed as a reply, it never reaches
   `DefaultHandler`, so the `hw2ui` update and the preset-change resync it should have triggered
   never happen. The UI keeps showing stale state.

The window is not small. A part load is dozens of sequential reads, and the startup name burst holds
a reader for many seconds across eighteen requests.

This was the mechanism behind a crash fixed on 2026-07-19 (`main`, merge `97e4d03`): the name-list
burst accepted any Roland sysex as a reply, and a front-panel preset change during startup was
collected as an answer to the name request and sliced for a name it was far too short to hold. That
fix hardened one reader by giving it a structural matcher — `NameListEndMarker.IsNameListReply`. This
spec generalises that to every reader.

## Goal

A read accepts only the reply to its own request. Anything else that arrives meanwhile is preserved
and delivered to the normal unsolicited path once the read has finished, so a message the device sends
is treated identically whether or not a read happened to be in flight.

## Architecture

Three pieces, each small and separately testable.

### 1. Reply matchers

A new pure file, `Src/Models/Services/ReplyMatchers.cs`, holding one predicate per conversation type.
No MIDI, no tasks — the same shape as `NameListEndMarker`, `PartLoadState` and the other helpers in
that directory.

```csharp
/// <summary>What a reader is waiting for. A conversation supplies one so a read can tell its own
/// reply from anything else the device happens to send while it is waiting.</summary>
public interface IReplyMatcher
{
    bool Matches(byte[] message);

    /// <summary>Shown in logs when a message is deferred, so a matcher that is wrong is visible as
    /// traffic that never matched rather than as parameters that silently stopped updating.</summary>
    string Describe();
}
```

Implementations:

| matcher | matches | used by |
|---|---|---|
| `DataSetAt(byte[] address)` | `f0 41`, command `0x12` at index 6, address at indices 7-10 equal to `address` | `MakeDataRequestAsync`, `GetLoadedSrxAsync` |
| `IdentityReply` | `f0 7e`, `06 02` at indices 3-4 | `CheckIdentityAsync` |
| `NameListReply(byte[] address)` | delegates to the existing `NameListEndMarker.IsNameListReply(message, address)` | `GetListOfNamesHelper` |

`NameListEndMarker.AddressOf(request)` already extracts the four address bytes from a request, and
requests and replies carry the address at the same offset. Matchers are therefore built from the
request the conversation is about to send, not from a constant.

### 2. Deferral in the reader

`AsyncMidiInputWrapper` takes the matcher as a constructor argument:

```csharp
public AsyncMidiInputWrapper(IMidiIn midiIn, IReplyMatcher expected)
```

`WaitForMidiMessageAsync` loops instead of returning the first message: a message the matcher accepts
is returned; anything else is appended to a private list and the wait continues. The burst variant,
`WaitForMidiMessageAsyncExpectingMultipleInARow`, does the same and returns each accepted reply.

The collected messages are exposed, not dispatched:

```csharp
/// <summary>Messages that arrived while this read was waiting and were not its reply, in arrival
/// order. Taking them clears the list; the caller delivers them once the port is free.</summary>
public IReadOnlyList<byte[]> TakeDeferred();
```

**Matching is done per chunk, not per message.** A single MIDI event can carry several concatenated
sysex messages. A chunk is accepted whole if *any* fragment in it matches (split with
`ByteUtils.SplitAfterF7`), and the whole chunk is returned unchanged, exactly as today — the parsers
downstream already expect that shape. A chunk is deferred only when no fragment matches. The known
limitation: a chunk carrying both the reply and an unsolicited message delivers the whole thing to the
parser and the unsolicited part is still lost. That is today's behaviour for that case, and narrowing
it would change what `ParseFromSysexReply` receives, so it stays out of this change.

### 3. One delivery path

The dispatch logic currently inline in `MidiIn.DefaultHandler:149-165` moves to a method on the
interface:

```csharp
/// <summary>Route a message nobody requested: a data set becomes a UI update, a preset change
/// becomes a resync, anything else is logged. Called both by the default handler and by a reader
/// draining what arrived while it was waiting, so the two cannot diverge.</summary>
void DispatchUnsolicited(byte[] message);
```

`DefaultHandler` calls it. So does the drain. That equivalence is the fix: a front-panel preset change
does the same thing whether or not a read was in flight.

Each dispatch is individually guarded — one malformed deferred message must not strand the rest.

## Timing

**The drain runs after the semaphore is released.** A deferred message must reach the UI with the port
actually free, not merely with the read logically finished. Dispatch is a `MessageBus` post to a
throttled subscriber, so nothing reenters synchronously, but a resync it triggers will take the
semaphore and should find it available.

This pulls the three single-reply conversations into one shape, in `Integra7Api`:

```csharp
private async Task<byte[]> RunRequestAsync(byte[] request, IReplyMatcher expected, string what)
{
    var midiIn = _midiIn;            // captured: CheckIdentityAsync nulls the field on failure
    if (midiIn is null) return [];

    byte[] reply;
    IReadOnlyList<byte[]> deferred;

    await _semaphore.WaitAsync();
    try
    {
        var mi = new AsyncMidiInputWrapper(midiIn, expected);
        _midiOut?.SafeSend(request);
        reply = await mi.WaitForMidiMessageAsync();
        deferred = mi.TakeDeferred();
        if (reply.Length == 0)
        {
            mi.CleanupAfterTimeOut();
            Log.Error("Timeout waiting for MIDI reply: {What}.", what);
        }
    }
    finally
    {
        _semaphore.Release();
    }

    foreach (var m in deferred)
    {
        // Guarded per message: one malformed deferred message must not strand the rest.
        try { midiIn.DispatchUnsolicited(m); }
        catch (Exception e) { Log.Warning(e, "Failed to dispatch a message deferred during {What}.", what); }
    }

    return reply;
}
```

`MakeDataRequestAsync` (`:101`), `CheckIdentityAsync` (`:75`) and `GetLoadedSrxAsync` (`:185`) become
calls to it, each supplying its matcher and post-processing the reply as it does today.
`GetListOfNamesHelper` (`:514`) keeps its own loop — it accepts many replies — and drains the same way
after releasing the semaphore.

Two behaviour changes fall out of this, both intended:

- **`CheckIdentityAsync` starts taking the semaphore.** It takes none today (`:75-99`), so it can run
  concurrently with any other conversation — including the name burst, when the user presses Rescan.
  Routing it through the shared helper fixes that.
- The identity conversation captures `_midiIn` before the read, because the failure path nulls the
  field (`:86-87`) and the drain must still have a port to dispatch on.

## Timeout

The deadline runs from the request, not from the last message: non-matching traffic does not extend it,
or a chatty device could hold a read open indefinitely. Within a burst, each *accepted* reply extends
it, which is today's behaviour.

A read that times out still drains whatever it deferred. Those messages are real device traffic and
losing them is the bug being fixed, not an acceptable consequence of a timeout.

## Error handling

A deferred message that fails to dispatch is logged at Warning and skipped; the remaining ones are
still delivered.

Every deferred message is logged at Warning with its length, leading bytes, and the matcher's
`Describe()`. This is the safety net for the main regression risk: **a matcher that is too strict**
turns a working read into a 1.5 s timeout, after which `DomainBase` logs "Keeping the previous values"
and carries on — quiet, and easy to miss. A wrong matcher shows up in the log as traffic that never
matched, rather than as parameters that silently stopped updating.

## Testing

`ReplyMatchers` is pure, so the matching table is a unit fixture:

- `DataSetAt` accepts a DT1 at the requested address; rejects the same DT1 at a different address, a
  preset-change sysex, an identity reply, and a message too short to hold an address.
- `IdentityReply` accepts a real identity reply and rejects a DT1.
- `NameListReply` delegates correctly for both the tone-name address and the Studio Set address.
- A chunk containing an unsolicited message followed by the reply is accepted; a chunk containing
  neither is not.

`AsyncMidiInputWrapper` is tested against a fake `IMidiIn`, which the existing interface already makes
possible — no production seam has to be invented. Cases:

- the reply arrives first: returned, `TakeDeferred()` empty;
- one non-matching message arrives before the reply: reply returned, that message in `TakeDeferred()`;
- only non-matching messages arrive: the read times out and all of them are in `TakeDeferred()`;
- nothing arrives: the read times out with an empty deferred list;
- `TakeDeferred()` clears, so a second call returns nothing.

The ordering guarantee — deferred messages dispatch only after the semaphore is released — is about
lock state and is not visible to a unit test. It is asserted by construction in review and confirmed
by the hardware pass.

The existing 365 tests must stay green.

Build and test with the user-local SDK: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"`. The system
`dotnet` is 8/9 and too old for this solution.

## Hardware verification

1. **Front-panel preset change during a part load.** The change must apply once the load finishes,
   rather than being lost. Today it is swallowed. This is the headline behaviour.
2. **Front-panel preset change during the startup name burst.** Same expectation, across the longest
   read window in the application. This is the scenario that crashed before the burst was hardened.
3. **A normal session with no panel interaction.** The log must show no deferred-message warnings. Any
   warning here means a matcher is too strict and is rejecting real replies.
4. **Rescan MIDI devices while the name burst is running.** `CheckIdentityAsync` now takes the
   semaphore, so this should serialise rather than run two conversations on one port.

## Out of scope

**Piece B — a single owner** for all device traffic: a queue or actor through which every read, write
and burst passes, owning whole conversations rather than individual requests. `RunRequestAsync` above
is deliberately the shape B generalises — acquire, converse, release, then deliver what arrived
meanwhile — but B replaces the semaphore inside it with a queue and lets a conversation span several
requests. Until B lands, `ChangePresetAsync` (`Integra7Api.cs:445-451`) still sends its three messages
with no lock at all, so they can still be split by another flow's request.

**Piece C — explicit conversations** for the multi-step sequences: program-change-then-read,
`WriteToneToUserMemory`'s three steps, discriminator write-then-read, and restore-audition-then-program
-change. C depends on B.

**The legacy synchronous reply path** — `_manualReplyHandling`,
`AnnounceIntentionToManuallyHandleReply`, `RestoreAutomaticHandling`, `GetReply` — is untouched.

This spec does not make the device layer safe. It makes reads honest, which is what lets B be verified:
until replies are matched, a correctly serialised conversation and a lucky one look the same.
