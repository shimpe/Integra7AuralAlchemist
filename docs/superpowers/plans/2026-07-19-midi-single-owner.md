# Single owner for MIDI device access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make one object own the MIDI port, handing out a lease that a conversation holds for its whole duration, so a multi-message sequence cannot be split and bypassing the lock stops being expressible.

**Architecture:** A `MidiPort` owns the `IMidiIn`/`IMidiOut` handles and hands out an `IMidiLease` via `AcquireAsync(what)`. Only the lease exposes send and request. A lease installs one reader for its lifetime, accumulates messages that were not its replies, and dispatches them on release — after the port is free. There is no reentrancy: a lease is never acquired while one is held, enforced by public methods acquiring and private helpers taking a lease parameter.

**Tech Stack:** .NET 10, C# 13, managed-midi 1.10.1, Serilog, NUnit.

**Spec:** `docs/superpowers/specs/2026-07-19-midi-single-owner-design.md`

---

## Domain background (read this first)

This application edits a Roland INTEGRA-7 hardware synthesizer over MIDI sysex.

```
f0 41 <dev> 00 00 64 <cmd> <addr0..3> <data...> <checksum> f7
 0   1   2   3  4  5    6     7..10
```

A read sends an RQ1 (command `0x11`) naming a 4-byte address; the device answers with a DT1 (command
`0x12`) at that address. Byte 2 is the device ID and varies.

**What already exists** (do not rebuild it):

- `Src/Models/Services/ReplyMatchers.cs` — `IReplyMatcher` with `Matches(byte[])` and `Describe()`,
  plus `DataSetAt(address)`, `IdentityReply`, `NameListReply(address)`.
- `Src/Models/Services/AsyncMidiInputWrapper.cs` — takes `(IMidiIn, IReplyMatcher)`, installs itself as
  the single MIDI input handler, returns only a matching reply, and keeps everything else in
  `TakeDeferred()`.
- `Src/Models/Services/MidiIn.cs` — `IMidiIn` with `ConfigureHandler`, `ConfigureDefaultHandler`,
  `RemoveHandler`, `DispatchUnsolicited`, `GetReply`, `AnnounceIntentionToManuallyHandleReply`,
  `RestoreAutomaticHandling`.
- `Src/Models/Services/MidiOut.cs` — `IMidiOut` with `ConnectionOk()` and `SafeSend(byte[])`.

**The problem this fixes.** A `SemaphoreSlim` serialises individual requests. Sending has two doors:
`AsyncMidiOutputWrapper.SafeSendAsync` (takes the semaphore, per message) and `IMidiOut.SafeSend`
(takes nothing). `ChangePresetAsync` uses the second, three times, so its bank-select and
program-change messages can be split by another flow's request.

## Build and test commands

The system `dotnet` on this machine is version 8/9 and **too old** for this solution. Always use the
user-local SDK:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

If a build fails with MSB3027 / MSB3021 ("cannot copy … being used by another process"), a running app
instance or a Rider XAML previewer holds the output files. **Report it — do not kill the process**, it
belongs to the user.

If `git commit` fails with "Permission denied" writing to `.git/objects`, that is a transient
anti-virus lock. Wait a moment and retry the same command.

## Conventions

- Never pass `--no-verify` to `git commit`.
- Every commit message ends with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work on the branch `midi-single-owner`, which already exists and holds the spec commit. Do not merge
  to `main`; the user does that.
- Tests: `Tests/TestXxx.cs`, `namespace Tests;`, NUnit `[TestFixture]`, behavioural method names. See
  `Tests/TestAsyncMidiInputWrapper.cs` — it has a `FakeMidiIn` to model on.

## File structure

| File | Responsibility |
|---|---|
| `Src/Models/Services/MidiPort.cs` (create) | `IMidiPort`, `IMidiLease`, `MidiPort` and its private lease. Owns the in/out handles and the exclusion. |
| `Tests/TestMidiPort.cs` (create) | Exclusivity, disposal, drain-on-release, the long-hold warning and the hard timeout. |
| `Src/Models/Services/Integra7Api.cs` (modify) | Every device method becomes acquire-converse-release. |
| `Src/Models/Services/AsyncMidiOutputWrapper.cs` (delete) | Subsumed by the port. |
| `Src/Models/Services/MidiHandlerOwnership.cs` (delete) | Subsumed: a single owner makes the desync it guards impossible. |
| ~36 files (modify) | Remove the dead `SemaphoreSlim` constructor parameter. |

---

### Task 1: The port and the lease

**Files:**
- Create: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

**There is no reentrancy.** An earlier draft tracked the current flow's lease in an `AsyncLocal` so a
nested `AcquireAsync` could reuse it. That cannot work: a write to an `AsyncLocal` inside an `async`
method is not visible to the caller — `ExecutionContext` flows down, never back up — and `AcquireAsync`
must be async because it awaits the gate. Verified on this runtime:

```
after async method: 'null'        // write inside an async method, lost to the caller
after sync method:  'from-sync'   // write inside a sync method, visible
```

