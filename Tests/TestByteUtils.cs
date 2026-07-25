using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class ByteUtilsTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestFlatten()
    {
        var data = ByteUtils.Flatten([0x00], [0x00, 0x00]);
        Assert.That(data, Is.EquivalentTo((byte[]) [0x00, 0x00, 0x00]));
    }

    [Test]
    public void TestAddressWithOffset()
    {
        byte[] addr = [0x00, 0x20, 0x00];
        byte[] offs = [0x02, 0x00];
        var comb = ByteUtils.AddressWithOffset(addr, offs);
        Assert.That(comb, Is.EquivalentTo((byte[]) [0x00, 0x22, 0x00]));
    }

    [Test]
    public void TestAddressWithOffsetCarryOver()
    {
        byte[] addr = [0x00, 0x20, 0x00];
        byte[] offs = [0x00, 0x70, 0x00];
        var comb = ByteUtils.AddressWithOffset(addr, offs);
        Assert.That(comb, Is.EquivalentTo((byte[]) [0x01, 0x10, 0x00]));
    }

    [Test]
    public void TestCheckSum()
    {
        var payload = ByteUtils.Concat(
            ByteUtils.AddressWithOffset(
                ByteUtils.AddressWithOffset(
                    [0x18, 0x00, 0x00, 0x00] /*temp studio set start address*/,
                    [0x06, 0x00] /*studio set reverb offset*/),
                [0x00, 0x00] /*reverb type address*/),
            [0x02] /*reverb value*/);
        Assert.That(payload, Is.EquivalentTo((byte[]) [0x18, 0x00, 0x06, 0x00, 0x02]));
        var cs = ByteUtils.CheckSum(payload);
        Assert.That(cs, Is.EqualTo(0x60));
    }

    [Test]
    public void TestIntToBytes7_2()
    {
        var value = 0x7f;
        var data = ByteUtils.IntToBytes7_2(value);
        Assert.That(data, Is.EquivalentTo((byte[]) [0x00, 0x7f]));

        var value2 = 0x80;
        var data2 = ByteUtils.IntToBytes7_2(value2);
        Assert.That(data2, Is.EquivalentTo((byte[]) [0x01, 0x00]));
    }

    [Test]
    public void TestBytes7ToInt()
    {
        byte[] data = [0x00, 0x7f];
        var value = ByteUtils.Bytes7ToInt(data);
        Assert.That(value, Is.EqualTo(0x7f));

        byte[] data2 = [0x01, 0x00];
        var value2 = ByteUtils.Bytes7ToInt(data2);
        Assert.That(value2, Is.EqualTo(0x80));
    }

    [Test]
    public void TestIntToNibbled()
    {
        long value = 0xab;
        var nibbled = ByteUtils.IntToNibbled(value, 2);
        Assert.That(nibbled, Is.EquivalentTo((byte[]) [0x0a, 0x0b]));

        long value2 = 0xfbea;
        var nibbled2 = ByteUtils.IntToNibbled(value2, 4);
        Assert.That(nibbled2, Is.EquivalentTo((byte[]) [0xf, 0xb, 0x0e, 0x0a]));
    }

    [Test]
    public void TestNibbledToInt()
    {
        byte[] data = [0x0a, 0x0b];
        Assert.That(ByteUtils.NibbledToInt(data), Is.EqualTo(0xab));

        byte[] data2 = [0x0f, 0x0b, 0x0e, 0x0a];
        Assert.That(ByteUtils.NibbledToInt(data2), Is.EqualTo(0xfbea));
    }

    [Test]
    public void TestConcat1()
    {
        byte[] data1 = [0x01, 0x02, 0x03];
        byte[] data2 = [0x04, 0x05];
        var conc = ByteUtils.Concat(data1, data2);
        Assert.That(conc, Is.EquivalentTo((byte[]) [0x01, 0x02, 0x03, 0x04, 0x05]));
    }

    [Test]
    public void TestConcat2()
    {
        byte[] data1 = [];
        byte[] data2 = [0x04, 0x05];
        var conc = ByteUtils.Concat(data1, data2);
        Assert.That(conc, Is.EquivalentTo((byte[]) [0x04, 0x05]));
    }

    [Test]
    public void TestConcat3()
    {
        byte[] data1 = [0x01, 0x02, 0x03];
        byte[] data2 = [];
        var conc = ByteUtils.Concat(data1, data2);
        Assert.That(conc, Is.EquivalentTo((byte[]) [0x01, 0x02, 0x03]));
    }

    [Test]
    public void TestConcat4()
    {
        byte[] data1 = [];
        byte[] data2 = [];
        var conc = ByteUtils.Concat(data1, data2);
        Assert.That(conc, Is.EquivalentTo((byte[]) []));
    }

    [Test]
    public void TestSlice()
    {
        byte[] data1 = [0x0, 0x1, 0x2, 0x3];

        var slice = ByteUtils.Slice(data1, 0, 1);
        Assert.That(slice, Is.EquivalentTo((byte[]) [0x0]));

        var slice2 = ByteUtils.Slice(data1, 1, 2);
        Assert.That(slice2, Is.EquivalentTo((byte[]) [0x1, 0x2]));
    }

    [Test]
    public void TestZeros()
    {
        var noOfZeros = 0;
        var zeros = ByteUtils.Zeros(noOfZeros);
        Assert.That(zeros, Is.EquivalentTo((byte[]) []));

        var noOfZeros2 = 4;
        var zeros2 = ByteUtils.Zeros(noOfZeros2);
        Assert.That(zeros2, Is.EquivalentTo((byte[]) [0x00, 0x00, 0x00, 0x00]));
    }

    [Test]
    public void TestPadString()
    {
        var shortd = Encoding.ASCII.GetBytes("biebel");
        var longd = ByteUtils.PadString(shortd, 12);
        Assert.That(longd, Is.EquivalentTo(ByteUtils.Flatten(shortd, [0x20, 0x20, 0x20, 0x20, 0x20, 0x20])));
    }

    [Test]
    public void TestSplitAfterF7()
    {
        byte[] reply =
        [
            0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x19, 0x02, 0x00, 0x11, 0x01, 0x40, 0x40, 0x40,
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x13, 0xf7, 0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12,
            0x19, 0x02, 0x00, 0x20, 0x40, 0x05, 0x40, 0x40, 0x40, 0x3f, 0x00, 0x40, 0x41, 0xf7, 0x00, 0x00
        ];

        byte[] exp0 =
        [
            0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x19, 0x02, 0x00, 0x11, 0x01, 0x40, 0x40, 0x40,
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x13, 0xf7
        ];
        byte[] exp1 =
        [
            0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12,
            0x19, 0x02, 0x00, 0x20, 0x40, 0x05, 0x40, 0x40, 0x40, 0x3f, 0x00, 0x40, 0x41, 0xf7
        ];

        byte[][] split = ByteUtils.SplitAfterF7(reply);
        Assert.That(split[0], Is.EquivalentTo(exp0));
        Assert.That(split[1], Is.EquivalentTo(exp1));
    }

    /// <summary>A message that ends exactly at its terminator leaves nothing over, so it splits to
    /// just itself. It used to gain a trailing null slot as well, which had the parser log
    /// "Ignoring a sysex fragment with no end-of-sysex marker" for every well-formed message the
    /// hardware sent.</summary>
    [Test]
    public void SplitAfterF7AddsNoNullSlotForAMessageEndingAtItsTerminator()
    {
        byte[] oneMessage = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x6b, 0xf7];

        var split = ByteUtils.SplitAfterF7(oneMessage);

        Assert.That(split, Has.Length.EqualTo(1), "nothing follows the terminator, so there is no tail");
        Assert.That(split[0], Is.EquivalentTo(oneMessage));
    }

    /// <summary>Bytes after the last terminator are an incomplete message and DO get the null slot --
    /// callers skip it, and the parser reports it. Pinned because the declared return type is
    /// byte[][], not byte[]?[], so the compiler's null analysis cannot warn about it.</summary>
    [Test]
    public void SplitAfterF7MarksAnUnterminatedTailWithANullSlot()
    {
        byte[] oneMessage = [0xf0, 0x41, 0x10, 0x00, 0x00, 0x64, 0x12, 0x0f, 0x00, 0x04, 0x02, 0x6b, 0xf7];
        byte[] withTail = [.. oneMessage, 0xf0, 0x41];

        var split = ByteUtils.SplitAfterF7(withTail);

        Assert.That(split, Has.Length.EqualTo(2), "the message plus a marker for the unterminated tail");
        Assert.That(split[0], Is.EquivalentTo(oneMessage));
        Assert.That(split[1], Is.Null, "not an empty array -- null");
    }

    [Test]
    public void SplitAfterF7ReturnsOnlyTheNullSlotWhenThereIsNoTerminator()
    {
        // A short channel-voice message -- a program change -- carries no f7 at all.
        var split = ByteUtils.SplitAfterF7([0xc0, 0x05]);

        Assert.That(split, Has.Length.EqualTo(1));
        Assert.That(split[0], Is.Null);
    }
}