using System;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class EditJournalTests
{
    private static DateTimeOffset _now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static EditJournal NewJournal() => new(() => _now);

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
}
