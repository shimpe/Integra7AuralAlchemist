# How a control and the INTEGRA-7 stay in step

**Last updated:** 2026-07-20

This describes what happens between a control on screen and the hardware, in both directions. It is
the layer *above* [MIDI_DEVICE_ACCESS.md](MIDI_DEVICE_ACCESS.md), which covers who owns the MIDI port
and how a conversation is held. Read this one to understand where a value comes from and where it
goes; read that one when you need to know why a sequence must be atomic.

---

## The big picture

There is one model object per parameter: a **`FullyQualifiedParameter`** (an *FQP*). It knows the
parameter's address on the device and its current value. It is not a copy of what the device holds —
it *is* the application's answer to "what is this parameter set to".

Every control on screen is, directly or indirectly, a view of one FQP. Every message from the device
ends up writing one. That is what makes the two directions meet:

```
                  ┌──────────────────────────────────────┐
                  │        FullyQualifiedParameter       │
   controls ─────▶│   address + value + INotifyProperty  │◀───── device
   (write)        │              Changed                 │       (read)
                  └──────────────────────────────────────┘
                          │                    ▲
      the control changes │                    │ a message arrives,
      it, then it is sent │                    │ it is parsed into the FQP,
      to the device       ▼                    │ the FQP notifies
                     INTEGRA-7 ────────────────┘
```

Two consequences worth holding on to:

- **Nothing pushes values into controls.** A control updates because the FQP it watches raised
  `PropertyChanged`. It does not matter whether the change came from the user, from a hardware read,
  or from a full domain resync — the notification is the same.
- **A write updates the model first, then the device.** So the UI never waits for the hardware to
  agree before showing what the user just did.

### Where the FQPs live

FQPs are grouped into **domains** (`DomainBase`). A domain is one addressable block on the device —
"PCM Synth Tone Common for part 3", "Studio Set Part 7" — holding every FQP in that block. A domain is
the unit that gets read and written as a range, and `Integra7Domain` owns them all.

---

## Direction 1: a control changes something

### There are two front doors

The application shows the same parameters two ways, and they take different routes.

| | friendly editors (PCM Synth, SN-S, drums, …) | raw parameter grid |
|---|---|---|
| control binds to | `ParamInt` / `ParamString` / `ParamBool` | the FQP, through a generated template |
| write is triggered by | the wrapper's `Value` setter | a `UpdateMessageSpec` on the `"ui2hw"` bus |
| throttled | **per parameter** | **globally, one stream for everything** |
| handled by | `ThrottledParameterWriter` | `MainWindowViewModel.UpdateIntegraFromUiAsync` |

The throttling difference is the one that matters in practice. `ThrottledParameterWriter` groups by
key before throttling (`ThrottledParameterWriter.cs:25-27`), so two envelope handles dragged together
both get written. The raw grid's single `"ui2hw"` stream collapses everything within the window to the
last message, whatever parameter it was for (`MainWindowViewModel.cs:650`).

Both windows are `Constants.THROTTLE` = **250 ms** (`Constants.cs:12`).

### What a write actually does

Taking the friendly path, from `ParamInt.Value` (`SynthParam.cs:57-84`):

```csharp
public int Value
{
    set
    {
        value = Math.Clamp(value, _min, _max);
        if (_value == value) return;              // no-op writes go no further
        this.RaiseAndSetIfChanged(ref _value, value);
        if (!_suppress) Enqueue();                // _suppress: see "echo suppression" below
    }
}
```

`Enqueue` hands a closure to the throttled writer, keyed by domain + path
(`SynthParam.cs:50`). When the window closes, that closure runs:

```csharp
await using var lease = await _domain.BeginConversationAsync($"edit {_p.ParSpec.Path}");
await _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(...), lease);
```

`DomainBase.WriteToIntegraAsync(path, displayValue, lease)` is two steps
(`DomainBase.cs:125-129`):

