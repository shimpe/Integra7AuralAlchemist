# Tone-level init, copy and randomise — design

**Stage 5** of `docs/superpowers/plans/2026-07-25-feature-roadmap.md`.

**Goal.** Three actions on the tone loaded into the selected part: reset it to a known starting point
(**Init**), move it to another part (**Copy** / **Paste**), and vary it under control (**Randomise**).

**Not in scope.** Blending two tones, patch morphing, locking individual parameters, randomising a
Studio Set, randomising more than one drum note at a time, and a `.syx` interchange format. Each is a
later addition that this design leaves room for rather than a gap in it.

---

## What this is built on

Nearly all of the device work already exists and is shipped:

| Existing | What it gives this stage |
| --- | --- |
| `StudioSetSnapshotService.CaptureToneAsync` | Reads every block of a tone from one part |
| `StudioSetSnapshotService.RestoreToneAsync` | Writes a tone into *any* part — it re-targets by design, and refuses an engine mismatch with a message written for the user |
| `ToneDomainNames.For(toneType, part)` | Which blocks a tone of each engine is made of |
| `DomainBase.WriteToIntegraAsync(lease)` | One transmission per block instead of one per parameter |
| `EditJournal` + `BeginGesture` | A step that spans many parameters and undoes as one press |
| `SnapshotLibrary` / `LibraryViewModel` | Somewhere for a user's own init tone to live |
| `MainWindowViewModel.ResolveSelectedToneAsync` | The part, its engine and its name, resolved once |

So Copy and Paste are almost entirely assembly. Init is a restore with a different source. Randomise
is the only one that needs new thinking, and it gets most of this document.

---

## Decisions, and why

**Operations write per block, not per parameter.** Values are applied into the domain with
`ModifySingleParameterRawValue` and each block is then bulk-written, exactly as a snapshot restore
does. That covers every parameter — including ones no friendly editor draws and ones in tabs the user
has never opened — and it costs ~10 transmissions instead of ~1,400.

**Randomise works in raw space.** New values are computed in the parameter's own `IMin..IMax`, not in
display space. Display space would mean parsing and re-formatting a string for every parameter, and it
walks into a trap this codebase has already been caught by once: a parameter whose displayed value is
not an integer (Master Tune, whose `OMin`/`OMax` are fractional) does not round-trip through an integer
formatter. Raw space is exact and needs no formatter at all.

**Nothing unmapped is ever randomised.** The category table lists what *may* move; everything else —
output assign, control assignments, receive switches, mute groups, partial and kit names, velocity
zone ranges — is untouched because no rule names it. A blocklist would have to be extended every time
the parameter database gains an entry; this fails closed instead.

**Discriminators are never randomised.** `ParSpec.IsParent` is true for exactly `MFX Type` (all five
engines) and `SuperNATURAL Acoustic Tone Common/Instrument`. Both would change what every dependent
parameter in the block *means*, so a randomise that moved one would be writing values interpreted
against a context that no longer holds. Excluding them also happens to be what a user wants: "randomise
the effects" means vary the current effect, not roll a different effect type.

**Init and Paste clear the edit history; Randomise records one undo step.** A whole-tone replacement
makes every step in the history describe a tone that is no longer loaded, which is why `Load Tone`
already clears it. Recording one instead is not viable at the top end: a PCM drum kit is 88 partials ×
148 parameters ≈ 13,000 changes, and undo replays a step one write at a time. Randomise is bounded —
category-scoped, and one drum note at most — so it stays undoable, which is the case that most needs
it. Init and Paste ask for confirmation first, and the confirmation says the action cannot be undone.

**Init loads a real tone snapshot rather than a table of values.** Five hand-authored value tables
would have to cover every parameter of every engine or leave the rest at whatever the device happened
to hold; a snapshot captured from the instrument is complete by construction and is real device data.
It also reuses the restore path unchanged, including all of its validation.

---

## Components

Four new services, all pure — no Avalonia, no MIDI, so all four are unit-tested against the real
parameter database.

### `ToneParameterCategories` (`Src/Models/Services/`)

Maps a parameter path to one of the categories below, or to nothing. Ordered prefix rules per engine,
the shape `SnsFilterRules`, `SnsOscillatorRules` and `PcmTvfRules` already use.

```csharp
public enum ToneCategory
{
    PitchAndOscillator,
    WaveChoice,
    Filter,
    Amplifier,
    LfoAndModulation,
    Effects,
    InstrumentCharacter,
}

public static class ToneParameterCategories
{
    /// <summary>The category this path belongs to, or null when it must never be randomised.</summary>
    public static ToneCategory? For(string path);

    /// <summary>Which categories have any parameter at all in this engine's blocks. The dialog
    /// disables the rest rather than hiding them, so the list does not change shape per engine.</summary>
    public static IReadOnlySet<ToneCategory> PresentIn(string toneType);
}
```

