using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which patches are the same sound saved more than once.</summary>
public class DuplicateGroupsTests
{
    private static (string Path, RawVector Vector) Entry(string path, string engine, params long[] values) =>
        (path, new RawVector(SnapshotKinds.Tone, engine, values));

    [Test]
    public void Identical_vectors_are_a_group()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 1, 2, 3)], threshold: 0);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0], Is.EqualTo(new[] { "a.json", "b.json" }));
    }

    [Test]
    public void Nothing_alike_is_no_groups()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 9, 9, 9)], threshold: 1);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>The threshold is a count of differing parameters, and it is inclusive: "at most N".</summary>
    [Test]
    public void The_threshold_is_inclusive()
    {
        var pair = new[] { Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "SN-S", 1, 2, 9) };

        Assert.That(DuplicateGroups.Find(pair, threshold: 1), Has.Count.EqualTo(1));
        Assert.That(DuplicateGroups.Find(pair, threshold: 0), Is.Empty);
    }

    /// <summary>Different engines are never compared: the same position in two engines' vectors is two
    /// different parameters, so the count would be meaningless even where the lengths happened to match.
    /// </summary>
    [Test]
    public void Engines_are_never_mixed()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2, 3), Entry("b.json", "PCMS", 1, 2, 3)], threshold: 0);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>And neither are a tone and a Studio Set, which is what the kind is in the key for.</summary>
    [Test]
    public void A_tone_and_a_studio_set_never_pair()
    {
        var groups = DuplicateGroups.Find(
            [("a.json", new RawVector(SnapshotKinds.Tone, "SN-S", [1, 2])),
             ("b.json", new RawVector(SnapshotKinds.StudioSet, null, [1, 2]))], threshold: 0);

        Assert.That(groups, Is.Empty);
    }

    /// <summary>Grouping is transitive, and deliberately: A is near B and B is near C, so all three are one
    /// group even though A and C differ by more than the threshold. The panel says "each differs in at most
    /// N from at least one other here" rather than implying every pair is alike.</summary>
    [Test]
    public void Grouping_is_transitive()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 0, 0), Entry("b.json", "SN-S", 1, 0), Entry("c.json", "SN-S", 1, 1)],
            threshold: 1);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0], Has.Count.EqualTo(3));
    }

    /// <summary>Two families that turn out to be one. This is the case the transitive test above does not
    /// reach: there, every alike pair either starts a group or joins an existing one, so the branch that
    /// merges two groups is never taken. Here a.json's family and c.json's family are each complete before
    /// e.json is found to be near a member of both, and the whole of the higher-numbered family has to
    /// survive being folded into the lower-numbered one -- which is what would silently lose members if a
    /// merge dropped a group instead of emptying it.</summary>
    [Test]
    public void Two_families_found_to_be_one_keep_every_member()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 0, 0, 0, 0), Entry("b.json", "SN-S", 1, 0, 0, 0),
             Entry("c.json", "SN-S", 0, 0, 1, 1), Entry("d.json", "SN-S", 0, 1, 1, 1),
             Entry("e.json", "SN-S", 0, 0, 0, 1)],
            threshold: 1);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0], Is.EqualTo(new[] { "a.json", "b.json", "c.json", "d.json", "e.json" }));
    }

    /// <summary>Vectors of different lengths are the same engine written by two builds of this
    /// application, one of which knew a parameter the other did not. Comparing them positionally would
    /// line up the wrong parameters from the first difference onwards, so they are simply not compared.
    /// </summary>
    [Test]
    public void Vectors_of_different_lengths_are_not_compared()
    {
        var groups = DuplicateGroups.Find(
            [Entry("a.json", "SN-S", 1, 2), Entry("b.json", "SN-S", 1, 2, 3)], threshold: 5);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public void An_empty_library_has_no_groups()
    {
        Assert.That(DuplicateGroups.Find([], threshold: 5), Is.Empty);
    }

    /// <summary>Order is fixed so that two scans of the same folder present the same list. Within a group,
    /// by path; between groups, by the first path.</summary>
    [Test]
    public void Groups_and_their_members_are_in_a_stable_order()
    {
        var groups = DuplicateGroups.Find(
            [Entry("z.json", "SN-S", 5, 5), Entry("m.json", "SN-S", 5, 5),
             Entry("a.json", "SN-S", 9, 9), Entry("b.json", "SN-S", 9, 9)], threshold: 0);

        Assert.That(groups[0], Is.EqualTo(new[] { "a.json", "b.json" }));
        Assert.That(groups[1], Is.EqualTo(new[] { "m.json", "z.json" }));
    }
}
