# How the application talks to the INTEGRA-7

**Last updated:** 2026-07-19

This describes how MIDI device access works after the redesign of July 2026. It is written for
someone about to change code that touches the hardware — including whoever you are in six months.

---

## The big picture

Everything the application says to, or hears from, the INTEGRA-7 goes through **one object**: a
`MidiPort`. Nothing else holds the MIDI handles, so there is no second door.

To use the device you first take a **lease**. A lease is exclusive use of the port for as long as you
hold it, and it is the only thing that can send or read. One lease covers one **conversation** — a
whole exchange with the device, not a single message.

```
        your code
            │
            │  await using var lease = await port.AcquireAsync("preset change");
            ▼
      ┌───────────┐   one at a time    ┌──────────────┐
      │  MidiPort │───────────────────▶│  INTEGRA-7   │
      └───────────┘                    └──────────────┘
            ▲
            │  everyone else waits here
            │
     other conversations
```

That is the whole idea. The rest of this document is what follows from it.

### Why it exists

Before this, a lock protected each individual *message*, not each *exchange*. Two consequences bit
repeatedly:

- **Multi-message sequences could be split.** Changing a preset means sending bank-select MSB,
  bank-select LSB, and a program change. Those three must arrive together. Another flow's request
  landing between them selects the wrong patch.
- **Write-then-read sequences read someone else's state.** Writing a parameter and then re-reading to
  see its effect only works if nothing else wrote in between.

There was also a second way to send that took no lock at all, so the rule "hold the lock" depended on
everyone remembering. Now it is not expressible: the send methods live on the lease, and only the port
can make one.

---

## The four rules

Everything else is detail. These are the rules.

### 1. One conversation at a time

`AcquireAsync` blocks until the port is free. A conversation holds it until disposed.

### 2. Never acquire a lease while you already hold one

There is **no reentrancy**. If you hold a lease and call something that acquires its own, you block
against yourself until a 60-second timeout throws.

This is why the shape below matters: **public methods acquire; helpers take a lease.**

```csharp
// Public entry point — acquires.
public async Task DoSomethingAsync()
{
    await using var lease = await _port.AcquireAsync("something");
    await StepOneAsync(lease);
    await StepTwoAsync(lease);
}

// Helper — never acquires, always given one.
private async Task StepOneAsync(IMidiLease lease) => await lease.SendAsync(...);
```

### 3. If you were given a lease, you must not release it

It belongs to the conversation that opened it and has to outlive your call. `Integra7Api` expresses
this with a small `Borrowed` struct: it releases the port only when *this* call acquired it.

### 4. A lease covers everything the conversation needs, and nothing more

Holding it longer than necessary makes everyone else wait. Holding it for less than the sequence needs
defeats the point.

---

## A conversation, start to finish

Changing a preset is the clearest example. It sends three messages that must not be separated:

```csharp
public async Task ChangePresetAsync(byte channel, int msb, int lsb, int pc, IMidiLease? lease = null)
{
    await using (var use = await LeaseAsync(lease, "preset change"))
    {
        await use.Lease.SendAsync(BankSelectMsb(channel, msb));
        await use.Lease.SendAsync(BankSelectLsb(channel, lsb));
        await use.Lease.SendAsync(ProgramChange(channel, pc - 1));
    }

    MessageBus.Current.SendMessage(new UpdateResyncPart(channel));
}
```

Three things in that snippet are worth noticing:

- **`LeaseAsync(lease, "preset change")`** — use the lease we were given, or take our own if we were
  given none. That is rule 3 in code.
- **The name `"preset change"`** — it is what a hang report will say was holding the port. Name
  conversations for what they are, not for the method they live in.
- **The message posted *after* the block** — its subscriber will want the port, so it is posted once
  ours is free.

### Sends keep their order without you having to await

Sends chain on the lease. Two sends issued back to back arrive in that order whether or not you awaited
the first. This is deliberate: a missed `await` is a compiler *warning*, not an error, and one missed
await used to let a bank select race its own program change.

