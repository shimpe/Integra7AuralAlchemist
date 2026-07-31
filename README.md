# Integra-7 Aural Alchemist

An editor for the **Roland INTEGRA-7** sound module. Every documented parameter is reachable, but the
point of it is the layer above that: purpose-built editors that present each of the five sound
engines the way a musician thinks about them, a librarian for your own patches, and a set of views
over the whole Studio Set at once.

It talks to the instrument over MIDI/SysEx in both directions — turn a knob on the front panel and
the screen follows; change something on screen and the module hears it.

| | | |
|:-:|:-:|:-:|
| <img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SnSynthEditor.png?raw=true" width="290"/> | <img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Mixer.png?raw=true" width="290"/> | <img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/MorphPad.png?raw=true" width="290"/> |
| a tone, engine by engine | the Studio Set, all sixteen parts | the point between several sounds |

## Getting it

**Download a release.** Each tagged release carries a self-contained archive per platform — Windows
x64, Linux x64, macOS on Apple Silicon and macOS on Intel. Nothing has to be installed first: the
.NET runtime travels inside the archive, and every sound name, picture and parameter definition is
embedded in the program itself. Unpack it and run it.

The application is not code-signed, so each platform says so once. That is the absence of a
certificate, not a statement about the files:

- **Windows** — SmartScreen shows *Windows protected your PC*. Choose *More info*, then *Run anyway*.
- **macOS** — drag *Integra-7 Aural Alchemist* into Applications, then run
  `xattr -dr com.apple.quarantine "/Applications/Integra-7 Aural Alchemist.app"` once. Without it
  Gatekeeper refuses to open the app at all.
- **Linux** — unpack the tarball and run `./Integra7AuralAlchemist`. ALSA has to be present; on
  Debian and Ubuntu that is `libasound2`.

Windows and Linux are what this has actually been used on. The macOS archives are built by the same
job and have not been tried by anyone here.

Then switch the INTEGRA-7 on, connect it by USB or MIDI, and choose the ports from the boxes at the
bottom of the window. If it found the module, the status bar says so.

