using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public void Rejects_the_next_format_version_up()
    {
        // 3 is the version an off-by-one or a "<=" would most easily let through, and the one a future
        // build will really write, carrying fields this build would silently ignore.
        var json = StudioSetSnapshot.ToJson(Sample() with { FormatVersion = 3 });

        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson(json));
        Assert.That(e!.Message, Does.Contain("3"));
    }

    /// <summary>The on-disk shape of a version 1 file: display strings only, no Raw anywhere. Written
    /// by hand rather than serialised, because there is no version 1 object any more and a serialised
    /// one would carry today's fields.</summary>
    private const string VersionOneFile = """
        {
          "FormatVersion": 1,
          "Name": "World Pop Set",
          "Domains": [
            {
              "Start": "Temporary Studio Set",
              "Offset": "Offset/Not Used",
              "Offset2": "Offset2/Studio Set Common",
              "Values": [
                { "Path": "Studio Set Common/Studio Set Name", "Value": "World Pop Set" },
                { "Path": "Studio Set Common/Studio Set Tempo", "Value": "120" }
              ]
            }
          ]
        }
        """;

    [Test]
    public void Rejects_a_version_1_file()
    {
        // Version 1 carried no raw values, so restoring one would go through the display-string
        // conversion this whole format version exists to stop relying on. No version 1 file was ever
        // released, so refusing with a message naming the version beats silently restoring it the
        // weak way.
        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson(VersionOneFile));

        Assert.That(e!.Message, Does.Contain("1"));
        Assert.That(e.Message, Does.Contain("2"));
    }

    [Test]
    public void Round_trips_the_raw_value()
    {
        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common Reverb",
            [
                new SnapshotValue("Studio Set Common Reverb/Reverb Type", "Room1", 1),
                new SnapshotValue("Studio Set Common/Studio Set Name", "World Pop Set"),
            ]),
        ]);

        var json = StudioSetSnapshot.ToJson(snapshot);
        var restored = StudioSetSnapshot.FromJson(json);

        Assert.That(restored.FormatVersion, Is.EqualTo(2));
        Assert.That(restored.Domains[0].Values[0].Raw, Is.EqualTo(1));
        Assert.That(restored.Domains[0].Values[0].Value, Is.EqualTo("Room1"),
            "the display string stays in the file: these are meant to be read and diffed");
        Assert.That(restored.Domains[0].Values[1].Raw, Is.Null, "a text parameter has no raw form");
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

public class ToneDomainNamesTests
{
    private static readonly string[] AllToneTypes = ["PCMS", "PCMD", "SN-S", "SN-A", "SN-D"];

    /// <summary>A block's Offset2 ends in "Partial &lt;n&gt;" only for an actual partial block. This is
    /// deliberately anchored so it does not also match "PCM Synth Tone Partial Mix Table", a *common*
    /// block whose name happens to contain the word "Partial" too.</summary>
    private static readonly Regex PartialSuffix = new(@"Partial (\d+)$");

    private static Integra7Domain BuildDomain(IIntegra7Api api) =>
        new(api, new Integra7StartAddresses(), TestFailedReadKeepsValues.LoadParameters());

    [Test]
    public void Counts_the_right_number_of_blocks_per_engine()
    {
        // The partial counts come from Constants, not a literal, so a change to a NO_OF_PARTIALS_*
        // value fails here rather than only showing up as a mismatched capture much later.
        Assert.That(ToneDomainNames.For("PCMS", 0), Has.Count.EqualTo(4 + Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE));
        Assert.That(ToneDomainNames.For("PCMD", 0), Has.Count.EqualTo(4 + Constants.NO_OF_PARTIALS_PCM_DRUM));
        Assert.That(ToneDomainNames.For("SN-S", 0), Has.Count.EqualTo(2 + Constants.NO_OF_PARTIALS_SN_SYNTH_TONE));
        Assert.That(ToneDomainNames.For("SN-A", 0), Has.Count.EqualTo(2));
        Assert.That(ToneDomainNames.For("SN-D", 0), Has.Count.EqualTo(3 + Constants.NO_OF_PARTIALS_SN_DRUM));
    }

