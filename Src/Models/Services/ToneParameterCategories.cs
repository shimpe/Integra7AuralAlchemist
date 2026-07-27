using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The groups a randomise offers to tick. Deliberately the same six-plus-one for every engine
/// rather than a list per engine: the dialog then has one shape, and "leave the filter alone" means the
/// same thing whichever tone is loaded.</summary>
public enum ToneCategory
{
    PitchAndOscillator,
    WaveChoice,
    Filter,
    Amplifier,
    LfoAndModulation,
    Effects,

    /// <summary>SuperNATURAL Acoustic only. Its tone is mostly the instrument's own modify parameters,
    /// whose meaning changes with the instrument -- Modify Parameter 1 is String Resonance on a grand
    /// piano, Noise Level on a Rhodes, Mallet Hardness on a vibraphone. They cannot honestly be sorted
    /// into filter/amp/pitch by name, so they get a category of their own instead of a wrong one.</summary>
    InstrumentCharacter,
}

/// <summary>Which category a tone parameter belongs to, if any.
///
/// <b>Unmapped means never randomised.</b> This is the whole safety model: output assign, control
/// assignments, receive switches, mute groups, names and velocity zones are excluded because no rule
/// names them, not because a blocklist remembers them. A blocklist would have to be extended every time
/// the parameter database gains an entry, and the entry someone forgets is the one that silences a
/// partial or re-routes an output.
///
/// Rules are matched against the part of the path after the block name, and the first match wins, so
/// each block's list is written longest-prefix-first: "OSC Pitch Env" has to be tried before "OSC
/// Pitch". Pure, so all of it is unit-tested against the real parameter database.</summary>
public static class ToneParameterCategories
{
    private const ToneCategory Pitch = ToneCategory.PitchAndOscillator;
    private const ToneCategory Wave = ToneCategory.WaveChoice;
    private const ToneCategory Filter = ToneCategory.Filter;
    private const ToneCategory Amp = ToneCategory.Amplifier;
    private const ToneCategory Lfo = ToneCategory.LfoAndModulation;
    private const ToneCategory Fx = ToneCategory.Effects;
    private const ToneCategory Character = ToneCategory.InstrumentCharacter;

    /// <summary>An envelope belongs to what it modulates -- Filter Env to Filter, AMP Env to Amplifier,
    /// OSC Pitch Env to Pitch. That is how a user thinks about "leave the filter alone", and it is why
    /// the tables below are not simply "anything with Env in it".</summary>
    private static readonly (string Prefix, ToneCategory Category)[] SnSynthCommon =
    [
        ("Octave Shift", Pitch), ("Pitch Bend Range", Pitch), ("Portamento Time", Pitch),
        ("Analog Feel", Pitch),
        ("Wave Shape", Wave),
        ("Tone Level", Amp),
        ("Ring Switch", Fx), ("TFX Switch", Fx),
    ];

    private static readonly (string, ToneCategory)[] SnSynthPartial =
    [
        ("OSC Pitch Env", Pitch), ("OSC Pitch", Pitch), ("OSC Detune", Pitch),
        ("OSC Pulse Width", Pitch), ("Super Saw Detune", Pitch),
        ("OSC Wave", Wave), ("Wave Gain", Wave), ("Wave Number", Wave),
        ("Filter", Filter), ("HPF Cutoff", Filter), ("Cutoff Aftertouch Sens", Filter),
        ("AMP", Amp), ("Level Aftertouch Sens", Amp),
        ("Modulation LFO", Lfo), ("LFO", Lfo),
    ];

    private static readonly (string, ToneCategory)[] Mfx =
    [
        ("MFX Parameter", Fx),
        // No "MFX Control" rule. MFX Control Assign, Source and Sens name which incoming MIDI
        // controller drives which MFX parameter -- routing, not sound. Randomising them changes nothing
        // audible until a controller moves, and rewires a mapping the user set up on purpose.
        ("MFX Chorus Send Level", Fx), ("MFX Reverb Send Level", Fx),
    ];

    private static readonly (string, ToneCategory)[] PcmSynthCommon =
    [
        ("PCM Synth Tone Coarse Tune", Pitch), ("PCM Synth Tone Fine Tune", Pitch),
        ("Octave Shift", Pitch), ("Stretch Tune Depth", Pitch), ("Pitch Bend Range", Pitch),
        ("Portamento Time", Pitch), ("Analog Feel", Pitch),
        ("Cutoff Offset", Filter), ("Resonance Offset", Filter),
        ("PCM Synth Tone Level", Amp), ("PCM Synth Tone Pan", Amp),
        ("Attack Time Offset", Amp), ("Release Time Offset", Amp), ("Velocity Sens Offset", Amp),
    ];

