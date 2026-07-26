# Running Aural Alchemist as a VST3 or CLAP plugin

An assessment, not a plan of record. Written 2026-07-25 against the code as it stands. Companion to
`WASM_FEASIBILITY.md`.

**Short answer:** harder than the browser, and for a less obvious payoff. The plugin ABI is the
*easy* part — a solved problem in .NET. The difficulty is that this is a hardware editor with no
audio, so it has to fight the host for the MIDI port; that Avalonia has no supported way to live
inside a window the host owns; and that the .NET plugin route wants NativeAOT, which this code is
not ready for.

---

## What being a plugin would actually buy

Worth naming, because it shapes how much of the below is worth paying for:

1. **The editor lives in the project window** instead of a separate app.
2. **The Studio Set is saved with the song.** The app has no persistence today — state lives in the
   instrument — so "open the project, get the sounds back" would be a genuinely new capability.
3. **Automation** of parameters from the DAW timeline.

Note that none of the three needs the plugin to process audio. This would be a MIDI-effect /
instrument plugin that outputs silence, which both formats allow but neither is really shaped for.

---

## Problem 1: who owns the MIDI port

This is the one that decides the design, and it has no comfortable answer.

**Option A — the plugin opens the OS MIDI port itself**, exactly as the standalone app does today
(`MidiIn`/`MidiOut` over managed-midi). Almost no code changes. But on Windows a WinMM MIDI device
is typically opened exclusively: if the DAW already has the INTEGRA-7 port open for a MIDI track,
the plugin's open fails, and vice versa. Since the whole point of being in the DAW is that the DAW is
also playing the instrument, this collides in the normal case rather than the edge case. (Whether the
Roland driver is multi-client, and whether Windows MIDI Services changes this, needs checking against
the actual device before trusting either answer.)

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
must draw inside it. Avalonia does not support this as a first-class scenario.

- `NativeControlHost` is the **opposite** direction — it puts native controls *inside* Avalonia.
- Embedding Avalonia into a foreign parent does exist in practice on Windows (it is what the WinForms
  and WPF interop hosts do internally), so an `HWND`-parented top level is reachable.
- On macOS it is an open question — the Avalonia discussions on hosting inside an `NSView` end at
  "it might be possible to implement a TopLevel that holds an NSView", which is not a foundation to
  plan a cross-platform plugin on.

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
covering win/osx/linux on x64 and arm64 — and it is built on **NativeAOT**. CLAP is a plain C ABI, so
a .NET NativeAOT library can export `clap_entry` directly with `[UnmanagedCallersOnly]`, no C++ shim
at all. Either way the plugin is an AOT-compiled shared library.

The app is partway there: `AvaloniaUseCompiledBindingsByDefault` is already on, which is the single
biggest AOT prerequisite for an Avalonia app. What is not ready:

**`ViewLocator` resolves views by string.** It does `Type.GetType(vmName.Replace("ViewModel","View"))`
followed by `Activator.CreateInstance`. Under trimming, a type referenced only by name is not
reachable, so it is removed — and the failure is a blank panel at runtime, not a build error. Views
referenced explicitly in XAML are rooted and safe; these ten are reached **only** through the
ViewLocator and would need rooting (or the ViewLocator replaced with an explicit map):

```
DiscriminatedParamSectionView   PCMDrumWmtLayerView   SNDrumCompEqPanelView
LfoPanelView                    PcmLfoPanelView       SNDrumNoteEditorView
MfxPanelView                    PcmPmtPanelView       ToneNoteRailView
PCMDrumNoteEditorView
```

**ReactiveUI** is the other question mark. `ReactiveUI.SourceGenerators` is already in use, which
removes much of the reflection, but a full AOT pass is the kind of thing that surfaces problems only
when you try it.

---

## Problem 4: process-wide state versus multiple instances

A standalone app can use process-wide singletons freely. A plugin cannot: two instances of the plugin
in one project share the same statics. The ones that would cross-talk:

- **`MessageBus.Current`** — the `"ui2hw"` and `"hw2ui"` buses, plus `UpdateResyncPart`. Every
  instance would see every other instance's parameter edits and resync requests.
- **`LoadedSrxState.Default`** — the loaded expansion boards.
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
  benefit, without any of the above. It is also useful on its own.
- **A thin plugin that owns nothing but the state**, storing a Studio Set snapshot in the project and
  sending it on load, with the existing standalone app kept as the editor. Sidesteps the entire
  editor-window and multi-instance problem.
- **Out-of-process editor**: the plugin launches the standalone app and talks to it. Some commercial
  hardware editors do exactly this, for exactly these reasons.

---

## Rough shape of the effort

| Step | Size |
| --- | --- |
| Decide MIDI routing (Option A vs B) — needs a hardware experiment | small, but gates everything |
| Plugin skeleton (CLAP export, or NPlug for VST3) with no UI | small |
| NativeAOT pass: ViewLocator rooting, ReactiveUI, trimming warnings | medium, with unknowns |
| Avalonia inside a host-provided window, per platform | large; unresolved on macOS |
| Per-instance state, or enforce a single instance | small if single-instance |
| State persistence (the actual new feature) | medium |

**The experiment that would settle it fastest**, and needs none of the refactoring: with the DAW open
and the INTEGRA-7 assigned to a MIDI track, run the standalone app and see whether it can still open
the port. If it can, Option A is viable and this becomes mostly a UI-embedding problem. If it cannot,
everything hinges on whether your DAW passes plugin sysex both ways — which is the second experiment,
and the one that has historically disappointed.

---

Sources consulted while writing this: [NPlug](https://github.com/xoofx/NPlug),
[Avalonia native interop docs](https://docs.avaloniaui.net/docs/app-development/native-interop),
[Avalonia discussion on hosting inside an NSView](https://github.com/AvaloniaUI/Avalonia/discussions/15719).
