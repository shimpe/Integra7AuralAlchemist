using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreMidi;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Integra7Api's device methods as conversations. A preset change is the reason the port
/// exists: its three messages used to go out with no lock at all, so another flow's request could land
/// between the bank select and the program change.</summary>
[TestFixture]
public class TestIntegra7ApiConversations
{
    /// <summary>A port that records what was asked of it, so a test can see how many conversations a
    /// method opened and what went out inside each.</summary>
    private sealed class RecordingPort : IMidiPort
    {
        public List<string> Conversations { get; } = [];
        public List<byte[]> Sent { get; } = [];
        public bool Connected { get; set; } = true;

        public bool ConnectionOk() => Connected;

        public Task<IMidiLease> AcquireAsync(string what)
        {
            Conversations.Add(what);
            return Task.FromResult<IMidiLease>(new RecordingLease(this));
        }

        private sealed class RecordingLease(RecordingPort port) : IMidiLease
        {
            public Task SendAsync(byte[] data)
            {
                port.Sent.Add(data);
                return Task.CompletedTask;
            }

            public Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected) =>
                Task.FromResult<byte[]>([]);

            public Task<byte[]> ReadNextAsync(IReplyMatcher expected) => Task.FromResult<byte[]>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task APresetChangeIsOneConversation()
    {
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.ChangePresetAsync(3, 87, 0, 5);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "preset change" }),
            "all three messages must go out under one lease, or another flow can land between them");
        Assert.That(port.Sent, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task APresetChangeSendsBankSelectThenProgramChangeInThatOrder()
    {
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.ChangePresetAsync(3, 87, 2, 5);

        // CC on channel 3, controller 0 (bank MSB), then controller 0x20 (bank LSB), then program.
        Assert.That(port.Sent[0], Is.EqualTo(new byte[] { 0xb3, 0x00, 87 }));
        Assert.That(port.Sent[1], Is.EqualTo(new byte[] { 0xb3, 0x20, 2 }));
        Assert.That(port.Sent[2], Is.EqualTo(new byte[] { 0xc3, 4 }), "the program change is zero-based");
    }

    [Test]
    public void ABankNumberTheDeviceCannotHaveIsRefusedRatherThanSkipped()
    {
        // Skipping the bank select and sending the rest would select some other patch and say nothing
        // about it.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        Assert.That(async () => await api.ChangePresetAsync(0, 1, 0, 5), Throws.TypeOf<MidiException>());
        Assert.That(async () => await api.ChangePresetAsync(0, 87, 200, 5), Throws.TypeOf<MidiException>());
    }

    [Test]
    public async Task AllNotesOffIsOneConversationForAllSixteenParts()
    {
        // Each send used to acquire separately, so a read could be serviced between the messages for
        // parts 3 and 4.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.AllNotesOffAsync();

        Assert.That(port.Conversations, Is.EqualTo(new[] { "all notes off" }));
        Assert.That(port.Sent, Has.Count.EqualTo(16));
    }

    [Test]
    public void AnUnpluggedDeviceIsReportedEvenAfterAGoodIdentityCheck()
    {
        // The port answers from the output handle, which drops its details when a send fails -- so a
        // cable pulled mid-session shows up without anyone re-running the identity check.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        Assert.That(api.ConnectionOk(), Is.True);

        port.Connected = false;

        Assert.That(api.ConnectionOk(), Is.False);
    }
}
