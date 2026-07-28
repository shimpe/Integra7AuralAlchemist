using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Tests;

/// <summary>The step LFO's parameters, as the database really carries them. The editor is built on these
/// exact ranges, and a silent change to one would move every bar it draws.</summary>
public class StepLfoParametersTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    /// <summary>It had no name table at all, so it displayed as 0 or 1 -- in the raw grid, in snapshot
    /// files and in comparisons. The names are the instrument's own two types, with what each does.</summary>
    [Test]
    public void The_step_type_is_named_rather_than_numbered()
    {
        var spec = _parameters.Lookup("PCM Synth Tone Partial/LFO Step Type");

        Assert.That(spec.Repr, Is.Not.Null);
        Assert.That(spec.Repr![0], Is.EqualTo("Type 1 (stepped)"));
        Assert.That(spec.Repr[1], Is.EqualTo("Type 2 (smoothed)"));
    }

    /// <summary>Sixteen steps, each raw 28..100 shown as -36..+36. The geometry is built from the
    /// displayed range, so this is what pins it.</summary>
    [Test]
    public void There_are_sixteen_steps_over_a_bipolar_range()
    {
        var steps = Enumerable.Range(1, 16)
            .Select(n => _parameters.Lookup($"PCM Synth Tone Partial/LFO Step {n}"))
            .ToList();

        Assert.That(steps.Select(s => s.OMin), Is.All.EqualTo(-36));
        Assert.That(steps.Select(s => s.OMax), Is.All.EqualTo(36));
        Assert.That(steps.Select(s => s.IMin), Is.All.EqualTo(28));
        Assert.That(steps.Select(s => s.IMax), Is.All.EqualTo(100));
    }
}
