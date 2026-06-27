using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestMidiNote
{
    [TestCase(0, "C-1")]
    [TestCase(12, "C0")]
    [TestCase(60, "C4")]
    [TestCase(61, "C#4")]
    [TestCase(69, "A4")]
    [TestCase(127, "G9")]
    public void Name_uses_the_octave_convention(int note, string expected)
        => Assert.That(MidiNote.Name(note), Is.EqualTo(expected));

    [TestCase(60, false)] // C
    [TestCase(61, true)]  // C#
    [TestCase(65, false)] // F
    [TestCase(66, true)]  // F#
    public void IsBlack_flags_accidentals(int note, bool expected)
        => Assert.That(MidiNote.IsBlack(note), Is.EqualTo(expected));

    [Test]
    public void IsC_true_only_at_octave_boundaries()
    {
        Assert.That(MidiNote.IsC(0), Is.True);
        Assert.That(MidiNote.IsC(60), Is.True);
        Assert.That(MidiNote.IsC(61), Is.False);
    }
}
