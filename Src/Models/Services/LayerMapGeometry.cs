using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One part's zone, as the layer map draws it: an immutable snapshot, not a live view model. The
/// control takes a list of these and knows nothing else about the application — which is what keeps the
/// drawing and hit-testing testable, and what lets the view model own the parameter wrappers.</summary>
/// <param name="PartNo">Zero-based.</param>
/// <param name="Label">What to write on the zone: the part number.</param>
/// <param name="ToneName">The patch the part holds, drawn after the label when there is room.</param>
public readonly record struct LayerZone(
    int PartNo,
    int KeyLo, int KeyHi,
    int VelLo, int VelHi,
    int KeyFadeLo, int KeyFadeHi,
    int VelFadeLo, int VelFadeHi,
    string Label, string ToneName);

/// <summary>Where the layer map draws things, what is under the pointer, and what a drag of it means. Pure,
/// and built on <see cref="PmtZoneMapping"/> rather than re-deriving it: the key axis is the same axis the PMT
/// zone editor uses, and velocity is that editor's Y mapping applied within one lane instead of the whole
/// chart.
///
/// Sixteen lanes, part 1 at the top. A lane spans the full key range; a zone occupies part of its lane
/// horizontally by key and vertically by velocity, so which parts answer a given key is read down a column
/// and how loudly each answers is read within its own row.
///
/// **Heights here are the lane area, not the control.** The chart reserves <see cref="AxisHeight"/> along the
/// bottom for note names, and every function below except <see cref="LaneAreaHeight"/> and
/// <see cref="MinHeight"/> takes the height of the lanes alone. A caller works out
/// <c>LaneAreaHeight(Bounds.Height)</c> once and passes that everywhere; passing <c>Bounds.Height</c> instead
/// draws and hit-tests everything a fraction of a lane low, consistently enough to look like imprecision
/// rather than a mistake. That is the one thing a caller of this class can get wrong, which is why the
/// parameter is called <c>laneAreaH</c> and not <c>h</c>.
///
/// The control on top of this should contain no arithmetic and no measurement of its own: it turns pointer
/// events into calls here and drawing calls out of the results. That is not tidiness — it is the only layer of
/// this feature a test can reach, since there is no headless-Avalonia harness in this repository.</summary>
public static class LayerMapGeometry
{
    /// <summary>Lanes, one per part.</summary>
    public const int Lanes = Constants.NO_OF_PARTS;

    /// <summary>The least a lane can be and still work. See <see cref="HitMargin"/>, which this is tied to:
    /// twenty pixels is about a line of text tall and leaves twelve pixels of grabbable body between two
    /// four-pixel velocity handles.</summary>
    public const double MinLaneHeight = 20;

    /// <summary>How near an edge counts as grabbing it, passed to <see cref="HitTest"/>.
    ///
    /// Four, deliberately not the six <c>PmtZoneEditorControl</c> uses. That control draws four zones over the
    /// chart's whole height, so its zones are hundreds of pixels tall and six pixels is a sliver of one. Here a
    /// zone is a single lane tall, and since a part's default velocity range is the full 0..127 the usual zone
    /// fills its lane exactly: at six, a twenty-pixel lane would be twelve pixels of velocity handle around
    /// eight pixels of body, so most presses inside a zone would start a resize instead of selecting or
    /// auditioning it.
    ///
    /// Four is the largest margin that keeps a full-lane zone's body (<c>MinLaneHeight - 2 * HitMargin</c>,
    /// twelve) larger than its two handles together (<c>2 * HitMargin</c>, eight). A test pins that inequality,
    /// so raising this forces a matching rise in <see cref="MinLaneHeight"/> rather than quietly producing a
    /// lane that is mostly handle.</summary>
    public const double HitMargin = 4;

