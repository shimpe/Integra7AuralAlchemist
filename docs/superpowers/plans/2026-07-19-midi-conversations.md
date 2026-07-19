# Conversations spanning the domain layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a caller hold the MIDI port across a sequence that leaves `Integra7Api`, goes out through the domain layer and comes back, so four multi-step sequences stop being interleavable.

**Architecture:** Every method on the path from a caller to the device takes an optional `IMidiLease? lease = null`. A call given one borrows it and must not release it; a call given none acquires and releases its own. Because every default is null, threading the parameter changes no behaviour — the four sequences then open a conversation and pass the lease down.

**Tech Stack:** .NET 10, C# 13, Avalonia 12.0.5, ReactiveUI, NUnit.

**Spec:** `docs/superpowers/specs/2026-07-19-midi-conversations-design.md`

---

## Domain background (read this first)

This application edits a Roland INTEGRA-7 synthesizer over MIDI sysex.

`Src/Models/Services/MidiPort.cs` — **read it first** — is the single owner of the port. A conversation
calls `AcquireAsync(what)` and holds an `IMidiLease` for its whole duration; only the lease can send or
read. **There is no reentrancy:** acquiring a lease while already holding one blocks and throws
`TimeoutException` after 60 seconds naming both conversations. The rule is that public methods acquire
and helpers take a lease.

The path from a caller to the device:

```
caller → DomainBase.WriteToIntegraAsync / ReadFromIntegraAsync
       → FullyQualifiedParameter(.Range).WriteToIntegraAsync / RetrieveFromIntegraAsync
       → IIntegra7Api.MakeDataTransmissionAsync / MakeDataRequestAsync
       → the lease
```

Four sequences span that path and are currently interleavable. The sharpest is
`Integra7Api.WriteToneToUserMemory`: its first step selects the new patch on the device, so a read
between steps 1 and 3 reads the *new* patch, and a preset change there writes the wrong name to the
wrong slot.

## Build and test commands

The system `dotnet` on this machine is version 8/9 and **too old**. Always use the user-local SDK:

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

MSB3027 / MSB3021 means a running app instance or a Rider XAML previewer holds the output files —
**report it, do not kill the process**, it belongs to the user. "Permission denied" on `.git/objects`
is a transient anti-virus lock; wait a moment and retry.

## Conventions

- Never pass `--no-verify` to `git commit`.
- Every commit message ends with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work on the branch `midi-conversations`, which already exists and holds the spec commit. Do not merge
  to `main`; the user does that.
- Tests: `Tests/TestXxx.cs`, `namespace Tests;`, NUnit `[TestFixture]`, behavioural method names.

## File structure

| File | Responsibility |
|---|---|
| `Src/Models/Services/Integra7Api.cs` | `Borrowed`, `BeginConversationAsync`, and the optional lease on its three device methods. |
| `Src/Models/Data/FullyQualifiedParameter.cs`, `FullyQualifiedParameterRange.cs` | Pass a lease through to the api. |
| `Src/Models/Domain/DomainBase.cs`, `Integra7Domain.cs` | Pass a lease through, and forward `BeginConversationAsync`. |
| `Src/Models/Services/WaveOutOfRangeReset.cs` | Pass a lease through. |
| `Src/ViewModels/SynthParam.cs`, `PCMSynthToneEditorViewModel.cs`, `SNSynthToneEditorViewModel.cs`, `PartViewModel.cs`, `MainWindowViewModel.cs` | Open the conversations. |
| `Tests/TestIntegra7ApiConversations.cs` | Extend; `RecordingPort.MostLeasesHeldAtOnce` is already there. |

Tasks 1–3 are behaviour-preserving. Tasks 4–6 change behaviour and are what the hardware pass checks.

---

### Task 1: Borrowing a lease, and starting a conversation

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs`
- Test: `Tests/TestIntegra7ApiConversations.cs`

- [ ] **Step 1: Write the failing tests**

Append inside the existing `TestIntegra7ApiConversations` class, before its closing brace:

```csharp
    [Test]
    public async Task ACallGivenALeaseDoesNotReleaseIt()
    {
        // The lease belongs to the conversation that opened it and has to outlive the call. Releasing
        // it here would free the port in the middle of a sequence, which is the whole thing this
        // exists to prevent.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await using var conversation = await api.BeginConversationAsync("a sequence");
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x01], conversation);
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x02], conversation);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "a sequence" }),
            "the two writes must join the conversation, not open their own");
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1));
        Assert.That(port.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ACallGivenNoLeaseAcquiresAndReleasesItsOwn()
    {
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x01]);
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x02]);

        Assert.That(port.Conversations, Has.Count.EqualTo(2), "each call is its own conversation");
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1), "and neither outlives its call");
    }