If there is no release yet, or you would rather build it, see [building from
source](#building-from-source) at the end.

## The friendly editors

Each of the five engines gets its own editor, laid out around what that engine actually is rather
than around the address map. They share a shape: what the whole tone does along the top, the parts it
is made of down the left, and the selected part's detail filling the rest.

**SuperNATURAL Synth** — three partials, each with its own oscillator, filter, amplifier and LFOs.
The cards down the left are the partials; the tabs on the right are what that partial does.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SnSynthEditor.png?raw=true" width="900"/>

**PCM Synth** — four partials over the wave memory, with the wave chooser, the key × velocity zone
map for the PMT layers, and the envelopes as graphs you drag.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/PcmSynthEditor.png?raw=true" width="900"/>

Every partial card carries three read-only preview graphs — the filter's shape, the pitch envelope,
and the amplifier and filter envelopes on shared axes — which redraw as you edit, so you can see what
the other partials are doing without leaving the one you are on. **S** and **M** solo and mute; the
switch turns the partial off entirely.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/PartialPreviews.png?raw=true" width="300"/>

**SuperNATURAL Acoustic** — a two-step Family → Instrument chooser, and then the parameters that
particular instrument has. A piano offers string resonance and hammer noise; a saxophone offers
something else entirely. The list shows only instruments that are actually loaded.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SnAcousticEditor.png?raw=true" width="900"/>

**SuperNATURAL Drums** — the kit as a rail of named drums. Click one to hear it and to edit it.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SnDrumKit.png?raw=true" width="900"/>

**PCM Drums** — the same idea over the PCM kits, with the four WMT layers and the velocity map that
decides which of them sounds at which velocity.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/PcmDrumKit.png?raw=true" width="900"/>

Anything easier to shape than to type is a graph you can drag: multi-stage pitch, filter and
amplifier envelopes, ADSR envelopes with amplifier and filter on shared axes, filter-response curves
where the handle is cutoff and resonance, LFO waveform previews, and the key × velocity zone maps.
Numbers stay editable beside every one of them.

## The whole Studio Set at once

**Mixer** — all sixteen parts plus the external input as channel strips: level, pan, the two effect
sends, output assignment, mute and solo. Chorus and reverb are buses, so a send is per part while
the effect itself lives once in the Studio Set.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Mixer.png?raw=true" width="900"/>

**Layers** — one lane per part, key left to right, velocity as the height of the box within its lane.
Drag an edge to change a range, drag the body to move it. Click to select a part and to hear it at
the note and velocity under the pointer; silence means the part does not answer there.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Layers.png?raw=true" width="900"/>

**Motional Surround** — the parts as points in a room you drag them around, with room type, size and
ambience above, and a row of layout presets along the bottom.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/MotionalSurround.png?raw=true" width="900"/>

**Master EQ** — the three bands as a curve. Drag a handle sideways for frequency and up or down for
gain; double-click to flatten that band. The same panel serves the per-part EQ.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/MasterEq.png?raw=true" width="900"/>

Chorus, reverb and the MFX slots have friendly panels of their own, which change shape with the
effect type you choose.

## The library

Your own patches live in a folder of snapshot files. A snapshot is a tone or a whole Studio Set, with
a name, a category, free-text tags, notes, a rating and a favourite mark — all editable in the panel
on the right, none of which the INTEGRA-7 itself has room for.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Library.png?raw=true" width="900"/>

What the library does:

- **Search and filter** by name, notes, categories and tags, and narrow by kind, engine, category,
  rating or favourite. *Look inside patches* searches the parameter values themselves, so you can
  find every tone whose filter is a particular type.
- **Audition** — hear a snapshot in the selected part without losing what is there. The part is put
  back when you press Stop, load something else, or leave the tab.
- **Load** into the instrument, either as a tone into the selected part or as a whole Studio Set.
- **Version history** — every write keeps the version it replaced, listed by date and restorable.
  Deleting moves the file to a history folder beside the library rather than destroying it.
- **Find duplicates** — snapshots that are the same sound whatever they are called, with a tolerance
  you set for how many parameters are allowed to differ.
- **Use as the init tone** — make one of your own patches the starting point the *Init* button uses
  for that engine, in place of the bundled one.
- **Export a patch list** for a DAW, so the instrument's sounds appear by name in the track's program
  menu instead of as numbers.
- **Seed from instrument** — sweep the INTEGRA-7's own preset banks into the library in one run, so
  that everything above works over the factory sounds too, not only over what you have made.

## Comparing

Two snapshots side by side, from the library, from a file, or read straight out of the instrument.
The result is every parameter that differs, grouped by block, with a count and a filter — and a
marker for parameters one side has and the other does not.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Compare.png?raw=true" width="900"/>

It answers the question you cannot answer on the front panel: *what did I actually change?* The
result can be copied or saved as text.

## Morphing

Two to seven library tones at the corners of a pad, all of the same engine, and the point between
them is the sound. Drag the puck and the part follows continuously. A blend you like can be saved
back into the library as a patch of its own; the pad itself can be saved and reloaded.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/MorphPad.png?raw=true" width="900"/>

## Expansions

Load SRX and ExSN expansions into the module's four slots from the pictures, and the rest of the
application follows: wave-group pickers, SuperNATURAL-Acoustic instrument lists and preset browsing
all show only what is currently loaded, and waveform names resolve correctly per board. A patch that
wants a board you have not loaded keeps its own setting, flagged *(not loaded)* rather than silently
reset.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/SrxLoader.png?raw=true" width="900"/>

## Every parameter, still

Nothing is hidden. Each part and the Studio Set common area carry an **Advanced** tab holding the raw
parameter grid, with a filter box, and every friendly editor has *Advanced …* buttons that jump
straight to the matching grid. The preset browser beside it takes one box of words, each of which
has to match somewhere — the sound's name, its bank, its engine, its category, or whether it is a
factory or a user tone — and it only ever lists banks the module currently has loaded.

<img src="https://github.com/shimpe/Integra7AuralAlchemist/blob/main/Screenshot/Parameters.png?raw=true" width="900"/>

## Playing, and getting back

A playable note rail sits beside the tone editors: press and hold to sustain, and where you click
along a key sets the velocity, from 1 at the left edge to 127 at the right. Drum editors get a rail
labelled with the kit's own drum names. *Play Note* and *Play Phrase* in the status bar audition the
selected part, and *Panic!* silences everything.

Edits are journaled, so **Undo** and **Redo** work across parameters, and **Compare** rolls the whole
session's edits back so you can hear what the sound was before you started — and then forward again.
Tones can be initialised, copied between parts and pasted, saved back to the module's user memory, or
written into the library. *Randomise…* moves the groups you tick away from where they are by a
strength you choose, and leaves everything you did not tick exactly as it was — which is a way of
finding a variation on a sound rather than a way of throwing dice at one.

## Building from source

You need the **.NET 10 SDK**.

**With JetBrains Rider** (free for non-commercial use — https://www.jetbrains.com/rider/): open
`Integra7AuralAlchemist.sln` and click *Run*.

**From the command line:**

```sh
dotnet build Integra7AuralAlchemist.sln -c Release
dotnet run --project Src -c Release
```

Run the tests with `dotnet test`; there are over 1200 of them.

To produce the same archives the release job does:

```sh
dotnet publish Src/Integra7AuralAlchemist.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## How it is put together

- **.NET 10** and C#, nullable enabled, Avalonia compiled bindings.
- **[Avalonia](https://avaloniaui.net/) 12** for the user interface (Desktop, DataGrid,
  ItemsRepeater, Inter font), with **[FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia)**
  controls.
- **[ReactiveUI](https://www.reactiveui.net/) 24** (ReactiveUI.Avalonia, ReactiveUI.Reactive,
  ReactiveUI.SourceGenerators) and **[DynamicData](https://github.com/reactivemarbles/DynamicData)**
  for the MVVM and reactive layer.
- **[managed-midi](https://github.com/atsushieno/managed-midi)** for MIDI and SysEx.
- **[Serilog](https://serilog.net/)** (console and file sinks) for the log, which is worth reading
  when something does not behave: it records every action the user takes.
- **NUnit 4** for the tests.
- The parameter database (`Assets/parameters.bin`) is **generated during the build** from the C#
  definitions and CSV tables in `Tools/ParameterBlobGenerator`. It is not in version control and is
  never hand-edited; changing a definition and rebuilding is the whole workflow.

Two pieces of the design are written up rather than left to be reverse-engineered:
[`docs/MIDI_DEVICE_ACCESS.md`](docs/MIDI_DEVICE_ACCESS.md) for how the module is opened, shared and
recovered, and [`docs/UI_HARDWARE_DATAFLOW.md`](docs/UI_HARDWARE_DATAFLOW.md) for how an edit travels
between the screen and the instrument. Read them before changing either.

## Licence

GNU General Public License v3 — see [LICENSE](LICENSE).