So the rule is: **a lease is never acquired while one is held.** Later tasks enforce it structurally —
public `Integra7Api` methods acquire, private helpers take an `IMidiLease` parameter, so a missed
hand-off is a compile error. Task 4 adds a timeout as the escape hatch for a nested acquire written
anyway.

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestMidiPort.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One object owns the port. A conversation holds it for its whole duration, so a sequence
/// of messages that must arrive together cannot be split by another flow's request.</summary>
[TestFixture]
public class TestMidiPort
{
    private sealed class FakeMidiOut : IMidiOut
    {
        public List<byte[]> Sent { get; } = [];
        public bool ConnectionOk() => true;
        public void SafeSend(byte[] data) => Sent.Add(data);
    }

    private sealed class FakeMidiIn : IMidiIn
    {
        private EventHandler<MidiReceivedEventArgs>? _handler;

        public List<byte[]> Dispatched { get; } = [];
        public bool HandlerInstalled => _handler is not null;

        public void Push(byte[] message) =>
            _handler?.Invoke(this, new MidiReceivedEventArgs
            {
                Data = message, Start = 0, Length = message.Length, Timestamp = 0
            });

        public void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler) => _handler = handler;
        public void ConfigureDefaultHandler() => _handler = null;
        public void RemoveHandler(EventHandler<MidiReceivedEventArgs> handler) => _handler = null;
        public void DispatchUnsolicited(byte[] message) => Dispatched.Add(message);
        public byte[] GetReply() => [];
        public void AnnounceIntentionToManuallyHandleReply() { }
        public void RestoreAutomaticHandling() { }
    }

    private static MidiPort NewPort(out FakeMidiIn midiIn, out FakeMidiOut midiOut)
    {
        midiIn = new FakeMidiIn();
        midiOut = new FakeMidiOut();
        return new MidiPort(midiOut, midiIn);
    }

    [Test]
    public async Task AConversationGetsThePortToItself()
    {
        var port = NewPort(out _, out var midiOut);

        await using (var lease = await port.AcquireAsync("first"))
        {
            await lease.SendAsync([0x01]);
            await lease.SendAsync([0x02]);
        }

        Assert.That(midiOut.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ASecondConversationWaitsForTheFirstToFinish()
    {
        var port = NewPort(out _, out _);

        var first = await port.AcquireAsync("first");
        var second = port.AcquireAsync("second");

        Assert.That(second.IsCompleted, Is.False, "the port is taken; the second acquire must wait");

        await first.DisposeAsync();

        Assert.That(await Task.WhenAny(second, Task.Delay(3000)), Is.SameAs(second),
            "releasing the port must let the waiting conversation proceed");
        await (await second).DisposeAsync();
    }

    [Test]
    public async Task ConversationsRunOneAtATime()
    {
        // Two conversations racing: whatever order they get the port in, their sends must not
        // interleave. This is the whole point -- a bank select and its program change must arrive
        // together.
        var port = NewPort(out _, out var midiOut);

        async Task Converse(string what, byte tag)
        {
            await using var lease = await port.AcquireAsync(what);
            await lease.SendAsync([tag, 1]);
            await Task.Delay(20);
            await lease.SendAsync([tag, 2]);
        }

        await Task.WhenAll(Converse("a", 0xa0), Converse("b", 0xb0));

        Assert.That(midiOut.Sent, Has.Count.EqualTo(4));
        Assert.That(midiOut.Sent[0][0], Is.EqualTo(midiOut.Sent[1][0]),
            "the first conversation's two sends must be adjacent, not interleaved");
        Assert.That(midiOut.Sent[2][0], Is.EqualTo(midiOut.Sent[3][0]));
    }

    [Test]
    public async Task AConversationThatThrowsStillReleasesThePort()
    {
        var port = NewPort(out _, out _);

        try
        {
            await using var lease = await port.AcquireAsync("throws");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
        }

        var next = port.AcquireAsync("next");
        Assert.That(await Task.WhenAny(next, Task.Delay(3000)), Is.SameAs(next));
        await (await next).DisposeAsync();
    }

    [Test]
    public async Task AReleasedLeaseCannotBeUsed()
    {
        // A fire-and-forget task started inside a conversation can outlive it. Throwing makes that
        // loud instead of letting it write onto a port someone else now owns.
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("done");
        await lease.DisposeAsync();

        Assert.That(async () => await lease.SendAsync([0x01]), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task DisposingTwiceIsHarmless()
    {
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("first");
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        // A second dispose must not release the gate again -- that would let two conversations in.
        var a = await port.AcquireAsync("a");
        var b = port.AcquireAsync("b");

        Assert.That(b.IsCompleted, Is.False, "the gate was released twice; two conversations got in");

        await a.DisposeAsync();
        await (await b).DisposeAsync();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestMidiPort"
```

Expected: build failure, `CS0246: The type or namespace name 'MidiPort' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/MidiPort.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestMidiPort"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

Then the full suite:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 393` (387 existing plus 6 new).

**If any test hangs rather than failing**, stop and report which. A hang means something acquires while
holding, which this design forbids.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/MidiPort.cs Tests/TestMidiPort.cs
git commit -m "feat: give the MIDI port a single owner and a per-conversation lease

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 3: Requests, the lease's reader, and the drain on release

**Files:**
- Modify: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

A lease installs **one** reader for its lifetime — not one per request. Messages that are not a reply
accumulate on the lease and dispatch once, on release, after the port is free.

- [ ] **Step 1: Write the failing tests**

Append inside the `TestMidiPort` class in `Tests/TestMidiPort.cs`, before its closing brace:

```csharp
    private static readonly byte[] Address = [0x0f, 0x00, 0x04, 0x02];

    private static byte[] Reply() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x00, 0x6b, 0xf7
    ];

    private static byte[] PanelChange() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x03, 0x02, 0x01, 0x0e, 0xf7
    ];

    [Test]
    public async Task ARequestReturnsItsOwnReply()
    {
        var port = NewPort(out var midiIn, out var midiOut);

        await using var lease = await port.AcquireAsync("read");
        var request = lease.RequestAsync([0x11], ReplyMatchers.DataSetAt(Address));
        midiIn.Push(Reply());

        Assert.That(await Task.WhenAny(request, Task.Delay(3000)), Is.SameAs(request));
        Assert.That(await request, Is.EqualTo(Reply()));
        Assert.That(midiOut.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task MessagesThatWereNotTheReplyDispatchWhenTheLeaseIsReleased()
    {
        var port = NewPort(out var midiIn, out _);

        var lease = await port.AcquireAsync("read");
        var request = lease.RequestAsync([0x11], ReplyMatchers.DataSetAt(Address));
        midiIn.Push(PanelChange());
        midiIn.Push(Reply());
        await request;

        Assert.That(midiIn.Dispatched, Is.Empty, "not while the port is still held");

        await lease.DisposeAsync();

        Assert.That(midiIn.Dispatched, Is.EqualTo(new[] { PanelChange() }));
    }

    [Test]
    public async Task OneReaderCoversTheWholeConversation()
    {
        // The burst reads many replies to one request. A lease that installed a reader per request
        // would hand the port back between them and miss the rest.
        var port = NewPort(out var midiIn, out _);

        await using var lease = await port.AcquireAsync("burst");
        var first = lease.RequestAsync([0x11], ReplyMatchers.DataSetAt(Address));
        midiIn.Push(Reply());
        await first;

        Assert.That(midiIn.HandlerInstalled, Is.True, "the conversation is not over");

        var second = lease.ReadNextAsync(ReplyMatchers.DataSetAt(Address));
        midiIn.Push(Reply());

        Assert.That(await Task.WhenAny(second, Task.Delay(3000)), Is.SameAs(second));
        Assert.That(await second, Is.EqualTo(Reply()));
    }

    [Test]
    public async Task TheReaderIsHandedBackWhenTheLeaseIsReleased()
    {
        var port = NewPort(out var midiIn, out _);

        var lease = await port.AcquireAsync("read");
        var request = lease.RequestAsync([0x11], ReplyMatchers.DataSetAt(Address));
        midiIn.Push(Reply());
        await request;
        await lease.DisposeAsync();

        Assert.That(midiIn.HandlerInstalled, Is.False);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestMidiPort"
```

Expected: build failure — `IMidiLease` has no `RequestAsync` or `ReadNextAsync`.

- [ ] **Step 3: Write the implementation**

In `Src/Models/Services/MidiPort.cs`, add to the `IMidiLease` interface:

```csharp
    /// <summary>Send a request and wait for the reply <paramref name="expected"/> recognises. Anything
    /// else that arrives is kept and dispatched when the lease is released.</summary>
    Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected);

    /// <summary>Wait for another reply to a request already sent -- the burst case.</summary>
    Task<byte[]> ReadNextAsync(IReplyMatcher expected);
```

and to the `Lease` class:

```csharp
        /// <summary>This conversation's reader, installed once and kept for the whole lease. A reader
        /// per request would hand the port back between the replies of a burst.</summary>
        private AsyncMidiInputWrapper? _reader;

        private readonly List<byte[]> _deferred = [];

        public async Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected)
        {
            ThrowIfReleased();
            var reader = Reader(expected);
            await Task.Run(() => _port._midiOut.SafeSend(request));
            return await ReadFrom(reader, expected);
        }

        public async Task<byte[]> ReadNextAsync(IReplyMatcher expected)
        {
            ThrowIfReleased();
            return await ReadFrom(Reader(expected), expected);
        }

        private AsyncMidiInputWrapper Reader(IReplyMatcher expected) =>
            _reader ??= new AsyncMidiInputWrapper(_port._midiIn, expected);

        private async Task<byte[]> ReadFrom(AsyncMidiInputWrapper reader, IReplyMatcher expected)
        {
            var reply = await reader.WaitForAsync(expected);
            _deferred.AddRange(reader.TakeDeferred());
            return reply;
        }
```

and change `DisposeAsync` so it hands the reader back and drains:

```csharp
        public async ValueTask DisposeAsync()
        {
            if (Released) return;
            if (--_depth > 0) return;

            Released = true;
            _reader?.CleanupAfterTimeOut();
            var deferred = _deferred.ToArray();
            _deferred.Clear();
            _port.Release(this);

            // After the release, deliberately: a deferred message must reach the UI with the port
            // actually free, because a resync it triggers will take the port.
            foreach (var m in deferred)
            {
                // Guarded per message: one malformed deferred message must not strand the rest.
                try
                {
                    _port._midiIn.DispatchUnsolicited(m);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Failed to dispatch a message deferred during '{What}'.", _what);
                }
            }
        }
```

This needs one new method on `AsyncMidiInputWrapper`, because a lease's reader must be reusable across
several requests with a *different* matcher each time. In
`Src/Models/Services/AsyncMidiInputWrapper.cs`, add:

```csharp
    /// <summary>Wait for a message <paramref name="expected"/> recognises, keeping anything else. The
    /// matcher is per call rather than per reader: one lease reads several different replies through
    /// the same installed reader.</summary>
    public Task<byte[]> WaitForAsync(IReplyMatcher expected)
    {
        _expected = expected;
        return WaitForMatchingMessageAsync(handBackPort: false);
    }
```

and change the `_expected` field from `readonly` to a plain field so it can be reassigned:

```csharp
    private IReplyMatcher _expected;
```

- [ ] **Step 4: Run the tests**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestMidiPort"
```

Expected: `Passed! - Failed: 0, Passed: 10`.

Then the whole suite, which must still be green — `AsyncMidiInputWrapper`'s existing tests exercise the
old entry points and must be unaffected:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 397` (387 existing plus 10 new).

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/MidiPort.cs Src/Models/Services/AsyncMidiInputWrapper.cs Tests/TestMidiPort.cs
git commit -m "feat: read through the lease, and drain what it kept on release

One reader covers a whole conversation, so the burst needs no special case, and
messages the reads were not waiting for dispatch once -- after the port is free.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Say who is holding the port, and give up eventually

**Files:**
- Modify: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

Every hang chased during this work presented as a frozen UI with no indication of who held what.

Two thresholds, and the difference matters. A wait that runs long is **warned** about but keeps
waiting: the name burst legitimately holds the port for several seconds, and a part load queued behind
it is waiting correctly, not deadlocked. A wait that runs absurdly long **throws**, because at that
point something acquired while holding — the one thing this design forbids — and hanging forever is
the failure mode the whole redesign exists to remove.

Add the hard timeout alongside the warning, with this test:

```csharp
    [Test]
    public async Task AWaitThatRunsAbsurdlyLongGivesUpAndSaysWho()
    {
        // Only reachable if something acquired while already holding, which the design forbids.
        // Hanging forever is what this whole redesign exists to stop, so give up and name both sides.
        var port = NewPort(out _, out _);
        port.SlowAcquireThreshold = TimeSpan.FromMilliseconds(50);
        port.AcquireTimeout = TimeSpan.FromMilliseconds(200);

        await using var holder = await port.AcquireAsync("user tone names 448-511");

        var boom = Assert.ThrowsAsync<TimeoutException>(async () =>
            await port.AcquireAsync("part 6 load"));

        Assert.That(boom!.Message, Does.Contain("part 6 load"));
        Assert.That(boom.Message, Does.Contain("user tone names 448-511"));
    }
```

`AcquireTimeout` defaults to `TimeSpan.FromSeconds(60)` — far beyond any legitimate wait, including a
full eighteen-request name burst.

- [ ] **Step 1: Write the failing test**

Append inside `TestMidiPort`:

```csharp
    [Test]
    public async Task WaitingTooLongForThePortNamesBothConversations()
    {
        var port = NewPort(out _, out _);
        var warnings = new List<string>();
        port.OnSlowAcquire = (waiting, holder, heldFor) =>
            warnings.Add($"{waiting}|{holder}|{heldFor.TotalMilliseconds >= 0}");

        port.SlowAcquireThreshold = TimeSpan.FromMilliseconds(100);

        var holder = await port.AcquireAsync("user tone names 448-511");
        var waiter = port.AcquireAsync("part 6 load");
        await Task.Delay(300);
        await holder.DisposeAsync();
        await (await waiter).DisposeAsync();

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Is.EqualTo("part 6 load|user tone names 448-511|True"));
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~WaitingTooLongForThePortNamesBothConversations"
```

Expected: build failure — `MidiPort` has no `OnSlowAcquire` or `SlowAcquireThreshold`.

- [ ] **Step 3: Write the implementation**

In `Src/Models/Services/MidiPort.cs`, add these fields to `MidiPort`:

```csharp
    /// <summary>How long a conversation may wait for the port before the wait is reported. Settable so
    /// a test does not have to wait out the real threshold.</summary>
    public TimeSpan SlowAcquireThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a conversation may wait before the wait is treated as a bug rather than a
    /// queue. Far beyond any legitimate wait -- a full eighteen-request name burst is seconds, not
    /// minutes -- so reaching it means something acquired while already holding.</summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Called when a conversation waits longer than the threshold: (waiting, holder, heldFor).
    /// Overridable so a test can observe it; it logs by default.</summary>
    public Action<string, string, TimeSpan>? OnSlowAcquire { get; set; }

    private string _holder = "nobody";
    private DateTime _heldSince = DateTime.UtcNow;
```

and rewrite `AcquireAsync` to time the wait:

```csharp
    public async Task<IMidiLease> AcquireAsync(string what)
    {
        if (!await _gate.WaitAsync(SlowAcquireThreshold))
        {
            // Name both sides. Every hang chased over this work looked like a frozen UI with no
            // indication of who was holding the port and who was waiting for it.
            var holder = _holder;
            var heldFor = DateTime.UtcNow - _heldSince;
            if (OnSlowAcquire is not null) OnSlowAcquire(what, holder, heldFor);
            else
                Log.Warning("Waiting {Waited:0}s to acquire the MIDI port for '{What}' -- held by " +
                            "'{Holder}' for {HeldFor:0}s.", SlowAcquireThreshold.TotalSeconds, what,
                    holder, heldFor.TotalSeconds);

            if (!await _gate.WaitAsync(AcquireTimeout - SlowAcquireThreshold))
                throw new TimeoutException(
                    $"Gave up after {AcquireTimeout.TotalSeconds:0}s waiting for the MIDI port for " +
                    $"'{what}' -- held by '{holder}'. Something acquired the port while already " +
                    "holding it, which is not supported: public methods acquire, helpers take a lease.");
        }

        _holder = what;
        _heldSince = DateTime.UtcNow;
        return new Lease(this, what);
    }
```

and set the holder back in `Release`:

```csharp
    private void Release()
    {
        _holder = "nobody";
        _gate.Release();
    }
```

- [ ] **Step 4: Run the tests**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 398`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/MidiPort.cs Tests/TestMidiPort.cs
git commit -m "feat: report who is holding the MIDI port when a wait runs long

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Move Integra7Api onto the port

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs`
- Modify: `Src/ViewModels/MainWindowViewModel.cs:26`, `:159`, `:654`
- Delete: `Src/Models/Services/AsyncMidiOutputWrapper.cs`

This is the task that makes the claim true. `Integra7Api` stops holding `IMidiOut`/`IMidiIn` and holds
an `IMidiPort` instead, so `SafeSend` becomes unreachable from it.

- [ ] **Step 1: Change the constructor and fields**

In `Src/Models/Services/Integra7Api.cs`, replace:

```csharp
    private readonly SemaphoreSlim _semaphore;
    private byte _deviceId;
    private IMidiIn? _midiIn;
    private IMidiOut? _midiOut;

    public Integra7Api(IMidiOut midiOut, IMidiIn midiIn, SemaphoreSlim semaphore)
    {
        _midiOut = midiOut;
        _midiIn = midiIn;
        _semaphore = semaphore;
    }
```

with:

```csharp
    private readonly IMidiPort _port;
    private byte _deviceId;

    /// <summary>Cleared when the identity check fails, which is how the rest of the application learns
    /// there is no usable device. The port itself outlives that.</summary>
    private bool _connected = true;

    public Integra7Api(IMidiPort port)
    {
        _port = port;
    }
```

Every `_midiOut = null; _midiIn = null;` becomes `_connected = false;`, and `ConnectionOk()` becomes:

```csharp
    public bool ConnectionOk() => _connected;
```

- [ ] **Step 2: Rewrite `RunRequestAsync` over the lease**

Replace the whole `RunRequestAsync` method with:

```csharp
    /// <summary>One request and its reply, as a conversation of its own. Callers that need several
    /// requests to be atomic take a lease themselves and the nested acquire here reuses it.</summary>
    private async Task<byte[]> RunRequestAsync(byte[] request, IReplyMatcher expected, string what)
    {
        await using var port = await _port.AcquireAsync(what);
        var reply = await port.RequestAsync(request, expected);
        if (reply.Length == 0)
            Log.Error("Timeout waiting for MIDI reply: {What}, expecting {Expected}.", what,
                expected.Describe());

        return reply;
    }
```

The drain, the semaphore and the `_midiIn` capture all disappear — the lease does them.

- [ ] **Step 3: Rewrite the write methods**

Replace each of these methods so it takes a lease instead of constructing an `AsyncMidiOutputWrapper`:

```csharp
    public async Task MakeDataTransmissionAsync(byte[] address, byte[] data)
    {
        var transmission = Integra7SysexHelpers.MakeDataSet(DeviceId(), address, data);
        await using var port = await _port.AcquireAsync("parameter write");
        await port.SendAsync(transmission);
    }

    public async Task NoteOnAsync(byte Channel, byte Note, byte Velocity)
    {
        byte[] data = [(byte)(Integra7MidiControlNos.NoteOn + Channel), Note, Velocity];
        await using var port = await _port.AcquireAsync("note on");
        await port.SendAsync(data);
    }

    public async Task NoteOffAsync(byte Channel, byte Note)
    {
        byte[] data = [(byte)(Integra7MidiControlNos.NoteOff + Channel), Note, 0];
        await using var port = await _port.AcquireAsync("note off");
        await port.SendAsync(data);
    }

    public async Task AllNotesOffAsync()
    {
        // One lease for all sixteen: a read serviced between the messages for parts 3 and 4 used to be
        // possible, because each send acquired separately.
        await using var port = await _port.AcquireAsync("all notes off");
        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            byte[] data = [(byte)(Integra7MidiControlNos.AllNotesOff + i), 0x7C, 0x00];
            await port.SendAsync(data);
        }
    }

    public async Task SendStopPreviewPhraseMsgAsync()
    {
        var stop = Integra7SysexHelpers.MakeStopPreviewPhraseMsg(_deviceId);
        await using var port = await _port.AcquireAsync("stop preview phrase");
        await port.SendAsync(stop);
    }

    public async Task SendPlayPreviewPhraseMsgAsync(byte channel)
    {
        var start = Integra7SysexHelpers.MakePlayPreviewPhraseMsg(channel, _deviceId);
        await using var port = await _port.AcquireAsync("play preview phrase");
        await port.SendAsync(start);
    }

    public async Task SendLoadSrxAsync(byte srx_slot1, byte srx_slot2, byte srx_slot3, byte srx_slot4)
    {
        var msg = Integra7SysexHelpers.MakeLoadSrxMsg(srx_slot1, srx_slot2, srx_slot3, srx_slot4, _deviceId);
        await using var port = await _port.AcquireAsync("load SRX");
        await port.SendAsync(msg);
    }
```

- [ ] **Step 4: Rewrite `ChangePresetAsync` — the one with no lock at all**

`BankSelectMsb`, `BankSelectLsb` and `ProgramChange` currently call `_midiOut?.SafeSend` directly.
Change each to *build and return* its message instead of sending it, then send all three under one
lease. Replace `ChangePresetAsync` and those three helpers with:

```csharp
    public async Task ChangePresetAsync(byte Channel, int Msb, int Lsb, int Pc)
    {
        // These three must arrive consecutively on the same channel. They used to be sent with no lock
        // at all, so another flow's request could land between the bank select and the program change.
        await using (var port = await _port.AcquireAsync("preset change"))
        {
            if (BankSelectMsb(Channel, Msb) is { } msb) await port.SendAsync(msb);
            if (BankSelectLsb(Channel, Lsb) is { } lsb) await port.SendAsync(lsb);
            await port.SendAsync(ProgramChange(Channel, Pc - 1));
        }

        // Posted after the port is free: the subscriber takes a lease, and would otherwise block until
        // this conversation ended.
        MessageBus.Current.SendMessage(new UpdateResyncPart(Channel));
    }

    private static byte[]? BankSelectMsb(byte Channel, int BankNumberMsb)
    {
        ISet<int> PossibleBankMsb = new HashSet<int> { 85, 86, 87, 88, 89, 92, 93, 95, 96, 97, 120, 121 };
        if (!PossibleBankMsb.Contains(BankNumberMsb)) return null;

        return [(byte)(MidiEvent.CC + Channel), 0, (byte)BankNumberMsb];
    }
```

and the other two, whose byte layouts and guards are preserved exactly — only the send becomes a
return. Note `BankSelectLsb` throws for an out-of-range value rather than skipping, unlike
`BankSelectMsb`; keep that difference:

```csharp
    private static byte[] BankSelectLsb(byte Channel, int BankNumberLsb)
    {
        if (0 <= BankNumberLsb && BankNumberLsb <= 127)
            return [(byte)(MidiEvent.CC + Channel), 0x20, (byte)BankNumberLsb];

        throw new MidiException("Trying to select impossible LSB BankNumber: " + BankNumberLsb);
    }

    private static byte[] ProgramChange(byte Channel, int ProgramNumber) =>
        [(byte)(MidiEvent.Program + Channel), (byte)ProgramNumber];
```

Because `BankSelectLsb` now always returns a value or throws, the `ChangePresetAsync` body above should
read `await port.SendAsync(BankSelectLsb(Channel, Lsb));` rather than the `is { } lsb` pattern — only
`BankSelectMsb` is nullable.

- [ ] **Step 5: `WriteToneToUserMemory` — one lease per step, NOT one for the method**

**Do not wrap this method in a single lease.** That is the obvious move and it would hang.

Its step 2 calls `ChangePresetNameAsync`, which writes out through `Integra7Domain` → `DomainBase` →
`FullyQualifiedParameter` and re-enters `MakeDataTransmissionAsync` — which acquires. Holding a lease
across that is a nested acquire, which this design does not support: the inner acquire would block on
the gate the outer conversation holds, until the sixty-second timeout throws.

So each *send* takes and releases its own lease. Replace every

```csharp
                var w = new AsyncMidiOutputWrapper(_midiOut, _semaphore);
                await w.SafeSendAsync(msg);
```

with

```csharp
                await using (var port = await _port.AcquireAsync("write tone to user memory"))
                {
                    await port.SendAsync(msg);
                }
```

and leave `ChangePresetNameAsync` (step 2) alone — it reaches the port through
`MakeDataTransmissionAsync`, which acquires for itself.

This method therefore stays **non-atomic**, and the race it leaves open is real: step 1 selects the new
patch, so a read landing between steps 1 and 3 reads the new patch, and a preset change there writes
the wrong name to the wrong slot. Closing it needs a lease threaded through the domain layer, which is
piece C. Add this comment above the method so the next reader does not "fix" it by wrapping:

```csharp
    /// <summary>NOT atomic, deliberately. Step 2 writes the name out through the domain layer, which
    /// re-enters MakeDataTransmissionAsync and acquires the port for itself -- so holding a lease
    /// across these three steps would deadlock against ourselves. Making it one conversation needs a
    /// lease threaded through Integra7Domain and FullyQualifiedParameter, which is a separate piece of
    /// work.</summary>
```

- [ ] **Step 6: Move the burst onto the lease**

`GetListOfNamesHelper` and `GatherNamesAsync` collapse back into one method: the split existed only so
the drain could run after the semaphore was released, and the lease does that now.

Replace both with a single `GetListOfNamesHelper` whose opening is:

```csharp
    private async Task<List<string>> GetListOfNamesHelper(byte[] msg)
    {
        // Replies carry back the address they answer. Taking it from the request rather than
        // assuming one keeps this helper correct for every name list -- Studio Set names are
        // requested at 0f 00 03 02, where the tone lists use 0f 00 04 02.
        var expectedAddress = NameListEndMarker.AddressOf(msg);
        var expected = ReplyMatchers.NameListReply(expectedAddress);

        // Named for the address, so the long-hold warning says which burst is holding the port.
        await using var port =
            await _port.AcquireAsync($"name list at {BitConverter.ToString(expectedAddress)}");

        List<byte[]> allReplies = [];
        var totalRepliesReceived = 0;
        var localReply = await port.RequestAsync(msg, expected);
        var continueReading = true;
        while (continueReading)
        {
```

Inside the loop, the only change to the existing body is that the next read comes from the lease:

```csharp
                localReply = await port.ReadNextAsync(expected);
```

Keep the rest of the loop exactly as it is — the `SplitAfterF7` split, the `r is null` guard, the
`IsNameListReply` filter, the `IsEndOfBurst` stop, the dropped-message warning, and the name slicing
after the loop. Note the first read now happens *before* the loop (it was `RequestAsync`, sending the
request), and subsequent reads happen at the *end* of each iteration; restructure the loop's control
flow to match, and keep the total-timeout branch that returns an empty list when
`totalRepliesReceived == 0`.

Delete the `mi.CleanupAfterTimeOut()` calls: the lease hands the reader back on release. The
`continueReading = false` that accompanied them stays.

- [ ] **Step 7: Update the construction sites**

In `Src/ViewModels/MainWindowViewModel.cs`, delete the `SemaphoreSlim` field at `:26` and construct a
port instead. At `:159` and `:654`, replace `new Integra7Api(new MidiOut(...), new MidiIn(...), _semaphore)`
with `new Integra7Api(new MidiPort(new MidiOut(...), new MidiIn(...)))`. Read those two lines first —
they pass device names that must be preserved.

- [ ] **Step 8: Delete the output wrapper**

```bash
git rm Src/Models/Services/AsyncMidiOutputWrapper.cs
```

- [ ] **Step 9: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 398`. `Tests/TestFailedReadKeepsValues.cs` implements
`IIntegra7Api` and may need no change, since the interface is unchanged — check.

- [ ] **Step 10: Verify nothing can bypass the port**

```bash
grep -rn "SafeSend\|AsyncMidiInputWrapper\|AsyncMidiOutputWrapper" Src/ --include=*.cs
```

Expected: `SafeSend` only in `MidiOut.cs` (its definition) and `MidiPort.cs` (the only caller);
`AsyncMidiInputWrapper` only in its own file and `MidiPort.cs`; `AsyncMidiOutputWrapper` nowhere. If
`Integra7Api.cs` still appears in that list, a send escaped the migration.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "refactor: route every device conversation through the port

Integra7Api no longer holds the in and out handles, so a send outside a lease is
no longer expressible. The preset change, all-notes-off and the three-step user
tone write each become one conversation instead of one lease per message.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Delete the dead semaphore

**Files:** ~36 — `Src/Models/Domain/*.cs`, `Src/ViewModels/PartViewModel.cs`, `Src/ViewModels/PartialViewModel.cs`, the four partial view models.

- [ ] **Step 1: Re-verify the claim before deleting**

```bash
grep -rn "_semaphore\." Src/ --include=*.cs
```

Expected after Task 5: **nothing**. Before Task 5 it returned six lines, all in `Integra7Api.cs` and
`AsyncMidiOutputWrapper.cs`. If any other file appears, that file really does use the semaphore and
this task's premise is wrong — stop and report.

- [ ] **Step 2: Remove the parameter and fields**

Remove the `SemaphoreSlim semaphore` constructor parameter from `Integra7Domain`, `DomainBase`, every
`Domain*` class, `PartViewModel`, `PartialViewModel`, `PCMSynthTonePartialViewModel`,
`PCMDrumKitPartialViewModel`, `SNSynthTonePartialViewModel` and `SNDrumKitPartialViewModel`, along with
the unused `_semaphore` fields in `DomainBase`, `PartViewModel` and `PartialViewModel`, and update
every construction site the compiler flags.

Work compiler-error-driven: build, fix the reported call sites, build again. Do not hand-edit files the
compiler has not flagged.

- [ ] **Step 3: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 398`.

- [ ] **Step 4: Delete the ownership guard**

`MidiHandlerOwnership` exists to catch a reader finding that another reader owns the port — which a
single owner makes impossible.

```bash
git rm Src/Models/Services/MidiHandlerOwnership.cs Tests/TestMidiHandlerOwnership.cs
```

Then remove its use in `Src/Models/Services/MidiIn.cs:104` — `RemoveHandler` should simply restore the
default handler when the handler being removed is the installed one.

Rebuild and re-test. Expected: `Failed: 0, Passed: 395` (398 minus the three ownership tests — confirm
the actual count of tests in `TestMidiHandlerOwnership.cs` and report it).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: delete the semaphore nothing was using

It was a constructor parameter on the domain classes and the part view models,
none of which ever took it -- only Integra7Api did, and the port owns that now.
MidiHandlerOwnership goes too: it guarded a request/reply desync that a single
owner makes impossible.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Hardware verification

**Files:** none — this task runs the application against the real INTEGRA-7.

**Stop and hand this to the user.** Do not mark the plan complete without it.

- [ ] **Step 1: Confirm no other instance is running**

```bash
tasklist | grep -i integra7
```

Expected: no rows. If there are, the user must close that instance — do not kill it.

- [ ] **Step 2: Launch and collect the log**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj
```

Log: `Src/bin/Debug/net10.0/logs/I7AuralAlchemist<YYYYMMDD>.log`.

- [ ] **Step 3: Run the five scenarios**

1. **Change a preset.** The bank-select and program-change messages are not split; the log shows one
   `preset change` conversation.
2. **Save a user tone.** The three-step write holds one lease start to finish, and the saved tone has
   the right name in the right slot.
3. **Panic (all notes off) during a part load.** Sixteen messages under one lease, and the load
   resumes afterwards.
4. **Startup with no interaction.** No acquire warnings, and user preset names load.
5. **Open a part while the startup name burst is running.** Expect the long-hold warning naming both
   conversations — `held by 'name list at 0F-00-04-02'`. This is the positive test of the new
   diagnostic and the first time the application says out loud what it is waiting for.

- [ ] **Step 4: Report**

Report each scenario with the relevant log lines. A hang here is the failure mode to watch: if the UI
freezes, the log should now name the holder, which is the information every previous hang lacked.

---

## Notes for the implementer

- **Never acquire while holding.** There is no reentrancy — an `AsyncLocal` cannot carry the lease,
  because a write to one inside an `async` method is invisible to the caller. The rule is enforced by
  shape: public `Integra7Api` methods acquire, private helpers take an `IMidiLease` parameter. If you
  find yourself wanting to acquire inside a method that already has a lease in scope, pass the lease
  instead.
- **The trap is a path that leaves `Integra7Api` and comes back.** `WriteToneToUserMemory`'s step 2
  writes through `Integra7Domain` and re-enters `MakeDataTransmissionAsync`. Holding a lease across it
  deadlocks. Task 5 Step 5 spells this out; do not "tidy" it into one lease.
- **A `TimeoutException` from the port means exactly that mistake.** It names the holder and the
  waiter. Report both rather than raising the timeout.
- **A released lease used after its conversation** throws `ObjectDisposedException` — a fire-and-forget
  task that outlived the conversation that started it. If a hardware scenario produces one, report
  which conversation, do not suppress it.
- **Do not merge to `main`.** The user does that, after the hardware pass.
- Piece C — `WriteToneToUserMemory`'s three steps, `SynthParam`'s write-then-read,
  restore-audition-then-program-change, and Motional Surround's L-R/F-B pair — is out of scope and
  still takes one lease per step. They all need the same thing: a lease threaded through the domain
  layer.
