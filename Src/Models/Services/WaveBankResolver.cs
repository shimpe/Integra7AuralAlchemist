namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure rule mapping a wave group's (Type, ID) to a waveform bank name: Internal→INT,
/// SRX→SRX{id} (id 1..12). Anything else falls back to INT.</summary>
public static class WaveBankResolver
{
    public const string TypeInternal = "Internal";
    public const string TypeSrx = "SRX";

    public static string BankName(string groupType, int groupId)
        => groupType == TypeSrx && groupId is >= 1 and <= 12 ? $"SRX{groupId}" : "INT";
}
