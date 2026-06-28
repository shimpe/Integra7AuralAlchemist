# Integra-7 Aural Alchemist

A cross-platform editor (Linux and Windows so far) for the **Roland INTEGRA-7** sound module. On top of
full access to every documented parameter, it adds **friendly, musical visual editors** for each tone
type — with draggable envelope/filter graphs, playable note rails, and live two-way sync with the
hardware over MIDI/SysEx.

## Features

### Friendly visual editors (per tone type)
- Purpose-built editors for **PCM Synth Tone**, **PCM Drum Kit**, **SuperNATURAL Synth Tone**,
  **SuperNATURAL Acoustic Tone**, and **SuperNATURAL Drum Kit**, presenting the sound the way a musician
  thinks about it (wave/oscillator, pitch, filter, amp, LFO, effects) instead of raw addresses.
- **Interactive graphs** for what's easier to shape visually than to type:
  - Multi-stage **pitch / filter (TVF) / amp (TVA) envelopes** — drag the points, or edit the numbers.
  - **ADSR envelopes** (SuperNATURAL) with amp + filter overlaid on shared axes.
  - **Filter-response curves** (drag = cutoff / resonance) and **LFO waveform** previews.
  - A **Motional Surround** spatial editor (drag sound positions in 2D).
  - **Key × velocity zone** maps for PCM drum WMT / synth PMT layers.
- A **partial rack** with cards showing each partial at a glance, including small read-only
  **envelope/filter preview thumbnails** (pitch = purple, filter = amber, amp = blue) that update live
  while you edit.
- **Solo / Mute / on-off auditioning** of partials (soloing or enabling a partial also selects it), plus
  per-partial **Copy / Paste / Init**.

### Play & audition
- A **playable note rail** beside the tone editors (press-and-hold to sustain a note) and
  **click-to-audition** drum rails labelled with each kit's drum names.

### Expansion-aware (SRX / ExSN)
- Load **SRX** and **ExSN** expansions from the UI into the unit's four slots.
- Pickers show only **currently-loaded** expansions: the PCM **Wave Group** (SRX board) selector and the
  SuperNATURAL-Acoustic **instrument** picker list only loaded boards; a patch's own board/instrument is
  kept and flagged "(not loaded)" when its expansion isn't installed.
- **Bank-aware waveform names** (correct per SRX board), and preset browsing filtered to loaded
  expansions.

### Full raw-parameter access
- Every documented parameter is still exposed in **"Advanced"** tabs — grouped under a single Advanced
  tab per part, alongside the friendly Editor — with search/filtering of presets and parameters.
- The friendly editors' "Advanced …" buttons jump straight to the matching raw tab.

### Hardware sync & presets
- **Two-way live sync**: edits made on the INTEGRA-7 update the UI, and edits in the tool are sent to the
  INTEGRA-7.
- Save an edited tone back to the INTEGRA-7 as a **user preset**.

## Tech stack / dependencies

- **.NET 10**, C# (nullable enabled, Avalonia compiled bindings).
- **[Avalonia](https://avaloniaui.net/) 12** for the cross-platform UI (Desktop, DataGrid, ItemsRepeater,
  Inter font), with **[FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia)** controls.
- **[ReactiveUI](https://www.reactiveui.net/)** (ReactiveUI.Avalonia + ReactiveUI.SourceGenerators) and
  **[DynamicData](https://github.com/reactivemarbles/DynamicData)** for the MVVM / reactive layer.
- **[managed-midi](https://github.com/atsushieno/managed-midi)** for MIDI / SysEx I/O.
- **[Serilog](https://serilog.net/)** (console + file sinks) for logging.
- **NUnit** for the unit tests.
- The parameter database (`Assets/parameters.bin`) is **generated at build time** from the C# definitions
  + CSVs in `Tools/ParameterBlobGenerator`; it is git-ignored and never hand-edited.

## How to build and run from source

You need the **.NET 10 SDK** installed.

**With JetBrains Rider** (free for non-commercial use — https://www.jetbrains.com/rider/):
- Open `Integra7AuralAlchemist.sln`, then click *Run* in the *Run* menu. It builds in a few seconds and
  starts.

**From the command line:**
```sh
dotnet build Integra7AuralAlchemist.sln -c Release
dotnet run --project Src -c Release
```
The build automatically (re)generates the embedded parameter database whenever its sources change. Run
the tests with `dotnet test`.

## Some screenshots

> Note: the app has gained many friendly visual editors since these were taken — the live UI shows more.

Example of how parameters are exposed for a SuperNATURAL Drum kit (the other types of instruments are supported too)
<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Parameters.png?raw=true" width="800"/>

Example of the SRX selection screen
<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SrxLoader.png?raw=true" width="800"/>

Example of how you can filter presets and parameters in the ui
<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Filtering.png?raw=true" width="800"/>
