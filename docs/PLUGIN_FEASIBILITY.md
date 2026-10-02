# Running Aural Alchemist as a VST3 or CLAP plugin

An assessment, not a plan of record. Written 2026-07-25 against the code as it stands, and rechecked
claim by claim on 2026-10-02 — against the code, against the current state of Avalonia, NPlug and
Windows MIDI, and with one experiment on the actual instrument. Companion to `WASM_FEASIBILITY.md`.

**Short answer:** still a big job, but two of the three obstacles the first version named have
shrunk. The plugin ABI remains the *easy* part — a solved problem in .NET. **The MIDI port** is no
longer a fight on current Windows: every port is multi-client now, and this app was measured sharing
the INTEGRA-7 with a second client (see Problem 1). **The editor window** has a path on every platform,
including macOS, where the first version called it unresolved. What remains hard is everything that
comes from living in someone else's process: NativeAOT, which this code is not ready for, and
process-wide state that two instances would share.

---

## What being a plugin would actually buy

Worth naming, because it shapes how much of the below is worth paying for:

1. **The editor lives in the project window** instead of a separate app.
2. **The Studio Set is saved with the song.** When this was first written the app had no persistence
   at all. It now has Studio Set snapshots — the whole set as one JSON document, loadable back into
   the instrument — so this has become a matter of storing that document in the plugin's state and
   loading it when the project opens. The capability is new for a DAW project; the machinery is not.
3. **Automation** of parameters from the DAW timeline.

Note that none of the three needs the plugin to process audio. This would be a MIDI-effect /
instrument plugin that outputs silence, which both formats allow but neither is really shaped for.

---

## Problem 1: who owns the MIDI port

This is the one that decides the design. The first version found no comfortable answer; there is now
a likely one (Option A), not yet confirmed with a DAW as the other client.

**Option A — the plugin opens the OS MIDI port itself**, exactly as the standalone app does today
(`MidiIn`/`MidiOut` over OwnAudioSharp.Midi). Almost no code changes. The first version of this
document expected it to collide with the DAW, because a WinMM device used to be opened exclusively.
That has changed:

- **Windows.** Windows MIDI Services, now built into Windows 11, makes every MIDI 1.0 port
  multi-client regardless of driver, and routes the old WinMM API through it. **Measured
  2026-10-02:** two instances of this application open on the INTEGRA-7 at the same time, the second
  started while the first held the port. Both opened it, both passed the identity check, and both
  read all 1,164 replies of their startup sync with no timeouts. This was two copies of this app
  rather than a DAW, but a DAW reaches the port through the same service. (Windows MIDI Services is
  rolled out in phases, so an older or un-updated Windows 11 may still open ports exclusively.)
- **macOS.** CoreMIDI has always been multi-client.
- **Linux.** OwnAudioSharp.Midi uses the ALSA sequencer, which is multi-client by design.

The measurement also showed the cost. **The instrument's replies go to every client.** The first
instance received all 1,164 of the second's replies as unsolicited traffic and passed them to its
`DispatchUnsolicited` as front-panel updates. For a plugin beside its DAW that means the DAW's MIDI
input sees the editor's whole conversation (DAWs usually filter SysEx from recording, but not all
do), and two editors on one instrument would apply each other's reads. Neither is a blocker; both
are worth knowing before choosing A.

**Option B — the sysex travels through the host**, as plugin MIDI input/output events. This is the
architecturally correct answer and the risky one:

- CLAP has first-class sysex: `clap_event_midi_sysex`.
- VST3 carries it as a `DataEvent` of type `kMidiSysEx`.
- Host support is the problem. Plenty of hosts drop sysex from plugin output, or never deliver it to
  plugin input, and this is not something you can work around from inside the plugin.

Option B also breaks an assumption the device layer is built on. `docs/MIDI_DEVICE_ACCESS.md`
describes a conversation: acquire the port, send, await the matching reply, with timeouts tuned to a
port the app owns outright. Under Option B a reply cannot arrive sooner than the next audio block, so
every round trip costs at least one buffer — and reading the tone-name lists is *hundreds* of round
trips. It would still work; the timeout tuning and the progress UI would both need revisiting.

---

## Problem 2: the editor window

