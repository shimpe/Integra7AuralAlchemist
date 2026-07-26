using System;
using Avalonia;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>How a key×velocity zone and its crossfades are painted. One implementation, shared by every chart
/// in the application that draws a range with fades on it — <see cref="LayerMapControl"/>'s sixteen parts and
/// <see cref="PmtZoneEditorControl"/>'s four partials today, the drum WMT map next.
///
/// <para>It exists because the alternative is worse than not having the feature at all: the same crossfade drawn
/// two ways in one application teaches the user two different things about the same parameter, and the two copies
/// drift the first time either is tuned. The fill opacity, the floor and the dash pattern are constants here for
/// the same reason — they are the vocabulary a reader learns on one chart and applies to the next, so there must
/// be exactly one of each.</para>
///
/// <para>Deliberately not in <see cref="PmtZoneMapping"/> or <c>LayerMapGeometry</c>: those are pure and free of
/// Avalonia so a test can reach them, and a <c>DrawingContext</c> in either would end that. What is here is only
/// the painting — every rectangle it is handed has already been worked out, clipped and (on the layer map)
/// placed in its lane by the geometry, and nothing below measures, clamps or second-guesses one.</para></summary>
public static class ZoneShading
{
    /// <summary>Which side of the body a band lies on, and therefore which way it fades and where its outer edge
    /// is. Named for where the band is drawn rather than for the parameter it comes from: <see cref="Below"/> is
    /// the *lower* velocity fade, because loud is up and a range's soft end is the bottom of its box.
    ///
    /// <para>An enum rather than the pair of relative gradient corners this took when it lived in
    /// <c>LayerMapControl</c>. Four call sites became twenty when the PMT chart joined, and
    /// <c>new Point(0, 1), new Point(0, 0)</c> at a call site is a puzzle with a wrong answer available; the
    /// corners are still there, in one place, below.</para></summary>
    public enum FadeSide
    {
        /// <summary>Left of the body: the lower key fade, over the keys below the range.</summary>
        Left,

        /// <summary>Right of the body: the upper key fade, over the keys above the range.</summary>
        Right,

        /// <summary>Under the body: the lower velocity fade, over the velocities below the range.</summary>
        Below,

        /// <summary>Over the body: the upper velocity fade, over the velocities above the range.</summary>
        Above
    }

    /// <summary>How solid a zone's body fill is. Shared by both charts so a range means the same thing to the eye
    /// on each of them, and shared with the fades below so a band meets the body it belongs to exactly — a seam
    /// there reads as a step, which is the one thing a taper must not look like.
    ///
    /// <para>The outline carries the edge; the fill only has to say "this area belongs to this zone" without
    /// hiding the lane stripe, the keyboard tint or a neighbouring fade beneath it.</para></summary>
    public const double FillOpacity = 0.22;

    /// <summary>How much of the body's fill a fade band still has at its far end, where the zone is only just
    /// starting to be heard. Not zero, which is what it was: on a ground this dark a translucent colour tapering
    /// to nothing disappears well before the band does, so a crossfade looked like the hard split the band exists
    /// to disprove. Half keeps the whole band present while still reading as quieter than the body — the taper is
    /// the signal, and a floor does not remove it, it only stops it running out of contrast.</summary>
    public const double FadeFloorFraction = 0.5;

    /// <summary>The dashed line marking where a fade begins. Thinner than a body outline and dashed against its
    /// solid, so the two edges of a crossfade are told apart at a glance: dashed is where the zone starts to be
    /// heard, solid is where it reaches full level.</summary>
    public const double FadeEdgeThickness = 1;

    /// <inheritdoc cref="FadeEdgeThickness"/>
    public static readonly DashStyle FadeEdgeDashes = new([3, 3], 0);

    /// <summary>One fade band, as a gradient across the rectangle the geometry gave for it, with its outer edge
    /// marked.</summary>
    /// <param name="band">Where the band goes, already clipped to the chart (and to its lane, on the layer map).
    /// A zero-extent rectangle — the common case, since most zones have no fade at all — draws nothing.</param>
    /// <param name="color">The zone's own colour. Both gradient stops are this colour at an explicit alpha.</param>
    /// <param name="side">Which side of the body the band is on.</param>
    public static void DrawFade(DrawingContext context, PmtZoneMapping.Rect band, Color color, FadeSide side)
    {
        // A fade width of zero is a band of no extent: nothing to draw, and no gradient whose direction would be
        // undefined. A range that already reaches the end of its axis produces the same thing, because the
        // geometry clipped the band to the room actually available.
        if (band.W <= 0 || band.H <= 0) return;

        // Both ends are the zone's own colour at an explicit alpha, never a named transparent: a gradient
        // towards a colourless stop washes through grey on the way, and the band would read as a shadow instead
        // of as the zone getting quieter.
        var solid = (byte)Math.Round(byte.MaxValue * FillOpacity);
        var floor = (byte)Math.Round(byte.MaxValue * FillOpacity * FadeFloorFraction);

        // The band's two ends as relative corners of its own rectangle: `outer` is the end away from the range,
        // where the zone is faintest, and `inner` is the end that meets the body and must match its fill.
        var (outer, inner) = side switch
        {
            FadeSide.Left => (new Point(0, 0), new Point(1, 0)),
            FadeSide.Right => (new Point(1, 0), new Point(0, 0)),
            FadeSide.Below => (new Point(0, 1), new Point(0, 0)),
            _ => (new Point(0, 0), new Point(0, 1)), // Above
        };

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(outer, RelativeUnit.Relative),
            EndPoint = new RelativePoint(inner, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(floor, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(solid, color.R, color.G, color.B), 1)
            }
        };

        context.FillRectangle(brush, new Rect(band.X, band.Y, band.W, band.H));

        // The band's outer edge: where the zone first starts to be heard. At full strength, because a gradient's
        // own far end is by definition the faintest thing on the chart and cannot mark its own boundary.
        var (from, to) = side switch
        {
            FadeSide.Left => (new Point(band.X, band.Y), new Point(band.X, band.Y + band.H)),
            FadeSide.Right => (new Point(band.X + band.W, band.Y), new Point(band.X + band.W, band.Y + band.H)),
            FadeSide.Below => (new Point(band.X, band.Y + band.H), new Point(band.X + band.W, band.Y + band.H)),
            _ => (new Point(band.X, band.Y), new Point(band.X + band.W, band.Y)), // Above
        };

        context.DrawLine(new Pen(new SolidColorBrush(color), FadeEdgeThickness, FadeEdgeDashes), from, to);
    }

    /// <summary>The colour behind a solid brush. A gradient is built from colours and not from brushes, so the
    /// fades need this; anything that is not a single colour has none to fade from and fades from nothing, which
    /// draws an empty band rather than throwing.</summary>
    public static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
}
