using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CoreMidi;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;
using Microsoft.Reactive.Testing;

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
        private int _open;

        public List<string> Conversations { get; } = [];
        public List<byte[]> Sent { get; } = [];
        public bool Connected { get; set; } = true;

        /// <summary>The most leases held at once. The real port would block on the second one and
        /// throw a minute later; this counts instead, so a nested acquire fails a test in
        /// milliseconds rather than hanging hardware.</summary>
        public int MostLeasesHeldAtOnce { get; private set; }

        public bool ConnectionOk() => Connected;

        public Task<IMidiLease> AcquireAsync(string what)
        {
            Conversations.Add(what);
            _open++;
            if (_open > MostLeasesHeldAtOnce) MostLeasesHeldAtOnce = _open;
            return Task.FromResult<IMidiLease>(new RecordingLease(this));
        }

        private sealed class RecordingLease(RecordingPort port) : IMidiLease
        {
            private bool _released;

            public Task SendAsync(byte[] data)
            {
                ThrowIfReleased();
                port.Sent.Add(data);
                return Task.CompletedTask;
            }

            public Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected)
            {
                ThrowIfReleased();
                return Task.FromResult<byte[]>([]);
            }

            public Task<byte[]> ReadNextAsync(IReplyMatcher expected)
            {
                ThrowIfReleased();
                return Task.FromResult<byte[]>([]);
            }

            /// <summary>Mirrors the real lease, which throws on use after release. A fake that quietly
            /// accepted a send on a released lease would let a call that wrongly disposes a lease it
            /// only borrowed pass every assertion here, while failing on hardware.</summary>
            private void ThrowIfReleased()
            {
                if (_released)
                    throw new ObjectDisposedException(nameof(IMidiLease),
                        "This lease was already released.");
            }

            public ValueTask DisposeAsync()
            {
                if (!_released)
                {
                    _released = true;
                    port._open--;
                }

                return ValueTask.CompletedTask;
            }
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
    public async Task AllNotesOffSendsAControlChangeAndNotGarbage()
    {
        // It used to build [123 + channel, 124, 0], putting the controller number where the status
        // byte belongs. A status byte below 0x80 is not a MIDI message, so WinMM rejected all sixteen
        // and Panic never reached the device -- silently, because the send failure was never logged.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.AllNotesOffAsync();

        for (var channel = 0; channel < 16; channel++)
        {
            Assert.That(port.Sent[channel], Is.EqualTo(new byte[] { (byte)(0xb0 + channel), 123, 0 }),
                $"channel {channel}");
            Assert.That(port.Sent[channel][0], Is.GreaterThanOrEqualTo(0x80),
                "a status byte below 0x80 is not a MIDI message at all");
        }
    }

    [Test]
    public async Task WritingAToneToUserMemoryIsOneConversation()
    {
        // Step 1 selects the new patch on the device, so a read landing between steps 1 and 3 reads
        // the new patch, and a preset change there writes the wrong name to the wrong slot. This used
        // to be several conversations because there was no way to pass a lease through the domain
        // layer, and holding one across the steps would have deadlocked.
        var port = new RecordingPort();
        var api = new Integra7Api(port);
        var domain = new Integra7Domain(api, new Integra7StartAddresses(), LoadParameters());

        await api.WriteToneToUserMemory(domain, "SN-S", 0, "TEST NAME", 0);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "write tone to user memory" }));
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1));
    }

    private static Integra7Parameters LoadParameters()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin");
        return new Integra7Parameters(File.OpenRead(path));
    }

    [Test]
    public void AParentParameterEditIsOneConversationCoveringWriteResetAndReread()
    {
        // SynthParam's Enqueue bodies (ParamInt/ParamString/ParamBool) write the edited value, then --
        // for a parent parameter -- run WaveOutOfRangeReset and re-read the domain, all under one
        // lease. A lease dropped from any of those calls would not deadlock a real device today (the
        // real port would block for 60s and throw); here it shows up immediately as a second
        // conversation or two leases held at once. "Studio Set Common Chorus/Chorus Type" is used
        // because it is a real isparent:true parameter that is NOT a wave-group discriminator, so
        // WaveOutOfRangeReset.ApplyAsync is a genuine no-op for it and the test does not need
        // WaveformBanks.Default (which requires a running Avalonia application to load its CSV assets
        // and is unavailable in this headless test host).
        var port = new RecordingPort();
        var api = new Integra7Api(port);
        var domain = new Integra7Domain(api, new Integra7StartAddresses(), LoadParameters());
        var chorusDomain = domain.StudioSetCommonChorus;
        var chorusType = chorusDomain.GetRelevantParameters(true, true)
            .Single(p => p.ParSpec.Path == "Studio Set Common Chorus/Chorus Type");
        Assert.That(chorusType.ParSpec.IsParent, Is.True,
            "the test needs a real parent parameter to exercise the reset-and-re-read branch");

        var scheduler = new TestScheduler();
        using var writer = new ThrottledParameterWriter(250, scheduler);
        var sut = new ParamString(chorusDomain, chorusType, writer);

        sut.Value = "Chorus"; // was "" (the FQP's unparsed default) -- a genuine change, so it enqueues

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(251).Ticks);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "edit Studio Set Common Chorus/Chorus Type" }),
            "the write, the wave-group reset, and the re-read must all run under one lease");
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1));
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

    [Test]
    public async Task ACallGivenALeaseDoesNotReleaseIt()
    {
        // The lease belongs to the conversation that opened it and has to outlive the call. Releasing
        // it here would free the port in the middle of a sequence, which is the whole thing this
        // exists to prevent.
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await using var conversation = await api.BeginConversationAsync("a sequence");
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x01], conversation);
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x02], conversation);

        Assert.That(port.Conversations, Is.EqualTo(new[] { "a sequence" }),
            "the two writes must join the conversation, not open their own");
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1));
        Assert.That(port.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ACallGivenNoLeaseAcquiresAndReleasesItsOwn()
    {
        var port = new RecordingPort();
        var api = new Integra7Api(port);

        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x01]);
        await api.MakeDataTransmissionAsync([0x0f, 0x00, 0x04, 0x02], [0x02]);

        Assert.That(port.Conversations, Has.Count.EqualTo(2), "each call is its own conversation");
        Assert.That(port.MostLeasesHeldAtOnce, Is.EqualTo(1), "and neither outlives its call");
    }
}
