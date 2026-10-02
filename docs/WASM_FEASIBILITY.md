# Running Aural Alchemist in a browser (WebAssembly)

An assessment, not a plan of record. Written 2026-07-25 against the code as it stands, and rechecked
claim by claim against the code on 2026-10-02. Since the first version the MIDI library has changed
(OwnAudioSharp.Midi instead of managed-midi), two of the changes asked for here have been made for
reasons of their own (an app-owned event-args type, no `Thread.Sleep` in Play Note), and one claim has
stopped being true: the application now keeps a library of files on disk.

**Short answer:** the UI would port with almost no work. MIDI is the main problem, and it decides
whether this is worth doing at all — Safari and iOS cannot run it, and every other browser will ask
for permission before letting the app reach the instrument. The library is the second problem: it is
a folder of files, and a browser has no folder to give it.

---

## The big picture

Avalonia already targets the browser, so the view layer, the view models, ReactiveUI, DynamicData
and the parameter database all come along unchanged. What does not come along is
[OwnAudioSharp.Midi](https://github.com/ModernMube/OwnAudioSharp): it is a native Rust core over
WinMM, CoreMIDI and the ALSA sequencer, none of which exist in a browser sandbox, and the package
ships no WebAssembly build of that core. In a browser the only way to reach a
MIDI device is the **Web MIDI API**, through JavaScript interop.

So the work splits into four very unequal parts:

1. **Restructure the projects** so there is a browser head — mechanical, low risk.
2. **Write a Web MIDI backend** and stop the rest of the app knowing which one it has — this is the
   real work.
3. **Give the library somewhere to live** — the Library, Compare, Morph and patch-list export all read
   and write ordinary files today.
4. **Fix the handful of places that block the thread** — small, but some are hard failures in WASM
   rather than degradations.

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

`Avalonia.Controls.DataGrid` and `FluentAvaloniaUI` are managed and run in the browser. (The first
version of this document also listed `Avalonia.Controls.ItemsRepeater`; the project no longer
references it.) The owner-drawn controls (`RotaryKnobDial`, `EqCurveControl`, `PmtZoneEditorControl`,
`LayerMapControl`, `MorphPadControl`, the envelope editors) are plain `Render` overrides — nothing
platform-bound.

## 2. Assets: nothing to do

Every data file the app *ships* already goes through Avalonia's asset loader, so all of it is
embedded and works unchanged:

| What | Where |
| --- | --- |
| `parameters.bin` | `Integra7Parameters` — `avares://…/Assets/parameters.bin` |
| `Presets.csv` | `MainWindowViewModel` (constructor) → `PresetTable.Load` |
| `PartialWaveForms_*.csv` | `WaveformBanks.Default` |
| `InitTones/*.json` | `InitToneResolution`, for the *Init* button |

The other CSVs under `Assets/` are read at build time by the parameter-blob generator, not at run
time.

## 2a. Files the user keeps: the new problem

When this document was first written, the only filesystem access in the application was the Serilog
file sink. That is no longer so. Everything the application has gained since July that keeps sounds
keeps them as files:

| What | Where |
| --- | --- |
| The snapshot library, its listing and its version history | `SnapshotLibrary`, `LibraryListing`, `PatchHistory`, `LibraryViewModel` |
| Which folder the library is, and the user's preferences | `LibrarySettings` — `Documents` and `AppData` via `Environment.GetFolderPath` |
| Morph pads | `MorphPadFile`, `MorphPadViewModel` |
| Comparing against a file, saving a comparison | `CompareViewModel` |
| Seeding the library from the instrument | `SeedRunViewModel`, `SeedRefusal` (which probes the folder by writing to it) |
| Open and save dialogs | `StorageProvider` in `MainWindow.axaml.cs` |

A browser has no folder to point `LibrarySettings` at. The choices are a storage seam under these
classes with a browser implementation over the origin-private file system or IndexedDB (the library
then lives inside the browser and needs export/import to get out), or the File System Access API's
directory picker, which lets the user grant a real folder but exists only in Chromium browsers. That
last limit matters less than it sounds: Chromium is also where Web MIDI works without an add-on (see
section 5). Either way it is medium-sized new work, and it touches code that is currently well tested
against a real filesystem.

## 3. MIDI — the actual work

### How little of the MIDI library the app touches

The contact surface is unusually small, which is the good news. Every use is inside `MidiIn.cs`
and `MidiOut.cs`:

| Symbol | Uses |
| --- | --- |
| `MidiPortFactory.GetInputPortNames` / `GetOutputPortNames` | 1 each, to find the INTEGRA-7 by name |
| `MidiPortFactory.OpenInput` / `OpenOutput` | 1 each |
| `IMidiInputPort` (`MessageReceived`, `SysExReceived`, `Start`) | `MidiIn` only |
| `IMidiOutputPort` (`Send`, `SendSysEx`) | `MidiOut` only |
| `MidiMessage` | the status-plus-two-bytes shape short messages arrive and leave in |

The status bytes the app sends are its own constants (`Integra7MidiControlNos`), and `MidiWire`
translates between the library's message shapes and the byte arrays everything else works with.
Above that sit the app's own abstractions: `IMidiPort` / `IMidiLease` (conversations and leases —
see `docs/MIDI_DEVICE_ACCESS.md`) know nothing about the library.

### The seam is already in place

`IMidiOut` is library-agnostic (`ConnectionOk`, `SafeSend`), and so, since the move to
OwnAudioSharp.Midi, is `IMidiIn`:

```csharp
void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler);
```

