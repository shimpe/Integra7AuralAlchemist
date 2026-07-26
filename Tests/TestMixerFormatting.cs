using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class MixerFormattingTests
{
    [Test]
    public void Pan_is_labelled_the_way_the_instrument_labels_it()
    {
        Assert.That(MixerFormatting.PanLabel(0), Is.EqualTo("C"));
        Assert.That(MixerFormatting.PanLabel(-64), Is.EqualTo("L64"), "the hard-left end of the range");
        Assert.That(MixerFormatting.PanLabel(-1), Is.EqualTo("L1"));
        Assert.That(MixerFormatting.PanLabel(1), Is.EqualTo("R1"));
        Assert.That(MixerFormatting.PanLabel(63), Is.EqualTo("R63"), "the hard-right end");
    }
}

public class SoloPartMappingTests
{
    [Test]
    public void The_value_names_the_soloed_part_one_based()
    {
        Assert.That(SoloPartMapping.SoloedPart("1"), Is.EqualTo(0), "part 1 is index 0");
        Assert.That(SoloPartMapping.SoloedPart("16"), Is.EqualTo(15));
    }

    [Test]
    public void Anything_that_is_not_a_part_means_nothing_is_soloed()
    {
        Assert.That(SoloPartMapping.SoloedPart("OFF"), Is.Null);
        Assert.That(SoloPartMapping.SoloedPart(""), Is.Null);
        // Out of range rather than unparseable: a value this build does not expect must read as "no solo"
        // rather than as a part index that would light up the wrong strip -- or none, and then no strip
        // would show the solo the instrument is applying.
        Assert.That(SoloPartMapping.SoloedPart("0"), Is.Null);
        Assert.That(SoloPartMapping.SoloedPart("17"), Is.Null);
        // The parameter is declared nullable, so the null path is part of the contract rather than an
        // impossibility -- and int.TryParse answering false for it is what makes this the safe answer.
        Assert.That(SoloPartMapping.SoloedPart(null), Is.Null);
    }

    [Test]
    public void Soloing_a_part_writes_its_one_based_number_and_clearing_writes_the_off_option()
    {
        Assert.That(SoloPartMapping.ValueForPart(0), Is.EqualTo("1"));
        Assert.That(SoloPartMapping.ValueForPart(15), Is.EqualTo("16"));
        Assert.That(SoloPartMapping.Off, Is.EqualTo("OFF"));
    }

    [Test]
    public void The_off_option_is_the_one_the_parameter_offers()
    {
        // Taken from the parameter's own Options rather than assumed, so a build whose repr changed spelling
        // still clears solo instead of writing a string the device does not recognise -- an unmatched string
        // becomes raw 0 with no diagnostic in Release (see ParamString's UpdateFromDisplayedValue).
        Assert.That(SoloPartMapping.OffValue(["OFF", "1", "2"]), Is.EqualTo("OFF"));
        Assert.That(SoloPartMapping.OffValue(["Off", "1", "2"]), Is.EqualTo("Off"));
        Assert.That(SoloPartMapping.OffValue(["1", "2"]), Is.EqualTo("OFF"),
            "no non-part option at all: fall back to the spelling this build knows");
        // An options list that is empty because the parameter has not been read yet. The fallback covers it
        // for the same reason: writing nothing at all is not an option, and "OFF" is the spelling this
        // build knows.
        Assert.That(SoloPartMapping.OffValue([]), Is.EqualTo("OFF"));
    }
}