```

- [ ] **Step 2: Run them to verify they fail**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~TestIntegra7ApiConversations"
```

Expected: build failure — `Integra7Api` has no `BeginConversationAsync`, and
`MakeDataTransmissionAsync` takes two arguments.

- [ ] **Step 3: Add `Borrowed`, `LeaseAsync` and `BeginConversationAsync`**

In `Src/Models/Services/Integra7Api.cs`, add to the `IIntegra7Api` interface:

```csharp
    /// <summary>Open a conversation the caller will hold across several calls, passing the lease to
    /// each. A caller making only one call should not use this: the call acquires for itself.</summary>
    Task<IMidiLease> BeginConversationAsync(string what);
```

and to `Integra7Api`:

```csharp
    public Task<IMidiLease> BeginConversationAsync(string what) => _port.AcquireAsync(what);

    /// <summary>A lease this call may or may not own. Disposing it releases the port only when this
    /// call acquired it; a lease passed in belongs to the conversation that opened it and must outlive
    /// this call.</summary>
    private readonly struct Borrowed(IMidiLease lease, bool owned) : IAsyncDisposable
    {
        public IMidiLease Lease { get; } = lease;

        public ValueTask DisposeAsync() => owned ? lease.DisposeAsync() : ValueTask.CompletedTask;
    }

    private async Task<Borrowed> LeaseAsync(IMidiLease? given, string what) =>
        given is not null ? new Borrowed(given, false) : new Borrowed(await _port.AcquireAsync(what), true);
```

- [ ] **Step 4: Give `MakeDataTransmissionAsync` the optional lease**

Replace it with:

```csharp
    public async Task MakeDataTransmissionAsync(byte[] address, byte[] data, IMidiLease? lease = null)
    {
        var transmission = Integra7SysexHelpers.MakeDataSet(DeviceId(), address, data);
        await using var use = await LeaseAsync(lease, "parameter write");
        await use.Lease.SendAsync(transmission);
    }
```

and update its declaration in `IIntegra7Api` to match:

```csharp
    Task MakeDataTransmissionAsync(byte[] address, byte[] data, IMidiLease? lease = null);
```

- [ ] **Step 5: Run the tests**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 411` (409 existing plus 2 new). `Tests/TestFailedReadKeepsValues.cs`
implements `IIntegra7Api` and will need the two new members — add
`public Task<IMidiLease> BeginConversationAsync(string what) => throw new NotSupportedException();`
and the optional parameter on its `MakeDataTransmissionAsync`. That fake never uses either.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/Integra7Api.cs Tests/TestIntegra7ApiConversations.cs Tests/TestFailedReadKeepsValues.cs
git commit -m "feat: let a caller open a conversation and lend its lease

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: The other two device methods

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs`

Behaviour-preserving: the defaults are null, so every existing caller acquires exactly as before.

- [ ] **Step 1: Thread the lease through `MakeDataRequestAsync`**

`MakeDataRequestAsync` delegates to the private `RunRequestAsync`. Give both the parameter:

```csharp
    public async Task<byte[]> MakeDataRequestAsync(byte[] address, long size, IMidiLease? lease = null)
    {
        var data = Integra7SysexHelpers.MakeDataRequest(DeviceId(), address, size);
        return await RunRequestAsync(data, ReplyMatchers.DataSetAt(address), "data request", lease);
    }

    private async Task<byte[]> RunRequestAsync(byte[] request, IReplyMatcher expected, string what,
        IMidiLease? lease = null)
    {
        await using var use = await LeaseAsync(lease, what);
        var reply = await use.Lease.RequestAsync(request, expected);
        if (reply.Length == 0)
            Log.Error("Timeout waiting for MIDI reply: {What}, expecting {Expected}.", what,
                expected.Describe());

        return reply;
    }
```

Leave `CheckIdentityAsync` and `GetLoadedSrxAsync` calling `RunRequestAsync` without a lease — they are
single conversations and always will be.

- [ ] **Step 2: Thread it through `ChangePresetAsync`**

