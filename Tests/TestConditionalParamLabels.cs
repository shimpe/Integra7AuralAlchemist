using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestConditionalParamLabels
{
    [Test]
    public void Strips_value_name_prefix()
    {
        var r = ConditionalParamLabels.FriendlyNames("Equalizer",
            new[] { "Equalizer Low Freq", "Equalizer Low Gain" });
        Assert.That(r, Is.EqualTo(new[] { "Low Freq", "Low Gain" }));
    }

    [Test]
    public void Common_prefix_when_value_name_differs_from_param_prefix()
    {
        // SN-A: display "INT 001: Concert Grand", but the param prefix is "ConcertGrand".
        var r = ConditionalParamLabels.FriendlyNames("INT 001: Concert Grand",
            new[] { "ConcertGrand String Resonance", "ConcertGrand Lid" });
        Assert.That(r, Is.EqualTo(new[] { "String Resonance", "Lid" }));
    }

    [Test]
    public void Single_is_never_empty()
    {
        Assert.That(ConditionalParamLabels.FriendlyName("Chorus", "Chorus"), Is.EqualTo("Chorus"));
    }
}
