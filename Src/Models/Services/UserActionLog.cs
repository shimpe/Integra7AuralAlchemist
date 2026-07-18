using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Records what the user did, so a log read after the fact shows the actions that led to a
/// problem instead of only the MIDI traffic they caused. Every line is tagged <c>[UI]</c>, so
/// `grep "\[UI\]"` gives a clean transcript of the session.
///
/// Logged at Information level: this is the context you want in a bug report, and it is a handful of
/// lines per minute of use, not per parameter change.</summary>
public static class UserActionLog
{
    public const string Tag = "[UI]";

    public static void Action(string what) => Log.Information($"{Tag} {what}");

    /// <summary>Marks the start and end of something the user triggered that takes a while, so a
    /// truncated or stalled operation is obvious in the log.</summary>
    public static void Begin(string what) => Log.Information($"{Tag} BEGIN {what}");

    public static void End(string what) => Log.Information($"{Tag} END   {what}");

    public static void Failed(string what, string reason) => Log.Error($"{Tag} FAILED {what}: {reason}");
}
