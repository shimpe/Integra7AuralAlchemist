using System;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Controls;
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

    private static ParameterChange Change(string path, string oldValue, string newValue,
        bool isDiscriminator = false) =>
        new("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 1", path, oldValue, newValue,
            isDiscriminator);

    /// <summary>The single write a one-parameter step produces. Every assertion that reads a step's path
    /// or value goes through here, so a step that unexpectedly grew a second change fails loudly instead
    /// of being silently indexed past.</summary>
    private static (ParameterChange Change, string ValueToApply) Only(PendingEdit pending)
    {
        Assert.That(pending.Writes.Count, Is.EqualTo(1), "expected a step covering a single parameter");
        return pending.Writes[0];
    }

    [Test]
    public void Undo_returns_the_step_reversed_and_redo_returns_it_forward()
    {
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));

        Assert.That(journal.CanUndo, Is.True);
        Assert.That(journal.TryUndo(out var undo), Is.True);
        var undoWrite = Only(undo!);
        Assert.That(undoWrite.Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(undoWrite.ValueToApply, Is.EqualTo("100"), "undo applies the value from before the edit");

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.True);
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(Only(redo!).ValueToApply, Is.EqualTo("110"), "redo applies the value the edit set");
    }

    [Test]
    public void A_gesture_on_one_parameter_is_one_step()
    {
        // A knob drag is hundreds of setter calls. Undo must return the value from before the drag,
        // not walk back through every intermediate.
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.AddMilliseconds(50);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));
        clock.Now = clock.Now.AddMilliseconds(50);
        journal.Record(Change("Studio Set Part/Part Level", "102", "103"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the three calls were one gesture");

        // Mutation-proven: keeping the OLD NewValue when coalescing leaves the undo-side assertion
        // above green too, so the redo side must be pinned separately.
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(Only(redo!).ValueToApply, Is.EqualTo("103"), "redo applies the value the gesture ended on");
    }

    [Test]
    public void A_pause_starts_a_new_step()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(Only(first!).ValueToApply, Is.EqualTo("101"));
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(Only(second!).ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void Coming_back_to_a_parameter_after_a_pause_does_not_reopen_its_old_step()
    {
        // Three gestures, one parameter each, with a pause between them. Record must only ever look at
        // the step that is still open -- never search back through older ones for a matching target --
        // or the third gesture below would merge into the first, which shares its path and address, and
        // the value the first gesture started from would be lost.
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var levelAgain), Is.True);
        var thirdGesture = Only(levelAgain!);
        Assert.That(thirdGesture.Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(thirdGesture.ValueToApply, Is.EqualTo("101"));

        Assert.That(journal.TryUndo(out var pan), Is.True);
        Assert.That(Only(pan!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"));

        Assert.That(journal.TryUndo(out var level), Is.True);
        var firstGesture = Only(level!);
        Assert.That(firstGesture.Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(firstGesture.ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void The_same_path_in_a_different_part_is_a_different_change()
    {
        // Every part's parameters share a path; only the address tells them apart. Two parts touched
        // inside one window are one step -- that is what a step being a gesture means -- but they must
        // stay two changes within it: folding them together on the path alone would keep only one of the
        // two OldValues and undo the wrong part, leaving the other where the gesture left it.
        var (journal, _) = NewJournal();
        journal.Record(new ParameterChange("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", "Studio Set Part/Part Level", "100", "101", false));
        journal.Record(new ParameterChange("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 2", "Studio Set Part/Part Level", "50", "51", false));

        Assert.That(journal.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Select(c => c.Offset2),
            Is.EqualTo(new[] { "Offset2/Studio Set Part 1", "Offset2/Studio Set Part 2" }));
        Assert.That(pending.Writes.Select(w => (w.Change.Offset2, w.ValueToApply)),
            Is.EqualTo(new[]
            {
                ("Offset2/Studio Set Part 1", "100"),
                ("Offset2/Studio Set Part 2", "50")
            }), "both parts go back, each to its own pre-gesture value");
        Assert.That(journal.CanUndo, Is.False);
    }

    [Test]
    public void A_new_edit_drops_the_redo_history()
    {
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);
        Assert.That(journal.CanRedo, Is.True);

        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.CanRedo, Is.False, "the redone future no longer follows from this history");
    }

    [Test]
    public void Nothing_is_recorded_while_a_step_is_being_applied()
    {
        // The write undo performs comes back through the same setters that record. Without this the
        // history would never empty.
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.ApplyAsync(() =>
        {
            journal.Record(Change("Studio Set Part/Part Level", "110", "100"));
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
            journal.Record(Change($"Studio Set Part/Parameter {i}", $"{i}", $"{i + 1}"));
            clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        }

        Assert.That(journal.TryUndo(out var firstPopped), Is.True);
        var newest = firstPopped!;
        Assert.That(Only(newest).Change.Path, Is.EqualTo("Studio Set Part/Parameter 249"),
            "the most recently recorded edit undoes first");

        var undone = 1;
        var lastPopped = newest;
        while (journal.TryUndo(out var pending))
        {
            lastPopped = pending;
            undone++;
        }

        Assert.That(undone, Is.EqualTo(EditJournal.Capacity));
        Assert.That(Only(lastPopped).Change.Path, Is.EqualTo("Studio Set Part/Parameter 50"),
            "the oldest surviving edit -- everything before it was dropped for capacity");
    }

    [Test]
    public void The_capacity_bound_counts_steps_not_changes()
    {
        // Why a step is a gesture. One drag on an envelope handle is hundreds of pointer moves, each
        // setting two parameters, so it used to record hundreds of steps -- which on its own exhausted
        // the bound and threw away everything the user had done before the drag. The edit made before
        // the drag below must survive it.
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);

        // 200 pointer moves, 1 ms apart, so the whole drag fits inside one coalesce window: 400
        // parameter changes, twice the bound.
        const int moves = 200;
        for (var i = 0; i < moves; i++)
        {
            journal.Record(Change("SuperNATURAL Acoustic Tone Common/TVA Level Velocity Sens", $"{i}", $"{i + 1}"));
            journal.Record(Change("SuperNATURAL Acoustic Tone Common/TVA Level Velocity Curve", $"{i}", $"{i + 1}"));
            clock.Now = clock.Now.AddMilliseconds(1);
        }

        Assert.That(journal.TryUndo(out var drag), Is.True);
        Assert.That(drag!.Step.Changes.Count, Is.EqualTo(2), "the whole drag is one step of two changes");

        Assert.That(journal.CanUndo, Is.True, "the edit made before the drag must not have been pushed out");
        Assert.That(journal.TryUndo(out var beforeTheDrag), Is.True);
        Assert.That(Only(beforeTheDrag!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "two steps in total, not 401");
    }

    [Test]
    public void Two_parameters_interleaved_inside_the_window_are_one_step()
    {
        // The real case, and the reason a step is a gesture rather than a parameter: one
        // MultiStageEnvelopeControl.OnPointerMoved sets a level from the pointer's Y and a time from its
        // X, so a drag records Level3, Time3, Level3, Time3, ... When coalescing also required the same
        // target, every record saw the other parameter on top and nothing ever merged.
        var (journal, clock) = NewJournal();
        for (var i = 0; i < 3; i++)
        {
            journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", $"{100 + i}", $"{101 + i}"));
            journal.Record(Change("PCM Synth Tone Partial/TVA Env Time 3", $"{50 + i}", $"{51 + i}"));
            clock.Now = clock.Now.AddMilliseconds(10);
        }

        Assert.That(journal.TryUndo(out var pending), Is.True);
        Assert.That(journal.CanUndo, Is.False, "one gesture, one step");
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(2), "a level and a time, not six steps");

        var level = pending.Step.Changes[0];
        Assert.That(level.Path, Is.EqualTo("PCM Synth Tone Partial/TVA Env Level 3"),
            "the changes are in the order the gesture first touched them");
        Assert.That(level.OldValue, Is.EqualTo("100"), "the level from before the gesture began");
        Assert.That(level.NewValue, Is.EqualTo("103"), "the level the gesture ended on");

        var time = pending.Step.Changes[1];
        Assert.That(time.Path, Is.EqualTo("PCM Synth Tone Partial/TVA Env Time 3"));
        Assert.That(time.OldValue, Is.EqualTo("50"), "each change keeps its own first OldValue");
        Assert.That(time.NewValue, Is.EqualTo("53"), "and its own latest NewValue");
    }

    [Test]
    public void Undoing_an_envelope_drag_puts_both_parameters_back()
    {
        // Undo has to write both halves of the drag or the handle does not return to where it was.
        var (journal, clock) = NewJournal();
        for (var i = 0; i < 3; i++)
        {
            journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", $"{100 + i}", $"{101 + i}"));
            journal.Record(Change("PCM Synth Tone Partial/TVA Env Time 3", $"{50 + i}", $"{51 + i}"));
            clock.Now = clock.Now.AddMilliseconds(10);
        }

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo!.Writes.Select(w => (w.Change.Path, w.ValueToApply)), Is.EqualTo(new[]
        {
            // Neither of these governs the other, so they are written in the order the gesture recorded
            // them -- the same order in both directions. See PendingEdit.Writes.
            ("PCM Synth Tone Partial/TVA Env Level 3", "100"),
            ("PCM Synth Tone Partial/TVA Env Time 3", "50")
        }), "both parameters go back to the values they held before the drag");

        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo!.Writes.Select(w => (w.Change.Path, w.ValueToApply)), Is.EqualTo(new[]
        {
            ("PCM Synth Tone Partial/TVA Env Level 3", "103"),
            ("PCM Synth Tone Partial/TVA Env Time 3", "53")
        }), "redo walks the same changes in the same order, to the values the drag ended on");
    }

    [Test]
    public void A_record_after_the_window_starts_a_new_step_even_for_a_parameter_already_in_the_last_one()
    {
        // Releasing the handle and dragging it again is a second gesture, and undo must take back only
        // the second drag -- even though it touches a parameter the first one already has.
        var (journal, clock) = NewJournal();
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", "100", "101"));
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Time 3", "50", "51"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", "101", "102"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(second!.Step.Changes.Count, Is.EqualTo(1), "the pause closed the first step");
        Assert.That(Only(second).ValueToApply, Is.EqualTo("101"),
            "back to where the second gesture started, not to where the first one did");

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(first!.Step.Changes.Count, Is.EqualTo(2));
        Assert.That(first.Writes.Select(w => w.ValueToApply), Is.EqualTo(new[] { "100", "50" }));
    }

    [Test]
    public void A_parameter_touched_twice_in_one_gesture_is_still_one_change()
    {
        // Coalescing within a step, not just into it: the level below is set twice, and undo needs one
        // write per parameter carrying the value from before the gesture -- not one write per record.
        var (journal, clock) = NewJournal();
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", "100", "101"));
        clock.Now = clock.Now.AddMilliseconds(10);
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Level 3", "101", "102"));
        clock.Now = clock.Now.AddMilliseconds(10);
        journal.Record(Change("PCM Synth Tone Partial/TVA Env Time 3", "50", "51"));

        Assert.That(journal.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(2), "two parameters were touched, so two changes");
        Assert.That(pending.Writes.Select(w => (w.Change.Path, w.ValueToApply)), Is.EqualTo(new[]
        {
            ("PCM Synth Tone Partial/TVA Env Level 3", "100"),
            ("PCM Synth Tone Partial/TVA Env Time 3", "50")
        }));
    }

    [Test]
    public void A_discriminator_is_written_first_even_when_the_gesture_touched_it_last()
    {
        // Reachable, not hypothetical: pick a chorus type from its combo, then move one of that type's
        // knobs inside 250 ms, and both land in one group -- with the knob recorded first and the
        // discriminator second. "Studio Set Common Chorus/Chorus Type" really is isparent:true, and the
        // Chorus Parameter slots below really do name it as their parent.
        //
        // A dependent's display value only converts to the right byte while the type holds the value that
        // dependent belongs to: DomainBase.WriteToIntegraAsync rebuilds the ParserContext from the
        // block's current values and skips a parameter that is not ValidInContext there. So the type has
        // to be written first in BOTH directions. Reversing on undo happens to get the undo half of this
        // right and the redo half wrong; recorded order gets both halves wrong. Only asking the change
        // whether it is a discriminator gets both right.
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Common Chorus/Chorus Parameter 1/Filter Type", "OFF", "LPF"));
        clock.Now = clock.Now.AddMilliseconds(20);
        journal.Record(Change("Studio Set Common Chorus/Chorus Type", "CHORUS", "GM2 CHORUS",
            isDiscriminator: true));
        clock.Now = clock.Now.AddMilliseconds(20);
        journal.Record(Change("Studio Set Common Chorus/Chorus Parameter 2/Chorus Cutoff Freq", "800", "1000"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(undo!.Step.Changes.Select(c => c.Path), Is.EqualTo(new[]
        {
            "Studio Set Common Chorus/Chorus Parameter 1/Filter Type",
            "Studio Set Common Chorus/Chorus Type",
            "Studio Set Common Chorus/Chorus Parameter 2/Chorus Cutoff Freq"
        }), "the step itself still records what the gesture touched, in the order it touched it");

        Assert.That(undo.Writes.Select(w => (w.Change.Path, w.ValueToApply)), Is.EqualTo(new[]
        {
            ("Studio Set Common Chorus/Chorus Type", "CHORUS"),
            ("Studio Set Common Chorus/Chorus Parameter 1/Filter Type", "OFF"),
            ("Studio Set Common Chorus/Chorus Parameter 2/Chorus Cutoff Freq", "800")
        }), "the discriminator goes back first; the rest follow in recorded order");

        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(redo!.Writes.Select(w => (w.Change.Path, w.ValueToApply)), Is.EqualTo(new[]
        {
            ("Studio Set Common Chorus/Chorus Type", "GM2 CHORUS"),
            ("Studio Set Common Chorus/Chorus Parameter 1/Filter Type", "LPF"),
            ("Studio Set Common Chorus/Chorus Parameter 2/Chorus Cutoff Freq", "1000")
        }), "and forward exactly the same way: the order is the dependency, never the direction");
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
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Level", "110", "120"));

        Assert.That(journal.TryUndo(out var undone), Is.True);
        Assert.That(Only(undone!).ValueToApply, Is.EqualTo("110"));

        // No time is advanced here: this is well within the coalesce window of the *second* Record
        // call above, which is exactly the stale timestamp the bug compares against.
        journal.Record(Change("Studio Set Part/Part Level", "110", "130"));

        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(Only(first!).ValueToApply, Is.EqualTo("110"), "the post-undo gesture is its own step");
        Assert.That(journal.CanUndo, Is.True, "the original 100->110 step must still be there");
        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(Only(second!).ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void A_gesture_holds_one_step_open_however_long_it_takes()
    {
        // The reported bug. RotaryKnobDial.Commit assigns Value only when the *snapped* value changes,
        // so a slow, precise drag -- one step per second, which is exactly how you set a value carefully
        // -- records once per second. On the clock alone every one of those is its own undo step, and
        // undoing walks back through every intermediate value. The control knows it is still dragging,
        // so it says so.
        var (journal, clock) = NewJournal();
        using (journal.BeginGesture())
        {
            journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
            for (var i = 1; i < 5; i++)
            {
                // Ten coalesce windows apart: nothing about the timing suggests one gesture.
                clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 10);
                journal.Record(Change("Studio Set Part/Part Level", $"{100 + i}", $"{101 + i}"));
            }
        }

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).ValueToApply, Is.EqualTo("100"),
            "back to the value from before the drag, not to the previous step of it");
        Assert.That(journal.CanUndo, Is.False, "one drag, one step");
        Assert.That(journal.TryRedo(out var redo), Is.True);
        Assert.That(Only(redo!).ValueToApply, Is.EqualTo("105"), "and forward to where the drag ended");
    }

    [Test]
    public void Closing_a_gesture_ends_its_step()
    {
        // Releasing the knob ends the step even though no time has passed: turning a second knob
        // immediately afterwards is a second edit, and undoing must take back only that one.
        var (journal, _) = NewJournal();
        using (journal.BeginGesture())
            journal.Record(Change("Studio Set Part/Part Level", "100", "110"));

        // No time advanced at all -- well inside CoalesceWindow of the record above.
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(Only(second!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"));
        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(Only(first!).Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(Only(first!).ValueToApply, Is.EqualTo("100"));
    }

    [Test]
    public void A_gesture_starts_its_own_step_even_when_an_edit_just_happened()
    {
        // The other half of the same rule: an edit made just before the drag began is not part of it.
        // Grabbing a handle is a new gesture whatever the clock says.
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        using (journal.BeginGesture())
            journal.Record(Change("Studio Set Part/Part Level", "100", "110"));

        Assert.That(journal.TryUndo(out var drag), Is.True);
        Assert.That(Only(drag!).Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(journal.CanUndo, Is.True, "the edit before the drag is still its own step");
        Assert.That(journal.TryUndo(out var before), Is.True);
        Assert.That(Only(before!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"));
    }

    [Test]
    public void An_inner_gesture_scope_closing_does_not_end_the_outer_one()
    {
        // Depth, not a flag: a control that nests scopes (or a handler that opens one while a drag is
        // already in progress) must not be able to close the drag's group early.
        var (journal, clock) = NewJournal();
        using (journal.BeginGesture())
        {
            journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
            using (journal.BeginGesture())
            {
                clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 4);
                journal.Record(Change("Studio Set Part/Part Level", "101", "102"));
            }
            clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 4);
            journal.Record(Change("Studio Set Part/Part Level", "102", "103"));
        }

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "all three records were inside the outer gesture");
    }

    [Test]
    public void Disposing_a_gesture_scope_twice_does_not_close_an_outer_one()
    {
        // A control ends its drag from both pointer-released and pointer-capture-lost, so the same scope
        // really does get disposed twice. That must not decrement the depth twice.
        var (journal, clock) = NewJournal();
        using var outer = journal.BeginGesture();
        var inner = journal.BeginGesture();
        inner.Dispose();
        inner.Dispose();

        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 4);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the outer gesture was still open for both records");
    }

    [Test]
    public void A_gesture_nobody_closed_stops_swallowing_edits_once_it_falls_silent()
    {
        // Containment, not correctness: a scope is held in a control's field across two event handlers,
        // so unlike the rest of the journal it can leak. If it does, the group must not stay open for the
        // rest of the session, folding every later edit into one unusable step. StaleGestureWindow is far
        // longer than a pause inside a real drag, so this only fires once the "gesture" has genuinely
        // stopped happening.
        var (journal, clock) = NewJournal();
        journal.BeginGesture();   // deliberately never disposed
        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 4);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));

        clock.Now = clock.Now.Add(EditJournal.StaleGestureWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out var later), Is.True);
        Assert.That(Only(later!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"),
            "the later edit is its own step, not folded into the abandoned gesture");
        Assert.That(journal.TryUndo(out var leaked), Is.True);
        Assert.That(Only(leaked!).ValueToApply, Is.EqualTo("100"),
            "and what the gesture did record is still one step of its own");
    }

    [Test]
    public void An_edit_gesture_closes_its_scope_only_once_however_often_the_drag_ends()
    {
        // What EditGesture adds over calling the journal directly. Every one of the eight draggable
        // controls ends its drag from both pointer-released and pointer-capture-lost, which for a drag
        // that ends normally means End() runs twice; the second must not decrement a depth it does not
        // own. The outer scope here stands in for whatever else might be open at the time.
        var (journal, clock) = NewJournal();
        using var outer = journal.BeginGesture();
        var holder = new EditGesture(journal);
        holder.Begin();
        holder.End();
        holder.End();

        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow * 4);
        journal.Record(Change("Studio Set Part/Part Level", "101", "102"));

        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.False, "the outer gesture was still open for both records");
    }

    [Test]
    public void An_edit_gesture_closes_a_scope_a_previous_press_left_open()
    {
        // The other half: if a press ever fails to see its release, the next press must not stack a
        // second scope on top of the abandoned one -- that is how a depth counter leaks upwards.
        var (journal, clock) = NewJournal();
        var holder = new EditGesture(journal);
        holder.Begin();
        holder.Begin();
        holder.End();

        journal.Record(Change("Studio Set Part/Part Level", "100", "101"));
        clock.Now = clock.Now.Add(EditJournal.CoalesceWindow).AddMilliseconds(1);
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out var second), Is.True);
        Assert.That(Only(second!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"),
            "no gesture is open, so the clock governs again");
        Assert.That(journal.TryUndo(out var first), Is.True);
        Assert.That(Only(first!).Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
    }

    [Test]
    public void An_edit_after_undoing_mid_gesture_does_not_reopen_the_step_the_undo_left_on_top()
    {
        // Same regression as An_edit_after_undo_does_not_coalesce_with_whatever_is_now_on_top, for the
        // gesture path: TryUndo changes what _undo[^1] is, so an open gesture's claim on it is stale and
        // must be dropped, or the next record would merge into a step this gesture never touched.
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        using (journal.BeginGesture())
        {
            journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
            Assert.That(journal.TryUndo(out var undone), Is.True);
            Assert.That(Only(undone!).ValueToApply, Is.EqualTo("100"));

            journal.Record(Change("Studio Set Part/Part Level", "100", "120"));
        }

        Assert.That(journal.TryUndo(out var afterTheUndo), Is.True);
        Assert.That(Only(afterTheUndo!).ValueToApply, Is.EqualTo("100"));
        Assert.That(journal.CanUndo, Is.True, "the pan edit must not have been swallowed");
        Assert.That(journal.TryUndo(out var pan), Is.True);
        Assert.That(Only(pan!).Change.Path, Is.EqualTo("Studio Set Part/Part Pan"));
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
        //
        // The clock never advances, so every one of these lands in one step -- which makes a lost
        // update easy to see: each record names a different parameter, so all of them must survive as
        // their own change within that step, and none may be folded into another.
        var (journal, _) = NewJournal();
        const int threads = 16;
        const int perThread = 50;

        Assert.DoesNotThrow(() => Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
                journal.Record(Change($"Studio Set Part/Parameter {t}-{i}", "0", "1"));
        }));

        Assert.That(journal.TryUndo(out var pending), Is.True);
        Assert.That(journal.CanUndo, Is.False, "no time passed, so it is all one gesture");
        Assert.That(pending!.Step.Changes.Count, Is.EqualTo(threads * perThread),
            "every recorded change must survive, with none lost to a race");
        Assert.That(pending.Step.Changes.Select(c => c.Path).Distinct().Count(),
            Is.EqualTo(threads * perThread), "and none merged into another");
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
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        journal.TryUndo(out _);

        journal.Clear();

        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.CanRedo, Is.False);
    }

    [Test]
    public void Nothing_is_recorded_while_comparing()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        Assert.That(journal.TryBeginCompareToggle(out var enter), Is.True);
        journal.CommitCompareToggle(enter!);

        clock.Now += EditJournal.CoalesceWindow * 2;
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.CanUndo, Is.False,
            "coming back overwrites what was edited while comparing, so recording it would put a step in " +
            "the history that describes a value the instrument never keeps");
    }

    [Test]
    public void A_history_that_lost_its_oldest_steps_says_so()
    {
        var (journal, clock) = NewJournal();
        for (var i = 0; i <= EditJournal.Capacity; i++)
        {
            clock.Now += EditJournal.CoalesceWindow * 2;
            journal.Record(Change("Studio Set Part/Part Level", i.ToString(), (i + 1).ToString()));
        }

        Assert.That(journal.HistoryTruncated, Is.True,
            "one more step than the capacity evicted the oldest, so the original is no longer complete");

        journal.Clear();
        Assert.That(journal.HistoryTruncated, Is.False, "a cleared history has lost nothing");
    }

    [Test]
    public void Redo_is_refused_while_comparing()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        clock.Now += EditJournal.CoalesceWindow * 2;
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out _), Is.True); // the Pan step is on the redo side now
        Assert.That(journal.TryBeginCompareToggle(out var enter), Is.True);
        journal.CommitCompareToggle(enter!);

        Assert.That(journal.CanRedo, Is.False,
            "the redo side belongs to the edited sound, and that is not what is playing");
        Assert.That(journal.TryRedo(out _), Is.False);
        Assert.That(journal.CanUndo, Is.False);
        Assert.That(journal.TryUndo(out _), Is.False);
    }

    [Test]
    public void A_comparison_leaves_an_undone_step_where_it_was()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        clock.Now += EditJournal.CoalesceWindow * 2;
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));

        Assert.That(journal.TryUndo(out _), Is.True);
        Assert.That(journal.TryBeginCompareToggle(out var enter), Is.True);
        journal.CommitCompareToggle(enter!);
        Assert.That(journal.TryBeginCompareToggle(out var exit), Is.True);
        journal.CommitCompareToggle(exit!);

        Assert.That(journal.CanRedo, Is.True, "the redo side is reachable again once the edits are back");
        Assert.That(journal.TryUndo(out var undo), Is.True);
        Assert.That(Only(undo!).Change.Path, Is.EqualTo("Studio Set Part/Part Level"));
        Assert.That(journal.CanUndo, Is.False,
            "the step undone before the comparison is still on the redo side, not back under the history");
    }

    [Test]
    public void A_toggle_is_refused_once_the_history_has_been_cleared()
    {
        var (journal, _) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        Assert.That(journal.TryBeginCompareToggle(out var toggle), Is.True);

        // A preset change, or a Studio Set change from the front panel, arriving between the press and the
        // writes landing.
        journal.Clear();
        journal.CommitCompareToggle(toggle!);

        Assert.That(journal.IsComparing, Is.False,
            "the buffer would hold steps belonging to a patch that is no longer loaded");
        Assert.That(journal.CanCompare, Is.False);
    }

    [Test]
    public void A_toggle_is_refused_once_another_edit_has_been_recorded()
    {
        var (journal, clock) = NewJournal();
        journal.Record(Change("Studio Set Part/Part Level", "100", "110"));
        Assert.That(journal.TryBeginCompareToggle(out var toggle), Is.True);

        // An edit made while Compare waits for the wire: recording is only suppressed over the writes
        // themselves, and waiting for the lease happens before them.
        clock.Now += EditJournal.CoalesceWindow * 2;
        journal.Record(Change("Studio Set Part/Part Pan", "0", "10"));
        journal.CommitCompareToggle(toggle!);

        Assert.That(journal.IsComparing, Is.False,
            "the press is abandoned rather than committed against a history it no longer describes");
        Assert.That(journal.CanUndo, Is.True, "and nothing was consumed, so pressing Compare again retries");
    }
}
