# Running Aural Alchemist in a browser (WebAssembly)

An assessment, not a plan of record. Written 2026-07-25 against the code as it stands.

**Short answer:** the UI would port with almost no work. MIDI is the whole problem, and it decides
whether this is worth doing at all — Safari and iOS cannot run it, and every other browser will
demand an explicit SysEx permission that this app cannot work without.

---

## The big picture

Avalonia already targets the browser, so the view layer, the view models, ReactiveUI, DynamicData
and the parameter database all come along unchanged. What does not come along is
[managed-midi](https://github.com/atsushieno/managed-midi): its backends are P/Invoke into WinMM,
ALSA and CoreMIDI, none of which exist in a browser sandbox. In a browser the only way to reach a
MIDI device is the **Web MIDI API**, through JavaScript interop.

So the work splits into three very unequal parts:

1. **Restructure the projects** so there is a browser head — mechanical, low risk.
2. **Write a Web MIDI backend** and stop the rest of the app knowing which one it has — this is the
   real work.
3. **Fix the handful of places that block the thread or touch the filesystem** — small, but they are
   hard failures in WASM rather than degradations.

---

## 1. Project restructure

Today `Src/Integra7AuralAlchemist.csproj` is a single `WinExe` targeting `net10.0`, with
`Avalonia.Desktop` and `Program.cs` starting a classic desktop lifetime.

A browser build needs:

- the application code in a **library** (either plain `net10.0`, or multi-targeted
  `net10.0;net10.0-browser` if any code has to differ per platform),
- the existing desktop head keeping `Program.cs`, `Avalonia.Desktop`, the app manifest and the
  `.ico`,
- a new **browser head** targeting `net10.0-browser`, referencing `Avalonia.Browser`, starting with
  `SetupBrowserApp` instead of `StartWithClassicDesktopLifetime`, and shipping the usual
  `wwwroot/index.html` + `main.js`.

The `GenerateParameterBlob` target moves with the library. Nothing about it is desktop-specific: it
runs at build time on the developer's machine and registers `Assets/parameters.bin` as an
`AvaloniaResource`, so the blob is embedded in the assembly like every other asset.

`Avalonia.Controls.DataGrid`, `Avalonia.Controls.ItemsRepeater` and `FluentAvaloniaUI` are managed
and run in the browser. The owner-drawn controls (`RotaryKnobDial`, `EqCurveControl`,
`PmtZoneEditorControl`, the envelope editors) are plain `Render` overrides — nothing platform-bound.

## 2. Assets: nothing to do

Every data file the app reads already goes through Avalonia's asset loader, so all of it is embedded
and works unchanged:

| What | Where |
| --- | --- |
| `parameters.bin` | `Integra7Parameters` — `avares://…/Assets/parameters.bin` |
| `Presets.csv` | `MainWindowViewModel.LoadPresets` |
| `PartialWaveForms_*.csv` | `WaveformBanks.Default` |

The **only** filesystem access in the whole application is the Serilog file sink in `Program.cs`.

## 3. MIDI — the actual work

### How little of managed-midi the app touches

The contact surface is unusually small, which is the good news:

| Symbol | Uses |
| --- | --- |
| `MidiAccessManager.Default` | 2 (the `MidiIn` and `MidiOut` constructors) |
| `IMidiAccess` | 2 |
| `IMidiPortDetails` | 2 |
| `IMidiInput` / `IMidiOutput` | 1 each |
| `MidiEvent.CC` / `.Program` / `.NoteOn` / `.NoteOff` | 15 — plain byte constants, trivially replaced |

Above that sit the app's own abstractions, which are already clean: `IMidiPort` / `IMidiLease`
(conversations and leases — see `docs/MIDI_DEVICE_ACCESS.md`) know nothing about the library.

### The seam that needs widening

`IMidiOut` is already library-agnostic (`ConnectionOk`, `SafeSend`). `IMidiIn` is not:

```csharp
void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler);
```

`MidiReceivedEventArgs` is a Commons.Music.Midi type, so it leaks through `AsyncMidiInputWrapper`
and `Integra7Api`. Replacing it with an app-owned event-args type is a mechanical change, and once
it is done the whole application above `IMidiIn`/`IMidiOut` is backend-agnostic. `Tests` already
fakes these interfaces, so the refactor is covered.

### The backend to write

A `BrowserMidiIn` / `BrowserMidiOut` pair over `navigator.requestMIDIAccess({ sysex: true })`,
using `[JSImport]`/`[JSExport]` interop:

- enumerate inputs/outputs and match on name, as the desktop implementation does;
- forward `midimessage` events into the existing `DispatchUnsolicited` path;
- `send()` for output.

Inbound sysex needs no new handling: the app already copes with chunked and concatenated messages
(`ByteUtils.SplitAfterF7`, `AsyncMidiInputWrapper`), which is exactly what a browser may hand it.

### Two blocking calls that must become async

```
Src/Models/Services/MidiIn.cs:57   _access = _midiAccessManager?.OpenInputAsync(...).Result;
Src/Models/Services/MidiOut.cs:58  _access = _midiAccessManager?.OpenOutputAsync(...).Result;
```

Blocking on a promise from the WASM UI thread does not merely stall — the continuation can only run
on that same thread, so it never completes. Both need an async open (a factory method rather than
work in the constructor). Everything above them is already `async`/`await`.

## 4. Smaller changes

- **`MainWindowViewModel.PlayNoteAsync` uses `Thread.Sleep(1000)`** between note-on and note-off.
  In the browser that freezes the only thread for a second. It should be `await Task.Delay(1000)`
  regardless — it blocks the UI thread on desktop too.
- **Logging.** `Program.cs` writes `logs/I7AuralAlchemist.log` through `Serilog.Sinks.File`. The
  browser head needs console-only logging, or an in-memory buffer with a "download log" button —
  worth having, since the log is how problems in this app get diagnosed.
- **Threading.** WASM is single-threaded by default. Checked: no `new Thread`, no `.Wait()`, no
  reliance on real parallelism. The `SemaphoreSlim` / `lock` / `Interlocked` uses (11 in total,
  including `SyncCounter` and the port lease) are all cooperative and fine.

## 5. What the browser will and will not allow

This is what decides whether the exercise is worth it.

- **Safari and iOS do not support Web MIDI at all.** WebKit has declined to ship it for years on
  fingerprinting grounds — MIDI devices report identifying IDs. There is no flag to turn on. That
  rules out every iPad and iPhone, which is otherwise the most attractive reason to want a browser
  build.
- **Chrome, Edge, Opera and Samsung Internet** support it by default. **Firefox 108+** supports it,
  but the first request installs a one-time Site Permission Add-On.
- **SysEx needs its own grant.** `requestMIDIAccess({ sysex: true })` triggers a separate permission
  prompt. This application is *entirely* SysEx — every parameter read and write — so a user who
  declines gets a completely dead app, not a degraded one. It also requires a secure context
  (HTTPS, or localhost).

## 6. Unknowns worth a spike before committing

- **Bulk reads.** Loading tone names is hundreds of request/reply round trips. Nothing says Web MIDI
  cannot do it, but the existing inactivity timeouts were tuned against desktop latency and may need
  revisiting.
- **Large SysEx.** Implementations differ in how they deliver long messages. The fragment handling
  is already there; whether it is *enough* is an empirical question.

## 7. Rough shape of the effort

| Step | Size |
| --- | --- |
| Split into library + desktop head + browser head | small |
| De-leak `IMidiIn`, async open, `Task.Delay`, browser logging | small |
| Web MIDI backend over JS interop | the bulk of it |
| Hardware testing through a browser | unknown until the spike |

The cheapest way to learn whether this is real: build the browser head with MIDI stubbed out and see
the UI run, then spike **only** `requestMIDIAccess({sysex:true})` plus one identity request against
the actual instrument. That answers the permission story and the SysEx story in an afternoon,
before any refactoring is committed to.
