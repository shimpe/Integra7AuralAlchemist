# Per-part load state machine — design

**Date:** 2026-07-19
**Status:** approved, ready for planning
**Scope:** `PartViewModel`'s load lifecycle only. The companion proposal — a single owner for MIDI
device access owning whole conversations — is deliberately **not** in this spec. See
"Out of scope" below.

## Problem

`PartViewModel` tracks whether a part has been loaded using six fields that each answer one question
correctly, but which together encode the states implicitly:

| field | declared | question it answers |
|---|---|---|
| `_deferredInit` | `PartViewModel.cs:265` | is a load running / finished / never started |
| `_initCts` | `:268` | how to abandon a running load |
| `_isLoading` | `:1405` | may the user touch the preset list |
| `_everOpened` | `:1418` | skip this part on refresh, or re-initialize it |
| `_presetGeneration` | `:272` | is my program change still the newest one |
| `_applyingPresetFromDevice` | `:1409` | did the user ask for this preset, or the device report it |

Lazy part loading (merged 2026-07-18, `06d1c07`) made these flags interact for the first time and
produced ten defects. Three concrete faults remain in the current code:

1. **A completion window.** `RunDeferredInitAsync`'s `finally` sets `IsLoading = false` (`:1448`), but
   the task it belongs to only completes *after* the method returns. In between, `IsLoading` is false
   while `_deferredInit is { IsCompleted: false }` is still true — so a preset change landing there is
   accepted **and** takes the `wasInitializing` branch (`:1135`).

2. **The device echo.** `_applyingPresetFromDevice` suppresses only the refusal at `:1117`. It does not
   stop the fire-and-forget at `:1148`. So when the device reports which patch a part holds — from the
   background name loader (`:1164`), startup connect (`:1346`), or a resync (`:1872`,
   `MainWindowViewModel.cs:789`) — the application replies by sending a program change **back for the
   patch the device already holds**, and starts a reload. `Integra7Api.ChangePresetAsync:450` then
   posts `UpdateResyncPart`, which races that reload.

3. **A zero-settle read.** `Integra7Api.ChangePresetAsync` (`:445-451`) posts `UpdateResyncPart`
   immediately after its three MIDI messages. On a loaded part, that resync is what refreshes after a
   user preset change — and it starts reading the instant the program change goes out. The
   `Task.Delay(PresetSettleMilliseconds)` that exists to prevent exactly this (`PartViewModel.cs:1794`)
   sits on the reload path, which is unreachable whenever the preset list is disabled during a load.
   The path works today only because the MessageBus hop and `EnsurePreselectIsNotNullAsync` add
   incidental latency.

Fault 2 is the most likely explanation for the resync deadlock recorded in the
`device-access-model-redesign` memory: two flows re-reading one part, started by the device merely
reporting its own state. That deadlock was never root-caused; it is currently unreachable because the
preset list is disabled during a load, not because it was understood.

## Goal

Replace the six fields with one explicit state machine that is a pure, unit-testable object, and fix
faults 1–3 as a consequence of making the transitions explicit.

## Architecture

A new pure class, `Src/Models/Services/PartLoadState.cs`, holding the phase, the epoch and the
reload-pending marker, and exposing transition methods that **return what the caller must do**. It has
no MIDI, no tasks and no ReactiveUI, matching the existing helper pattern in that directory
(`VelocityMapping`, `RailScrollMapping`, `MidiHandlerOwnership`, `NameListEndMarker`).

`PartViewModel` owns one instance, still owns the `Task` and the `CancellationTokenSource`, and
performs the side effects the transitions ask for. The state machine decides; the view model executes.

### Types

```csharp
public enum PartLoadPhase { NeverOpened, Loading, Loaded, Abandoned }

public enum PresetSource { User, Device }

public enum LoadOutcome { Completed, Cancelled, Failed }

public enum OpenDecision { StartLoad, JoinExisting, None }

/// <summary>What PartViewModel must do about a preset change. Accepted == false means the change is
/// refused and the caller must snap the bound list back to the real selection.</summary>
public readonly record struct PresetDecision(
    bool Accepted,
    bool SendProgramChange,
    bool CancelCurrentLoad,
    bool Reload,
    int Epoch);
```

### States

| state | meaning | replaces |
|---|---|---|
| `NeverOpened` | tab never opened; no tone state exists | `_everOpened == false` |
| `Loading` | the read sequence is running | `_isLoading`, `_deferredInit is { IsCompleted: false }` |
| `Loaded` | usable tone state | `_deferredInit is { IsCompletedSuccessfully: true }` |
| `Abandoned` | opened once, then cancelled or failed | `_everOpened && _deferredInit is null` |

