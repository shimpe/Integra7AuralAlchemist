using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveBankResolver
{
    [Test]
    public void Internal_MapsToInt()
        => Assert.That(WaveBankResolver.BankName("Internal", 0), Is.EqualTo("INT"));

    [Test]
    public void Srx_MapsToNumberedBank()
    {
        Assert.That(WaveBankResolver.BankName("SRX", 1), Is.EqualTo("SRX1"));
        Assert.That(WaveBankResolver.BankName("SRX", 12), Is.EqualTo("SRX12"));
    }

    [Test]
    public void ReservedOrUnknownType_FallsBackToInt()
    {
        Assert.That(WaveBankResolver.BankName("Reserved", 0), Is.EqualTo("INT"));
        Assert.That(WaveBankResolver.BankName("", 5), Is.EqualTo("INT"));
    }

    [Test]
    public void SrxWithIdOutOfRange_FallsBackToInt()
    {
        Assert.That(WaveBankResolver.BankName("SRX", 0), Is.EqualTo("INT"));
        Assert.That(WaveBankResolver.BankName("SRX", 13), Is.EqualTo("INT"));
    }
}
