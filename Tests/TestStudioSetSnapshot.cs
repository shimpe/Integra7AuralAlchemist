using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class StudioSetSnapshotTests
{
    private static StudioSetSnapshot Sample() => new(
        StudioSetSnapshot.CurrentFormatVersion,
        "World Pop Set",
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
            [
                new SnapshotValue("Studio Set Common/Studio Set Name", "World Pop Set"),
                new SnapshotValue("Studio Set Common/Studio Set Tempo", "120"),
            ]),
        ]);

    [Test]
    public void Round_trips_through_json()
    {
        var restored = StudioSetSnapshot.FromJson(StudioSetSnapshot.ToJson(Sample()));

        Assert.That(restored.Name, Is.EqualTo("World Pop Set"));
        Assert.That(restored.Domains, Has.Count.EqualTo(1));
        Assert.That(restored.Domains[0].Offset2, Is.EqualTo("Offset2/Studio Set Common"));
        Assert.That(restored.Domains[0].Values[1].Path, Is.EqualTo("Studio Set Common/Studio Set Tempo"));
        Assert.That(restored.Domains[0].Values[1].Value, Is.EqualTo("120"));
    }

    [Test]
    public void Rejects_a_future_format_version()
    {
        var json = StudioSetSnapshot.ToJson(Sample() with { FormatVersion = 99 });

        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson(json));
        Assert.That(e!.Message, Does.Contain("99"));
    }

    [Test]
    public void Rejects_something_that_is_not_a_snapshot()
    {
        Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson("not json at all"));
    }

    [Test]
    public void Keeps_parameters_in_the_order_they_were_captured()
    {
        // Restoring depends on this: a discriminator has to be applied before the parameters that
        // only exist because of its value.
        var ordered = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("Studio Set Common Chorus/Chorus Type", "Delay"),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)", "ms"),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 2/Delay Left ms", "120"),
            ]),
        ]);

        var restored = StudioSetSnapshot.FromJson(StudioSetSnapshot.ToJson(ordered));

        Assert.That(restored.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(
            ordered.Domains[0].Values.ConvertAll(v => v.Path)));
    }

    [Test]
    public void Rejects_a_file_that_is_missing_its_contents()
    {
        // System.Text.Json fills a missing constructor parameter with default, so a truncated file
        // whose version happens to be right would otherwise load "successfully" as a snapshot with a
        // null name and null domains, and fail much later.
        Assert.Throws<SnapshotFormatException>(
            () => StudioSetSnapshot.FromJson($$"""{"FormatVersion":{{StudioSetSnapshot.CurrentFormatVersion}}}"""));
    }

    [Test]
    public void Rejects_a_domain_with_no_address()
    {
        // Restoring calls GetDomain(Start, Offset, Offset2) directly; a null there is a
        // NullReferenceException the moment restore runs, not a graceful failure.
        Assert.Throws<SnapshotFormatException>(
            () => StudioSetSnapshot.FromJson($$"""
                {"FormatVersion":{{StudioSetSnapshot.CurrentFormatVersion}},"Name":"x","Domains":[{"Values":[]}]}
                """));
    }

    [Test]
    public void Rejects_a_parameter_with_no_value()
    {
        // Restoring calls ModifySingleParameterDisplayedValue(Path, Value) directly; a null Value
        // there is a NullReferenceException the moment restore runs.
        Assert.Throws<SnapshotFormatException>(
            () => StudioSetSnapshot.FromJson($$"""
                {"FormatVersion":{{StudioSetSnapshot.CurrentFormatVersion}},"Name":"x","Domains":[
                    {"Start":"s","Offset":"o","Offset2":"o2","Values":[{"Path":"p"}]}
                ]}
                """));
    }

    [Test]
    public void Rejects_a_snapshot_with_no_blocks()
    {
        // A captured Studio Set always has blocks. An empty list means a truncated capture, and
        // restoring it would silently do nothing.
        Assert.Throws<SnapshotFormatException>(
            () => StudioSetSnapshot.FromJson(
                $$"""{"FormatVersion":{{StudioSetSnapshot.CurrentFormatVersion}},"Name":"x","Domains":[]}"""));
    }
}

public class StudioSetDomainNamesTests
{
    [Test]
    public void Lists_five_common_blocks_and_three_per_part()
    {
        var names = StudioSetDomainNames.All;

        Assert.That(names, Has.Count.EqualTo(5 + 3 * 16));
        Assert.That(names[0], Is.EqualTo(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 16")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part EQ 1")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set MIDI Channel 1")));
    }

    [Test]
    public void Has_no_duplicates()
    {
        Assert.That(StudioSetDomainNames.All, Is.Unique);
    }
}

public class StudioSetSnapshotServiceTests
{
    private static StudioSetSnapshot SnapshotFromBlocks(
        IEnumerable<(string Start, string Offset, string Offset2)> blocks) => new(
        StudioSetSnapshot.CurrentFormatVersion,
        "x",
        new List<SnapshotDomain>(
            blocks.Select(b => new SnapshotDomain(b.Start, b.Offset, b.Offset2, []))));

