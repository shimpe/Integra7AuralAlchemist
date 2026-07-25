using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class StudioSetSelectorTests
{
    [Test]
    public void The_setup_bank_select_and_program_change_select_the_studio_set()
    {
        Assert.That(StudioSetSelectors.Contains("Setup/Studio Set BS MSB"), Is.True);
        Assert.That(StudioSetSelectors.Contains("Setup/Studio Set BS LSB"), Is.True);
        Assert.That(StudioSetSelectors.Contains("Setup/Studio Set PC"), Is.True);
    }

    [Test]
    public void Nothing_else_does()
    {
        // The Setup block's other used parameter, and look-alikes from other blocks.
        Assert.That(StudioSetSelectors.Contains("Setup/Sound Mode"), Is.False);
        Assert.That(StudioSetSelectors.Contains("Studio Set Common/Studio Set Tempo"), Is.False);
        Assert.That(StudioSetSelectors.Contains("Studio Set Part/Tone Bank Select MSB"), Is.False);
    }

    [Test]
    public void Matching_is_exact()
    {
        Assert.That(StudioSetSelectors.Contains("Setup/Studio Set PC "), Is.False);
        Assert.That(StudioSetSelectors.Contains("setup/studio set pc"), Is.False);
        Assert.That(StudioSetSelectors.Contains(""), Is.False);
    }

    /// <summary>The selectors are read-only for exactly the reason they force a resync: the app cannot
    /// write them without desyncing itself from the device.</summary>
    [Test]
    public void Every_selector_is_read_only_and_ordinary_parameters_stay_editable()
    {
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set BS MSB"), Is.True);
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set BS LSB"), Is.True);
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set PC"), Is.True);
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Sound Mode"), Is.False);
        Assert.That(ReadOnlyParameters.IsReadOnly("Studio Set Part/Part Level"), Is.False);
    }
}
