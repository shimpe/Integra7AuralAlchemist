using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The layer map's readout. Not decoration: the readout is the only place the chart states a part's
/// values as numbers, so it is what the user checks the picture against — and what has to agree with the four
/// spin boxes on the part's own Set Part tab.</summary>
public class LayerMapFormattingTests
{
    /// <summary>Part 3, keys C4..C6, the top half of the velocity range, a twelve-semitone fade in at the bottom
    /// of the key range and nothing else.</summary>
    private static LayerZone Zone(int keyLo = 60, int keyHi = 84, int velLo = 64, int velHi = 127,
        int keyFadeLo = 12, int keyFadeHi = 0, int velFadeLo = 0, int velFadeHi = 0,
        int partNo = 2, string toneName = "Ac.Piano 1")
        => new(partNo, keyLo, keyHi, velLo, velHi, keyFadeLo, keyFadeHi, velFadeLo, velFadeHi,
            (partNo + 1).ToString(), toneName);

    [Test]
    public void A_key_is_both_named_and_numbered()
    {
        // The name is what a split is read against; the number is what the part's own tab shows, so the readout
        // gives both or the user cannot tell whether the two pages agree.
        Assert.That(LayerMapFormatting.Key(60), Is.EqualTo("C4 (60)"), "this application's middle C");
        Assert.That(LayerMapFormatting.Key(0), Is.EqualTo("C-1 (0)"), "the bottom of the range");
        Assert.That(LayerMapFormatting.Key(127), Is.EqualTo("G9 (127)"), "the top of it");
        // Clamped in both halves together, so the two can never describe different keys. No caller can produce
        // this -- every value comes from a 0..127 ParamInt -- but "G9 (200)" would be worse than either half.
        Assert.That(LayerMapFormatting.Key(200), Is.EqualTo("G9 (127)"));
    }

    [Test]
    public void A_key_range_reads_lower_first_even_when_the_two_are_inverted()
    {
        Assert.That(LayerMapFormatting.KeyRange(Zone()), Is.EqualTo("C4 (60) – C6 (84)"));

        // Deliberately not sorted. The geometry tolerates an inverted range because a rectangle has to be drawn
        // somewhere; the readout's job is the opposite one -- to agree with the raw Keyboard Range Lower and
        // Keyboard Range Upper on the part's own tab. Swapping them here would hide exactly the state the user
        // opened the readout to understand.
        Assert.That(LayerMapFormatting.KeyRange(Zone(keyLo: 84, keyHi: 60)),
            Is.EqualTo("C6 (84) – C4 (60)"));
    }

    [Test]
    public void Velocity_is_numbers_only()
    {
        // Velocity has no note names to give it, and the instrument shows these as numbers too.
        Assert.That(LayerMapFormatting.VelocityRange(Zone()), Is.EqualTo("64 – 127"));
        Assert.That(LayerMapFormatting.VelocityRange(Zone(velLo: 0, velHi: 127)), Is.EqualTo("0 – 127"));
    }

    [Test]
    public void Both_pairs_of_fade_widths_are_shown_lower_then_upper()
    {
        Assert.That(LayerMapFormatting.Fades(Zone()), Is.EqualTo("keys 12 / 0 · velocity 0 / 0"));
        Assert.That(LayerMapFormatting.Fades(Zone(keyFadeLo: 0, keyFadeHi: 4, velFadeLo: 8, velFadeHi: 16)),
            Is.EqualTo("keys 0 / 4 · velocity 8 / 16"));
    }

    [Test]
    public void The_title_names_the_part_one_based_and_drops_a_missing_tone_name_with_its_separator()
    {
        Assert.That(LayerMapFormatting.PartLabel(2), Is.EqualTo("Part 3"), "part numbers are one-based on screen");
        Assert.That(LayerMapFormatting.Title(Zone()), Is.EqualTo("Part 3 · Ac.Piano 1"));

        // An unresolved preset and an empty user-tone slot both legitimately have no name. The separator goes
        // with it rather than being left dangling after the part number.
        Assert.That(LayerMapFormatting.Title(Zone(toneName: "")), Is.EqualTo("Part 3"));
        Assert.That(LayerMapFormatting.Title(Zone(toneName: "   ")), Is.EqualTo("Part 3"),
            "the instrument pads its name fields with spaces");
        Assert.That(LayerMapFormatting.Title(Zone(toneName: "  Ac.Piano 1  ")), Is.EqualTo("Part 3 · Ac.Piano 1"));
    }
}

