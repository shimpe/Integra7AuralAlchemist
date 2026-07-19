using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Commons.Music.Midi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One object owns the port. A conversation holds it for its whole duration, so a sequence
/// of messages that must arrive together cannot be split by another flow's request.</summary>
[TestFixture]
public class TestMidiPort
{
    private sealed class FakeMidiOut : IMidiOut
    {
        public List<byte[]> Sent { get; } = [];
        public bool ConnectionOk() => true;
        public void SafeSend(byte[] data) => Sent.Add(data);
    }

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

    private static MidiPort NewPort(out FakeMidiIn midiIn, out FakeMidiOut midiOut)
    {
        midiIn = new FakeMidiIn();
        midiOut = new FakeMidiOut();
        return new MidiPort(midiOut, midiIn);
    }

    [Test]
    public async Task AConversationGetsThePortToItself()
    {
        var port = NewPort(out _, out var midiOut);

        await using (var lease = await port.AcquireAsync("first"))
        {
            await lease.SendAsync([0x01]);
            await lease.SendAsync([0x02]);
        }

        Assert.That(midiOut.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ASecondConversationWaitsForTheFirstToFinish()
    {
        var port = NewPort(out _, out _);

        var first = await port.AcquireAsync("first");
        var second = port.AcquireAsync("second");

        Assert.That(second.IsCompleted, Is.False, "the port is taken; the second acquire must wait");

        await first.DisposeAsync();

        Assert.That(await Task.WhenAny(second, Task.Delay(3000)), Is.SameAs(second),
            "releasing the port must let the waiting conversation proceed");
        await (await second).DisposeAsync();
    }

    [Test]
    public async Task ConversationsRunOneAtATime()
    {
        // Two conversations racing: whatever order they get the port in, their sends must not
        // interleave. This is the whole point -- a bank select and its program change must arrive
        // together.
        var port = NewPort(out _, out var midiOut);

        async Task Converse(string what, byte tag)
        {
            await using var lease = await port.AcquireAsync(what);
            await lease.SendAsync([tag, 1]);
            await Task.Delay(20);
            await lease.SendAsync([tag, 2]);
        }

        await Task.WhenAll(Converse("a", 0xa0), Converse("b", 0xb0));

        Assert.That(midiOut.Sent, Has.Count.EqualTo(4));
        Assert.That(midiOut.Sent[0][0], Is.EqualTo(midiOut.Sent[1][0]),
            "the first conversation's two sends must be adjacent, not interleaved");
        Assert.That(midiOut.Sent[2][0], Is.EqualTo(midiOut.Sent[3][0]));
    }

    [Test]
    public async Task AConversationThatThrowsStillReleasesThePort()
    {
        var port = NewPort(out _, out _);

        try
        {
            await using var lease = await port.AcquireAsync("throws");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
        }

        var next = port.AcquireAsync("next");
        Assert.That(await Task.WhenAny(next, Task.Delay(3000)), Is.SameAs(next));
        await (await next).DisposeAsync();
    }

    [Test]
    public async Task AReleasedLeaseCannotBeUsed()
    {
        // A fire-and-forget task started inside a conversation can outlive it. Throwing makes that
        // loud instead of letting it write onto a port someone else now owns.
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("done");
        await lease.DisposeAsync();

        Assert.That(async () => await lease.SendAsync([0x01]), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task DisposingTwiceIsHarmless()
    {
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("first");
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        // A second dispose must not release the gate again -- that would let two conversations in.
        var a = await port.AcquireAsync("a");
        var b = port.AcquireAsync("b");

        Assert.That(b.IsCompleted, Is.False, "the gate was released twice; two conversations got in");

        await a.DisposeAsync();
        await (await b).DisposeAsync();
    }

    [Test]
    public async Task SendsKeepTheirOrderEvenWhenTheCallerForgetsToAwait()
    {
        // The port exists so a sequence that must arrive together cannot be split. That guarantee
        // must not rest on every caller remembering to await -- one missed await is a warning, not
        // an error.
        var port = NewPort(out _, out var midiOut);

        await using (var lease = await port.AcquireAsync("preset change"))
        {
            _ = lease.SendAsync([0xb0, 0x00, 0x55]);
            _ = lease.SendAsync([0xb0, 0x20, 0x00]);
            await lease.SendAsync([0xc0, 0x07]);
        }

        Assert.That(midiOut.Sent.Select(m => m[0]), Is.EqualTo(new byte[] { 0xb0, 0xb0, 0xc0 }));
        Assert.That(midiOut.Sent[0][1], Is.EqualTo(0x00));
        Assert.That(midiOut.Sent[1][1], Is.EqualTo(0x20));
    }

    [Test]
    public async Task ThePortIsNotHandedOnWhileASendIsStillInFlight()
    {
        var port = NewPort(out _, out var midiOut);

        var lease = await port.AcquireAsync("first");
        _ = lease.SendAsync([0x01]);
        await lease.DisposeAsync();

        Assert.That(midiOut.Sent, Has.Count.EqualTo(1),
            "disposal must wait for the send it queued, or it can land inside the next conversation");
    }

    [Test]
    public async Task DisposingFromTwoThreadsAtOnceReleasesThePortOnce()
    {
        var port = NewPort(out _, out _);

        var lease = await port.AcquireAsync("first");
        await Task.WhenAll(
            Task.Run(async () => await lease.DisposeAsync()),
            Task.Run(async () => await lease.DisposeAsync()));

        // If the gate was released twice its count is 2, and two conversations get in at once.
        var a = await port.AcquireAsync("a");
        var b = port.AcquireAsync("b");

        Assert.That(b.IsCompleted, Is.False, "the gate was released twice");

        await a.DisposeAsync();
        await (await b).DisposeAsync();
    }
}