1. **`ModifySingleParameterDisplayedValue`** — convert the display string to the raw value and store it
   in the FQP. This is the moment the application's model changes; every control watching that FQP
   updates now.
2. **`WriteToIntegraAsync(path, lease)`** — find the FQP, compute its full address, and send a data
   transmission (`FullyQualifiedParameter.cs:144-150`).

### Parent parameters cost more

Some parameters reinterpret others. Change a wave group and the wave numbers underneath it mean
something different; change an MFX type and its per-type parameters are a different set entirely.
These are marked `IsParent`, and writing one does three things under a single lease
(`SynthParam.cs:79-83`):

```csharp
await _domain.WriteToIntegraAsync(path, value, lease);
if (_p.ParSpec.IsParent)
{
    await WaveOutOfRangeReset.ApplyAsync(_domain, _p, WaveformBanks.Default, lease);
    await _domain.ReadFromIntegraAsync(lease);   // re-read the whole domain
}
```

The re-read is what makes the dependent controls show the truth rather than values that were valid
under the old parent. It must see the state *these* writes produced, which is why all three share one
conversation — see [MIDI_DEVICE_ACCESS.md](MIDI_DEVICE_ACCESS.md) for what a lease guarantees.

This is also why a parent edit feels heavier than a plain one: it is a write, a reset, and a full
domain read, and it holds the port for all three.

### Motional Surround goes its own way

`MotionalSurroundViewModel` has its own per-key write pipeline rather than using
`ThrottledParameterWriter` (`MotionalSurroundViewModel.cs:52-59`). The keys are position-shaped
(`pos:<part>`, `val:<path>`), so dragging a puck diagonally flushes both axes instead of one
superseding the other. It is also the one path deliberately left non-atomic, because it is the
highest-frequency write in the application.

---

## Direction 2: the device says something

### What arrives, and how it is classified

Every inbound message that nobody is waiting for reaches `MidiIn.DispatchUnsolicited`
(`MidiIn.cs:128-145`), which sorts it into exactly three cases:

| the message is | what happens |
|---|---|
| a **data set** (DT1) | `UpdateFromSysexSpec` on the `"hw2ui"` bus |
| **part of a preset change** | `UpdateSetPresetAndResyncPart` for that channel |
| anything else | logged and dropped |

"Part of a preset change" means a bank select MSB, a bank select LSB, or a program change
(`Integra7Api.cs:480-505`). The device sends all three when you turn the patch dial, which is why the
subscriber is throttled — otherwise one dial turn would trigger three resyncs.

There is a second way in. While a read is in progress, the lease's reader owns the MIDI input, so
anything that is not the awaited reply is **deferred** rather than dropped, and replayed through this
same `DispatchUnsolicited` once the port is free. A front-panel preset change made during a part load
therefore arrives at the handlers below exactly as if the application had been idle — just later. See
[MIDI_DEVICE_ACCESS.md](MIDI_DEVICE_ACCESS.md).

### A data set becomes parameter updates

`UpdateUiFromIntegraAsync` (`MainWindowViewModel.cs:699-732`) parses the sysex into a list of
parameter updates, then makes one decision: **was anything high-impact?**

```csharp
var ParentControlModified = parameters.Any(spec => spec.Par.ParSpec.IsParent);
var PresetChanged = parameters.Any(spec =>
    spec.Par.ParSpec.Path.Contains("Tone Bank Select") ||
    spec.Par.ParSpec.Path.Contains("Tone Bank Program Number"));
```

- **No** → write each reported value straight into its FQP. Cheap, no device traffic.
- **Yes** → re-read each affected domain from the device in full. The reported values cannot be
  trusted to describe the rest of the block, because their meaning just changed.

The parser itself (`SysexDataTransmissionParser.ConvertSysexToParameterUpdates`) walks the payload
address by address and is deliberately loud about what it cannot understand — an unknown address stops
the walk rather than looping, and every rejection logs, because a silently dropped update is the
hardest kind to notice.

### How a control finds out

