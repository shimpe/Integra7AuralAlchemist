using System;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class EditJournalTests
{
    private sealed class TestClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    }

    // Each test gets its own EditJournal and its own clock, captured by the lambda passed to the
    // journal's constructor. A shared static field would only fix NUnit's run order (a test that
    // happens to run after one that advanced the clock would see a stale value); a fresh clock per
    // test also makes the tests safe under genuine parallel execution, since there is no mutable
    // state left to share.
    private static (EditJournal Journal, TestClock Clock) NewJournal()
    {
        var clock = new TestClock();
        return (new EditJournal(() => clock.Now), clock);
    }

    private static EditStep Step(string path, string oldValue, string newValue) =>
        new("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 1", path, oldValue, newValue);

    [Test]
    public void Undo_returns_the_step_reversed_and_redo_returns_it_forward()
    {
        var (journal, _) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));

        Assert.That(journal.CanUndo, Is.True);
        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo!.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(undo.ValueToApply, Is.EqualTo("100"), "undo applies the value from before the edit");

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True);
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo!.ValueToApply, Is.EqualTo("110"), "redo applies the value the edit set");
    }

    [Test]
    public void A_gesture_on_one_parameter_is_one_step()
    {
        // A knob drag is hundreds of setter calls. Undo must return the value from before the drag,
        // not walk back through every intermediate.
        var (journal, clock) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));
        clock.Now = clock.Now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "102", "103"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo!.ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the three calls were one gesture");

        // Mutation-proven: keeping the OLD NewValue when coalescing leaves the undo-side assertion
        // above green too, so the redo side must be pinned separately.
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo!.ValueToApply, Is.EqualTo("103"), "redo applies the value the gesture ended on");
    }

    [Test]
    public void A_pause_starts_a_new_step()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first!.ValueToApply, Is.EqualTo("101"));
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second!.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void Editing_a_different_parameter_starts_a_new_step()
    {
        var (journal, _) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        journal.Record(Step("Studio Set Part/Part Pan", "0", "10"));
        // Back on the first parameter: this must be its own third step, not a continuation of the
        // very first one, even though it shares its path and address.
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var levelAgain), Is.True);
        Assert.That(levelAgain!.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(levelAgain.ValueToApply, Is.EqualTo("101"));

        Assert.That(journal.TryUndo(out var pan), Is.True);
        Assert.That(pan!.Path, Is.EqualTo("Studio Set Part/Part Pan"));

        Assert.That(journal.TryUndo(out var level), Is.True);
        Assert.That(level!.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(level.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void The_same_path_in_a_different_part_is_a_different_parameter()
    {
        // Every part's parameters share a path; only the address tells them apart. Coalescing on the
        // path alone would merge an edit on part 1 with one on part 2 and undo the wrong part.
        var (journal, _) = NewJournal();
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", "Studio Set Part/Part Level", "100", "101"));
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 2", "Studio Set Part/Part Level", "50", "51"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second!.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 2"));
        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first!.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 1"));
    }

    [Test]
    public void A_new_edit_drops_the_redo_history()
    {
        var (journal, _) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);
        Assert.That(journal.CanRedo, Is.True);

        journal.Record(Step("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.CanRedo, Is.False, "the redone future no longer follows from this history");
    }

    [Test]
    public void Nothing_is_recorded_while_a_step_is_being_applied()
    {
        // The write undo performs comes back through the same setters that record. Without this the
        // history would never empty.
        var (journal, _) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.ApplyAsync(() =>
        {
            journal.Record(Step("Studio Set Part/Part Level", "110", "100"));
            return System.Threading.Tasks.Task.CompletedTask;
        }).GetAwaiter().GetResult();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True, "the redo the undo created must survive applying it");
    }

    [Test]
    public void The_history_is_bounded()
    {
        // Mutation-proven: a bare count assertion stays green even if the newest entries are the ones
        // dropped instead of the oldest. Asserting identity at both ends pins which end actually goes.
        var (journal, clock) = NewJournal();
        for (var i = 0; i < EditJournal.Capacity + 50; i++)
        {
            journal.Record(Step($"Studio Set Part/Parameter {i}", $"{i}", $"{i + 1}"));
            clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        }

        Assert.That(journal.TryUndo(out var firstPopped), Is.True);
        Assert.That(firstPopped!.Path, Is.EqualTo("Studio Set Part/Parameter 249"),
            "the most recently recorded edit undoes first");

        var undone = 1;
        PendingEdit lastPopped = firstPopped;
        while (journal.TryUndo(out var pending))
        {
            lastPopped = pending;
            undone++;
        }

        Assert.That(undone, Is.EqualTo(EditJournal.Capacity));
        Assert.That(lastPopped.Path, Is.EqualTo("Studio Set Part/Parameter 50"),
            "the oldest surviving edit -- everything before it was dropped for capacity");
    }

    [Test]
    public void An_edit_after_undo_does_not_coalesce_with_whatever_is_now_on_top()
    {
        // Regression: _lastRecordedAt described the top of _undo only while Record was the last thing
        // to touch it. TryUndo/TryRedo change what is on top without updating it, so a fresh gesture
        // started shortly after an undo -- well within the coalesce window of the record that preceded
        // the undo -- could wrongly merge into the step the undo left on top, silently deleting the
        // step that was undone (and the value "110" it recorded).
        var (journal, clock) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Step("Studio Set Part/Part Level", "110", "120"));

        Assert.That(journal.TryUndo(out var undone), Is.True);
        Assert.That(undone!.ValueToApply, Is.EqualTo("110"));

        // No time is advanced here: this is well within the coalesce window of the *second* Record
        // call above, which is exactly the stale timestamp the bug compares against.
        journal.Record(Step("Studio Set Part/Part Level", "110", "130"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first!.ValueToApply, Is.EqualTo("110"), "the post-undo gesture is its own step");
        Assert.That(journal.CanUndo, Is.True, "the original 100->110 step must still be there");
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second!.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void Concurrent_records_from_many_threads_do_not_corrupt_the_history()
    {
        // Not speculation: the friendly editors call Record from SynthParam's setters on the UI
        // thread, but the raw grid's path (MainWindowViewModel.UpdateIntegraFromUiAsync) is reached
        // through MessageBus.Current.Listen<UpdateMessageSpec>("ui2hw").Throttle(...), and Throttle
        // with no scheduler runs on the thread pool. Record must survive being called from both at
        // once; List<T> is not thread-safe, so without a lock this either throws or silently loses
        // writes under enough concurrent volume.
        var (journal, _) = NewJournal();
        const int threads = 16;
        const int perThread = 50;

        Assert.DoesNotThrow(() => Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
                journal.Record(Step($"Studio Set Part/Parameter {t}-{i}", "0", "1"));
        }));

        var undone = 0;
        while (journal.TryUndo(out _)) undone++;
        Assert.That(undone, Is.EqualTo(Math.Min(threads * perThread, EditJournal.Capacity)),
            "every recorded edit (up to the capacity) must survive, with none lost to a race");
    }

    [Test]
    public async Task Nested_ApplyAsync_does_not_unsuppress_the_outer_call_early()
    {
        // ApplyAsync must restore the previous suppression state on exit, not unconditionally clear
        // it: if it clears, an inner ApplyAsync finishing (e.g. a nested undo triggered while another
        // is in flight) turns recording back on even though the outer call is still awaiting.
        var (journal, _) = NewJournal();
        var stillSuppressedAfterInner = false;

        await journal.ApplyAsync(async () =>
        {
            await journal.ApplyAsync(() => Task.CompletedTask);
            stillSuppressedAfterInner = journal.IsApplying;
        });

        Assert.That(stillSuppressedAfterInner, Is.True,
            "the outer ApplyAsync's suppression must still be in effect after the inner one returns");
    }

    [Test]
    public void Clearing_forgets_both_directions()
    {
        var (journal, _) = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.Clear();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.False);
    }
}
