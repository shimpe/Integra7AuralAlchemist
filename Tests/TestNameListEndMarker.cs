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
}