`MidiReceivedEventArgs` is now the app's own type, a complete message as a byte array. When this
document was first written it was a Commons.Music.Midi type that leaked through
`AsyncMidiInputWrapper` and `Integra7Api`; that is no longer so, and the whole application above
`IMidiIn`/`IMidiOut` is backend-agnostic. `Tests` fakes both interfaces.

### The backend to write

A `BrowserMidiIn` / `BrowserMidiOut` pair over `navigator.requestMIDIAccess({ sysex: true })`,
using `[JSImport]`/`[JSExport]` interop:

- enumerate inputs/outputs and match on name, as the desktop implementation does;
- forward `midimessage` events into the existing `DispatchUnsolicited` path;
- `send()` for output.

Inbound sysex needs no new handling: the app already copes with chunked and concatenated messages
(`ByteUtils.SplitAfterF7`, `AsyncMidiInputWrapper`), which is exactly what a browser may hand it.

### Opening the port must become async

`MidiIn` opens its port in the constructor, and `MidiOut` on its first send. With OwnAudioSharp.Midi
both are plain synchronous calls into the native core (managed-midi's version blocked on a task with
`.Result`, which is gone). A browser backend cannot keep that shape: `requestMIDIAccess` returns a
promise, and blocking on a promise from the WASM UI thread does not merely stall — the continuation
can only run on that same thread, so it never completes. The open therefore has to move into an
async factory method that `MainWindowViewModel.InitializeAsync` awaits. Everything above it is
already `async`/`await`.

## 4. Smaller changes

- ~~**`MainWindowViewModel.PlayNoteAsync` uses `Thread.Sleep(1000)`**~~ — done 2026-10-02: it now
  awaits `Task.Delay(1000)` between note-on and note-off, which was worth doing on desktop anyway.
- **Logging.** `Program.cs` writes `logs/I7AuralAlchemist.log` through `Serilog.Sinks.File`. The
  browser head needs console-only logging, or an in-memory buffer with a "download log" button —
  worth having, since the log is how problems in this app get diagnosed.
- **Threading.** WASM is single-threaded by default. Rechecked 2026-10-02: no `new Thread`, no
  `.Wait()`, no `.Result`, no `Thread.Sleep`. The `SemaphoreSlim` / `lock` / `Interlocked` uses
  (35 now, most of them in `EditJournal`, the port lease and `StudioSetSnapshotService`) are all
  uncontended locks or cooperative waits and fine. What has appeared
  since July is **`Task.Run`, eight times**, all to keep long file work off the UI thread: library
  search and *Look inside patches*, the duplicate scan, bulk edits, deleting a selection or a set of
  duplicates, building a patch-list export, reading the library for the morph pad's corner picker, and
  the seeding pre-check. On single-threaded WASM `Task.Run` does not fail — the
  work simply runs on the one thread — so these become UI freezes rather than errors. The duplicate
  scan over a seeded library (several million comparisons) would be the one people notice; it would
  need to yield periodically, or WASM threading would need enabling.

## 5. What the browser will and will not allow

This is what decides whether the exercise is worth it.

Rechecked 2026-10-02; nothing here has moved in the app's favour.

- **Safari and iOS do not support Web MIDI at all.** WebKit has declined to ship it on fingerprinting
  grounds — MIDI devices report identifying IDs — and still has not. There is no flag to turn on, and
  because every iOS browser must use WebKit, Chrome and Firefox on iOS inherit the gap. That rules out
  every iPad and iPhone, which is otherwise the most attractive reason to want a browser build. (A
  third-party Safari extension adds Web MIDI on macOS; asking users to install it is not a plan.)
- **Chrome, Edge, Opera and Samsung Internet** support it, but since Chrome 124 *any* MIDI access asks
  the user for permission, not only SysEx. **Firefox 108+** supports it, but the first request still
  makes the user install a Site Permission Add-On.
- **SysEx needs an explicit grant.** `requestMIDIAccess({ sysex: true })` is what triggers the
  prompt. This application is *entirely* SysEx — every parameter read and write — so a user who
  declines gets a completely dead app, not a degraded one. It also requires a secure context
  (HTTPS, or localhost).

## 6. Unknowns worth a spike before committing

- **Bulk reads.** Loading tone names is hundreds of request/reply round trips, and seeding the library
  from the instrument is thousands. Nothing says Web MIDI cannot do it, but the reply deadline (1.5 s,
  counted from the request) was tuned against desktop latency and may need revisiting.
- **Large SysEx.** Implementations differ in how they deliver long messages. The fragment handling
  is already there; whether it is *enough* is an empirical question.

## 7. Rough shape of the effort

| Step | Size |
| --- | --- |
| Split into library + desktop head + browser head | small |
| Async port open, browser logging (`IMidiIn` de-leak and `Task.Delay` already done) | small |
| Web MIDI backend over JS interop | the bulk of it |
| A storage seam under the library, with a browser implementation | medium |
| Make the `Task.Run` scans yield, or enable WASM threading | small to medium |
| Hardware testing through a browser | unknown until the spike |

The cheapest way to learn whether this is real: build the browser head with MIDI stubbed out and see
the UI run, then spike **only** `requestMIDIAccess({sysex:true})` plus one identity request against
the actual instrument. That answers the permission story and the SysEx story in an afternoon,
before any refactoring is committed to.

---

Sources for the browser-support section, as checked on 2026-10-02:
[Can I use: Web MIDI API](https://caniuse.com/midi),
[Chrome: access to MIDI devices now requires user permission](https://developer.chrome.com/blog/web-midi-permission-prompt),
[Firefox: enable Web MIDI everywhere (site permission add-on)](https://bugzil.la/1795025),
[Safari-WebMIDI extension](https://github.com/triglav-modular/Safari-WebMIDI).
