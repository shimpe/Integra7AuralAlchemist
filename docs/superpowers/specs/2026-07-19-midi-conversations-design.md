# Conversations that span the domain layer — design

**Date:** 2026-07-19
**Status:** approved, ready for planning
**Scope:** piece C, the last of the device-access redesign. Pieces A (reply matching, merge `6bd99e7`)
and B (the single owner, merge `141ed5a`) have shipped.

## Problem

`MidiPort` hands out a lease that covers a conversation, and `Integra7Api` uses one per method. But four
sequences are not one method: they leave `Integra7Api`, go out through the domain layer, and come back.
Each call along the way acquires its own lease, so another flow can land in the middle.

| sequence | what interleaving costs |
|---|---|
| `Integra7Api.WriteToneToUserMemory` | Step 1 selects the new patch. A read between steps 1 and 3 reads the *new* patch; a preset change there writes the wrong name to the wrong slot. |
| `SynthParam.Enqueue` (`Src/ViewModels/SynthParam.cs:69-79`) | Write, reset out-of-range dependents, re-read. The read must observe the state *those* writes produced; another flow's write to the same domain in between corrupts it silently. |
| The `ui2hw` handler (`Src/ViewModels/MainWindowViewModel.cs:668-680`) | Same shape as above, reached from the raw parameter grid instead of a friendly editor. |
| `PartViewModel.ChangePresetAsync` | Restores partial switches through both editors, then sends the program change. Interleaving can leave the *outgoing* tone holding audition state. |

`WriteToneToUserMemory` currently carries a comment saying it is deliberately not atomic, and a test
asserting it never holds two leases at once — because with no way to pass a lease down, one lease
across its steps would deadlock against itself. This spec removes that limitation.

**Deliberately excluded:** `MotionalSurroundPartViewModel.WritePositionAsync`, whose two writes
describe one puck position. Interleaving leaves the puck briefly mixed, which is cosmetic and
self-corrects on the next drag — and the puck drag is the highest-frequency write path in the
application, so keeping it off the lease matters more than the defect does.

## Architecture

### An optional lease, threaded

Every method on the path from a caller to the device gains an `IMidiLease? lease = null` parameter:

| method | file |
|---|---|
| `MakeDataRequestAsync`, `MakeDataTransmissionAsync`, `ChangePresetAsync` | `IIntegra7Api` / `Integra7Api` |
| `WriteToIntegraAsync`, `RetrieveFromIntegraAsync` | `FullyQualifiedParameter`, `FullyQualifiedParameterRange` |
| `ReadFromIntegraAsync` (×2), `WriteToIntegraAsync` (×3) | `DomainBase` |
| `WriteSingleParameterToIntegraAsync` | `Integra7Domain` |
| `ApplyAsync` | `WaveOutOfRangeReset` |
| `RestoreAuditionAsync` | `PCMSynthToneEditorViewModel`, `SNSynthToneEditorViewModel` |
| `WriteImmediateAsync` | `SynthParam` |

Because every default is null, **adding the parameters changes no behaviour**: the port is acquired
exactly where it is today. The wide, mechanical part of this change can land and be verified green
before any behaviour moves.

### Borrowing versus owning

A call given a lease must not release it — the lease belongs to the conversation that opened it and
has to outlive the call.

```csharp
    /// <summary>A lease this call may or may not own. Disposing it releases the port only when this
    /// call acquired it; a lease passed in belongs to the conversation that opened it.</summary>
    private readonly struct Borrowed(IMidiLease lease, bool owned) : IAsyncDisposable
    {
        public IMidiLease Lease { get; } = lease;
        public ValueTask DisposeAsync() => owned ? lease.DisposeAsync() : ValueTask.CompletedTask;
    }

    private async Task<Borrowed> LeaseAsync(IMidiLease? given, string what) =>
        given is not null ? new Borrowed(given, false) : new Borrowed(await _port.AcquireAsync(what), true);
```

so each device method reads:

```csharp
    public async Task MakeDataTransmissionAsync(byte[] address, byte[] data, IMidiLease? lease = null)
    {
        var transmission = Integra7SysexHelpers.MakeDataSet(DeviceId(), address, data);
        await using var use = await LeaseAsync(lease, "parameter write");
        await use.Lease.SendAsync(transmission);
    }
```

### Starting a conversation

A caller needs a way to open one. `IIntegra7Api` gains a single method — it is already the device
facade, and this is the only new capability:

