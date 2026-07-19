# MIDI reply matching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a MIDI read accept only the reply to its own request, and deliver anything else that arrived meanwhile to the normal unsolicited path once the port is free.

**Architecture:** A pure `ReplyMatchers` file says what each conversation is waiting for. `AsyncMidiInputWrapper` takes a matcher, returns only a matching message, and collects the rest for the caller to drain. `Integra7Api` drains them after releasing the semaphore, through a single `DispatchUnsolicited` method extracted from `MidiIn.DefaultHandler` so requested and unrequested traffic follow one path.

**Tech Stack:** .NET 10, C# 13, managed-midi 1.10.1, Serilog, NUnit.

**Spec:** `docs/superpowers/specs/2026-07-19-midi-reply-matching-design.md`

---

## Domain background (read this first)

This application edits a Roland INTEGRA-7 hardware synthesizer over MIDI sysex.

A read is a **request/reply conversation**: the application sends an RQ1 (command byte `0x11`) naming
a 4-byte address, and the device answers with a DT1 (command `0x12`) carrying that same address plus
the data. Roland sysex looks like:

```
f0 41 <dev> 00 00 64 <cmd> <addr0..3> <data...> <checksum> f7
 0   1   2   3  4  5    6     7..10
```

`f0` starts sysex, `41` is Roland, byte 2 is the device ID (varies — never match on it), bytes 3-5 are
the model ID, byte 6 is the command, bytes 7-10 are the address.

The identity request is different: it is a *universal non-realtime* message, `f0 7e <dev> 06 01 f7`,
answered by `f0 7e <dev> 06 02 41 ... f7`. Match it on `f0 7e` and `06 02` at indices 3 and 4.

**The bug.** `AsyncMidiInputWrapper` installs itself as the single MIDI input handler in its
constructor, which detaches `MidiIn.DefaultHandler`. It then returns the *first message that arrives*,
whatever it is. So if the user turns the patch dial on the device while a read is in flight, that
message is returned as the reply and parsed as the address range that was requested — wrong values,
silently — and the panel change never reaches the UI.

## Build and test commands

The system `dotnet` on this machine is version 8/9 and **too old** for this solution. Always use the
user-local SDK:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

If a build fails with MSB3027 / MSB3021 ("cannot copy … being used by another process"), a running
app instance or a Rider XAML previewer holds the output files. **Report it — do not kill the process**,
it belongs to the user.

If `git commit` fails with "Permission denied" writing to `.git/objects`, that is a transient
anti-virus lock. Wait a moment and retry the same command.

## Conventions

- Never pass `--no-verify` to `git commit`.
- Every commit message ends with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work on the branch `midi-reply-matching`, which already exists and holds the spec commit. Do not
  merge to `main`; the user does that.
- Tests live in `Tests/`, one file per subject, named `TestXxx.cs`, `namespace Tests;`, NUnit
  `[TestFixture]`. See `Tests/TestNameListEndMarker.cs` for house style: behavioural method names,
  captured-fixture comments explaining where the bytes came from.
- Pure helpers live in `Src/Models/Services/` with no MIDI, no tasks and no ReactiveUI. See
  `NameListEndMarker.cs`, `PartLoadState.cs`, `MidiHandlerOwnership.cs`.

## File structure

| File | Responsibility |
|---|---|
| `Src/Models/Services/ReplyMatchers.cs` (create) | `IReplyMatcher` plus one implementation per conversation type. Pure. |
| `Tests/TestReplyMatchers.cs` (create) | The matching table. |
| `Src/Models/Services/AsyncMidiInputWrapper.cs` (modify) | Takes a matcher; returns only a match; collects the rest. |
| `Tests/TestAsyncMidiInputWrapper.cs` (create) | Deferral behaviour, against a fake `IMidiIn`. |
| `Src/Models/Services/MidiIn.cs` (modify) | `DispatchUnsolicited` extracted from `DefaultHandler`, added to `IMidiIn`. |
| `Src/Models/Services/Integra7Api.cs` (modify) | `RunRequestAsync` helper; three conversations routed through it; the burst drains too. |

---

### Task 1: Reply matchers