It watches its FQP. `ParamInt`, `ParamString` and `ParamBool` all subscribe in their constructors and
respond the same way (`SynthParam.cs:94-98`):

```csharp
private void OnModelChanged(object? s, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
    Dispatcher.UIThread.Post(ApplyFromModel);
}
```

The `Post` matters: the change usually arrives on a thread-pool thread, because the MessageBus
subscribers are throttled and throttling schedules off the UI thread.

### What `ForceUiRefresh` is *not* for

`ForceUiRefresh` looks like the thing that updates the display. It is not
(`PartViewModel.cs:1224-1229`):

> The displayed values themselves update via `INotifyPropertyChanged`. [`ForceUiRefresh`] re-evaluates
> the DynamicData filters/visibility for the affected section.

It answers "**which** parameters should be on screen", not "what do they say". After a parent change,
a different set of parameters is relevant, and the source caches have to be told to re-filter. If you
are chasing a value that will not update, `ForceUiRefresh` is the wrong place to look — check that
`PropertyChanged` fired.

That is the `ResyncNeeded == false` branch. The flag is literally `ParSpec.IsParent`
(`MainWindowViewModel.cs:734-737`), and when it is **true** the method does something quite different:
it refreshes nothing locally and instead posts `UpdateResyncPart` (`PartViewModel.cs:1311-1317`),
re-entering the throttled resync path and re-reading from the device. So the same call is either "just
re-filter" or "go ask the hardware again", depending on whether a parent parameter was involved.

Two more re-entries exist by design, and are worth knowing before you chase a loop:

- A value refresh whose path contains `"Tone Bank Select"` or `"Tone Bank Program Number"` posts
  `UpdateSetPresetAndResyncPart` for its part (`PartViewModel.cs:1289-1292`).
- The resync path calls back into `ForceUiRefresh` with `ResyncNeeded: false` — explicitly, with the
  comment `/* don't cause inf loop */` (e.g. `PartViewModel.cs:1858-1859`). That `false` is what
  terminates the recursion; do not "fix" it to `true`.

### Echo suppression

Applying a model value to a control sets the control's `Value`, and setting `Value` normally enqueues a
write. That would send the device a value it just told us about. Each wrapper guards its
model-to-control direction with `_suppress` (`SynthParam.cs:100-109`):

```csharp
private void ApplyFromModel()
{
    _suppress = true;
    try { /* assign Value from the model */ }
    finally { _suppress = false; }
}
```

`ParamString` additionally updates `Options` **before** `Value`, because a `ComboBox` whose
`SelectedItem` is not yet in its `ItemsSource` clears its selection and does not recover once the list
catches up (`SynthParam.cs:181-188`).

The raw grid does the same thing in a different place: `DataTemplateProvider.BindToModel`
(`DataTemplateProvider.cs:25-39`) subscribes each generated control to its FQP, posts to the UI
thread, and wraps the apply in the same suppression flag. So both front doors follow one pattern —
**watch the FQP, marshal to the UI thread, suppress the echo** — even though they share no code.

One subtlety specific to the grid: it subscribes on *attach* rather than once at build time, because
Avalonia transiently detaches and re-attaches item containers during re-layout. Subscribing once would
leave a re-attached control permanently unsubscribed and silently dead (`DataTemplateProvider.cs:41-45`).

---

## The round trip, end to end

Turning the patch dial on the front panel of the device:

1. The device sends bank MSB, bank LSB and a program change.
2. `MidiIn.DefaultHandler` receives each one and calls `DispatchUnsolicited`.
3. Each is recognised by `CheckIsPartOfPresetChange`, so three `UpdateSetPresetAndResyncPart`
   messages are posted for that channel.
4. The subscriber's 250 ms throttle collapses them into one.
5. `SetPresetAndResyncPartAsync` reads the part's bank and program numbers back, matches them to a
   preset, and updates the selection. If the part was ever opened, it resyncs the whole part.
6. Every domain read along the way copies values into FQPs, which raise `PropertyChanged`, which the
   parameter wrappers post to the UI thread — and the controls redraw.

