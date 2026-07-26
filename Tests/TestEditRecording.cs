using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Integra7AuralAlchemist.Controls;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;
using Microsoft.Reactive.Testing;

namespace Tests;

/// <summary>What the friendly editors put into the undo history -- and, more importantly, what they must
/// keep out of it. <see cref="ParamInt"/> stands in for all three wrappers: they share the setter shape,
/// and only ParamInt can be exercised without a repr table.
///
/// No device is involved. The <see cref="ThrottledParameterWriter"/> below runs on a
/// <see cref="TestScheduler"/> that is never advanced, so the enqueued write never fires; recording
/// happens on the setter's own thread, before the throttle, which is the whole point.</summary>
[TestFixture]
public class EditRecordingTests
{
    private const string Start = "Temporary Studio Set";
    private const string Offset = "Offset/Not Used";
    private const string Offset2 = "Offset2/Studio Set Part 1";
    private const string Path = "Studio Set Part/Part Level";

    private const string ChorusOffset2 = "Offset2/Studio Set Common Chorus";
    private const string ChorusTypePath = "Studio Set Common Chorus/Chorus Type";

    private static Integra7Parameters? _parameters;

    private static DomainBase NewDomain()
    {
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();
        return new DomainBase(new TestFailedReadKeepsValues.SilentApi(), new Integra7StartAddresses(),
            _parameters, Start, Offset, Offset2, "Studio Set Part/");
    }

    private static FullyQualifiedParameter NewParameter()
    {
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();
        return new FullyQualifiedParameter(Start, Offset, Offset2, _parameters.Lookup(Path));
    }

    private static DomainBase NewChorusDomain()
    {
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();
        return new DomainBase(new TestFailedReadKeepsValues.SilentApi(), new Integra7StartAddresses(),
            _parameters, Start, Offset, ChorusOffset2, "Studio Set Common Chorus/");
    }

    private static FullyQualifiedParameter NewChorusTypeParameter()
    {
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();
        return new FullyQualifiedParameter(Start, Offset, ChorusOffset2, _parameters.Lookup(ChorusTypePath));
    }

    /// <summary>The journal the application records into is a process-wide singleton, so a test that did
    /// not clear it would see whatever the previous test left behind.</summary>
    [SetUp]
    public void ClearTheJournal() => EditJournal.Default.Clear();

    [TearDown]
    public void ClearTheJournalAgain() => EditJournal.Default.Clear();