    [TestCase(0)]
    [TestCase(9)]
    public void Every_block_names_the_requested_part_in_its_start(int zeroBasedPartNo)
    {
        var expectedStart = $"Temporary Tone Part {zeroBasedPartNo + 1}";

        foreach (var toneType in AllToneTypes)
            Assert.That(ToneDomainNames.For(toneType, zeroBasedPartNo).Select(b => b.Start),
                Has.All.EqualTo(expectedStart), $"tone type {toneType}");
    }

    [TestCase("PCMS", 4)]
    [TestCase("PCMD", 4)]
    [TestCase("SN-S", 2)]
    [TestCase("SN-A", 2)]
    [TestCase("SN-D", 3)]
    public void Orders_common_blocks_before_ascending_partials(string toneType, int commonBlockCount)
    {
        var names = ToneDomainNames.For(toneType, 0);

        Assert.That(names.Take(commonBlockCount).Select(b => b.Offset2),
            Has.None.Matches<string>(o => PartialSuffix.IsMatch(o)),
            "the common blocks must all precede the partials");

        var partialNumbers = names.Skip(commonBlockCount)
            .Select(b => int.Parse(PartialSuffix.Match(b.Offset2).Groups[1].Value))
            .ToList();
        Assert.That(partialNumbers, Is.EqualTo(Enumerable.Range(1, partialNumbers.Count)),
            "partials must ascend starting from 1");
    }

    [Test]
    public void Every_tone_block_resolves_to_its_own_domain()
    {
        // GetDomain falls back to an unrelated block rather than throwing, so a typo in
        // ToneDomainNames would silently capture and restore the wrong addresses.
        var domain = BuildDomain(new TestFailedReadKeepsValues.SilentApi());

        foreach (var toneType in AllToneTypes)
        foreach (var (start, offset, offset2) in ToneDomainNames.For(toneType, 0))
            Assert.That(domain.GetDomain(start, offset, offset2).Offset2AddressName, Is.EqualTo(offset2),
                $"tone type {toneType}, block {offset2}");
    }

    [Test]
    public void Throws_for_an_unrecognised_tone_type()
    {
        var e = Assert.Throws<ArgumentException>(() => ToneDomainNames.For("bogus", 0));
        Assert.That(e!.Message, Does.Contain("bogus"));
    }

    [TestCase("PCMS")]
    [TestCase("PCMD")]
    [TestCase("SN-S")]
    [TestCase("SN-A")]
    [TestCase("SN-D")]
    public void IsKnownToneType_agrees_with_For_for_a_known_type(string toneType)
    {
        Assert.That(ToneDomainNames.IsKnownToneType(toneType), Is.True);
        Assert.DoesNotThrow(() => ToneDomainNames.For(toneType, 0));
    }

