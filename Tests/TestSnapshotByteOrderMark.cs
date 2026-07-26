using System;
using System.IO;
using System.Text;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>A snapshot file that starts with a UTF-8 byte-order mark.
///
/// <b>Why this fixture exists.</b> The snapshot format was built to be read, diffed and hand-edited -- that
/// is the whole reason the parameter data nests by address and the values carry their display strings. So the
/// files will be opened in editors, and a good many editors on Windows write a UTF-8 byte-order mark back out
/// whether the file had one or not; Notepad did it by default for twenty years, and plenty of tooling still
/// does. <c>Utf8JsonReader</c> does not treat that mark as whitespace and is documented not to skip
/// it, so before this fixture a re-saved snapshot was refused: it vanished from the library entirely, and
/// opening it by hand said "This file is not an INTEGRA-7 snapshot", which is both true of the bytes and
/// entirely useless to the person holding a file they have not knowingly changed.
///
/// <b>The two entry points have to agree.</b> A library listing reads a file's head; opening one reads the
/// whole thing. If only one of them skipped the mark, the library would either list files it cannot open or
/// hide files it can -- both worse than the symmetric refusal they started from, because the user gets a
/// contradiction instead of one wrong answer. So each test here asks both readers the same question, and the
/// agreement is the assertion rather than a side effect.
///
/// <b>Only a leading mark is special</b>, and the two tests at the bottom pin that. U+FEFF anywhere else is
/// either a character inside a string, where JSON allows it and it is part of the text, or a character
/// between tokens, where JSON does not allow it and the file is broken. Neither is a byte-order mark, and
/// treating either as one would be this fix quietly editing files it does not understand.</summary>
public class SnapshotByteOrderMarkTests
{
    /// <summary>The mark itself. U+FEFF encodes to EF BB BF in UTF-8, so this one constant serves both entry
    /// points: prefixed to a string it is what a mis-decoded file looks like in memory, and run through
    /// <see cref="Encoding.UTF8"/> it is the three bytes an editor really wrote to the disk.</summary>
    private const string Mark = "\uFEFF";

    private string _folder = "";

    [SetUp]
    public void CreateTempFolder()
    {
        // The pattern TestLibrarySettings established: a GUID directory per test under one shared parent, so
        // that anything ever left behind is findable and removable in one place.
        _folder = Path.Combine(Path.GetTempPath(), "Integra7AuralAlchemist.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveTempFolder()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.WriteLine($"Could not remove the temp directory {_folder}: {e.Message}");
        }
    }