A plugin is handed a parent window (`HWND` on Windows, `NSView` on macOS, an X11 window on Linux) and
must draw inside it. Avalonia does not document this as a plugin scenario, but every platform now has
a way in, built on `EmbeddableControlRoot`, the top level Avalonia uses whenever it lives inside
someone else's window:

- `NativeControlHost` is the **opposite** direction — it puts native controls *inside* Avalonia — and
  is not the answer.
- **Windows:** embedding Avalonia into a foreign `HWND` is what the WinForms and WPF interop hosts do,
  so an `HWND`-parented top level is reachable.
- **macOS:** the first version of this document called this an open question. It was already
  answered: Avalonia 11.2 added a top level that lives inside a foreign `NSView` (AvaloniaUI PR
  #15932), so this project's Avalonia 12 has it.
- **Linux/X11:** `XEmbedPlug` embeds Avalonia into a foreign X11 window.

None of the three is documented or tested for plugin hosts, whose windows come and go and whose
threading rules differ per DAW. That makes this a spike per platform rather than a known recipe —
but no longer a dead end on macOS.

Two further consequences of living inside someone else's process:

- **Avalonia initialises once per process.** Several plugin instances in one DAW must share a single
  Avalonia app and create a top level each, rather than each running `AppBuilder`. The current entry
  point (`Program.Main` → `StartWithClassicDesktopLifetime`) assumes it owns the process and its
  message loop; a plugin owns neither.
- **Editor windows open and close repeatedly** while the plugin instance lives on. Today the UI is
  built once at startup and never torn down.

---

## Problem 3: NativeAOT, and what this code does that dislikes it

The .NET route into VST3 is [NPlug](https://github.com/xoofx/NPlug) — purely managed, no C++/CLI,
covering win/osx/linux on x64 and arm64, at 0.5 (June 2026, VST3 SDK 3.8) — and it is built on
**NativeAOT**. CLAP is a plain C ABI, so
a .NET NativeAOT library can export `clap_entry` directly with `[UnmanagedCallersOnly]`, no C++ shim
at all. Either way the plugin is an AOT-compiled shared library.

The app is partway there: `AvaloniaUseCompiledBindingsByDefault` is already on, which is the single
biggest AOT prerequisite for an Avalonia app. The MIDI library is ready too: OwnAudioSharp.Midi is
marked AOT-compatible and trimmable, and reaches its native core through source-generated P/Invoke
rather than reflection. What is not ready:

**`ViewLocator` resolves views by string.** It does `Type.GetType(vmName.Replace("ViewModel","View"))`
followed by `Activator.CreateInstance`. Under trimming, a type referenced only by name is not
reachable, so it is removed — and the failure is a blank panel at runtime, not a build error. Views
referenced explicitly in XAML are rooted and safe; these eleven are reached **only** through the
ViewLocator and would need rooting (or the ViewLocator replaced with an explicit map). Rechecked
2026-10-02: `StepLfoPanelView` has joined the ten the first version found, and every other view is
referenced explicitly.

```
DiscriminatedParamSectionView   PCMDrumWmtLayerView   SNDrumCompEqPanelView
LfoPanelView                    PcmLfoPanelView       SNDrumNoteEditorView
MfxPanelView                    PcmPmtPanelView       StepLfoPanelView
PCMDrumNoteEditorView           ToneNoteRailView
```

**JSON is serialised by reflection.** New since the first version: snapshots, morph pads and the
library settings go through `JsonSerializer` with no source-generated `JsonSerializerContext`.
NativeAOT turns reflection-based serialisation off by default, so all three would fail at run time —
and snapshots are exactly what a plugin's saved state would be. The snapshot already has a
hand-written converter (`SnapshotJsonConverter`), so adding a context is small; it just has to be
done, and tested under AOT.

**ReactiveUI** is the other question mark. `ReactiveUI.SourceGenerators` is already in use, which
removes much of the reflection, but a full AOT pass is the kind of thing that surfaces problems only
when you try it.

---

## Problem 4: process-wide state versus multiple instances

A standalone app can use process-wide singletons freely. A plugin cannot: two instances of the plugin
in one project share the same statics. The ones that would cross-talk:

- **`MessageBus.Current`** — the `"ui2hw"` and `"hw2ui"` buses, plus `UpdateResyncPart` and
  `UpdateSetPresetAndResyncPart`. Every instance would see every other instance's parameter edits
  and resync requests.
- **`EditJournal.Default`** — new since the first version: the undo/redo history and Compare's
  buffer. Two instances would undo each other's edits, and Compare in one would roll back the
  other's.
- **`LoadedSrxState.Default`** — the loaded expansion boards. (This one is arguably *right* to share:
  there is one instrument.)
- **`Log.Logger`** — one static logger, and a file sink pointing at a relative `logs/` path, which in
  a plugin resolves against the *host's* working directory.

Either these become per-instance (a scoped message bus is the awkward one), or the plugin refuses to
instantiate twice. Refusing is defensible for a hardware editor — there is only one instrument — and
is far cheaper.

---

## Format choice

| | CLAP | VST3 |
| --- | --- | --- |
| ABI | plain C — a .NET NativeAOT export is enough | C++ vtables; use NPlug, or write a C++ shim |
| Sysex | first-class event type | `DataEvent` / `kMidiSysEx` |
| .NET support | roll it yourself over the C header | NPlug, actively developed |
| Host reach | growing, not universal | everywhere |

If the goal is to learn whether the idea works at all, CLAP is the cheaper experiment. If the goal is
something people can actually load in their DAW, it has to be VST3 (and then also AU on macOS, which
is another wrapper again).

---

## Cheaper things that get most of the value

Worth weighing before committing, because two of the three motivations do not actually require a
plugin:

- **Save/load a Studio Set snapshot to a file** in the standalone app. That is the "recall my sounds"
  benefit, without any of the above. **Done since the first version** — the Library saves and loads
  whole Studio Sets, so a user can already keep one beside each DAW project by hand.
- **A thin plugin that owns nothing but the state**, storing a Studio Set snapshot in the project and
  sending it on load, with the existing standalone app kept as the editor. Sidesteps the entire
  editor-window and multi-instance problem.
- **Out-of-process editor**: the plugin launches the standalone app and talks to it. Some commercial
  hardware editors do exactly this, for exactly these reasons.

---

## Rough shape of the effort

| Step | Size |
| --- | --- |
| Decide MIDI routing (Option A vs B) | small — Option A now looks viable; confirm with a real DAW |
| Plugin skeleton (CLAP export, or NPlug for VST3) with no UI | small |
| NativeAOT pass: ViewLocator rooting, JSON source generation, ReactiveUI, trimming warnings | medium, with unknowns |
| Avalonia inside a host-provided window, per platform | medium to large; a path exists on all three, none proven in a plugin host |
| Per-instance state, or enforce a single instance | small if single-instance |
| State persistence (the actual new feature) | small — a Studio Set snapshot is already the state |

**The experiment that would settle it fastest**, and needs none of the refactoring: with the DAW open
and the INTEGRA-7 assigned to a MIDI track, run the standalone app and see whether it can still open
the port. Half of it has been done: on 2026-10-02 a second client opened the port while this app held
it, on Windows 11 with Windows MIDI Services. What remains is to repeat it with the DAW as the other
client, and to see whether the DAW's MIDI track records the editor's SysEx traffic. If both come out
well, Option A holds and this becomes an AOT and UI-embedding problem. If not, everything hinges on
whether your DAW passes plugin SysEx both ways — the experiment that has historically disappointed.

---

Sources consulted while writing this: [NPlug](https://github.com/xoofx/NPlug),
[Avalonia native interop docs](https://docs.avaloniaui.net/docs/app-development/native-interop),
[Avalonia discussion on hosting inside an NSView](https://github.com/AvaloniaUI/Avalonia/discussions/15719).
Added when rechecking on 2026-10-02:
[NPlug on NuGet](https://www.nuget.org/packages/NPlug/),
[Avalonia PR #15932: TopLevel embedding in a foreign NSView](https://github.com/AvaloniaUI/Avalonia/pull/15932),
[Avalonia `XEmbedPlug`](https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.X11/XEmbedPlug.cs),
[Windows MIDI Services: multi-client MIDI in Windows 11](https://blogs.windows.com/windowsexperience/2026/02/17/making-music-with-midi-just-got-a-real-boost-in-windows-11/).