    [Test]
    public void Keeps_a_snapshot_whose_blocks_are_all_known()
    {
        var snapshot = SnapshotFromBlocks(StudioSetDomainNames.All);

        Assert.DoesNotThrow(() => StudioSetSnapshotService.ValidateBlocksAreKnown(snapshot));
    }

    [Test]
    public void Rejects_a_snapshot_with_a_made_up_block()
    {
        var snapshot = SnapshotFromBlocks(
        [
            StudioSetDomainNames.All[0],
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Not A Real Block"),
        ]);

        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshotService.ValidateBlocksAreKnown(snapshot));
        Assert.That(e!.Message, Does.Contain("Offset2/Not A Real Block"));
    }

    [Test]
    public void Rejects_a_block_whose_start_does_not_match_even_if_the_offset2_is_real()
    {
        var snapshot = SnapshotFromBlocks(
        [
            ("System", "Offset/Not Used", "Offset2/Studio Set Common"),
        ]);

        // A real Offset2 is deliberately mixed in with the wrong Start: a message naming only "Offset2/
        // Studio Set Common" (which is, in isolation, a real block) would pass even though the message
        // named the wrong culprit. It must name "System", the field that is actually wrong.
        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshotService.ValidateBlocksAreKnown(snapshot));
        Assert.That(e!.Message, Does.Contain("System"));
    }

    private static Integra7Domain BuildDomain(IIntegra7Api api) =>
        new(api, new Integra7StartAddresses(), TestFailedReadKeepsValues.LoadParameters());

    /// <summary>A lease whose every member throws. Used to prove validation fails before any MIDI
    /// traffic: if RestoreAsync ever touched the lease, the test would fail with the wrong exception
    /// type instead of the SnapshotFormatException validation is expected to throw.</summary>
    private sealed class NeverUsedLease : IMidiLease
    {
        private static NotSupportedException Bug() =>
            new("Validation must fail before any MIDI traffic, so nothing should ever touch the lease.");

        public Task SendAsync(byte[] data) => throw Bug();
        public Task<byte[]> RequestAsync(byte[] request, IReplyMatcher expected) => throw Bug();
        public Task<byte[]> ReadNextAsync(IReplyMatcher expected) => throw Bug();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public void Keeps_a_snapshot_whose_parameters_all_exist()
    {
        var domain = BuildDomain(new TestFailedReadKeepsValues.SilentApi());
        var block = StudioSetDomainNames.All[0];
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        // Existence check only (true, true): built from the same live parameter list
        // ValidateParametersAreKnown itself queries, so this cannot drift from what "known" means.
        var realPaths = d.GetRelevantParameters(true, true).Select(p => p.ParSpec.Path).Take(3).ToList();
        Assert.That(realPaths, Is.Not.Empty, "the fixture needs real parameters to validate against");

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2,
                realPaths.Select(p => new SnapshotValue(p, "0")).ToList()),
        ]);

        Assert.DoesNotThrow(() => StudioSetSnapshotService.ValidateParametersAreKnown(domain, snapshot));
    }

    [Test]
    public void Rejects_a_snapshot_with_a_made_up_parameter_path()
    {
        var domain = BuildDomain(new TestFailedReadKeepsValues.SilentApi());
        var block = StudioSetDomainNames.All[0];

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2,
            [
                new SnapshotValue("Studio Set Common/Not A Real Parameter", "0"),
            ]),
        ]);

        var e = Assert.Throws<SnapshotFormatException>(
            () => StudioSetSnapshotService.ValidateParametersAreKnown(domain, snapshot));
        Assert.That(e!.Message, Does.Contain("Studio Set Common/Not A Real Parameter"));
    }

    [Test]
    public async Task Sends_nothing_when_restoring_an_invalid_snapshot()
    {
        var api = new TestFailedReadKeepsValues.SilentApi();
        var domain = BuildDomain(api);

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Not A Real Block", []),
        ]);

        Assert.ThrowsAsync<SnapshotFormatException>(
            async () => await StudioSetSnapshotService.RestoreAsync(domain, snapshot, new NeverUsedLease()));

        Assert.That(api.Requests, Is.EqualTo(0), "an invalid snapshot must be rejected before any read");
        Assert.That(api.Transmissions, Is.EqualTo(0), "an invalid snapshot must be rejected before any write");
    }
}
