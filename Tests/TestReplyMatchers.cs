using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What each conversation will accept as its own reply. Everything here is pure -- no MIDI,
/// no hardware -- which is the point: a matcher that is too strict turns a working read into a
/// timeout, and that failure is silent at runtime.</summary>
[TestFixture]
public class TestReplyMatchers
{
    // f0 41 <dev> 00 00 64 12 | 0f 00 04 02 | payload | checksum | f7
    private static byte[] DataSet(byte[] address, byte deviceId = 0x10) =>
    [
        0xf0, 0x41, deviceId, 0x00, 0x00, 0x64, 0x12,
        address[0], address[1], address[2], address[3],
        0x00, 0x01, 0x02, 0x6b, 0xf7
    ];

    private static readonly byte[] ToneNameAddress = [0x0f, 0x00, 0x04, 0x02];
    private static readonly byte[] SrxAddress = [0x0f, 0x00, 0x00, 0x10];

    // Captured shape of an identity reply: f0 7e <dev> 06 02 41 ...
    private static readonly byte[] IdentityReplyMessage =
    [
        0xf0, 0x7e, 0x10, 0x06, 0x02, 0x41, 0x64, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xf7
    ];

    [Test]
    public void ADataSetAtTheRequestedAddressIsTheReply()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(ToneNameAddress)), Is.True);
    }

    [Test]
    public void ADataSetAtADifferentAddressIsNotTheReply()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(SrxAddress)), Is.False);
    }

    [Test]
    public void TheDeviceIdIsIgnoredWhenMatching()
    {
        // Byte 2 varies with how the unit is configured; matching on it would reject every reply
        // from a device that is not on the default ID.
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches(DataSet(ToneNameAddress, 0x11)), Is.True);
    }

    [Test]
    public void ARequestIsNotMistakenForAReply()
    {
        // Same address, command 0x11 (request) rather than 0x12 (data set).
        var request = DataSet(ToneNameAddress);
        request[6] = 0x11;

        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Matches(request), Is.False);
    }

    [Test]
    public void AMessageTooShortToHoldAnAddressMatchesNothing()
    {
        var matcher = ReplyMatchers.DataSetAt(ToneNameAddress);

        Assert.That(matcher.Matches([]), Is.False);
        Assert.That(matcher.Matches([0xfe]), Is.False);                    // active sensing
        Assert.That(matcher.Matches([0xc0, 0x05]), Is.False);              // program change
        Assert.That(matcher.Matches([0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f]), Is.False);
    }

    [Test]
    public void AnIdentityReplyMatchesOnlyTheIdentityMatcher()
    {
        Assert.That(ReplyMatchers.IdentityReply.Matches(IdentityReplyMessage), Is.True);
        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Matches(IdentityReplyMessage), Is.False);
        Assert.That(ReplyMatchers.IdentityReply.Matches(DataSet(ToneNameAddress)), Is.False);
    }

    [Test]
    public void AnIdentityRequestIsNotMistakenForItsReply()
    {
        // 06 01 is the request, 06 02 the reply.
        byte[] identityRequest = [0xf0, 0x7e, 0x7f, 0x06, 0x01, 0xf7];

        Assert.That(ReplyMatchers.IdentityReply.Matches(identityRequest), Is.False);
    }

    [Test]
    public void TheNameListMatcherDelegatesToTheBurstReader()
    {
        // 34 bytes, all-zero payload -- the burst terminator, which is a name-list reply.
        byte[] terminator =
        [
            0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x6b, 0xf7
        ];

        Assert.That(ReplyMatchers.NameListReply(ToneNameAddress).Matches(terminator), Is.True);
        Assert.That(ReplyMatchers.NameListReply(SrxAddress).Matches(terminator), Is.False);
    }

    [Test]
    public void EveryMatcherDescribesItself()
    {
        // Describe() is logged when a message is deferred, so a wrong matcher is findable.
        Assert.That(ReplyMatchers.DataSetAt(ToneNameAddress).Describe(), Does.Contain("0F-00-04-02"));
        Assert.That(ReplyMatchers.IdentityReply.Describe(), Is.Not.Empty);
        Assert.That(ReplyMatchers.NameListReply(ToneNameAddress).Describe(), Does.Contain("0F-00-04-02"));
    }
}
