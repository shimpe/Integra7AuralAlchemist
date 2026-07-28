using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which tone Init loads. Pure: existence is asked of the caller through two predicates, so
/// this is testable without touching the disk or Avalonia's asset loader.</summary>
public class InitToneResolutionTests
{
    /// <summary>A folder name with no separator in it, joined to the file name the same way the code
    /// under test joins them.
    ///
    /// It used to be "C:\Library" with the expected result spelled "C:\Library\My Init.json". That passed
    /// on Windows and failed on Linux and macOS, where <see cref="Path.Combine"/> joins with '/' and
    /// produced "C:\Library/My Init.json" -- one string with both separators in it, which is nobody's
    /// path. The subject here is which of the two candidates wins, not how a path is spelled, so the
    /// fixture no longer says anything about spelling.</summary>
    private static readonly string Folder = Path.Combine("some", "library");

    private static string InTheLibrary(string file) => Path.Combine(Folder, file);

    private static InitToneSource Resolve(IReadOnlyDictionary<string, string> marks,
        bool fileExists, bool assetExists) =>
        InitToneResolution.Resolve(marks, Folder, "SN-S", _ => fileExists, _ => assetExists);

    [Test]
    public void A_marked_library_entry_wins_over_the_bundled_asset()
    {
        var source = Resolve(new Dictionary<string, string> { ["SN-S"] = "My Init.json" },
            fileExists: true, assetExists: true);

        Assert.That(source.FilePath, Is.EqualTo(InTheLibrary("My Init.json")));
        Assert.That(source.AssetUri, Is.Null);
    }

    /// <summary>A mark can outlive the file it names -- the entry is deleted from the library, or the
    /// library folder is repointed somewhere that does not have it. Falling through to the bundled tone
    /// is better than refusing; the command still says the mark was stale.</summary>
    [Test]
    public void A_mark_whose_file_is_gone_falls_through_to_the_asset()
    {
        var source = Resolve(new Dictionary<string, string> { ["SN-S"] = "Deleted.json" },
            fileExists: false, assetExists: true);

        Assert.That(source.FilePath, Is.Null);
        Assert.That(source.AssetUri, Is.EqualTo("avares://Integra7AuralAlchemist/Assets/InitTones/SN-S.json"));
        Assert.That(source.MarkWasStale, Is.True);
    }

    [Test]
    public void No_mark_and_no_asset_resolves_to_nothing()
    {
        var source = Resolve(new Dictionary<string, string>(), fileExists: false, assetExists: false);

        Assert.That(source.FilePath, Is.Null);
        Assert.That(source.AssetUri, Is.Null);
        Assert.That(source.MarkWasStale, Is.False);
        Assert.That(source.HasTone, Is.False);
    }

    [Test]
    public void Uses_the_bundled_asset_when_nothing_is_marked()
    {
        var source = Resolve(new Dictionary<string, string>(), fileExists: false, assetExists: true);

        Assert.That(source.AssetUri, Is.EqualTo("avares://Integra7AuralAlchemist/Assets/InitTones/SN-S.json"));
        Assert.That(source.HasTone, Is.True);
    }
}