**Files:**
- Create: `Src/Models/Services/ReplyMatchers.cs`
- Test: `Tests/TestReplyMatchers.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestReplyMatchers.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What each conversation will accept as its own reply. Everything here is pure -- no MIDI,
/// no hardware -- which is the point: a matcher that is too strict turns a working read into a
/// timeout, and that failure is silent at runtime.</summary>
[TestFixture]
public class TestReplyMatchers
{
    // f0 41 <dev> 00 00 64 12 | 0f 00 04 02 | payload | checksum | f7
    private static byte[] DataSet(byte[] address, byte deviceId = 0x10) =>
    [
        0xf0, 0x41, deviceId, 0x00, 0x00, 0x64, 0x12,
        address[0], address[1], address[2], address[3],
        0x00, 0x01, 0x02, 0x6b, 0xf7
    ];

    private static readonly byte[] ToneNameAddress = [0x0f, 0x00, 0x04, 0x02];
    private static readonly byte[] SrxAddress = [0x0f, 0x00, 0x00, 0x10];

    // Captured shape of an identity reply: f0 7e <dev> 06 02 41 ...
    private static readonly byte[] IdentityReplyMessage =
    [
        0xf0, 0x7e, 0x10, 0x06, 0x02, 0x41, 0x64, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xf7
    ];

    [Test]
    public void ADataSetAtTheRequestedAddressIsTheReply()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(ToneNameAddress)), Is.True);
    }

    [Test]
    public void ADataSetAtADifferentAddressIsNotTheReply()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(SrxAddress)), Is.False);
    }

    [Test]
    public void TheDeviceIdIsIgnoredWhenMatching()
    {
        // Byte 2 varies with how the unit is configured; matching on it would reject every reply
        // from a device that is not on the default ID.
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(ToneNameAddress, 0x11)), Is.True);
    }

    [Test]
    public void ARequestIsNotMistakenForAReply()
    {
        // Same address, command 0x11 (request) rather than 0x12 (data set).
        var request = DataSet(ToneNameAddress);
        request[6] = 0x11;

        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Matches(request), Is.False);
    }

    [Test]
    public void AMessageTooShortToHoldAnAddressMatchesNothing()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches([]), Is.False);
        Assert.That(matcher.Matches([0xfe]), Is.False);                    // active sensing
        Assert.That(matcher.Matches([0xc0, 0x05]), Is.False);              // program change
        Assert.That(matcher.Matches([0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f]), Is.False);
    }

    [Test]
    public void AnIdentityReplyMatchesOnlyTheIdentityMatcher()
    {
        Assert.That(ReplyMatchers.IdentityReply.Matches(IdentityReplyMessage), Is.True);
        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Matches(IdentityReplyMessage), Is.False);
        Assert.That(ReplyMatchers.IdentityReply.Matches(DataSet(ToneNameAddress)), Is.False);
    }

    [Test]
    public void AnIdentityRequestIsNotMistakenForItsReply()
    {
        // 06 01 is the request, 06 02 the reply.
        byte[] identityRequest = [0xf0, 0x7e, 0x7f, 0x06, 0x01, 0xf7];

        Assert.That(ReplyMatchers.IdentityReply.Matches(identityRequest), Is.False);
    }

    [Test]
    public void TheNameListMatcherDelegatesToTheBurstReader()
    {
        // 34 bytes, all-zero payload -- the burst terminator, which is a name-list reply.
        byte[] terminator =
        [
            0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x6b, 0xf7
        ];

        Assert.That(ReplyMatchers.NameListReply(ToneNameAddress).Matches(terminator), Is.True);
        Assert.That(ReplyMatchers.NameListReply(SrxAddress).Matches(terminator), Is.False);
    }

    [Test]
    public void EveryMatcherDescribesItself()
    {
        // Describe() is logged when a message is deferred, so a wrong matcher is findable.
        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Describe(), Does.Contain("0F-00-04-02"));
        Assert.That(ReplyMatchers.IdentityReply.Describe(), Is.Not.Empty);
        Assert.That(ReplyMatchers.NameListReply(ToneNameAddress).Describe(), Does.Contain("0F-00-04-02"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestReplyMatchers"
```

Expected: build failure, `CS0103: The name 'ReplyMatchers' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/ReplyMatchers.cs`:

