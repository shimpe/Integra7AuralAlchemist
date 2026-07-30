using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Whether the instrument's four expansion slots have stopped moving, decided from a series of
/// readings taken one poll apart.
///
/// <b>Pure, and separate from the adapter, because this rule was wrong once and it cost a user their
/// boards.</b> The rule it replaces settled as soon as two consecutive readings agreed, which a device that
/// has not yet acted on a request also does -- so a sweep cancelled mid-load "converged" on the loadout it
/// was already leaving behind, reported success, and left three boards evicted. Everything else about that
/// failure needed an INTEGRA-7 to see; this part of it does not, and it is here so that it never again has
/// to be.
///
/// <b>A reading the instrument did not answer is not a reading.</b> Measured on the user's unit on
/// 2026-07-30: an idle instrument answers the slot query in two to five milliseconds and does so whatever
/// the slots hold, all four Off included; while it is loading boards it answers nothing at all and the read
/// runs out its 1.5-second deadline, which <see cref="Integra7Api.GetLoadedSrxAsync"/> renders as
/// (0,0,0,0). So all-zeros is two quite different states wearing one face, and the caller passes null for
/// the one that means "still working" rather than letting this class guess. Those readings do not merely
/// fail to count -- they put the count back to nothing, because a device that went quiet in the middle of a
/// loadout it is applying board by board would otherwise have its half-done state believed.
///
/// <b>Three, not two.</b> The instrument goes quiet within a poll of being sent a loadout -- both loads
/// measured were already silent 1.5 s in -- so two agreeing answers would very probably be enough. Three
/// spans three seconds of polling and about four and a half of wall clock, comfortably past the 2.5 s of
/// the shortest thing the device does with its slots, and the cost of the extra poll is 1.5 seconds per
/// board round against the cost of being wrong, which is a whole round swept against slots that do not hold
/// what the sweep thinks they do.
///
/// <b>Never compared against what was asked for.</b> The device rewrites a loadout it is given -- sending
/// (19,0,0,0) reads back as (19,20,21,22), because that board occupies all four slots -- so a rule that
/// waited for the request to appear would wait for something that is never going to. It is also why this
/// takes no request: there is nothing here to compare one against, which is the surest way of not comparing
/// against it. And a loadout the instrument already holds changes nothing at all -- verified by sending the
/// user's own (2,13,6,0) back to them, which produced not one busy poll -- so a rule that waited for the
/// reading to change would hang on the one case where the right answer is "already done".</summary>
public sealed class SeedSettling
{
    /// <summary>How many agreeing answers make a settled loadout. See the class remarks for why it is not
    /// two.</summary>
    public const int Agreeing = 3;

    private int[]? _held;
    private int _agreements;

    /// <summary>Take another reading, and answer whether the slots can now be believed.</summary>
    /// <param name="slots">The four values the instrument answered with, or null when it did not answer --
    /// which is what it does throughout a board load.</param>
    public bool Settled(int[]? slots)
    {
        if (slots is null)
        {
            _held = null;
            _agreements = 0;
            return false;
        }

        if (_held is not null && _held.SequenceEqual(slots)) _agreements++;
        else
        {
            _held = slots;
            _agreements = 1;
        }

        return _agreements >= Agreeing;
    }
}