---

## Reading: how a reply is recognised

A read is not "send, then take the next message that arrives". The device also speaks unprompted —
turn the patch dial on the front panel and it says so — and while a read is in progress, that reader
is the only listener.

So a read says what it is waiting for:

```csharp
var reply = await lease.RequestAsync(request, ReplyMatchers.DataSetAt(address));
```

`IReplyMatcher` recognises the reply this conversation asked for. Three exist:

| matcher | recognises |
|---|---|
| `ReplyMatchers.DataSetAt(address)` | a data set (DT1) carrying that address |
| `ReplyMatchers.IdentityReply` | the universal identity reply |
| `ReplyMatchers.NameListReply(address)` | one reply of a name-list burst |

Anything that arrives and is **not** the reply is kept, not discarded. When the lease is released, those
messages are handed to the normal unsolicited path — the same route they would have taken had no read
been in progress. So a preset change made on the device during a part load is applied when the load
finishes, instead of vanishing.

**Why the drain happens after release, not before:** an unsolicited message can trigger a resync, which
needs the port. It must find it free.

### Bursts

Fetching the user tone names sends one request and reads many replies. That needs no special case: a
lease keeps **one reader for its whole life**, so `RequestAsync` once and `ReadNextAsync` thereafter.

```csharp
await using var lease = await _port.AcquireAsync($"name list at {address}");
var first = await lease.RequestAsync(request, expected);
// ... then, repeatedly:
var next = await lease.ReadNextAsync(expected);
```

---

## Conversations that leave and come back

Some sequences are not one method. A parameter edit writes through the domain layer:

```
your code → DomainBase.WriteToIntegraAsync
          → FullyQualifiedParameter.WriteToIntegraAsync
          → Integra7Api.MakeDataTransmissionAsync
          → the lease
```

If each of those acquired its own lease, the sequence would be interleavable again. So **every method
on that path takes an optional lease**:

```csharp
public async Task WriteToIntegraAsync(string parameterName, string displayedValue,
    IMidiLease? lease = null)
```

Pass yours down and the whole sequence is one conversation. Pass nothing and each call is its own —
which is right for a single write.

Opening one from a view model:

```csharp
// From anything holding an IIntegra7Api:
await using var lease = await _i7Api.BeginConversationAsync($"part {PartNo} preset change");

// Or from anything holding a domain:
await using var lease = await _domain.BeginConversationAsync($"edit {path}");
```

### The sequences that do this today

| sequence | why it must be atomic |
|---|---|
| `Integra7Api.WriteToneToUserMemory` | Step 1 selects the new patch. A preset change between steps writes the wrong name to the wrong slot. |
| The three `Enqueue` bodies in `SynthParam` | Write, reset dependents pushed out of range, re-read. The read must see what *those* writes produced. |
| `MainWindowViewModel.UpdateIntegraFromUiAsync` | Same, from the raw parameter grid. |
| `PartViewModel.ChangePresetAsync` | Audition switches must be restored *before* the patch changes, or the outgoing tone keeps them. |

**Deliberately not atomic:** `MotionalSurroundPartViewModel.WritePositionAsync` writes two values for
one puck position. Interleaving leaves the puck briefly mixed — cosmetic, self-correcting on the next
drag — and it is the highest-frequency write path in the application, so it stays off the lease.

---

## When something goes wrong

The failure modes are deliberately loud, because the ones that came before were not.

### A wait that runs long

After 5 seconds waiting for the port, the log says who has it:

```
Waiting 5s to acquire the MIDI port for 'part 6 load' -- held by 'name list at 0F-00-04-02' for 7s.
```

This is **normal** during startup: a part load queued behind the name burst is waiting correctly. It is
information, not an error.

### A wait that runs absurdly long

After 60 seconds it throws, naming both sides:

```
Gave up after 60s waiting for the MIDI port for 'X' -- held by 'Y'. Something acquired the port while
already holding it, which is not supported: public methods acquire, helpers take a lease.
```

