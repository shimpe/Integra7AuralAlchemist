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
    private int[] _exSnBoards = Array.Empty<int>();

    /// <summary>The loaded SRX board numbers (1..12), distinct. Empty when none loaded.</summary>
    public IReadOnlyCollection<int> Boards => _boards;

    /// <summary>The loaded ExSN board numbers (1..6, from slot values 13..18), distinct. The SN-Acoustic
    /// catalog only references ExSN1..5.</summary>
    public IReadOnlyCollection<int> ExSnBoards => _exSnBoards;

    /// <summary>Raised after the loaded sets are recomputed, so loaded-aware UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>Recompute the loaded sets from the 4 raw slot values: SRX boards (1..12) and ExSN boards
    /// (slot 13..18 -> board 1..6), deduped; then raise <see cref="Changed"/>.</summary>
    public void SetFromSlots(int slot1, int slot2, int slot3, int slot4)
    {
        var slots = new[] { slot1, slot2, slot3, slot4 };
        _boards = slots.Where(v => v is >= 1 and <= 12).Distinct().ToArray();
        _exSnBoards = slots.Where(v => v is >= 13 and <= 18).Select(v => v - 12).Distinct().ToArray();
        Changed?.Invoke();
    }

    /// <summary>Shared instance consulted by the domain read path.</summary>
    public static LoadedSrxState Default { get; } = new();
}