Two of the six fields do not become states. They become arguments and members, which removes the
ambient mutation:

- `_applyingPresetFromDevice` → the `PresetSource` argument. `PreSelectConfiguredPreset` calls a new
  `ApplyDevicePreset(p)` method; the XAML-bound setter passes `PresetSource.User`. No `try`/`finally`
  flag flipping and no reentrancy window.
- `_presetGeneration` → `Epoch` inside `PartLoadState`, bumped on every accepted preset change and
  checked through `IsCurrent(epoch)`. Same guard, one owner.

## Transitions

### `RequestOpen()`

| state | returns | new state |
|---|---|---|
| `NeverOpened` | `StartLoad` | `Loading` |
| `Abandoned` | `StartLoad` | `Loading` |
| `Loading` | `JoinExisting` | `Loading` |
| `Loaded` | `None` | `Loaded` |

`EnsureInitializedAsync` maps directly onto this: `StartLoad` creates the CTS and the task,
`JoinExisting` returns the existing task, `None` returns `Task.CompletedTask`.

### `LoadFinished(LoadOutcome)`

| outcome | new state | `ReloadPending` |
|---|---|---|
| `Completed` | `Loaded` | cleared |
| `Cancelled` | `Abandoned` | cleared |
| `Failed` | `Abandoned` | cleared |

This closes fault 1. The phase changes in one move rather than through a `finally` that runs before
the task completes, so there is no interval in which the part is neither loading nor loaded.

### `RequestPreset(PresetSource)`

| state | source | accepted | send PC | cancel load | reload | epoch |
|---|---|---|---|---|---|---|
| `Loading` | User | **no** | — | — | — | unchanged |
| `Loading` | Device | yes | no | yes | yes | bump |
| `Loaded` | User | yes | **yes** | no | yes | bump |
| `Loaded` | Device | yes | no | no | yes | bump |
| `Abandoned` | User | yes | yes | no | no | bump |
| `Abandoned` | Device | yes | no | no | no | bump |
| `NeverOpened` | User | yes | yes | no | no | bump |
| `NeverOpened` | Device | yes | no | no | no | bump |

Notes on the rows that are not obvious:

- **`Loading` + User is refused.** This preserves today's behaviour, but as a property of the table
  rather than an `if (IsLoading && !_applyingPresetFromDevice)` guard that scrolling the list was once
  enough to route around. The caller raises a property-changed notification so the bound list snaps
  back to the real selection.
- **`Loading` + Device cancels and restarts, and sends nothing.** The running load's earlier reads
  describe a patch the device no longer holds, so they are stale and the load must restart. No program
  change is sent, because the device is the one reporting the change. This row is fault 2.
- **`Loaded` + Device does not send a program change either** — same reason — but still reloads, so
  the part's parameters match what the device now holds.
- **`Abandoned` and `NeverOpened` never reload.** There is no loaded state to refresh. A later
  `RequestOpen` (from opening the tab, or from `ResyncPartAsync`, which calls
  `EnsureInitializedAsync` at `:1862`) does the loading.
- **`Cancel load` is only ever true for `Loading`**, so the view model can assert the CTS is non-null
  when it is asked to cancel.

### Reload ownership

`Reload == true` sets `ReloadPending`. `PartViewModel` then runs the single deterministic path: send
the program change if asked, wait `PresetSettleMilliseconds`, check `IsCurrent(epoch)`, reload.
`LoadFinished` clears `ReloadPending`.

`ResyncPartAsync` returns early while `ReloadPending` is set. That is what drops the untimed
`UpdateResyncPart` posted by `Integra7Api.cs:450`, fixing fault 3: the refresh after a preset change
now always takes the settle delay, and happens exactly once instead of twice.

Resyncs that do **not** follow a preset change — a move on the INTEGRA-7's own front panel, an SRX
load, `ResyncAllPartsAsync` — are unaffected, because `ReloadPending` is only ever set by an accepted
preset change.

## Derived properties

`PartViewModel` keeps the names its callers and XAML already use:

```csharp
public bool IsLoading             => _load.Phase == PartLoadPhase.Loading;
public bool IsInitialized         => _load.Phase == PartLoadPhase.Loaded;
public bool NeedsReinitialization => _load.Phase == PartLoadPhase.Abandoned;
public bool WantsRefresh          => _load.Phase is PartLoadPhase.Loaded or PartLoadPhase.Abandoned;
```

