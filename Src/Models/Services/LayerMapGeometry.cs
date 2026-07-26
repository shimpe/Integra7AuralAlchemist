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

/// <summary>Where the layer map draws things and what is under the pointer. Pure, and built on
/// <see cref="PmtZoneMapping"/> rather than re-deriving it: the key axis is the same axis the PMT zone editor
/// uses, and velocity is that editor's Y mapping applied within one lane instead of the whole chart.
///
/// Sixteen lanes, part 1 at the top. A lane spans the full key range; a zone occupies part of its lane
/// horizontally by key and vertically by velocity, so which parts answer a given key is read down a column
/// and how loudly each answers is read within its own row.</summary>
public static class LayerMapGeometry
{
    /// <summary>Lanes, one per part.</summary>
    public const int Lanes = Constants.NO_OF_PARTS;

    /// <summary>The whole of one part's row.</summary>
    public static PmtZoneMapping.Rect LaneRect(int part, double w, double h)
    {
        var laneH = h / Lanes;
        return new PmtZoneMapping.Rect(0, part * laneH, w, laneH);
    }

    /// <summary>Which part's lane contains <paramref name="y"/>, or null when the point is off the chart.
    /// Null rather than clamped: a pointer that leaves the chart must stop the drag it was doing, not carry on
    /// editing the nearest part.</summary>
    public static int? LaneAt(double y, double w, double h)
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

    /// <summary>The velocity <paramref name="y"/> names within <paramref name="part"/>'s lane. The same Y in
    /// a different lane is a different velocity, which is what makes a lane a lane.</summary>
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
        var lane = LaneAt(y, w, h);
        if (lane is not { } part) return (null, PmtZoneMapping.Handle.None);

        foreach (var z in zones)
        {
            if (z.PartNo != part) continue;
            var r = ZoneRect(part, z.KeyLo, z.KeyHi, z.VelLo, z.VelHi, w, h);
            return (part, PmtZoneMapping.HitRect(x, y, r, margin));
        }

        return (part, PmtZoneMapping.Handle.None);
    }

    /// <summary>How wide a fade is, in pixels. A fade width is a span of keys or of velocity steps, so its
    /// pixels are that span's pixels — which is why these are widths and not positions.</summary>
    public static double KeyFadeLowerWidth(LayerZone z, double w) => PmtZoneMapping.KeyToX(z.KeyFadeLo, w);

    public static double KeyFadeUpperWidth(LayerZone z, double w) => PmtZoneMapping.KeyToX(z.KeyFadeHi, w);

    public static double VelFadeLowerHeight(LayerZone z, double h)
    {
        var laneH = h / Lanes;
        return laneH - PmtZoneMapping.VelToY(z.VelFadeLo, laneH);
    }

    public static double VelFadeUpperHeight(LayerZone z, double h)
    {
        var laneH = h / Lanes;
        return laneH - PmtZoneMapping.VelToY(z.VelFadeHi, laneH);
    }
}
