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

    // ---- What a drag means -------------------------------------------------------------------------------
    //
    // HitRect's other half. HitRect answers "what did the user grab"; this answers "and what does moving it
    // do". The two belong together and they belong *here*, because both charts that draw a key x velocity zone
    // ask the first question of this class and, until this function existed, answered the second one twice --
    // LayerMapGeometry.ResolveDrag in parameter values with tests, and PmtZoneEditorControl.OnPointerMoved in
    // pixel deltas without any. Two implementations of one rule is two behaviours the moment either is touched,
    // and the user of an application that draws the same chart twice is entitled to have it mean the same thing
    // both times.
    //
    // Four plain ints rather than a LayerZone, which is why the shared form lives here and not there: a
    // LayerZone also carries four fade widths, a part number, a label and a tone name, and the PMT chart has a
    // part number that means a partial, no tone name at all, and its fades in eight separate styled properties.
    // The rule is about the four numbers; everything else is the caller's shape and stays the caller's problem.
    // LayerMapGeometry.ResolveDrag is the LayerZone-shaped wrapper and does nothing but call this.

    /// <summary>The four values a drag should write, given the zone as it was when the pointer went down and
    /// where the pointer is now.
    ///
    /// <para><b>Values, not pixels.</b> Everything here is a key number or a velocity step, so each chart keeps
    /// its own pixels-to-values mapping — whole-chart for the PMT editor, per-lane for the layer map — and
    /// neither maps them twice. It is also the only reason any of this is testable: there is no headless-Avalonia
    /// harness in this repository, so arithmetic that stays in a control is arithmetic nothing can check.</para>
    ///
    /// <para><b>Resolved from the press, not accumulated.</b> <paramref name="keyLo"/>..<paramref name="velHi"/>
    /// are the zone as it was when the pointer went down, not as it is now, and <paramref name="keyAtPress"/> /
    /// <paramref name="velAtPress"/> are where the press landed in the same units. A drag therefore cannot
    /// accumulate rounding drift, and returning the pointer to where it started restores exactly the values that
    /// were there. It also means a caller must not feed this its own previous output.</para>
    ///
    /// <para><b>Lo may not cross hi, and the rule is block, not swap.</b> Swapping would invert the zone's
    /// meaning halfway through the gesture and, worse, the edge the user grabbed would stop being the edge under
    /// their pointer. Blocking keeps the grabbed edge under the pointer and keeps the zone valid at every step.
    /// <c>lo == hi</c> is legal and means one key, or one velocity step, so the block is at <c>lo &lt;= hi</c>
    /// and not at some minimum span.</para>
    ///
    /// <para><b>A <see cref="Handle.Body"/> drag preserves both spans:</b> dragged past an end the movement
    /// stops, it does not squash. Squashing would be silent data loss — the user drags too far and the zone they
    /// were only trying to move is narrower when they let go.</para>
    ///
    /// <para>All four values come back on every call, including the three a single-edge drag did not touch, so a
    /// caller never has to reconstruct them. <b>That is not permission to write all four.</b> The untouched three
    /// are press-time values and go stale the moment anything else edits the zone — the instrument's front panel,
    /// or a Studio Set change — so a caller writes only the values the handle it is dragging owns. See
    /// <see cref="LayerZoneChanges.FieldsFor"/>, which is that ownership written down.</para></summary>
    /// <param name="handle">What was grabbed, from <see cref="HitRect"/>.</param>
    /// <param name="keyNow">The key under the pointer now. Clamped here, so a pointer dragged off the chart
    /// pins the edge at 0 or 127 rather than producing a key that is not one.</param>
    /// <param name="velNow">The velocity under the pointer now, likewise clamped.</param>
    public static (int KeyLo, int KeyHi, int VelLo, int VelHi) ResolveDrag(
        int keyLo, int keyHi, int velLo, int velHi, Handle handle,
        int keyNow, int velNow, int keyAtPress, int velAtPress)
    {
        // The pointer's values are the only untrusted ones; a zone's come from 0..127 parameters.
        var key = Clamp(keyNow);
        var vel = Clamp(velNow);

        switch (handle)
        {
            case Handle.Left:
                return (Math.Min(key, keyHi), keyHi, velLo, velHi);

            case Handle.Right:
                return (keyLo, Math.Max(key, keyLo), velLo, velHi);

            // Top is the *loud* edge and Bottom the soft one, on both charts, because both draw velocity with
            // loud at the top. The pairing looks inverted read as geometry and is right read as parameters.
            case Handle.Top:
                return (keyLo, keyHi, velLo, Math.Max(vel, velLo));

            case Handle.Bottom:
                return (keyLo, keyHi, Math.Min(vel, velHi), velHi);

            case Handle.Body:
            {
                var dKey = ShiftPreservingSpan(keyLo, keyHi, key - Clamp(keyAtPress));
                var dVel = ShiftPreservingSpan(velLo, velHi, vel - Clamp(velAtPress));

                // Clamped again after the shift, which ShiftPreservingSpan has already made unnecessary for any
                // range with lo <= hi. It is here for the one that does not: ToRect tolerates a zone stored with
                // lo and hi the wrong way round, so the rest of this class has to as well, and for such a range
                // the shift that keeps *hi* on the axis can carry *lo* off the end of it. Without this the
                // caller would be handed a key of 139 to write to a 0..127 parameter.
                return (Clamp(keyLo + dKey), Clamp(keyHi + dKey), Clamp(velLo + dVel), Clamp(velHi + dVel));
            }

            default:
                // Handle.None: a press that grabbed nothing is a question, not an edit.
                return (keyLo, keyHi, velLo, velHi);
        }
    }

    /// <summary>The largest part of <paramref name="delta"/> that moves lo..hi without either end leaving
    /// 0..127, so the span survives the drag and only the movement is cut short. A range that already fills
    /// the axis cannot move along it at all, which is correct: there is nowhere for it to go.</summary>
    private static int ShiftPreservingSpan(int lo, int hi, int delta)
    {
        if (lo + delta < Min) delta = Min - lo;
        if (hi + delta > Max) delta = Max - hi;
        return delta;
    }
}
