using Integra7AuralAlchemist.Models.Services;

namespace Tests;

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
}
