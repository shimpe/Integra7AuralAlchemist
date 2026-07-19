# Single owner for MIDI device access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make one object own the MIDI port, handing out a lease that a conversation holds for its whole duration, so a multi-message sequence cannot be split and bypassing the lock stops being expressible.

**Architecture:** A `MidiPort` owns the `IMidiIn`/`IMidiOut` handles and hands out an `IMidiLease` via `AcquireAsync(what)`. Only the lease exposes send and request. A lease installs one reader for its lifetime, accumulates messages that were not its replies, and dispatches them on release — after the port is free. Reentrancy reuses the lease already held by the current async flow.

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
| `Tests/TestMidiPort.cs` (create) | Exclusivity, reentrancy, disposal, drain-on-release, long-hold warning. |
| `Src/Models/Services/Integra7Api.cs` (modify) | Every device method becomes acquire-converse-release. |
| `Src/Models/Services/AsyncMidiOutputWrapper.cs` (delete) | Subsumed by the port. |
| `Src/Models/Services/MidiHandlerOwnership.cs` (delete) | Subsumed: a single owner makes the desync it guards impossible. |
| ~36 files (modify) | Remove the dead `SemaphoreSlim` constructor parameter. |

---

### Task 1: The port, exclusivity and reentrancy

**Files:**
- Create: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestMidiPort.cs`:

```csharp
using System;
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
    public async Task AcquiringAgainInsideAConversationReusesTheLease()
    {
        // Without this, any conversation that called a helper which also acquires would block on
        // itself -- and the request helper is reached from dozens of places.
        var port = NewPort(out _, out _);

        await using var outer = await port.AcquireAsync("outer");
        var inner = await port.AcquireAsync("inner");

        Assert.That(inner, Is.SameAs(outer));
    }

    [Test]
    public async Task DisposingTheInnerHandleDoesNotReleaseThePort()
    {
        var port = NewPort(out _, out _);

        var outer = await port.AcquireAsync("outer");
        var inner = await port.AcquireAsync("inner");
        await inner.DisposeAsync();

        var another = port.AcquireAsync("another");
        Assert.That(another.IsCompleted, Is.False, "only the outermost dispose releases the port");

        await outer.DisposeAsync();
        Assert.That(await Task.WhenAny(another, Task.Delay(3000)), Is.SameAs(another));
        await (await another).DisposeAsync();
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
        // AsyncLocal flows into fire-and-forget work started inside a conversation, so such a task
        // would appear to still hold a lease the conversation has released. Throwing makes that loud
        // instead of letting it write onto a port someone else now owns.
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("done");
        await lease.DisposeAsync();

        Assert.That(async () => await lease.SendAsync([0x01]), Throws.TypeOf<ObjectDisposedException>());
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The one owner of the MIDI port. Everything that talks to the device takes a lease first,
/// and a lease covers a whole conversation rather than a single message -- so a sequence that must
/// arrive together, like a bank select followed by a program change, cannot be split.</summary>
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

    /// <summary>The lease held by the current async flow, so a nested acquire can reuse it rather than
    /// block on itself.</summary>
    private static readonly AsyncLocal<Lease?> Held = new();

    public MidiPort(IMidiOut midiOut, IMidiIn midiIn)
    {
        _midiOut = midiOut;
        _midiIn = midiIn;
    }

    public async Task<IMidiLease> AcquireAsync(string what)
    {
        if (Held.Value is { Released: false } already) return already;

        await _gate.WaitAsync();

        var lease = new Lease(this, what);
        Held.Value = lease;
        return lease;
    }

    private void Release(Lease lease)
    {
        if (Held.Value == lease) Held.Value = null;
        _gate.Release();
    }

    private sealed class Lease : IMidiLease
    {
        private readonly MidiPort _port;
        private readonly string _what;

        public Lease(MidiPort port, string what)
        {
            _port = port;
            _what = what;
        }

        public bool Released { get; private set; }

        public Task SendAsync(byte[] data)
        {
            ThrowIfReleased();
            // Offloaded: SafeSend blocks on the driver, and callers are often on the UI thread.
            return Task.Run(() => _port._midiOut.SafeSend(data));
        }

        public ValueTask DisposeAsync()
        {
            // A nested acquire returns this same lease, and disposing that handle must not release the
            // port -- only the outermost dispose does. Reaching here twice for the same lease is that
            // inner dispose, so it is a no-op.
            if (Released) return ValueTask.CompletedTask;

            Released = true;
            _port.Release(this);
            return ValueTask.CompletedTask;
        }

        private void ThrowIfReleased()
        {
            if (Released)
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

Expected: **`Failed: 1, Passed: 5`** — five pass, and
`DisposingTheInnerHandleDoesNotReleaseThePort` FAILS.

That failure is intended and is what Task 2 fixes. A nested acquire returns the *same object*, so the
inner `DisposeAsync` sets `Released` and hands the port back while the outer conversation is still
running. Leave it failing; do not add the depth count here.

If that test unexpectedly PASSES, stop and report — it would mean the implementation differs from the
one given above, and Task 2's premise no longer holds.

- [ ] **Step 5: Commit**

Commit with the nesting test still red — Task 2 is the other half, and keeping the nesting semantics in
their own commit is deliberate.

```bash
git add Src/Models/Services/MidiPort.cs Tests/TestMidiPort.cs
git commit -m "feat: give the MIDI port a single owner and a per-conversation lease

Nesting is not handled yet: an inner dispose still releases the port, and the
test for it is red until the next commit.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Nesting depth

**Files:**
- Modify: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

Task 1's lease releases on the first `DisposeAsync`, so an inner `await using` releases the port while
the outer conversation is still running. `DisposingTheInnerHandleDoesNotReleaseThePort` will have
caught this. Fix it with a depth count.

- [ ] **Step 1: Confirm the failing test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~DisposingTheInnerHandleDoesNotReleaseThePort"
```

Expected: FAIL — the third acquire completes when it should still be waiting.

If it PASSES, stop and report: it would mean Task 1's implementation already handles nesting, and this
task is unnecessary.

- [ ] **Step 2: Add the depth count**

In `Src/Models/Services/MidiPort.cs`, change `AcquireAsync` and the `Lease` so nesting is counted:

```csharp
    public async Task<IMidiLease> AcquireAsync(string what)
    {
        if (Held.Value is { Released: false } already)
        {
            already.Enter();
            return already;
        }

        await _gate.WaitAsync();

        var lease = new Lease(this, what);
        Held.Value = lease;
        return lease;
    }
```

and in `Lease`:

```csharp
        /// <summary>How many acquires are outstanding on this lease. A nested acquire returns this
        /// same object, so the port must not be released until as many disposes have come back.</summary>
        private int _depth = 1;

        public void Enter() => _depth++;

        public ValueTask DisposeAsync()
        {
            if (Released) return ValueTask.CompletedTask;
            if (--_depth > 0) return ValueTask.CompletedTask;

            Released = true;
            _port.Release(this);
            return ValueTask.CompletedTask;
        }
```

- [ ] **Step 3: Run the tests**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestMidiPort"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [ ] **Step 4: Commit**

```bash
git add Src/Models/Services/MidiPort.cs
git commit -m "fix: release the port only when the outermost lease is disposed

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

### Task 4: Say who is holding the port

**Files:**
- Modify: `Src/Models/Services/MidiPort.cs`
- Test: `Tests/TestMidiPort.cs`

Every hang chased during this work presented as a frozen UI with no indication of who held what.

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
        if (Held.Value is { Released: false } already)
        {
            already.Enter();
            return already;
        }

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

            await _gate.WaitAsync();
        }

        _holder = what;
        _heldSince = DateTime.UtcNow;
        var lease = new Lease(this, what);
        Held.Value = lease;
        return lease;
    }
```

and set the holder back in `Release`:

```csharp
    private void Release(Lease lease)
    {
        if (Held.Value == lease) Held.Value = null;
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

- [ ] **Step 5: Give `WriteToneToUserMemory` one lease**

Its three steps currently acquire separately, and step 1 has the side effect of selecting the new
patch — so a read between steps 1 and 3 reads the *new* patch, and a preset change there writes the
wrong name to the wrong slot.

Take one lease at the top of the method:

```csharp
        await using var port = await _port.AcquireAsync("write tone to user memory");
```

and replace every

```csharp
                var w = new AsyncMidiOutputWrapper(_midiOut, _semaphore);
                await w.SafeSendAsync(msg);
```

with

```csharp
                await port.SendAsync(msg);
```

`ChangePresetNameAsync`, called as step 2, writes through `WriteToIntegraAsync` and so reaches
`MakeDataTransmissionAsync`, whose own acquire nests and reuses this lease. Confirm that is what
happens rather than assuming it — if it does not, the three steps are not atomic and this task has not
achieved its purpose.

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

- **The reentrancy model is the risky part.** `AsyncLocal` flows into work started inside a lease, so a
  fire-and-forget task can outlive the conversation and hold a released lease. That is why using one
  throws. If a hardware scenario produces `ObjectDisposedException` naming a conversation, that is this
  hazard made visible — report which two conversations, do not suppress it.
- **Where `AsyncLocal` does not flow** is work handed to a pre-existing pump —
  `ThrottledParameterWriter`'s Rx subscription on `Scheduler.Default`. Work reaching the port from
  there is a different async flow and queues behind the lease, which is correct. It also means a
  conversation holding a lease while awaiting a throttled write would deadlock. Check each migrated
  method for that shape.
- **Do not merge to `main`.** The user does that, after the hardware pass.
- Piece C — `SynthParam`'s write-then-read, restore-audition-then-program-change, and Motional
  Surround's L-R/F-B pair — is out of scope and still takes one lease per step.