An envelope belongs to what it modulates: `Filter Env Attack Time` is Filter, `AMP Env Attack Time` is
Amplifier, `OSC Pitch Env Depth` is Pitch. That is how a user thinks about "leave the filter alone".

Rules are matched against the part of the path after the block name, longest prefix first.

**SuperNATURAL Synth Tone** (`Common`, `Common MFX`, `Partial 1..3`)

| Prefix | Category |
| --- | --- |
| `OSC Pitch Env`, `OSC Pitch`, `OSC Detune`, `OSC Pulse Width`, `Super Saw Detune`, `Octave Shift`, `Pitch Bend Range`, `Portamento`, `Analog Feel` | Pitch & oscillator |
| `OSC Wave`, `Wave Gain`, `Wave Number`, `Wave Shape` | Wave choice |
| `Filter`, `HPF Cutoff`, `Cutoff Aftertouch Sens` | Filter |
| `AMP`, `Tone Level`, `Level Aftertouch Sens` | Amplifier |
| `LFO`, `Modulation LFO` | LFO & modulation |
| `MFX Parameter`, `MFX Control`, `MFX Chorus Send Level`, `MFX Reverb Send Level`, `TFX Switch`, `Ring Switch` | Effects |

Unmapped, therefore never randomised: `Tone Name`, `Tone Category`, `MFX Type` (a discriminator
anyway), `Partial1..3 Switch` and `Select`, `Mono Switch`, `Unison Switch`, `Unison Size`,
`Legato Switch`, `Phrase Number`, `Phrase Octave Shift`, every `Reserved*`.

**PCM Synth Tone** (`Common`, `Common 2`, `Common MFX`, `Partial Mix Table`, `Partial 1..4`)

| Prefix | Category |
| --- | --- |
| `Pitch Env`, `Partial Coarse Tune`, `Partial Fine Tune`, `Partial Random Pitch Depth`, `Wave Pitch Keyfollow`, `PCM Synth Tone Coarse Tune`, `PCM Synth Tone Fine Tune`, `Octave Shift`, `Stretch Tune Depth`, `Pitch Bend Range`, `Portamento Time`, `Analog Feel` | Pitch & oscillator |
| `Wave Group Type`, `Wave Group ID`, `Wave Number`, `Wave Gain`, `Wave FXM`, `Wave Tempo Sync` | Wave choice |
| `TVF`, `Cutoff Offset`, `Resonance Offset` | Filter |
| `TVA`, `Bias`, `Partial Level`, `Partial Pan`, `Partial Pan Keyfollow`, `Partial Random Pan Depth`, `Partial Alternate Pan Depth`, `PCM Synth Tone Level`, `PCM Synth Tone Pan`, `Attack Time Offset`, `Release Time Offset`, `Velocity Sens Offset` | Amplifier |
| `LFO1`, `LFO2`, `LFO Step`, `Modulation LFO` | LFO & modulation |
| `MFX Parameter`, `MFX Control`, `MFX Chorus Send Level`, `MFX Reverb Send Level`, `Partial Chorus Send Level`, `Partial Reverb Send Level`, `TFX Switch` | Effects |

Unmapped: `PCM Synth Tone Name`, `PCM Synth Tone Priority`, `Tone Category`, `MFX Type`, `Mono-Poly`,
`Legato Switch`, `Legato Retrigger`, `Portamento Switch`, `Portamento Mode`, `Portamento Type`,
`Portamento Start`, `Matrix Control * Source`, `Dest` and `Sens` (control assignments), `Partial Mix
Table Control Switch`, `Partial Output Level`, `Partial Output Assign`, `Partial Receive *`, `Partial
Redamper Switch`, `Partial Control * Switch *`, `Partial Env Mode`, `Partial Delay *`, the whole
`Partial Mix Table` block (structure types, boosters and the PMT key and velocity zones — randomising a
zone silences a partial rather than changing its sound), `Phrase Number`, `Phrase Octave Shift`, every
`Reserved*`.

**SuperNATURAL Acoustic Tone** (`Common`, `Common MFX`)

