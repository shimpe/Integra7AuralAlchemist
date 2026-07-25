using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class ReadOnlyParametersTests
{
    [Test]
    public void The_setup_studio_set_selectors_are_read_only()
    {
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set BS MSB"), Is.True);
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set BS LSB"), Is.True);
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set PC"), Is.True);
    }

    [Test]
    public void Everything_else_stays_editable()
    {
        // The Setup block's other used parameter, and a look-alike from another block.
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Sound Mode"), Is.False);
        Assert.That(ReadOnlyParameters.IsReadOnly("Studio Set Common/Studio Set Tempo"), Is.False);
        Assert.That(ReadOnlyParameters.IsReadOnly("Studio Set Part/Part Level"), Is.False);
    }

    [Test]
    public void Matching_is_exact()
    {
        Assert.That(ReadOnlyParameters.IsReadOnly("Setup/Studio Set PC "), Is.False);
        Assert.That(ReadOnlyParameters.IsReadOnly("setup/studio set pc"), Is.False);
        Assert.That(ReadOnlyParameters.IsReadOnly(""), Is.False);
    }
}
