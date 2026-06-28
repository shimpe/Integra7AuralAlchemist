using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The set of SRX boards currently loaded into the unit's 4 slots. Slot values use the same
/// encoding as the SrxSelector combo (0 = Empty, 1..12 = SRX01..12, 13..18 = ExSN1..6, 19 = HQ); only
/// 1..12 (SRX) are relevant to PCM wave groups. Mirrors the <see cref="WaveformBanks"/>.Default pattern:
/// a shared mutable singleton the domain read path consults, updated by the main view model.</summary>
public sealed class LoadedSrxState
{
    private int[] _boards = Array.Empty<int>();

    /// <summary>The loaded SRX board numbers (1..12), distinct. Empty when none loaded.</summary>
    public IReadOnlyCollection<int> Boards => _boards;

    /// <summary>Recompute the loaded set from the 4 raw slot values (keeps only 1..12, deduped).</summary>
    public void SetFromSlots(int slot1, int slot2, int slot3, int slot4)
        => _boards = new[] { slot1, slot2, slot3, slot4 }
            .Where(v => v is >= 1 and <= 12)
            .Distinct()
            .ToArray();

    /// <summary>Shared instance consulted by the domain read path.</summary>
    public static LoadedSrxState Default { get; } = new();
}