```csharp
    public async Task ChangePresetAsync(byte Channel, int Msb, int Lsb, int Pc, IMidiLease? lease = null)
    {
        // These three must arrive consecutively on the same channel.
        await using (var use = await LeaseAsync(lease, "preset change"))
        {
            await use.Lease.SendAsync(BankSelectMsb(Channel, Msb));
            await use.Lease.SendAsync(BankSelectLsb(Channel, Lsb));
            await use.Lease.SendAsync(ProgramChange(Channel, Pc - 1));
        }

        // Posted after our own lease is released. When a lease was lent to us the caller still holds
        // the port, so the subscriber queues behind it -- it is a different async flow, so it waits
        // rather than deadlocking.
        MessageBus.Current.SendMessage(new UpdateResyncPart(Channel));
    }
```

Update both declarations in `IIntegra7Api`:

```csharp
    Task ChangePresetAsync(byte Channel, int Msb, int Lsb, int Pc, IMidiLease? lease = null);
    Task<byte[]> MakeDataRequestAsync(byte[] address, long size, IMidiLease? lease = null);
```

- [ ] **Step 3: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 411`. Update `Tests/TestFailedReadKeepsValues.cs`'s
fake for the two changed signatures if the compiler asks.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: let a lease be lent to every device method

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Thread the lease through the domain layer

**Files:**
- Modify: `Src/Models/Data/FullyQualifiedParameter.cs`, `Src/Models/Data/FullyQualifiedParameterRange.cs`, `Src/Models/Domain/DomainBase.cs`, `Src/Models/Domain/Integra7Domain.cs`, `Src/Models/Services/WaveOutOfRangeReset.cs`

Wide and mechanical, and behaviour-preserving throughout: every new parameter defaults to null and is
passed straight down.

- [ ] **Step 1: `FullyQualifiedParameter` and `FullyQualifiedParameterRange`**

Add `IMidiLease? lease = null` as the last parameter of all four methods. Each makes **exactly one** api
call — verified — so each body needs exactly one edit:

| file:line | change |
|---|---|
| `FullyQualifiedParameter.cs:133` | `MakeDataRequestAsync(totalAddr, ParSpec.Bytes)` → `(totalAddr, ParSpec.Bytes, lease)` |
| `FullyQualifiedParameter.cs:149` | `MakeDataTransmissionAsync(totalAddr, data)` → `(totalAddr, data, lease)` |
| `FullyQualifiedParameterRange.cs:55` | `MakeDataTransmissionAsync(totalAddr, data)` → `(totalAddr, data, lease)` |
| `FullyQualifiedParameterRange.cs:78` | `MakeDataRequestAsync(totalAddr, size)` → `(totalAddr, size, lease)` |

The signatures become, for example:

```csharp
    public async Task WriteToIntegraAsync(IIntegra7Api integra7Api, Integra7StartAddresses startAddresses,
        Integra7Parameters parameters, IMidiLease? lease = null)
```

Change nothing else in these four methods.

- [ ] **Step 2: `DomainBase`**

Add `IMidiLease? lease = null` to all five methods and pass it down:

- `ReadFromIntegraAsync()`
- `ReadFromIntegraAsync(string parameterName)`
- `WriteToIntegraAsync()`
- `WriteToIntegraAsync(string parameterName)`
- `WriteToIntegraAsync(string parameterName, string displayedValue)`

Note `WriteToIntegraAsync(string, string)` delegates to `WriteToIntegraAsync(string)` — pass the lease
through that delegation too.

Then add the forwarder, so a view model holding only a domain can open a conversation:

```csharp
    /// <summary>Open a conversation covering several calls on this domain. Pass the lease to each of
    /// them; a call that does not receive it acquires its own, which would block against this one.</summary>
    public Task<IMidiLease> BeginConversationAsync(string what) => _integra7Api.BeginConversationAsync(what);
```

- [ ] **Step 3: `Integra7Domain` and `WaveOutOfRangeReset`**

```csharp
    public async Task WriteSingleParameterToIntegraAsync(FullyQualifiedParameter p, IMidiLease? lease = null)
```

passing `lease` to the call it makes, and:

```csharp
    public static async Task ApplyAsync(DomainBase domain, FullyQualifiedParameter edited,
        WaveformBanks banks, IMidiLease? lease = null)
```

There is exactly one `WriteToIntegraAsync` call in its body — verified — though it may sit inside a
loop. Pass `lease` to it.

- [ ] **Step 4: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 411`. Nothing passes a lease yet, so behaviour is
unchanged — that is the point of doing this as its own commit.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: let a lease travel from a caller to the device

