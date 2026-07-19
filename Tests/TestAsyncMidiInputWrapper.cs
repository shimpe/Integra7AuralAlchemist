using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A read must return its own reply and keep everything else, rather than returning whichever
/// message happened to arrive first. The messages it keeps are real device traffic -- a front-panel
/// preset change, most often -- and losing them is the bug this exists to fix.</summary>
[TestFixture]
public class TestAsyncMidiInputWrapper
{
    /// <summary>A MIDI input the test drives directly: Push delivers a message to whatever handler the
    /// wrapper installed.</summary>
    private sealed class FakeMidiIn : IMidiIn
    {
        private EventHandler<MidiReceivedEventArgs>? _handler;

        public List<byte[]> Dispatched { get; } = [];
        public bool HandlerInstalled => _handler is not null;

        public void Push(byte[] message) =>
            _handler?.Invoke(this, new MidiReceivedEventArgs
            {
                Data = message, Start = 0, Length = message.Length, Timestamp = 0
            });

        public void ConfigureHandler(EventHandler<MidiReceivedEventArgs> handler) => _handler = handler;
        public void ConfigureDefaultHandler() => _handler = null;
        public void RemoveHandler(EventHandler<MidiReceivedEventArgs> handler) => _handler = null;
        public void DispatchUnsolicited(byte[] message) => Dispatched.Add(message);
        public byte[] GetReply() => [];
        public void AnnounceIntentionToManuallyHandleReply() { }
        public void RestoreAutomaticHandling() { }
    }

    private static readonly byte[] Address = [0x0f, 0x00, 0x04, 0x02];

    private static byte[] Reply() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x00, 0x6b, 0xf7
    ];

    // A preset change the device sends unprompted -- shorter, and at a different address.
    private static byte[] PanelChange() =>
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x03, 0x02, 0x01, 0x0e, 0xf7
    ];

    [Test]
    public void TheReplyIsReturnedAndNothingIsDeferred()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True, "the read did not complete");
        Assert.That(read.Result, Is.EqualTo(Reply()));
        Assert.That(mi.TakeDeferred(), Is.Empty);
    }

    [Test]
    public void AMessageThatIsNotTheReplyIsKeptAndTheReadKeepsWaiting()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True, "the read did not complete");
        Assert.That(read.Result, Is.EqualTo(Reply()),
            "the panel change must not be returned as the reply -- that is the defect");
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { PanelChange() }));
    }

    [Test]
    public void MessagesAreKeptInArrivalOrder()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        byte[] first = [0xc0, 0x05];
        byte[] second = [0xb0, 0x00, 0x55];
        midi.Push(first);
        midi.Push(second);
        midi.Push(Reply());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void ATimeoutStillKeepsWhatArrived()
    {
        // The read gets nothing it wants, but the panel change is real traffic and must survive.
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());

        Assert.That(read.Wait(TimeSpan.FromSeconds(4)), Is.True, "the read should have timed out by now");
        Assert.That(read.Result, Is.Empty, "a timeout returns no reply");
        Assert.That(mi.TakeDeferred(), Is.EqualTo(new[] { PanelChange() }));
    }

    [Test]
    public void TakingTheDeferredMessagesClearsThem()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(PanelChange());
        midi.Push(Reply());
        read.Wait(TimeSpan.FromSeconds(3));

        Assert.That(mi.TakeDeferred(), Has.Count.EqualTo(1));
        Assert.That(mi.TakeDeferred(), Is.Empty, "a second drain must not deliver the same message twice");
    }

    [Test]
    public void AChunkCarryingTheReplyAmongOtherMessagesIsAccepted()
    {
        // One MIDI event can carry several concatenated sysex messages. If any of them is the reply,
        // the chunk is what the parsers downstream already expect to receive.
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));

        var combined = new List<byte>();
        combined.AddRange(PanelChange());
        combined.AddRange(Reply());

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(combined.ToArray());

        Assert.That(read.Wait(TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(read.Result, Is.EqualTo(combined.ToArray()));
        Assert.That(mi.TakeDeferred(), Is.Empty);
    }

    [Test]
    public void TheHandlerIsHandedBackWhenTheReadCompletes()
    {
        var midi = new FakeMidiIn();
        var mi = new AsyncMidiInputWrapper(midi, ReplyMatchers.DataSetAt(Address));
        Assert.That(midi.HandlerInstalled, Is.True);

        var read = mi.WaitForMidiMessageAsync();
        midi.Push(Reply());
        read.Wait(TimeSpan.FromSeconds(3));

        Assert.That(midi.HandlerInstalled, Is.False);
    }
}
