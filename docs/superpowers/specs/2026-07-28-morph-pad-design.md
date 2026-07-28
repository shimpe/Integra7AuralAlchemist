# Morph pad — design

**Goal.** A screen where two to seven library tones sit at the corners of a polygon and a point inside it
blends them into the tone loaded in the selected part, live, as the point moves.

**Not in scope.** Drum kits, for the reason given below. Morphing between engines. Animating the point
(an LFO or envelope driving it) — a later addition this design leaves room for. Writing a blend into the
instrument's user memory: Save User Tone already does that once the sound is in a part.

---

## What this is built on

| Existing | What it gives |
| --- | --- |
| `Integra7Snapshot` with raw values per parameter | The corners. Every captured parameter carries the value the device stores, which is what a blend must work in |
| `SnapshotLibrary` / `LibraryEntry.Head.ToneType` | Which library entries may sit on a corner |
| `StudioSetSnapshotService.RestoreToneAsync`'s block writing | How a tone reaches a part |
| `ToneDomainNames.For(toneType, part)` | The blocks a tone is made of |
| `Src/Controls/PointerGesture.cs`, `EditGesture.cs` | Dragging that survives a lost capture |
| `LayerMapGeometry`, `StepLfoGeometry` | The house pattern: a visual editor's arithmetic lives in a tested class |
| `ToneRandomiser` | The precedent for classifying a parameter as numeric or discrete, and for working in raw space |

---

## Decisions, and why

**Weights are inverse distance, power 1**: `wᵢ = (1/dᵢ) / Σ(1/dⱼ)`. For two corners this is exactly a
linear crossfade along the line — halfway is 50/50 — which is what interpolating linearly means, and it
generalises to any corner count with no special cases. A pointer exactly on a corner yields that corner
at 1.0 rather than dividing by zero.

**Blending happens in raw space**, not display space, for the reason `ToneRandomiser` records: the raw
value is what the device stores, and display strings are a rendering that does not always round-trip
through an integer formatter.

**A discrete parameter takes the winning corner's value, and a discriminator takes its whole group with
it.** 1,711 SuperNATURAL Synth parameters exist only for a particular `MFX Type`, and 2,794 SuperNATURAL
Acoustic ones only for a particular `Instrument`. "MFX Parameter 5" is Phaser Rate under one type and
Delay Feedback under another, so averaging it across corners that disagree produces a number that means
nothing in either effect. The winner supplies that entire group unblended, and everything not governed by
a discriminator — oscillator, filter, amplifier, LFO — goes on blending.

**The winner is sticky, but reproducible from cold.** A corner takes the lead only when its weight
exceeds the current leader's by 5% relative, so hovering on a boundary does not flicker between two
patches. With no history — the first evaluation, or a pad just loaded from disk — the highest weight wins
outright, ties going to the lowest corner number. Without that fallback a saved position would not
reproduce the sound it was saved at, which is most of the point of saving it.

**Drums are excluded.** A kit is 62 or 88 independent notes; blending them mixes unrelated sounds and
produces a kit that is no longer a kit. The corner picker offers only `PCMS`, `SN-S` and `SN-A` tones.

**All corners share one engine, and the target part must already hold it.** These are the temporary
tone's own addresses, so PCM data written into a SuperNATURAL part means something else entirely — the
same guard `RestoreToneAsync` already applies, reached the same way.

**Morphing writes nothing to the edit journal, and clears it once on arrival.** At four writes a second
a drag would fill the 200-step history in under a minute, and undo would replay a whole tone per step.
Clearing on arrival is what Load Tone already does, and for the same reason: the steps describe a tone
that is no longer loaded.

---

## Components

### `MorphWeights` (`Src/Controls/`)

Pure. Corner positions for a count of 2–7, and the weights for a point.

```csharp
public static class MorphWeights
{
    /// <summary>Corner positions in a unit circle centred on (0,0): evenly spaced, first corner at the
    /// top, and for two corners the left and right ends of the horizontal diameter rather than a
    /// degenerate polygon.</summary>
    public static IReadOnlyList<Point> Corners(int count);

    /// <summary>Each corner's share of a point, summing to 1. A point on a corner gives that corner 1.0
    /// and every other 0.</summary>
    public static IReadOnlyList<double> For(Point p, IReadOnlyList<Point> corners);
}
```

### `MorphWinner` (`Src/Models/Services/`)

