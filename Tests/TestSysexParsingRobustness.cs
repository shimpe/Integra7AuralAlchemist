using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

[TestFixture]
public class TestIdentityReplyRobustness
{
    [Test]
    public void AnIdentityReplyWithoutATerminatorIsRejectedRatherThanIndexed()
    {
        // TrimAfterEndOfSysex yields nothing when the f7 is missing, so guarding the raw reply's
        // length is not enough -- the trimmed one is what gets indexed.
        byte[] noTerminator = [0xf0, 0x7e, 0x10, 0x06, 0x02];

        Assert.That(Integra7SysexHelpers.CheckIdentityReply(noTerminator, out var deviceId), Is.False);
        Assert.That(deviceId, Is.EqualTo(0));
    }

    [Test]
    public void AShortIdentityReplyIsRejected()
    {
        Assert.That(Integra7SysexHelpers.CheckIdentityReply([], out _), Is.False);
        Assert.That(Integra7SysexHelpers.CheckIdentityReply([0xf0, 0xf7], out _), Is.False);
    }

    [Test]
    public void AReplyThatIsNotAnIdentityReplyIsRejectedWithoutThrowing()
    {
        byte[] dataSet = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x00, 0x6b, 0xf7];

        Assert.That(Integra7SysexHelpers.CheckIdentityReply(dataSet, out _), Is.False);
    }
}

[TestFixture]
public class TestSysexParsingRobustness
{
    // A well-formed data-set message: header, a 4-byte address, one byte of parameter data,
    // a checksum byte and the end-of-sysex marker.
    private static readonly byte[] WellFormedDataSet =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, // header (device id ignored)
        0x01, 0x02, 0x03, 0x04, // address
        0x00, // one byte of "parameter data"
        0x00, // checksum (not validated by these helpers)
        0xf7
    ];

    // Same header/command as above, but a well-formed data REQUEST rather than a data set.
    private static readonly byte[] WellFormedDataRequest =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x11, // 0x11 = data request, not data set
        0x01, 0x02, 0x03, 0x04,
        0x00,
        0x00,
        0xf7
    ];

    [TestFixture]
    public class CheckIsDataSetMsgTests
    {
        [Test]
        public void RejectsAnEmptyMessage()
        {
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg([]), Is.False);
        }

        [Test]
        public void RejectsAOneByteActiveSensingByte()
        {
            // 0xfe is the MIDI active-sensing status byte; the device sends it on its own.
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg([0xfe]), Is.False);
        }

        [Test]
        public void RejectsAThreeByteProgramChangeShapedMessage()
        {
            byte[] programChangeShaped = [0xc0, 0x05, 0x00];
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg(programChangeShaped), Is.False);
        }

        [Test]
        public void RejectsASixByteMessageOneByteShortOfTheHeader()
        {
            // The header is 7 bytes (F0 41 <devid> 00 00 64 12); this is missing the command byte.
            byte[] almostHeader = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64];
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg(almostHeader), Is.False);
        }

        [Test]
        public void AcceptsARealDataSetMessage()
        {
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg(WellFormedDataSet), Is.True);
        }

        [Test]
        public void RejectsAWellFormedSysexThatIsNotADataSet()
        {
            Assert.That(Integra7SysexHelpers.CheckIsDataSetMsg(WellFormedDataRequest), Is.False);
        }
    }

    [TestFixture]
    public class ExtractPayloadTests
    {
        [Test]
        public void ReturnsEmptyWhenThereIsNoEndOfSysexMarker()
        {
            byte[] noTerminator = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x01, 0x02, 0x03, 0x04, 0x00];
            Assert.That(Integra7SysexHelpers.ExtractPayload(noTerminator), Is.Empty);
        }

        [Test]
        public void ReturnsEmptyWhenTooShortToHoldHeaderPlusChecksum()
        {
            // Header only, immediately followed by the terminator -- no room left for a checksum byte,
            // let alone a payload.
            byte[] tooShort = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0xf7];
            Assert.That(Integra7SysexHelpers.ExtractPayload(tooShort), Is.Empty);
        }

        [Test]
        public void ExtractsThePayloadFromAWellFormedMessage()
        {
            Assert.That(Integra7SysexHelpers.ExtractPayload(WellFormedDataSet),
                Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x00 }));
        }
    }

    [TestFixture]
    public class TrimAfterEndOfSysexTests
    {
        [Test]
        public void ReturnsEmptyWhenThereIsNoEndOfSysexMarker()
        {
            byte[] noTerminator = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x01];
            Assert.That(Integra7SysexHelpers.TrimAfterEndOfSysex(noTerminator), Is.Empty);
        }

        [Test]
        public void KeepsEverythingUpToAndIncludingTheTerminatorForAWellFormedMessage()
        {
            Assert.That(Integra7SysexHelpers.TrimAfterEndOfSysex(WellFormedDataSet),
                Is.EqualTo(WellFormedDataSet));
        }
    }

    [TestFixture]
    public class ConvertSysexToParameterUpdatesTests
    {
        // i7 == null makes every address lookup fail. Before the fix, a failed lookup left
        // currentLocation and address unchanged, so the parse loop never advanced and never
        // terminated.
        //
        // Run on a worker and wait, rather than using [Timeout]: NUnit's timeout relied on
        // Thread.Abort, which does not exist on .NET 5 and later, so a regression would be reported
        // as failed while the loop kept spinning for the life of the test process. Waiting on the
        // task reports the failure reliably. The orphaned worker is the accepted price -- a
        // regression here means an infinite loop either way, and the suite still finishes.
        [Test]
        public void TerminatesAndReturnsEmptyWhenTheDomainIsNull()
        {
            Integra7Domain? noDomain = null;
            List<UpdateMessageSpec>? result = null;

            var parse = Task.Run(() =>
                result = SysexDataTransmissionParser.ConvertSysexToParameterUpdates(WellFormedDataSet, noDomain));

            Assert.That(parse.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the parse loop did not terminate -- an address it cannot resolve must end the parse, not spin");
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ReturnsEmptyRatherThanCrashingForAPayloadShorterThanFourBytes()
        {
            // Header, a 2-byte "address" (too short to be one), checksum, terminator.
            byte[] shortPayload = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x01, 0x02, 0x00, 0xf7];
            Integra7Domain? noDomain = null;
            var result = SysexDataTransmissionParser.ConvertSysexToParameterUpdates(shortPayload, noDomain);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ReturnsEmptyForAMessageThatIsNotADataSet()
        {
            Integra7Domain? noDomain = null;
            var result = SysexDataTransmissionParser.ConvertSysexToParameterUpdates(WellFormedDataRequest, noDomain);
            Assert.That(result, Is.Empty);
        }
    }
}
