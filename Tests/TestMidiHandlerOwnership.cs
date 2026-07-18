using System;
using System.Collections.Generic;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The MIDI port has a single handler slot. These pin the rule that keeps request and reply
/// paired: a reader may only hand the port back while it still owns it.</summary>
[TestFixture]
public class TestMidiHandlerOwnership
{
    /// <summary>Stands in for MidiIn, modelling just the handler slot and who currently receives.</summary>
    private sealed class FakeMidiIn : IMidiIn
    {
        private readonly EventHandler<MidiReceivedEventArgs> _default;
        public EventHandler<MidiReceivedEventArgs> Installed { get; private set; }
        public List<string> Warnings { get; } = [];

        public FakeMidiIn()
        {
            _default = (_, _) => { };
            Installed = _default;
        }

        public bool DefaultIsInstalled => ReferenceEquals(Installed, _default);

        public void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler)
        {
            if (!Equals(Installed, _default)) Warnings.Add("installed over another reader");
            Installed = handler;
        }

        public void ConfigureDefaultHandler() => Installed = _default;

        public void RemoveHandler(EventHandler<MidiReceivedEventArgs> handler)
        {
            // Uses the same decision the real MidiIn.RemoveHandler uses, so this exercises shipped
            // logic rather than a copy of it.
            if (!MidiHandlerOwnership.MayRestoreDefault(Installed, handler))
            {
                // Mirrors MidiIn: only a *different reader* holding the port is noteworthy; the port
                // already being back on the default handler is the ordinary double-hand-back.
                if (!DefaultIsInstalled) Warnings.Add("refused to detach another reader");
                return;
            }

            ConfigureDefaultHandler();
        }

        public byte[] GetReply() => [];
        public void AnnounceIntentionToManuallyHandleReply() { }
        public void RestoreAutomaticHandling() { }
    }

    [Test]
    public void AReaderThatFinishesLateDoesNotDetachTheReaderThatFollowedIt()
    {
        var midi = new FakeMidiIn();
        EventHandler<MidiReceivedEventArgs> readerA = (_, _) => { };
        EventHandler<MidiReceivedEventArgs> readerB = (_, _) => { };

        midi.ConfigureHandler(readerA);
        midi.ConfigureHandler(readerB);   // B takes over while A is still in flight

        // A finishes last and tries to hand the port back.
        midi.RemoveHandler(readerA);

        Assert.That(midi.Installed, Is.EqualTo(readerB), "B must still be receiving its reply");
        Assert.That(midi.Warnings, Does.Contain("refused to detach another reader"));
    }

    [Test]
    public void TheOwningReaderRestoresTheDefaultHandler()
    {
        var midi = new FakeMidiIn();
        EventHandler<MidiReceivedEventArgs> reader = (_, _) => { };

        midi.ConfigureHandler(reader);
        Assert.That(midi.DefaultIsInstalled, Is.False);

        midi.RemoveHandler(reader);
        Assert.That(midi.DefaultIsInstalled, Is.True, "the port must go back to unsolicited-message handling");
    }

    [Test]
    public void RemovingTwiceIsHarmless()
    {
        var midi = new FakeMidiIn();
        EventHandler<MidiReceivedEventArgs> reader = (_, _) => { };

        midi.ConfigureHandler(reader);
        midi.RemoveHandler(reader);
        midi.RemoveHandler(reader);   // the read hands the port back, then its caller's cleanup asks again

        Assert.That(midi.DefaultIsInstalled, Is.True);
        Assert.That(midi.Warnings, Is.Empty,
            "handing back a port that is already handed back is routine, not a conflict worth warning about");
    }

    [Test]
    public void OverlappingReadersAreReported()
    {
        // Overlap is what the MIDI semaphore is supposed to prevent; if it happens we want it in the log.
        var midi = new FakeMidiIn();
        EventHandler<MidiReceivedEventArgs> readerA = (_, _) => { };
        EventHandler<MidiReceivedEventArgs> readerB = (_, _) => { };

        midi.ConfigureHandler(readerA);
        midi.ConfigureHandler(readerB);

        Assert.That(midi.Warnings, Does.Contain("installed over another reader"));
    }

    [Test]
    public void OwnershipRuleItself()
    {
        object a = new(), b = new();
        Assert.That(MidiHandlerOwnership.MayRestoreDefault(a, a), Is.True);
        Assert.That(MidiHandlerOwnership.MayRestoreDefault(a, b), Is.False, "a different reader owns it");
        Assert.That(MidiHandlerOwnership.MayRestoreDefault(a, null), Is.False);
        Assert.That(MidiHandlerOwnership.MayRestoreDefault(null, a), Is.False);
    }

    [Test]
    public void SequentialReadersEachGetTheirOwnTurn()
    {
        var midi = new FakeMidiIn();
        EventHandler<MidiReceivedEventArgs> first = (_, _) => { };
        EventHandler<MidiReceivedEventArgs> second = (_, _) => { };

        midi.ConfigureHandler(first);
        midi.RemoveHandler(first);
        midi.ConfigureHandler(second);
        midi.RemoveHandler(second);

        Assert.That(midi.DefaultIsInstalled, Is.True);
        Assert.That(midi.Warnings, Is.Empty, "no overlap, so nothing to warn about");
    }
}