Pure, and the only stateful part of the blend — it holds the current leader.

```csharp
public sealed class MorphWinner
{
    /// <summary>The corner whose discrete values win. Sticky: the leader changes only when another
    /// corner beats it by <see cref="Margin"/>, which stops the discrete values flickering when the
    /// pointer sits on a boundary. Call <see cref="Reset"/> when the pad's corners or the pointer are
    /// set from outside a drag, so a loaded position resolves the same way every time.</summary>
    public int Winner(IReadOnlyList<double> weights);

    public void Reset();

    /// <summary>How much better a challenger must be, relative to the leader. 5%: enough that a
    /// boundary does not flicker, small enough that the lead changes where a user would expect.</summary>
    public const double Margin = 0.05;
}
```

### `MorphedTone` (`Src/Models/Services/`)

Pure. The blend, as an `Integra7Snapshot` ready for the existing block-writing path.

```csharp
public static class MorphedTone
{
    /// <summary>Blend the corner snapshots by weight. <paramref name="winner"/> supplies every discrete
    /// value, every parameter governed by a discriminator, and the tone's name.</summary>
    public static Integra7Snapshot Blend(IReadOnlyList<Integra7Snapshot> corners,
        IReadOnlyList<double> weights, int winner, Integra7Parameters parameters);
}
```

Per parameter, by the same classification `ToneRandomiser` uses:

| Kind | Result |
| --- | --- |
| Numeric | `round(Σ wᵢ · rawᵢ)`, clamped to `IMin..IMax` |
| Discrete (`Repr` or `Discrete` list) | the winner's raw value |
| Governed by a discriminator (`ParentCtrl` names one) | the winner's raw value |
| Text (no raw) | the winner's string |

A parameter missing from a corner's snapshot — an older file — is taken from the winner rather than
treated as zero, and the blend records that it happened so the screen can say so once.

### `MorphPadGeometry` (`Src/Controls/`)

Pure. Control-space arithmetic: corner positions scaled into the control's bounds, the point under the
pointer, and **projection onto the polygon** so a pointer dragged outside lands on the nearest edge
rather than leaving the shape. For two corners that projection is onto the segment.

### `MorphPadControl` (`Src/Controls/`)

Draws the polygon, the numbered corners with their patch names, and the point. One `PointerGesture` per
drag. Reports the point in polygon space; it knows nothing about snapshots.

**The fill is the feature's face, and it is specified rather than left to taste.** Four candidates were
rendered and compared before choosing (`scratchpad/pad-candidates.png` in the session that designed
this). Every interior pixel is coloured from the same inverse-distance weights the blend uses:

1. **Hue** comes from a *sharpened* mix: each weight raised to the power **2.5**, renormalised, then used
   to mix the corner colours. Sharpened because with seven corners the raw weights never let any colour
   dominate, and the pad reads as one grey-brown wash.
2. **Brightness** rises with how decided the point is. With `dominance = (w_max − w_second) / w_max`, the
   mixed colour is multiplied by **0.55 + 0.60 × dominance** — never black at the centre, never blown out
   at a corner.

The faint seams this produces, radiating from the centre, fall exactly where two corners are level, which
is where the discrete values flip. The picture therefore says something true about the sound rather than
merely decorating it.

**It is honest about one thing and not another.** The fill shows the *instantaneous* winner, while the
audible discrete values follow `MorphWinner`, which is sticky. Near a boundary the colour can therefore
say one corner while the sound still holds the previous one. That was accepted deliberately: drawing the
hysteresis would mean the same pad position painting differently depending on how it was approached,
which is worse to look at than a boundary that leads the ear by a few pixels.

**Corner colours** are seven hues evenly spaced from 15°, at saturation 0.62 and lightness 0.58 — chosen
to sit on this application's dark panels rather than glow off them. They go in `App.axaml` as
`SnMorphCorner1Brush`..`SnMorphCorner7Brush`, because no colour in this application is written in a
control. Corner *n* always takes colour *n*, so the association between a colour and a corner number is
learnable.

**Two corners have no interior**, so that case draws as a thick horizontal track with the gradient along
it and a corner marker at each end — a crossfade rendered as one, which is what it is.

**Rendering cost is paid once, not per frame.** The weight field depends only on the corner count and the
control's size, never on the pointer, so the fill is rendered into a `WriteableBitmap` when either
changes and simply blitted afterwards. Dragging the point redraws two markers over a cached image.