```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a reader is waiting for. A conversation supplies one so a read can tell its own reply
/// from anything else the device happens to send while it is waiting -- a preset change made on the
/// front panel, most commonly. Without this a read returns whatever arrives first and parses it as the
/// address range it asked for.</summary>
public interface IReplyMatcher
{
    bool Matches(byte[] message);

    /// <summary>Logged when a message is deferred, so a matcher that is too strict shows up as traffic
    /// that never matched rather than as parameters that silently stopped updating.</summary>
    string Describe();
}

/// <summary>The matchers the four device conversations use. Pure: no MIDI, no tasks.</summary>
public static class ReplyMatchers
{
    private const int CommandIndex = 6;
    private const int DataSetCommand = 0x12;
    private const int AddressIndex = 7;
    private const int AddressLength = 4;

    /// <summary>A Roland data set (DT1) carrying <paramref name="address"/> -- the answer to a data
    /// request for it. Byte 2 is the device ID and is deliberately not matched: it varies with how the
    /// unit is configured.</summary>
    public static IReplyMatcher DataSetAt(byte[] address) => new DataSetMatcher(address);

    /// <summary>The universal non-realtime identity reply, f0 7e &lt;dev&gt; 06 02 ...</summary>
    public static IReplyMatcher IdentityReply { get; } = new IdentityMatcher();

    /// <summary>A reply belonging to a name-list burst at <paramref name="address"/>. Delegates to the
    /// structural match the burst reader already uses.</summary>
    public static IReplyMatcher NameListReply(byte[] address) => new NameListMatcher(address);

    private sealed class DataSetMatcher(byte[] address) : IReplyMatcher
    {
        public bool Matches(byte[] message)
        {
            if (message.Length < AddressIndex + AddressLength) return false;
            if (message[0] != 0xf0 || message[1] != 0x41) return false;
            if (message[CommandIndex] != DataSetCommand) return false;

            for (var i = 0; i < AddressLength; i++)
                if (message[AddressIndex + i] != address[i])
                    return false;

            return true;
        }

        public string Describe() => $"a data set at {BitConverter.ToString(address)}";
    }

    private sealed class IdentityMatcher : IReplyMatcher
    {
        public bool Matches(byte[] message) =>
            message.Length >= 5 && message[0] == 0xf0 && message[1] == 0x7e &&
            message[3] == 0x06 && message[4] == 0x02;

        public string Describe() => "an identity reply";
    }

    private sealed class NameListMatcher(byte[] address) : IReplyMatcher
    {
        public bool Matches(byte[] message) => NameListEndMarker.IsNameListReply(message, address);

        public string Describe() => $"a name-list reply at {BitConverter.ToString(address)}";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestReplyMatchers"
```

Expected: `Passed! - Failed: 0, Passed: 9`.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ReplyMatchers.cs Tests/TestReplyMatchers.cs
git commit -m "feat: describe what each device conversation accepts as its reply

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Defer what is not the reply

**Files:**
- Modify: `Src/Models/Services/AsyncMidiInputWrapper.cs`
- Test: `Tests/TestAsyncMidiInputWrapper.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestAsyncMidiInputWrapper.cs`. Note `MidiReceivedEventArgs` is object-initializer
constructible — this has been verified against the installed managed-midi 1.10.1.

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A read must return its own reply and keep everything else, rather than returning whichever
/// message happened to arrive first. The messages it keeps are real device traffic -- a front-panel
/// preset change, most often -- and losing them is the bug this exists to fix.</summary>
[TestFixture]
public class TestAsyncMidiInputWrapper
{
    /// <summary>A MIDI input the test drives directly: Push delivers a message to whatever handler the
    /// wrapper installed.</summary>
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

    private static readonly byte[] Address = [0x0f, 0x00, 0x04, 0x02];

