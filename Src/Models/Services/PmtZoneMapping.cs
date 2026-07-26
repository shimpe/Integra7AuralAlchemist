using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the PMT key×velocity zone editor. X is the
/// MIDI key (0..127, left→right), Y is velocity (0..127, loud at top). No Avalonia dependency.</summary>
public static class PmtZoneMapping
{
    public const int Min = 0, Max = 127;

    public readonly record struct Rect(double X, double Y, double W, double H);
    public enum Handle { None, Body, Left, Right, Top, Bottom }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double KeyToX(int key, double w) => Clamp(key) / 127.0 * w;
    public static int XToKey(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    public static double VelToY(int vel, double h) => (1.0 - Clamp(vel) / 127.0) * h;
    public static int YToVel(double y, double h)
        => h <= 0 ? Min : Clamp((int)Math.Round((1.0 - y / h) * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Pixel rectangle for a zone. Tolerates lo/hi being swapped.</summary>
    public static Rect ToRect(int keyLo, int keyHi, int velLo, int velHi, double w, double h)
    {
        var x = KeyToX(Math.Min(keyLo, keyHi), w);
        var x2 = KeyToX(Math.Max(keyLo, keyHi), w);
        var yTop = VelToY(Math.Max(velLo, velHi), h);
        var yBot = VelToY(Math.Min(velLo, velHi), h);
        return new Rect(x, yTop, x2 - x, yBot - yTop);
    }

    // ---- Fade bands --------------------------------------------------------------------------------------
    //
    // The whole-chart twins of LayerMapGeometry's four *FadeRect functions. Same rule, same assumption, no
    // lanes: here a zone occupies the whole velocity axis of the chart rather than a sixteenth of it, so the
    // velocity bands are measured against `h` directly instead of against a lane's own height.
    //
    // ASSUMPTION, not yet confirmed on hardware, and the same one the layer map is drawn on: a fade width is
    // the span *outside* the range over which the partial or part fades in or out, so a `Keyboard Fade Width
    // Lower` of 12 means it fades in across the twelve semitones *below* `Keyboard Range Lower`. That is how
    // Roland documents the parameter. If it turns out to be drawn *inside* the range instead, these four
    // functions and LayerMapGeometry's four are the only places that change -- which is exactly why the
    // arithmetic is here and not in the controls.
    //
    // Each band's inner edge is taken from ToRect's own body rectangle rather than recomputed from the range,
    // so the gradient meets the fill with no seam and no overlap however the arithmetic rounds. Its outer edge
    // is clamped into 0..127 before it is mapped, so a fade wider than the room available becomes a narrower
    // band -- never a band that starts off the left of the chart or runs off the right of it. A zone three
    // semitones above the bottom of the keyboard with a twelve-semitone lower fade gets a three-semitone band,
    // because three semitones is all the keyboard it has to fade across.
    //
    // Unlike the layer map these are *position*-based on both axes, not cell-based on the key axis: this chart
    // draws its zones with ToRect, which maps a key to a position, and a band whose inner edge did not sit
    // exactly on the body's edge would read as the fade starting in the wrong place. The two charts differ here
    // because their bodies differ, and each band agrees with the body it belongs to.

    /// <summary>The band below the key range over which the zone fades in: from <c>keyLo - fade</c> (clipped at
    /// key 0) up to the body's left edge, over the zone's own velocity extent. Zero-width when
    /// <paramref name="fade"/> is 0, or when the range already starts at key 0.</summary>
    public static Rect KeyFadeLowerRect(int keyLo, int keyHi, int velLo, int velHi, int fade, double w, double h)
    {
        var body = ToRect(keyLo, keyHi, velLo, velHi, w, h);
        var x = KeyToX(Clamp(Math.Min(keyLo, keyHi) - fade), w);
        return new Rect(x, body.Y, body.X - x, body.H);
    }

    /// <summary>The band above the key range over which the zone fades out: from the body's right edge up to
    /// <c>keyHi + fade</c>, clipped at key 127.</summary>
    public static Rect KeyFadeUpperRect(int keyLo, int keyHi, int velLo, int velHi, int fade, double w, double h)
    {
        var body = ToRect(keyLo, keyHi, velLo, velHi, w, h);
        var x = body.X + body.W;
        return new Rect(x, body.Y, KeyToX(Clamp(Math.Max(keyLo, keyHi) + fade), w) - x, body.H);
    }

    /// <summary>The band below the velocity range over which the zone fades in — which is drawn <i>below</i>
    /// the body, because loud is up. Clipped at velocity 0, i.e. at the bottom of the chart.</summary>
    public static Rect VelFadeLowerRect(int keyLo, int keyHi, int velLo, int velHi, int fade, double w, double h)
    {
        var body = ToRect(keyLo, keyHi, velLo, velHi, w, h);
        var y = body.Y + body.H;
        return new Rect(body.X, y, body.W, VelToY(Clamp(Math.Min(velLo, velHi) - fade), h) - y);
    }

    /// <summary>The band above the velocity range over which the zone fades out, drawn above the body. Clipped
    /// at velocity 127, i.e. at the top of the chart.</summary>
    public static Rect VelFadeUpperRect(int keyLo, int keyHi, int velLo, int velHi, int fade, double w, double h)
    {
        var body = ToRect(keyLo, keyHi, velLo, velHi, w, h);
        var y = VelToY(Clamp(Math.Max(velLo, velHi) + fade), h);
        return new Rect(body.X, y, body.W, body.Y - y);
    }

    /// <summary>Which part of one rectangle is under (px,py): an edge within <paramref name="margin"/>,
    /// else the body if inside, else None. Edges win over body.</summary>
    public static Handle HitRect(double px, double py, Rect r, double margin)
    {
        var inX = px >= r.X - margin && px <= r.X + r.W + margin;
        var inY = py >= r.Y - margin && py <= r.Y + r.H + margin;
        if (!inX || !inY) return Handle.None;
        if (Math.Abs(px - r.X) <= margin) return Handle.Left;
        if (Math.Abs(px - (r.X + r.W)) <= margin) return Handle.Right;
        if (Math.Abs(py - r.Y) <= margin) return Handle.Top;
        if (Math.Abs(py - (r.Y + r.H)) <= margin) return Handle.Bottom;
        if (px > r.X && px < r.X + r.W && py > r.Y && py < r.Y + r.H) return Handle.Body;
        return Handle.None;
    }
}
