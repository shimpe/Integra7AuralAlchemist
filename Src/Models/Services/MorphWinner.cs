using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which corner supplies the discrete values of a blend.
///
/// <b>Sticky on purpose.</b> A discrete parameter cannot be averaged -- a filter mode is Low pass or it
/// is not -- so somewhere on the pad every one of them changes at once. On a plain nearest-corner rule
/// that boundary is a knife edge: a pointer resting on it flips the whole set back and forth. The leader
/// therefore keeps the lead until a challenger is <see cref="Margin"/> better.
///
/// <b>But decided by the weights alone when there is no history.</b> Without that, the same point would
/// sound different depending on the path taken to it, and a saved pad position would not reproduce the
/// sound it was saved at -- which is most of the reason to save one. Callers <see cref="Reset"/> when
/// they set the point from outside a drag.</summary>
public sealed class MorphWinner
{
    /// <summary>How much better a challenger must be, relative to the leader. Enough that a boundary
    /// does not flicker, small enough that the lead changes about where a user expects it to.</summary>
    public const double Margin = 0.05;

    private int _leader = -1;

    public void Reset() => _leader = -1;

    public int Winner(IReadOnlyList<double> weights)
    {
        var best = 0;
        for (var i = 1; i < weights.Count; i++)
            if (weights[i] > weights[best]) best = i;   // strictly greater, so a tie keeps the lower index

        // No history, or a leader from a pad with more corners than this one has.
        if (_leader < 0 || _leader >= weights.Count)
        {
            _leader = best;
            return _leader;
        }

        if (best != _leader && weights[best] > weights[_leader] * (1 + Margin)) _leader = best;
        return _leader;
    }
}
