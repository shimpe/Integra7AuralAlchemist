using System;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>OwnAudio.Midi hands a received short message over as a status and two data bytes, and takes
/// SysEx and short messages through separate calls. These pin the translation to and from the byte
/// arrays the rest of the application works with.</summary>
[TestFixture]
public class TestMidiWire
{
    [Test]
    public void AReceivedProgramChangeKeepsOnlyItsOneDataByte()
    {
        // CheckIsPartOfPresetChange and the dispatch logging both see exactly what was on the wire.
        Assert.That(MidiWire.FromShortMessage(0xC3, 0x05, 0x00), Is.EqualTo(new byte[] { 0xC3, 0x05 }));
    }

    [Test]
    public void AReceivedControlChangeKeepsBothDataBytes()
    {
        Assert.That(MidiWire.FromShortMessage(0xB2, 0x20, 0x40), Is.EqualTo(new byte[] { 0xB2, 0x20, 0x40 }));
    }

    [Test]
    public void AReceivedRealTimeMessageIsOneByte()
    {
        Assert.That(MidiWire.FromShortMessage(0xFE, 0x00, 0x00), Is.EqualTo(new byte[] { 0xFE }));
    }

    [TestCase((byte)0x80, 3)]
    [TestCase((byte)0x9F, 3)]
    [TestCase((byte)0xBF, 3)]
    [TestCase((byte)0xC0, 2)]
    [TestCase((byte)0xDF, 2)]
    [TestCase((byte)0xE0, 3)]
    [TestCase((byte)0xF1, 2)]
    [TestCase((byte)0xF2, 3)]
    [TestCase((byte)0xF3, 2)]
    [TestCase((byte)0xF6, 1)]
    [TestCase((byte)0xF8, 1)]
    [TestCase((byte)0x7F, 0)]
    public void ShortMessageLengthFollowsTheStatus(byte status, int expected)
    {
        Assert.That(MidiWire.ShortMessageLength(status), Is.EqualTo(expected));
    }

    [Test]
    public void ABankSelectAndProgramChangeInOneArrayAreSentAsThreeMessages()
    {
        byte[] data = [0xB0, 0x00, 0x57, 0xB0, 0x20, 0x40, 0xC0, 0x05];

        var messages = MidiWire.SplitMessages(data).Select(m => m.ToArray()).ToList();

        Assert.That(messages, Is.EqualTo(new[]
        {
            new byte[] { 0xB0, 0x00, 0x57 },
            new byte[] { 0xB0, 0x20, 0x40 },
            new byte[] { 0xC0, 0x05 }
        }));
    }

    [Test]
    public void EachSysExRunsThroughItsOwnTerminator()
    {
        byte[] data = [0xF0, 0x41, 0x10, 0xF7, 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7, 0x90, 0x3C, 0x64];

        var messages = MidiWire.SplitMessages(data).Select(m => m.ToArray()).ToList();

        Assert.That(messages, Is.EqualTo(new[]
        {
            new byte[] { 0xF0, 0x41, 0x10, 0xF7 },
            new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 },
            new byte[] { 0x90, 0x3C, 0x64 }
        }));
    }

    [Test]
    public void AnUnterminatedSysExRunsToTheEnd()
    {
        byte[] data = [0xF0, 0x41, 0x10, 0x00];

        Assert.That(MidiWire.SplitMessages(data).Single().ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void AStrayDataByteIsRefusedRatherThanSentAsAStatus()
    {
        Assert.That(() => MidiWire.SplitMessages([0x90, 0x3C, 0x64, 0x3C]), Throws.TypeOf<FormatException>());
    }
}