Nothing passes one yet: every default is null, so the port is still acquired
exactly where it was.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Make the user tone write one conversation

**Files:**
- Modify: `Src/Models/Services/Integra7Api.cs`
- Test: `Tests/TestIntegra7ApiConversations.cs`

This is the sequence the whole piece exists for. Its steps currently acquire separately, and it carries
a comment explaining that a single lease would deadlock — which Task 3 has now made untrue.

- [ ] **Step 1: Invert the existing test**

In `Tests/TestIntegra7ApiConversations.cs`, replace `WritingAToneToUserMemoryKeepsItsStepsInSeparateConversations`
with:

```csharp
    [Test]
    public async Task WritingAToneToUserMemoryIsOneConversation()
    {
        // Step 1 selects the new patch on the device, so a read landing between steps 1 and 3 reads
        // the new patch, and a preset change there writes the wrong name to the wrong slot. This used
        // to be several conversations because there was no way to pass a lease through the domain
        // layer, and holding one across the steps would have deadlocked.
        var port = new RecordingPort();
        var api = new Integra7Api(port);
        var domain = new Integra7Domain(api, new Integra7StartAddresses(), LoadParameters());

        await api.WriteToneToUserMemory(domain, "SN-S", 0, "TEST NAME", 0);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "write tone to user memory" }));
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1));
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~WritingAToneToUserMemory"
```

Expected: FAIL — several conversations are opened, not one.

- [ ] **Step 3: Take one lease for the method**

In `WriteToneToUserMemory`, remove the `/// <summary>NOT atomic, deliberately...` comment above the
method, and take one lease at the top:

```csharp
        await using var port = await _port.AcquireAsync("write tone to user memory");
```

Replace every

```csharp
                await using (var port = await _port.AcquireAsync("write tone to user memory"))
                {
                    await port.SendAsync(msg);
                }
```

with

```csharp
                await port.SendAsync(msg);
```

and pass the lease to step 2 so its domain writes join the conversation:

```csharp
        await ChangePresetNameAsync(i7domain, zeroBasedPartNo, toneTypeStr, name, port);
```

giving `ChangePresetNameAsync` an `IMidiLease? lease = null` parameter that it passes to each
`WriteToIntegraAsync` call it makes. Read its body first — it writes one name parameter per tone type.

- [ ] **Step 4: Run the tests**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: `Failed: 0, Passed: 411`.

**If this HANGS**, a write inside `ChangePresetNameAsync` did not receive the lease and is acquiring
its own — a nested acquire. Find it rather than raising the timeout.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix: write a user tone as one conversation

Its first step selects the new patch, so a read between steps 1 and 3 read the
new patch and a preset change there wrote the wrong name to the wrong slot.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Make the two write-reset-read paths one conversation each

**Files:**
- Modify: `Src/ViewModels/SynthParam.cs:69-79`, `Src/ViewModels/MainWindowViewModel.cs:668-680`

Both write a parameter, reset dependents that the write pushed out of range, then re-read the domain.
The re-read must observe the state *those* writes produced.

- [ ] **Step 1: `SynthParam.Enqueue`**

It currently reads:

```csharp
    private void Enqueue() => _writer.Enqueue(_key, async () =>
    {
        await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(CultureInfo.InvariantCulture));
        if (_p.ParSpec.IsParent)
        {
            await WaveOutOfRangeReset.ApplyAsync(_domain, _p, WaveformBanks.Default);
            await _domain.ReadFromIntegraAsync();
        }
    });
```

Replace the body with:

```csharp
    private void Enqueue() => _writer.Enqueue(_key, async () =>
    {
        // One conversation: the re-read below must see the state these writes produced, and another
        // flow writing to the same domain in between would corrupt it without saying so.
        await using var lease = await _domain.BeginConversationAsync($"edit {_p.ParSpec.Path}");
        await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(CultureInfo.InvariantCulture),
            lease);
        // A parent param reinterprets dependent slots on the hardware; re-read so dependent controls
        // show correct values (mirrors the advanced view's IsParent resync). See memory
        // conditional-parameters-and-write-races.
        if (_p.ParSpec.IsParent)
        {
            await WaveOutOfRangeReset.ApplyAsync(_domain, _p, WaveformBanks.Default, lease);
            await _domain.ReadFromIntegraAsync(lease);
        }
    });
```