/// <summary>Which of a zone's eight values a drag actually moved. This is the checkable half of the layer map's
/// "write only what changed" rule: the chart raises the whole zone on every pointer move, so if this said
/// "everything" the map would spend a sysex round trip and an undo entry per unchanged parameter per move.</summary>
public class LayerZoneChangesTests
{
    private static LayerZone Zone(int keyLo = 60, int keyHi = 72, int velLo = 0, int velHi = 127,
        int keyFadeLo = 0, int keyFadeHi = 0, int velFadeLo = 0, int velFadeHi = 0)
        => new(1, keyLo, keyHi, velLo, velHi, keyFadeLo, keyFadeHi, velFadeLo, velFadeHi, "2", "");

    [Test]
    public void Two_identical_snapshots_are_nothing_to_write()
    {
        // The common case mid-drag: the pointer has moved several pixels without crossing into the next key.
        Assert.That(LayerZoneChanges.Between(Zone(), Zone()), Is.EqualTo(LayerZoneField.None));
    }

    [Test]
    public void Each_of_the_eight_is_reported_on_its_own()
    {
        var z = Zone();
        Assert.That(LayerZoneChanges.Between(z, Zone(keyLo: 48)), Is.EqualTo(LayerZoneField.KeyLo));
        Assert.That(LayerZoneChanges.Between(z, Zone(keyHi: 96)), Is.EqualTo(LayerZoneField.KeyHi));
        Assert.That(LayerZoneChanges.Between(z, Zone(velLo: 40)), Is.EqualTo(LayerZoneField.VelLo));
        Assert.That(LayerZoneChanges.Between(z, Zone(velHi: 100)), Is.EqualTo(LayerZoneField.VelHi));
        // The fades are compared too, although no drag moves them today: if fade dragging is ever added the
        // writing side is already correct rather than silently dropping the edits.
        Assert.That(LayerZoneChanges.Between(z, Zone(keyFadeLo: 12)), Is.EqualTo(LayerZoneField.KeyFadeLo));
        Assert.That(LayerZoneChanges.Between(z, Zone(keyFadeHi: 12)), Is.EqualTo(LayerZoneField.KeyFadeHi));
        Assert.That(LayerZoneChanges.Between(z, Zone(velFadeLo: 12)), Is.EqualTo(LayerZoneField.VelFadeLo));
        Assert.That(LayerZoneChanges.Between(z, Zone(velFadeHi: 12)), Is.EqualTo(LayerZoneField.VelFadeHi));
    }

    [Test]
    public void Several_at_once_are_all_reported()
    {
        var all = LayerZoneChanges.Between(Zone(),
            Zone(48, 96, 40, 100, 12, 12, 12, 12));
        Assert.That(all, Is.EqualTo(LayerZoneField.KeyLo | LayerZoneField.KeyHi | LayerZoneField.VelLo |
                                    LayerZoneField.VelHi | LayerZoneField.KeyFadeLo | LayerZoneField.KeyFadeHi |
                                    LayerZoneField.VelFadeLo | LayerZoneField.VelFadeHi));
    }

    [Test]
    public void Dragging_a_key_edge_writes_that_edge_and_nothing_else()
    {
        // Composed with the real drag resolution rather than with two hand-made snapshots, because the claim
        // worth pinning is about the pair: what the chart raises, and what the view model then writes. A key
        // drag must not rewrite the velocity range -- each unnecessary write is a round trip and an undo step.
        var origin = Zone();
        var edited = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left,
            keyNow: 48, velNow: 20, keyAtPress: 60, velAtPress: 64);

