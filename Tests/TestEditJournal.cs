using System;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class EditJournalTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset _now = InitialNow;

    // _now is a shared static that these tests advance. NUnit may run the fixture's tests in any
    // order (and, depending on configuration, in parallel), so without resetting it here a test that
    // happens to run after one that advanced the clock would see a stale value instead of the fixed
    // starting point it expects. Resetting on every call makes each test independent of run order.
    private static EditJournal NewJournal()
    {
        _now = InitialNow;
        return new EditJournal(() => _now);
    }

    private static EditStep Step(string path, string oldValue, string newValue) =>
        new("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 1", path, oldValue, newValue);

    [Test]
    public void Undo_returns_the_step_reversed_and_redo_returns_it_forward()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));

        Assert.That(journal.CanUndo, Is.True);
        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(undo.ValueToApply, Is.EqualTo("100"), "undo applies the value from before the edit");

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True);
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo.ValueToApply, Is.EqualTo("110"), "redo applies the value the edit set");
    }

    [Test]
    public void A_gesture_on_one_parameter_is_one_step()
    {
        // A knob drag is hundreds of setter calls. Undo must return the value from before the drag,
        // not walk back through every intermediate.
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        _now = _now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));
        _now = _now.AddMilliseconds(50);
        journal.Record(Step("Studio Set Part/Part Level", "102", "103"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo.ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the three calls were one gesture");
    }

    [Test]
    public void A_pause_starts_a_new_step()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        _now = _now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Step("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first.ValueToApply, Is.EqualTo("101"));
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void Editing_a_different_parameter_starts_a_new_step()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "101"));
        journal.Record(Step("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out var pan), Is.True);
        Assert.That(pan.Path, Is.EqualTo("Studio Set Part/Part Pan"));
        Assert.That(journal.TryUndo(out var level), Is.True);
        Assert.That(level.Path, Is.EqualTo("Studio Set Part/Part Level"));
    }

    [Test]
    public void The_same_path_in_a_different_part_is_a_different_parameter()
    {
        // Every part's parameters share a path; only the address tells them apart. Coalescing on the
        // path alone would merge an edit on part 1 with one on part 2 and undo the wrong part.
        var journal = NewJournal();
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", "Studio Set Part/Part Level", "100", "101"));
        journal.Record(new EditStep("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 2", "Studio Set Part/Part Level", "50", "51"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 2"));
        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first.Step.Offset2, Is.EqualTo("Offset2/Studio Set Part 1"));
    }

    [Test]
    public void A_new_edit_drops_the_redo_history()
    {
        var journal = NewJournal();
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
        var journal = NewJournal();
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
        var journal = NewJournal();
        for (var i = 0; i < EditJournal.Capacity + 50; i++)
        {
            journal.Record(Step($"Studio Set Part/Parameter {i}", $"{i}", $"{i + 1}"));
            _now = _now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        }

        var undone = 0;
        while (journal.TryUndo(out _)) undone++;
        Assert.That(undone, Is.EqualTo(EditJournal.Capacity));
    }

    [Test]
    public void Clearing_forgets_both_directions()
    {
        var journal = NewJournal();
        journal.Record(Step("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.Clear();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.False);
    }
}
