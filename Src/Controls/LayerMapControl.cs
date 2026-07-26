using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>All sixteen parts' key and velocity ranges on one chart. One lane per part, part 1 at the top, key
/// left-to-right across the lane, and each part's velocity range drawn as its box's vertical extent *within*
/// its own lane. So which parts answer a given key is read straight down a column, and how loudly each answers
/// is read within its row.
///
/// <para>This class draws; it does not compute. Every rectangle it fills comes from
/// <see cref="LayerMapGeometry"/>, which is pure and has tests — and which is the only layer of this feature a
/// test can reach, there being no headless-Avalonia harness in this repository. Arithmetic that drifts in here
/// is arithmetic nothing can check, so it belongs there instead.</para>
///
/// <para>It takes an immutable list of <see cref="LayerZone"/> snapshots rather than sixteen sets of four
/// styled properties. That shape is exactly why <see cref="PmtZoneEditorControl"/> — four zones as sixteen
/// individual properties, plus four setter switch statements to write them back — could not be reused for
/// this; repeating it at sixteen parts would hand the next person the same problem four times over. The view
/// model owns the live parameter wrappers and hands over snapshots; this control knows nothing about view
/// models at all.</para>
///
/// <para>Drawing only, deliberately: pointer handling arrives separately, and the geometry it needs
/// (<see cref="LayerMapGeometry.HitTest"/>, <see cref="LayerMapGeometry.ResolveDrag"/>) is already written and
/// tested. There is no <c>ToolTip</c> here and there must never be one: a tooltip is a popup, it swallows
/// clicks on the control it describes, and this chart is nothing but click targets. See the status-bar comment
/// in <c>MainWindow.axaml</c> for what that cost the last time.</para></summary>
public class LayerMapControl : Control
{
    // ---- Palette -----------------------------------------------------------------------------------------
    //
    // Resource keys, not colours. The codebase rule is that colours live in App.axaml and are referenced by
    // key; a brush built in code with a literal colour in it breaks that just as thoroughly as a hex value in
    // XAML would, because the palette stops being in one place. Two of these keys are new (LayerMap*), the
    // rest are reused from the panels this chart sits beside so it reads as part of the same application.

    /// <summary>The chart's own background — the same dark ground the envelope and PMT graphs use.</summary>
    private const string BackgroundKey = "SnEnvelopeBackgroundBrush";

    /// <summary>Tints every other lane, so sixteen rows can be counted.</summary>
    private const string LaneAltKey = "LayerMapLaneAltBrush";

    /// <summary>The finest lines on the chart: the hairline under each lane.</summary>
    private const string GridKey = "SnEnvelopeGridBrush";

    /// <summary>Heavier than <see cref="GridKey"/>: the octave lines.</summary>
    private const string OctaveKey = "SnEnvelopeAxisBrush";

    /// <summary>Secondary text: the note names on the key axis and each part's tone name.</summary>
    private const string MutedTextKey = "SnMutedTextBrush";

    /// <summary>A part's range: the body fill, its outline, and the colour its fades taper towards.</summary>
    private const string ZoneKey = "LayerMapZoneBrush";

    /// <summary>The part number written in each lane.</summary>
    private const string LabelKey = "LayerMapLabelBrush";

    /// <summary>The outline around the selected part's lane — the same colour a selected card gets.</summary>
    private const string SelectionKey = "SnCardSelectedBorderBrush";

    // ---- Fixed measurements ------------------------------------------------------------------------------

    /// <summary>The strip along the bottom that holds the note names, below the lanes. Reserved rather than
    /// written over the last lane, which is what the four-lane WMT map does: at sixteen lanes the bottom row is
    /// as much real content as the other fifteen, and putting "C-1 C0 C1 …" through part 16's zone would make
    /// one part permanently harder to read than the rest.</summary>
    private const double AxisHeight = 16;

    /// <summary>Note 60, middle C.</summary>
    private const int MiddleC = 60;

    /// <summary>How solid a zone's fill is. The same 0.22 the PMT zone editor uses, so a range means the same
    /// thing to the eye on both charts. The outline carries the edge; the fill only has to say "this area
    /// belongs to this part" without hiding the lane's stripe or a neighbouring fade beneath it.</summary>
    private const double ZoneFillOpacity = 0.22;

    private const double ZoneStrokeThickness = 1.5;
    private const double SelectionThickness = 1.5;
    private const double OctaveAnchorThickness = 1.5;
    private const double LabelFontSize = 11;
    private const double AxisFontSize = 9;

    /// <summary>Breathing space between a label and whatever it sits against.</summary>
    private const double LabelPad = 4;

