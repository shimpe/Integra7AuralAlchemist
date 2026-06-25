using System.IO;
using Integra7AuralAlchemist.Models.Data;

namespace Tests;

public class ParametersTests
{
    private readonly Integra7Parameters _parameters;

    public ParametersTests()
    {
        // parameters.bin is copied to the test output directory (see Tests.csproj). Feed it through the
        // real Integra7Parameters via its stream constructor so the production lookup logic is tested.
        var binPath = Path.Combine(AppContext.BaseDirectory, "parameters.bin");
        _parameters = new Integra7Parameters(File.OpenRead(binPath));
    }

    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestFromTo()
    {
        var l = _parameters.GetParametersFromTo("Studio Set Part/Keyboard Range Lower",
            "Studio Set Part/Velocity Range Upper");
        Assert.That(l.Count, Is.EqualTo(6));
        Assert.That(l[0].Path, Is.EqualTo("Studio Set Part/Keyboard Range Lower"));
        Assert.That(l[1].Path, Is.EqualTo("Studio Set Part/Keyboard Range Upper"));
        Assert.That(l[2].Path, Is.EqualTo("Studio Set Part/Keyboard Fade Width Lower"));
        Assert.That(l[3].Path, Is.EqualTo("Studio Set Part/Keyboard Fade Width Upper"));
        Assert.That(l[4].Path, Is.EqualTo("Studio Set Part/Velocity Range Lower"));
        Assert.That(l[5].Path, Is.EqualTo("Studio Set Part/Velocity Range Upper"));
    }
}
