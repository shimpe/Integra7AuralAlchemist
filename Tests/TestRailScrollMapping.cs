using Integra7AuralAlchemist.Models.Services;

namespace Tests;

[TestFixture]
public class TestRailScrollMapping
{
    // The tone rail: 128 rows of 22px = 2816px of content.
    private const int Count = 128;
    private const double Extent = 128 * 22.0;

    [Test]
    public void CentersMiddleCInTheViewport()
    {
        // Rail rows run note 127 down to 0, so middle C (60) is row 67.
        const double viewport = 600.0;
        var offset = RailScrollMapping.CenterOffset(67, Count, Extent, viewport);

        // Row 67 spans 1474..1496, centre 1485. Centring it means offset = 1485 - 300.
        Assert.That(offset, Is.EqualTo(1185.0).Within(0.001));

        // And the row really is inside the viewport, in its middle.
        var rowCentre = 67 * 22.0 + 11.0;
        Assert.That(rowCentre - offset, Is.EqualTo(viewport / 2).Within(0.001));
    }

    [Test]
    public void ClampsAtTheTopForEarlyRows()
    {
        Assert.That(RailScrollMapping.CenterOffset(0, Count, Extent, 600.0), Is.EqualTo(0.0));
        Assert.That(RailScrollMapping.CenterOffset(5, Count, Extent, 600.0), Is.EqualTo(0.0));
    }

    [Test]
    public void ClampsAtTheBottomForLateRows()
    {
        var max = Extent - 600.0;
        Assert.That(RailScrollMapping.CenterOffset(127, Count, Extent, 600.0), Is.EqualTo(max));
    }

    [Test]
    public void ReturnsZeroWhenEverythingFits()
    {
        // Viewport taller than the content: nothing to scroll.
        Assert.That(RailScrollMapping.CenterOffset(67, Count, Extent, Extent + 100), Is.EqualTo(0.0));
    }

    [Test]
    public void NeverLeavesTheScrollableRange()
    {
        var max = Extent - 600.0;
        for (var i = 0; i < Count; i++)
        {
            var offset = RailScrollMapping.CenterOffset(i, Count, Extent, 600.0);
            Assert.That(offset, Is.InRange(0.0, max), $"row {i}");
        }
    }

    [Test]
    public void HandlesAnUnmeasuredRail()
    {
        Assert.That(RailScrollMapping.CenterOffset(67, Count, 0, 600.0), Is.EqualTo(0.0));
        Assert.That(RailScrollMapping.CenterOffset(67, 0, Extent, 600.0), Is.EqualTo(0.0));
        Assert.That(RailScrollMapping.CenterOffset(-1, Count, Extent, 600.0), Is.EqualTo(0.0));
    }
}