    /// <summary>How much of the chart's width a tone name may occupy before it is trimmed. The tone name is
    /// context; the ranges are the content, and a patch called "Ac.Piano 1 w/Strings PRO" must not paint over
    /// the part of the chart the user came here to read.</summary>
    private const double ToneNameWidthFraction = 0.25;

    // ---- Properties --------------------------------------------------------------------------------------

    /// <summary>The zones to draw, one snapshot per part, in any order. Null draws an empty chart, which is
    /// what a view bound before its view model exists should show.</summary>
    public static readonly StyledProperty<IReadOnlyList<LayerZone>?> ZonesProperty =
        AvaloniaProperty.Register<LayerMapControl, IReadOnlyList<LayerZone>?>(nameof(Zones));

    /// <summary>The selected part, zero-based, or -1 for none. Two-way by default because the control is what
    /// discovers the selection — a press lands in a lane and that lane is the selection — so the value has to
    /// travel back out to whatever is showing the selected part's numbers.</summary>
    public static readonly StyledProperty<int> SelectedPartProperty =
        AvaloniaProperty.Register<LayerMapControl, int>(nameof(SelectedPart), -1,
            defaultBindingMode: BindingMode.TwoWay);

    public IReadOnlyList<LayerZone>? Zones
    {
        get => GetValue(ZonesProperty);
        set => SetValue(ZonesProperty, value);
    }

    public int SelectedPart
    {
        get => GetValue(SelectedPartProperty);
        set => SetValue(SelectedPartProperty, value);
    }

    static LayerMapControl()
    {
        // A new list (the view model rebuilds it rather than mutating it, precisely so this works) or a new
        // selection redraws the chart.
        AffectsRender<LayerMapControl>(ZonesProperty, SelectedPartProperty);

        // The lane height comes from the geometry and not from a number chosen here, so this control and the
        // view that hosts it cannot disagree about how tall a legible lane is. The axis strip is added on top
        // of it rather than taken out of it: the lanes tile only the area above the strip, so a control exactly
        // LayerMapGeometry.MinHeight tall would give each lane a pixel less than MinLaneHeight.
        MinHeightProperty.OverrideDefaultValue<LayerMapControl>(LayerMapGeometry.MinHeight + AxisHeight);
    }

    /// <summary>The height the sixteen lanes tile — everything above the note-name strip. Every call into
    /// <see cref="LayerMapGeometry"/> is passed this and never <c>Bounds.Height</c>, or a zone would be drawn a
    /// lane's-worth of a strip lower than the lane it belongs to. Pointer handling added later must use the
    /// same value for the same reason.</summary>
    private double ChartHeight => Math.Max(0, Bounds.Height - AxisHeight);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height, chartH = ChartHeight;
        var palette = ResolvePalette();

        context.FillRectangle(palette.Background, new Rect(0, 0, w, h));
        if (w <= 0 || chartH <= 0) return; // mid-layout, or squeezed to nothing: there is nothing to place yet

        var culture = CultureInfo.CurrentCulture;

        DrawLanes(context, palette, w, chartH);
        DrawKeyAxis(context, palette, w, chartH, culture);

        var zones = Zones;
        if (zones is not null)
            foreach (var zone in zones)
            {
                // A snapshot for a part that has no lane cannot be drawn anywhere sensible. Skipping beats
                // asking the geometry for lane 17 and getting a rectangle off the bottom of the chart.
                if (zone.PartNo < 0 || zone.PartNo >= LayerMapGeometry.Lanes) continue;
                DrawZone(context, palette, zone, w, chartH);
                DrawLaneLabel(context, palette, zone, w, chartH, culture);
            }

