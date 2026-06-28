# Playable Note Rail for Friendly Tone Editors — Design Spec

**Date:** 2026-06-28
**Status:** Approved (brainstorm)

## Goal

Add a thin, playable piano-note rail (note names only, click-to-audition) to the three friendly **tone** editors — PCM Synth, SuperNATURAL Synth (SN-S), SuperNATURAL Acoustic (SN-A) — mirroring the drum editors' note rail but without per-key sound names (tones are one instrument played across the keyboard, not per-key sounds).

## Scope

In: the three friendly tone editors. Out: the drum editors (already have rails); any change to how tones are edited beyond adding the rail.

## Behaviour

- A vertical strip of all **128 MIDI notes** (note 127 at top → 0 at bottom, so low notes sit at the bottom — same orientation as the drum rail).
- Each row: a sideways piano key (white/black) with a bold "C4"-style label on C rows, plus the note name (`MidiNote.Name`) beside it.
- Notes **outside the 88-key piano range** (below A0 = note 21, or above C8 = note 108) are rendered **de-emphasized** (dimmed key + text) but remain clickable.
- Clicking a row **auditions** that note on the part's MIDI channel: `NoteOnAsync((byte)PartNo, note, 100)` → ~300 ms → `NoteOffAsync`. Best-effort (MIDI errors swallowed), exactly like the drum rail.
- The rail does not select anything or change the editor's content (tones have no per-note editor); it is purely an audition strip.

## Components (built once, reused by all three editors)

Because the strip is identical across the editors (pure note data, no per-editor state), it is a single shared control + view-model:

- **`ToneNoteViewModel`** (`Src/ViewModels/`): one row. Properties: `Note` (0..127), `NoteName` => `MidiNote.Name(Note)`, `IsBlack` => `MidiNote.IsBlack(Note)`, `IsC` => `MidiNote.IsC(Note)`, `CLabel` (= `NoteName` when `IsC`, else ""), `InPianoRange` (`Note is >= 21 and <= 108`).
- **`ToneNoteRailViewModel`** (`Src/ViewModels/`): builds the 128 `ToneNoteViewModel` rows descending (127→0), stores a `Func<int, Task>? playNote`, exposes `PlayNote(int note)` (fires the callback best-effort). `IDisposable` (no-op or trivial — rows hold no subscriptions).
- **`ToneNoteRailView`** (`Src/Views/`, UserControl): a fixed-width (~88px) strip — a scrollable `ListBox`/`ItemsControl` over `Notes`, each row a `Grid` with the sideways key (`Border.key`, `Classes.black` when `IsBlack`, `Classes.outOfRange` when not `InPianoRange`) + the note-name `TextBlock`. Code-behind `NoteRow_PointerPressed` calls `vm.PlayNote(note.Note)` (mirrors `PCMDrumKitEditorView.axaml.cs`). Reuses `DrumWhiteKeyBrush`/`DrumBlackKeyBrush`; uses two new dimmed brushes for the out-of-range rows.

Each tone editor view-model exposes `public ToneNoteRailViewModel NoteRail { get; }`, constructed with the play-note callback and disposed in the VM's `Dispose`.

## Placement

The rail is a fixed-width column at the far left of each editor's content row (`Grid.Row="1"`):

- **PCM Synth** (`PCMSynthToneEditorView.axaml`) & **SN-S** (`SNSynthToneEditorView.axaml`): content grid `ColumnDefinitions="240,*"` (partial rail | tabs) → `ColumnDefinitions="Auto,240,*"`; the note rail goes in column 0; the existing partial rail and tab area shift to columns 1 and 2.
- **SN-A** (`SNAcousticToneEditorView.axaml`): content row is a bare `TabControl` → wrap it in a `Grid ColumnDefinitions="Auto,*"` with the note rail in column 0 and the `TabControl` in column 1.

## Wiring (play-note callback)

`PartViewModel.InitializeParameterSourceCachesAsync` constructs the three tone editor VMs. Each gains a play-note callback argument (same lambda shape as the drum editors):

```csharp
async note =>
{
    try { await _i7Api.NoteOnAsync((byte)PartNo, (byte)note, 100); await Task.Delay(300); await _i7Api.NoteOffAsync((byte)PartNo, (byte)note); }
    catch { /* ignore — auditioning is non-essential */ }
}
```

Each VM builds `NoteRail = new ToneNoteRailViewModel(playNote)`.

## Resources (no hardcoded colors)

Add to `App.axaml`: `ToneKeyOutOfRangeWhiteBrush` and `ToneKeyOutOfRangeBlackBrush` (dimmed variants of `DrumWhiteKeyBrush`/`DrumBlackKeyBrush`) — referenced by the `Border.key.outOfRange` styles in `ToneNoteRailView`. The out-of-range note-name **text** is dimmed via reduced `Opacity` on the row (no extra brush). No inline hex in XAML.

## Testing

- NUnit: `ToneNoteViewModel.InPianoRange` boundaries (20 → false, 21 → true, 108 → true, 109 → false); optionally the rail builds 128 rows with `Notes[0].Note == 127` and `Notes[127].Note == 0`.
- The existing 261-test suite stays green. Build (Release) is the gate for the view/VM glue. No `--no-verify`.

## Out of scope

Sustained/held notes (click = fixed-duration audition); velocity control (fixed 100); MIDI input highlighting; any change to the partial rails or tabs beyond shifting their grid column.
