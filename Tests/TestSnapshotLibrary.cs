using System;
using System.IO;
using System.Linq;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A folder of snapshot files, listed for browsing and annotated in place.
///
/// Two things are being pinned, and the second is the one with teeth. The first is that a library folder is a
/// folder: whatever else is in it -- another application's config, a text file, half a copy -- is passed over
/// silently, because an error nobody can act on is worse than a file that simply is not a snapshot. The second
/// is that annotating a sound cannot change the sound. The metadata lives in the same file as ~1,500 parameter
/// values, so adding a tag rewrites the file that holds the Studio Set, and the tests below say that the values
/// written back are the file's own -- byte for byte, including ones nothing in this application put there.
///
/// The temp-directory pattern is <c>TestLibrarySettings</c>': a GUID directory per test under one shared
/// parent, created in SetUp and removed in TearDown, so that no test can see a file another one wrote.
/// </summary>
public class SnapshotLibraryTests
{
    private string _folder = "";

    [SetUp]
    public void CreateTempFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "Integra7AuralAlchemist.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveTempFolder()
    {
        // A deletion that fails -- an indexer or a scanner still holding a file written milliseconds ago --
        // must not fail a test that actually passed. The directory is GUID-named, so what is left behind is
        // inert, and the shared parent above is where to look for it.
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.WriteLine($"Could not remove the temp directory {_folder}: {e.Message}");
        }
    }

    private static Integra7Snapshot Tone(string name) => new(
        Integra7Snapshot.CurrentFormatVersion, name,
        [
            new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                "Offset2/SuperNATURAL Synth Tone Common",
                [
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", name),
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
                ]),
        ],
        SnapshotKinds.Tone, "SN-S");

    private static Integra7Snapshot StudioSet(string name) => new(
        Integra7Snapshot.CurrentFormatVersion, name,
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
                [
                    new SnapshotValue("Studio Set Common/Studio Set Name", name),
                    new SnapshotValue("Studio Set Common/Studio Set Tempo", "120", 120),
                ]),
        ]);

    private string Save(string fileName, Integra7Snapshot snapshot)
    {
        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, Integra7Snapshot.ToJson(snapshot));
        return path;
    }

    /// <summary>Everything the parameter data is, as it sits in the file. The comparison the annotation tests
    /// make: not "the values round-trip" but "these bytes did not move", which also covers their order.
    /// </summary>
    private static string ParameterData(string path)
    {
        var json = File.ReadAllText(path);
        return json[json.IndexOf("\"Blocks\"", StringComparison.Ordinal)..];
    }

    [Test]
    public void Lists_every_snapshot_in_the_folder_with_its_head_and_its_date()
    {
        Save("rhodes.json", Tone("Warm Rhodes") with { Category = "E.Piano", Rating = 4, Favourite = true });
        Save("set.json", StudioSet("World Pop Set") with { Tags = ["trio gig"], Notes = "second half" });

        var entries = SnapshotLibrary.Read(_folder).OrderBy(e => e.Head.Name).ToList();

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Head.Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(entries[0].Head.Kind, Is.EqualTo(SnapshotKinds.Tone));
        Assert.That(entries[0].Head.ToneType, Is.EqualTo("SN-S"));
        Assert.That(entries[0].Head.Category, Is.EqualTo("E.Piano"));
        Assert.That(entries[0].Head.Rating, Is.EqualTo(4));
        Assert.That(entries[0].Head.Favourite, Is.True);
        Assert.That(entries[0].FilePath, Is.EqualTo(Path.Combine(_folder, "rhodes.json")));
        Assert.That(entries[1].Head.Name, Is.EqualTo("World Pop Set"));
        Assert.That(entries[1].Head.Tags, Is.EqualTo(new[] { "trio gig" }));
        Assert.That(entries[1].Head.Notes, Is.EqualTo("second half"));
        // Not an exact time: the point is that it is the file's own date and not the default DateTime, which
        // is what a browser would sort by and what File.GetLastWriteTime answers for a path that is gone.
        Assert.That(entries[1].Modified, Is.EqualTo(File.GetLastWriteTime(entries[1].FilePath)));
        Assert.That(entries[1].Modified, Is.GreaterThan(new DateTime(2020, 1, 1)));
    }

    /// <summary>A library folder is a folder, and the user will keep other things in it. None of these produce
    /// an error, because there is nothing the user could do about one: a config file belonging to another
    /// application is not a broken snapshot, it is simply not a snapshot, and the two are indistinguishable
    /// from here.</summary>
    [Test]
    public void Anything_that_is_not_a_snapshot_is_skipped_without_a_word()
    {
        var good = Save("rhodes.json", Tone("Warm Rhodes"));
        File.WriteAllText(Path.Combine(_folder, "notes.json"), "These are my notes about Friday.");
        File.WriteAllText(Path.Combine(_folder, "someothertool.json"), """{ "name": "not ours", "v": 2 }""");
        File.WriteAllText(Path.Combine(_folder, "empty.json"), "");
        var full = Integra7Snapshot.ToJson(Tone("Half A Copy"));
        File.WriteAllText(Path.Combine(_folder, "truncated.json"), full[..(full.Length / 2)]);

        var entries = SnapshotLibrary.Read(_folder);

        Assert.That(entries.Select(e => e.FilePath), Is.EqualTo(new[] { good }));
    }

    /// <summary>The pattern is <c>*.json</c>, and a snapshot saved under another extension is not in the
    /// library. Worth pinning because the rule is arbitrary in the way conventions are: nothing about the file
    /// says .json is required, and a listing that read every file in the folder would open the user's PDFs to
    /// find out what they were.</summary>
    [Test]
    public void Only_json_files_are_read()
    {
        Save("rhodes.json", Tone("Warm Rhodes"));
        File.WriteAllText(Path.Combine(_folder, "rhodes.txt"), Integra7Snapshot.ToJson(Tone("Text Rhodes")));
        File.WriteAllText(Path.Combine(_folder, "rhodes.json.bak"), Integra7Snapshot.ToJson(Tone("Old Rhodes")));

        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.Head.Name), Is.EqualTo(new[] { "Warm Rhodes" }));
    }

    /// <summary>A stated limitation, not an oversight: the library is one folder and not a tree. A tree needs a
    /// way to show, choose and save into a branch, which is a feature of its own; this is the test that says
    /// the current answer is deliberate, and the one that will fail first when that changes.</summary>
    [Test]
    public void Sub_folders_are_not_enumerated()
    {
        Save("rhodes.json", Tone("Warm Rhodes"));
        var branch = Path.Combine(_folder, "Live Sets");
        Directory.CreateDirectory(branch);
        File.WriteAllText(Path.Combine(branch, "set.json"), Integra7Snapshot.ToJson(StudioSet("Buried Set")));

        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.Head.Name), Is.EqualTo(new[] { "Warm Rhodes" }));
    }

    /// <summary>The normal state of the default library folder until the first save, so it cannot be an error:
    /// the library would only ever open on one, where "nothing here yet" is the truth. It is also what a folder
    /// on a drive that is not mounted looks like from here, which <c>LibrarySettings</c> deliberately does not
    /// resolve for the same reason.</summary>
    [Test]
    public void A_folder_that_is_not_there_lists_as_empty_rather_than_throwing()
    {
        var missing = Path.Combine(_folder, "not", "mounted", "yet");

        Assert.That(SnapshotLibrary.Read(missing), Is.Empty);
        Assert.That(SnapshotLibrary.Read(""), Is.Empty, "and so does no folder at all");
    }

    /// <summary>One file another process is holding costs that file and no more. A snapshot open in an editor, a
    /// sync client copying it, a scanner reading it -- letting any of those throw would mean one locked file
    /// emptied the whole browser, which is a much worse trade than one row missing from a list the user can
    /// refresh.</summary>
    [Test]
    public void A_file_another_process_is_holding_is_left_out_and_the_rest_are_listed()
    {
        Save("rhodes.json", Tone("Warm Rhodes"));
        var locked = Save("locked.json", Tone("Held Open"));

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.Head.Name),
                Is.EqualTo(new[] { "Warm Rhodes" }));

        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.Head.Name),
            Has.Member("Held Open"), "and it is back the moment the handle is released");
    }

    /// <summary>A snapshot re-saved by an editor that added a byte-order mark is a snapshot. This is the
    /// library-level statement of what <c>ByteOrderMark</c> fixes: before it, the file was missing from this
    /// list, silently, through the same exit a stray file takes -- and the user had done nothing to it but
    /// look.</summary>
    [Test]
    public void A_file_an_editor_re_saved_with_a_byte_order_mark_is_listed_and_can_be_annotated()
    {
        var path = Path.Combine(_folder, "re-saved.json");
        File.WriteAllText(path, Integra7Snapshot.ToJson(Tone("Warm Rhodes")),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.Head.Name), Is.EqualTo(new[] { "Warm Rhodes" }));

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Rating: 4));

        Assert.That(SnapshotLibrary.Read(_folder).Single().Head.Rating, Is.EqualTo(4));
    }

    /// <summary>The one that matters most. Annotating a sound must not change the sound, and the assertion is
    /// not that the values round-trip but that the file's parameter data is the same text it was -- which covers
    /// their order too, and order is what a restore applies them in.</summary>
    [Test]
    public void Writing_metadata_leaves_every_parameter_exactly_as_it_was()
    {
        var path = Save("set.json", StudioSet("World Pop Set"));
        var before = ParameterData(path);

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata("", ["trio gig", "warm"], "second half only",
            4, true));

        Assert.That(ParameterData(path), Is.EqualTo(before));
        var head = SnapshotLibrary.Read(_folder).Single().Head;
        Assert.That(head.Tags, Is.EqualTo(new[] { "trio gig", "warm" }));
        Assert.That(head.Notes, Is.EqualTo("second half only"));
        Assert.That(head.Rating, Is.EqualTo(4));
        Assert.That(head.Favourite, Is.True);
        Assert.That(head.Name, Is.EqualTo("World Pop Set"), "and the file is still the same snapshot");
        Assert.That(head.Kind, Is.EqualTo(SnapshotKinds.StudioSet));
    }

    /// <summary>And the parameter data comes from the file rather than from anything this application is
    /// holding. The way to prove it is to put a value in the file that nothing in memory has ever seen -- a
    /// hand-edited tempo, exactly what this format invites -- and then annotate the file: if the write pulled
    /// its values from a captured snapshot, or from an earlier read of a different file, the hand edit would be
    /// gone. There is no in-memory snapshot in WriteMetadata's signature for a caller to hand it, which is the
    /// strongest form that guarantee can take, and this is what says so.</summary>
    [Test]
    public void The_parameter_data_written_back_is_the_file_own_and_not_anything_in_memory()
    {
        var path = Save("set.json", StudioSet("World Pop Set"));
        var other = Save("second.json", StudioSet("Another Set"));
        var otherBefore = File.ReadAllText(other);
        // The tempo is the only 120 in the file, and this hits both halves of its [raw, "display"] leaf --
        // which is exactly what somebody changing a tempo in an editor would do.
        File.WriteAllText(path, File.ReadAllText(path).Replace("120", "96"));

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "slower now"));

        var written = Integra7Snapshot.FromJson(File.ReadAllText(path));
        Assert.That(written.Domains[0].Values[1].Raw, Is.EqualTo(96), "the hand-edited tempo survived");
        Assert.That(written.Domains[0].Values[1].Value, Is.EqualTo("96"));
        Assert.That(File.ReadAllText(path), Does.Not.Contain("120"), "and nothing put the old one back");
        Assert.That(written.Notes, Is.EqualTo("slower now"));
        Assert.That(File.ReadAllText(other), Is.EqualTo(otherBefore),
            "and the file next to it was not touched at all");
    }

    [Test]
    public void Writing_metadata_replaces_all_five_fields_including_back_to_nothing()
    {
        // Clearing matters as much as setting: a user who removes a tag and saves expects it gone, and a write
        // that only ever added would leave the file disagreeing with what they are looking at.
        var path = Save("rhodes.json", Tone("Warm Rhodes") with
        {
            Category = "E.Piano", Tags = ["warm"], Notes = "less bark", Rating = 4, Favourite = true,
        });

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata());

        var head = SnapshotLibrary.Read(_folder).Single().Head;
        Assert.That(head.Category, Is.EqualTo(""));
        Assert.That(head.Tags, Is.Empty);
        Assert.That(head.Notes, Is.EqualTo(""));
        Assert.That(head.Rating, Is.EqualTo(0));
        Assert.That(head.Favourite, Is.False);
    }

    /// <summary>Reading goes through <c>FromJson</c>, which judges the file -- so a snapshot this build will not
    /// open cannot be annotated either. That is the right way round: quietly rewriting a hand-edited file into
    /// something readable, as a side effect of adding a tag, would be the worst possible moment to repair
    /// anything.</summary>
    [Test]
    public void A_file_that_cannot_be_opened_cannot_be_annotated_and_is_left_alone()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));
        var broken = File.ReadAllText(path).Replace("\"Rating\": 0", "\"Rating\": 7");
        File.WriteAllText(path, broken);

        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "mine now")),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo(broken), "and the file is untouched");
        Assert.That(SnapshotLibrary.Read(_folder), Has.Count.EqualTo(1),
            "while still being listed, so the user can see the file they cannot edit");
    }

    /// <summary>A rating no file may hold is refused before the file is opened. The star control cannot produce
    /// one, so this is about a caller with a bug rather than a user with a mouse -- which is exactly the case
    /// that would otherwise turn a perfectly good snapshot into one this build refuses to read.</summary>
    [Test]
    public void A_rating_outside_zero_to_five_is_refused_before_the_file_is_touched()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));
        var before = File.ReadAllText(path);

        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Rating: 7)),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Rating: -1)),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public void Annotating_a_file_that_is_not_there_reports_it()
    {
        Assert.That(() => SnapshotLibrary.WriteMetadata(Path.Combine(_folder, "gone.json"),
            new SnapshotMetadata(Rating: 3)), Throws.InstanceOf<IOException>());
    }

    /// <summary>The write is atomic -- a temp file beside the target, then a rename over it -- so this pins the
    /// half of that which has nothing else to witness it: when the rename fails, the file is whole and the temp
    /// file is gone. A stray temp beside every snapshot the user failed to annotate would be a mess that never
    /// cleans itself up.
    ///
    /// The failure is arranged the way it will really happen: another process holding the file open. Sharing
    /// read access lets the read succeed, so the write gets as far as the rename, and holding the handle
    /// without delete access is what makes the rename fail.
    ///
    /// Not a specific exception type, for the reason TestLibrarySettings gives about the same rename: what
    /// Windows raises for a replace it cannot perform is UnauthorizedAccessException rather than the
    /// IOException the operation reads like, and pinning either would be pinning the platform rather than the
    /// contract. The contract is that the failure is reported at all, because the user just asked for
    /// something to be saved.</summary>
    [Test]
    public void A_failed_write_reports_it_leaves_the_file_whole_and_leaves_no_temporary_file_behind()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));
        var before = File.ReadAllText(path);

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Rating: 4)),
                Throws.Exception);

        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        Assert.That(Directory.EnumerateFiles(_folder), Is.EqualTo(new[] { path }),
            "no .saving file left behind");
    }

    /// <summary>The temp file is not called .json, and this is why: a listing that ran while a write was in
    /// flight would otherwise show the same snapshot twice, once under a name that is about to disappear.
    /// </summary>
    [Test]
    public void A_write_in_flight_cannot_be_listed_as_a_second_snapshot()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));
        File.WriteAllText(path + ".saving", File.ReadAllText(path));

        Assert.That(SnapshotLibrary.Read(_folder).Select(e => e.FilePath), Is.EqualTo(new[] { path }));
    }

    /// <summary>The name is the sixth metadata field, added when the browser needed the library's entries to be
    /// renameable. It goes through the same write path as the other five -- one place rewrites a snapshot -- and
    /// the assertion that matters is the same one: the parameter data does not move.</summary>
    [Test]
    public void A_snapshot_can_be_renamed_through_the_same_write_path_as_its_annotations()
    {
        var path = Save("set.json", StudioSet("World Pop Set"));
        var before = ParameterData(path);

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Rating: 5, Name: "Sunday Morning Set"));

        var head = SnapshotLibrary.Read(_folder).Single().Head;
        Assert.That(head.Name, Is.EqualTo("Sunday Morning Set"));
        Assert.That(head.Rating, Is.EqualTo(5));
        Assert.That(ParameterData(path), Is.EqualTo(before), "and the parameters did not move");
        Assert.That(path, Does.EndWith("set.json"),
            "renaming changes what the snapshot calls itself and not what the file is called");
    }

    /// <summary>Null is not blank. A caller that only wants to annotate says nothing about the name and the file's
    /// own is kept -- which is what every caller written before the browser existed does, and what the record's
    /// default means.</summary>
    [Test]
    public void Metadata_that_says_nothing_about_the_name_leaves_the_name_alone()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));

        SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Notes: "less bark"));

        var head = SnapshotLibrary.Read(_folder).Single().Head;
        Assert.That(head.Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(head.Notes, Is.EqualTo("less bark"));
    }

    /// <summary>A blank name is refused before the file is touched, like a rating of seven. It is the one field
    /// whose absence the browser cannot show: a row with no name is one the user cannot tell from the row above
    /// it, and the file it names may be their only copy of that sound.</summary>
    [Test]
    public void A_blank_name_is_refused_before_the_file_is_touched()
    {
        var path = Save("rhodes.json", Tone("Warm Rhodes"));
        var before = File.ReadAllText(path);

        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Name: "")),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(() => SnapshotLibrary.WriteMetadata(path, new SnapshotMetadata(Name: "   ")),
            Throws.TypeOf<SnapshotFormatException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public void Creating_a_snapshot_in_the_library_names_the_file_after_it_and_applies_its_metadata()
    {
        var path = SnapshotLibrary.Create(_folder, Tone("Warm Rhodes"),
            new SnapshotMetadata("E.Piano", ["warm"], "", 4, true, "Warm Rhodes"));

        Assert.That(path, Is.EqualTo(Path.Combine(_folder, "Warm Rhodes.json")));
        var head = SnapshotLibrary.Read(_folder).Single().Head;
        Assert.That(head.Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(head.Category, Is.EqualTo("E.Piano"));
        Assert.That(head.Tags, Is.EqualTo(new[] { "warm" }));
        Assert.That(head.Rating, Is.EqualTo(4));
        Assert.That(head.Favourite, Is.True);
        Assert.That(head.ToneType, Is.EqualTo("SN-S"), "and it is still the tone that was captured");
    }

    /// <summary>Two captures of the same sound do not overwrite each other. A library of captures is full of
    /// "Init Tone" and "Studio Set", and silently replacing the earlier one would destroy a snapshot the user
    /// still has -- with the same button that is supposed to be keeping them.</summary>
    [Test]
    public void A_name_that_is_already_taken_is_suffixed_rather_than_overwritten()
    {
        var first = SnapshotLibrary.Create(_folder, Tone("Init Tone"), new SnapshotMetadata(Name: "Init Tone"));
        var second = SnapshotLibrary.Create(_folder, Tone("Init Tone"), new SnapshotMetadata(Name: "Init Tone"));
        var third = SnapshotLibrary.Create(_folder, Tone("Init Tone"), new SnapshotMetadata(Name: "Init Tone"));

        Assert.That(Path.GetFileName(first), Is.EqualTo("Init Tone.json"));
        Assert.That(Path.GetFileName(second), Is.EqualTo("Init Tone (2).json"));
        Assert.That(Path.GetFileName(third), Is.EqualTo("Init Tone (3).json"));
        Assert.That(SnapshotLibrary.Read(_folder), Has.Count.EqualTo(3));
    }

    /// <summary>The first save into the default library folder happens before anything has created it. Recording
    /// where the library is and putting a file in it are different questions -- <c>LibrarySettings</c> answers the
    /// first and deliberately does not create anything -- so this is where the folder comes into existence.
    /// </summary>
    [Test]
    public void Creating_a_snapshot_creates_the_library_folder_if_it_is_not_there()
    {
        var folder = Path.Combine(_folder, "Library", "Nested");

        var path = SnapshotLibrary.Create(folder, Tone("Warm Rhodes"), new SnapshotMetadata(Name: "Warm Rhodes"));

        Assert.That(File.Exists(path));
        Assert.That(SnapshotLibrary.Read(folder), Has.Count.EqualTo(1));
    }

    /// <summary>The instrument's character set includes ':', '/' and '*', which a file name cannot hold. Pure, so
    /// it is tested without a disk -- and the cases below are the ones that produce a file with no name at all
    /// rather than merely an ugly one.</summary>
    [Test]
    public void A_file_name_is_the_snapshot_name_with_whatever_a_file_name_cannot_hold_replaced()
    {
        Assert.That(SnapshotLibrary.FileNameFor("Warm Rhodes"), Is.EqualTo("Warm Rhodes.json"));
        Assert.That(SnapshotLibrary.FileNameFor("Pad:2/3*"), Is.EqualTo("Pad_2_3_.json"));
        Assert.That(SnapshotLibrary.FileNameFor(""), Is.EqualTo("Snapshot.json"));
        Assert.That(SnapshotLibrary.FileNameFor("   "), Is.EqualTo("Snapshot.json"));
        // Trailing dots and spaces are legal in a string and not in a Windows file name: the API drops them
        // silently, so a uniqueness check made on the longer name would not see the collision.
        Assert.That(SnapshotLibrary.FileNameFor("Warm Rhodes ."), Is.EqualTo("Warm Rhodes.json"));
    }
}
