using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which expansion board a bank lives on, and how few loadouts cover a set of banks.</summary>
public class SeedBoardsTests
{
    [Test]
    public void A_bank_on_no_board_needs_none()
    {
        Assert.That(SeedBoards.For("PRST"), Is.Null);
        Assert.That(SeedBoards.For("GM2/GM2#"), Is.Null);
    }

    [Test]
    public void An_srx_bank_names_its_board()
    {
        Assert.That(SeedBoards.For("SRX01"), Is.EqualTo(1));
        Assert.That(SeedBoards.For("SRX12"), Is.EqualTo(12));
    }

    /// <summary>The ExSN boards continue the same numbering, which is the instrument's, not ours.</summary>
    [Test]
    public void An_exsn_bank_names_its_board()
    {
        Assert.That(SeedBoards.For("ExSN1"), Is.EqualTo(13));
        Assert.That(SeedBoards.For("ExSN6"), Is.EqualTo(18));
    }

    /// <summary>Four slots, so four boards per loadout and no more.</summary>
    [Test]
    public void Boards_are_grouped_four_at_a_time()
    {
        var rounds = SeedBoards.Loadouts([1, 2, 3, 4, 5]);

        Assert.That(rounds, Has.Count.EqualTo(2));
        Assert.That(rounds[0], Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(rounds[1], Is.EqualTo(new[] { 5, 0, 0, 0 }));
    }

    /// <summary>A loadout is always four values, padded with Off, because that is what the device is sent.
    /// </summary>
    [Test]
    public void A_short_loadout_is_padded_with_off()
    {
        Assert.That(SeedBoards.Loadouts([7]), Is.EqualTo(new[] { new[] { 7, 0, 0, 0 } }));
    }

    [Test]
    public void No_boards_is_no_loadouts()
    {
        Assert.That(SeedBoards.Loadouts([]), Is.Empty);
    }

    /// <summary>Ordered, so that two plans over the same banks load the boards in the same order and a run
    /// that was interrupted resumes into the same rounds rather than reloading boards it already used.
    /// </summary>
    [Test]
    public void Loadouts_are_in_board_order_whatever_order_the_banks_came_in()
    {
        Assert.That(SeedBoards.Loadouts([9, 2, 5, 1]), Is.EqualTo(new[] { new[] { 1, 2, 5, 9 } }));
    }

    /// <summary>A board asked for twice is loaded once. This is the ordinary case rather than an edge one:
    /// the caller has a board per patch, so a bank of several hundred patches arrives here as several
    /// hundred copies of one number, and without this a single bank would fill every slot and spill into a
    /// second round.</summary>
    [Test]
    public void A_board_wanted_by_several_banks_is_loaded_once()
    {
        Assert.That(SeedBoards.Loadouts([1, 1, 1, 1, 1]), Is.EqualTo(new[] { new[] { 1, 0, 0, 0 } }));
    }
}
