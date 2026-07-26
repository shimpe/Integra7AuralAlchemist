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
/// The control on top of this should contain no arithmetic: it turns pointer events into calls here and
/// drawing calls out of the results. That is not tidiness — it is the only layer of this feature a test can
/// reach, since there is no headless-Avalonia harness in this repository.</summary>
public static class LayerMapGeometry
{
    /// <summary>Lanes, one per part.</summary>
    public const int Lanes = Constants.NO_OF_PARTS;

    /// <summary>The least a lane can be and still work: about a line of text tall, which also leaves roughly
    /// twelve pixels of body between two four-pixel velocity grab margins. Below this the top and bottom
    /// handles of a full-lane zone meet in the middle and the zone stops being draggable.</summary>
    public const double MinLaneHeight = 20;

    /// <summary>What the view must give the chart. Here rather than typed into XAML so it cannot drift from
    /// the lane height the rest of this class assumes.</summary>
    public static double MinHeight => Lanes * MinLaneHeight;

    /// <summary>The whole of one part's row.</summary>
    public static PmtZoneMapping.Rect LaneRect(int part, double w, double h)
    {
        var laneH = h / Lanes;
        return new PmtZoneMapping.Rect(0, part * laneH, w, laneH);
    }

    /// <summary>Which part's lane contains <paramref name="y"/>, or null when the point is off the chart.
    /// Null rather than clamped: a pointer that leaves the chart must stop the drag it was doing, not carry on
    /// editing the nearest part.</summary>
    public static int? LaneAt(double y, double h)
    {
        if (h <= 0 || y < 0 || y >= h) return null;
        var lane = (int)(y / (h / Lanes));
        return lane < 0 || lane >= Lanes ? null : lane;
    }

    /// <summary>The rectangle a zone occupies: its key range horizontally, its velocity range vertically
    /// within its own lane, loud at the top.</summary>
    public static PmtZoneMapping.Rect ZoneRect(int part, int keyLo, int keyHi, int velLo, int velHi,
        double w, double h)
    {
        var lane = LaneRect(part, w, h);
        var x = PmtZoneMapping.KeyToX(Math.Min(keyLo, keyHi), w);
        var x2 = PmtZoneMapping.KeyToX(Math.Max(keyLo, keyHi), w);
        // VelToY over the lane's own height, then offset into the lane.
        var yTop = lane.Y + PmtZoneMapping.VelToY(Math.Max(velLo, velHi), lane.H);
        var yBot = lane.Y + PmtZoneMapping.VelToY(Math.Min(velLo, velHi), lane.H);
        return new PmtZoneMapping.Rect(x, yTop, x2 - x, yBot - yTop);
    }

    /// <summary>The rectangle <paramref name="z"/> occupies. The overload taking loose numbers stays for
    /// callers mid-drag, which have a candidate range and not yet a zone.</summary>
    public static PmtZoneMapping.Rect ZoneRect(LayerZone z, double w, double h)
        => ZoneRect(z.PartNo, z.KeyLo, z.KeyHi, z.VelLo, z.VelHi, w, h);

    /// <summary>The velocity <paramref name="y"/> names within <paramref name="part"/>'s lane. The same Y in
    /// a different lane is a different velocity, which is what makes a lane a lane.
    ///
    /// A Y outside the lane clamps — 127 above it, 0 below — rather than refusing. That is exactly what a
    /// velocity drag wants, provided the caller passes the part the drag was *captured* on and not the part
    /// <see cref="LaneAt"/> reports for the pointer's current Y: dragging past the top of the lane then pins
    /// the edge at 127 instead of quietly re-reading the value in the neighbouring part's lane.</summary>
    public static int VelocityAt(int part, double y, double h)
    {
        var lane = LaneRect(part, 0, h);
        return PmtZoneMapping.YToVel(y - lane.Y, lane.H);
    }

    /// <summary>The key <paramref name="x"/> names. A straight pass-through, here so a caller never has to
    /// mix the two mapping classes.</summary>
    public static int KeyAt(double x, double w) => PmtZoneMapping.XToKey(x, w);

