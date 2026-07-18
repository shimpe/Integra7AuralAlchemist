using System;
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;

namespace Tests;

/// <summary>Pins GetParametersWithPrefix against the behaviour it had when every call rescanned the
/// whole store: same parameters, same order, callers still own the list they get back.</summary>
[TestFixture]
public class TestParameterPrefixLookup
{
    // The prefixes the domain layer actually asks for, plus a few shapes that should match nothing
    // or everything.
    private static readonly string[] Prefixes =
    [
        "PCM Synth Tone Partial/",
        "PCM Synth Tone Common/",
        "PCM Synth Tone Common MFX/",
        "PCM Synth Tone PMT/",
        "PCM Drum Kit Common/",
        "PCM Drum Kit Partial/",
        "SuperNATURAL Synth Tone Common/",
        "SuperNATURAL Synth Tone Partial/",
        "SuperNATURAL Acoustic Tone Common/",
        "SuperNATURAL Drum Kit Common/",
        "SuperNATURAL Drum Kit Partial/",
        "Studio Set Part/",
        "Studio Set Common/",
        "Setup/",
        "System/",
        "",
        "zzz-no-such-prefix",
        "PCM Synth Tone Partial"   // same family, without the trailing slash
    ];

    private static Integra7Parameters Load()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin");
        return new Integra7Parameters(File.OpenRead(path));
    }

    /// <summary>The original implementation, kept here as the oracle.</summary>
    private static List<string> ReferenceScan(Integra7Parameters p, string prefix)
    {
        var all = p.GetParametersWithPrefix("");
        var result = new List<string>();
        foreach (var spec in all)
            if (spec.Path.StartsWith(prefix)) // culture-sensitive, as the original was
                result.Add(spec.Path);
        return result;
    }

    [Test]
    public void MatchesTheOriginalLinearScanForEveryPrefix()
    {
        var p = Load();
        foreach (var prefix in Prefixes)
        {
            var expected = ReferenceScan(p, prefix);
            var actual = p.GetParametersWithPrefix(prefix).ConvertAll(s => s.Path);
            Assert.That(actual, Is.EqualTo(expected), $"prefix '{prefix}'");
        }
    }

    [Test]
    public void RepeatedCallsAgreeWithTheFirst()
    {
        var p = Load();
        foreach (var prefix in Prefixes)
        {
            var first = p.GetParametersWithPrefix(prefix).ConvertAll(s => s.Path);
            var second = p.GetParametersWithPrefix(prefix).ConvertAll(s => s.Path);
            Assert.That(second, Is.EqualTo(first), $"prefix '{prefix}'");
        }
    }

    [Test]
    public void CallersGetIndependentLists()
    {
        var p = Load();
        var a = p.GetParametersWithPrefix("Studio Set Part/");
        var b = p.GetParametersWithPrefix("Studio Set Part/");
        Assert.That(a, Is.Not.SameAs(b));

        var countBefore = b.Count;
        a.Clear(); // mutating one caller's list must not affect anyone else's
        Assert.That(b, Has.Count.EqualTo(countBefore));
        Assert.That(p.GetParametersWithPrefix("Studio Set Part/"), Has.Count.EqualTo(countBefore));
    }

    [Test]
    public void OrdinalAndCultureComparisonAgreeOnEveryPathInTheDatabase()
    {
        // The rewrite switched StartsWith to Ordinal. That is only safe if it cannot change which
        // parameters match, so check it against every path/prefix pair the app can produce.
        var p = Load();
        var allPaths = p.GetParametersWithPrefix("").ConvertAll(s => s.Path);
        Assert.That(allPaths, Is.Not.Empty);

        foreach (var prefix in Prefixes)
        foreach (var path in allPaths)
            Assert.That(path.StartsWith(prefix, StringComparison.Ordinal),
                Is.EqualTo(path.StartsWith(prefix)),
                $"ordinal/culture disagree for path '{path}' and prefix '{prefix}'");
    }

    [Test]
    public void EmptyPrefixReturnsEverythingAndUnknownPrefixNothing()
    {
        var p = Load();
        Assert.That(p.GetParametersWithPrefix(""), Has.Count.GreaterThan(1000));
        Assert.That(p.GetParametersWithPrefix("zzz-no-such-prefix"), Is.Empty);
    }
}
