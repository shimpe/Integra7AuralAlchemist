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
        var block = StudioSetDomainNames.All[1]; // Offset2/Studio Set Common Chorus
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        const string path = "Studio Set Common Chorus/Chorus Parameter 1/GM2 Pre-LPF";

        // On an unread domain the Chorus Type discriminator's registered value is "", which matches
        // no parval, so every parameter conditional on it -- including this one -- is invalid in
        // context. That makes it absent from (true, false) and present only in (true, true); picking
        // it here (rather than an always-valid parameter) pins that ValidateParametersAreKnown must
        // query (true, true) -- a plausible copy-paste from CaptureAsync's (true, false) fix would
        // silently make this method reject perfectly valid snapshots.
        Assert.That(d.GetRelevantParameters(true, false).Select(p => p.ParSpec.Path), Does.Not.Contain(path));
        Assert.That(d.GetRelevantParameters(true, true).Select(p => p.ParSpec.Path), Does.Contain(path));

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2, [new SnapshotValue(path, "0")]),
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

    /// <summary>Reserved parameters have <c>repr:null</c> (no enum table), so
    /// <c>DisplayValueToRawValueConverter.UpdateFromDisplayedValue</c> takes its numeric-mapping branch
    /// for them rather than the enum-lookup branch every other test in this file exercises. Nothing
    /// covered that path before this test. "Studio Set Common/Reserved30" is unconditional (no
    /// discriminator), so it needs no device read to be valid in context -- it is simply omitted from
    /// GetRelevantParameters()'s plain default, which excludes reserved parameters, and that is exactly
    /// the omission fix 1 (CaptureAsync's switch to (true, false)) closed.</summary>
    [Test]
    public void A_reserved_parameter_survives_capture_then_json_then_validate()
    {
        var domain = BuildDomain(new TestFailedReadKeepsValues.SilentApi());
        var block = StudioSetDomainNames.All[0]; // Offset2/Studio Set Common
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        const string path = "Studio Set Common/Reserved30";

        // Pins the bug fix 1 closed: the plain default excludes reserved parameters outright, so the
        // old capture (GetRelevantParameters()) silently dropped this one.
        Assert.That(d.GetRelevantParameters().Select(p => p.ParSpec.Path), Does.Not.Contain(path),
            "the plain default must still exclude reserved parameters -- this pins the bug the fix closed");

        // CaptureAsync now uses (true, false): reserved included, invalid-in-context still excluded.
        var reserved = d.GetRelevantParameters(true, false).Single(p => p.ParSpec.Path == path);
        Assert.That(reserved.ParSpec.Reserved, Is.True);
        Assert.That(reserved.ParSpec.Repr, Is.Null,
            "reserved parameters take the numeric-mapping branch of UpdateFromDisplayedValue, not the enum branch");

        // capture
        var captured = new SnapshotValue(reserved.ParSpec.Path, "42");
        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2, [captured]),
        ]);

        // JSON
        var restored = StudioSetSnapshot.FromJson(StudioSetSnapshot.ToJson(snapshot));

        // validate
        Assert.DoesNotThrow(() => StudioSetSnapshotService.ValidateParametersAreKnown(domain, restored));

        // Applying the restored value exercises the numeric-mapping branch end to end: it must not
        // throw, and the value must survive unchanged.
        var value = restored.Domains[0].Values[0];
        Assert.DoesNotThrow(() => d.ModifySingleParameterDisplayedValue(value.Path, value.Value));
        Assert.That(d.LookupSingleParameterDisplayedValue(value.Path), Is.EqualTo("42"));
    }

    [Test]
    public async Task Sends_nothing_when_restoring_a_snapshot_with_an_unknown_block()
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

    [Test]
    public async Task Sends_nothing_when_restoring_a_snapshot_with_an_unknown_parameter_path()
    {
        // Distinct from the case above: an unknown block trips ValidateBlocksAreKnown and never
        // reaches ValidateParametersAreKnown at all. A valid block with a bogus path is the only way
        // to prove RestoreAsync sends nothing when it is ValidateParametersAreKnown that rejects it.
        var api = new TestFailedReadKeepsValues.SilentApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[0];

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2,
            [
                new SnapshotValue("Studio Set Common/Not A Real Parameter", "0"),
            ]),
        ]);

        Assert.ThrowsAsync<SnapshotFormatException>(
            async () => await StudioSetSnapshotService.RestoreAsync(domain, snapshot, new NeverUsedLease()));

        Assert.That(api.Requests, Is.EqualTo(0), "an invalid snapshot must be rejected before any read");
        Assert.That(api.Transmissions, Is.EqualTo(0), "an invalid snapshot must be rejected before any write");
    }

    [Test]
    public void Every_studio_set_block_resolves_to_its_own_domain()
    {
        // GetDomain falls back to an unrelated block rather than throwing, so a typo in
        // StudioSetDomainNames would silently capture and restore the wrong addresses.
        var domain = BuildDomain(new TestFailedReadKeepsValues.SilentApi());
        foreach (var (start, offset, offset2) in StudioSetDomainNames.All)
            Assert.That(domain.GetDomain(start, offset, offset2).Offset2AddressName, Is.EqualTo(offset2));
    }

    /// <summary>The bulk write goes out as one DT1 at the block's base address, so the assembled payload
    /// has to tile the block exactly. It does not when a discriminator holds a value no variant matches:
    /// no variant of the dependent group is context-valid, that group contributes zero bytes, and every
    /// parameter after it lands one group too early -- silent corruption of addresses the code never
    /// names. Reachable from a hand-edited file or a snapshot captured against a build with different
    /// enum strings (see StudioSetSnapshot's format-version-1 note, and note that
    /// UpdateFromDisplayedValue assigns the unmatched string to StringValue as-is, which is what poisons
    /// the context). This feature is the only caller of the bulk write in the whole application.</summary>
    [Test]
    public void Refuses_to_bulk_write_a_block_whose_discriminator_matches_no_variant()
    {
        var api = new TestFailedReadKeepsValues.SilentApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[1]; // Offset2/Studio Set Common Chorus
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);

        d.ModifySingleParameterDisplayedValue("Studio Set Common Chorus/Chorus Type", "Not A Real Type");

        var e = Assert.ThrowsAsync<InvalidOperationException>(async () => await d.WriteToIntegraAsync());
        Assert.That(e!.Message, Does.Contain("Offset2/Studio Set Common Chorus"));
        Assert.That(api.Transmissions, Is.EqualTo(0), "a misaligned payload must never reach the device");
    }

    [Test]
    public void Bulk_writes_a_block_whose_discriminators_all_hold_legal_values()
    {
        var api = new TestFailedReadKeepsValues.SilentApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[1]; // Offset2/Studio Set Common Chorus
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);

        // "Off" is a real Chorus Type, so exactly one variant of each Chorus Parameter group is valid in
        // context and the payload tiles the block. Setting it also matters for what this test proves: an
        // unread domain's Chorus Type is "", which matches nothing, so without this line the guard would
        // fire here too and the test could not tell a working guard from one that refuses everything.
        d.ModifySingleParameterDisplayedValue("Studio Set Common Chorus/Chorus Type", "Off");

        Assert.DoesNotThrowAsync(async () => await d.WriteToIntegraAsync());
        Assert.That(api.Transmissions, Is.EqualTo(1));
    }
}
