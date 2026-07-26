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
/// leaves the wrappers to be wrappers.</para>
///
/// <para><b>And a difference is not on its own permission to write.</b> <see cref="Between"/> answers what
/// differs; <see cref="FieldsFor"/> answers what a given drag is entitled to touch, and a caller writing on
/// behalf of a drag needs both — see <see cref="FieldsFor"/> for the reverting bug that the diff alone
/// allows.</para></summary>
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

    /// <summary>The fields a drag on <paramref name="handle"/> is allowed to write — everything else is off
    /// limits to it, whatever the values happen to say.
    ///
    /// <para><b>Why a mask and not just a diff.</b> The control resolves every move as <c>origin with { …the
    /// dragged field… }</c>, where <c>origin</c> is the zone as it was when the pointer went down — deliberately,
    /// so a slow drag cannot accumulate rounding drift and so returning the pointer to where it started restores
    /// exactly the values that were there. That makes the seven fields the drag is not moving <i>press-time</i>
    /// values, and press-time values go stale the moment anything else changes the part: a front-panel edit, or a
    /// Studio Set change, which resyncs all sixteen parts at once.</para>
    ///
    /// <para>Diffed against the live values, a stale field reads as a change and gets written. Concretely: hold a
    /// drag on part 3's left edge, and a Studio Set change sets that part's <c>Velocity Fade Width Lower</c> to
    /// 20. The next pointer move raises a zone still carrying the press-time 10; the diff says <c>VelFadeLo</c>
    /// changed, and the map writes 10 — silently reverting what the instrument had just reported, for a field the
    /// user never touched. Masking by the handle kills that whole class of bug rather than the one instance: a key
    /// drag can then never write a velocity value or a fade value, however stale its origin is.</para>
    ///
    /// <para><b>No handle owns a fade field.</b> That is the invariant, not an omission. The map draws fades and
    /// does not drag them — the four knobs are on the part's own Set Part tab, which the readout's <b>Edit
    /// fades…</b> button leads to — so no drag on this chart may ever write one, and this is where that is
    /// enforced. Adding fade dragging later means giving some handle a fade field here, and nowhere else.</para>
    ///
    /// <para><see cref="PmtZoneMapping.Handle.None"/> owns nothing: a press on the empty part of a lane is a
    /// question the chart answers by sounding a note, never an edit. <c>Top</c> owns <c>VelHi</c> and
    /// <c>Bottom</c> owns <c>VelLo</c> because loud is up — the same orientation
    /// <c>LayerMapGeometry.ResolveDrag</c> resolves those two handles with, and the reason the pairing is worth a
    /// test of its own.</para></summary>
    public static LayerZoneField FieldsFor(PmtZoneMapping.Handle handle) => handle switch
    {
        PmtZoneMapping.Handle.Left => LayerZoneField.KeyLo,
        PmtZoneMapping.Handle.Right => LayerZoneField.KeyHi,
        PmtZoneMapping.Handle.Top => LayerZoneField.VelHi,
        PmtZoneMapping.Handle.Bottom => LayerZoneField.VelLo,
        // A body drag moves the zone bodily, so it owns all four range values -- and only those four. The spans
        // are preserved rather than the edges written independently, but that is ResolveDrag's business; here it
        // is simply four fields wide.
        PmtZoneMapping.Handle.Body => LayerZoneField.KeyLo | LayerZoneField.KeyHi |
                                      LayerZoneField.VelLo | LayerZoneField.VelHi,
        _ => LayerZoneField.None,
    };
}
