using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
/// <para>The pointer handling follows the same rule: a press is turned into a
/// <see cref="LayerMapGeometry.HitTest"/>, a move into a <see cref="LayerMapGeometry.ResolveDrag"/>, and the
/// result into one of three events. The control decides *what kind* of thing happened — a question, an edit, a
/// navigation — and the geometry decides what the values become. There is no <c>ToolTip</c> here and there must
/// never be one: a tooltip is a popup, it swallows clicks on the control it describes, and this chart is nothing
/// but click targets. See the status-bar comment in <c>MainWindow.axaml</c> for what that cost the last
/// time.</para></summary>
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

    /// <summary>Darkens the column behind every black key. Semi-transparent black rather than an opaque
    /// colour, so it darkens whatever it is over — the chart ground, and the lane stripe drawn after it —
    /// instead of having to be two colours that stay in step with both.</summary>
    private const string BlackKeyKey = "LayerMapBlackKeyBrush";

    /// <summary>Lifts the column behind every white key. The other half of the keyboard pattern, and on this
    /// ground the half that does most of the work: see <see cref="DrawKeyColumns"/>.</summary>
    private const string WhiteKeyKey = "LayerMapWhiteKeyBrush";

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
    //
    // The note-name strip's height is *not* here. It is LayerMapGeometry.AxisHeight, along with the reasoning
    // for reserving a strip at all, because the drawing below and the pointer handling further down both have to
    // subtract the identical value from Bounds.Height and a control-local copy is one edit away from disagreeing
    // with the geometry the drags are resolved in.

    /// <summary>Note 60, middle C.</summary>
    private const int MiddleC = 60;

    // The three cursors a hover can produce, built once each and not per pointer move -- a move arrives for
    // every pixel, and a Cursor owns a platform handle, so building them on the fly would allocate and free
    // handles by the thousand for a value that only ever takes three states. RotaryKnobDial builds its one
    // cursor in its constructor for the same reason; what differs here is that which one applies depends on
    // where the pointer is, so they cannot be a single field set once.
    //
    // Lazy, and deliberately not `static readonly Cursor = new(...)`: constructing a Cursor asks the platform
    // for a handle through ICursorFactory, so a plain static initialiser runs that the first time *anything*
    // touches this type -- including a unit test reading a styled property's default, where no platform is
    // registered and the whole type then fails to initialise. Deferring to first use means they are built when
    // a pointer is actually over the control, by which time there is certainly a platform. A test caught this;
    // the symptom was a TypeInitializationException wrapping "Unable to locate 'ICursorFactory'".

    /// <summary>Over a key edge: the drag moves the range's lower or upper key, left and right.</summary>
    private static readonly Lazy<Cursor> ResizeKeyCursor = new(() => new Cursor(StandardCursorType.SizeWestEast));

    /// <summary>Over a velocity edge: the drag moves the range's loud or soft limit, up and down within the
    /// lane.</summary>
    private static readonly Lazy<Cursor> ResizeVelocityCursor =
        new(() => new Cursor(StandardCursorType.SizeNorthSouth));

    /// <summary>Over a zone's body: the drag moves the whole range in both axes at once.</summary>
    private static readonly Lazy<Cursor> MoveCursor = new(() => new Cursor(StandardCursorType.SizeAll));

    /// <summary>How solid a zone's fill is. The same 0.22 the PMT zone editor uses, so a range means the same
    /// thing to the eye on both charts. The outline carries the edge; the fill only has to say "this area
    /// belongs to this part" without hiding the lane's stripe or a neighbouring fade beneath it.</summary>
    private const double ZoneFillOpacity = 0.22;

    /// <summary>How much of the body's fill a fade band still has at its far end, where the part is only just
    /// starting to be heard. Not zero, which is what it was: on a ground this dark a translucent blue tapering
    /// to nothing disappears well before the band does, so a crossfade looked like the hard split the band
    /// exists to disprove. Half keeps the whole band present while still reading as quieter than the body —
    /// the taper is the signal, and a floor does not remove it, it only stops it running out of contrast.
    /// </summary>
    private const double FadeFloorFraction = 0.5;

    /// <summary>The dashed line marking where a fade begins. Thinner than the body's outline and dashed
    /// against its solid, so the two edges of a crossfade are told apart at a glance: dashed is where the part
    /// starts to be heard, solid is where it reaches full level.</summary>
    private const double FadeEdgeThickness = 1;

    private static readonly DashStyle FadeEdgeDashes = new([3, 3], 0);

    private const double ZoneStrokeThickness = 1.5;
    private const double SelectionThickness = 1.5;
    private const double OctaveAnchorThickness = 1.5;
    private const double LabelFontSize = 11;
    private const double AxisFontSize = 9;

    /// <summary>Breathing space between a label and whatever it sits against.</summary>
    private const double LabelPad = 4;

    /// <summary>Below this many pixels per key, the per-note gridlines are dropped and only the octaves are
    /// drawn. Four is where 128 hairlines stop being a grid and start being a tint over the whole chart —
    /// which would sit *under* the zones and make the thing the user is actually reading harder to see. The
    /// chart is 128 keys wide whatever its size, so this is a statement about the window, not the data: it
    /// bites below roughly 500px of chart.</summary>
    private const double MinKeyWidthForAllNotes = 4;

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

    // ---- Events ------------------------------------------------------------------------------------------
    //
    // Three events, rather than commands or a reference to a view model, for the same reason the control takes
    // snapshots in: it is handed values and hands back what happened to them, and knows nothing about who is on
    // the other end. That is what lets the view model own the live parameter wrappers, the throttled writer and
    // the undo journal without any of it leaking into a file no test can reach.

    /// <summary>A zone was dragged. Carries the whole new zone rather than a delta or a single edge, so the
    /// handler writes values and does no arithmetic: every rule about what a drag *means* — an edge blocking at
    /// its opposite instead of inverting the zone, a body move keeping its span at the chart edge — has already
    /// been applied by <see cref="LayerMapGeometry.ResolveDrag"/>, where a test can see it.
    ///
    /// <para>Raised repeatedly through a drag, once for each pointer move that resolves to values different from
    /// the ones already sent. The handler is expected to write only the fields that actually changed, so a key
    /// drag does not also rewrite an untouched velocity range.</para></summary>
    public event EventHandler<LayerZoneEditedEventArgs>? ZoneEdited;

    /// <summary>A zone was double-clicked: show that part's own tab. The chart says which parts overlap where;
    /// changing anything but the four ranges happens on the part's own page, and this is the way there.</summary>
    public event EventHandler<LayerPartEventArgs>? ZoneActivated;

    /// <summary>A point in a lane was pressed: sound that part at that key and that velocity.
    ///
    /// <para>Raised for a press anywhere in a lane, <b>including outside the part's own zone</b>. A part that
    /// does not answer the key or the velocity under the pointer stays silent, and that silence is the chart
    /// answering "no, not here" — it is the map working, not a dropped note.</para></summary>
    public event EventHandler<LayerAuditionEventArgs>? AuditionRequested;

    // ---- Drag state --------------------------------------------------------------------------------------
    //
    // PmtZoneEditorControl's _dragZone / _dragHandle / _dragOrigPos triple, in parameter space instead of pixels.
    // Values and not pixels on purpose: the geometry speaks in keys and velocity steps, and it is the only layer
    // of this feature a test can reach, so a drag remembered as pixels would put the pixels-to-values conversion
    // here, in the file nothing can check. It also makes a drag survive a resize mid-gesture — a remembered key
    // still means the same key when the chart is narrower, where a remembered X means a different one.

    /// <summary>The part whose zone is being dragged, or -1 when no drag is in progress. Every velocity question
    /// asked during the drag is asked about <i>this</i> part, never about the lane under the pointer.</summary>
    private int _dragPart = -1;

    /// <summary>Which part of the zone was grabbed, and so what the movement will mean.</summary>
    private PmtZoneMapping.Handle _dragHandle;

    /// <summary>The zone as it was when the pointer went down. Every move resolves from this rather than from the
    /// previous move, so the drag cannot accumulate rounding drift and bringing the pointer back to where it
    /// started restores exactly the values that were there.</summary>
    private LayerZone _dragOrigin;

    /// <summary>Where the press landed, in keys and in velocity steps: the reference a <c>Body</c> drag measures
    /// its shift from. Quantised once, at press, so a slow drag across a single key accumulates rather than
    /// rounding to nothing on every move.</summary>
    private int _dragKeyAtPress, _dragVelAtPress;

    /// <summary>The last zone handed to <see cref="ZoneEdited"/>. A pointer moved a few pixels within one key and
    /// one velocity step resolves to the values already sent; re-raising them would ask the view model to write
    /// what it has just written, dozens of times a second, for a drag that has not yet crossed a boundary.
    /// Correctness does not depend on this — the handler ignores unchanged fields anyway — but the throttle and
    /// the journal have better things to do.</summary>
    private LayerZone _dragLast;

    /// <summary>One drag is one undo step, however slowly it is dragged and however many of the four values it
    /// moves. A <see cref="PointerGesture"/> rather than an <see cref="EditGesture"/> directly, because the step
    /// also has to close when the drag is <i>interrupted</i> — the window losing activation with the button still
    /// down — and the <c>PointerCaptureLost</c> event that says so is Direct, so it reaches only the element that
    /// held the capture. That class already knows this; the last capture-lost handler written from scratch in this
    /// repository hung it where a Direct event never arrives, and a leaked gesture is not confined to the control
    /// that leaked it — the depth counter lives on the ambient journal, so every later edit anywhere in the
    /// application would fold into that one step.</summary>
    private readonly PointerGesture _gesture = new();

    static LayerMapControl()
    {
        // A new list (the view model rebuilds it rather than mutating it, precisely so this works) or a new
        // selection redraws the chart.
        AffectsRender<LayerMapControl>(ZonesProperty, SelectedPartProperty);

        // Taken from the geometry whole, and not added to: LayerMapGeometry.MinHeight is a *total* — sixteen
        // legible lanes and the note-name strip — so a control exactly this tall gets full-height lanes with the
        // strip already paid for. Adding AxisHeight here would reserve the strip twice over, which costs sixteen
        // pixels of dead space at the bottom of the chart and shows up as nothing at all: no build error, no
        // failing test, just a chart that is slightly too tall for its content. The number to hand over is the
        // number, and the reason it is a total lives in MinHeight's own comment.
        MinHeightProperty.OverrideDefaultValue<LayerMapControl>(LayerMapGeometry.MinHeight);
    }

    /// <summary>The height the sixteen lanes tile — everything above the note-name strip. Every call into
    /// <see cref="LayerMapGeometry"/> is passed this and never <c>Bounds.Height</c>, or a zone would be drawn a
    /// lane's-worth of a strip lower than the lane it belongs to. The pointer handling below uses the same value
    /// for the same reason: a press hit-tested against a different height than the zones were drawn with lands
    /// on the wrong lane near the bottom of the chart, and near-misses everywhere else.</summary>
    private double ChartHeight => LayerMapGeometry.LaneAreaHeight(Bounds.Height);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height, chartH = ChartHeight;
        var palette = ResolvePalette();

        context.FillRectangle(palette.Background, new Rect(0, 0, w, h));
        if (w <= 0 || chartH <= 0) return; // mid-layout, or squeezed to nothing: there is nothing to place yet

        var culture = CultureInfo.CurrentCulture;

        // Before the lanes, so the lane stripe reads as a stripe over the keyboard rather than the keyboard
        // interrupting it, and well before the zones, which are what the eye is meant to end up on.
        DrawKeyColumns(context, palette, w, chartH);
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

    /// <summary>A tinted column behind every key, so the chart reads as a keyboard laid on its side and a key
    /// can be found by its two-and-three pattern rather than by counting from a C.
    ///
    /// <para>A column is a whole <see cref="LayerMapGeometry.KeyCell"/>, so the tint and the gridlines coincide
    /// by construction rather than by two pieces of arithmetic agreeing. Centred on the key's *position*
    /// instead — which is what this did first — every column sat half a key off the lines drawn between the
    /// keys, and the whole axis read as shifted.</para>
    ///
    /// <para>Both directions, not only the black keys. The chart's ground is nearly black, so darkening has
    /// almost no headroom left in it: a semi-transparent black over <c>#1B1F22</c> moves it a handful of levels
    /// and disappears. The white keys are lifted as well as the black ones pressed down, and the pattern comes
    /// from the distance between the two rather than from either alone.</para>
    ///
    /// <para>Unlike the per-note gridlines this never drops out on a narrow chart. A hairline at two pixels a
    /// key becomes an even wash that hides what is drawn over it; a fill at two pixels a key is still the
    /// two-and-three grouping, which is the whole of what it is for.</para></summary>
    private static void DrawKeyColumns(DrawingContext context, in Palette palette, double w, double chartH)
    {
        for (var key = PmtZoneMapping.Min; key <= PmtZoneMapping.Max; key++)
        {
            var cell = LayerMapGeometry.KeyCell(key, w);
            if (cell.W <= 0) continue;

            // Asked of MidiNote, like the C test in the axis, so one definition of the keyboard serves the
            // tint, the gridlines and the note names and none of the three can disagree with the others.
            context.FillRectangle(MidiNote.IsBlack(key) ? palette.BlackKey : palette.WhiteKey,
                new Rect(cell.X, 0, cell.W, chartH));
        }
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

    /// <summary>The key axis: a line at every note, heavier at every C, heaviest at middle C, and the C names
    /// in the strip beneath.
    ///
    /// <para>Every note gets a line so a zone edge can be read as the key it is rather than as a position
    /// between two octaves — the question this chart exists to answer is usually "where exactly does part 3
    /// stop", and counting semitones off a C twelve keys away is not answering it.</para>
    ///
    /// <para>The per-note lines are dropped below <see cref="MinKeyWidthForAllNotes"/> pixels a key. At 128
    /// keys across the chart they are ~10px apart on a maximised window and legible; on a narrow one they
    /// close to a grey wash that hides the zones drawn over them, which is worse than not having them. The
    /// octave lines never drop, so the axis degrades to what it was rather than to nothing.</para></summary>
    private static void DrawKeyAxis(DrawingContext context, in Palette palette, double w, double chartH,
        CultureInfo culture)
    {
        var octavePen = new Pen(palette.Octave);
        var anchorPen = new Pen(palette.Octave, OctaveAnchorThickness);
        var semitonePen = new Pen(palette.Grid);
        var labelRight = double.NegativeInfinity;

        // One key's width, which is what decides whether 128 lines help or smear. The geometry maps 0..127
        // across the full width, so a key is that width over 127 steps.
        var keyWidth = w / (PmtZoneMapping.Max - PmtZoneMapping.Min);
        var allNotes = keyWidth >= MinKeyWidthForAllNotes;

        if (allNotes)
            for (var key = PmtZoneMapping.Min; key <= PmtZoneMapping.Max; key++)
            {
                // The C lines are drawn again below, heavier, so skipping them here keeps a C from being a
                // faint line with a strong one on top of it -- which at one pixel reads as neither.
                if (MidiNote.IsC(key)) continue;

                // The line goes *between* this key and the one below it, not through its middle: it is the
                // edge of a cell, which is the edge a zone is drawn to and the edge a drag snaps to.
                var kx = LayerMapGeometry.KeyBoundaryX(key, w);
                context.DrawLine(semitonePen, new Point(kx, 0), new Point(kx, chartH));
            }

        for (var key = PmtZoneMapping.Min; key <= PmtZoneMapping.Max; key++)
        {
            // Asked of MidiNote rather than stepped in twelves, so the one definition of "a C" is used here
            // too and the axis cannot disagree with the note names printed under it.
            if (!MidiNote.IsC(key)) continue;

            // C's *left* boundary, not C's position — the line marks where C begins, which is what a piano roll
            // draws and what makes the name printed just to its right read as labelling that key rather than
            // floating between two. Through the geometry rather than PmtZoneMapping even though one forwards to
            // the other: the axis this chart draws its gridlines on has to be the axis its presses are
            // hit-tested against, and the way to guarantee that is for both to go through the one class.
            var x = LayerMapGeometry.KeyBoundaryX(key, w);

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
            context.DrawText(name, new Point(x + LabelPad,
                chartH + (LayerMapGeometry.AxisHeight - name.Height) / 2));
            labelRight = x + LabelPad + name.Width;
        }
    }

    /// <summary>One part's range: its fades, its body, its outline.</summary>
    private static void DrawZone(DrawingContext context, in Palette palette, LayerZone zone, double w,
        double chartH)
    {
        var body = ToRect(LayerMapGeometry.ZoneRect(zone, w, chartH));

        // Each band lies *outside* the range — the lower key band is below KeyLo, the lower velocity band below
        // VelLo, which in a lane means below the box because loud is up. All four rects are already clipped to
        // the chart and to the lane by the geometry (a twelve-semitone fade on a zone starting at key 3 is a
        // three-semitone band), so nothing here clamps, measures or second-guesses them.
        //
        // This is the whole value of the chart. Two parts that crossfade across a break have to *look* like
        // they overlap; drawn as hard rectangles they would look like a split, and the user would go looking
        // for the crossfade they had already dialled in.
        //
        // Drawn outside the body's opacity push, each band carrying its own alphas, because the two ends want
        // different things. Where a band meets the body it must match the body exactly or the seam shows and a
        // taper reads as a step -- so its solid end is ZoneFillOpacity, the same value the push applies. Its
        // far end used to be zero alpha, which was the mistake: over a ground this dark, a translucent blue
        // fading to nothing is invisible for most of its length, so a crossfade looked like the hard split it
        // was drawn to disprove. It now floors at FadeFloorFraction of the body, and the outer edge is marked
        // with a dashed line at full strength, so the *extent* of a fade is legible even where its gradient is
        // not: dashed for where the part starts to be heard, solid for where it reaches full level.
        DrawFade(context, LayerMapGeometry.KeyFadeLowerRect(zone, w, chartH), palette.ZoneColor,
            new Point(0, 0), new Point(1, 0));
        DrawFade(context, LayerMapGeometry.KeyFadeUpperRect(zone, w, chartH), palette.ZoneColor,
            new Point(1, 0), new Point(0, 0));
        DrawFade(context, LayerMapGeometry.VelFadeLowerRect(zone, w, chartH), palette.ZoneColor,
            new Point(0, 1), new Point(0, 0));
        DrawFade(context, LayerMapGeometry.VelFadeUpperRect(zone, w, chartH), palette.ZoneColor,
            new Point(0, 0), new Point(0, 1));

        using (context.PushOpacity(ZoneFillOpacity))
            context.FillRectangle(palette.Zone, body);

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

        // Both ends are the zone's own colour at an explicit alpha, never a named transparent: a gradient
        // towards a colourless stop washes through grey on the way, and the band would read as a shadow
        // instead of as the part getting quieter.
        var solid = (byte)Math.Round(byte.MaxValue * ZoneFillOpacity);
        var floor = (byte)Math.Round(byte.MaxValue * ZoneFillOpacity * FadeFloorFraction);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(transparentEnd, RelativeUnit.Relative),
            EndPoint = new RelativePoint(solidEnd, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(floor, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(solid, color.R, color.G, color.B), 1)
            }
        };

        context.FillRectangle(brush, ToRect(band));

        // The band's outer edge: where the part first starts to be heard. Dashed, against the body's solid
        // outline for where it reaches full level, so the two edges of a crossfade cannot be confused with one
        // another — and at full strength, because a gradient's own far end is by definition the faintest thing
        // on the chart and cannot mark its own boundary.
        var horizontal = Math.Abs(solidEnd.X - transparentEnd.X) > 0.5;
        var outerX = transparentEnd.X <= 0.5 ? band.X : band.X + band.W;
        var outerY = transparentEnd.Y <= 0.5 ? band.Y : band.Y + band.H;
        var from = horizontal ? new Point(outerX, band.Y) : new Point(band.X, outerY);
        var to = horizontal ? new Point(outerX, band.Y + band.H) : new Point(band.X + band.W, outerY);

        context.DrawLine(new Pen(new SolidColorBrush(color), FadeEdgeThickness, FadeEdgeDashes), from, to);
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

    // ---- Interaction -------------------------------------------------------------------------------------

    /// <summary>A press selects the part whose lane it landed in, and then does exactly one of three things:
    /// opens that part's tab (a double-click), starts a drag (an edge of the zone, or its body), or asks the part
    /// to sound the note under the pointer (anywhere else in the lane — and on the body too, since a press there
    /// is a question until the pointer moves).</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Left button only. A right or middle press has nothing to mean on this chart, and letting one open a
        // drag would hand an undo gesture to a button whose release this control has no promise of seeing. The
        // Motional Surround pucks check for the same reason.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A drag already in progress owns the pointer until it is released, and a further press must not restart
        // it. The check above does not cover this: IsLeftButtonPressed stays true for as long as the left button is
        // held, so pressing a second button mid-drag — a chord, or a stray touch — arrives here as a press that
        // looks entirely legitimate. Falling through would run _gesture.Begin again, which closes the open undo
        // step and opens a fresh one, so one continuous drag would land in the journal as two steps needing two
        // presses of Undo; and it would re-anchor _dragOrigin to the half-dragged values, so the rest of the
        // gesture would measure its movement from there and the zone would jump.
        if (_dragPart >= 0) return;

        var zones = Zones;
        double w = Bounds.Width, chartH = ChartHeight;
        if (zones is null || w <= 0 || chartH <= 0) return;

        var pos = e.GetPosition(this);
        var (hitPart, handle) = LayerMapGeometry.HitTest(pos.X, pos.Y, zones, w, chartH,
            LayerMapGeometry.HitMargin);

        // No lane means the note-name strip or off the chart altogether: nothing to select, nothing to sound and
        // above all no drag. A press on the axis must not edit part 16 merely because its lane is the one next
        // to it — which is why the geometry answers null there rather than clamping to the nearest lane.
        if (hitPart is not { } part) return;

        // A press in a lane is a selection, whatever else it turns out to be. The property is TwoWay, so this is
        // how the readout learns whose four numbers to show.
        SelectedPart = part;
        e.Handled = true;

        // The second press of a double-click. Navigating away is the whole of what it means: no note, because the
        // first click already sounded one and the tab is about to change under the user, and no drag, because the
        // pointer is about to be over a chart that is no longer on screen. Tested with >= rather than == so the
        // third press of a rapid triple-click — which reports 3 — does not fall through into a drag.
        if (e.ClickCount >= 2)
        {
            ZoneActivated?.Invoke(this, new LayerPartEventArgs(part));
            return;
        }

        // The press, in the units the geometry and the audition both speak. Read once and reused for both, so the
        // note the user hears and the reference a body drag is measured from cannot be a key apart. The velocity is
        // asked of the part whose lane was hit -- the same lane the box is drawn in, which is what makes a press
        // low in a lane soft and high in it loud.
        var keyAtPress = LayerMapGeometry.KeyAt(pos.X, w);
        var velAtPress = LayerMapGeometry.VelocityAt(part, pos.Y, chartH);

        // Anywhere in the lane except an edge. An edge press is a drag and not a question: sounding a note every
        // time a split is nudged would be maddening, and the note would be the *old* range's answer anyway.
        if (handle is PmtZoneMapping.Handle.None or PmtZoneMapping.Handle.Body)
            AuditionRequested?.Invoke(this, new LayerAuditionEventArgs(part, keyAtPress, velAtPress));

        // Handle.None is a press in the empty part of a lane — a question, and it has just been asked. There is
        // no edge and no body under the pointer, so there is nothing to drag.
        if (handle == PmtZoneMapping.Handle.None) return;

        // The hit named a handle, so the list does hold a zone for this part; the geometry could not have found
        // an edge otherwise. Belt and braces, because a null here would mean dragging a default-constructed zone
        // — part 1, keys 0..0 — over the top of whatever the user actually grabbed.
        if (ZoneOf(zones, part) is not { } origin) return;

        // Captured on the control itself, so the drag survives the pointer leaving it: a velocity edge dragged to
        // 127 leaves its lane at the top, and a key edge dragged to 0 leaves the chart at the left. Captured
        // *before* the gesture opens, because moving the capture makes whoever held it lose it — and when that is
        // already this control, a capture-lost handler attached any earlier would be woken by this press rather
        // than by the end of this drag.
        e.Pointer.Capture(this);
        _gesture.Begin(this, EndDrag);

        // After Begin and not before: Begin closes any gesture still held from an earlier press, and closing one
        // runs its EndDrag — which would clear the drag being set up here.
        _dragPart = part;
        _dragHandle = handle;
        _dragOrigin = origin;
        _dragLast = origin;
        _dragKeyAtPress = keyAtPress;
        _dragVelAtPress = velAtPress;
    }

    /// <summary>A lookup, a call and a raise. What the movement means — which of the four values moves, where it
    /// stops, what happens when it meets its opposite — is entirely
    /// <see cref="LayerMapGeometry.ResolveDrag"/>'s, and there is deliberately no arithmetic here to disagree
    /// with it.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // Not dragging: the move is only worth anything as a question about what is under the pointer, and the
        // answer is the cursor. A resize handle a few pixels wide is invisible otherwise -- the user finds it by
        // pressing and seeing what happens, which on a chart where a press also auditions is a poor way to ask.
        if (_dragPart < 0)
        {
            ShowHoverCursor(e.GetPosition(this));
            return;
        }

        double w = Bounds.Width, chartH = ChartHeight;
        if (w <= 0 || chartH <= 0) return;

        var pos = e.GetPosition(this);

        // VelocityAt is asked about the part the drag was *captured* on and never about the lane under the
        // pointer. It clamps a Y outside the lane it is given — 127 above, 0 below — which is exactly what a
        // velocity drag wants: pushed past the top of the lane, the edge pins at 127. Asking
        // VelocityAt(LaneAt(pos.Y)!, ...) instead would re-read the value in whichever neighbouring lane the
        // pointer strayed into and silently retarget the drag onto a part the user never touched.
        var keyNow = LayerMapGeometry.KeyAt(pos.X, w);
        var velNow = LayerMapGeometry.VelocityAt(_dragPart, pos.Y, chartH);

        var edited = LayerMapGeometry.ResolveDrag(_dragOrigin, _dragHandle, keyNow, velNow,
            _dragKeyAtPress, _dragVelAtPress);

        e.Handled = true;
        if (edited == _dragLast) return; // still the same key and velocity step: nothing new to say
        _dragLast = edited;

        // The handle travels with the values. Seven of the zone's eight numbers are the ones the drag started
        // from, not the ones the instrument holds now, so a handler that wrote every field that differs from live
        // would revert whatever a front-panel edit or a Studio Set change had altered mid-drag. The handle is what
        // tells it which single field this gesture is entitled to write; see LayerZoneChanges.FieldsFor.
        ZoneEdited?.Invoke(this, new LayerZoneEditedEventArgs(edited, _dragHandle));
    }

    /// <summary>Say what a press here would do, by changing the cursor: the two key edges resize left-right,
    /// the two velocity edges resize up-down, a zone's body moves in both, and everything else is a plain
    /// arrow.
    ///
    /// <para>Hover only. During a drag the cursor is left exactly as the press set it, because the pointer
    /// routinely leaves the zone it is dragging -- past the end of the key range, or into a neighbouring lane
    /// -- and a cursor that flickered to "arrow" halfway through a resize would say the drag had stopped when
    /// it had not.</para>
    ///
    /// <para>The margin this hit-tests with is <see cref="LayerMapGeometry.HitMargin"/>, the same one the press
    /// uses. That is the whole point: a cursor that changed over a different band than the one that actually
    /// grabs would be worse than no cursor at all, because the user would trust it.</para></summary>
    private void ShowHoverCursor(Point pos)
    {
        double w = Bounds.Width, chartH = ChartHeight;
        if (w <= 0 || chartH <= 0) return;

        var zones = Zones;
        var handle = zones is null
            ? PmtZoneMapping.Handle.None
            : LayerMapGeometry.HitTest(pos.X, pos.Y, zones, w, chartH, LayerMapGeometry.HitMargin).Handle;

        Cursor = handle switch
        {
            PmtZoneMapping.Handle.Left or PmtZoneMapping.Handle.Right => ResizeKeyCursor.Value,
            PmtZoneMapping.Handle.Top or PmtZoneMapping.Handle.Bottom => ResizeVelocityCursor.Value,
            PmtZoneMapping.Handle.Body => MoveCursor.Value,
            _ => Cursor.Default,
        };
    }

    /// <summary>The pointer left the chart, so nothing here is under it any more. Without this the control
    /// keeps whatever cursor the last hover set, and a pointer that leaves over a resize handle takes the
    /// resize cursor with it for as long as it is away.</summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragPart < 0) Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragPart < 0) return;

        // Releasing the capture reaches EndDrag through the gesture's own capture-lost handler; End() is
        // idempotent, so it does not matter which of the two arrives first, and the undo step closes exactly
        // once either way. There is no OnPointerCaptureLost override here on purpose — PointerGesture holds that
        // half, and a second hand-rolled copy of it is what went wrong the last time.
        e.Pointer.Capture(null);
        _gesture.End();
        e.Handled = true;
    }

    /// <summary>Forget the drag in progress. Reached from the release and — through the gesture — from a capture
    /// loss, because an interrupted drag has to clear this as well as a released one: left set, the next pointer
    /// move across the chart would go on resizing a zone with no button held.</summary>
    private void EndDrag()
    {
        _dragPart = -1;
        _dragHandle = PmtZoneMapping.Handle.None;
    }

    /// <summary>The snapshot a part contributed, or null if the list holds none for it. Sixteen entries walked
    /// once per press; the list arrives in any order, so there is no index to shortcut to.</summary>
    private static LayerZone? ZoneOf(IReadOnlyList<LayerZone> zones, int part)
    {
        foreach (var zone in zones)
            if (zone.PartNo == part)
                return zone;

        return null;
    }

    // ---- Resources ---------------------------------------------------------------------------------------

    /// <summary>Every brush the chart draws with, resolved once per render.</summary>
    private readonly record struct Palette(
        IBrush Background, IBrush LaneAlt, IBrush BlackKey, IBrush WhiteKey, IBrush Grid, IBrush Octave,
        IBrush MutedText, IBrush Zone, Color ZoneColor, IBrush Label, IBrush Selection);

    private Palette ResolvePalette()
    {
        var zone = FindBrush(ZoneKey);
        return new Palette(
            FindBrush(BackgroundKey), FindBrush(LaneAltKey), FindBrush(BlackKeyKey), FindBrush(WhiteKeyKey),
            FindBrush(GridKey), FindBrush(OctaveKey), FindBrush(MutedTextKey), zone, ColorOf(zone),
            FindBrush(LabelKey), FindBrush(SelectionKey));
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

/// <summary>What a drag resolved to: the zone as it should now be, whole, together with the handle that says which
/// of its values the drag is actually about. Carrying the whole zone and not a delta or a changed edge is what
/// keeps the arithmetic in <see cref="LayerMapGeometry.ResolveDrag"/>, where a test can see it.
///
/// <para><b>The whole zone is not a licence to write the whole zone.</b> The seven values the drag is not moving
/// are the ones it started from, so a handler that wrote every value differing from the instrument's current state
/// would revert anything that changed under the drag — which is why <see cref="Handle"/> is here and why it must
/// be used. This comment used to say that comparing the values and writing the differences was "a task with no
/// rules in it and so nothing to get wrong twice"; it had exactly one rule in it, and that was the bug.</para>
/// </summary>
public sealed class LayerZoneEditedEventArgs(LayerZone zone, PmtZoneMapping.Handle handle) : EventArgs
{
    /// <summary>The values as they should now be, <see cref="LayerZone.PartNo"/> included — the drag was captured
    /// on one part and stays on it however far the pointer wanders, so the part travels with the values rather
    /// than being re-derived at the other end.</summary>
    public LayerZone Zone { get; } = zone;

    /// <summary>Which part of the zone is being dragged, and therefore <b>which of the eight values this event is
    /// about</b>. A handler must not write a field this handle does not own.
    ///
    /// <para>The reason is that <see cref="Zone"/> is <c>origin with { …the dragged field… }</c> — the drag
    /// resolves from the zone as it was at press, so that a slow gesture cannot accumulate drift. The other seven
    /// numbers are therefore press-time values, and they go stale as soon as anything else changes the part: the
    /// instrument's front panel, or a Studio Set change, which resyncs all sixteen parts at once. A handler that
    /// wrote every field differing from the live value would then push a press-time number back over what the
    /// device had just reported, for a field the user never touched.
    /// <see cref="LayerZoneChanges.FieldsFor"/> is the mask that prevents it, and is where that scenario is
    /// written out in full.</para></summary>
    public PmtZoneMapping.Handle Handle { get; } = handle;

    /// <summary>Zero-based, and the same number as <c>Zone.PartNo</c>. Here so a handler that only needs to know
    /// whose values these are does not have to reach through the snapshot for it.</summary>
    public int PartNo => Zone.PartNo;
}

/// <summary>Which part a gesture was about, when that is all there is to say.</summary>
public sealed class LayerPartEventArgs(int partNo) : EventArgs
{
    /// <summary>Zero-based, like <see cref="LayerZone.PartNo"/> and unlike the part numbers the user reads on the
    /// chart. Every part index crossing this boundary is zero-based; the "+ 1" belongs at the display end, where
    /// the label is built.</summary>
    public int PartNo { get; } = partNo;
}

/// <summary>A note to sound so the user can hear whether a part answers where they pressed.
///
/// The velocity is the pointer's height within the lane — the same mapping that draws the box — so pressing low
/// in a lane plays soft and high plays loud. Both numbers are what was pressed and not what the part accepts: a
/// press outside the part's range is passed on unchanged, the part ignores it, and the silence is the answer.
/// </summary>
public sealed class LayerAuditionEventArgs(int partNo, int note, int velocity) : EventArgs
{
    /// <summary>Zero-based.</summary>
    public int PartNo { get; } = partNo;

    /// <summary>MIDI note number, 0..127.</summary>
    public int Note { get; } = note;

    /// <summary>MIDI velocity, 0..127. Zero is possible — the very bottom of a lane — and a note-on at velocity
    /// zero is a note-off on the wire, so a handler that means to be heard has to say so.</summary>
    public int Velocity { get; } = velocity;
}
