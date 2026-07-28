using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Naming a value a snapshot holds, with the tables this build has.
///
/// A snapshot stores both the raw value the instrument holds and the string as the build that captured it
/// rendered that raw. The two age differently: the raw is the instrument's and never changes, while the
/// string is only as good as the name tables that build had. Every file written before this build learned
/// to name the SuperNATURAL tone category stores it as "36", and a comparison of two such files showed
/// "36" on both sides where it should say "Synth Pad/Strings".
///
/// So the file is the authority on the value and the database is the authority on its name, and where the
/// database has nothing better to say the stored string stands -- which is what keeps a parameter this
/// build has since dropped, or one that never had a name table, readable rather than blank.</summary>
public static class SnapshotValueNames
{
    /// <summary>The best display this build can give for a stored value.</summary>
    public static string Best(Integra7Parameters parameters, string path, long? raw, string stored)
    {
        // A text parameter -- a tone name -- has no raw: its value is its string, and there is nothing to
        // look up.
        if (raw is not { } value) return stored;

        // LookupIndex rather than Lookup, which asserts: a path this build does not have is an ordinary
        // thing for an old file to contain, not a programming error.
        if (parameters.LookupIndex(path) < 0) return stored;

        var spec = parameters.Lookup(path);

        if (spec.Repr is { } repr && RawIsTheReprKey(spec, value) &&
            repr.TryGetValue((int)value, out var name))
            return name;

        if (spec.Discrete is { } discrete)
            foreach (var entry in discrete)
                if (entry.Item1 == value)
                    return entry.Item2;

        return stored;
    }

    /// <summary>Repr is keyed by the displayed number, not by the raw one:
    /// <see cref="SysexParameterValueInterpreter"/> maps the raw through IMin/IMax to OMin/OMax before it
    /// looks a name up, and an MFX-style parameter stores its displayed 0 as raw 32768. Reading such a
    /// repr at the raw would not miss -- 0 is a key it has -- it would answer confidently and wrongly, and
    /// a wrong name is worse than a number. So the raw is used only where that mapping is the identity,
    /// and everything else keeps what the file says.
    ///
    /// A raw outside the parameter's own input range is refused for the same reason: a hand-edited file
    /// can hold anything, and nothing that is not a value of this parameter should be named as one.</summary>
    private static bool RawIsTheReprKey(Integra7ParameterSpec spec, long raw) =>
        spec.IMin == spec.OMin && spec.IMax == spec.OMax && raw >= spec.IMin && raw <= spec.IMax;
}