`IsLoading` must still raise a change notification, because `MainWindow.axaml:217` binds
`IsEnabled="{Binding !IsLoading}"` on the preset selector. `PartViewModel` raises it whenever a
transition changes the phase; `PartLoadState` itself stays free of ReactiveUI.

`WantsRefresh` replaces the `IsInitialized || NeedsReinitialization` pair repeated at
`MainWindowViewModel.cs:737`, `:763` and `:794`, which means exactly "opened at some point, and not
currently loading".

## Code that is removed

- The six fields listed in "Problem".
- The `reload` parameter of `ChangePresetAndReloadAsync` (`:1780`). With `Loading` + User refused,
  `wasInitializing` is false in every reachable state, so the parameter only ever carried `false`.
  The method keeps its delay-and-reload tail, which is now driven by `PresetDecision.Reload` instead.
- The `try`/`finally` around `SelectedPreset = p` in `PreSelectConfiguredPreset` (`:1180-1188`),
  replaced by the `ApplyDevicePreset` call.

## Error handling

A load that throws reports `LoadOutcome.Failed`, moving to `Abandoned` rather than caching the
failure — the same intent as `_deferredInit = null` in the current `catch` (`:1442`), but now the
phase says so explicitly and `WantsRefresh` picks the part up on the next refresh. Cancellation is
reported as `LoadOutcome.Cancelled` and reaches the same phase; the two are distinguished only in the
`UserActionLog` message, matching the existing wording at `:1430-1437`.

`PartLoadState` throws no exceptions. Every method is total: each returns a decision for every phase,
so there is no invalid-transition case for `PartViewModel` to handle.

## Testing

`PartLoadState` is pure, so the tables above become one NUnit fixture,
`Tests/TestPartLoadState.cs`, with no hardware and no view model:

- `RequestOpen` — 4 cases, one per phase, asserting both the return value and the resulting phase.
- `LoadFinished` — 3 cases, asserting phase and that `ReloadPending` is cleared.
- `RequestPreset` — 8 cases, one per row, asserting all five fields of `PresetDecision`.
- The completion window (fault 1): `Loading` → `LoadFinished(Completed)` → `RequestPreset(User)` is
  accepted with `CancelCurrentLoad == false`, proving there is no interval where a preset change both
  passes the refusal and takes the cancel branch.
- The device echo (fault 2): every `PresetSource.Device` row has `SendProgramChange == false`.
- Epoch supersede: two consecutive accepted changes; `IsCurrent` is false for the first epoch and true
  for the second.
- `ReloadPending` is set by every row with `Reload == true` and by no other, and is cleared by
  `LoadFinished`.

The existing 314 tests must stay green. `IsLoading`, `IsInitialized` and `NeedsReinitialization` keep
their names and meanings, so no XAML and no `MainWindowViewModel` call site changes except for
adopting `WantsRefresh`.

Build and test with the user-local SDK: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"`. The system
`dotnet` is 8/9 and too old for this solution.

## Hardware verification

Faults 2 and 3 are behaviour changes, so the branch needs a pass on the real device:

1. Start up, switch to a part while the application is still initializing, then pick a preset —
   the case that produced the original run of defects. The list stays disabled during the load and
   the picked preset applies afterwards.
2. Change a preset on a loaded part. The log shows one refresh, not two, and no read is issued
   inside the settle delay.
3. Change a preset on the INTEGRA-7's own front panel while a part is loading. The application must
   not send a program change back. This is the untested path into the unexplained deadlock, and is
   the reason this scenario is called out rather than left to incidental testing.
4. Change a preset on the front panel of a loaded part. The part refreshes once, with no echo.

## Out of scope

The single owner for MIDI device access — a queue or actor through which every read, write and burst
passes, owning whole conversations rather than individual requests — is a separate spec. A survey done
while writing this one found twelve concurrency hazards in the current device layer, including
`ChangePresetAsync` bypassing the semaphore entirely (`Integra7Api.cs:445-451`) and unsolicited device
messages being delivered as if they were the reply to an in-flight read (`MidiIn.cs:133-134` versus
`AsyncMidiInputWrapper.cs:27-36`). None of those are addressed here.

This spec narrows the part's own lifecycle so that the device-access work has one fewer moving part to
reason about. It does not make the device layer safe.

The `[UI]` interaction logging (`UserActionLog`) stays exactly as it is. Reconstructing user actions
from raw MIDI bytes was the main obstacle to diagnosing the original defects, and the `BEGIN`/`END`
brackets around a part load are what made a silent stall visible.
