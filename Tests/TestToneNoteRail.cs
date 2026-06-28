using Integra7AuralAlchemist.ViewModels;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestToneNoteRail
{
    [Test]
    public void InPianoRange_Boundaries()
    {
        Assert.That(new ToneNoteViewModel(20).InPianoRange, Is.False); // below A0
        Assert.That(new ToneNoteViewModel(21).InPianoRange, Is.True);  // A0
        Assert.That(new ToneNoteViewModel(108).InPianoRange, Is.True); // C8
        Assert.That(new ToneNoteViewModel(109).InPianoRange, Is.False); // above C8
    }

    [Test]
    public void NoteName_UsesMidiNoteConvention()
        => Assert.That(new ToneNoteViewModel(60).NoteName, Is.EqualTo("C4"));

    [Test]
    public void Rail_Has128Rows_LowNoteAtBottom()
    {
        var rail = new ToneNoteRailViewModel();
        Assert.That(rail.Notes.Count, Is.EqualTo(128));
        Assert.That(rail.Notes[0].Note, Is.EqualTo(127));   // top
        Assert.That(rail.Notes[127].Note, Is.EqualTo(0));   // bottom
    }
}
