using System;
using System.IO;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A saved pad. Corner patches are stored as file names relative to the library folder, for the
/// reason the init-tone marks are: the library folder is a setting the user can move.</summary>
public class MorphPadFileTests
{
    private string _folder = "";

    [SetUp]
    public void CreateTempFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "Integra7AuralAlchemist.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveTempFolder()
    {
        // A deletion that fails must not fail a test that actually passed -- see LibrarySettingsTests,
        // whose temp-directory pattern this follows.
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"Could not remove the temp directory {_folder}: {e.Message}");
        }
    }

    [Test]
    public void A_pad_round_trips()
    {
        var path = Path.Combine(_folder, "Pads", "Strings.json");
        var pad = new MorphPad("SN-S", ["A.json", "B.json", "C.json"], 0.25, -0.5);

        MorphPadFile.Save(path, pad);
        var loaded = MorphPadFile.Load(path);

        Assert.That(loaded.ToneType, Is.EqualTo("SN-S"));
        Assert.That(loaded.CornerFiles, Is.EqualTo(new[] { "A.json", "B.json", "C.json" }));
        Assert.That(loaded.X, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(loaded.Y, Is.EqualTo(-0.5).Within(1e-9));
    }

    [Test]
    public void Saving_creates_the_folder_it_needs()
    {
        var path = Path.Combine(_folder, "Pads", "New.json");

        MorphPadFile.Save(path, new MorphPad("PCMS", ["A.json", "B.json"], 0, 0));

        Assert.That(File.Exists(path), Is.True);
    }

    /// <summary>A file that is not a pad, or not JSON at all, must say so rather than throwing something
    /// the caller cannot show a user.</summary>
    [Test]
    public void An_unreadable_pad_is_refused_with_a_message()
    {
        var path = Path.Combine(_folder, "broken.json");
        File.WriteAllText(path, "this is not JSON");

        Assert.That(() => MorphPadFile.Load(path), Throws.TypeOf<SnapshotFormatException>());
    }
}