The "Syncing…" overlay is visible for steps 5-6, driven by `SyncCounter`
(`Src/Models/Services/SyncCounter.cs`), which counts overlapping sync operations so the overlay
disappears when the last one finishes rather than the first.

---

## Threading, in one place

| runs on | what |
|---|---|
| UI thread | control setters, `ApplyFromModel`, DynamicData refreshes |
| thread pool | every MessageBus subscriber (throttling schedules there), every throttled write |
| thread pool | the MIDI reader that hands messages to `DispatchUnsolicited` |

The rules that follow:

- **Touching a bound property from a subscriber means posting to the UI thread.** `Dispatcher.UIThread.Post`
  is the codebase's idiom; `ObserveOn(RxSchedulers.MainThreadScheduler)` is used where the update is
  a stream rather than an event.
- **Subscribers can overlap.** They are `async void`, so a second message can start being handled
  before the first finishes. Anything they mutate must tolerate that — this is exactly why the sync
  overlay's counter is interlocked.

---

## Where everything lives

| file | what it is |
|---|---|
| `Src/Models/Data/FullyQualifiedParameter.cs` | the model object both directions meet at |
| `Src/Models/Domain/DomainBase.cs` | a block of FQPs; read/write a range or a single value |
| `Src/ViewModels/SynthParam.cs` | `ParamInt`/`ParamString`/`ParamBool` — control ⇄ FQP, both directions |
| `Src/Models/Services/ThrottledParameterWriter.cs` | per-key write debounce for the friendly editors |
| `Src/DataTemplates/DataTemplateProvider.cs` | the raw grid's controls; sends `"ui2hw"` |
| `Src/Models/Data/UpdateMessageSpec.cs` | the four MessageBus message types |
| `Src/Models/Services/MidiIn.cs` | `DispatchUnsolicited` — where unrequested messages are sorted |
| `Src/Models/Services/SysexDataTransmissionParser.cs` | inbound sysex → parameter updates |
| `Src/ViewModels/MainWindowViewModel.cs` | the four subscribers, and the resync entry points |
| `Src/ViewModels/PartViewModel.cs` | `ForceUiRefresh` — visibility, not values |

---

## Known limits

- **Rx `Throttle` is a debounce, not a rate limit.** It emits only the *last* item of a burst and drops
  the rest. That is exactly what is wanted for the MSB/LSB/PC triple, but it applies just as much to
  the inbound `"hw2ui"` stream: two *unrelated* data-set messages arriving within 250 ms of each other
  lose the first, and its values are never applied. All four subscriptions share this behaviour.
- **The raw grid's global throttle** collapses unrelated parameters for the same reason. Editing two
  parameters within 250 ms in that view can drop the first. The friendly editors do not have this
  problem, because they throttle per key.
- **The low-impact refresh looks redundant.** `MainWindowViewModel.cs:711-716` calls
  `ForceUiRefresh(parameters.First().Par)` inside the loop, rather than for the `spec` being iterated.
  The natural worry is that it is carrying the dependent-control visibility update — it is not.
  `IsParent` is set at build time to exactly the set of paths some parameter names as `ParentCtrl` or
  `ParentCtrl2` (`Tools/ParameterBlobGenerator/Integra7ParameterDatabaseAnalyzer.cs:132-145`), and this
  branch runs only when no `IsParent` parameter changed. So nothing any filter watches has changed, and
  no visibility can have. What remains is N identical refreshes of one section, which would additionally
  target the wrong section if a data-set message ever spanned two domains.
- **`SyncInfo` is written from thread-pool threads** by the resync loops, unlike `IsSyncing`. It drives
  a status string rather than blocking the UI, so it has been left alone.
- **A high-impact inbound change re-reads whole domains.** Correct, but it is the most expensive thing
  the inbound path can do, and it is triggered by a string match on the parameter path
  (`"Tone Bank Select"`, `"Tone Bank Program Number"`).