| Prefix | Category |
| --- | --- |
| `Octave Shift`, `Portamento Time Offset` | Pitch & oscillator |
| `Cutoff Offset`, `Resonance Offset` | Filter |
| `Attack Time Offset`, `Release Time Offset`, `Tone Level` | Amplifier |
| `Vibrato Rate`, `Vibrato Depth`, `Vibrato Delay` | LFO & modulation |
| `MFX Parameter`, `MFX Control`, `MFX Chorus Send Level`, `MFX Reverb Send Level`, `TFX Switch` | Effects |
| `Modify Parameter ` | Instrument character |

The modify parameters are what an SN-A tone mostly *is*, and each one's meaning depends on the selected
instrument — `Modify Parameter 1` is `String Resonance` on a grand piano, `Noise Level` on a Rhodes and
`Mallet Hardness` on a vibraphone. They cannot be sorted into filter/amp/pitch by name, so they get
their own category rather than a dishonest one. `Instrument`, `Inst Number`, `Inst Variation` and
`Category` stay unmapped: the first is a discriminator, and all four choose *which* instrument this is
rather than shaping it. So do `Tone Name`, `Mono-Poly`, `Phrase Number`, `Phrase Octave Shift` and
every `Reserved*`.

**SuperNATURAL Drum Kit** (`Common`, `Common MFX`, `Common Comp-EQ`, `Partial 1..62`)

| Prefix | Category |
| --- | --- |
| `Tune` | Pitch & oscillator |
| `Inst Number`, `Variation` | Wave choice |
| `Brilliance` | Filter |
| `Attack`, `Decay`, `Level`, `Pan`, `Stereo Width`, `Dynamic Range`, `Kit Level` | Amplifier |
| `Chorus Send Level`, `Reverb Send Level`, `Ambience Level`, `TFX Switch`, `MFX Parameter`, `MFX Control` | Effects |

Unmapped: `Output Assign`, `Kit Name`, `Phrase Number`, `MFX Type`, the Comp-EQ block, every
`Reserved*`.

**PCM Drum Kit** (`Common`, `Common 2`, `Common MFX`, `Common Comp-EQ`, `Partial 1..88`)

| Prefix | Category |
| --- | --- |
| `Pitch Env`, `Partial Coarse Tune`, `Partial Fine Tune`, `Partial Random Pitch Depth`, `WMT* Wave Coarse Tune`, `WMT* Wave Fine Tune` | Pitch & oscillator |
| `WMT* Wave Group Type`, `WMT* Wave Group ID`, `WMT* Wave Number`, `WMT* Wave Gain`, `WMT* Wave FXM`, `WMT* Wave Tempo Sync`, `WMT* Wave Switch` | Wave choice |
| `TVF` | Filter |
| `TVA`, `Partial Level`, `Partial Pan`, `Partial Random Pan Depth`, `Partial Alternate Pan Depth`, `WMT* Wave Level`, `WMT* Wave Pan`, `Kit Level` | Amplifier |
| `Partial Chorus Send Level`, `Partial Reverb Send Level`, `MFX Parameter`, `MFX Control`, `MFX Chorus Send Level`, `MFX Reverb Send Level`, `TFX Switch` | Effects |

A PCM drum partial has **no LFO**, so this engine claims no LFO & modulation category and the dialog
shows that row disabled. `TFX Switch` lives in the `Common 2` block for both PCM engines and in plain
`Common` for all three SuperNATURAL ones — verified against the parameter database, and the reason the
PCM `Common 2` blocks are mapped at all.

`WMT*` means the rule applies to all four wave-mix-table slots (`WMT1`..`WMT4`). Unmapped: `Partial
Name`, `Assign Type`, `Mute Group`, `Partial Output Level`, `Partial Output Assign`, `Partial Receive
*`, `Partial Pitch Bend Range`, `Partial Env Mode`, `WMT Velocity Control`, every `WMT* Velocity Range`
and `Fade Width` (a zone, not a sound), `WMT* Random Pan Switch`, `WMT* Alternate Pan Switch`, the
Comp-EQ block, every `Reserved*`.

### `ToneRandomiser` (`Src/Models/Services/`)

```csharp
/// <summary>How far each category may move, 0..1. A category absent from the map is not randomised.</summary>
public sealed record RandomisationStrengths(IReadOnlyDictionary<ToneCategory, double> ByCategory);

public static class ToneRandomiser
{
    /// <summary>The raw values to apply, keyed by path. Parameters not returned are left alone.</summary>
    public static IReadOnlyDictionary<string, int> NewValuesFor(
        IEnumerable<FullyQualifiedParameter> parameters, RandomisationStrengths strengths, Random rng);
}
```

For each parameter, in the order given:

1. Skip when `ParSpec.IsParent` (a discriminator), when the type is `ASCII` (a name), or when
   `ToneParameterCategories.For(path)` is null.