    /// <summary>The "Common 2" blocks hold two things between them: a phrase number, which is a demo
    /// phrase and not a sound, and the TFX switch, which is. Verified against the database -- TFX Switch
    /// is in Common 2 for both PCM engines and in plain Common for all three SuperNATURAL ones.</summary>
    private static readonly (string, ToneCategory)[] PcmCommon2 =
    [
        ("TFX Switch", Fx),
    ];

    private static readonly (string, ToneCategory)[] PcmSynthPartial =
    [
        ("Pitch Env", Pitch), ("Partial Coarse Tune", Pitch), ("Partial Fine Tune", Pitch),
        ("Partial Random Pitch Depth", Pitch), ("Wave Pitch Keyfollow", Pitch),
        ("Wave Group Type", Wave), ("Wave Group ID", Wave), ("Wave Number", Wave),
        ("Wave Gain", Wave), ("Wave FXM", Wave), ("Wave Tempo Sync", Wave),
        ("TVF", Filter),
        ("TVA", Amp), ("Bias", Amp), ("Partial Level", Amp), ("Partial Pan", Amp),
        ("Partial Random Pan Depth", Amp), ("Partial Alternate Pan Depth", Amp),
        ("Modulation LFO", Lfo), ("LFO1", Lfo), ("LFO2", Lfo), ("LFO Step", Lfo),
        ("Partial Chorus Send Level", Fx), ("Partial Reverb Send Level", Fx),
    ];

    private static readonly (string, ToneCategory)[] SnAcousticCommon =
    [
        ("Octave Shift", Pitch), ("Portamento Time Offset", Pitch),
        ("Cutoff Offset", Filter), ("Resonance Offset", Filter),
        ("Attack Time Offset", Amp), ("Release Time Offset", Amp), ("Tone Level", Amp),
        ("Vibrato Rate", Lfo), ("Vibrato Depth", Lfo), ("Vibrato Delay", Lfo),
        ("TFX Switch", Fx),
        // Last, so that a future concrete rule above it still wins.
        ("Modify Parameter ", Character),
    ];

    private static readonly (string, ToneCategory)[] SnDrumCommon =
    [
        ("Kit Level", Amp),
        ("Ambience Level", Fx), ("TFX Switch", Fx),
    ];

    private static readonly (string, ToneCategory)[] SnDrumPartial =
    [
        ("Tune", Pitch),
        ("Inst Number", Wave), ("Variation", Wave),
        ("Brilliance", Filter),
        ("Attack", Amp), ("Decay", Amp), ("Level", Amp), ("Pan", Amp), ("Stereo Width", Amp),
        ("Dynamic Range", Amp),
        ("Chorus Send Level", Fx), ("Reverb Send Level", Fx),
    ];

    private static readonly (string, ToneCategory)[] PcmDrumCommon =
    [
        ("Kit Level", Amp),
    ];

    /// <summary>WMT slot numbers are stripped before matching (see <see cref="Normalise"/>), so one rule
    /// covers all four wave-mix-table slots.</summary>
    private static readonly (string, ToneCategory)[] PcmDrumPartial =
    [
        ("Pitch Env", Pitch), ("Partial Coarse Tune", Pitch), ("Partial Fine Tune", Pitch),
        ("Partial Random Pitch Depth", Pitch),
        ("WMT Wave Coarse Tune", Pitch), ("WMT Wave Fine Tune", Pitch),
        ("WMT Wave Group Type", Wave), ("WMT Wave Group ID", Wave), ("WMT Wave Number", Wave),
        ("WMT Wave Gain", Wave), ("WMT Wave FXM", Wave), ("WMT Wave Tempo Sync", Wave),
        ("WMT Wave Switch", Wave),
        ("TVF", Filter),
        ("TVA", Amp), ("Partial Level", Amp), ("Partial Pan", Amp),
        ("Partial Random Pan Depth", Amp), ("Partial Alternate Pan Depth", Amp),
        ("WMT Wave Level", Amp), ("WMT Wave Pan", Amp),
        // No LFO rule: a PCM drum partial has no LFO at all (verified against the database). A rule for
        // one would make PresentIn claim a category the engine does not have, and the dialog would offer
        // a tick that could not do anything.
        ("Partial Chorus Send Level", Fx), ("Partial Reverb Send Level", Fx),
    ];