    /// <summary>A tone snapshot with every piece of metadata set, so that a mark cannot cost one field
    /// quietly. Written by the real writer, so the file under test is a real one.</summary>
    private static Integra7Snapshot Annotated() => new(
        Integra7Snapshot.CurrentFormatVersion, "Warm Rhodes",
        [
            new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                "Offset2/SuperNATURAL Synth Tone Common",
                [
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Name", "Warm Rhodes"),
                    new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
                ]),
        ],
        SnapshotKinds.Tone, "SN-S", "E.Piano", ["warm", "trio gig"], "less bark", 4, true);

    private static SnapshotHead? Head(string json) =>
        SnapshotHead.TryRead(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    /// <summary>"Reads identically", stated as strongly as it can be: re-writing what was read out of the
    /// marked file reproduces the unmarked file exactly, character for character. That covers the name, all
    /// five pieces of metadata, every parameter and -- because the writer walks the list it was given -- the
    /// order they are in, which is the one property of this format a restore depends on.</summary>
    [Test]
    public void A_marked_file_opens_and_reads_identically_to_the_same_file_without_the_mark()
    {
        var plain = Integra7Snapshot.ToJson(Annotated());

        var reread = Integra7Snapshot.ToJson(Integra7Snapshot.FromJson(Mark + plain));

        Assert.That(reread, Is.EqualTo(plain));
    }

    /// <summary>The user-visible half of the bug: the file was not in the library at all. Nothing said why,
    /// because nothing had failed -- a file that is not a snapshot is skipped, deliberately and silently, so
    /// that another application's config in the same folder does not produce an error the user cannot act
    /// on. That silence is right for a stray file and was exactly wrong for this one.</summary>
    [Test]
    public void A_marked_file_still_appears_in_a_listing()
    {
        var plain = Integra7Snapshot.ToJson(Annotated());

        AssertSameHead(Head(Mark + plain), Head(plain));
    }

    /// <summary>The invariant the fix is really for, asserted rather than argued: whatever a mark does, it
    /// does the same thing to both readers. A library that lists what it cannot open, or hides what it can,
    /// is a contradiction the user has no way to resolve; one wrong answer given consistently at least
    /// matches what the folder looks like.</summary>
    [Test]
    public void Both_readers_agree_about_a_marked_file()
    {
        var marked = Mark + Integra7Snapshot.ToJson(Annotated());

        Assert.That(Head(marked), Is.Not.Null, "listed by the library...");
        Assert.That(() => Integra7Snapshot.FromJson(marked), Throws.Nothing, "...and openable from it");
    }

    /// <summary>The same thing again, but through a real file written by a real encoder rather than a string
    /// with a character stuck on the front -- because the bytes are what an editor actually leaves behind and
    /// the two entry points reach them by different routes.
    ///
    /// <see cref="SnapshotHead.TryRead"/> is handed the file's bytes and sees the mark, which is where the
    /// library met this. <see cref="Integra7Snapshot.FromJson"/> takes a string, and how the mark reaches it
    /// depends on who decoded the file: <c>File.ReadAllText</c> detects the preamble and strips it, which is
    /// why this never showed up in Load Studio Set, while a decode that does not -- <c>Encoding.UTF8.GetString</c>
    /// over the bytes, which is what any caller reading a file as bytes will do -- passes U+FEFF straight
    /// through. Both are exercised here, because the format is not entitled to assume which one a future
    /// caller picks.</summary>
    [Test]
    public void A_file_an_editor_re_saved_with_a_mark_reads_like_the_one_it_replaced()
    {
        var plain = Integra7Snapshot.ToJson(Annotated());
        var withMark = Path.Combine(_folder, "re-saved.json");
        var withoutMark = Path.Combine(_folder, "as-written.json");
        // encoderShouldEmitUTF8Identifier -- the mark -- is the whole difference between the two files.
        File.WriteAllText(withMark, plain, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(withoutMark, plain, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.That(File.ReadAllBytes(withMark), Has.Length.EqualTo(File.ReadAllBytes(withoutMark).Length + 3),
            "the point of the test is that the file on disk really does start with the three mark bytes");

        using (var marked = File.OpenRead(withMark))
        using (var unmarked = File.OpenRead(withoutMark))
            AssertSameHead(SnapshotHead.TryRead(marked), SnapshotHead.TryRead(unmarked));

        Assert.That(Integra7Snapshot.FromJson(File.ReadAllText(withMark)).Name, Is.EqualTo("Warm Rhodes"));
        Assert.That(Integra7Snapshot.FromJson(Encoding.UTF8.GetString(File.ReadAllBytes(withMark))).Name,
            Is.EqualTo("Warm Rhodes"), "however the caller decoded the file");
    }

    /// <summary>A mark is only a mark at the start of a file. In the middle of one, between two tokens, it is
    /// a character JSON does not allow there, and the file is broken in a way this fix has no business
    /// guessing at -- so both readers refuse it, together, exactly as they refuse any other malformed file.
    /// </summary>
    [Test]
    public void A_mark_in_the_middle_of_a_file_is_not_skipped_by_either_reader()
    {
        var broken = "{" + Mark + Integra7Snapshot.ToJson(Annotated())[1..];

        Assert.That(Head(broken), Is.Null);
        Assert.That(() => Integra7Snapshot.FromJson(broken), Throws.TypeOf<SnapshotFormatException>());
    }

    /// <summary>And inside a string it is text. U+FEFF is a legal character in a JSON string -- only U+0000
    /// to U+001F must be escaped -- so it belongs to the value, and a reader that stripped it would be
    /// silently editing a name the user typed or pasted. Both keep it, and keep it in the same place.
    /// </summary>
    [Test]
    public void A_mark_inside_a_value_is_part_of_that_value()
    {
        var json = Integra7Snapshot.ToJson(Annotated() with { Notes = "less" + Mark + "bark" });

        Assert.That(Head(json)!.Notes, Is.EqualTo("less" + Mark + "bark"));
        Assert.That(Integra7Snapshot.FromJson(json).Notes, Is.EqualTo("less" + Mark + "bark"));
    }

    /// <summary>A file holding nothing but a mark: what an editor leaves when a save produced no content, or
    /// what a truncated copy can amount to. Skipping the mark must leave the readers where an empty file
    /// leaves them -- not a snapshot, and no throw out of the listing -- rather than one step past the end of
    /// a buffer.</summary>
    [Test]
    public void A_file_that_holds_nothing_but_a_mark_is_not_a_snapshot()
    {
        Assert.That(Head(Mark), Is.Null);
        Assert.That(() => Integra7Snapshot.FromJson(Mark), Throws.TypeOf<SnapshotFormatException>());
    }

    /// <summary>Field by field rather than <c>Is.EqualTo</c> on the records: <see cref="SnapshotHead"/>
    /// carries its tags as an <c>IReadOnlyList</c>, and a record compares that member with the default
    /// equality comparer -- reference equality for two distinct lists holding the same strings -- so record
    /// equality would fail for two heads that are in fact identical. Naming the fields also says which one
    /// differed when one does.</summary>
    private static void AssertSameHead(SnapshotHead? actual, SnapshotHead? expected)
    {
        Assert.That(expected, Is.Not.Null, "the unmarked file is the control and must read");
        Assert.That(actual, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(actual!.Name, Is.EqualTo(expected!.Name));
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.ToneType, Is.EqualTo(expected.ToneType));
            Assert.That(actual.Category, Is.EqualTo(expected.Category));
            Assert.That(actual.Tags, Is.EqualTo(expected.Tags));
            Assert.That(actual.Notes, Is.EqualTo(expected.Notes));
            Assert.That(actual.Rating, Is.EqualTo(expected.Rating));
            Assert.That(actual.Favourite, Is.EqualTo(expected.Favourite));
        });
    }
}