2. Skip when its category has no strength, or a strength of zero.
3. **Enumerated** — the spec carries a `Repr` or a `Discrete` list, so the values are labels and the
   distance between two of them means nothing. With probability equal to the strength, draw uniformly
   from the legal values; otherwise leave it. At 0.1 most switches and modes hold; at 1.0 nearly all
   change.
4. **Numeric** — `window = round(strength × (IMax − IMin))`, new value =
   `clamp(current + rng.Next(−window, +window + 1), IMin, IMax)`. The window is symmetric around the
   current value, so a low strength nudges and a high one is close to a free draw, and clamping is what
   keeps a parameter near its limit from silently wrapping.

The caller passes `Random`; every test passes a seeded one, so the whole service is deterministic under
test. Reserved parameters never reach it — the caller asks the domain for
`GetRelevantParameters(false, false)`, which excludes both reserved and context-invalid parameters.
Context-invalid is the right exclusion here and the opposite of what a snapshot capture wants: a
snapshot has to carry parameters that its own discriminators will make valid, whereas randomise never
moves a discriminator, so a parameter that is invalid now stays invalid.

### `ToneClipboard` (`Src/Models/Services/`)

One session-scoped slot holding an `Integra7Snapshot` and nothing else — the snapshot already names its
engine and its blocks. Not persisted: a clipboard that outlives the application is a surprise, and the
library is where a tone goes to be kept.

```csharp
public sealed class ToneClipboard
{
    public Integra7Snapshot? Content { get; private set; }
    public bool HasContent { get; }
    public event Action? Changed;   // so Paste can enable itself
    public void Put(Integra7Snapshot snapshot);
}
```

Copy captures from the selected part into it; Paste restores it into the selected part. The engine
guard is `RestoreToneAsync`'s existing one — a tone copied from a SuperNATURAL part and pasted into a
PCM part is refused with the message that already names both engines and says what to select first.

### `InitToneResolution` (`Src/Models/Services/`)

Which snapshot Init loads for an engine:

1. The library entry the user marked as the init tone for that engine, if it is still there.
2. Otherwise the bundled asset `avares://Integra7AuralAlchemist/Assets/InitTones/<toneType>.json`.
3. Otherwise nothing, and the command says so.

The mark is stored in the settings file as a **file name relative to the library folder**, not an
absolute path: the library folder is already a setting the user can change, and a relative name follows
it. `LibrarySettings` grows from one property to two, and its `Stored` record gains
`IReadOnlyDictionary<string, string>? InitTones` keyed by tone type (`"SN-S"`, `"PCMS"`, …). A settings
file written by an older build has no such key, which deserializes to null and means "nothing marked" —
the same tolerance the existing `LibraryFolder` property has.

---

## Undo and the edit journal

**Randomise** records one step. For each parameter it moves: read the old displayed value with
`LookupSingleParameterDisplayedValue`, apply the raw value, read the new displayed value, and record a
`ParameterChange` — all inside a single `EditJournal.BeginGesture()` scope, so every change folds into
one `EditStep` however long the operation takes and one press of Undo takes the whole thing back.
`IsDiscriminator` is always false, because a discriminator is never randomised.

**Init and Paste** call `EditJournal.Default.Clear()` after the restore, exactly as `LoadToneAsync`
does, and for the reason recorded there: the steps in the history name parameters of a tone that is no
longer loaded.

All three are refused while Compare is active, through the existing `RefuseWhileComparing` guard. While
comparing, the journal's buffer is the only copy of the user's edits.

---

## User interface

### Toolbar

Four buttons beside the existing Save Tone / Export Tone / Load Tone, acting on the same target and
resolved the same way (`ResolveSelectedToneAsync`):

| Button | Enabled when |
| --- | --- |
| **Init Tone** | A part tab is selected (not Common) |
| **Copy Tone** | A part tab is selected |
| **Paste Tone** | A part tab is selected *and* the clipboard has content |
| **Randomise…** | A part tab is selected |

### Confirm dialog

`Interaction<string, bool> ShowConfirmDialog` — the first yes/no dialog in the application, so it is
built as one reusable interaction rather than one per command. Init and Paste both raise it:

> Replacing the tone in part 4 cannot be undone, and it clears the edit history. Continue?

### Randomise dialog

`RandomiseToneViewModel` + `RandomiseToneView`. One row per category in a fixed order, each row a
checkbox and a 0–100 % strength slider. A category with no parameters in the loaded engine is shown
disabled rather than hidden, so the dialog does not change shape from one engine to the next
(`ToneParameterCategories.PresentIn`).