    [Test]
    public void A_user_edit_records_one_change_with_the_parameters_address_and_both_values()
    {
        var p = NewParameter();
        p.StringValue = "100";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamInt(NewDomain(), p, writer, 0, 127);
        Assert.That(param.Value, Is.EqualTo(100), "the wrapper starts from the parameter's own value");

        param.Value = 110;

        Assert.That(EditJournal.Default.CanUndo, Is.True, "a user edit must be recorded");
        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(1), "one setter call, one change");
        var change = pending.Step.Changes[0];
        Assert.That(change.Start, Is.EqualTo(Start));
        Assert.That(change.Offset, Is.EqualTo(Offset));
        Assert.That(change.Offset2, Is.EqualTo(Offset2));
        Assert.That(change.Path, Is.EqualTo(Path));
        Assert.That(change.OldValue, Is.EqualTo("100"), "the value from before the edit");
        Assert.That(change.NewValue, Is.EqualTo("110"), "the value the edit produced");
        Assert.That(change.IsDiscriminator, Is.False,
            "read off the real spec: nothing in the Studio Set Part block governs another parameter");
        Assert.That(pending.Writes.Single().ValueToApply, Is.EqualTo("100"),
            "undoing it writes the value from before the edit");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "exactly one step, not several");
    }

    /// <summary>The Studio Set Part block used everywhere else in this fixture has no discriminator in
    /// it, so every other test here would still pass with a record site that hard-coded <c>IsDiscriminator:
    /// false</c> -- silently restoring the wrong write order (see <see cref="PendingEdit.Writes"/>).
    /// "Studio Set Common Chorus/Chorus Type" really does govern the Chorus Parameter slots (the database
    /// analyzer sets <c>IsParent</c> on it from the other side's <c>par:</c> references, so the flag is
    /// asserted here off the live parameter rather than assumed from the definitions file), so this proves
    /// the flag genuinely propagates as true through a real record site instead of only proving it can be
    /// false.</summary>
    [Test]
    public void A_discriminator_edit_is_recorded_with_IsDiscriminator_true()
    {
        var p = NewChorusTypeParameter();
        Assert.That(p.ParSpec.IsParent, Is.True,
            "this only proves anything if Chorus Type really is a discriminator; otherwise it would " +
            "pass vacuously even for a record site that always passed false");

        p.StringValue = "Chorus";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamString(NewChorusDomain(), p, writer);
        Assert.That(param.Value, Is.EqualTo("Chorus"), "the wrapper starts from the parameter's own value");

        param.Value = "GM2 Chorus";

        Assert.That(EditJournal.Default.CanUndo, Is.True, "a user edit must be recorded");
        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(1), "one setter call, one change");
        var change = pending.Step.Changes[0];
        Assert.That(change.Start, Is.EqualTo(Start));
        Assert.That(change.Offset, Is.EqualTo(Offset));
        Assert.That(change.Offset2, Is.EqualTo(ChorusOffset2));
        Assert.That(change.Path, Is.EqualTo(ChorusTypePath));
        Assert.That(change.OldValue, Is.EqualTo("Chorus"));
        Assert.That(change.NewValue, Is.EqualTo("GM2 Chorus"));
        Assert.That(change.IsDiscriminator, Is.True,
            "read off the real spec: Chorus Type governs the Chorus Parameter slots");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "exactly one step, not several");
    }

    /// <summary>The one that matters. A value arriving from the instrument's front panel travels
    /// StringValue -> PropertyChanged -> ApplyFromModel -> the same Value setter a user edit uses, with
    /// _suppress set. If that were recorded, undo would push the device's own state back at it.</summary>
    [Test]
    public void A_change_arriving_from_the_device_records_nothing()
    {
        var p = NewParameter();
        p.StringValue = "100";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamInt(NewDomain(), p, writer, 0, 127);

        // What a read from the device does: set the model value and let the wrapper follow. ParamInt
        // marshals that through the dispatcher, so the queued job has to be run for the test to be
        // testing anything -- hence the assertion on Value below.
        p.StringValue = "42";
        Dispatcher.UIThread.RunJobs();

        Assert.That(param.Value, Is.EqualTo(42), "the echo must actually have reached the setter");
        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "a change the device reported is not an edit the user made");
    }

    /// <summary>Construction reads the parameter's current value through the same suppressed path, which
    /// is how every editor tab is populated. Fifteen view models building their wrappers must not fill
    /// the history before the user has touched anything.</summary>
    [Test]
    public void Building_a_wrapper_over_an_already_valued_parameter_records_nothing()
    {
        var p = NewParameter();
        p.StringValue = "77";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamInt(NewDomain(), p, writer, 0, 127);

        Assert.That(param.Value, Is.EqualTo(77));
        Assert.That(EditJournal.Default.CanUndo, Is.False);
    }

    /// <summary>A slow drag, end to end: a real control's styled property, a real two-way binding, a real
    /// <see cref="ParamInt"/>, and the journal the application actually records into.
    ///
    /// Two things are being pinned. One, the gesture the control holds open really does keep a whole drag
    /// in one step -- the two assignments below are more than <see cref="EditJournal.CoalesceWindow"/>
    /// apart in <em>real</em> time (the singleton journal has no injectable clock, so that is the only way
    /// to say it here), which is precisely the careful, one-step-per-second drag that used to produce an
    /// undo step per step. Two, the binding carries the assignment to the wrapper <em>synchronously</em>:
    /// the whole design assumes the record lands while the control's scope is open, and a binding that
    /// posted its source update instead would land it after the release and split the drag in two. The
    /// assertion on <c>param.Value</c> immediately after the first assignment is what pins that, and it
    /// fails loudly rather than silently if Avalonia ever defers it.</summary>
    [Test]
    public void A_slow_drag_over_a_real_control_and_a_real_wrapper_is_one_step()
    {
        var p = NewParameter();
        p.StringValue = "100";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamInt(NewDomain(), p, writer, 0, 127);

        // Level3 stands in for the eight controls' value properties: same styled-property-with-a-two-way
        // binding shape, and this one needs no windowing platform to construct.
        var control = new MultiStageEnvelopeControl();
        using var binding = control.Bind(MultiStageEnvelopeControl.Level3Property,
            new Binding(nameof(ParamInt.Value)) { Mode = BindingMode.TwoWay, Source = param });
        Assert.That(control.Level3, Is.EqualTo(100), "the binding starts from the wrapper's value");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "binding to it is not an edit");

        var gesture = new EditGesture();
        gesture.Begin();                        // what OnPointerPressed does once a drag is certain
        control.Level3 = 101;
        Assert.That(param.Value, Is.EqualTo(101),
            "the assignment must reach the wrapper before the pointer handler returns -- if this fails, " +
            "the binding has become deferred and a scope opened and closed by the pointer handlers can " +
            "no longer contain the records");

        System.Threading.Thread.Sleep(EditJournal.CoalesceWindow + TimeSpan.FromMilliseconds(50));
        control.Level3 = 102;
        gesture.End();                          // what OnPointerReleased does

        Assert.That(EditJournal.Default.TryUndo(out var undo), Is.True);
        Assert.That(undo!.Writes.Single().ValueToApply, Is.EqualTo("100"),
            "back to the value from before the drag");
        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "one drag, one step, even though the two changes are further apart than the coalesce window");
    }

    /// <summary>Setting the value it already holds is not an edit, so the early return before the raise
    /// must also come before the recording.</summary>
    [Test]
    public void Setting_the_value_it_already_holds_records_nothing()
    {
        var p = NewParameter();
        p.StringValue = "100";
        using var writer = new ThrottledParameterWriter(Constants.THROTTLE, new TestScheduler());
        using var param = new ParamInt(NewDomain(), p, writer, 0, 127);

        param.Value = 100;

        Assert.That(EditJournal.Default.CanUndo, Is.False);
    }

    // ---------------------------------------------------------------------------------------------
    // The third write door: the Motional Surround editor, which has no ParamInt to record for it. It
    // writes through DomainBase.WriteToIntegraAsync(path, displayValue) on its own debounced subject, so
    // all it holds at the point of an edit is a block and a path, and DomainEditRecorder derives the rest.
    // ---------------------------------------------------------------------------------------------

    private const string MsOffset2 = "Offset2/Studio Set Common Motional Surround";
    private const string MsPrefix = "Studio Set Common Motional Surround/";
    private const string MsDepthPath = MsPrefix + "Motional Surround Depth";

    private const string PartLrPath = "Studio Set Part/Motional Surround L-R";
    private const string PartFbPath = "Studio Set Part/Motional Surround F-B";
    private const string PartWidthPath = "Studio Set Part/Motional Surround Width";
    private const string PartAmbiencePath = "Studio Set Part/Motional Surround Ambience Send Level";

    private static Integra7Parameters Parameters() =>
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();

    private static DomainBase NewMotionalSurroundCommonDomain() =>
        new(new TestFailedReadKeepsValues.SilentApi(), new Integra7StartAddresses(), Parameters(),
            Start, Offset, MsOffset2, MsPrefix);

    /// <summary>A whole instrument's worth of blocks, without a device: what the Motional Surround view
    /// model takes, because it spans seventeen of them (the common block plus one per part).</summary>
    private static Integra7Domain NewCommunicator() =>
        new(new TestFailedReadKeepsValues.SilentApi(), new Integra7StartAddresses(), Parameters());

    /// <summary>Give the part block the four values the view model reads at construction, so it does not
    /// start every part from the empty string a never-read parameter holds.</summary>
    private static DomainBase GivenPart1Position(Integra7Domain i7, string lr, string fb, string width,
        string ambience)
    {
        var part = i7.StudioSetPart(0);
        part.ModifySingleParameterDisplayedValue(PartLrPath, lr);
        part.ModifySingleParameterDisplayedValue(PartFbPath, fb);
        part.ModifySingleParameterDisplayedValue(PartWidthPath, width);
        part.ModifySingleParameterDisplayedValue(PartAmbiencePath, ambience);
        return part;
    }

    /// <summary>Everything a <see cref="ParameterChange"/> needs, derived from a block and a path alone.
    /// The old value is the interesting half: it is read off the block rather than supplied by the caller,
    /// which is only right as long as nothing has written the new one there yet.</summary>
    [Test]
    public void An_edit_described_from_a_block_and_a_path_carries_the_address_and_the_previous_value()
    {
        var domain = NewMotionalSurroundCommonDomain();
        domain.ModifySingleParameterDisplayedValue(MsDepthPath, "40");

        var change = DomainEditRecorder.Describe(domain, MsDepthPath, "80");

        Assert.That(change, Is.Not.Null);
        Assert.That(change!.Start, Is.EqualTo(Start));
        Assert.That(change.Offset, Is.EqualTo(Offset));
        Assert.That(change.Offset2, Is.EqualTo(MsOffset2));
        Assert.That(change.Path, Is.EqualTo(MsDepthPath));
        Assert.That(change.OldValue, Is.EqualTo("40"), "read off the block, not handed in by the caller");
        Assert.That(change.NewValue, Is.EqualTo("80"));
        Assert.That(change.IsDiscriminator, Is.False);
    }

    /// <summary>The write really does come after the read, on the real path: writing through the domain
    /// replaces the value <see cref="DomainEditRecorder.Describe"/> would have called old, so describing
    /// afterwards yields old == new and an undo that does nothing. Pins the ordering the Motional Surround
    /// setters depend on -- record, then enqueue.</summary>
    [Test]
    public void Describing_after_the_write_instead_of_before_it_would_lose_the_previous_value()
    {
        var domain = NewMotionalSurroundCommonDomain();
        domain.ModifySingleParameterDisplayedValue(MsDepthPath, "40");

        var before = DomainEditRecorder.Describe(domain, MsDepthPath, "80");
        domain.ModifySingleParameterDisplayedValue(MsDepthPath, "80");   // what the write does first
        var after = DomainEditRecorder.Describe(domain, MsDepthPath, "80");

        Assert.That(before!.OldValue, Is.EqualTo("40"));
        Assert.That(after!.OldValue, Is.EqualTo("80"),
            "which is why the record has to happen before the write is enqueued, not after");
    }

    /// <summary>The flag is read off the live spec, not assumed. Every Motional Surround parameter answers
    /// false (see the test below), so nothing else here would notice a hard-coded false; this puts a real
    /// discriminator through the same helper.</summary>
    [Test]
    public void Describe_reads_the_discriminator_flag_off_the_spec_rather_than_assuming_it()
    {
        var domain = NewChorusDomain();
        domain.ModifySingleParameterDisplayedValue(ChorusTypePath, "Chorus");

        var change = DomainEditRecorder.Describe(domain, ChorusTypePath, "GM2 Chorus");

        Assert.That(change!.OldValue, Is.EqualTo("Chorus"));
        Assert.That(change.IsDiscriminator, Is.True,
            "Chorus Type governs the Chorus Parameter slots, and the helper has to notice");
    }

    /// <summary>Why the test above needs a chorus block to prove anything: neither Motional Surround block
    /// holds a discriminator, so a helper that always answered false would pass every other test here.
    /// Asserted off the real database rather than read off the definitions file, because <c>IsParent</c> is
    /// something the analyzer derives from the other side's <c>par:</c> references.</summary>
    [Test]
    public void No_motional_surround_parameter_is_a_discriminator()
    {
        Assert.That(NewMotionalSurroundCommonDomain().GetRelevantParameters(true, true)
                .Where(p => p.ParSpec.IsParent).Select(p => p.ParSpec.Path),
            Is.Empty, "nothing in the Motional Surround common block governs another parameter");

        var partMs = NewDomain().GetRelevantParameters(true, true)
            .Where(p => p.ParSpec.Path.Contains("Motional Surround")).ToList();
        Assert.That(partMs.Count, Is.EqualTo(4), "L-R, F-B, Width and Ambience Send Level");
        Assert.That(partMs.Where(p => p.ParSpec.IsParent), Is.Empty);
    }

    /// <summary>Nothing to go back to, nothing recorded. A path the block does not hold cannot be undone,
    /// and neither can a block nobody has read -- every parameter in one still holds the empty string, so
    /// there is no previous value, and recording "" would have undo write it to the instrument.</summary>
    [Test]
    public void An_edit_with_no_previous_value_to_go_back_to_records_nothing()
    {
        var domain = NewMotionalSurroundCommonDomain();

        Assert.That(DomainEditRecorder.Describe(domain, MsPrefix + "No Such Parameter", "1"), Is.Null,
            "the block holds no such parameter");
        Assert.That(DomainEditRecorder.Describe(domain, MsDepthPath, "80"), Is.Null,
            "the block has never been read, so nothing in it has a value yet");

        DomainEditRecorder.Record(domain, MsPrefix + "No Such Parameter", "1");
        DomainEditRecorder.Record(domain, MsDepthPath, "80");

        Assert.That(EditJournal.Default.CanUndo, Is.False);
    }

    /// <summary>The part view model's own door, through the real thing: seventeen blocks, sixteen part view
    /// models and the external one, all built against a real parameter database and no device. Width stands
    /// in for Ambience and Channel -- they share the setter shape and all three funnel through
    /// <c>EnqueueValueWrite</c>, as do the common values.</summary>
    [Test]
    public void A_part_width_edit_records_one_change_against_that_parts_own_block()
    {
        var i7 = NewCommunicator();
        GivenPart1Position(i7, "0", "0", "16", "0");
        using var vm = new MotionalSurroundViewModel(i7);
        var part = vm.InternalParts[0];
        Assert.That(part.Width, Is.EqualTo(16), "the view model starts from the block's own values");
        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "building seventeen part view models is not an edit");

        part.Width = 24;

        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(1));
        var change = pending.Step.Changes[0];
        Assert.That(change.Start, Is.EqualTo(Start));
        Assert.That(change.Offset, Is.EqualTo(Offset));
        Assert.That(change.Offset2, Is.EqualTo("Offset2/Studio Set Part 1"),
            "part 1's own block, not the common one -- undo would otherwise move a different part");
        Assert.That(change.Path, Is.EqualTo(PartWidthPath));
        Assert.That(change.OldValue, Is.EqualTo("16"));
        Assert.That(change.NewValue, Is.EqualTo("24"));
        Assert.That(change.IsDiscriminator, Is.False);
        Assert.That(EditJournal.Default.CanUndo, Is.False, "exactly one step");
    }

    /// <summary>The one that matters here too. A value from the instrument's front panel arrives on the
    /// shared parameter object and reaches the same setter a user edit does, with <c>_suppress</c> set.</summary>
    [Test]
    public void A_part_width_change_arriving_from_the_device_records_nothing()
    {
        var i7 = NewCommunicator();
        var part1 = GivenPart1Position(i7, "0", "0", "16", "0");
        using var vm = new MotionalSurroundViewModel(i7);

        // What a read from the device leaves behind: a new value on the parameter the view model watches.
        // The view model marshals that through the dispatcher, so the queued job has to run.
        part1.ModifySingleParameterDisplayedValue(PartWidthPath, "20");
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.InternalParts[0].Width, Is.EqualTo(20), "the echo must have reached the setter");
        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "a change the device reported is not an edit the user made");
    }

    /// <summary>A slow puck drag. Two things at once: the gesture the view holds open keeps a drag that is
    /// more than <see cref="EditJournal.CoalesceWindow"/> wide in one step, and only the axis whose setter
    /// fired is recorded -- <c>EnqueuePositionWrite</c> writes L-R and F-B together, and recording the pair
    /// from each setter would put a change with old == new into every single-axis edit.</summary>
    [Test]
    public void A_slow_puck_drag_is_one_step_and_records_only_the_axis_that_moved()
    {
        var i7 = NewCommunicator();
        GivenPart1Position(i7, "0", "0", "16", "0");
        using var vm = new MotionalSurroundViewModel(i7);
        var part = vm.InternalParts[0];

        var gesture = new EditGesture();
        gesture.Begin();                    // what OnPuckPointerPressed does once the drag is certain
        part.Lr = 10;
        System.Threading.Thread.Sleep(EditJournal.CoalesceWindow + TimeSpan.FromMilliseconds(50));
        part.Lr = 20;
        gesture.End();                      // what OnPuckPointerReleased and capture-lost do

        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(1),
            "one drag, one parameter: F-B never moved and must not be in the step");
        var change = pending.Step.Changes[0];
        Assert.That(change.Path, Is.EqualTo(PartLrPath));
        Assert.That(change.OldValue, Is.EqualTo("0"), "back to where the drag started, not to 10");
        Assert.That(change.NewValue, Is.EqualTo("20"));
        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "one drag, one step, even though the two changes are further apart than the coalesce window");
    }

    /// <summary>A diagonal drag moves both axes, and undo has to put both back or the puck does not return
    /// to where it was -- the same reason an envelope handle records two changes.</summary>
    [Test]
    public void A_diagonal_puck_drag_records_both_axes_in_one_step()
    {
        var i7 = NewCommunicator();
        GivenPart1Position(i7, "0", "0", "16", "0");
        using var vm = new MotionalSurroundViewModel(i7);
        var part = vm.InternalParts[0];

        var gesture = new EditGesture();
        gesture.Begin();
        part.Lr = 10; part.Fb = -20;        // what OnPuckPointerMoved does, once
        part.Lr = 11; part.Fb = -21;        // and again
        gesture.End();

        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Select(c => c.Path),
            Is.EqualTo(new[] { PartLrPath, PartFbPath }), "both axes, in the order the drag touched them");
        Assert.That(pending.Step.Changes.Select(c => c.OldValue), Is.EqualTo(new[] { "0", "0" }));
        Assert.That(pending.Step.Changes.Select(c => c.NewValue), Is.EqualTo(new[] { "11", "-21" }));
        Assert.That(EditJournal.Default.CanUndo, Is.False, "one drag, one step");
    }

    /// <summary>How <c>MotionalSurroundView</c> is allowed to delimit a drag from its root, asserted rather
    /// than assumed because it is Avalonia's business and not ours. The press and the release tunnel, so a
    /// handler on the root sees them before the Slider's template parts do, whichever of those holds the
    /// pointer capture. Capture-lost does <em>not</em>: it is Direct, so it is only ever delivered to the
    /// element that held the capture, and a handler on an ancestor -- which is what this view had for its
    /// pucks -- can never run. That is why the view attaches the capture-lost handler to the captured
    /// element at press time instead; if this ever starts bubbling, that indirection becomes unnecessary.</summary>
    [Test]
    public void The_pointer_events_the_slider_gestures_hang_off_route_as_this_view_needs()
    {
        Assert.That(InputElement.PointerPressedEvent.RoutingStrategies & RoutingStrategies.Tunnel,
            Is.EqualTo(RoutingStrategies.Tunnel), "the press has to reach a handler on the view root");
        Assert.That(InputElement.PointerReleasedEvent.RoutingStrategies & RoutingStrategies.Tunnel,
            Is.EqualTo(RoutingStrategies.Tunnel), "and so does the release, which closes the gesture");
        Assert.That(InputElement.PointerCaptureLostEvent.RoutingStrategies,
            Is.EqualTo(RoutingStrategies.Direct),
            "Direct: a capture-lost handler is only any use on the element that held the capture");
    }

    // ---------------------------------------------------------------------------------------------
    // The preset commands. These bypass the setters by design -- a preset writes seventeen parts at once
    // and cannot go through a debounced door -- so they record explicitly, and each command holds one
    // journal gesture so that the whole preset is a single undo step.
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] MsGlobalPaths =
    [
        MsPrefix + "Room Type", MsPrefix + "Room Size", MsDepthPath, MsPrefix + "Ambience Level",
        MsPrefix + "Ambience Time", MsPrefix + "Ambience Density", MsPrefix + "Ambience HF Damp"
    ];

    private static readonly string[] ExtPartPaths =
    [
        MsPrefix + "Ext Part L-R", MsPrefix + "Ext Part F-B", MsPrefix + "Ext Part Width",
        MsPrefix + "Ext Part Ambience Send Level"
    ];

    /// <summary>Seed every block a preset touches -- sixteen part blocks and the external part's four
    /// parameters in the common one -- so the view model does not start from the empty string a never-read
    /// parameter holds, and so there is an old value for undo to go back to.</summary>
    private static void GivenEveryPartAt(Integra7Domain i7, int lr, int fb, int width, int ambience)
    {
        string S(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            var part = i7.StudioSetPart(i);
            part.ModifySingleParameterDisplayedValue(PartLrPath, S(lr));
            part.ModifySingleParameterDisplayedValue(PartFbPath, S(fb));
            part.ModifySingleParameterDisplayedValue(PartWidthPath, S(width));
            part.ModifySingleParameterDisplayedValue(PartAmbiencePath, S(ambience));
        }

        var common = i7.StudioSetCommonMotionalSurround;
        var extValues = new[] { S(lr), S(fb), S(width), S(ambience) };
        for (var i = 0; i < ExtPartPaths.Length; i++)
            common.ModifySingleParameterDisplayedValue(ExtPartPaths[i], extValues[i]);
    }

    /// <summary>Every parameter's display value as the blocks hold it now, for the paths given.</summary>
    private static Dictionary<string, string> Snapshot(DomainBase domain, params string[] paths)
        => paths.ToDictionary(p => p, domain.LookupSingleParameterDisplayedValue);

    /// <summary>A preset is one thing the user did, so it is one step -- and every part it moved is in it.
    /// <c>CenterAll</c> is the simplest: seventeen parts, both axes each, nothing else.</summary>
    [Test]
    public async Task A_preset_records_one_step_covering_every_part_it_moved()
    {
        var i7 = NewCommunicator();
        GivenEveryPartAt(i7, 10, -20, 8, 5);
        using var vm = new MotionalSurroundViewModel(i7);
        Assert.That(EditJournal.Default.CanUndo, Is.False, "building the view model is not an edit");

        await vm.CenterAll();

        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(EditJournal.Default.CanUndo, Is.False, "one command, one step -- not one step per part");
        var changes = pending!.Step.Changes;
        Assert.That(changes.Count, Is.EqualTo(2 * 17),
            "both axes of all sixteen internal parts and the external one");
        Assert.That(changes.Select(c => c.OldValue).Distinct(), Is.EquivalentTo(new[] { "10", "-20" }),
            "the values from before the preset, read off each block");
        Assert.That(changes.Select(c => c.NewValue).Distinct(), Is.EqualTo(new[] { "0" }));
        Assert.That(changes.Select(c => c.Offset2).Distinct().Count(), Is.EqualTo(17),
            "each part's own block, so undo cannot move a part the preset never touched");
        Assert.That(pending.Writes.Select(w => w.ValueToApply).Distinct(),
            Is.EquivalentTo(new[] { "10", "-20" }), "undoing it puts every part back where it was");
    }

    /// <summary>The one the coordinator's half-undo was about. <c>AmbientHallLayout</c> changes the room and
    /// the parts together, and one press has to put back both -- room and parts -- or the instrument ends up
    /// in a state that is neither the before nor the after.</summary>
    [Test]
    public async Task Undoing_a_preset_restores_the_globals_and_the_positions_together()
    {
        var i7 = NewCommunicator();
        var common = i7.StudioSetCommonMotionalSurround;
        GivenEveryPartAt(i7, 10, -30, 8, 5);
        foreach (var (path, value) in new[]
                 {
                     (MsPrefix + "Room Type", "Room1"), (MsPrefix + "Room Size", "Small"),
                     (MsDepthPath, "10"), (MsPrefix + "Ambience Level", "5"),
                     (MsPrefix + "Ambience Time", "5"), (MsPrefix + "Ambience Density", "5"),
                     (MsPrefix + "Ambience HF Damp", "5")
                 })
            common.ModifySingleParameterDisplayedValue(path, value);

        using var vm = new MotionalSurroundViewModel(i7);
        var globalsBefore = Snapshot(common, MsGlobalPaths);
        var partBefore = Snapshot(i7.StudioSetPart(0), PartLrPath, PartFbPath, PartWidthPath,
            PartAmbiencePath);

        await vm.AmbientHallLayout();

        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(EditJournal.Default.CanUndo, Is.False, "the room and the parts are one step, not two");
        var changes = pending!.Step.Changes;
        Assert.That(changes.Count, Is.EqualTo(MsGlobalPaths.Length + 16 * 3),
            "seven common values, and F-B, Width and Ambience for each of the sixteen internal parts");
        Assert.That(changes.Where(c => c.Path == PartLrPath), Is.Empty,
            "this preset keeps L-R, and a parameter it did not change must not be in the step");

        var undoes = pending.Writes.ToDictionary(w => (w.Change.Offset2, w.Change.Path),
            w => w.ValueToApply);
        foreach (var (path, before) in globalsBefore)
            Assert.That(undoes[(common.Offset2AddressName, path)], Is.EqualTo(before),
                $"undo must put '{path}' back to what the room was before the preset");
        foreach (var path in new[] { PartFbPath, PartWidthPath, PartAmbiencePath })
            Assert.That(undoes[("Offset2/Studio Set Part 1", path)], Is.EqualTo(partBefore[path]),
                $"and part 1's '{path}' back to where it was");
    }

    /// <summary>A preset asks every part for a position whether or not it is already there, so without the
    /// unchanged check a press of <c>CenterAll</c> on an already-centred field would fill an undo step with
    /// thirty-four changes that change nothing.</summary>
    [Test]
    public async Task A_preset_records_nothing_for_the_parts_it_does_not_move()
    {
        var i7 = NewCommunicator();
        GivenEveryPartAt(i7, 0, 0, 8, 5);
        using var vm = new MotionalSurroundViewModel(i7);

        await vm.CenterAll();

        Assert.That(EditJournal.Default.CanUndo, Is.False,
            "every part was already centred, so the preset changed nothing there is to undo");
    }

    /// <summary>An <see cref="TestFailedReadKeepsValues.SilentApi"/> whose writes take real time, so a
    /// preset's first and last records are further apart than <see cref="EditJournal.CoalesceWindow"/> and
    /// only the gesture the command holds can keep them in one step.</summary>
    private sealed class SlowWriteApi : TestFailedReadKeepsValues.SilentApi
    {
        public override async Task MakeDataTransmissionAsync(byte[] address, byte[] data,
            IMidiLease? lease = null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            await base.MakeDataTransmissionAsync(address, data, lease);
        }
    }

    /// <summary>The gesture, pinned the way the slow drags are: a preset that takes longer than the coalesce
    /// window is still one step. Only the first and the last of the seventeen parts have anywhere to move,
    /// so the two records that happen are separated by the thirty writes for the fifteen parts in between --
    /// comfortably past the window, and nowhere near
    /// <see cref="EditJournal.StaleGestureWindow"/>. Without the gesture this is two steps, and undo would
    /// leave the field half-centred.</summary>
    [Test]
    public async Task A_preset_slower_than_the_coalesce_window_is_still_one_step()
    {
        var i7 = new Integra7Domain(new SlowWriteApi(), new Integra7StartAddresses(), Parameters());
        GivenEveryPartAt(i7, 0, 0, 8, 5);
        i7.StudioSetPart(0).ModifySingleParameterDisplayedValue(PartLrPath, "40");
        i7.StudioSetCommonMotionalSurround
            .ModifySingleParameterDisplayedValue(MsPrefix + "Ext Part L-R", "-40");
        using var vm = new MotionalSurroundViewModel(i7);

        var started = DateTimeOffset.UtcNow;
        await vm.CenterAll();
        var took = DateTimeOffset.UtcNow - started;

        Assert.That(took, Is.GreaterThan(EditJournal.CoalesceWindow),
            "the test only proves anything if the command really outlasted the coalesce window");
        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Select(c => c.OldValue), Is.EqualTo(new[] { "40", "-40" }),
            "the first part's L-R and the external part's, in one step, thirty writes apart");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "one command, one step");
    }

    /// <summary>And that a Direct handler on the element really is served while a bubbling one on its parent
    /// is not -- the whole reason the view moved its capture-lost registration off the puck host and onto
    /// the puck. Raised directly rather than through an input device: this is about routing, and no windowing
    /// platform is needed to settle it.</summary>
    [Test]
    public void A_capture_lost_handler_reaches_the_element_that_lost_it_and_not_its_parent()
    {
        var child = new Border();
        var parent = new Border { Child = child };
        var reached = new List<string>();
        child.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => reached.Add("child"),
            RoutingStrategies.Direct);
        parent.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => reached.Add("parent"),
            RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);

        child.RaiseEvent(new PointerCaptureLostEventArgs(child, null!));

        Assert.That(reached, Is.EqualTo(new[] { "child" }),
            "a handler on an ancestor is never called for a Direct event, however it is registered");
    }
}