    /// <summary>What is under the pointer: which part's lane, and which part of that part's zone.
    ///
    /// The part comes from the lane, not from the zone, so a point inside a lane but outside that part's zone
    /// still names the part with <c>Handle.None</c>. That is deliberate and load-bearing: clicking an empty
    /// spot in a lane auditions *that part* at that key and velocity and hears nothing, which is how the map
    /// answers "does this part respond here?".</summary>
    public static (int? Part, PmtZoneMapping.Handle Handle) HitTest(double x, double y,
        IReadOnlyList<LayerZone> zones, double w, double h, double margin)
    {
        var lane = LaneAt(y, h);
        if (lane is not { } part) return (null, PmtZoneMapping.Handle.None);

        foreach (var z in zones)
        {
            if (z.PartNo != part) continue;
            var r = ZoneRect(part, z.KeyLo, z.KeyHi, z.VelLo, z.VelHi, w, h);
            return (part, PmtZoneMapping.HitRect(x, y, r, margin));
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
    /// is narrower when they let go.</summary>
    public static LayerZone ResolveDrag(LayerZone origin, PmtZoneMapping.Handle handle,
        int keyNow, int velNow, int keyAtPress, int velAtPress)
    {
        // The pointer's values are the only untrusted ones; the origin's come from 0..127 parameters.
        var key = PmtZoneMapping.Clamp(keyNow);
        var vel = PmtZoneMapping.Clamp(velNow);

        switch (handle)
        {
            case PmtZoneMapping.Handle.Left:
                return origin with { KeyLo = Math.Min(key, origin.KeyHi) };

            case PmtZoneMapping.Handle.Right:
                return origin with { KeyHi = Math.Max(key, origin.KeyLo) };

            case PmtZoneMapping.Handle.Top:
                return origin with { VelHi = Math.Max(vel, origin.VelLo) };

            case PmtZoneMapping.Handle.Bottom:
                return origin with { VelLo = Math.Min(vel, origin.VelHi) };

            case PmtZoneMapping.Handle.Body:
                var dKey = ShiftPreservingSpan(origin.KeyLo, origin.KeyHi,
                    key - PmtZoneMapping.Clamp(keyAtPress));
                var dVel = ShiftPreservingSpan(origin.VelLo, origin.VelHi,
                    vel - PmtZoneMapping.Clamp(velAtPress));
                return origin with
                {
                    KeyLo = PmtZoneMapping.Clamp(origin.KeyLo + dKey),
                    KeyHi = PmtZoneMapping.Clamp(origin.KeyHi + dKey),
                    VelLo = PmtZoneMapping.Clamp(origin.VelLo + dVel),
                    VelHi = PmtZoneMapping.Clamp(origin.VelHi + dVel)
                };

            default:
                // Handle.None: a press on an empty spot in a lane is a question, not an edit.
                return origin;
        }
    }

    /// <summary>The largest part of <paramref name="delta"/> that moves lo..hi without either end leaving
    /// 0..127, so the span survives the drag and only the movement is cut short. A range that already fills
    /// the axis cannot move along it at all, which is correct: there is nowhere for it to go.</summary>
    private static int ShiftPreservingSpan(int lo, int hi, int delta)
    {
        if (lo + delta < PmtZoneMapping.Min) delta = PmtZoneMapping.Min - lo;
        if (hi + delta > PmtZoneMapping.Max) delta = PmtZoneMapping.Max - hi;
        return delta;
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

    /// <summary>The band below the key range over which the part fades in: from <c>KeyLo - KeyFadeLo</c> up to
    /// <c>KeyLo</c>, clipped at key 0, over the zone's own velocity extent.</summary>
    public static PmtZoneMapping.Rect KeyFadeLowerRect(LayerZone z, double w, double h)
    {
        var body = ZoneRect(z, w, h);
        var from = PmtZoneMapping.Clamp(Math.Min(z.KeyLo, z.KeyHi) - z.KeyFadeLo);
        var x = PmtZoneMapping.KeyToX(from, w);
        return new PmtZoneMapping.Rect(x, body.Y, body.X - x, body.H);
    }

    /// <summary>The band above the key range over which the part fades out: from <c>KeyHi</c> up to
    /// <c>KeyHi + KeyFadeHi</c>, clipped at key 127.</summary>
    public static PmtZoneMapping.Rect KeyFadeUpperRect(LayerZone z, double w, double h)
    {
        var body = ZoneRect(z, w, h);
        var to = PmtZoneMapping.Clamp(Math.Max(z.KeyLo, z.KeyHi) + z.KeyFadeHi);
        var x = body.X + body.W;
        return new PmtZoneMapping.Rect(x, body.Y, PmtZoneMapping.KeyToX(to, w) - x, body.H);
    }

    /// <summary>The band below the velocity range over which the part fades in — which is *below* the zone in
    /// the lane, because loud is up. Clipped at velocity 0, i.e. at the bottom of the lane.</summary>
    public static PmtZoneMapping.Rect VelFadeLowerRect(LayerZone z, double w, double h)
    {
        var body = ZoneRect(z, w, h);
        var lane = LaneRect(z.PartNo, w, h);
        var from = PmtZoneMapping.Clamp(Math.Min(z.VelLo, z.VelHi) - z.VelFadeLo);
        var y = body.Y + body.H;
        return new PmtZoneMapping.Rect(body.X, y, body.W,
            lane.Y + PmtZoneMapping.VelToY(from, lane.H) - y);
    }

    /// <summary>The band above the velocity range over which the part fades out, above the zone in the lane.
    /// Clipped at velocity 127, i.e. at the top of the lane.</summary>
    public static PmtZoneMapping.Rect VelFadeUpperRect(LayerZone z, double w, double h)
    {
        var body = ZoneRect(z, w, h);
        var lane = LaneRect(z.PartNo, w, h);
        var to = PmtZoneMapping.Clamp(Math.Max(z.VelLo, z.VelHi) + z.VelFadeHi);
        var y = lane.Y + PmtZoneMapping.VelToY(to, lane.H);
        return new PmtZoneMapping.Rect(body.X, y, body.W, body.Y - y);
    }
}