**This is always a bug in this application**, never a device problem. It means rule 2 was broken — a
call inside a conversation did not receive the lease and tried to take its own. Find that call. Do not
raise the timeout.

### Using a lease after its conversation ended

Throws `ObjectDisposedException` naming the conversation. This happens when a fire-and-forget task
started inside a conversation outlives it. Writing onto a port someone else now owns would be worse.

### A send that fails

Logged at `Error` with the failing bytes. Failures that mean the handle is dead drop the port —
`ConnectionOk()` then reports no device. Anything else (a malformed message, say) drops only the cached
handle, so the next send reopens and one bad message cannot condemn the device for the session.

---

## How to add code that talks to the device

**Making a single call?** Just call it. `MakeDataRequestAsync`, `WriteToIntegraAsync` and friends
acquire and release for you.

```csharp
await _domain.WriteToIntegraAsync(path, value);
```

**Making several calls that must not be interleaved?** Open a conversation and pass the lease to every
call in it.

```csharp
await using var lease = await _domain.BeginConversationAsync($"what I am doing");
await _domain.WriteToIntegraAsync(path, value, lease);
await _domain.ReadFromIntegraAsync(lease);
```

Then check three things:

1. **Every call inside the block takes the lease.** One that does not will hang for 60 seconds. This is
   the mistake to look for.
2. **You did not pass the lease to something that starts a *different* flow** — enqueuing work on the
   throttled writer, or posting to the MessageBus. Those run later, on their own; they must acquire for
   themselves.
3. **You are not holding the lease across work that does not need it** — UI updates, logging,
   in-memory mutation. Do those outside.

### Testing it

`Tests/TestIntegra7ApiConversations.cs` has a `RecordingPort` that records how many conversations were
opened and — importantly — `MostLeasesHeldAtOnce`. Asserting that is **1** catches a missed hand-off in
milliseconds, instead of as a 60-second hang on hardware. Every sequence made atomic has such a test;
add one for any you add.

This matters more than it looks. The three `SynthParam` conversations run inside the throttled writer's
subscriber, which catches and logs — so a missed hand-off there would be *swallowed* after 60 seconds
rather than freezing anything. The test is the thing that catches it.

---

## Where everything lives

| file | what it is |
|---|---|
| `Src/Models/Services/MidiPort.cs` | The port, the lease, the timeouts. The whole mechanism. |
| `Src/Models/Services/ReplyMatchers.cs` | What each conversation accepts as its reply. Pure. |
| `Src/Models/Services/AsyncMidiInputWrapper.cs` | A lease's reader: matches replies, keeps everything else. |
| `Src/Models/Services/Integra7Api.cs` | Every device operation, and `Borrowed` / `LeaseAsync`. |
| `Src/Models/Services/MidiIn.cs` | `DispatchUnsolicited` — where unrequested messages go. |
| `Src/Models/Domain/DomainBase.cs` | The domain-layer path, and `BeginConversationAsync`. |

Related but separate: `Src/Models/Services/PartLoadState.cs` governs when a *part* is loaded, not who
owns the port. The two interact (a part load is a long conversation) but are independent designs.

The specs and plans behind this work are under `docs/superpowers/specs/` and
`docs/superpowers/plans/`, dated 2026-07-19. They record why each decision was made, including the
ones that were reversed.

---

## Known limits

- **Rescanning MIDI devices** builds a second port while the first may be mid-conversation. In practice
  the Rescan button is disabled while connected to hardware, so this is not reachable from the UI.
- **The legacy synchronous reply path** — `AnnounceIntentionToManuallyHandleReply`,
  `RestoreAutomaticHandling`, `GetReply` on `MidiIn` — has no production caller and is a candidate for
  deletion.
- **A parent-parameter edit holds the port longer than a plain one**, because it covers write → reset →
  a full domain re-read. That is what atomicity costs here. If it ever feels sluggish, narrow the
  conversation rather than widening the port.