A line above the rows names the target — "Randomising the tone in part 4", or for a drum kit
"Randomising note 38 (D2) of the kit in part 10".

The view model is held by `MainWindowViewModel` rather than constructed per press, so a second
randomise starts from the settings the first one used. Not persisted across sessions; that is a later
addition if it is missed.

### Drum kits

Randomise acts on the **selected note only** — one partial block. The note comes from
`PartViewModel.SNDrumKitEditor?.SelectedNote` or `PcmDrumKitEditor?.SelectedNote`, whose `Index`
(0..61 / 0..87) maps directly to `Offset2/… Partial {Index + 1}`. With no drum editor initialised or no
note selected, the command refuses on the status line:

> Cannot randomise a drum kit: open the part's drum tab and select a note first.

Init, Copy and Paste are unaffected — they act on the whole kit, because they are whole-tone
operations.

---

## Failure, and what the user is told

Every command follows the shape the existing tone commands have: `UserActionLog.Action` on entry,
`SignalStartSync`/`SignalStopSync` around the device work, `SnapshotFailed` + `SnapshotStatus` for the
outcome, and `ResyncPartAsync` afterwards so the screen re-reads what actually landed. Success messages
say **sent**, not applied — the device acknowledges no parameter write.

| Situation | Message |
| --- | --- |
| No init tone for this engine | `No init tone is set for SN-S. Save a tone into the library, then mark it as the init tone for this engine from the library's context menu.` |
| Marked init tone is gone from the library | The same message, prefixed `The tone marked as the init tone for SN-S is no longer in the library.` |
| Paste with an empty clipboard | `Nothing to paste: copy a tone first.` (the button is disabled, but the command is reachable) |
| Paste across engines | `RestoreToneAsync`'s existing message, which names both engines |
| A block the device does not answer for | `RestoreToneAsync`'s existing per-block message |
| Randomise on the Common tab | `ResolveSelectedToneAsync`'s existing message |
| Randomise with nothing ticked | The dialog's OK button is disabled; nothing to say |

---

## Testing

The four services are pure, and they carry the weight.

**`ToneParameterCategories`** — against the real parameter database, so a renamed parameter fails a
test rather than silently dropping out of randomisation:
- A sample path from each category, for each of the five engines, lands in the expected category.
- `Partial Output Assign`, `Partial Name`, `Tone Name`, `Assign Type`, `Mute Group`, the receive
  switches, the control assignments and the PMT/WMT zone ranges all map to nothing.
- Every `Reserved*` path maps to nothing.
- `PresentIn` reports Instrument character for SN-A only, and Filter for every engine.

**`ToneRandomiser`** — with a seeded `Random`:
- Strength 0 for every category returns an empty map.
- A numeric value never leaves `IMin..IMax`, including one starting at either limit.
- The window scales: strength 0.1 on a 0..127 parameter never moves it more than 13.
- An enum holds at strength 0 and is re-drawn at strength 1.
- A discriminator (`MFX Type`), an ASCII parameter (`Tone Name`) and an unmapped path are never
  returned, even with every category at full strength.
- Same seed, same result.

**`ToneClipboard`** — put then read round-trips; `HasContent` is false until the first put; `Changed`
fires on put.

**`InitToneResolution`** — a marked library entry wins over the bundled asset; a mark pointing at a file
that is gone falls through to the asset; no mark and no asset resolves to null rather than throwing.

**`LibrarySettings`** — a settings file with no `InitTones` key loads; a mark round-trips through save
and load; marks survive a library-folder change (they are relative names).

**Journal** — one randomise over a block produces exactly one `EditStep` whose changes cover every
parameter that moved, and undoing it restores every old value.

---

## The one thing that needs hardware

The five bundled init tones must be captured from the instrument and committed as
`Src/Assets/InitTones/{PCMS,PCMD,SN-S,SN-A,SN-D}.json`: build a neutral starting tone for each engine on
the device, use **Export Tone**, and put the file there. They are genuine device data that way —
complete, and guaranteed to satisfy `RestoreToneAsync`'s block and parameter validation, which a
hand-written file would not be.

Until they exist, Init reports that no init tone is set for that engine and points at the library route,
so the feature ships in a usable state and completes when the files land.

---

## What this leaves for later

- Per-parameter locks ("randomise everything except this cutoff"), which the category table makes
  straightforward to add.
- Blending two tones, the other half of what Midi Quest and SoundDiver offer.
- Randomising a whole drum kit, which needs the journal question answered differently.
- Persisting randomise settings across sessions.