    /// <summary>The strip along the bottom of the chart that holds the note names, below the lanes.
    ///
    /// A strip *reserved* below the lanes, rather than axis labels written over the content the way the
    /// four-lane WMT and PMT maps write their velocity ticks straight onto the chart. Four lanes over the whole
    /// height can spare the corner of one; at sixteen lanes the bottom row is as much real content as the other
    /// fifteen, and running "C-1 C0 C1 …" through part 16's zone would make one part permanently harder to read
    /// than the rest.
    ///
    /// It lives here rather than in the control because both the drawing and the pointer handling have to
    /// subtract the identical value — if they disagree by even a pixel, every drag lands a fraction of a lane
    /// off the zone it is drawn on. Which is also why the control must not keep a constant of its own beside
    /// this one: two copies of a number that has to agree with itself is the whole failure mode.</summary>
    public const double AxisHeight = 16;

    /// <summary>The total height a control needs: sixteen legible lanes **and** the note-name strip. A control
    /// exactly this tall gets full-height lanes, so this is the number to hand to <c>MinHeight</c> as-is — do
    /// not add the strip again.</summary>
    public static double MinHeight => Lanes * MinLaneHeight + AxisHeight;

    /// <summary>The height the lanes tile, given the control's total height: everything above the note-name
    /// strip. **The only function here that takes a total height.** Floored at zero, so a control briefly
    /// measured smaller than its strip produces an empty chart rather than negative geometry.</summary>
    public static double LaneAreaHeight(double totalHeight) => Math.Max(0, totalHeight - AxisHeight);

    /// <summary>The whole of one part's row.</summary>
    public static PmtZoneMapping.Rect LaneRect(int part, double w, double laneAreaH)
    {
        var laneH = laneAreaH / Lanes;
        return new PmtZoneMapping.Rect(0, part * laneH, w, laneH);
    }

    /// <summary>Which part's lane contains <paramref name="y"/>, or null when the point is off the chart.
    /// Null rather than clamped: a pointer that leaves the chart must stop the drag it was doing, not carry on
    /// editing the nearest part. A y in the note-name strip is off the chart by this reckoning, which is what
    /// stops a press on the axis from editing part 16.</summary>
    public static int? LaneAt(double y, double laneAreaH)
    {
        if (laneAreaH <= 0 || y < 0 || y >= laneAreaH) return null;
        var lane = (int)(y / (laneAreaH / Lanes));
        return lane < 0 || lane >= Lanes ? null : lane;
    }

    /// <summary>The rectangle a zone occupies: its key range horizontally, its velocity range vertically
    /// within its own lane, loud at the top. This overload takes the range loose, for a caller mid-drag that
    /// has a candidate range and not yet a zone.
    ///
    /// <para>Horizontally the zone spans whole <see cref="KeyCell"/>s — from the left edge of the lowest key's
    /// cell to the right edge of the highest's — so its edges land on the gridlines the chart draws, and a
    /// one-key range (<c>lo == hi</c>) is one cell wide rather than nothing at all. Vertically it is a
    /// position, not a cell; see the note beside <c>KeyCell</c> for why the two axes differ.</para></summary>
    public static PmtZoneMapping.Rect ZoneRect(int part, int keyLo, int keyHi, int velLo, int velHi,
        double w, double laneAreaH)
    {
        var lane = LaneRect(part, w, laneAreaH);
        var loCell = KeyCell(Math.Min(keyLo, keyHi), w);
        var hiCell = KeyCell(Math.Max(keyLo, keyHi), w);
        // VelToY over the lane's own height, then offset into the lane.
        var yTop = lane.Y + PmtZoneMapping.VelToY(Math.Max(velLo, velHi), lane.H);
        var yBot = lane.Y + PmtZoneMapping.VelToY(Math.Min(velLo, velHi), lane.H);
        return new PmtZoneMapping.Rect(loCell.X, yTop, hiCell.X + hiCell.W - loCell.X, yBot - yTop);
    }

