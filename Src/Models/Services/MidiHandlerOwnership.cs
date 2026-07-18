namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The rule that keeps a MIDI request paired with its reply. The port has one handler slot,
/// so a reader may only hand it back while it still owns it; a reader that finishes after another has
/// taken over would otherwise detach that one, which then never receives its reply.</summary>
public static class MidiHandlerOwnership
{
    /// <summary>True when <paramref name="requester"/> is the handler currently installed, and may
    /// therefore restore the default handler.</summary>
    public static bool MayRestoreDefault(object? installed, object? requester) =>
        requester is not null && Equals(installed, requester);
}
