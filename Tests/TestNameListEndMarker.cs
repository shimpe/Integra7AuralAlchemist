using Integra7AuralAlchemist.Models.Services;

namespace Tests;

[TestFixture]
public class TestNameListEndMarker
{
    // Captured from the application log: the reply that closes every name-list burst.
    private static readonly byte[] Terminator =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6b, 0xf7
    ];

    // Captured from the same burst: an ordinary reply carrying the name "INIT KIT        ".
    private static readonly byte[] NameReply =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02,
        0x56, 0x00, 0x00, 0x21, 0x00, 0x49, 0x4e, 0x49, 0x54, 0x20, 0x4b,
        0x49, 0x54, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
        0x38, 0xf7
    ];

    [Test]
    public void RecognisesTheCapturedTerminator()
    {
        Assert.That(NameListEndMarker.IsEndOfBurst(Terminator), Is.True);
    }

    [Test]
    public void RejectsAnOrdinaryNameReply()
    {
        Assert.That(NameListEndMarker.IsEndOfBurst(NameReply), Is.False);
    }

    [Test]
    public void AcceptsAnotherDeviceId()
    {
        // Byte 2 is the device ID and varies with the unit's configuration.
        var other = (byte[])Terminator.Clone();
        other[2] = 0x17;
        Assert.That(NameListEndMarker.IsEndOfBurst(other), Is.True);
    }

    [Test]
    public void RejectsAnotherAddress()
    {
        // Same shape, but not the name-list address.
        var other = (byte[])Terminator.Clone();
        other[7] = 0x18;
        Assert.That(NameListEndMarker.IsEndOfBurst(other), Is.False);
    }

    [Test]
    public void RejectsARequestRatherThanADataSet()
    {
        var other = (byte[])Terminator.Clone();
        other[6] = 0x11; // data request, not data set
        Assert.That(NameListEndMarker.IsEndOfBurst(other), Is.False);
    }

    [Test]
    public void RejectsAnyNonZeroPayloadByte()
    {
        for (var i = 11; i < 32; i++)
        {
            var other = (byte[])Terminator.Clone();
            other[i] = 0x01;
            Assert.That(NameListEndMarker.IsEndOfBurst(other), Is.False, $"payload byte {i}");
        }
    }

    [Test]
    public void RejectsMalformedMessages()
    {
        Assert.That(NameListEndMarker.IsEndOfBurst(null), Is.False);
        Assert.That(NameListEndMarker.IsEndOfBurst([]), Is.False);
        Assert.That(NameListEndMarker.IsEndOfBurst([0xf0, 0x41, 0xf7]), Is.False);
        var truncated = new byte[33];
        Terminator.AsSpan(0, 33).CopyTo(truncated);
        Assert.That(NameListEndMarker.IsEndOfBurst(truncated), Is.False);
    }

    [Test]
    public void RecognisesAWellFormedNameReplyAsANameListReply()
    {
        Assert.That(NameListEndMarker.IsNameListReply(NameReply), Is.True);
    }

    [Test]
    public void RecognisesTheEndOfBurstMessageAsANameListReplyToo()
    {
        // The terminator is a name-list reply too -- it's just the one with an empty name.
        // IsEndOfBurst is the narrower predicate that also demands the all-zero payload.
        Assert.That(NameListEndMarker.IsNameListReply(Terminator), Is.True);
    }

    [Test]
    public void RejectsAShortPanelChangeMessageArrivingMidBurst()
    {
        // A realistic short Roland sysex -- the shape of what the device sends unsolicited
        // when the user turns the front-panel patch dial. Same f0 41 prefix as a name reply,
        // but far too short to hold a name at indices 16..31. This is the exact shape that
        // used to crash ByteUtils.Slice when it slipped into a name-list burst.
        byte[] panelChange = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x01, 0x00, 0x00, 0x00, 0x01, 0x0e, 0xf7];
        Assert.That(NameListEndMarker.IsNameListReply(panelChange), Is.False);
    }

    [Test]
    public void RejectsA34ByteMessageWithTheWrongCommandByte()
    {
        var other = (byte[])NameReply.Clone();
        other[6] = 0x11; // data request, not data set
        Assert.That(NameListEndMarker.IsNameListReply(other), Is.False);
    }

    [Test]
    public void RejectsA34ByteMessageAtAnotherAddress()
    {
        var other = (byte[])NameReply.Clone();
        other[7] = 0x18;
        Assert.That(NameListEndMarker.IsNameListReply(other), Is.False);
    }

    [Test]
    public void RejectsNullMessage()
    {
        Assert.That(NameListEndMarker.IsNameListReply(null), Is.False);
    }

    [Test]
    public void RejectsAMessageNotStartingWithF0Then41()
    {
        var wrongStart = (byte[])NameReply.Clone();
        wrongStart[0] = 0xf1;
        Assert.That(NameListEndMarker.IsNameListReply(wrongStart), Is.False);

        var wrongManufacturer = (byte[])NameReply.Clone();
        wrongManufacturer[1] = 0x42;
        Assert.That(NameListEndMarker.IsNameListReply(wrongManufacturer), Is.False);
    }

    [Test]
    public void RejectsAMessageNotEndingWithF7()
    {
        var other = (byte[])NameReply.Clone();
        other[^1] = 0x00;
        Assert.That(NameListEndMarker.IsNameListReply(other), Is.False);
    }

    // The request the tone name lists are fetched with. Its address is what the replies carry back.
    private static readonly byte[] ToneNameRequest =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x11, 0x0f, 0x00, 0x04, 0x02,
        0x56, 0x00, 0x00, 0x40, 0x39, 0xf7
    ];

    // Studio Set names are requested at a different address: 0f 00 03 02, not 0f 00 04 02.
    private static readonly byte[] StudioSetNameRequest =
    [
        0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x11, 0x0f, 0x00, 0x03, 0x02,
        0x55, 0x00, 0x00, 0x40, 0x3b, 0xf7
    ];

    [Test]
    public void TakesTheExpectedAddressFromTheRequest()
    {
        Assert.That(NameListEndMarker.AddressOf(ToneNameRequest),
            Is.EqualTo(new byte[] { 0x0f, 0x00, 0x04, 0x02 }));
        Assert.That(NameListEndMarker.AddressOf(StudioSetNameRequest),
            Is.EqualTo(new byte[] { 0x0f, 0x00, 0x03, 0x02 }));
    }

    [Test]
    public void ARequestTooShortToHoldAnAddressMatchesNothing()
    {
        Assert.That(NameListEndMarker.AddressOf([0xf0, 0x41, 0x10]), Is.Empty);
        Assert.That(NameListEndMarker.IsNameListReply(NameReply, []), Is.False,
            "an address that could not be read must match nothing, rather than everything");
    }

    [Test]
    public void AcceptsAReplyAtTheStudioSetAddressWhenThatIsWhatWasAsked()
    {
        // Hardcoding the tone-list address would reject every Studio Set name reply, turning a list
        // that failed to load into a silently empty one.
        var reply = (byte[])NameReply.Clone();
        reply[9] = 0x03;

        Assert.That(NameListEndMarker.IsNameListReply(reply, NameListEndMarker.AddressOf(StudioSetNameRequest)),
            Is.True);
        Assert.That(NameListEndMarker.IsNameListReply(reply, NameListEndMarker.AddressOf(ToneNameRequest)),
            Is.False);
    }

    [Test]
    public void DoesNotMistakeAReplyToADifferentRequestForThisBursts()
    {
        Assert.That(NameListEndMarker.IsNameListReply(NameReply, NameListEndMarker.AddressOf(StudioSetNameRequest)),
            Is.False);
    }
}
