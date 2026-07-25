using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;

namespace Tests;

/// <summary>The answerable address lookup. <c>Integra7Domain.GetDomain</c> cannot say no -- an address
/// it does not recognise is logged and answered with an unrelated block -- so anything holding a triple
/// that did not come from a live domain, and about to write through it, has to ask
/// <c>TryGetDomain</c> instead. Undo/redo is the caller that does: its steps carry an address triple
/// recorded earlier, and applying one to the wrong block would change a part of the instrument the user
/// never touched.
///
/// No device: the fake API here never answers, which is irrelevant because nothing below reads.</summary>
[TestFixture]
public class DomainLookupTests
{
    private static Integra7Domain NewDomain() =>
        new(new TestFailedReadKeepsValues.SilentApi(), new Integra7StartAddresses(),
            TestFailedReadKeepsValues.LoadParameters());

    [Test]
    public void A_known_triple_resolves_to_the_block_that_was_asked_for()
    {
        var found = NewDomain().TryGetDomain("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 1", out var domain);

        Assert.That(found, Is.True);
        Assert.That(domain!.StartAddressName, Is.EqualTo("Temporary Studio Set"));
        Assert.That(domain.OffsetAddressName, Is.EqualTo("Offset/Not Used"));
        Assert.That(domain.Offset2AddressName, Is.EqualTo("Offset2/Studio Set Part 1"));
    }

    /// <summary>The one that matters. GetDomain answers this with <c>_parameterMapper.First().Value</c>
    /// -- a real, unrelated block a caller would then happily write into.</summary>
    [Test]
    public void An_unknown_triple_is_refused_rather_than_answered_with_some_other_block()
    {
        var i7 = NewDomain();

        var found = i7.TryGetDomain("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Part 99", out var domain);

        Assert.That(found, Is.False);
        Assert.That(domain, Is.Null);
        Assert.That(i7.GetDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 99"),
            Is.Not.Null,
            "GetDomain still hands back a block for the same address -- which is exactly why TryGetDomain exists");
    }
}