    /// <summary>The rectangle <paramref name="z"/> occupies. Prefer this wherever a whole zone is in hand.</summary>
    public static PmtZoneMapping.Rect ZoneRect(LayerZone z, double w, double laneAreaH)
        => ZoneRect(z.PartNo, z.KeyLo, z.KeyHi, z.VelLo, z.VelHi, w, laneAreaH);

    /// <summary>The velocity <paramref name="y"/> names within <paramref name="part"/>'s lane. The same Y in
    /// a different lane is a different velocity, which is what makes a lane a lane.
    ///
    /// A Y outside the lane clamps — 127 above it, 0 below — rather than refusing. That is exactly what a
    /// velocity drag wants, provided the caller passes the part the drag was *captured* on and not the part
    /// <see cref="LaneAt"/> reports for the pointer's current Y: dragging past the top of the lane then pins
    /// the edge at 127 instead of quietly re-reading the value in the neighbouring part's lane.</summary>
    public static int VelocityAt(int part, double y, double laneAreaH)
    {
        var lane = LaneRect(part, 0, laneAreaH);
        return PmtZoneMapping.YToVel(y - lane.Y, lane.H);
    }

    /// <summary>The key <paramref name="x"/> names, and its mirror. Both here so a caller never has to mix the
    /// two mapping classes — the axis a control draws and the axis it hit-tests must be the same one.</summary>
    public static int KeyAt(double x, double w) => PmtZoneMapping.XToKey(x, w);

    /// <inheritdoc cref="KeyAt"/>
    public static double KeyX(int key, double w) => PmtZoneMapping.KeyToX(key, w);

    /// <summary>The band of pixels that belongs to one key: <c>[KeyX(k) - half, KeyX(k) + half]</c>, clipped to
    /// the chart.
    ///
    /// <para><b>A key is a cell, not a position, and the cell is the unit this chart draws in.</b> That is not a
    /// new decision — <see cref="KeyAt"/> rounds, so a key has always owned half a step either side of its
    /// centre. What was new was drawing against the *centre* while hit-testing against the *cell*, which put
    /// every gridline half a column away from the edge it appeared to mark and gave a one-key zone
    /// (<c>lo == hi</c>) zero width. Drawing cells makes lines, tint, zones and hit-testing agree by
    /// construction rather than by two functions happening to round the same way.</para>
    ///
    /// <para>Keys 0 and 127 get half-width cells, because the mapping puts their centres exactly on the chart's
    /// edges. That is the honest consequence and it is better than the alternative, which would be shifting the
    /// whole axis by half a key inside <see cref="PmtZoneMapping"/> — a class the PMT zone editor shares and
    /// which is right as it stands.</para></summary>
    public static (double X, double W) KeyCell(int key, double w)
    {
        var half = w / (PmtZoneMapping.Max - PmtZoneMapping.Min) / 2.0;
        var centre = KeyX(key, w);
        var left = Math.Max(0, centre - half);
        var right = Math.Min(w, centre + half);
        return (left, Math.Max(0, right - left));
    }

    /// <summary>Where a gridline for <paramref name="key"/> goes: the left edge of its cell, which is the
    /// boundary <i>between</i> it and the key below. Key 127's right-hand boundary is the chart's right edge,
    /// <paramref name="w"/> itself.
    ///
    /// <para>A line between keys rather than through one is what a piano roll draws, and it is what makes the
    /// note name printed just to its right read as labelling that key rather than as floating between two.
    /// </para></summary>
    public static double KeyBoundaryX(int key, double w) => KeyCell(key, w).X;

    // Why only the key axis has cells: a key is a discrete thing the chart draws a line for and the user names
    // ("the split is at C4"), so it needs a width. Velocity is a continuum -- nothing draws a line per velocity
    // step, nothing labels one, and a lane is twenty pixels tall for a hundred and twenty-eight of them, so a
    // "cell" there would be a sixth of a pixel and would exist only to be symmetrical. VelocityAt, ZoneRect's
    // vertical half and the two velocity fade bands are all deliberately position-based, not cell-based.