    private static byte[] Reply() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x00, 0x6b, 0xf7
    ];

    // A preset change the device sends unprompted -- shorter, and at a different address.
    private static byte[] PanelChange() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x03, 0x02, 0x01, 0x0e, 0xf7
    ];

    [Test]
    public void TheReplyIsReturnedAndNothingIsDeferred()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True, "the read did not complete");
        Assert.That(read.Result, Is.EqualTo(Reply()));
        Assert.That(mi.TakeDeferred(), Is.Empty);
    }

    [Test]
    public void AMessageThatIsNotTheReplyIsKeptAndTheReadKeepsWaiting()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True, "the read did not complete");
        Assert.That(read.Result, Is.EqualTo(Reply()),
            "the panel change must not be returned as the reply -- that is the defect");
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { PanelChange() }));
    }

    [Test]
    public void MessagesAreKeptInArrivalOrder()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        byte[] first = [0xc0, 0x05];
        byte[] second = [0xb0, 0x00, 0x55];
        midi.Push(first);
        midi.Push(second);
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void ATimeoutStillKeepsWhatArrived()
    {
        // The read gets nothing it wants, but the panel change is real traffic and must survive.
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());

        Assert.That(read.Wait(TimeSpan.FromSeconds(4)), Is.True, "the read should have timed out by now");
        Assert.That(read.Result, Is.Empty, "a timeout returns no reply");
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { PanelChange() }));
    }

    [Test]
    public void TakingTheDeferredMessagesClearsThem()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());
        midi.Push(Reply());
        read.Wait(TimeSpan.FromSeconds(3));

        Assert.That(mi.TakeDeferred(), Has.Count.EqualTo(1));
        Assert.That(mi.TakeDeferred(), Is.Empty, "a second drain must not deliver the same message twice");
    }

    [Test]
    public void AChunkCarryingTheReplyAmongOtherMessagesIsAccepted()
    {
        // One MIDI event can carry several concatenated sysex messages. If any of them is the reply,
        // the chunk is what the parsers downstream already expect to receive.
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var combined = new List<byte>();
        combined.AddRange(PanelChange());
        combined.AddRange(Reply());

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(combined.ToArray());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(read.Result, Is.EqualTo(combined.ToArray()));
        Assert.That(mi.TakeDeferred(), Is.Empty);
    }

    [Test]
    public void TheHandlerIsHandedBackWhenTheReadCompletes()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));
        Assert.That(midi.HandlerInstalled, Is.True);

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(Reply());
        read.Wait(TimeSpan.FromSeconds(3));

        Assert.That(midi.HandlerInstalled, Is.False);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestAsyncMidiInputWrapper"
```

Expected: build failure — `IMidiIn` has no `DispatchUnsolicited` (added in Task 3, so the fake will not
compile yet) and `AsyncMidiInputWrapper` has no two-argument constructor and no `TakeDeferred`.

Because the fake needs `DispatchUnsolicited` and that arrives in Task 3, **do Task 3's Step 1 now**
(add the member to the `IMidiIn` interface and implement it in `MidiIn`), then return here. Task 3's
remaining steps still apply.

- [ ] **Step 3: Write the implementation**

Replace the whole of `Src/Models/Services/AsyncMidiInputWrapper.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

public class AsyncMidiInputWrapper
{
    private const double inactivityTimespan = 1.5;
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private readonly IMidiIn _midiInput;

    /// <summary>This reader's handler, kept so the port can be handed back by identity. Restoring the
    /// default handler unconditionally would detach whichever reader happens to be installed, not
    /// necessarily this one.</summary>
    private readonly EventHandler<MidiReceivedEventArgs> _handler;

    /// <summary>What this conversation is waiting for.</summary>
    private readonly IReplyMatcher _expected;

    /// <summary>Messages that arrived while waiting and were not the reply. This reader owns the MIDI
    /// input for its whole duration, so it is the only thing that can see them; dropping them would
    /// lose real device traffic, such as a preset change made on the front panel.</summary>
    private readonly List<byte[]> _deferred = [];

    public AsyncMidiInputWrapper(IMidiIn midiIn, IReplyMatcher expected)
    {
        _midiInput = midiIn;
        _expected = expected;
        _handler = OnMidiMessageReceived;
        _midiInput.ConfigureHandler(_handler);
    }

    private void OnMidiMessageReceived(object? sender, MidiReceivedEventArgs e)
    {
        var localCopy = new byte[e.Length];
        Buffer.BlockCopy(e.Data, 0, localCopy, 0, e.Length);
        ByteStreamDisplay.Display($"Received {localCopy.Length} bytes (async): ", localCopy);
        _channel.Writer.TryWrite(localCopy);
    }

    /// <summary>Messages that arrived while this read was waiting and were not its reply, in arrival
    /// order. Taking them clears the list; the caller delivers them once the port is free.</summary>
    public IReadOnlyList<byte[]> TakeDeferred()
    {
        var taken = _deferred.ToArray();
        _deferred.Clear();
        return taken;
    }

