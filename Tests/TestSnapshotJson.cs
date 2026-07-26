using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The version 3 file shape: parameter data nested by the three address names and then by the
/// parameter path's own '/'. What these tests are really protecting is the order values arrive in --
/// <c>StudioSetSnapshotService.ApplyBlockValues</c> applies a block's values in file order because a
/// discriminator has to be applied before the parameters that only exist under its value -- and the fact
/// that a leaf carries both the raw value a restore writes and the display string that makes the file
/// readable.</summary>
public class SnapshotJsonTests
{
    /// <summary>Two blocks, and within the first one a discriminator followed by two parameters that only
    /// exist because of its value. Deliberately in an order that is not alphabetical in any of the three
    /// dimensions that could be sorted -- the values within a block, the path segments, or the blocks
    /// themselves -- so that anything which sorted rather than preserved would show up.</summary>
    private static Integra7Snapshot Ordered() => new(
        Integra7Snapshot.CurrentFormatVersion,
        "World Pop Set",
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common Chorus",
            [
                new SnapshotValue("Studio Set Common Chorus/Chorus Type", "Delay", 3),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)", "ms", 0),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 2/Delay Left ms", "120", 120),
            ]),
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
            [
                new SnapshotValue("Studio Set Common/Studio Set Name", "World Pop Set"),
                new SnapshotValue("Studio Set Common/Studio Set Tempo", "120", 120),
            ]),
        ]);

    /// <summary>The test the whole reshaping exists for. A restore applies a block's values in the order
    /// the file lists them; a chorus type arriving after the knobs that only exist under it is a restore
    /// that writes values nobody asked for, and it would fail on hardware rather than here.
    ///
    /// This is also what says the reader does not go through a dictionary. A
    /// <c>Dictionary&lt;string, …&gt;</c> would pass this test today -- it preserves insertion order
    /// until something is removed from it -- and that is the point: it would pass by accident, and stop
    /// passing at some later, quieter moment.</summary>
    [Test]
    public void Values_come_back_in_the_order_they_went_in()
    {
        var written = Ordered();

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(written));

        Assert.That(read.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(
            written.Domains[0].Values.ConvertAll(v => v.Path)),
            "a discriminator must not arrive after the parameters that depend on it");
    }

    [Test]
    public void Blocks_come_back_in_the_order_they_went_in()
    {
        // "Studio Set Common Chorus" before "Studio Set Common", which is neither alphabetical nor the
        // order StudioSetDomainNames lists them in -- so this cannot pass by coincidence.
        var written = Ordered();

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(written));

        Assert.That(read.Domains.ConvertAll(d => d.Offset2), Is.EqualTo(
            written.Domains.ConvertAll(d => d.Offset2)));
    }

    [Test]
    public void Blocks_nest_by_start_then_offset_then_offset2()
    {
        var json = Integra7Snapshot.ToJson(Ordered());

        var start = json.IndexOf("\"Temporary Studio Set\"", StringComparison.Ordinal);
        var offset = json.IndexOf("\"Offset/Not Used\"", StringComparison.Ordinal);
        var offset2 = json.IndexOf("\"Offset2/Studio Set Common Chorus\"", StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThan(0));
        Assert.That(offset, Is.GreaterThan(start), "the offset nests inside the start");
        Assert.That(offset2, Is.GreaterThan(offset), "and the block inside the offset");

        // Both blocks share the Start and the Offset, so those must be written once and not once per
        // block -- a second copy of either would be a repeated key, which this build's own reader
        // refuses. A real Studio Set is 53 blocks sharing one Start and one Offset, so getting this
        // wrong would make every file this build writes unreadable by it.
        Assert.That(json.Split("\"Temporary Studio Set\"").Length - 1, Is.EqualTo(1));
        Assert.That(json.Split("\"Offset/Not Used\"").Length - 1, Is.EqualTo(1));
    }

    [Test]
    public void The_parameter_data_is_written_last()
    {
        // So that a later reader can take a file's head -- its name, its kind, whatever metadata this
        // record grows -- and stop before the ~4000 values it has no use for.
        var json = Integra7Snapshot.ToJson(Ordered());

        var blocks = json.IndexOf("\"Blocks\"", StringComparison.Ordinal);
        Assert.That(blocks, Is.GreaterThan(0));
        foreach (var property in new[] { "FormatVersion", "Name", "Kind", "ToneType" })
            Assert.That(json.IndexOf($"\"{property}\"", StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(blocks), $"{property} must be written before Blocks");
    }

    [Test]
    public void A_numeric_parameter_is_a_raw_value_and_a_display_string()
    {
        var json = Integra7Snapshot.ToJson(Ordered());

        // On one line, deliberately: an indented Utf8JsonWriter would otherwise put each element of the
        // pair on its own line, which would give back most of what the nesting saved and turn a
        // one-parameter change into a three-line diff.
        Assert.That(json, Does.Contain("\"Studio Set Tempo\": [120,\"120\"]"));

        var read = Integra7Snapshot.FromJson(json);
        var tempo = read.Domains[1].Values[1];
        Assert.That(tempo.Path, Is.EqualTo("Studio Set Common/Studio Set Tempo"));
        Assert.That(tempo.Raw, Is.EqualTo(120), "the raw value is what a restore writes");
        Assert.That(tempo.Value, Is.EqualTo("120"),
            "the display string stays in the file: these are meant to be read and diffed");
    }

    [Test]
    public void A_text_parameter_is_a_bare_string_and_comes_back_with_no_raw()
    {
        // A text parameter has no raw form at all -- its value IS the string -- so writing a pair for one
        // would be inventing a number, and reading Raw back as null is what tells a restore to apply the
        // string rather than call ApplyRawValue, which throws for a text parameter.
        var json = Integra7Snapshot.ToJson(Ordered());

        Assert.That(json, Does.Contain("\"Studio Set Name\": \"World Pop Set\""));

        var name = Integra7Snapshot.FromJson(json).Domains[1].Values[0];
        Assert.That(name.Value, Is.EqualTo("World Pop Set"));
        Assert.That(name.Raw, Is.Null);
    }

    [Test]
    public void A_display_string_that_needs_escaping_survives_the_pair()
    {
        // The pair is written as a pre-rendered JSON fragment rather than through WriteStartArray, so the
        // escaping is the one thing about it that could have been hand-rolled and wrong.
        var awkward = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2", [new SnapshotValue("a/b", "he said \"1/2\"\\ \n", 7)]),
        ]);

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(awkward));

        Assert.That(read.Domains[0].Values[0].Value, Is.EqualTo("he said \"1/2\"\\ \n"));
        Assert.That(read.Domains[0].Values[0].Raw, Is.EqualTo(7));
    }

    [Test]
    public void A_path_with_two_separators_nests_two_objects_deep()
    {
        var json = Integra7Snapshot.ToJson(Ordered());

        var block = json.IndexOf("\"Offset2/Studio Set Common Chorus\"", StringComparison.Ordinal);
        var outer = json.IndexOf("\"Studio Set Common Chorus\"", block, StringComparison.Ordinal);
        var inner = json.IndexOf("\"Chorus Parameter 1\"", outer, StringComparison.Ordinal);
        var leaf = json.IndexOf("\"Delay Left (ms-note)\"", inner, StringComparison.Ordinal);

        Assert.That(outer, Is.GreaterThan(block));
        Assert.That(inner, Is.GreaterThan(outer), "the second segment nests inside the first");
        Assert.That(leaf, Is.GreaterThan(inner), "and the parameter inside that");

        Assert.That(Integra7Snapshot.FromJson(json).Domains[0].Values[1].Path,
            Is.EqualTo("Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)"));
    }

    /// <summary>A path with no '/' at all. No parameter in the database is shaped like this -- every one
    /// of the 13980 has either one separator or two, which
    /// <see cref="Every_parameter_path_in_the_database_has_one_separator_or_two"/> is what says -- so this
    /// is reachable only from a hand-written file or a snapshot built in code. It still has to round-trip,
    /// because a reader that assumed two segments would mis-parse it into a path it invented.</summary>
    [Test]
    public void A_path_with_no_separator_is_a_single_leaf()
    {
        var flat = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("Tempo", "120", 120),
                new SnapshotValue("Name", "x"),
            ]),
        ]);

        var json = Integra7Snapshot.ToJson(flat);
        Assert.That(json, Does.Contain("\"Tempo\": [120,\"120\"]"));

        var read = Integra7Snapshot.FromJson(json);
        Assert.That(read.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(new[] { "Tempo", "Name" }));
        Assert.That(read.Domains[0].Values[0].Raw, Is.EqualTo(120));
        Assert.That(read.Domains[0].Values[1].Raw, Is.Null);
    }

    [Test]
    public void Two_parameters_sharing_a_prefix_land_in_one_object()
    {
        // The whole reason the format is nested: the shared prefix is written once. A writer that opened a
        // fresh object per value would emit the same key twice, which this build's own reader refuses --
        // so this is not merely about size.
        var shared = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)", "ms", 0),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 1/GM2 Pre-LPF", "0", 0),
            ]),
        ]);

        var json = Integra7Snapshot.ToJson(shared);

        Assert.That(json.Split("\"Chorus Parameter 1\"").Length - 1, Is.EqualTo(1),
            "the shared prefix is written once");

        var read = Integra7Snapshot.FromJson(json);
        Assert.That(read.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(new[]
        {
            "Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)",
            "Studio Set Common Chorus/Chorus Parameter 1/GM2 Pre-LPF",
        }));
    }

    [Test]
    public void A_run_of_values_returns_to_an_outer_object_without_repeating_its_key()
    {
        // Real blocks do exactly this: "Studio Set Common Reverb" holds both two-segment parameters and
        // three-segment ones, interleaved -- a plain parameter, then a whole "Reverb Parameter N" group,
        // then another plain parameter. The outer object must stay open across the inner one rather than
        // being closed and reopened, which would be a repeated key.
        var mixed = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("Reverb/Reverb Type", "Room1", 1),
                new SnapshotValue("Reverb/Reverb Parameter 1/Time", "50", 50),
                new SnapshotValue("Reverb/Reverb Level", "64", 64),
            ]),
        ]);

        var json = Integra7Snapshot.ToJson(mixed);
        Assert.That(json.Split("\"Reverb\"").Length - 1, Is.EqualTo(1));

        var read = Integra7Snapshot.FromJson(json);
        Assert.That(read.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(new[]
            { "Reverb/Reverb Type", "Reverb/Reverb Parameter 1/Time", "Reverb/Reverb Level" }));
    }

    [Test]
    public void Rejects_a_version_2_file()
    {
        // The shape version 2 wrote, by hand rather than serialised, because there is no version 2 writer
        // any more. Refused for the reason version 1 is: this build reads one shape, and reading half of
        // another -- version 2's Domains array is a property version 3's reader knows nothing about and
        // would skip, leaving a snapshot with no values at all -- would be worse than saying so.
        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson("""
            {
              "FormatVersion": 2,
              "Name": "World Pop Set",
              "Domains": [
                {
                  "Start": "Temporary Studio Set",
                  "Offset": "Offset/Not Used",
                  "Offset2": "Offset2/Studio Set Common",
                  "Values": [
                    { "Path": "Studio Set Common/Studio Set Name", "Value": "World Pop Set", "Raw": null },
                    { "Path": "Studio Set Common/Studio Set Tempo", "Value": "120", "Raw": 120 }
                  ]
                }
              ]
            }
            """));

        Assert.That(e!.Message, Does.Contain("2"), "the message has to name the version it found");
        Assert.That(e.Message, Does.Contain("3"), "and the version this build reads");
    }

    [Test]
    public void Rejects_the_same_parameter_twice_in_one_block()
    {
        // Reachable from a hand-edited file. Read into a dictionary the last one would silently win; read
        // into a list both would be applied, last one winning just as silently. A snapshot that quietly
        // drops or doubles a value is worse than one that will not open, and neither outcome is something
        // a user could ever notice.
        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": {
                "Temporary Studio Set": { "Offset/Not Used": { "Offset2/Studio Set Common": {
                  "Studio Set Common": {
                    "Studio Set Tempo": [120, "120"],
                    "Studio Set Tempo": [130, "130"]
                  } } } }
              }
            }
            """));

        Assert.That(e!.Message, Does.Contain("Studio Set Common/Studio Set Tempo"),
            "the message has to name the parameter, with the path it has in the file's own terms");
    }

    [Test]
    public void Rejects_the_same_parameter_group_twice_in_one_block()
    {
        // The same hazard one level up, and the more dangerous one: a repeated *object* key read into a
        // dictionary loses every parameter in the first copy, not just one.
        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": {
                "s": { "o": { "o2": {
                  "Chorus": { "Chorus Type": [3, "Delay"] },
                  "Chorus": { "Chorus Level": [64, "64"] }
                } } }
              }
            }
            """));

        Assert.That(e!.Message, Does.Contain("Chorus"));
    }

    [Test]
    public void Rejects_the_same_block_twice()
    {
        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": {
                "s": { "o": {
                  "Offset2/Studio Set Common": { "Studio Set Common": { "Studio Set Tempo": [120, "120"] } },
                  "Offset2/Studio Set Common": { "Studio Set Common": { "Studio Set Tempo": [130, "130"] } }
                } }
              }
            }
            """));

        Assert.That(e!.Message, Does.Contain("Offset2/Studio Set Common"));
    }

    [Test]
    public void Rejects_a_top_level_property_named_twice()
    {
        // Two "Blocks" properties is the case that matters: whichever this build ignored, it would ignore
        // silently, and a snapshot missing half its blocks restores half a Studio Set.
        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": { "s": { "o": { "o2": { "a": { "b": [1, "1"] } } } } },
              "Blocks": { "s": { "o": { "o2b": { "a": { "b": [2, "2"] } } } } }
            }
            """));

        Assert.That(e!.Message, Does.Contain("Blocks"));
    }

    [Test]
    public void Rejects_a_leaf_that_is_neither_a_string_nor_a_pair()
    {
        // A hand-edited file that dropped the display string and left the number. Taking it as a raw value
        // with no display string would be reading a shape this format does not define, and the display
        // string is not decoration -- it is what makes the file reviewable before it is sent to hardware.
        Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": { "s": { "o": { "o2": { "a": { "Tempo": 120 } } } } }
            }
            """));
    }

    [Test]
    public void Rejects_a_pair_with_more_than_two_elements()
    {
        Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.FromJson($$"""
            {
              "FormatVersion": {{Integra7Snapshot.CurrentFormatVersion}},
              "Name": "x",
              "Blocks": { "s": { "o": { "o2": { "a": { "Tempo": [120, "120", "?"] } } } } }
            }
            """));
    }

    [Test]
    public void Refuses_to_write_two_parameters_that_would_collide_once_nested()
    {
        // "a/b" as a value and "a/b/c" as an object want the same key. No pair of parameters in the
        // database collides this way -- Every_parameter_path_in_the_database_has_one_separator_or_two and
        // Every_block_a_snapshot_can_hold_survives_being_nested are what say so -- but a file that could
        // not be read back is the worst thing this writer could produce, so it refuses to produce one.
        var colliding = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("a/b", "1", 1),
                new SnapshotValue("a/b/c", "2", 2),
            ]),
        ]);

        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.ToJson(colliding));
        Assert.That(e!.Message, Does.Contain("a/b/c"));
    }

    [Test]
    public void Refuses_to_write_blocks_whose_addresses_are_left_and_returned_to()
    {
        // The nested shape cannot express it: writing "A" again would repeat a key, and grouping the two
        // "A" blocks together would reorder them, which is the one thing this format may not do. Every
        // Studio Set and every tone shares a single Start and Offset across all its blocks, so this is
        // unreachable today; it is refused rather than quietly reordered so that it stays unreachable.
        var interleaved = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("A", "o", "o2a", [new SnapshotValue("a/b", "1", 1)]),
            new SnapshotDomain("B", "o", "o2b", [new SnapshotValue("a/b", "2", 2)]),
            new SnapshotDomain("A", "o", "o2c", [new SnapshotValue("a/b", "3", 3)]),
        ]);

        var e = Assert.Throws<SnapshotFormatException>(() => Integra7Snapshot.ToJson(interleaved));
        Assert.That(e!.Message, Does.Contain("\"A\""), "the message has to name the address it returned to");
    }

    /// <summary>What the parameter database actually contains, since the nesting is built on it. Of its
    /// 13980 parameters, 1248 have one '/' and 12732 have two; none has none, and none has three. The
    /// exact counts are deliberately not asserted -- they move whenever a parameter is added -- but the
    /// shape is, because a path with no separator or with three would nest differently and only this says
    /// whether either exists.</summary>
    [Test]
    public void Every_parameter_path_in_the_database_has_one_separator_or_two()
    {
        var parameters = TestFailedReadKeepsValues.LoadParameters();

        var shapes = parameters.GetParametersWithPrefix("")
            .Select(p => p.Path.Split('/').Length)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.That(shapes, Is.EqualTo(new[] { 2, 3 }),
            "every parameter path is 'Block/Parameter' or 'Block/Group/Parameter'");
    }

    /// <summary>The nesting has to be lossless for the real data, not just for the examples above. Every
    /// block a snapshot can hold -- the 53 of a Studio Set plus the blocks of all five tone engines -- is
    /// captured exactly as <c>CaptureBlockValues</c> would capture it, written, read back, and compared
    /// path for path in order.
    ///
    /// What this catches that a hand-built fixture cannot: two parameters in one block whose paths
    /// collide once nested, and a container segment that is left and returned to. Neither exists today,
    /// and this is what would say so on the day a parameter is added that changes that -- at the moment
    /// it would first make a snapshot unwritable, rather than as a puzzle about a file that will not
    /// open.</summary>
    [Test]
    public void Every_block_a_snapshot_can_hold_survives_being_nested()
    {
        var domain = new Integra7Domain(new TestFailedReadKeepsValues.SilentApi(),
            new Integra7StartAddresses(), TestFailedReadKeepsValues.LoadParameters());

        var blocks = new List<(string Start, string Offset, string Offset2)>(StudioSetDomainNames.All);
        foreach (var toneType in new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" })
            blocks.AddRange(ToneDomainNames.For(toneType, 0));

        // One snapshot per block: a Studio Set's blocks all share one Start and one Offset, and so do a
        // tone's, but a Studio Set block and a tone block do not, and this is about the values.
        foreach (var (start, offset, offset2) in blocks)
        {
            var d = domain.GetDomain(start, offset, offset2);
            // (true, false) is exactly what CaptureBlockValues records -- reserved parameters included,
            // context-invalid ones excluded -- so this is the real value set of a real capture.
            var values = d.GetRelevantParameters(true, false)
                .Select(p => new SnapshotValue(p.ParSpec.Path, p.StringValue,
                    p.IsNumeric || p.IsDiscrete ? p.RawNumericValue : null))
                .ToList();
            Assert.That(values, Is.Not.Empty, $"{offset2} should have parameters");

            var written = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "x",
                [new SnapshotDomain(start, offset, offset2, values)]);

            var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(written));

            Assert.That(read.Domains[0].Values.ConvertAll(v => v.Path),
                Is.EqualTo(values.ConvertAll(v => v.Path)), $"{offset2}: paths and their order");
            Assert.That(read.Domains[0].Values.ConvertAll(v => v.Raw),
                Is.EqualTo(values.ConvertAll(v => v.Raw)), $"{offset2}: raw values");
            Assert.That(read.Domains[0].Values.ConvertAll(v => v.Value),
                Is.EqualTo(values.ConvertAll(v => v.Value)), $"{offset2}: displayed values");
        }
    }

    /// <summary>A whole Studio Set, all 53 blocks and every value in them, through the file and back.
    /// The per-block test above cannot see a mistake in how blocks are grouped under a shared Start and
    /// Offset, which is the part of the writer that only has anything to do when there is more than one
    /// block.</summary>
    [Test]
    public async Task A_whole_captured_studio_set_survives_the_file()
    {
        var api = new StudioSetSnapshotServiceTests.BlankReplyApi();
        var domain = StudioSetSnapshotServiceTests.BuildDomain(api);

        var captured = await StudioSetSnapshotService.CaptureAsync(domain, "World Pop Set",
            StudioSetSnapshotServiceTests.NoRealMidi());

        var read = Integra7Snapshot.FromJson(Integra7Snapshot.ToJson(captured));

        Assert.That(read.Domains.ConvertAll(d => (d.Start, d.Offset, d.Offset2)),
            Is.EqualTo(captured.Domains.ConvertAll(d => (d.Start, d.Offset, d.Offset2))),
            "every block, in capture order");
        for (var i = 0; i < captured.Domains.Count; i++)
            Assert.That(read.Domains[i].Values, Is.EqualTo(captured.Domains[i].Values),
                $"block {captured.Domains[i].Offset2}");
    }
}