    /// <summary>What is under the pointer: which part's lane, and which part of that part's zone. Pass
    /// <see cref="HitMargin"/> unless there is a reason not to.
    ///
    /// The part comes from the lane, not from the zone, so a point inside a lane but outside that part's zone
    /// still names the part with <c>Handle.None</c>. That is deliberate and load-bearing: clicking an empty
    /// spot in a lane auditions *that part* at that key and velocity and hears nothing, which is how the map
    /// answers "does this part respond here?".</summary>
    public static (int? Part, PmtZoneMapping.Handle Handle) HitTest(double x, double y,
        IReadOnlyList<LayerZone> zones, double w, double laneAreaH, double margin)
    {
        var lane = LaneAt(y, laneAreaH);
        if (lane is not { } part) return (null, PmtZoneMapping.Handle.None);

        foreach (var z in zones)
        {
            if (z.PartNo != part) continue;
            return (part, PmtZoneMapping.HitRect(x, y, ZoneRect(z, w, laneAreaH), margin));
        }

        return (part, PmtZoneMapping.Handle.None);
    }

    /// <summary>What a drag means: the zone the caller should write, given the zone as it was when the pointer
    /// went down and where the pointer is now. The control does the events; this does the arithmetic.
    ///
    /// Resolved from the press-time zone rather than accumulated from the last position, so a drag cannot
    /// drift and returning the pointer to where it started restores exactly the values that were there.
    /// <paramref name="keyNow"/> and <paramref name="velNow"/> come from <see cref="KeyAt"/> and
    /// <see cref="VelocityAt"/> — and <c>VelocityAt</c> must be asked about the part the drag was captured on.
    /// That is why the part is not a parameter here: it is already baked into the velocities the caller hands
    /// over, so there is no second place for the two to disagree.
    ///
    /// **Lo may not cross hi, and the rule is block, not swap.** Swapping would invert the zone's meaning
    /// halfway through the gesture and, worse, the edge the user grabbed would stop being the edge under their
    /// pointer. Blocking keeps the grabbed edge under the pointer and keeps the zone valid at every step.
    /// <c>lo == hi</c> is legal and means one key, or one velocity step, so the block is at <c>lo &lt;= hi</c>
    /// and not at some minimum span.
    ///
    /// A <c>Body</c> drag preserves both spans: dragged past an end the movement stops, it does not squash.
    /// Squashing would be silent data loss — the user drags too far and the zone they were only trying to move
    /// is narrower when they let go.
    ///
    /// **The rules themselves are <see cref="PmtZoneMapping.ResolveDrag"/>'s**, over four plain ints, and this is
    /// the <see cref="LayerZone"/>-shaped wrapper around them. They moved down there when the PMT zone editor
    /// needed the same rules: that chart has four zones in sixteen styled properties and no tone name, so a
    /// LayerZone is the wrong currency for it, while the four numbers are exactly what both charts have. What is
    /// left here is the wrapping — the fades, the part number and the labels ride through untouched, which is
    /// also the guarantee that no drag on this chart can write a fade.</summary>
    public static LayerZone ResolveDrag(LayerZone origin, PmtZoneMapping.Handle handle,
        int keyNow, int velNow, int keyAtPress, int velAtPress)
    {
        var (keyLo, keyHi, velLo, velHi) = PmtZoneMapping.ResolveDrag(
            origin.KeyLo, origin.KeyHi, origin.VelLo, origin.VelHi, handle,
            keyNow, velNow, keyAtPress, velAtPress);

        return origin with { KeyLo = keyLo, KeyHi = keyHi, VelLo = velLo, VelHi = velHi };
    }