There are two other `Enqueue` bodies of the same shape in this file — around lines 148-156
(`ParamString`) and 218-222 (`ParamBool`). Read each, and give it the same treatment: open a
conversation named for its parameter path, and pass the lease to every call in the body.

- [ ] **Step 2: the `ui2hw` handler**

In `Src/ViewModels/MainWindowViewModel.cs`, `UpdateIntegraFromUiAsync` currently reads:

```csharp
        p.StringValue = s.DisplayValue;
        await _integra7Communicator?.WriteSingleParameterToIntegraAsync(p);
        if (p.ParSpec.IsParent)
        {
            var resetDomain = _integra7Communicator?.GetDomain(p);
            if (resetDomain != null)
            {
                await WaveOutOfRangeReset.ApplyAsync(resetDomain, p, WaveformBanks.Default);
                await resetDomain.ReadFromIntegraAsync();
            }
            ForceUiRefresh(p);
        }
```

Replace with:

```csharp
        p.StringValue = s.DisplayValue;
        // One conversation, for the same reason as the friendly editors' writes: the re-read must see
        // the state this write produced.
        await using var lease = await Integra7!.BeginConversationAsync($"edit {p.ParSpec.Path}");
        await _integra7Communicator?.WriteSingleParameterToIntegraAsync(p, lease);
        if (p.ParSpec.IsParent)
        {
            var resetDomain = _integra7Communicator?.GetDomain(p);
            if (resetDomain != null)
            {
                await WaveOutOfRangeReset.ApplyAsync(resetDomain, p, WaveformBanks.Default, lease);
                await resetDomain.ReadFromIntegraAsync(lease);
            }
            ForceUiRefresh(p);
        }
```

`Integra7` is nullable; the method already dereferences `_integra7Communicator` with `?.`, so guard
consistently — if `Integra7` is null there is no device and the method has nothing to do. Add at the
top of the method:

```csharp
        if (Integra7 is null) return;
```

- [ ] **Step 3: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 411`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "fix: write, reset and re-read a parameter as one conversation

The re-read has to see the state those writes produced. Another flow writing to
the same domain in between corrupted it silently.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Make restore-audition and the preset change one conversation

**Files:**
- Modify: `Src/ViewModels/SynthParam.cs` (`WriteImmediateAsync`), `Src/ViewModels/PCMSynthToneEditorViewModel.cs:141`, `Src/ViewModels/SNSynthToneEditorViewModel.cs:133`, `Src/ViewModels/PartViewModel.cs:1815-1825`

Restoring the partial on/off switches must complete before the patch changes, or the *outgoing* tone is
left holding audition state.

- [ ] **Step 1: `SynthParam.WriteImmediateAsync`**

```csharp
    public async System.Threading.Tasks.Task WriteImmediateAsync(bool value, IMidiLease? lease = null)
    {
        _suppress = true;
        try { this.RaiseAndSetIfChanged(ref _value, value); }
        finally { _suppress = false; }
        // Supersede any throttled write still pending for this key (e.g. an audition write enqueued
        // when the user toggled solo/mute moments ago) with a no-op, so it cannot fire ~THROTTLE ms
        // later — after a following program change — and stamp this value onto the new preset.
        _writer.Enqueue(_key, () => System.Threading.Tasks.Task.CompletedTask);
        await _domain.WriteToIntegraAsync(_p.ParSpec.Path, value ? _on : _off, lease);
    }
```

- [ ] **Step 2: Both editors' `RestoreAuditionAsync`**

In `Src/ViewModels/PCMSynthToneEditorViewModel.cs` and `Src/ViewModels/SNSynthToneEditorViewModel.cs`,
change the signature to take a lease and pass it on:

```csharp
    public async System.Threading.Tasks.Task RestoreAuditionAsync(IMidiLease? lease = null)
    {
        if (!_auditing) return;
        _suppressRecompute = true;
        foreach (var p in Partials) p.SetAuditionFlags(false, false);
        for (var i = 0; i < Partials.Count; i++) await Partials[i].IsOn.WriteImmediateAsync(_savedSwitches[i], lease);
        _suppressRecompute = false;
        _auditing = false;
        IsAuditioning = false;
    }
