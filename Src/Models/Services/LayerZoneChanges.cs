using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The eight values a <see cref="LayerZone"/> carries that correspond to real parameters, as flags, so
/// "which of these moved" is one value a caller can test and a test can assert on. <see cref="LayerZone.PartNo"/>,
/// <see cref="LayerZone.Label"/> and <see cref="LayerZone.ToneName"/> are not here: they are not parameters, they
/// cannot be written, and a drag cannot change them.</summary>
[Flags]
public enum LayerZoneField
{
    None = 0,
    KeyLo = 1 << 0,
    KeyHi = 1 << 1,
    VelLo = 1 << 2,
    VelHi = 1 << 3,
    KeyFadeLo = 1 << 4,
    KeyFadeHi = 1 << 5,
    VelFadeLo = 1 << 6,
    VelFadeHi = 1 << 7,
}

/// <summary>Which of a zone's eight writable values differ between two snapshots.
///
/// <para>This exists so the layer map's "write only what changed" rule is a checkable claim rather than a
/// trusted one. The rule matters because of what a write costs on the other side: every value handed to a
/// <c>ParamInt</c> becomes a sysex round trip through <c>ThrottledParameterWriter</c> <b>and</b> an entry in the
/// <c>EditJournal</c>. A control that raises the whole zone on every pointer move — which
/// <c>LayerMapControl.ZoneEdited</c> does, on purpose, so the arithmetic can live in one tested place — would
/// therefore have a key drag rewriting the untouched velocity range too, spending round trips on values that did
/// not move and filling the undo history with no-ops the user has to press Undo through.</para>
///
/// <para><b>A second guard, not the only one.</b> <c>ParamInt</c>'s setter already returns early when the value
/// it is handed equals the value it holds, so a redundant write is cheap today. It is still wrong to make one:
/// that early return is another class's implementation detail, it is the wrong place to express an intention
/// about drags, and it says nothing in a test. Naming the changed fields here says what the map means, and
/// leaves the wrappers to be wrappers.</para></summary>
public static class LayerZoneChanges
{
    /// <summary>The fields whose value in <paramref name="to"/> differs from
    /// <paramref name="from"/>. <see cref="LayerZoneField.None"/> when the two agree on all eight — which is the
    /// common case mid-drag, since a pointer can move many pixels without crossing into the next key.
    ///
    /// <para>Fades are compared along with the ranges even though no drag moves them today: the map draws fades
    /// but does not edit them, so <c>ResolveDrag</c> returns them untouched and they will always report unchanged.
    /// Comparing them anyway costs two integer comparisons and means that if fade dragging is ever added, the
    /// writing side is already correct rather than silently dropping the new edits.</para>
    ///
    /// <para>The part numbers are not compared. Two snapshots of different parts have no meaningful field-level
    /// difference and the caller — which looks a zone up <i>by</i> its part number — cannot produce that pairing;
    /// answering as though it could would invite a caller to rely on it.</para></summary>
    public static LayerZoneField Between(LayerZone from, LayerZone to)
    {
        var changed = LayerZoneField.None;
        if (from.KeyLo != to.KeyLo) changed |= LayerZoneField.KeyLo;
        if (from.KeyHi != to.KeyHi) changed |= LayerZoneField.KeyHi;
        if (from.VelLo != to.VelLo) changed |= LayerZoneField.VelLo;
        if (from.VelHi != to.VelHi) changed |= LayerZoneField.VelHi;
        if (from.KeyFadeLo != to.KeyFadeLo) changed |= LayerZoneField.KeyFadeLo;
        if (from.KeyFadeHi != to.KeyFadeHi) changed |= LayerZoneField.KeyFadeHi;
        if (from.VelFadeLo != to.VelFadeLo) changed |= LayerZoneField.VelFadeLo;
        if (from.VelFadeHi != to.VelFadeHi) changed |= LayerZoneField.VelFadeHi;
        return changed;
    }
}
