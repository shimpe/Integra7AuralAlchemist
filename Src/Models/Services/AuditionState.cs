using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>What a running audition has borrowed from a part, and what it is playing there instead.
///
/// <b>Immutable, and a record, because the one thing this must never do is lose the memory.</b> Every
/// transition answers a new state rather than editing this one, so there is no path on which a field is
/// half updated -- and the caller that holds it can only replace it, never quietly mutate it.
///
/// <b><see cref="Borrowed"/> is set once per session.</b> Choosing a second candidate while one is playing
/// replaces <see cref="PlayingPath"/> and nothing else. That is what lets a user browse ten patches and
/// still get back the sound that was on the part before the first of them.</summary>
/// <param name="ZeroBasedPartNo">The part being borrowed, or -1 when nothing is.</param>
/// <param name="ToneType">The engine that part holds. <b>Carried by the session, not taken from each new
/// candidate</b>: it is what a second candidate has to match, and reading it off the candidate itself would
/// make every candidate match itself and let a tone of another engine through the guard.</param>
/// <param name="Borrowed">The part's own tone, captured before the first candidate was written.</param>
/// <param name="PlayingPath">The file being heard. <b>The path, not the name</b> -- two library files can
/// hold tones of the same name, and the panel decides whether its button says Stop by asking whether the
/// selected row is this one.</param>
public sealed record AuditionState(int ZeroBasedPartNo, string ToneType, Integra7Snapshot? Borrowed,
    string PlayingPath)
{
    public static readonly AuditionState Idle = new(-1, "", null, "");

    public bool IsRunning => Borrowed is not null;

    /// <summary>Whether this file is the one being heard. Case-insensitive, because Windows and macOS both
    /// hand back a path that differs from the stored one only in case.</summary>
    public bool IsPlaying(string filePath) =>
        IsRunning && string.Equals(PlayingPath, filePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Begin, remembering what was there. A start over a running session is a start: the caller
    /// has already given the previous one back, or has decided not to.</summary>
    public AuditionState Start(int zeroBasedPartNo, string toneType, Integra7Snapshot borrowed,
        string playingPath) =>
        new(zeroBasedPartNo, toneType, borrowed, playingPath);

    /// <summary>Play something else in the same part, keeping the memory and the engine. Idle stays idle: a
    /// switch with nothing running would otherwise invent a session holding nothing, and Stop would then
    /// write nothing back over the candidate the instrument is still playing.</summary>
    public AuditionState Switch(string playingPath) =>
        IsRunning ? this with { PlayingPath = playingPath } : this;

    public AuditionState Stop() => Idle;
}
