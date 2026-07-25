using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>
/// <see cref="UserToneSlots.ZeroBasedSlotOf" /> produces a hardware address: the user memory slot that
/// storing a tone overwrites. Every list the UI can show is a filtered projection of the preset list,
/// so the point of these tests is that the number depends on the preset and on the full list only --
/// not on list order, and not on which rows a filtered view happened to hide.
/// </summary>
[TestFixture]
public class TestUserToneSlots
{
    private static Integra7Preset Preset(int id, string kind, string toneType, string name)
    {
        // Number/MSB/LSB/PC are irrelevant here but the constructor validates the string fields, so the
        // bank and category have to be real ones.
        return new Integra7Preset(id, kind, toneType, "PRST", id, name, 89, 64, id + 1, "Ac.Piano");
    }

    /// <summary>A realistic slice: the two engines interleaved, INT presets ahead of the USR ones.</summary>
    private static List<Integra7Preset> SampleList()
    {
        return
        [
            Preset(1, "INT", "PCMS", "PRST piano"),
            Preset(2, "INT", "PCMS", "PRST strings"),
            Preset(3, "INT", "SN-S", "PRST lead"),
            Preset(10, "USR", "PCMS", "user pcm 0"),
            Preset(11, "USR", "SN-S", "user sns 0"),
            Preset(12, "USR", "PCMS", "user pcm 1"),
            Preset(13, "USR", "SN-S", "user sns 1"),
            Preset(14, "USR", "PCMS", "user pcm 2")
        ];
    }

    [Test]
    public void FirstUserToneOfAKindIsSlotZero()
    {
        var presets = SampleList();
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", presets.First(p => p.Id == 10)), Is.EqualTo(0));
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "SN-S", presets.First(p => p.Id == 11)), Is.EqualTo(0));
    }

    [Test]
    public void InternalPresetsAndOtherToneTypesDoNotCount()
    {
        var presets = SampleList();
        // Three INT presets and two SN-S user tones sit before Id 14 in the list, yet it is PCMS slot 2.
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", presets.First(p => p.Id == 14)), Is.EqualTo(2));
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "SN-S", presets.First(p => p.Id == 13)), Is.EqualTo(1));
    }

    [Test]
    public void SlotsFollowIdOrderNotListOrder()
    {
        var presets = SampleList();
        var shuffled = presets.OrderByDescending(p => p.Id).ToList();
        foreach (var p in presets.Where(p => p.InternalUserDefinedStr == "USR"))
            Assert.That(UserToneSlots.ZeroBasedSlotOf(shuffled, p.ToneTypeStr, p),
                Is.EqualTo(UserToneSlots.ZeroBasedSlotOf(presets, p.ToneTypeStr, p)),
                $"slot of {p.Name} changed when the list was reordered");
    }

    [Test]
    public void PresetThatIsNotInTheListHasNoSlot()
    {
        var presets = SampleList();
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", Preset(99, "USR", "PCMS", "stranger")),
            Is.EqualTo(-1));
    }

    [Test]
    public void InternalPresetHasNoSlot()
    {
        var presets = SampleList();
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", presets.First(p => p.Id == 1)), Is.EqualTo(-1));
    }

    [Test]
    public void PresetOfADifferentToneTypeHasNoSlot()
    {
        var presets = SampleList();
        // Id 11 is a user tone, but an SN-S one; asked as PCMS it must not report a PCMS slot.
        Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", presets.First(p => p.Id == 11)), Is.EqualTo(-1));
    }

    [Test]
    public void NullPresetHasNoSlot()
    {
        Assert.That(UserToneSlots.ZeroBasedSlotOf(SampleList(), "PCMS", null), Is.EqualTo(-1));
    }

    /// <summary>The defect this replaces: the dialog used the row index of the *filtered* grid as the
    /// slot number. With the search box empty the filtered rows happen to be exactly the user tones of
    /// the tone type in Id order, so the index matched; typing anything shrank the list and the index
    /// then addressed a different slot -- overwriting a different saved sound.
    ///
    /// So: for every subset a filter could produce, the slot computed from the full list is unchanged,
    /// and it is the row index within that subset only in the unfiltered case.</summary>
    [Test]
    public void SlotIsIndependentOfWhatAFilteredViewWouldHaveShown()
    {
        var presets = SampleList();
        var pcmsUserTones = presets
            .Where(p => p is { ToneTypeStr: "PCMS", InternalUserDefinedStr: "USR" })
            .OrderBy(p => p.Id).ToList();

        // What the dialog's grid shows with an empty search box, and the case that used to work.
        for (var row = 0; row < pcmsUserTones.Count; row++)
            Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", pcmsUserTones[row]), Is.EqualTo(row));

        // Now every subset the search box could leave behind. The rows shift; the slots must not.
        var rowsThatWouldHaveLied = 0;
        for (var mask = 1; mask < 1 << pcmsUserTones.Count; mask++)
        {
            var shown = pcmsUserTones.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            for (var row = 0; row < shown.Count; row++)
            {
                var expectedSlot = pcmsUserTones.IndexOf(shown[row]);
                if (row != expectedSlot) rowsThatWouldHaveLied++;
                Assert.That(UserToneSlots.ZeroBasedSlotOf(presets, "PCMS", shown[row]), Is.EqualTo(expectedSlot),
                    $"filtered view {mask}: row {row} must still address slot {expectedSlot}");
            }
        }

        // Guard against the above being vacuous: there must be subsets where the old row-index rule
        // really did point somewhere else, or the assertions prove nothing.
        Assert.That(rowsThatWouldHaveLied, Is.GreaterThan(0),
            "the sample list is too small to produce a filtered view whose row indices differ from the slots");
    }
}
