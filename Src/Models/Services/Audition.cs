using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Borrowing a part to hear something else in it, and giving it back.
///
/// <b>Two operations, and the first one reads.</b> Unlike a morph -- which never reads, because a blend
/// covers every parameter by construction -- an audition's whole safety is the capture it takes before it
/// writes anything. That capture is the only copy of the sound the user had, and it is why this cannot be
/// made faster by skipping the read.
///
/// <b>Same engine only.</b> A tone can only be written into a part whose temporary tone is already that
/// engine, and making the other case work costs a preset change and a full part reload each way -- see the
/// design document, which records why that is a later phase. The guard runs before the capture, so a
/// refusal costs nothing.
///
/// The lease is the caller's for both operations, and not optional: each of these walks every block of the
/// tone, so anything else talking to the device in between would interleave with a capture or a restore
/// that has to be one conversation.</summary>
public static class Audition
{
    /// <summary>Capture what the part holds, then write <paramref name="candidate"/> over it. Answers the
    /// capture, which the caller must hold until it stops.</summary>
    public static async Task<Integra7Snapshot> StartAsync(Integra7Domain domain, Integra7Snapshot candidate,
        int zeroBasedPartNo, string currentToneType, IMidiLease lease)
    {
        // Before the read, so a candidate that could never have been written does not cost a capture.
        StudioSetSnapshotService.EnsureToneFitsPart(candidate, zeroBasedPartNo, currentToneType);

        var borrowed = await StudioSetSnapshotService.CaptureToneAsync(domain, zeroBasedPartNo,
            currentToneType, "borrowed by audition", lease);

        await StudioSetSnapshotService.RestoreToneAsync(domain, candidate, zeroBasedPartNo,
            currentToneType, lease);

        return borrowed;
    }

    /// <summary>Write back what <see cref="StartAsync"/> captured. Throwing leaves the caller holding the
    /// capture, which is what lets Stop be pressed again.</summary>
    public static Task StopAsync(Integra7Domain domain, Integra7Snapshot borrowed, int zeroBasedPartNo,
        string currentToneType, IMidiLease lease) =>
        StudioSetSnapshotService.RestoreToneAsync(domain, borrowed, zeroBasedPartNo, currentToneType, lease);
}
