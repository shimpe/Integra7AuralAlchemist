using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One slot, this session only. Not persisted on purpose: a clipboard that survives a restart
/// is a surprise, and the library is where a tone goes to be kept.</summary>
public class ToneClipboardTests
{
    private static Integra7Snapshot Tone(string name) => new(
        Integra7Snapshot.CurrentFormatVersion, name,
        [
            new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                "Offset2/SuperNATURAL Synth Tone Common",
                [new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100)]),
        ],
        SnapshotKinds.Tone, "SN-S");

    [Test]
    public void Starts_empty()
    {
        var clipboard = new ToneClipboard();

        Assert.That(clipboard.HasContent, Is.False);
        Assert.That(clipboard.Content, Is.Null);
    }

    [Test]
    public void Holds_the_last_tone_put_into_it()
    {
        var clipboard = new ToneClipboard();

        clipboard.Put(Tone("first"));
        clipboard.Put(Tone("second"));

        Assert.That(clipboard.HasContent, Is.True);
        Assert.That(clipboard.Content!.Name, Is.EqualTo("second"));
    }

    [Test]
    public void Announces_a_change_so_paste_can_enable_itself()
    {
        var clipboard = new ToneClipboard();
        var announcements = 0;
        clipboard.Changed += () => announcements++;

        clipboard.Put(Tone("first"));

        Assert.That(announcements, Is.EqualTo(1));
    }
}
