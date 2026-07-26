using Avalonia.Threading;
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
        Assert.That(pending.Writes.Single().ValueToApply, Is.EqualTo("100"),
            "undoing it writes the value from before the edit");
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
}
