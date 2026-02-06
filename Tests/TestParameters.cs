using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class ParametersTests
{
    private static readonly string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string appFolder = Path.Combine(commonAppData, "Integra7AuralAlchemist");
    private static readonly string dbPath = Path.Combine(appFolder, "Integra7AuralAlchemist.db");
    private static readonly string idxPath = Path.Combine(appFolder, "Integra7AuralAlchemist.idx");
    public Integra7GzipJsonRepository _p = new(dbPath, idxPath, 32);

    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task TestFromTo()
    {
        var l = await _p.GetRangeAsync("Studio Set Part/Keyboard Range Lower", "Studio Set Part/Velocity Range Upper");
        Assert.That(l.Count, Is.EqualTo(6));
        Assert.That(l[0].Path, Is.EqualTo("Studio Set Part/Keyboard Range Lower"));
        Assert.That(l[1].Path, Is.EqualTo("Studio Set Part/Keyboard Range Upper"));
        Assert.That(l[2].Path, Is.EqualTo("Studio Set Part/Keyboard Fade Width Lower"));
        Assert.That(l[3].Path, Is.EqualTo("Studio Set Part/Keyboard Fade Width Upper"));
        Assert.That(l[4].Path, Is.EqualTo("Studio Set Part/Velocity Range Lower"));
        Assert.That(l[5].Path, Is.EqualTo("Studio Set Part/Velocity Range Upper"));
    }
}