    /// <summary>True when this chunk carries the reply. A single MIDI event can hold several
    /// concatenated messages, so the chunk is split and accepted whole if any fragment matches -- which
    /// is the shape the parsers downstream already expect.</summary>
    private bool IsExpected(byte[] chunk)
    {
        foreach (var fragment in ByteUtils.SplitAfterF7(chunk))
            if (fragment is not null && _expected.Matches(fragment))
                return true;

        return false;
    }

    private void Defer(byte[] message)
    {
        _deferred.Add(message);
        Log.Warning(
            "Deferring a message that is not {Expected}: {Length} byte(s), starting {Bytes}. It will be dispatched when the port is free.",
            _expected.Describe(), message.Length,
            BitConverter.ToString(message, 0, Math.Min(message.Length, 8)));
    }

    public async Task<byte[]> WaitForMidiMessageAsync()
    {
        var cts = new CancellationTokenSource();
        // The deadline runs from the request, not from the last message: traffic this read is not
        // waiting for must not be able to hold it open indefinitely.
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        while (true)
        {
            var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
            if (await Task.WhenAny(waitForTimeout, waitForData) != waitForData) break;

            var message = await _channel.Reader.ReadAsync(cts.Token);
            if (IsExpected(message))
            {
                await cts.CancelAsync();
                _midiInput.RemoveHandler(_handler);
                return message;
            }

            Defer(message);
        }

        await cts.CancelAsync();
        _midiInput.RemoveHandler(_handler);
        return [];
    }

    public async Task<byte[]> WaitForMidiMessageAsyncExpectingMultipleInARow()
    {
        var cts = new CancellationTokenSource();
        var waitForTimeout = Task.Delay(TimeSpan.FromSeconds(inactivityTimespan), cts.Token);

        while (true)
        {
            var waitForData = _channel.Reader.WaitToReadAsync(cts.Token).AsTask();
            if (await Task.WhenAny(waitForTimeout, waitForData) != waitForData) break;

            var message = await _channel.Reader.ReadAsync(cts.Token);
            if (IsExpected(message))
            {
                // Deliberately does NOT hand the port back: the burst reader stays installed across
                // every reply until its caller decides the burst has ended.
                await cts.CancelAsync();
                return message;
            }

            Defer(message);
        }

        await cts.CancelAsync();
        return [];
    }

