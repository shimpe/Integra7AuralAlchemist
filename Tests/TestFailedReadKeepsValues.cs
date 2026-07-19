using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A read that gets no reply must leave the previous values alone. Copying the unparsed
/// placeholders that a failed read leaves behind puts blanks on screen that look like real readings.</summary>
[TestFixture]
public class TestFailedReadKeepsValues
{
    /// <summary>An Integra7Api that never answers a data request, i.e. every read times out.</summary>
    private sealed class SilentApi : IIntegra7Api
    {
        public int Requests { get; private set; }

        public Task<byte[]> MakeDataRequestAsync(byte[] address, long size, IMidiLease? lease = null)
        {
            Requests++;
            return Task.FromResult(Array.Empty<byte>());   // what MakeDataRequestAsync returns on timeout
        }

        // Nothing below is exercised by these tests; only MakeDataRequestAsync matters.
        public bool ConnectionOk() => true;
        public byte DeviceId() => 0x10;
        public Task CheckIdentityAsync() => Task.CompletedTask;
        public Task MakeDataTransmissionAsync(byte[] address, byte[] data, IMidiLease? lease = null) =>
            Task.CompletedTask;

        public Task<IMidiLease> BeginConversationAsync(string what) =>
            throw new NotSupportedException("This fake never opens a conversation.");
        public Task NoteOnAsync(byte Channel, byte Note, byte Velocity) => Task.CompletedTask;
        public Task NoteOffAsync(byte Channel, byte Note) => Task.CompletedTask;
        public Task AllNotesOffAsync() => Task.CompletedTask;
        public Task ChangePresetAsync(byte Channel, int Msb, int Lsb, int Pc, IMidiLease? lease = null) =>
            Task.CompletedTask;
        public Task SendStopPreviewPhraseMsgAsync() => Task.CompletedTask;
        public Task SendPlayPreviewPhraseMsgAsync(byte channel) => Task.CompletedTask;
        public Task SendLoadSrxAsync(byte s1, byte s2, byte s3, byte s4) => Task.CompletedTask;
        public Task<(byte, byte, byte, byte)> GetLoadedSrxAsync() =>
            Task.FromResult(((byte)0, (byte)0, (byte)0, (byte)0));

        public Task WriteToneToUserMemory(Integra7AuralAlchemist.Models.Domain.Integra7Domain i7domain,
            string toneTypeStr, byte zeroBasedPartNo, string name, int zeroBasedMemoryId) => Task.CompletedTask;

        private static Task<List<string>> NoNames() => Task.FromResult(new List<string>());
        public Task<List<string>> GetStudioSetNames0to63() => NoNames();
        public Task<List<string>> GetPCMDrumKitUserNames0to31() => NoNames();
        public Task<List<string>> GetPCMToneUserNames0to63() => NoNames();
        public Task<List<string>> GetPCMToneUserNames64to127() => NoNames();
        public Task<List<string>> GetPCMToneUserNames128to191() => NoNames();
        public Task<List<string>> GetPCMToneUserNames192to255() => NoNames();
        public Task<List<string>> GetSuperNATURALDrumKitUserNames0to63() => NoNames();
        public Task<List<string>> GetSuperNATURALAcousticToneUserNames0to63() => NoNames();
        public Task<List<string>> GetSuperNATURALAcousticToneUserNames64to127() => NoNames();
        public Task<List<string>> GetSuperNATURALAcousticToneUserNames128to191() => NoNames();
        public Task<List<string>> GetSuperNATURALAcousticToneUserNames192to255() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames0to63() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames64to127() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames128to191() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames192to255() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames256to319() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames320to383() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames384to447() => NoNames();
        public Task<List<string>> GetSuperNATURALSynthToneUserNames448to511() => NoNames();
    }

    private static Integra7Parameters LoadParameters()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin");
        return new Integra7Parameters(File.OpenRead(path));
    }

    [Test]
    public void ARangeReadThatGetsNoReplyReportsFailure()
    {
        var parameters = LoadParameters();
        var addresses = new Integra7StartAddresses();
        var api = new SilentApi();

        var all = parameters.GetParametersWithPrefix("Studio Set Part/");
        Assert.That(all, Is.Not.Empty, "the fixture needs real parameters to range over");

        var range = new FullyQualifiedParameterRange("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", all[0], all[^1]);

        var ok = range.RetrieveFromIntegraAsync(api, addresses, parameters).GetAwaiter().GetResult();

        Assert.That(ok, Is.False, "a read with no reply must report failure rather than look successful");
        Assert.That(api.Requests, Is.EqualTo(1));
    }

    [Test]
    public void ASingleParameterReadThatGetsNoReplyKeepsItsValue()
    {
        var parameters = LoadParameters();
        var addresses = new Integra7StartAddresses();
        var api = new SilentApi();

        var spec = parameters.Lookup("Studio Set Part/Tone Bank Select MSB");
        var p = new FullyQualifiedParameter("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", spec);
        p.StringValue = "85";

        p.RetrieveFromIntegraAsync(api, addresses, parameters).GetAwaiter().GetResult();

        Assert.That(p.StringValue, Is.EqualTo("85"),
            "an unanswered read must not replace the value with one nobody read from the device");
    }
}