    [TestCase("bogus")]
    [TestCase("")]
    [TestCase("pcms")]
    public void IsKnownToneType_agrees_with_For_for_an_unknown_type(string toneType)
    {
        Assert.That(ToneDomainNames.IsKnownToneType(toneType), Is.False);
        Assert.Throws<ArgumentException>(() => ToneDomainNames.For(toneType, 0));
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

    /// <summary>A SilentApi whose reads succeed instead, with a well-formed all-zero reply of the
    /// requested length. Capture and restore both abort on a read the device does not answer -- by
    /// design, a half-read Studio Set must reach neither a file nor the instrument -- so a fake that
    /// answers is the only way to drive either of them without hardware. Zeros are a legitimate
    /// reading: raw 0 is a real value for every parameter in a Studio Set.</summary>
    private sealed class BlankReplyApi : TestFailedReadKeepsValues.SilentApi
    {
        /// <summary>The 11 header bytes FullyQualifiedParameter.ParseFromSysexReply skips, plus the
        /// checksum and F7 a real reply ends with -- the parser requires the reply to be strictly
        /// longer than header plus block, so the two trailing bytes are not padding for its own sake.</summary>
        private const int ReplyOverhead = 11 + 2;

        public override Task<byte[]> MakeDataRequestAsync(byte[] address, long size, IMidiLease? lease = null)
        {
            Requests++;
            return Task.FromResult(new byte[ReplyOverhead + (int)size]);
        }
    }

    /// <summary>Nothing in these tests reaches real MIDI -- BlankReplyApi ignores the lease it is
    /// handed -- so the lease that throws on every member doubles as proof of that.</summary>
    private static IMidiLease NoRealMidi() => new NeverUsedLease();

    /// <summary>The scenario the whole raw-value format exists for. "Not A Reverb Type Any More" stands
    /// in for a display string this build's parameter database no longer contains -- an enum entry
    /// renamed or reordered since the snapshot was captured. Restoring it as a string sets raw 0 with no
    /// diagnostic at all in Release, and because Reverb Type is a discriminator it also poisons the
    /// parser context, so the block's bulk write assembles the wrong number of bytes and is refused
    /// outright (see Refuses_to_bulk_write_a_block_whose_discriminator_matches_no_variant). With the raw
    /// value in the file the string is never consulted: the parameter lands on raw 1, the block tiles,
    /// and it goes out.</summary>
    [Test]
    public async Task Restores_from_the_raw_value_when_the_display_string_matches_nothing_in_the_repr()
    {
        var api = new BlankReplyApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[2]; // Offset2/Studio Set Common Reverb
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        const string path = "Studio Set Common Reverb/Reverb Type";

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2,
                [new SnapshotValue(path, "Not A Reverb Type Any More", 1)]),
        ]);

        await StudioSetSnapshotService.RestoreAsync(domain, snapshot, NoRealMidi());

        Assert.That(d.LookupSingleParameterDisplayedValue(path), Is.EqualTo("Room1"),
            "the raw value must win over a display string this build cannot resolve");
        Assert.That(api.Transmissions, Is.EqualTo(1), "the block must still tile, so it must still be sent");
    }

    [Test]
    public async Task Restores_a_value_with_no_raw_from_its_display_string()
    {
        // Restore reads Raw when it is there and falls back to the string when it is not. Capture only
        // omits Raw for text parameters, but the fallback has to be exercised on a parameter where the
        // two paths differ -- a hand-edited file can omit it anywhere.
        var api = new BlankReplyApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[2]; // Offset2/Studio Set Common Reverb
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        const string path = "Studio Set Common Reverb/Reverb Type";

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2, [new SnapshotValue(path, "Hall 1")]),
        ]);

        await StudioSetSnapshotService.RestoreAsync(domain, snapshot, NoRealMidi());

        Assert.That(d.LookupSingleParameterDisplayedValue(path), Is.EqualTo("Hall 1"));
        Assert.That(api.Transmissions, Is.EqualTo(1));
    }

    [Test]
    public async Task Restores_a_text_parameter_from_its_string_even_when_the_file_carries_a_raw()
    {
        // A text parameter's value IS its string; it has no raw form, and ApplyRawValue throws for one.
        // Capture never writes a Raw for one, but a hand-edited file can, so restore has to ask what
        // kind of parameter it is rather than trust the field's presence.
        var api = new BlankReplyApi();
        var domain = BuildDomain(api);
        var block = StudioSetDomainNames.All[0]; // Offset2/Studio Set Common
        var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);
        const string path = "Studio Set Common/Studio Set Name";

        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain(block.Start, block.Offset, block.Offset2,
                [new SnapshotValue(path, "World Pop Set", 42)]),
        ]);

        Assert.DoesNotThrowAsync(async () => await StudioSetSnapshotService.RestoreAsync(domain, snapshot, NoRealMidi()));
        Assert.That(d.LookupSingleParameterDisplayedValue(path), Is.EqualTo("World Pop Set"));
    }

    [Test]
    public async Task Captures_the_raw_value_next_to_the_displayed_one()
    {
        var api = new BlankReplyApi();
        var domain = BuildDomain(api);

        var snapshot = await StudioSetSnapshotService.CaptureAsync(domain, "x", NoRealMidi());

        Assert.That(snapshot.FormatVersion, Is.EqualTo(2));
        var common = snapshot.Domains[0].Values;
        // Every numeric and discrete parameter carries a raw value; only text ones do not.
        Assert.That(common.Find(v => v.Path == "Studio Set Common/Studio Set Tempo")!.Raw, Is.Not.Null);
        Assert.That(common.Find(v => v.Path == "Studio Set Common/Studio Set Name")!.Raw, Is.Null,
            "a text parameter has no raw form, so recording one would be a lie");
        Assert.That(common.TrueForAll(v => v.Value is not null),
            "the displayed value stays in every entry: it is what makes these files readable");
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