        DrawSelection(context, palette, w, chartH);
    }

    /// <summary>Lane grounds: an alternating tint and a hairline under each row.</summary>
    private static void DrawLanes(DrawingContext context, in Palette palette, double w, double chartH)
    {
        var gridPen = new Pen(palette.Grid);

        for (var part = 0; part < LayerMapGeometry.Lanes; part++)
        {
            var lane = LayerMapGeometry.LaneRect(part, w, chartH);

            // Sixteen identical rows cannot be counted. The stripe is deliberately faint — it has to let the
            // eye find part 11 without competing with the zones drawn on top of it.
            if (part % 2 == 1) context.FillRectangle(palette.LaneAlt, ToRect(lane));

            // Under every lane including the last, whose hairline doubles as the top of the note-name strip.
            // Without it a zone that fills its lane runs into its neighbour and the two read as one block.
            context.DrawLine(gridPen, new Point(0, lane.Y + lane.H), new Point(w, lane.Y + lane.H));
        }
    }

    /// <summary>The key axis: a line at every C, its note name in the strip beneath.</summary>
    private static void DrawKeyAxis(DrawingContext context, in Palette palette, double w, double chartH,
        CultureInfo culture)
    {
        var octavePen = new Pen(palette.Octave);
        var anchorPen = new Pen(palette.Octave, OctaveAnchorThickness);
        var labelRight = double.NegativeInfinity;

        for (var key = PmtZoneMapping.Min; key <= PmtZoneMapping.Max; key++)
        {
            // Asked of MidiNote rather than stepped in twelves, so the one definition of "a C" is used here
            // too and the axis cannot disagree with the note names printed under it.
            if (!MidiNote.IsC(key)) continue;

            var x = PmtZoneMapping.KeyToX(key, w);

            // Every C *is* an octave boundary, so "heavier at the octaves" can only mean heavier than the other
            // lines the chart draws — the lane hairlines — which is what the axis brush over the grid brush
            // gives. Middle C is heavier again: it is the landmark a musician reads a split against ("the split
            // is just below C4"), and with eleven identical octave lines there is otherwise nothing to count
            // from.
            context.DrawLine(key == MiddleC ? anchorPen : octavePen, new Point(x, 0), new Point(x, chartH));

            var name = new FormattedText(MidiNote.Name(key), culture, FlowDirection.LeftToRight, Typeface.Default,
                AxisFontSize, palette.MutedText);

            // On a narrow chart the octaves come closer together than their names are wide. Dropping a name
            // that would collide with the one before it leaves a sparser but readable axis, where drawing them
            // all leaves an unreadable smear.
            if (x + LabelPad < labelRight) continue;
            context.DrawText(name, new Point(x + LabelPad, chartH + (AxisHeight - name.Height) / 2));
            labelRight = x + LabelPad + name.Width;
        }
    }

    /// <summary>One part's range: its fades, its body, its outline.</summary>
    private static void DrawZone(DrawingContext context, in Palette palette, LayerZone zone, double w,
        double chartH)
    {
        var body = ToRect(LayerMapGeometry.ZoneRect(zone, w, chartH));

        // The four fades and the body share one opacity push, so where a gradient reaches full strength it is
        // exactly the body's fill. Fade them separately and the seam shows, which turns a taper into a step and
        // defeats the point of drawing fades at all.
        using (context.PushOpacity(ZoneFillOpacity))
        {
            // Each band lies *outside* the range — the lower key band is below KeyLo, the lower velocity band
            // below VelLo, which in a lane means below the box because loud is up. All four rects are already
            // clipped to the chart and to the lane by the geometry (a twelve-semitone fade on a zone starting
            // at key 3 is a three-semitone band), so nothing here clamps, measures or second-guesses them: the
            // gradient simply runs from transparent at the far end of the band to the body's own fill where the
            // band meets the body.
            //
            // This is the whole value of the chart. Two parts that crossfade across a break have to *look* like
            // they overlap; drawn as hard rectangles they would look like a split, and the user would go
            // looking for the crossfade they had already dialled in.
            DrawFade(context, LayerMapGeometry.KeyFadeLowerRect(zone, w, chartH), palette.ZoneColor,
                new Point(0, 0), new Point(1, 0));
            DrawFade(context, LayerMapGeometry.KeyFadeUpperRect(zone, w, chartH), palette.ZoneColor,
                new Point(1, 0), new Point(0, 0));
            DrawFade(context, LayerMapGeometry.VelFadeLowerRect(zone, w, chartH), palette.ZoneColor,
                new Point(0, 1), new Point(0, 0));
            DrawFade(context, LayerMapGeometry.VelFadeUpperRect(zone, w, chartH), palette.ZoneColor,
                new Point(0, 0), new Point(0, 1));

            context.FillRectangle(palette.Zone, body);
        }

        // Full strength, and outside the opacity push: the edge is what a reader measures a split against, and
        // what a later pointer handler will let them grab. A zone one key wide has no width to fill, and this
        // stroke is then the only thing that draws it at all.
        context.DrawRectangle(null, new Pen(palette.Zone, ZoneStrokeThickness), body);
    }

    /// <summary>One fade band, as a gradient across the rectangle the geometry gave for it.</summary>
    /// <param name="transparentEnd">Relative corner the band fades from — the end away from the range.</param>
    /// <param name="solidEnd">Relative corner where the band meets the range and matches its fill.</param>
    private static void DrawFade(DrawingContext context, PmtZoneMapping.Rect band, Color color,
        Point transparentEnd, Point solidEnd)
    {
        // A fade width of zero — the common case — is a band of no extent. Nothing to draw, and no gradient
        // whose direction would be undefined.
        if (band.W <= 0 || band.H <= 0) return;

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(transparentEnd, RelativeUnit.Relative),
            EndPoint = new RelativePoint(solidEnd, RelativeUnit.Relative),
            GradientStops =
            {
                // The zone's own colour at zero alpha, not a named transparent: a gradient towards a colourless
                // stop washes through grey on the way, and the band would read as a shadow instead of as the
                // part getting quieter.
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0),
                new GradientStop(color, 1)
            }
        };

        context.FillRectangle(brush, ToRect(band));
    }

    /// <summary>The part number and its tone name, at the left of the part's lane.</summary>
    private static void DrawLaneLabel(DrawingContext context, in Palette palette, LayerZone zone, double w,
        double chartH, CultureInfo culture)
    {
        var lane = LayerMapGeometry.LaneRect(zone.PartNo, w, chartH);

        var label = new FormattedText(zone.Label ?? "", culture, FlowDirection.LeftToRight, Typeface.Default,
            LabelFontSize, palette.Label);

        // A lane shorter than a line of text has no room for one, and a clipped half-line of digits is worse
        // than none. The geometry's MinLaneHeight is set so this does not happen at the control's minimum size;
        // the guard is for a host that overrides MinHeight anyway.
        if (label.Height > lane.H) return;

        // Vertically centred in the lane, and at the lane's left rather than at the zone's left edge: the
        // sixteen part numbers then line up in one column the eye can run down, even when a part's range is a
        // narrow split somewhere in the middle of the chart. Drawn after the zone, so it stays legible over
        // whatever fill ended up beneath it.
        var y = lane.Y + (lane.H - label.Height) / 2;
        context.DrawText(label, new Point(LabelPad, y));

        if (string.IsNullOrWhiteSpace(zone.ToneName)) return;

        var tone = new FormattedText(zone.ToneName, culture, FlowDirection.LeftToRight, Typeface.Default,
            LabelFontSize, palette.MutedText)
        {
            MaxTextWidth = w * ToneNameWidthFraction,
            Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(tone, new Point(LabelPad + label.Width + LabelPad, y));
    }

    /// <summary>The outline that says which part is selected.</summary>
    private void DrawSelection(DrawingContext context, in Palette palette, double w, double chartH)
    {
        var selected = SelectedPart;
        if (selected < 0 || selected >= LayerMapGeometry.Lanes) return;

        var lane = ToRect(LayerMapGeometry.LaneRect(selected, w, chartH));

        // The whole lane, not the zone: a part selected while its range is a sliver — or a single key, which
        // has no width at all — still has to show that it is the one whose numbers are on display. Deflated by
        // half the pen so the outline lands inside its own lane instead of straddling the neighbour's.
        context.DrawRectangle(null, new Pen(palette.Selection, SelectionThickness),
            lane.Deflate(SelectionThickness / 2));
    }

    // ---- Resources ---------------------------------------------------------------------------------------

    /// <summary>Every brush the chart draws with, resolved once per render.</summary>
    private readonly record struct Palette(
        IBrush Background, IBrush LaneAlt, IBrush Grid, IBrush Octave, IBrush MutedText,
        IBrush Zone, Color ZoneColor, IBrush Label, IBrush Selection);

    private Palette ResolvePalette()
    {
        var zone = FindBrush(ZoneKey);
        return new Palette(
            FindBrush(BackgroundKey), FindBrush(LaneAltKey), FindBrush(GridKey), FindBrush(OctaveKey),
            FindBrush(MutedTextKey), zone, ColorOf(zone), FindBrush(LabelKey), FindBrush(SelectionKey));
    }

    /// <summary>A brush from the resources, by key — resolved through the control itself, so it finds
    /// App.axaml's palette the way any <c>{StaticResource}</c> in XAML would and a host may still override one
    /// key locally. <c>DataTemplateProvider</c> reaches <c>KnobFxBrush</c> for the same reason.
    ///
    /// <para>A key that resolves to nothing paints nothing rather than throwing: a mistyped key should cost an
    /// invisible line, not a designer preview that will not open.</para></summary>
    private IBrush FindBrush(string key)
        => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Transparent;

    /// <summary>The colour behind a solid brush. A gradient is built from colours and not from brushes, so the
    /// fades need this; anything that is not a single colour has none to fade from and fades from nothing.
    /// </summary>
    private static Color ColorOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>The geometry speaks in its own rectangle so it can stay free of Avalonia and testable. This is
    /// the bridge, and deliberately the only place the two rectangle types meet.</summary>
    private static Rect ToRect(PmtZoneMapping.Rect r) => new(r.X, r.Y, r.W, r.H);
}