### `MorphPadViewModel` + `MorphPadView`

A new top-level tab, **Morph**. The pad on the left; on the right the corner count (2–7), the engine the
pad is locked to, the target part, the numbered corners each with a **Pick…** button, **Save blend to
library…**, and **Save pad** / **Load pad**.

Writing is throttled at 250 ms, the interval the knobs and envelope editors already use. Unlike
`RestoreToneAsync` it does **not** read each block first: the blend supplies every parameter the block
holds, so there is nothing to preserve. A flush is 5 transmissions for SuperNATURAL Synth and 8 for PCM
Synth, each in its own short conversation rather than one held across a drag that may last minutes.

### `MorphWriter` (`Src/Models/Services/`)

Sends one blend to a part. Its own service rather than a call into `StudioSetSnapshotService`, because it
differs from a restore in exactly one way that matters: **it does not read the block first**. A restore
reads because a snapshot may not cover every parameter the bulk write will transmit; a blend covers all
of them by construction, since it is built from full captures of the same engine. Skipping the read is
what makes a flush affordable four times a second.

Values are applied in the snapshot's own order, which is address order, so a discriminator is set before
the parameters that depend on it — the same property `ApplyBlockValues` relies on. Then one
`WriteToIntegraAsync` per block.

### `MorphPadFile` (`Src/Models/Services/`)

Pure read/write of a pad: corner count, corner patches as **file names relative to the library folder**,
and the last pointer position. Relative for the reason the init-tone marks are relative — the library
folder is a setting that can move. Pads live in a `Pads` folder beside the library.

---

## Failure, and what the user is told

| Situation | What happens |
| --- | --- |
| A corner's file is gone from the library | The pad refuses to morph and names that corner; the rest of the pad stays as it is |
| Fewer than two corners filled | The pad is inert and says so |
| The selected part holds a different engine | `RestoreToneAsync`'s existing message, which names both engines |
| No instrument connected | The pad still works and can be arranged; only the writing is disabled |
| A corner snapshot lacks a parameter the others have | Taken from the winner, and the screen says once that a corner is older than the rest |

Nothing here writes to user memory, and a failed flush leaves the part holding the previous blend — the
next flush overwrites it wholesale, so there is no half-applied state to unpick.

---

## Testing

The pure pieces carry it:

**`MorphWeights`** — weights sum to 1 for every count 2–7; equal at the centre; a point on a corner gives
that corner 1.0; two corners give a true linear crossfade (a quarter along is 0.75/0.25).

**`MorphWinner`** — from cold, the highest weight wins and ties go to the lowest corner; a challenger
within the margin does not take the lead; one beyond it does; `Reset` restores the cold behaviour.

**`MorphedTone`** — a numeric parameter lands on the weighted average and stays inside its range; a
discrete one takes the winner's; every parameter of a discriminator group comes from the winner even
when another corner is nearer on weight; the name comes from the winner; a parameter present in only
some corners comes from the winner and is reported.

**`MorphPadGeometry`** — a point inside is unchanged; one outside lands on the nearest edge; the
projection for two corners stays on the segment; corner positions match `MorphWeights` after scaling.

**The fill** — its two shaping steps are arithmetic and testable without drawing anything: the sharpened
weights still sum to 1; at a corner the sharpened mix is that corner's colour; dominance is 1 at a corner
and 0 at the centre of a regular polygon; and the brightness factor stays within 0.55..1.15 everywhere,
so no pixel is black or clipped.

**`MorphPadFile`** — a pad round-trips; a pad naming a file that no longer exists loads with that corner
marked missing rather than throwing.

The control, the view model and the view are not unit-tested, consistent with the rest of this
repository.

---

## Verification by hand (user)

- [ ] Two corners: drag from one end to the other and hear a linear crossfade; the ends sound exactly
  like the two library patches.
- [ ] Seven corners: the discrete values change as you cross between corners, and hovering on a boundary
  does not flicker between two patches.
- [ ] Corners with different MFX types: the effect section is always one coherent effect, never a hybrid.
- [ ] Save a pad, close the application, load it: the same position produces the same sound.
- [ ] Save a blend to the library, then load it into a part: it sounds like the spot on the pad.
- [ ] With no instrument connected the pad still arranges and saves.
- [ ] The fill looks right at 2, 3 and 7 corners, and dragging the point stays smooth — if it stutters,
  the fill is being re-rendered per pointer move rather than cached.
