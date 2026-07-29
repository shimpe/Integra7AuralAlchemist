using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What a running audition is holding, and the four things that can happen to it.
///
/// These are transitions rather than arithmetic, and every one of them is a way to lose a user's sound: a
/// start that forgets what was there, a switch that overwrites the memory, a stop that gives back the wrong
/// thing. That is why they are a record with tests rather than three fields on a view model.</summary>
public class AuditionStateTests
{
    private static Integra7Snapshot Tone(string name) =>
        new(Integra7Snapshot.CurrentFormatVersion, name, [], SnapshotKinds.Tone, "SN-S");

    [Test]
    public void Nothing_is_borrowed_to_begin_with()
    {
        Assert.That(AuditionState.Idle.IsRunning, Is.False);
    }

    [Test]
    public void Starting_remembers_the_part_its_engine_and_what_was_on_it()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\Warm Rhodes.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.ZeroBasedPartNo, Is.EqualTo(2));
            Assert.That(state.ToneType, Is.EqualTo("SN-S"));
            Assert.That(state.Borrowed!.Name, Is.EqualTo("what was there"));
        });
    }

    /// <summary>The rule the whole feature rests on. Browsing ten patches must still give back the one
    /// sound that was there before the first of them, so a second candidate replaces what is playing and
    /// never what is remembered.</summary>
    [Test]
    public void Switching_candidate_keeps_the_original_memory_and_the_engine()
    {
        var state = AuditionState.Idle
            .Start(2, "SN-S", Tone("what was there"), @"C:\lib\Warm Rhodes.json")
            .Switch(@"C:\lib\Glass Bell.json")
            .Switch(@"C:\lib\Old Pad.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.Borrowed!.Name, Is.EqualTo("what was there"));
            Assert.That(state.ZeroBasedPartNo, Is.EqualTo(2));
            Assert.That(state.ToneType, Is.EqualTo("SN-S"),
                "the engine is the part's, so a later candidate has something to be checked against");
            Assert.That(state.IsPlaying(@"C:\lib\Old Pad.json"), Is.True);
        });
    }

    /// <summary>Which row the panel offers Stop on. By path, because two library files can hold tones of
    /// the same name and a name comparison would put Stop on the wrong row.</summary>
    [Test]
    public void The_playing_file_is_recognised_by_path_whatever_its_case()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("x"), @"C:\lib\Warm Rhodes.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.IsPlaying(@"c:\LIB\warm rhodes.json"), Is.True);
            Assert.That(state.IsPlaying(@"C:\lib\Other.json"), Is.False);
            Assert.That(AuditionState.Idle.IsPlaying(@"C:\lib\Warm Rhodes.json"), Is.False,
                "and nothing is playing when nothing is running");
        });
    }

    /// <summary>Switching without a session is not a session. It cannot happen through the user interface,
    /// which only offers Stop while one is running -- and a state machine that quietly invented a session
    /// with nothing remembered would give back nothing on Stop.</summary>
    [Test]
    public void Switching_with_nothing_running_stays_idle()
    {
        Assert.That(AuditionState.Idle.Switch(@"C:\lib\Glass Bell.json").IsRunning, Is.False);
    }

    [Test]
    public void Stopping_gives_up_what_it_was_holding()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\a.json");

        Assert.Multiple(() =>
        {
            Assert.That(state.Stop().IsRunning, Is.False);
            Assert.That(state.Stop().Borrowed, Is.Null);
        });
    }

    [Test]
    public void Stopping_when_nothing_is_running_is_harmless()
    {
        Assert.That(AuditionState.Idle.Stop().IsRunning, Is.False);
    }

    /// <summary>A restore that failed must leave the session intact so Stop can be pressed again -- the
    /// instrument is still holding the candidate, and forgetting the memory would strand it there.</summary>
    [Test]
    public void A_state_that_could_not_be_given_back_is_still_running()
    {
        var state = AuditionState.Idle.Start(2, "SN-S", Tone("what was there"), @"C:\lib\a.json");

        Assert.That(state.IsRunning, Is.True, "Stop is the caller's to retry; the state itself is unchanged");
    }
}