    // ---- Fades -------------------------------------------------------------------------------------------
    //
    // ASSUMPTION, not yet confirmed on hardware: a fade width is the span *outside* the range over which the
    // part fades in or out, so a `Keyboard Fade Width Lower` of 12 means the part fades in across the twelve
    // semitones *below* `Keyboard Range Lower`. That is how Roland documents the parameter, but it has not
    // been checked against a device -- hardware step 10 of this plan is what confirms it. If the fade turns
    // out to be drawn *inside* the range instead, these four functions are the only place that changes.
    //
    // Each band is clipped to the axis it lives on -- the chart for keys, the lane for velocity -- so a fade
    // wider than the room available becomes a narrower band, never a band starting off the chart or spilling
    // into the neighbouring part's lane.

    /// <summary>The band below the key range over which the part fades in: the cells of keys
    /// <c>KeyLo - KeyFadeLo</c> up to <c>KeyLo - 1</c>, clipped at key 0, over the zone's own velocity extent.
    ///
    /// <para>Cell edges, like the body — its right edge <i>is</i> <c>body.X</c>, so the gradient meets the fill
    /// with no seam and no overlap however the arithmetic rounds. Against key positions the two would have met
    /// half a cell out, which on a taper reads as the fade starting in the wrong place rather than as a
    /// misalignment.</para></summary>
    public static PmtZoneMapping.Rect KeyFadeLowerRect(LayerZone z, double w, double laneAreaH)
    {
        var body = ZoneRect(z, w, laneAreaH);
        var from = PmtZoneMapping.Clamp(Math.Min(z.KeyLo, z.KeyHi) - z.KeyFadeLo);
        var x = KeyBoundaryX(from, w);
        return new PmtZoneMapping.Rect(x, body.Y, body.X - x, body.H);
    }

    /// <summary>The band above the key range over which the part fades out: the cells of keys <c>KeyHi + 1</c>
    /// up to <c>KeyHi + KeyFadeHi</c>, clipped at key 127. Cell edges, for the reason on the lower band.</summary>
    public static PmtZoneMapping.Rect KeyFadeUpperRect(LayerZone z, double w, double laneAreaH)
    {
        var body = ZoneRect(z, w, laneAreaH);
        var to = PmtZoneMapping.Clamp(Math.Max(z.KeyLo, z.KeyHi) + z.KeyFadeHi);
        var cell = KeyCell(to, w);
        var x = body.X + body.W;
        return new PmtZoneMapping.Rect(x, body.Y, cell.X + cell.W - x, body.H);
    }

    /// <summary>The band below the velocity range over which the part fades in — which is *below* the zone in
    /// the lane, because loud is up. Clipped at velocity 0, i.e. at the bottom of the lane.</summary>
    public static PmtZoneMapping.Rect VelFadeLowerRect(LayerZone z, double w, double laneAreaH)
    {
        var body = ZoneRect(z, w, laneAreaH);
        var lane = LaneRect(z.PartNo, w, laneAreaH);
        var from = PmtZoneMapping.Clamp(Math.Min(z.VelLo, z.VelHi) - z.VelFadeLo);
        var y = body.Y + body.H;
        return new PmtZoneMapping.Rect(body.X, y, body.W,
            lane.Y + PmtZoneMapping.VelToY(from, lane.H) - y);
    }

    /// <summary>The band above the velocity range over which the part fades out, above the zone in the lane.
    /// Clipped at velocity 127, i.e. at the top of the lane.</summary>
    public static PmtZoneMapping.Rect VelFadeUpperRect(LayerZone z, double w, double laneAreaH)
    {
        var body = ZoneRect(z, w, laneAreaH);
        var lane = LaneRect(z.PartNo, w, laneAreaH);
        var to = PmtZoneMapping.Clamp(Math.Max(z.VelLo, z.VelHi) + z.VelFadeHi);
        var y = lane.Y + PmtZoneMapping.VelToY(to, lane.H);
        return new PmtZoneMapping.Rect(body.X, y, body.W, body.Y - y);
    }
}