```

Read both files first — the bodies are near-identical but not guaranteed to be, so preserve whatever
each actually does around the loop.

- [ ] **Step 3: `PartViewModel.ChangePresetAsync`**

It currently reads:

```csharp
    public async Task ChangePresetAsync()
    {
        // Restore any active solo/mute audition (put partial on/off switches back) BEFORE the patch
        // changes, so the outgoing tone is left intact rather than its audition state. No-op otherwise.
        if (PcmSynthToneEditor is { } pcmEditor) await pcmEditor.RestoreAuditionAsync();
        if (SNSynthToneEditor is { } snsEditor) await snsEditor.RestoreAuditionAsync();

        var CurrentSelection = _selectedPreset;
        if (CurrentSelection != null)
            await _i7Api.ChangePresetAsync(PartNo, CurrentSelection.Msb, CurrentSelection.Lsb, CurrentSelection.Pc);
    }
```

Replace with:

```csharp
    public async Task ChangePresetAsync()
    {
        var CurrentSelection = _selectedPreset;
        if (CurrentSelection is null) return;

        // One conversation. Restoring the audition switches must land before the program change, or
        // the OUTGOING tone keeps the audition state -- and the restore writes go out through the
        // domain layer, so they need the lease handed to them explicitly.
        await using var lease = await _i7Api.BeginConversationAsync($"part {PartNo} preset change");

        // Restore any active solo/mute audition (put partial on/off switches back) BEFORE the patch
        // changes, so the outgoing tone is left intact rather than its audition state. No-op otherwise.
        if (PcmSynthToneEditor is { } pcmEditor) await pcmEditor.RestoreAuditionAsync(lease);
        if (SNSynthToneEditor is { } snsEditor) await snsEditor.RestoreAuditionAsync(lease);

        await _i7Api.ChangePresetAsync(PartNo, CurrentSelection.Msb, CurrentSelection.Lsb,
            CurrentSelection.Pc, lease);
    }
```

Note the early return moved to the top: there is no point opening a conversation when there is no
preset to change to. Confirm that reordering is safe by checking whether the original ran the audition
restore even when `_selectedPreset` was null — if it did, that was restoring switches for a part with
no preset, and skipping it is correct; say so in the report rather than silently changing it.

- [ ] **Step 4: Build and test**

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build Integra7AuralAlchemist.sln
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test Tests/Tests.csproj
```

Expected: BUILD SUCCEEDED and `Failed: 0, Passed: 411`.

- [ ] **Step 5: Verify no sequence acquires twice**

```bash
grep -n "BeginConversationAsync" Src/ --include=*.cs -r
```

Expected: the definition in `Integra7Api`/`IIntegra7Api`, the forwarder in `DomainBase`, and exactly
five call sites — three in `SynthParam.cs`, one in `MainWindowViewModel.cs`, one in `PartViewModel.cs`.
Any other call site is a sequence someone added without a plan step; report it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix: restore the audition switches and change the preset in one conversation

The switches have to be back before the patch changes, or the outgoing tone
keeps the audition state.

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

- [ ] **Step 3: Run the four scenarios**

1. **Save a user tone.** One `write tone to user memory` conversation, and the name lands in the right
   slot.
2. **Drag a parent control** — a waveform or oscillator selector. Dependent controls re-read correctly
   and no timeout warnings appear.
3. **Change a preset while a partial is soloed.** The audition state is restored before the patch
   changes, and the outgoing tone is not left holding it.
4. **A normal session.** No `Gave up after 60s` anywhere in the log.

- [ ] **Step 4: Report**

That last one is the important one. `Gave up after 60s` means a call inside a conversation did not
receive the lease and acquired its own. Report which two conversations the message names.

Also report whether a parent control feels slower to drag than before. It now holds the port across
write → reset → full domain re-read, where those used to acquire separately. That is expected; the
answer if it is too slow is to narrow the conversation, not to widen the port.

---

## Notes for the implementer

- **A lease is never acquired while one is held.** If a call inside a conversation does not receive the
  lease it acquires its own, which blocks and throws after 60 seconds. If a test hangs, that is why —
  find the call that did not get the lease.
- **A borrowed lease must not be released.** That is what `Borrowed` is for; never call `DisposeAsync`
  on a lease that was passed in.
- **Tasks 1–3 must not change behaviour.** If a test's result changes during them, something was
  threaded wrong.
- **`MotionalSurroundPartViewModel.WritePositionAsync` is deliberately left alone** — two writes for one
  puck position, where interleaving is cosmetic and self-corrects, on the highest-frequency write path
  in the application.
- **Do not merge to `main`.** The user does that, after the hardware pass.