    public void CleanupAfterTimeOut()
    {
        _midiInput.RemoveHandler(_handler);
        _midiInput.RestoreAutomaticHandling();
        _channel.Writer.TryComplete();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestAsyncMidiInputWrapper"
```

Expected: `Passed! - Failed: 0, Passed: 7`.

The build will still fail elsewhere: `Integra7Api` constructs `AsyncMidiInputWrapper` with one argument
at four sites (`:78`, `:109`, `:191`, `:518`). Tasks 4 and 5 fix those. If the test command cannot run
because the solution does not build, temporarily supply `ReplyMatchers.IdentityReply` at those four
sites to get a green build, then **replace each with its real matcher in Tasks 4 and 5** — do not leave
a placeholder matcher behind.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/AsyncMidiInputWrapper.cs Tests/TestAsyncMidiInputWrapper.cs Src/Models/Services/MidiIn.cs
git commit -m "feat: keep messages a read was not waiting for instead of returning them

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: One delivery path for unrequested messages

**Files:**
- Modify: `Src/Models/Services/MidiIn.cs:12-23` (the interface), `:140-175` (`DefaultHandler`)

- [ ] **Step 1: Add the member to the interface and implement it**

In `Src/Models/Services/MidiIn.cs`, add to the `IMidiIn` interface:

```csharp
    /// <summary>Route a message nobody requested: a data set becomes a UI update, a preset change
    /// becomes a resync, anything else is logged. Called both by the default handler and by a reader
    /// draining what arrived while it was waiting, so the two cannot diverge.</summary>
    public void DispatchUnsolicited(byte[] message);
```

Then replace the body of `DefaultHandler` (`:140-175`) so the dispatch logic moves into the new method:

```csharp
    private void DefaultHandler(object? sender, MidiReceivedEventArgs e)
    {
        _replyReady.Reset();
        _replyData = new byte[e.Length];
        var localCopy = new byte[e.Length];
        Debug.Assert(e.Length != 0);
        Array.Copy(e.Data, localCopy, e.Length);
        Array.Copy(localCopy, _replyData, e.Length);
        if (Verbose) ByteStreamDisplay.Display("Received (default handler): ", localCopy);
        if (!_manualReplyHandling)
        {
            DispatchUnsolicited(localCopy);
        }
        else
        {
            Log.Debug(
                "Received MIDI msg that will not be dispatched for ui update because manual reply handling is active.");
        }

        _manualReplyHandling = false;
        _replyReady.Set();
    }

    public void DispatchUnsolicited(byte[] message)
    {
        if (Integra7SysexHelpers.CheckIsDataSetMsg(message))
        {
            Log.Debug("Request UpdateSysexSpec");
            MessageBus.Current.SendMessage(new UpdateFromSysexSpec(message), "hw2ui");
        }
        else if (Integra7Api.CheckIsPartOfPresetChange(message, out var midiChannel))
        {
            Log.Debug($"Request UpdateSetPresetandResyncPart for channel {midiChannel}");
            MessageBus.Current.SendMessage(new UpdateSetPresetAndResyncPart(midiChannel));
        }
        else
        {
            Log.Debug("Received MIDI msg that will not be dispatched for ui update.");
            ByteStreamDisplay.Display("The message was: ", message);
        }
    }
```

This is a pure extraction — the behaviour for a message arriving with no reader installed is unchanged.

- [ ] **Step 2: Build**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: the only errors are the four `new AsyncMidiInputWrapper(_midiIn)` sites in `Integra7Api.cs`
if Task 2 is already applied. If Task 2 is not yet applied, expect a clean build.

- [ ] **Step 3: Commit**

Committed together with Task 2 (its Step 5 stages `MidiIn.cs`), since Task 2's fake `IMidiIn` cannot
compile without this interface member.

---

### Task 4: Route the three single-reply conversations through one helper

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs:75-99` (`CheckIdentityAsync`), `:101-125` (`MakeDataRequestAsync`), `:185-207` (`GetLoadedSrxAsync`)

- [ ] **Step 1: Add the helper**

In `Src/Models/Services/Integra7Api.cs`, add this private method next to `MakeDataRequestAsync`:

```csharp
    /// <summary>Send a request, wait for its own reply, then deliver anything else that arrived while
    /// waiting.
    ///
    /// The drain runs after the semaphore is released on purpose: a deferred message must reach the UI
    /// with the port actually free, not merely with the read logically finished. Dispatch is a
    /// MessageBus post to a throttled subscriber, so nothing reenters synchronously -- but a resync it
    /// triggers will take the semaphore, and it should find it available.</summary>
    private async Task<byte[]> RunRequestAsync(byte[] request, IReplyMatcher expected, string what)
    {
        // Captured: CheckIdentityAsync nulls the field on failure, and the drain still needs a port.
        var midiIn = _midiIn;
        if (midiIn is null) return [];

        byte[] reply;
        IReadOnlyList<byte[]> deferred;

        await _semaphore.WaitAsync();
        try
        {
            Log.Debug("DataRequest Lock acquired");
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
            Log.Debug("DataRequest Lock released");
            _semaphore.Release();
        }

        foreach (var m in deferred)
        {
            // Guarded per message: one malformed deferred message must not strand the rest.
            try
            {
                midiIn.DispatchUnsolicited(m);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to dispatch a message deferred during {What}.", what);
            }
        }

        return reply;
    }
```

- [ ] **Step 2: Route `MakeDataRequestAsync` through it**

Replace `MakeDataRequestAsync` (`:101-125`) with:

```csharp
    public async Task<byte[]> MakeDataRequestAsync(byte[] address, long size)
    {
        var data = Integra7SysexHelpers.MakeDataRequest(DeviceId(), address, size);
        return await RunRequestAsync(data, ReplyMatchers.DataSetAt(address), "data request");
    }
```

- [ ] **Step 3: Route `CheckIdentityAsync` through it**

Replace `CheckIdentityAsync` (`:75-99`) with:

```csharp
    public async Task CheckIdentityAsync()
    {
        // Now takes the semaphore, via the shared helper. It took none before, so pressing Rescan
        // during the startup name burst ran two conversations on one port.
        var reply = await RunRequestAsync(Integra7SysexHelpers.IDENTITY_REQUEST, ReplyMatchers.IdentityReply,
            "identity request");

        if (reply.Length == 0)
        {
            _midiOut = null;
            _midiIn = null;
            _deviceId = 0;
            return;
        }

        var usefulreply = Integra7SysexHelpers.TrimAfterEndOfSysex(reply);
        if (!Integra7SysexHelpers.CheckIdentityReply(usefulreply, out _deviceId))
        {
            _midiOut = null;
            _midiIn = null;
            _deviceId = 0;
        }
    }
```

Note the "Timeout waiting for MIDI reply while connecting to Integra-7." message is now produced by
the helper as "Timeout waiting for MIDI reply: identity request." — that is intended.

- [ ] **Step 4: Route `GetLoadedSrxAsync` through it**

Replace `GetLoadedSrxAsync` (`:185-207`) with:

```csharp
    public async Task<(byte /*slot1*/, byte /* slot2*/, byte /*slot3*/, byte /*slot4*/)> GetLoadedSrxAsync()
    {
        byte[] address = [0x0F, 0x00, 0x00, 0x10];
        var msg = Integra7SysexHelpers.MakeAskLoadedSrxMsg(_deviceId);
        var reply = await RunRequestAsync(msg, ReplyMatchers.DataSetAt(address), "loaded SRX request");

        if (reply.Length > 15) return (reply[11], reply[12], reply[13], reply[14]);

        return (0, 0, 0, 0);
    }
```

The address literal matches the payload `MakeAskLoadedSrxMsg` builds
(`Integra7SysexHelpers.cs:78`) — if that ever changes, this must change with it.

- [ ] **Step 5: Build and run the full suite**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: the only remaining error is `GetListOfNamesHelper` at `:518`, still constructing the wrapper
with one argument. Task 5 fixes it.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/Integra7Api.cs
git commit -m "refactor: give every single-reply conversation one shape

Acquire, converse, release, then deliver what arrived meanwhile. Identity
checking now takes the semaphore too -- it took none before, so a rescan during
the startup name burst ran two conversations on one port.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Give the name burst its matcher and its drain

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs:515-585` (`GetListOfNamesHelper`)

- [ ] **Step 1: Split the burst into gathering and delivering**

The drain must run after the semaphore is released, and every path out of the current method's `try`
block returns — so the method is split in two. The existing one is renamed and returns its deferred
messages alongside its names; a new wrapper keeps the original name and does the delivery.

First, rename `GetListOfNamesHelper` to `GatherNamesAsync` and change its signature to:

```csharp
    private async Task<(List<string> Names, IReadOnlyList<byte[]> Deferred)> GatherNamesAsync(byte[] msg)
```

Inside it, make these four changes and no others:

1. Replace the opening

```csharp
        await _semaphore.WaitAsync();
        var mi = new AsyncMidiInputWrapper(_midiIn);
        try
        {
            Log.Debug("DataRequest Lock acquired");
            List<byte[]> allReplies = [];
            var totalRepliesReceived = 0;
            // Replies carry back the address they answer. Taking it from the request rather than
            // assuming one keeps this helper correct for every name list -- Studio Set names are
            // requested at 0f 00 03 02, where the tone lists use 0f 00 04 02.
            var expectedAddress = NameListEndMarker.AddressOf(msg);
```

with

```csharp
        // Replies carry back the address they answer. Taking it from the request rather than
        // assuming one keeps this helper correct for every name list -- Studio Set names are
        // requested at 0f 00 03 02, where the tone lists use 0f 00 04 02.
        var expectedAddress = NameListEndMarker.AddressOf(msg);

        await _semaphore.WaitAsync();
        var mi = new AsyncMidiInputWrapper(_midiIn, ReplyMatchers.NameListReply(expectedAddress));
        try
        {
            Log.Debug("DataRequest Lock acquired");
            List<byte[]> allReplies = [];
            var totalRepliesReceived = 0;
```

2. Change the early `return [];` (the "no replies at all" path, inside the `else` branch that logs
   `"Timeout waiting for MIDI reply after data request."`) to `return ([], mi.TakeDeferred());`
3. Change the final `return names;` to `return (names, mi.TakeDeferred());`
4. Leave the `finally` block exactly as it is — each return now takes the deferred messages itself, so
   the `finally` needs no change.

Then add the wrapper that keeps the original name:

```csharp
    /// <summary>Fetch a list of names, then deliver anything the device sent while the burst held the
    /// port -- a preset change made on the front panel, most often. The burst is the longest read
    /// window in the application, so this is where an unsolicited message is most likely to land.</summary>
    private async Task<List<string>> GetListOfNamesHelper(byte[] msg)
    {
        var midiIn = _midiIn;
        if (midiIn is null) return [];

        var (names, deferred) = await GatherNamesAsync(msg);

        foreach (var m in deferred)
        {
            try
            {
                midiIn.DispatchUnsolicited(m);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to dispatch a message deferred during a name-list burst.");
            }
        }

        return names;
    }
```

- [ ] **Step 2: Build and run the full suite**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 381` — 365 existing plus 9 from Task 1 and 7 from
Task 2.

- [ ] **Step 3: Verify no placeholder matchers survived**

```bash
grep -n "AsyncMidiInputWrapper(" Src/Models/Services/Integra7Api.cs
```

Expected: two constructions, one in `RunRequestAsync` and one in `GatherNamesAsync`, each with a real
matcher. If any site still passes `ReplyMatchers.IdentityReply` as a stand-in from Task 2's Step 4,
replace it with the correct matcher now.

- [ ] **Step 4: Commit**

```bash
git add Src/Models/Services/Integra7Api.cs
git commit -m "fix: deliver what the device sent during a name burst

The burst holds the MIDI input for many seconds across eighteen requests, so it
is where an unsolicited message is most likely to be lost.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Hardware verification

**Files:** none — this task runs the application against the real INTEGRA-7.

**Stop and hand this to the user.** They have the hardware. Do not mark the plan complete without it.

- [ ] **Step 1: Confirm no other instance is running**

```bash
tasklist | grep -i integra7
```

Expected: no rows. If there are, the user must close that instance first — do not kill it.

- [ ] **Step 2: Launch and collect the log**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" run --project Src/Integra7AuralAlchemist.csproj
```

Log: `Src/bin/Debug/net10.0/logs/I7AuralAlchemist<YYYYMMDD>.log`.

- [ ] **Step 3: Run the four scenarios**

1. **Front-panel preset change during a part load.** Open a part, and while it is loading change the
   preset on the INTEGRA-7 itself. Expected: the change applies once the load finishes rather than
   being lost, and the log shows `Deferring a message that is not a data set at …` followed by the
   part picking up the new preset. Today the message is swallowed entirely.
2. **Front-panel preset change during the startup name burst.** Same expectation, across the longest
   read window in the application. This is the scenario that crashed before the burst was hardened.
3. **A normal session with no panel interaction.** Expected: **no** `Deferring a message` warnings at
   all. Any warning here means a matcher is too strict and is rejecting real replies — which would
   otherwise show up only as reads timing out and `DomainBase` logging "Keeping the previous values".
4. **Rescan MIDI devices while the name burst is running.** Expected: the two conversations serialise
   rather than overlapping, now that `CheckIdentityAsync` takes the semaphore.

- [ ] **Step 4: Report**

Report each scenario with the relevant log lines. If scenario 3 shows deferral warnings, stop — a
matcher is wrong, and it should be fixed as a unit test in `Tests/TestReplyMatchers.cs` first.

---

## Notes for the implementer

- **Do not change `ByteUtils.Slice`.** Its assertion guards against programmer error; callers validate.
- **`ByteUtils.SplitAfterF7` leaves a null in its last slot** when the input has no trailing `f7`, and
  returns `[null]` for input containing no `f7` at all. `IsExpected` guards for that with
  `fragment is not null`. A 2-byte program change therefore defers correctly rather than throwing.
- **The legacy synchronous path** — `_manualReplyHandling`, `AnnounceIntentionToManuallyHandleReply`,
  `RestoreAutomaticHandling`, `GetReply` — is untouched by this plan.
- **`ChangePresetAsync` still sends its three messages with no lock** (`Integra7Api.cs:445-451`). That
  is piece B of the device-access redesign and is deliberately out of scope here.
- **Do not merge to `main`.** The user does that, after the hardware pass.
