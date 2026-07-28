# Step LFO editor — design

**Goal.** Give the PCM Synth partial's step LFO — `LFO Step Type` and `LFO Step 1..16` — a friendly
editor, closing the last hole in friendly-editor coverage. Today those seventeen parameters are reachable
only through the raw parameter grid under "Advanced".

**Not in scope.** The SuperNATURAL engines, which have no step LFO. Copying a step sequence between
partials, randomising it as a unit, or presets of sequences: the tone-level clipboard and randomiser
already cover those parameters as part of the whole tone.

---

## What this is built on

| Existing | What it gives |
| --- | --- |
| `PCMPartialViewModel` | Already owns `Lfo1`/`Lfo2` panels and the partial's parameter dictionary |
| `PcmLfoPanelViewModel` | The sibling panel this one sits beside, and the shape to copy |
| `ParamInt` / `ParamString` (`SynthParam.cs`) | Throttled writes, echo suppression, undo journalling — all of it already works |
| `Src/Controls/EditGesture.cs` | One pointer stroke becomes one undo step |
| `Src/Controls/PointerGesture.cs` | A lost capture ends a drag exactly like a released button |
| `MultiStageEnvelopeControl`, `PmtZoneEditorControl` | The house pattern for a Canvas-drawn draggable editor |
| `LayerMapGeometry`, `PmtZoneMapping`, `KnobGeometry` | The house pattern for putting a visual editor's arithmetic in a tested class |

---

## The parameters

All in `Offset2/PCM Synth Tone Partial N`, verified against the parameter database:

| Path | Raw | Displayed |
| --- | --- | --- |
| `PCM Synth Tone Partial/LFO Step Type` | 0..1 | currently a bare number — see below |
| `PCM Synth Tone Partial/LFO Step 1` .. `LFO Step 16` | 28..100 | −36 .. +36 |

**The sixteen steps are shared by both LFOs.** Their paths carry no LFO number, unlike `LFO1 Rate` and
`LFO2 Rate`. A tone whose LFO1 *and* LFO2 are both set to the "Step" waveform (value 12 of
`LFO_WAVEFORM`) uses the same sequence twice. That is the instrument's design, not something to work
around, but it must be said on screen or it reads as a bug.

**`LFO Step Type` gains names.** It has `repr:null` today, so it displays as 0 or 1 everywhere — the same
gap the tone category had until this week. A new `LFO_STEP_TYPE` table gives it "Type 1 (stepped)" and
"Type 2 (smoothed)": Type 1 holds each step's value until the next, Type 2 glides between them. The
descriptions are the user's call, from the instrument rather than from this repo, which carries no Roland
reference. Because the names live in the parameter database rather than in this editor, they also fix the
raw grid, snapshot files and comparisons.

---

## Components

### `StepLfoGeometry` (`Src/Controls/`)

Pure arithmetic, no Avalonia types beyond `Rect`/`Point`, fully unit-tested. A visual editor's arithmetic
belongs here rather than in the control — the rule this codebase arrived at after reviewers kept moving
it.

```csharp
public sealed class StepLfoGeometry(double width, double height, int steps, int minValue, int maxValue)
{
    /// <summary>Which step the pointer is over, or null when it is outside the bars.</summary>
    public int? StepAt(double x);

    /// <summary>The value a pointer at this height means, clamped to the parameter's own range.</summary>
    public int ValueAt(double y);

    /// <summary>The bar for one step: from the centre line up for a positive value, down for a negative,
    /// and a thin sliver at the centre for zero, so a step at rest is still visible and still a target.
    /// </summary>
    public Rect BarFor(int step, int value);

    /// <summary>Where zero sits.</summary>
    public double CentreY { get; }
}
```

### `StepLfoControl` (`Src/Controls/`)

Draws the sixteen bars and a centre line, and handles the pointer.

**Drawing a sequence in one stroke.** Pressing sets the bar under the pointer; holding and moving sets
every bar the pointer passes, including ones skipped by a fast movement — a drag from one end to the
other leaves no gaps. That is how a step sequencer behaves everywhere else, and it is the reason to build
a control at all rather than sixteen knobs in a row.

**One stroke is one undo step**, through `EditGesture`, opened on press and closed on release *and* on
capture lost. `PointerCaptureLost` is a direct event that goes only to the element that held capture,
which is why `PointerGesture` exists and why this uses it rather than a second implementation.

Values are written through the panel's `ParamInt` wrappers, so throttling, echo suppression, undo, Compare
and the edit journal all work with nothing new.

### `StepLfoPanelViewModel` + `StepLfoPanelView`

A panel headed **Step LFO**, with the subtitle "shared by LFO 1 and LFO 2", holding the type combo and the
control. It is built by `PCMPartialViewModel` beside `Lfo1` and `Lfo2`, and shown in the partial's
**Motion** tab below them — always, not only when an LFO is set to Step, so a sequence can be drawn before
the waveform is switched.

```csharp
public sealed class StepLfoPanelViewModel : ViewModelBase, IDisposable
{
    public ParamString StepType { get; }
    public IReadOnlyList<ParamInt> Steps { get; }   // sixteen, in order
    public IReadOnlyList<IParam> Params { get; }    // for the partial's tracking, as the LFO panels do
}
```

---

## Failure, and what the user sees

Nothing here talks to the instrument directly: every write goes through `ParamInt`, which already handles
a disconnected device, throttling and echoes. There is no new failure path, no dialog and nothing to
confirm.

A step at rest (value 0) still draws a sliver at the centre line, so every bar is visible and clickable —
a zero-height bar would be an invisible target.

---

## Testing

`StepLfoGeometry` carries the weight, as the geometry classes for the other visual editors do:

- `StepAt` returns 0 at the left edge, 15 at the right edge, and null outside the bars on either side.
- `StepAt` divides the width evenly: sixteen distinct answers across the full width, each appearing once.
- `ValueAt` gives the maximum at the top, the minimum at the bottom, zero at the centre, and clamps
  rather than exceeding the range when the pointer is dragged past either edge.
- `BarFor` puts a positive value above the centre line and a negative one below; a zero value is a
  non-empty sliver at the centre.
- `BarFor` and `StepAt` agree: the bar for step *n* contains the x that `StepAt` maps back to *n*.
- A degenerate size (zero width or height) answers without dividing by zero.

The control, the panel and the view are not unit-tested, consistent with the rest of the repository. Their
verification is that the solution builds — which compiles every binding — and the hand checks below.

---

## Verification by hand (user)

- [ ] Drag across the sixteen bars in one stroke: every bar follows the pointer, none is skipped.
- [ ] One press of Undo takes the whole stroke back.
- [ ] Set an LFO's waveform to Step and confirm the sequence is audible, and that the values on screen
  match what the instrument's own display shows.
- [ ] Confirm the type labels are the right way round: "Type 1 (stepped)" should hold each value until the
  next step, "Type 2 (smoothed)" should glide between them. If they are reversed, the fix is one line in
  the parameter database.
- [ ] Edit a step, then compare the tone against a snapshot taken before: the step appears as a difference
  with its displayed value.