        // The same expression LayerZoneViewModel.Apply evaluates: what differs, confined to what the handle owns.
        Assert.That(LayerZoneChanges.Between(origin, edited) & LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Left),
            Is.EqualTo(LayerZoneField.KeyLo));
    }

    [Test]
    public void Each_handle_owns_exactly_the_values_its_drag_can_move()
    {
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Left), Is.EqualTo(LayerZoneField.KeyLo));
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Right), Is.EqualTo(LayerZoneField.KeyHi));
        // Loud is up, so the lane's top edge is the *upper* velocity and its bottom edge the lower one -- the
        // same pairing ResolveDrag uses for those two handles. Swapped, a drag on the top edge would write the
        // bottom one's parameter, which is a bug no amount of correct geometry would show.
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Top), Is.EqualTo(LayerZoneField.VelHi));
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Bottom), Is.EqualTo(LayerZoneField.VelLo));
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Body),
            Is.EqualTo(LayerZoneField.KeyLo | LayerZoneField.KeyHi | LayerZoneField.VelLo | LayerZoneField.VelHi),
            "a body drag moves the zone bodily, so it owns all four range values and only those");
        // A press on the empty part of a lane is a question the chart answers by sounding a note. It is never an
        // edit, so it may write nothing at all.
        Assert.That(LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.None), Is.EqualTo(LayerZoneField.None));
    }

    [Test]
    public void No_handle_owns_a_fade_field()
    {
        // The invariant, not an omission: the map draws fades and does not drag them -- the four knobs are on the
        // part's own Set Part tab -- so no drag on this chart may ever write one. Asserted over every handle
        // there is, so adding a handle without deciding this cannot pass.
        const LayerZoneField fades = LayerZoneField.KeyFadeLo | LayerZoneField.KeyFadeHi |
                                     LayerZoneField.VelFadeLo | LayerZoneField.VelFadeHi;

        foreach (var handle in Enum.GetValues<PmtZoneMapping.Handle>())
            Assert.That(LayerZoneChanges.FieldsFor(handle) & fades, Is.EqualTo(LayerZoneField.None),
                $"{handle} must not be able to write a fade width");
    }

    [Test]
    public void A_change_arriving_mid_drag_cannot_be_reverted_by_the_next_pointer_move()
    {
        // The bug the mask exists for, in full. The user is holding a drag on this part's left edge; the zone the
        // drag started from has Velocity Fade Width Lower at 10.
        var origin = Zone(velFadeLo: 10);

        // Mid-drag, something else changes that part: a front-panel edit, or a Studio Set change, which resyncs
        // all sixteen parts. The live value is now 20 -- and the drag knows nothing about it, because it resolves
        // from the press-time zone on purpose, so that a slow gesture cannot accumulate drift.
        var live = Zone(velFadeLo: 20);

        var edited = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Left,
            keyNow: 48, velNow: 20, keyAtPress: 60, velAtPress: 64);
        Assert.That(edited.VelFadeLo, Is.EqualTo(10), "the raise still carries the press-time fade width");

        // Diffed against live, that stale 10 reads as a change -- and writing it would silently push the fade
        // width back to what it was before the instrument reported 20, for a field the user never touched. This
        // assertion is the defect, stated so that it stays visible.
        Assert.That(LayerZoneChanges.Between(live, edited).HasFlag(LayerZoneField.VelFadeLo), Is.True,
            "the diff alone would write back a value the drag never moved");

        // Confined by the handle, the write is the one field the gesture is actually about.
        Assert.That(LayerZoneChanges.Between(live, edited) & LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Left),
            Is.EqualTo(LayerZoneField.KeyLo));
    }

    [Test]
    public void Moving_a_full_velocity_zone_sideways_writes_two_parameters_and_not_four()
    {
        // The default part has velocity 0..127, so its range already fills the axis and a body drag cannot move
        // it vertically at all -- ShiftPreservingSpan refuses rather than squashing. Which means the ordinary
        // gesture of sliding a split along the keyboard writes exactly the two key parameters. That is the
        // saving this whole mechanism exists for, on the most common zone there is.
        var origin = Zone(velLo: 0, velHi: 127);
        var edited = LayerMapGeometry.ResolveDrag(origin, PmtZoneMapping.Handle.Body,
            keyNow: 67, velNow: 70, keyAtPress: 66, velAtPress: 64);

        Assert.That(edited.KeyLo, Is.EqualTo(61), "shifted by the one key the pointer crossed");
        Assert.That(edited.KeyHi, Is.EqualTo(73));
        // A body drag's mask is four fields wide, so this saving is the diff's and not the mask's -- which is why
        // Apply needs both halves and not just the mask.
        Assert.That(LayerZoneChanges.Between(origin, edited) & LayerZoneChanges.FieldsFor(PmtZoneMapping.Handle.Body),
            Is.EqualTo(LayerZoneField.KeyLo | LayerZoneField.KeyHi));
    }
}