    /// <summary>Block name (the part of a path before the first '/') to its rules. A block absent from
    /// here -- the Comp-EQ blocks, the PCM Partial Mix Table -- has nothing randomisable in it at
    /// all.</summary>
    private static readonly Dictionary<string, (string, ToneCategory)[]> ByBlock = new(StringComparer.Ordinal)
    {
        ["SuperNATURAL Synth Tone Common"] = SnSynthCommon,
        ["SuperNATURAL Synth Tone Common MFX"] = Mfx,
        ["SuperNATURAL Synth Tone Partial"] = SnSynthPartial,
        ["PCM Synth Tone Common"] = PcmSynthCommon,
        ["PCM Synth Tone Common 2"] = PcmCommon2,
        ["PCM Synth Tone Common MFX"] = Mfx,
        ["PCM Synth Tone Partial"] = PcmSynthPartial,
        ["SuperNATURAL Acoustic Tone Common"] = SnAcousticCommon,
        ["SuperNATURAL Acoustic Tone Common MFX"] = Mfx,
        ["SuperNATURAL Drum Kit Common"] = SnDrumCommon,
        ["SuperNATURAL Drum Kit Common MFX"] = Mfx,
        ["SuperNATURAL Drum Kit Partial"] = SnDrumPartial,
        ["PCM Drum Kit Common"] = PcmDrumCommon,
        ["PCM Drum Kit Common 2"] = PcmCommon2,
        ["PCM Drum Kit Common MFX"] = Mfx,
        ["PCM Drum Kit Partial"] = PcmDrumPartial,
    };

    /// <summary>The category this path belongs to, or null when it must never be randomised.</summary>
    public static ToneCategory? For(string path)
    {
        var slash = path.IndexOf('/');
        if (slash < 0) return null;

        if (!ByBlock.TryGetValue(path[..slash], out var rules)) return null;

        var name = Normalise(path[(slash + 1)..]);
        // A reserved parameter says so in its own name, but in at least three shapes: a block's filler
        // is "Reserved3", an MFX slot the selected effect does not use is "MFX Parameter 1/Thru
        // (Reserved)", and some are simply "Phaser 3 Reserved". Matched anywhere in the name rather than
        // shape by shape, because the shapes are not a closed set -- the third one was found only when a
        // test started selecting on the database's own Reserved flag instead of on the spelling. Every
        // path in the database containing the word is flagged reserved, so this cannot catch a real
        // parameter. The caller excludes reserved parameters too; a rule that swept one up would still
        // be a rule matching more than it means to.
        if (name.Contains("Reserved", StringComparison.Ordinal)) return null;

        foreach (var (prefix, category) in rules)
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return category;

        return null;
    }

    /// <summary>"WMT3 Wave Level" becomes "WMT Wave Level", so one rule covers all four slots. Nothing
    /// else is normalised: LFO1 and LFO2 keep their numbers because they are genuinely two LFOs and a
    /// later build may well want to offer them separately.</summary>
    private static string Normalise(string name) =>
        name.Length > 3 && name.StartsWith("WMT", StringComparison.Ordinal) && char.IsDigit(name[3])
            ? "WMT" + name[4..]
            : name;

    /// <summary>Which categories this engine has any parameter in. The dialog shows the full list and
    /// disables the rest, so its shape does not change from one engine to the next.</summary>
    public static IReadOnlySet<ToneCategory> PresentIn(string toneType)
    {
        // Block names, not the Offset2 addresses: the address carries an "Offset2/" prefix and a partial
        // number, the block name in a path carries neither.
        var blocks = toneType switch
        {
            "SN-S" => new[] { "SuperNATURAL Synth Tone Common", "SuperNATURAL Synth Tone Common MFX",
                "SuperNATURAL Synth Tone Partial" },
            "PCMS" => ["PCM Synth Tone Common", "PCM Synth Tone Common 2", "PCM Synth Tone Common MFX",
                "PCM Synth Tone Partial"],
            "SN-A" => ["SuperNATURAL Acoustic Tone Common", "SuperNATURAL Acoustic Tone Common MFX"],
            "SN-D" => ["SuperNATURAL Drum Kit Common", "SuperNATURAL Drum Kit Common MFX",
                "SuperNATURAL Drum Kit Partial"],
            "PCMD" => ["PCM Drum Kit Common", "PCM Drum Kit Common 2", "PCM Drum Kit Common MFX",
                "PCM Drum Kit Partial"],
            _ => [],
        };

        return blocks.SelectMany(b => ByBlock.TryGetValue(b, out var rules)
                ? rules.Select(r => r.Item2)
                : [])
            .ToHashSet();
    }
}