```csharp
    /// <summary>Open a conversation the caller will hold across several calls, passing the lease to
    /// each. A caller making only one call should not use this: the call acquires for itself.</summary>
    Task<IMidiLease> BeginConversationAsync(string what);
```

`DomainBase` forwards it, so a view model holding only a domain can start one.

### Where each conversation begins

| sequence | opened by | named |
|---|---|---|
| `WriteToneToUserMemory` | itself | `write tone to user memory` |
| `SynthParam.Enqueue` | the throttled write | `edit <parameter path>` |
| `ui2hw` handler | the subscriber | `edit <parameter path>` |
| restore-audition → preset change | `PartViewModel.ChangePresetAsync` | `part <n> preset change` |

The last reaches furthest: `RestoreAuditionAsync` on both editor view models takes a lease and passes
it to `SynthParam.WriteImmediateAsync`, which passes it to the domain write.

## What this removes

`WriteToneToUserMemory`'s "NOT atomic, deliberately" comment comes off, and its test inverts:
`WritingAToneToUserMemoryKeepsItsStepsInSeparateConversations` becomes
`WritingAToneToUserMemoryIsOneConversation`.

That inversion is the point, not a contradiction. The old assertion pinned a limitation and said so in
its comment; this piece removes the limitation, so the assertion must move with it.

## Not changed

`ChangePresetAsync` posts `UpdateResyncPart`. With a caller-supplied lease, that post now happens while
the caller still holds the port. The subscriber is a different async flow, so it queues rather than
deadlocking, and `ResyncPartAsync`'s `ReloadPending` guard usually drops it anyway. Moving the post
would mean returning something for the caller to post later — more machinery than the problem
deserves.

## Error handling

A conversation that throws releases its lease through `await using`, as today. A borrowed lease is
untouched by the borrower on every path, including exceptions, because `Borrowed.DisposeAsync` checks
ownership before releasing.

**A missed hand-off is loud, not silent.** If a sequence opens a lease but one call inside it does not
receive it, that call acquires its own — a nested acquire, which blocks and throws `TimeoutException`
after 60 seconds naming both conversations. Unpleasant if it reaches hardware, which is why the tests
below are shaped to catch it first.

## Testing

`RecordingPort.MostLeasesHeldAtOnce` already exists in `Tests/TestIntegra7ApiConversations.cs`, built
for the `WriteToneToUserMemory` test. It turns a missed hand-off into a test failure in milliseconds
rather than a hang on hardware, and each of the four sequences gets the same assertion: exactly one
conversation, never two leases at once.

Two cases need more:

- **A borrowed lease is not released by the borrower.** Pass a lease into `MakeDataTransmissionAsync`,
  then keep using it. If `Borrowed` releases what it did not acquire, the port is free mid-sequence and
  the next call can take it.
- **`SynthParam` and the editors** need a real `Integra7Domain` over a `RecordingPort`-backed api. The
  existing `WriteToneToUserMemory` test shows how to build one.

The existing 409 tests must stay green, apart from the one inversion named above.

Build and test with the user-local SDK: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"` — the system
`dotnet` is 8/9 and too old.

## Hardware verification

1. **Save a user tone.** One conversation; the name lands in the right slot.
2. **Drag a parent control** — a waveform or oscillator selector. Dependents re-read correctly and no
   timeout warnings appear.
3. **Change a preset while a partial is soloed.** Audition state is restored before the patch changes,
   and the outgoing tone is not left holding it.
4. **A normal session.** No `Gave up after 60s` anywhere in the log. That exception means a hand-off
   was missed.

## A latency change to expect

A parent-parameter edit now holds the port across write → reset → *full domain re-read*, where each of
those used to acquire separately. That is what atomicity means here and it is the point — but dragging
a parent control holds the port longer than before, and other throttled writes queue behind it.

If the UI feels sluggish on a parent control after this, that is the cause. The answer is to narrow
what the conversation covers, not to widen the port.

## Out of scope

**`MotionalSurroundPartViewModel.WritePositionAsync`**, for the reasons under "Problem".

**Rescanning MIDI devices** builds a second port while the first may be mid-conversation. Carried over
from piece B's spec: noted, not addressed, and not covered by any hardware scenario.

**The legacy synchronous reply path** — `AnnounceIntentionToManuallyHandleReply`,
`RestoreAutomaticHandling`, `GetReply` — has had no production caller since piece A. Worth deleting,
but not here.
