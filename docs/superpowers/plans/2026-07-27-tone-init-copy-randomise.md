# Tone-level init, copy and randomise — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax
> for tracking.

**Goal:** Give the selected part four new tone-level actions — Init, Copy, Paste and a constrained
Randomise — on top of the tone capture/restore machinery that already ships.

**Architecture:** Four pure services decide *what* to change (`ToneParameterCategories`,
`ToneRandomiser`, `ToneClipboard`, `InitToneResolution`); one small device service applies a randomise
block by block (`ToneRandomisationService`); `MainWindowViewModel` gains four commands that reuse the
existing `StudioSetSnapshotService.CaptureToneAsync` / `RestoreToneAsync` for everything whole-tone.

**Tech stack:** .NET 10, C# 13, Avalonia 12, ReactiveUI (`[ReactiveCommand]` / `[Reactive]` source
generators), NUnit 3.

**Spec:** `docs/superpowers/specs/2026-07-27-tone-init-copy-randomise-design.md`. Read it before
starting; it records why each of these decisions is what it is.

---

## Conventions for every task

**Build and test with the user-local SDK.** The system `dotnet` is 8/9 and too old:

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj
```

If the build fails with `MSB3027`/`MSB3021 ... file is locked by`, the user's own running application or
Rider's Avalonia previewer holds `Src/bin`. **Do not kill either.** Redirect the output instead — the
four-deep nesting and the junction are both load-bearing, because several tests walk
`AppContext.BaseDirectory` + `..\..\..\..` to find `Src\Assets\parameters.bin`:

```powershell
$root = "C:\Scripts\Temp\claude\verify"
New-Item -ItemType Directory -Force -Path "$root\o\1\2\3" | Out-Null
if (-not (Test-Path "$root\Src")) { New-Item -ItemType Junction -Path "$root\Src" -Target "D:\Projects\Integra7AuralAlchemist\Src" | Out-Null }
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln -p:OutputPath="$root\o\1\2\3\"
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj -p:OutputPath="$root\o\1\2\3\"
```

A `--filter` argument must come **before** `-p:OutputPath`, or `dotnet test` silently runs the whole
suite.

**Git.** Work on the branch `feature/tone-init-copy-randomise`, which already exists and already holds
the spec commit. Stage explicit paths — never `git add -A` or `git add .`, and never stage
`Src/Assets/new-icon-orig.svg`, which is the user's own untracked file. Never pass `--no-verify`. Do not
merge to `main` and do not push; the user does both.

**House style.** Comments explain *why*, not *what*. Never hardcode a colour in XAML — use
`{StaticResource ...}`. An em dash in XAML prose must be the character `—`; a literal `--` inside an XML
comment fails the build.

---

## File structure

**New — pure services (no Avalonia, no MIDI, fully unit-tested):**

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/ToneParameterCategories.cs` | Which category a parameter path belongs to, per engine; which categories an engine has at all |
| `Src/Models/Services/ToneRandomiser.cs` | Given parameters, strengths and a `Random`, the new raw values |
| `Src/Models/Services/ToneClipboard.cs` | One session-scoped tone snapshot slot |
| `Src/Models/Services/InitToneResolution.cs` | Which file or asset is the init tone for an engine |

**New — device service:**

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/ToneRandomisationService.cs` | Read a block, apply new raw values, record one journal step, bulk-write |

**New — UI:**

| File | Responsibility |
| --- | --- |
| `Src/ViewModels/ConfirmViewModel.cs` | Message plus Yes/No commands answering `bool` |
| `Src/Views/ConfirmDialog.axaml` (+ `.axaml.cs`) | The application's one reusable yes/no window |
| `Src/ViewModels/RandomiseToneViewModel.cs` | Category rows, per-category strength, target line |
| `Src/Views/RandomiseToneDialog.axaml` (+ `.axaml.cs`) | The randomise dialog |

**New — assets:** `Src/Assets/InitTones/{PCMS,PCMD,SN-S,SN-A,SN-D}.json` (Task 11, needs hardware).

**Modified:**

| File | Change |
| --- | --- |
| `Src/Models/Services/ToneDomainNames.cs` | `IsDrumKit`, `DrumPartialFor` |
| `Src/Models/Services/LibrarySettings.cs` | Settings grow a per-engine init-tone mark |
| `Src/ViewModels/LibraryViewModel.cs` | Mark the selected entry as an init tone |
| `Src/Views/LibraryView.axaml` | The button for it |
| `Src/ViewModels/MainWindowViewModel.cs` | Four commands, two interactions, the clipboard |
| `Src/Views/MainWindow.axaml` | Four toolbar buttons |
| `Src/Views/MainWindow.axaml.cs` | Register the two new dialog handlers |

**New tests:** `Tests/TestToneParameterCategories.cs`, `Tests/TestToneRandomiser.cs`,
`Tests/TestToneRandomisationService.cs`, `Tests/TestToneClipboard.cs`,
`Tests/TestInitToneResolution.cs`; plus cases added to `Tests/TestLibrarySettings.cs`.

**Test fixtures you will reuse** (all already in the suite, all `internal` to the Tests assembly):
- `TestFailedReadKeepsValues.LoadParameters()` — the real `parameters.bin`.
- `TestFailedReadKeepsValues.SilentApi` — an `IIntegra7Api` whose reads time out.
- `Integra7SnapshotRestoreTests.BlankReplyApi` — a `SilentApi` whose reads succeed with all-zero data.
- `Integra7SnapshotRestoreTests.BuildDomain(api)` — an `Integra7Domain` over that fake.
- `Integra7SnapshotRestoreTests.NoRealMidi()` — a lease that throws if touched.

Check the exact fixture class names with
`grep -n "internal static Integra7Domain BuildDomain" Tests/TestStudioSetSnapshot.cs` before using them.

---

### Task 1: Parameter categories

**Files:**
- Create: `Src/Models/Services/ToneParameterCategories.cs`
- Test: `Tests/TestToneParameterCategories.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestToneParameterCategories.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What may be randomised, and what may never be.
///
/// These run against the real parameter database rather than against invented paths, so a parameter
/// this build renames stops being categorised and a test says so, instead of it silently dropping out
/// of randomisation with nothing to notice.</summary>
public class ToneParameterCategoriesTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    [TestCase("SuperNATURAL Synth Tone Partial/OSC Pitch", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Synth Tone Partial/OSC Pitch Env Depth", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Synth Tone Partial/OSC Wave", ToneCategory.WaveChoice)]
    [TestCase("SuperNATURAL Synth Tone Partial/Filter Cutoff", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Synth Tone Partial/Filter Env Attack Time", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Synth Tone Partial/AMP Env Decay Time", ToneCategory.Amplifier)]
    [TestCase("SuperNATURAL Synth Tone Partial/Modulation LFO Rate", ToneCategory.LfoAndModulation)]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Parameter 1", ToneCategory.Effects)]
    [TestCase("PCM Synth Tone Partial/TVF Cutoff Frequency", ToneCategory.Filter)]
    [TestCase("PCM Synth Tone Partial/TVA Env Time 1", ToneCategory.Amplifier)]
    [TestCase("PCM Synth Tone Partial/LFO1 Rate", ToneCategory.LfoAndModulation)]
    [TestCase("PCM Synth Tone Partial/LFO Step 1", ToneCategory.LfoAndModulation)]
    [TestCase("PCM Synth Tone Partial/Wave Number L (Mono)", ToneCategory.WaveChoice)]
    [TestCase("PCM Synth Tone Common/Cutoff Offset", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Acoustic Tone Common/Modify Parameter 1", ToneCategory.InstrumentCharacter)]
    [TestCase("SuperNATURAL Acoustic Tone Common/Vibrato Rate", ToneCategory.LfoAndModulation)]
    [TestCase("SuperNATURAL Drum Kit Partial/Brilliance", ToneCategory.Filter)]
    [TestCase("SuperNATURAL Drum Kit Partial/Tune", ToneCategory.PitchAndOscillator)]
    [TestCase("SuperNATURAL Drum Kit Partial/Inst Number", ToneCategory.WaveChoice)]
    [TestCase("PCM Drum Kit Partial/WMT1 Wave Number L (Mono)", ToneCategory.WaveChoice)]
    [TestCase("PCM Drum Kit Partial/WMT3 Wave Coarse Tune", ToneCategory.PitchAndOscillator)]
    [TestCase("PCM Drum Kit Partial/TVF Cutoff Frequency", ToneCategory.Filter)]
    public void Categorises_a_parameter(string path, ToneCategory expected)
    {
        Assert.That(ToneParameterCategories.For(path), Is.EqualTo(expected));
    }

    [TestCase("PCM Drum Kit Partial/Partial Output Assign")]
    [TestCase("SuperNATURAL Drum Kit Partial/Output Assign")]
    [TestCase("PCM Drum Kit Partial/Partial Name")]
    [TestCase("PCM Drum Kit Partial/Assign Type")]
    [TestCase("PCM Drum Kit Partial/Mute Group")]
    [TestCase("PCM Drum Kit Partial/WMT1 Velocity Range Lower")]
    [TestCase("PCM Synth Tone Common/PCM Synth Tone Name")]
    [TestCase("PCM Synth Tone Common/Matrix Control 1 Source")]
    [TestCase("PCM Synth Tone Partial/Partial Receive Sustain")]
    [TestCase("PCM Synth Tone Partial Mix Table/PMT 1 Keyboard Range Lower")]
    [TestCase("SuperNATURAL Synth Tone Common/Tone Name")]
    [TestCase("SuperNATURAL Synth Tone Common MFX/MFX Type")]
    [TestCase("SuperNATURAL Acoustic Tone Common/Instrument")]
    [TestCase("SuperNATURAL Drum Kit Common Comp-EQ/Comp1 Switch")]
    [TestCase("Studio Set Part/Part Output Assign")]
    public void Never_randomises_routing_identity_or_control_assignments(string path)
    {
        Assert.That(ToneParameterCategories.For(path), Is.Null);
    }

    /// <summary>Reserved parameters are excluded by the caller's GetRelevantParameters(false, false)
    /// too, but a rule that swept one up would be a rule matching more than it should, so the table
    /// itself has to refuse them.</summary>
    [Test]
    public void Never_categorises_a_reserved_parameter()
    {
        var reserved = _parameters.GetParametersWithPrefix("SuperNATURAL Synth Tone")
            .Where(p => p.Path.Contains("/Reserved"))
            .Select(p => p.Path)
            .ToList();

        Assert.That(reserved, Is.Not.Empty, "the fixture assumes this build has reserved parameters");
        foreach (var path in reserved)
            Assert.That(ToneParameterCategories.For(path), Is.Null, path);
    }

    [Test]
    public void Reports_which_categories_an_engine_has()
    {
        Assert.That(ToneParameterCategories.PresentIn("SN-A"),
            Does.Contain(ToneCategory.InstrumentCharacter));
        Assert.That(ToneParameterCategories.PresentIn("SN-S"),
            Does.Not.Contain(ToneCategory.InstrumentCharacter));
        foreach (var engine in new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" })
            Assert.That(ToneParameterCategories.PresentIn(engine), Does.Contain(ToneCategory.Filter),
                engine);
    }

    /// <summary>Every path a randomise will really be offered comes from these blocks, so the table is
    /// checked against them rather than against a list written here: a block that gains a parameter this
    /// build does not categorise is fine (unmapped means untouched), but a *rule* that matches nothing at
    /// all is a typo, and this is what catches it.</summary>
    /// <summary>An address names a partial by number ("Offset2/PCM Synth Tone Partial 3"); a parameter
    /// path names the block generically ("PCM Synth Tone Partial/TVF Cutoff Frequency"). Only the
    /// trailing number after the word "Partial" is dropped -- "PCM Synth Tone Common 2" is a block in its
    /// own right, and stripping its 2 would look up the wrong rules.</summary>
    private static string BlockNameOf(string offset2)
    {
        var name = offset2["Offset2/".Length..];
        var space = name.LastIndexOf(' ');
        if (space <= 0 || !int.TryParse(name[(space + 1)..], out _)) return name;

        var beforeNumber = name[..space];
        return beforeNumber.EndsWith(" Partial", StringComparison.Ordinal) ? beforeNumber : name;
    }

    [Test]
    public void Every_engine_has_at_least_one_parameter_in_every_category_it_claims()
    {
        foreach (var engine in new[] { "PCMS", "PCMD", "SN-S", "SN-A", "SN-D" })
        {
            var found = ToneDomainNames.For(engine, 0)
                .SelectMany(b => _parameters.GetParametersWithPrefix(BlockNameOf(b.Offset2) + "/"))
                .Select(p => ToneParameterCategories.For(p.Path))
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .ToHashSet();

            Assert.That(found, Is.EquivalentTo(ToneParameterCategories.PresentIn(engine)), engine);
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneParameterCategoriesTests
```

Expected: compile errors — `ToneCategory` and `ToneParameterCategories` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/ToneParameterCategories.cs`:

```csharp
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
        // A reserved parameter is named "Reserved3" or "... (Reserved)". The caller excludes them too,
        // but a rule that swept one up would be a rule matching more than it means to.
        if (name.StartsWith("Reserved", StringComparison.Ordinal) ||
            name.Contains("(Reserved)", StringComparison.Ordinal)) return null;

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
```

- [ ] **Step 4: Run the test until it passes**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneParameterCategoriesTests
```

Expected: PASS. If `Every_engine_has_at_least_one_parameter_in_every_category_it_claims` fails, a rule
prefix does not match any real parameter name — check it against
`grep -o 'path:"<block>/[^"]*"' Tools/ParameterBlobGenerator/ParameterDefinitions.cs`. Fix the rule, not
the test.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ToneParameterCategories.cs Tests/TestToneParameterCategories.cs
git commit -m "feat: categorise tone parameters for randomisation"
```

---

### Task 2: The randomiser

**Files:**
- Create: `Src/Models/Services/ToneRandomiser.cs`
- Test: `Tests/TestToneRandomiser.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestToneRandomiser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>How far a value may move, and what may not move at all.
///
/// Driven through a real domain built over the real parameter database rather than through invented
/// parameter specs: the rules being tested are about IMin/IMax, Repr and IsParent, which are properties
/// of the database, and a hand-rolled spec would let a wrong assumption about it pass.</summary>
public class ToneRandomiserTests
{
    private const string Block = "Offset2/SuperNATURAL Synth Tone Partial 1";
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";

    private static List<FullyQualifiedParameter> PartialParameters()
    {
        var domain = Integra7SnapshotRestoreTests.BuildDomain(
            new Integra7SnapshotRestoreTests.BlankReplyApi());
        return domain.GetDomain("Temporary Tone Part 1", Offset, Block)
            .GetRelevantParameters(false, false);
    }

    private static RandomisationStrengths All(double strength) =>
        new(Enum.GetValues<ToneCategory>().ToDictionary(c => c, _ => strength));

    [Test]
    public void Changes_nothing_at_strength_zero()
    {
        var values = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.0), new Random(1));

        Assert.That(values, Is.Empty);
    }

    [Test]
    public void Never_leaves_a_parameters_own_range()
    {
        var parameters = PartialParameters();
        var byPath = parameters.ToDictionary(p => p.ParSpec.Path);

        var values = ToneRandomiser.NewValuesFor(parameters, All(1.0), new Random(2));

        Assert.That(values, Is.Not.Empty);
        foreach (var (path, raw) in values)
        {
            var spec = byPath[path].ParSpec;
            Assert.That(raw, Is.InRange((long)spec.IMin, (long)spec.IMax), path);
        }
    }

    /// <summary>The point of a strength control: a low one produces a recognisable version of the sound
    /// that was there, not a new one. Cutoff runs 0..127, so 10 % is a window of 13 either way -- and
    /// the reading is 0 (BlankReplyApi answers with zeros), so the result must stay within 13.</summary>
    [Test]
    public void A_low_strength_only_nudges_a_numeric_value()
    {
        const string cutoff = "SuperNATURAL Synth Tone Partial/Filter Cutoff";
        var strengths = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 0.1 });

        // Many draws, because one could land near the middle of the window by luck.
        for (var seed = 0; seed < 50; seed++)
        {
            var values = ToneRandomiser.NewValuesFor(PartialParameters(), strengths, new Random(seed));
            if (values.TryGetValue(cutoff, out var raw))
                Assert.That(raw, Is.InRange(0L, 13L), $"seed {seed}");
        }
    }

    [Test]
    public void Leaves_an_enum_alone_at_low_strength_and_redraws_it_at_full()
    {
        const string mode = "SuperNATURAL Synth Tone Partial/Filter Mode";
        var timid = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 0.0001 });
        var bold = new RandomisationStrengths(
            new Dictionary<ToneCategory, double> { [ToneCategory.Filter] = 1.0 });

        var timidHits = 0;
        var boldHits = 0;
        for (var seed = 0; seed < 30; seed++)
        {
            if (ToneRandomiser.NewValuesFor(PartialParameters(), timid, new Random(seed))
                .ContainsKey(mode)) timidHits++;
            if (ToneRandomiser.NewValuesFor(PartialParameters(), bold, new Random(seed))
                .ContainsKey(mode)) boldHits++;
        }

        Assert.That(timidHits, Is.Zero, "an enum practically never moves at a strength of 0.01 %");
        Assert.That(boldHits, Is.EqualTo(30), "an enum always moves at full strength");
    }

    [Test]
    public void Never_returns_a_discriminator_a_name_or_an_uncategorised_parameter()
    {
        var domain = Integra7SnapshotRestoreTests.BuildDomain(
            new Integra7SnapshotRestoreTests.BlankReplyApi());
        var common = domain.GetDomain("Temporary Tone Part 1", Offset,
            "Offset2/SuperNATURAL Synth Tone Common").GetRelevantParameters(false, false);
        var mfx = domain.GetDomain("Temporary Tone Part 1", Offset,
            "Offset2/SuperNATURAL Synth Tone Common MFX").GetRelevantParameters(false, false);

        var values = ToneRandomiser.NewValuesFor(common.Concat(mfx), All(1.0), new Random(3));

        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common/Tone Name"));
        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common MFX/MFX Type"));
        Assert.That(values.Keys, Does.Not.Contain("SuperNATURAL Synth Tone Common/Partial1 Switch"));
        Assert.That(values.Keys.All(p => ToneParameterCategories.For(p) is not null));
    }

    [Test]
    public void Is_deterministic_for_a_seed()
    {
        var first = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.5), new Random(7));
        var second = ToneRandomiser.NewValuesFor(PartialParameters(), All(0.5), new Random(7));

        Assert.That(second, Is.EqualTo(first));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneRandomiserTests
```

Expected: compile errors — `RandomisationStrengths` and `ToneRandomiser` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/ToneRandomiser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>How far each ticked category may move, 0..1. A category absent from the map, or present with
/// a strength of zero, is not randomised at all.</summary>
public sealed record RandomisationStrengths(IReadOnlyDictionary<ToneCategory, double> ByCategory)
{
    public double For(ToneCategory category) =>
        ByCategory.TryGetValue(category, out var s) ? Math.Clamp(s, 0.0, 1.0) : 0.0;

    public bool Any => ByCategory.Values.Any(s => s > 0.0);
}

/// <summary>What a randomise would change, and to what.
///
/// <b>Raw values, not display strings.</b> Every parameter has an integer raw range (IMin..IMax) that the
/// device actually stores, and arithmetic on it is exact. The display value is a formatted string, and
/// some of them are not even integers -- Master Tune's are fractional -- so a randomiser that worked in
/// display space would have to parse and re-format, and would quietly mangle those.
///
/// <b>Nothing here talks to a device.</b> The caller reads the block, hands the parameters over, applies
/// what comes back and writes. That is what makes this testable, and it is where the whole "what may
/// move" question is settled.</summary>
public static class ToneRandomiser
{
    /// <summary>The new raw value for each parameter that should change. A parameter absent from the
    /// result is one the caller must leave exactly as it is.</summary>
    public static IReadOnlyDictionary<string, long> NewValuesFor(
        IEnumerable<FullyQualifiedParameter> parameters, RandomisationStrengths strengths, Random rng)
    {
        Dictionary<string, long> result = [];

        foreach (var p in parameters)
        {
            var spec = p.ParSpec;

            // A discriminator decides how every parameter that depends on it is interpreted, so moving
            // one would mean writing values against a context that no longer holds. In this database
            // that is MFX Type and the SuperNATURAL Acoustic instrument -- both of which a user asking
            // to "randomise the effects" or "vary this piano" means to keep.
            if (spec.IsParent) continue;

            // A name is text; there is no range to draw from and nothing musical to gain.
            if (spec.Type == Integra7ParameterSpec.SpecType.ASCII) continue;

            if (ToneParameterCategories.For(spec.Path) is not { } category) continue;

            var strength = strengths.For(category);
            if (strength <= 0.0) continue;

            // Enumerated: the values are labels, so the distance between two of them means nothing and a
            // window around the current one would be arithmetic on names. Strength becomes the chance of
            // re-drawing instead, which is what keeps most switches and modes still at a low setting.
            var choices = LegalValues(spec);
            if (choices is not null)
            {
                if (rng.NextDouble() >= strength) continue;
                var drawn = choices[rng.Next(choices.Count)];
                if (drawn != p.RawNumericValue) result[spec.Path] = drawn;
                continue;
            }

            var window = (long)Math.Round(strength * (spec.IMax - spec.IMin));
            if (window <= 0) continue;

            // Symmetric around the value that is there, then clamped -- so a parameter already near its
            // limit stays legal instead of wrapping to the other end of its range, which for a cutoff or
            // a level is the difference between a nudge and a jump.
            var moved = Math.Clamp(p.RawNumericValue + rng.NextInt64(-window, window + 1),
                spec.IMin, spec.IMax);
            if (moved != p.RawNumericValue) result[spec.Path] = moved;
        }

        return result;
    }

    /// <summary>The raw values an enumerated parameter may legally take, or null when it is a plain
    /// numeric one. DISCRETE parameters carry an explicit list; NUMERIC ones with a Repr are switches and
    /// modes whose raw values are keys in it. <c>EffectiveRepr</c> is deliberately not consulted: it is
    /// the bank-resolved wave-name list, which is presentation, and wave *numbers* are a plain numeric
    /// range that should be drawn from as one.</summary>
    private static IReadOnlyList<long>? LegalValues(Integra7ParameterSpec spec)
    {
        if (spec.Discrete is { } discrete) return [.. discrete.Select(d => (long)d.Item1)];
        if (spec.Repr is { } repr) return [.. repr.Keys.Select(k => (long)k)];
        return null;
    }
}
```

- [ ] **Step 4: Run the test until it passes**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneRandomiserTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ToneRandomiser.cs Tests/TestToneRandomiser.cs
git commit -m "feat: compute randomised tone values in raw space"
```

---

### Task 3: Applying a randomise to the instrument

**Files:**
- Create: `Src/Models/Services/ToneRandomisationService.cs`
- Modify: `Src/Models/Services/ToneDomainNames.cs` (add `IsDrumKit` and `DrumPartialFor`)
- Test: `Tests/TestToneRandomisationService.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestToneRandomisationService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Randomise reaches the instrument as one bulk write per block and reaches the history as one
/// undo step. Both halves matter: the write is what makes it fast enough to use on a whole tone, and the
/// single step is what makes a randomise you dislike one press away from gone.</summary>
public class ToneRandomisationServiceTests
{
    private const string Offset = "Offset/Temporary SuperNATURAL Synth Tone";

    private static IReadOnlyList<(string, string, string)> OnePartial() =>
        [("Temporary Tone Part 1", Offset, "Offset2/SuperNATURAL Synth Tone Partial 1")];

    private static RandomisationStrengths Everything() =>
        new(Enum.GetValues<ToneCategory>().ToDictionary(c => c, _ => 1.0));

    [SetUp]
    public void ClearHistory() => EditJournal.Default.Clear();

    [Test]
    public async Task Records_one_undo_step_for_the_whole_operation()
    {
        var api = new Integra7SnapshotRestoreTests.BlankReplyApi();
        var domain = Integra7SnapshotRestoreTests.BuildDomain(api);

        var changed = await ToneRandomisationService.RandomiseAsync(
            domain, OnePartial(), Everything(), new Random(11), lease: null);

        Assert.That(changed, Is.GreaterThan(0));
        Assert.That(EditJournal.Default.CanUndo, Is.True);
        Assert.That(EditJournal.Default.TryUndo(out var pending), Is.True);
        Assert.That(pending!.Writes, Has.Count.EqualTo(changed),
            "one step, carrying every parameter the randomise moved");
        Assert.That(EditJournal.Default.CanUndo, Is.False, "and only one step");
    }

    [Test]
    public async Task Sends_one_transmission_per_block()
    {
        var api = new Integra7SnapshotRestoreTests.BlankReplyApi();
        var domain = Integra7SnapshotRestoreTests.BuildDomain(api);

        await ToneRandomisationService.RandomiseAsync(
            domain, OnePartial(), Everything(), new Random(12), lease: null);

        Assert.That(api.Transmissions, Is.EqualTo(1));
    }

    [Test]
    public async Task Changes_nothing_and_writes_nothing_when_no_category_is_ticked()
    {
        var api = new Integra7SnapshotRestoreTests.BlankReplyApi();
        var domain = Integra7SnapshotRestoreTests.BuildDomain(api);

        var changed = await ToneRandomisationService.RandomiseAsync(domain, OnePartial(),
            new RandomisationStrengths(new Dictionary<ToneCategory, double>()), new Random(13),
            lease: null);

        Assert.That(changed, Is.Zero);
        Assert.That(api.Transmissions, Is.Zero, "an untouched block is not rewritten");
        Assert.That(EditJournal.Default.CanUndo, Is.False);
    }

    /// <summary>A block the device does not answer for must abort rather than randomise from whatever
    /// values happen to be in memory -- which, for a block never read this session, are zeros.</summary>
    [Test]
    public void Refuses_when_the_device_does_not_answer()
    {
        var domain = Integra7SnapshotRestoreTests.BuildDomain(
            new TestFailedReadKeepsValues.SilentApi());

        Assert.That(async () => await ToneRandomisationService.RandomiseAsync(
                domain, OnePartial(), Everything(), new Random(14), lease: null),
            Throws.TypeOf<SnapshotFormatException>());
    }

    [Test]
    public void Names_the_drum_partial_block_for_a_note()
    {
        var block = ToneDomainNames.DrumPartialFor("SN-D", zeroBasedPartNo: 3, zeroBasedNoteIndex: 5);

        Assert.That(block.Start, Is.EqualTo("Temporary Tone Part 4"));
        Assert.That(block.Offset2, Is.EqualTo("Offset2/SuperNATURAL Drum Kit Partial 6"));
        Assert.That(ToneDomainNames.IsDrumKit("SN-D"), Is.True);
        Assert.That(ToneDomainNames.IsDrumKit("SN-S"), Is.False);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneRandomisationServiceTests
```

Expected: compile errors — `ToneRandomisationService`, `ToneDomainNames.DrumPartialFor` and
`ToneDomainNames.IsDrumKit` do not exist.

- [ ] **Step 3: Add the two helpers to `ToneDomainNames`**

Append inside the class in `Src/Models/Services/ToneDomainNames.cs`, after `IsKnownToneType`:

```csharp
    /// <summary>Whether this engine's tone is a kit of independently edited notes rather than one
    /// patch. Randomise treats the two differently: a kit is randomised one note at a time, because
    /// "every note in the kit at once" is 88 partials and an undo step nobody can use.</summary>
    public static bool IsDrumKit(string toneType) => toneType is "PCMD" or "SN-D";

    /// <summary>The single partial block holding one drum note. <paramref name="zeroBasedNoteIndex"/> is
    /// the drum editor's own note index (0..61 for SN-D, 0..87 for PCMD), which is one less than the
    /// partial number in the address.</summary>
    public static (string Start, string Offset, string Offset2) DrumPartialFor(string toneType,
        int zeroBasedPartNo, int zeroBasedNoteIndex)
    {
        var (offset, block) = toneType switch
        {
            "SN-D" => ("Offset/Temporary SuperNATURAL Drum Kit", "SuperNATURAL Drum Kit Partial"),
            "PCMD" => ("Offset/Temporary PCM Drum Kit", "PCM Drum Kit Partial"),
            _ => throw new ArgumentException($"'{toneType}' is not a drum kit.", nameof(toneType)),
        };

        return (Start(zeroBasedPartNo), offset, $"Offset2/{block} {zeroBasedNoteIndex + 1}");
    }
```

- [ ] **Step 4: Write the service**

Create `Src/Models/Services/ToneRandomisationService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Randomise as the instrument sees it: read each block, apply the new raw values, record what
/// changed, and send the block in one transmission.
///
/// <b>Why the read is not optional.</b> The new value of a numeric parameter is drawn around the value
/// that is there, so "there" has to be what the device holds and not what memory happens to carry -- a
/// block never read this session reads back as zeros, and randomising around zero is not randomising the
/// sound the user is listening to. It is also what makes the bulk write safe: WriteToIntegraAsync
/// flattens every context-valid parameter in the block, including the ones this randomise left alone.
///
/// <b>One undo step.</b> Every change is recorded inside a single <c>BeginGesture</c> scope, so a
/// randomise across several blocks still folds into one <c>EditStep</c> and one press of Undo takes all
/// of it back. Recording happens between reading the old displayed value and reading the new one, which
/// is the order <see cref="DomainEditRecorder"/> explains: record after the change and the old value is
/// gone.</summary>
public static class ToneRandomisationService
{
    /// <summary>Randomise every block in <paramref name="blocks"/> and answer how many parameters
    /// changed. <paramref name="lease"/> is the caller's conversation, held across the whole operation so
    /// nothing else writes into the middle of it.</summary>
    public static async Task<int> RandomiseAsync(Integra7Domain domain,
        IReadOnlyList<(string Start, string Offset, string Offset2)> blocks,
        RandomisationStrengths strengths, Random rng, IMidiLease? lease)
    {
        var changed = 0;

        // Opened around every block, not per block, so a multi-block randomise is one undo step.
        using var gesture = EditJournal.Default.BeginGesture();

        foreach (var (start, offset, offset2) in blocks)
        {
            var d = domain.GetDomain(start, offset, offset2);

            if (!await d.ReadFromIntegraAsync(lease))
                throw new SnapshotFormatException(
                    $"Could not randomise the tone: the device did not answer for block " +
                    $"(\"{start}\", \"{offset}\", \"{offset2}\").");

            // (false, false): neither reserved nor context-invalid. The opposite of what a snapshot
            // capture wants, and deliberately so -- a capture has to carry parameters its own
            // discriminators will make valid, whereas randomise never moves a discriminator, so a
            // parameter that is invalid now stays invalid.
            var parameters = d.GetRelevantParameters(false, false);
            var newValues = ToneRandomiser.NewValuesFor(parameters, strengths, rng);
            if (newValues.Count == 0) continue;

            foreach (var (path, raw) in newValues)
            {
                var before = d.LookupSingleParameterDisplayedValue(path);
                d.ModifySingleParameterRawValue(path, raw);
                var after = d.LookupSingleParameterDisplayedValue(path);

                EditJournal.Default.Record(new ParameterChange(
                    Start: start, Offset: offset, Offset2: offset2, Path: path,
                    OldValue: before, NewValue: after,
                    // Never true here: ToneRandomiser refuses a discriminator outright.
                    IsDiscriminator: false));
            }

            await d.WriteToIntegraAsync(lease);
            changed += newValues.Count;
        }

        Log.Information("Randomised {Count} parameters across {Blocks} block(s). The device does not " +
                        "acknowledge parameter writes, so this confirms the data was sent.",
            changed, blocks.Count);
        return changed;
    }
}
```

- [ ] **Step 5: Run the tests until they pass**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneRandomisationServiceTests
```

Expected: PASS. If `Records_one_undo_step_for_the_whole_operation` reports more than one step, the
gesture scope is being opened inside the block loop rather than around it.

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/ToneRandomisationService.cs Src/Models/Services/ToneDomainNames.cs Tests/TestToneRandomisationService.cs
git commit -m "feat: apply a randomise per block and record it as one undo step"
```

---

### Task 4: The tone clipboard

**Files:**
- Create: `Src/Models/Services/ToneClipboard.cs`
- Test: `Tests/TestToneClipboard.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestToneClipboard.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>One slot, this session only. Not persisted on purpose: a clipboard that survives a restart
/// is a surprise, and the library is where a tone goes to be kept.</summary>
public class ToneClipboardTests
{
    private static Integra7Snapshot Tone(string name) => new(
        Integra7Snapshot.CurrentFormatVersion, name,
        [
            new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                "Offset2/SuperNATURAL Synth Tone Common",
                [new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100)]),
        ],
        SnapshotKinds.Tone, "SN-S");

    [Test]
    public void Starts_empty()
    {
        var clipboard = new ToneClipboard();

        Assert.That(clipboard.HasContent, Is.False);
        Assert.That(clipboard.Content, Is.Null);
    }

    [Test]
    public void Holds_the_last_tone_put_into_it()
    {
        var clipboard = new ToneClipboard();

        clipboard.Put(Tone("first"));
        clipboard.Put(Tone("second"));

        Assert.That(clipboard.HasContent, Is.True);
        Assert.That(clipboard.Content!.Name, Is.EqualTo("second"));
    }

    [Test]
    public void Announces_a_change_so_paste_can_enable_itself()
    {
        var clipboard = new ToneClipboard();
        var announcements = 0;
        clipboard.Changed += () => announcements++;

        clipboard.Put(Tone("first"));

        Assert.That(announcements, Is.EqualTo(1));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneClipboardTests
```

Expected: compile error — `ToneClipboard` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/ToneClipboard.cs`:

```csharp
using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One tone, copied from a part and waiting to be pasted into another.
///
/// A whole <see cref="Integra7Snapshot"/> rather than a bag of values, because that is exactly what
/// <c>StudioSetSnapshotService.RestoreToneAsync</c> takes, and because the snapshot already names its own
/// engine -- which is what lets the paste be refused when the target part holds a different one.
///
/// Not persisted, and not static: an instance held by <c>MainWindowViewModel</c> for the life of the
/// window. A clipboard that outlived the process would be a surprise, and the library is where a tone
/// goes when it is meant to be kept.</summary>
public sealed class ToneClipboard
{
    public Integra7Snapshot? Content { get; private set; }

    public bool HasContent => Content is not null;

    /// <summary>Raised when the contents change, so the Paste button can enable itself. Fired from
    /// whichever thread called <see cref="Put"/> -- a UI listener marshals back itself, as it does for
    /// <c>EditJournal.Changed</c>.</summary>
    public event Action? Changed;

    public void Put(Integra7Snapshot snapshot)
    {
        Content = snapshot;
        Changed?.Invoke();
    }
}
```

- [ ] **Step 4: Run the tests until they pass**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter ToneClipboardTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/ToneClipboard.cs Tests/TestToneClipboard.cs
git commit -m "feat: hold one copied tone for the session"
```

---

### Task 5: Settings remember an init tone per engine

**Files:**
- Modify: `Src/Models/Services/LibrarySettings.cs`
- Modify: `Src/ViewModels/LibraryViewModel.cs:83` (the `Load` call site)
- Test: `Tests/TestLibrarySettings.cs`

- [ ] **Step 1: Write the failing test**

Append to the existing fixture in `Tests/TestLibrarySettings.cs` (inside the class, after the current
tests):

```csharp
    /// <summary>The init-tone marks are the second thing in this file, and they arrived after it
    /// shipped -- so the case that matters most is a settings file written by a build that had never
    /// heard of them.</summary>
    [Test]
    public void A_settings_file_from_before_init_tones_still_loads()
    {
        File.WriteAllText(_settingsPath, """{ "LibraryFolder": "C:\\Sounds" }""");

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.Folder, Is.EqualTo(@"C:\Sounds"));
        Assert.That(preferences.InitTones, Is.Empty);
    }

    [Test]
    public void A_mark_round_trips()
    {
        LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(@"C:\Sounds",
            new Dictionary<string, string> { ["SN-S"] = "My Init Pad.json" }));

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.InitTones["SN-S"], Is.EqualTo("My Init Pad.json"));
    }

    /// <summary>Changing the library folder goes through the one-argument Save, which predates the
    /// marks. If it wrote the whole file from its single argument it would silently forget them.</summary>
    [Test]
    public void Changing_the_folder_keeps_the_marks()
    {
        LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(@"C:\Sounds",
            new Dictionary<string, string> { ["PCMS"] = "Init.json" }));

        LibrarySettings.Save(_settingsPath, @"D:\Other");

        var preferences = LibrarySettings.LoadAll(_settingsPath);
        Assert.That(preferences.Folder, Is.EqualTo(@"D:\Other"));
        Assert.That(preferences.InitTones["PCMS"], Is.EqualTo("Init.json"));
    }

    [Test]
    public void An_unreadable_settings_file_yields_the_default_folder_and_no_marks()
    {
        File.WriteAllText(_settingsPath, "this is not JSON");

        var preferences = LibrarySettings.LoadAll(_settingsPath);

        Assert.That(preferences.Folder, Is.EqualTo(LibrarySettings.DefaultFolder));
        Assert.That(preferences.InitTones, Is.Empty);
    }
```

Add `using System.Collections.Generic;` to the file's usings if it is not already there.

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter LibrarySettingsTests
```

Expected: compile errors — `LibraryPreferences`, `LoadAll` and `SaveAll` do not exist.

- [ ] **Step 3: Extend `LibrarySettings`**

In `Src/Models/Services/LibrarySettings.cs`, replace the `Stored` record and add the new members. The
existing `Load` and `Save` stay, reimplemented in terms of the new pair, because every current caller
uses them:

```csharp
    /// <summary>The file's shape. Both properties are nullable so that a file which mentions neither --
    /// or which was written by a build that had never heard of one of them -- deserializes rather than
    /// failing. "Nothing said" is a state this file is allowed to be in.</summary>
    private sealed record Stored(string? LibraryFolder, Dictionary<string, string>? InitTones = null);

// NOTE: declare this record at namespace level, beside the LibrarySettings class and not inside it, so
// callers write `new LibraryPreferences(...)` rather than `new LibrarySettings.LibraryPreferences(...)`.

    /// <summary>Everything the settings file holds: where the library is, and which library file is the
    /// init tone for each engine, keyed by tone type ("SN-S", "PCMS", ...).
    ///
    /// The init-tone values are file names <em>relative to the library folder</em>, not absolute paths.
    /// The folder is itself a setting the user can change, and a relative name follows it; an absolute
    /// path would silently point outside the library the moment they did.</summary>
    public sealed record LibraryPreferences(string Folder, IReadOnlyDictionary<string, string> InitTones);

    /// <summary>Everything in <paramref name="settingsPath"/>, with the same
    /// answers-whatever-happens contract as <see cref="Load"/> -- see there for why the catch is this
    /// wide.</summary>
    public static LibraryPreferences LoadAll(string settingsPath)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(settingsPath), Options);
            var folder = stored?.LibraryFolder;
            return new LibraryPreferences(
                string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder,
                stored?.InitTones ?? []);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not read the library settings at {Path}; using the default folder",
                settingsPath);
            return new LibraryPreferences(DefaultFolder, new Dictionary<string, string>());
        }
    }

    /// <summary>Write the whole settings file, atomically -- see <see cref="Save"/> for why.</summary>
    public static void SaveAll(string settingsPath, LibraryPreferences preferences)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var stored = new Stored(preferences.Folder,
            new Dictionary<string, string>(preferences.InitTones));

        var tempPath = settingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(stored, Options));
            File.Move(tempPath, settingsPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception cleanup)
            {
                Log.Warning(cleanup, "Could not remove the temporary settings file {Path}", tempPath);
            }

            throw;
        }
    }
```

Then reduce the two existing methods to wrappers, keeping their doc comments where they are:

```csharp
    public static string Load(string settingsPath) => LoadAll(settingsPath).Folder;

    /// <summary>... (keep the existing comment, and add:)
    ///
    /// Reads the file before writing it so that the init-tone marks -- the other thing in it -- survive
    /// a folder change. Writing from this one argument alone would forget them.</summary>
    public static void Save(string settingsPath, string folder) =>
        SaveAll(settingsPath, LoadAll(settingsPath) with { Folder = folder });
```

Add `using System.Collections.Generic;` to the file's usings.

- [ ] **Step 4: Run the tests until they pass**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter LibrarySettingsTests
```

Expected: PASS, including the pre-existing cases.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/LibrarySettings.cs Tests/TestLibrarySettings.cs
git commit -m "feat: settings remember which library tone is an engine's init tone"
```

---

### Task 6: Resolving the init tone

**Files:**
- Create: `Src/Models/Services/InitToneResolution.cs`
- Test: `Tests/TestInitToneResolution.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestInitToneResolution.cs`:

```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>Which tone Init loads. Pure: existence is asked of the caller through two predicates, so
/// this is testable without touching the disk or Avalonia's asset loader.</summary>
public class InitToneResolutionTests
{
    private const string Folder = @"C:\Library";

    private static InitToneSource Resolve(IReadOnlyDictionary<string, string> marks,
        bool fileExists, bool assetExists) =>
        InitToneResolution.Resolve(marks, Folder, "SN-S", _ => fileExists, _ => assetExists);

    [Test]
    public void A_marked_library_entry_wins_over_the_bundled_asset()
    {
        var source = Resolve(new Dictionary<string, string> { ["SN-S"] = "My Init.json" },
            fileExists: true, assetExists: true);

        Assert.That(source.FilePath, Is.EqualTo(@"C:\Library\My Init.json"));
        Assert.That(source.AssetUri, Is.Null);
    }

    /// <summary>A mark can outlive the file it names -- the entry is deleted from the library, or the
    /// library folder is repointed somewhere that does not have it. Falling through to the bundled tone
    /// is better than refusing; the command still says the mark was stale.</summary>
    [Test]
    public void A_mark_whose_file_is_gone_falls_through_to_the_asset()
    {
        var source = Resolve(new Dictionary<string, string> { ["SN-S"] = "Deleted.json" },
            fileExists: false, assetExists: true);

        Assert.That(source.FilePath, Is.Null);
        Assert.That(source.AssetUri, Is.EqualTo("avares://Integra7AuralAlchemist/Assets/InitTones/SN-S.json"));
        Assert.That(source.MarkWasStale, Is.True);
    }

    [Test]
    public void No_mark_and_no_asset_resolves_to_nothing()
    {
        var source = Resolve(new Dictionary<string, string>(), fileExists: false, assetExists: false);

        Assert.That(source.FilePath, Is.Null);
        Assert.That(source.AssetUri, Is.Null);
        Assert.That(source.MarkWasStale, Is.False);
        Assert.That(source.HasTone, Is.False);
    }

    [Test]
    public void Uses_the_bundled_asset_when_nothing_is_marked()
    {
        var source = Resolve(new Dictionary<string, string>(), fileExists: false, assetExists: true);

        Assert.That(source.AssetUri, Is.EqualTo("avares://Integra7AuralAlchemist/Assets/InitTones/SN-S.json"));
        Assert.That(source.HasTone, Is.True);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter InitToneResolutionTests
```

Expected: compile errors — `InitToneSource` and `InitToneResolution` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Src/Models/Services/InitToneResolution.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Where Init should read its tone from: a file in the library, a bundled asset, or nowhere.
/// Exactly one of the two paths is set when there is a tone at all.</summary>
/// <param name="MarkWasStale">True when the user had marked a library entry for this engine and it is no
/// longer there. The bundled tone is still used, but the command says so -- silently loading a different
/// sound than the one that was marked is how a user stops trusting the feature.</param>
public sealed record InitToneSource(string? FilePath, string? AssetUri, bool MarkWasStale)
{
    public bool HasTone => FilePath is not null || AssetUri is not null;
}

/// <summary>Which tone Init loads for an engine.
///
/// Pure by construction: existence is asked of the caller through two predicates rather than of the file
/// system and Avalonia's asset loader directly, which is what lets every branch be tested. The view model
/// passes <c>File.Exists</c> and an asset-loader probe.</summary>
public static class InitToneResolution
{
    /// <summary>Where a build's own init tone for an engine lives. Named by tone type, so the five files
    /// are PCMS.json, PCMD.json, SN-S.json, SN-A.json and SN-D.json.</summary>
    public static string AssetUriFor(string toneType) =>
        $"avares://Integra7AuralAlchemist/Assets/InitTones/{toneType}.json";

    /// <param name="marks">The init-tone marks from the settings file: tone type to a file name relative
    /// to <paramref name="libraryFolder"/>.</param>
    public static InitToneSource Resolve(IReadOnlyDictionary<string, string> marks, string libraryFolder,
        string toneType, Func<string, bool> fileExists, Func<string, bool> assetExists)
    {
        var marked = marks.TryGetValue(toneType, out var name) && !string.IsNullOrWhiteSpace(name)
            ? Path.Combine(libraryFolder, name)
            : null;

        if (marked is not null && fileExists(marked))
            return new InitToneSource(marked, null, MarkWasStale: false);

        var asset = AssetUriFor(toneType);
        return new InitToneSource(null, assetExists(asset) ? asset : null,
            MarkWasStale: marked is not null);
    }
}
```

- [ ] **Step 4: Run the tests until they pass**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter InitToneResolutionTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/InitToneResolution.cs Tests/TestInitToneResolution.cs
git commit -m "feat: resolve an engine's init tone from the library or a bundled asset"
```

---

### Task 7: Marking a library entry as an init tone

**Files:**
- Modify: `Src/ViewModels/LibraryViewModel.cs`
- Modify: `Src/Views/LibraryView.axaml`

This task has no unit test: it is view-model wiring over `LibrarySettings`, which Task 5 tested, and the
library view models are not currently under test. Verify it by running the application.

- [ ] **Step 1: Load and hold the marks in `LibraryViewModel`**

At `Src/ViewModels/LibraryViewModel.cs:83`, the constructor currently reads:

```csharp
        _folder = LibrarySettings.Load(settingsPath);
```

Replace with:

```csharp
        var preferences = LibrarySettings.LoadAll(settingsPath);
        _folder = preferences.Folder;
        _initTones = new Dictionary<string, string>(preferences.InitTones);
```

Add the field beside the other private state:

```csharp
    /// <summary>Which library file is the init tone for each engine, keyed by tone type. Held here
    /// rather than re-read on every use because it is also what the "Use as the init tone" button
    /// edits.</summary>
    private Dictionary<string, string> _initTones = [];
```

- [ ] **Step 2: Expose the mark and the command**

Add to `LibraryViewModel`, next to `SelectedIsTone`:

```csharp
    /// <summary>Whether the selected entry can be made an init tone: a tone (not a Studio Set) whose
    /// engine this build recognises, since the mark is stored per engine.</summary>
    public bool CanMarkAsInitTone =>
        SelectedIsTone && SelectedEntry?.Entry.Head.ToneType is { } t &&
        ToneDomainNames.IsKnownToneType(t);

    /// <summary>What the details panel says about the selected entry's init-tone status -- empty when
    /// there is nothing to say, which is most of the time.</summary>
    public string InitToneNote =>
        SelectedEntry?.Entry.Head.ToneType is { } toneType &&
        _initTones.TryGetValue(toneType, out var file) &&
        string.Equals(file, Path.GetFileName(SelectedEntry.FilePath), StringComparison.OrdinalIgnoreCase)
            ? $"Init Tone starts from this when the part holds a {toneType} tone."
            : "";

    /// <summary>Make the selected entry the tone Init starts from for its engine. Stored as a file name
    /// relative to the library folder, so it follows the library if the folder moves.</summary>
    public void MarkAsInitTone()
    {
        if (SelectedEntry?.Entry.Head.ToneType is not { } toneType) return;

        _initTones[toneType] = Path.GetFileName(SelectedEntry.FilePath);
        try
        {
            LibrarySettings.SaveAll(_settingsPath, new LibraryPreferences(Folder, _initTones));
            _report($"Init Tone will start from {SelectedEntry.Name} for {toneType} tones.", false);
        }
        catch (Exception e)
        {
            // The in-memory map is left as it is: the user's intent is recorded for this session even
            // when the file could not be written, and the message says the setting will not survive.
            Log.Warning(e, "Could not save the init-tone mark");
            _report($"Could not remember that: {e.Message} The mark applies until the application closes.",
                true);
        }

        this.RaisePropertyChanged(nameof(InitToneNote));
    }
```

Add `using System.IO;` and `using Serilog;` if they are not already present, and check the exact name of
the status-reporting delegate field (`_report` above) with
`grep -n "Action<string, bool>\|_report" Src/ViewModels/LibraryViewModel.cs` — use whatever the file
really calls it.

Find where `SelectedEntry` changes are already observed (search for `nameof(SelectedIsTone)` or the
`WhenAnyValue(x => x.SelectedEntry)` subscription) and raise `CanMarkAsInitTone` and `InitToneNote`
alongside the properties already raised there.

- [ ] **Step 3: Add the button to the details panel**

In `Src/Views/LibraryView.axaml`, immediately after the existing "Load into the instrument" button
(around line 221), add:

```xml
                            <Button Content="Use as the init tone"
                                    Command="{Binding MarkAsInitTone}"
                                    IsEnabled="{Binding CanMarkAsInitTone}"
                                    ToolTip.Tip="Make this the tone the Init Tone button starts from, for parts holding this kind of tone. Replaces the bundled starting point."
                                    Padding="8,2" />
                            <TextBlock Text="{Binding InitToneNote}"
                                       Foreground="{StaticResource SnMutedTextBrush}"
                                       TextWrapping="Wrap" />
```

Match the surrounding indentation and container: check whether the neighbouring buttons sit in a
`StackPanel` with `Spacing` before adding margins of your own.

- [ ] **Step 4: Build and check the binding compiles**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: build succeeds. An `AVLN2000` error means a compiled binding names something the view model
does not have — the XAML compiler runs as part of a full build, so this is where a typo surfaces.

- [ ] **Step 5: Run the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj
```

Expected: everything that passed before still passes.

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/LibraryViewModel.cs Src/Views/LibraryView.axaml
git commit -m "feat: mark a library tone as an engine's init tone"
```

---

### Task 8: The confirm dialog

**Files:**
- Create: `Src/ViewModels/ConfirmViewModel.cs`
- Create: `Src/Views/ConfirmDialog.axaml`, `Src/Views/ConfirmDialog.axaml.cs`

- [ ] **Step 1: Write the view model**

Create `Src/ViewModels/ConfirmViewModel.cs`:

```csharp
using System.Reactive;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>A question with two answers. The application's first yes/no dialog, built as one reusable
/// window rather than one per command: Init and Paste both need exactly this, and a third caller will
/// too.
///
/// Both commands answer a bool and both close the window with it, the shape
/// <c>SaveToLibraryViewModel</c> established -- which is what lets the caller read the result without a
/// second flag to keep in step.</summary>
public sealed class ConfirmViewModel : ViewModelBase
{
    public ConfirmViewModel(string message, string confirmLabel = "Continue")
    {
        Message = message;
        ConfirmLabel = confirmLabel;

        // Parameterless, for the reason SaveToLibraryViewModel gives: a ReactiveCommand<Unit, T> invoked
        // from a button with no CommandParameter is handed null, and casting null to Unit throws.
        ConfirmCommand = ReactiveCommand.Create(() => true);
        CancelCommand = ReactiveCommand.Create(() => false);
    }

    public string Message { get; }

    /// <summary>What the affirmative button says. "Continue" for a replacement the user asked for;
    /// a caller with something more specific to say passes it.</summary>
    public string ConfirmLabel { get; }

    public ReactiveCommand<Unit, bool> ConfirmCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
}
```

- [ ] **Step 2: Write the window**

Create `Src/Views/ConfirmDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
        mc:Ignorable="d" d:DesignWidth="460" d:DesignHeight="160"
        x:Class="Integra7AuralAlchemist.Views.ConfirmDialog"
        x:DataType="vm:ConfirmViewModel"
        Title="Are you sure?"
        Width="460"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner">

    <StackPanel Orientation="Vertical" Margin="16" Spacing="16">
        <TextBlock Text="{Binding Message}" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Right">
            <Button Content="{Binding ConfirmLabel}" Command="{Binding ConfirmCommand}" Padding="14,3" />
            <Button Content="Cancel" Command="{Binding CancelCommand}" Padding="14,3" IsDefault="True" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `Src/Views/ConfirmDialog.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Both commands answer a bool and both close the window with it, exactly as
/// <see cref="SaveToLibraryDialog"/> does with its metadata. Cancel is the default button because
/// everything that asks this question is about to overwrite something.</summary>
public partial class ConfirmDialog : ReactiveWindow<ConfirmViewModel>
{
    public ConfirmDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.WhenActivated(action => action(ViewModel!.ConfirmCommand.Subscribe(Close)));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(Close)));
    }
}
```

- [ ] **Step 3: Build**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: build succeeds. If `Close` cannot bind to `Subscribe`, check how `SaveToLibraryDialog` does it
— the overload being used there is `Close(object?)`.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/ConfirmViewModel.cs Src/Views/ConfirmDialog.axaml Src/Views/ConfirmDialog.axaml.cs
git commit -m "feat: add a reusable yes/no dialog"
```

---

### Task 9: The randomise dialog

**Files:**
- Create: `Src/ViewModels/RandomiseToneViewModel.cs`
- Create: `Src/Views/RandomiseToneDialog.axaml`, `Src/Views/RandomiseToneDialog.axaml.cs`

- [ ] **Step 1: Write the view model**

Create `Src/ViewModels/RandomiseToneViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One category's row in the randomise dialog: whether it is included, and how far its
/// parameters may move.
///
/// <b>A slider, not a RotaryKnob.</b> The knob is the application's control for editing a sound, and it
/// earns that everywhere a parameter is edited. This is a settings row in a modal dialog -- closer to the
/// library's filters than to a filter cutoff -- and a labelled percentage slider says what it does
/// without being turned.</summary>
public sealed partial class RandomiseCategoryViewModel : ViewModelBase
{
    public RandomiseCategoryViewModel(ToneCategory category, string label, bool present)
    {
        Category = category;
        Label = label;
        IsPresent = present;
    }

    public ToneCategory Category { get; }

    public string Label { get; }

    /// <summary>Whether the loaded engine has any parameter in this category. A category it does not have
    /// is shown disabled rather than hidden, so the dialog keeps one shape.</summary>
    public bool IsPresent { get; }

    [Reactive] private bool _included;

    /// <summary>0..100, as the slider shows it. Divided by 100 on the way out -- the service works in
    /// 0..1, and a percentage is what a user reads.</summary>
    [Reactive] private double _strengthPercent = 25;
}

/// <summary>What a randomise should touch and how hard.
///
/// Held by <c>MainWindowViewModel</c> for the life of the window rather than built per press, so a second
/// randomise starts from the settings the first one used -- the point of the feature is trying again.
/// Not persisted across sessions; that is a later addition if it is ever missed.</summary>
public sealed partial class RandomiseToneViewModel : ViewModelBase
{
    private static readonly (ToneCategory Category, string Label)[] Rows =
    [
        (ToneCategory.PitchAndOscillator, "Pitch and oscillator"),
        (ToneCategory.WaveChoice, "Wave choice"),
        (ToneCategory.Filter, "Filter"),
        (ToneCategory.Amplifier, "Amplifier"),
        (ToneCategory.LfoAndModulation, "LFO and modulation"),
        (ToneCategory.Effects, "Effects"),
        (ToneCategory.InstrumentCharacter, "Instrument character"),
    ];

    public RandomiseToneViewModel()
    {
        foreach (var (category, label) in Rows)
            Categories.Add(new RandomiseCategoryViewModel(category, label, present: true));

        RandomiseCommand = ReactiveCommand.Create(() => true);
        CancelCommand = ReactiveCommand.Create(() => false);
    }

    public ObservableCollection<RandomiseCategoryViewModel> Categories { get; } = [];

    /// <summary>What this press will act on, e.g. "Randomising the tone in part 4" or "Randomising note
    /// 38 (D2) of the kit in part 10". Set by the caller before the dialog is shown, because only it
    /// knows which part is selected and what is in it.</summary>
    [Reactive] private string _target = "";

    public ReactiveCommand<Unit, bool> RandomiseCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }

    /// <summary>Point the rows at an engine: categories it does not have are disabled and unticked, so a
    /// tick left over from a different engine cannot silently do nothing.</summary>
    public void PrepareFor(string toneType, string target)
    {
        Target = target;
        var present = ToneParameterCategories.PresentIn(toneType);

        Categories.Clear();
        foreach (var (category, label) in Rows)
        {
            var row = new RandomiseCategoryViewModel(category, label, present.Contains(category));
            if (_lastIncluded.Contains(category) && row.IsPresent) row.Included = true;
            if (_lastStrengths.TryGetValue(category, out var strength)) row.StrengthPercent = strength;
            Categories.Add(row);
        }
    }

    /// <summary>What the user ticked, as the service wants it. Also remembers the settings for the next
    /// press.</summary>
    public RandomisationStrengths Strengths()
    {
        _lastIncluded = [.. Categories.Where(c => c.Included).Select(c => c.Category)];
        _lastStrengths = Categories.ToDictionary(c => c.Category, c => c.StrengthPercent);

        return new RandomisationStrengths(Categories
            .Where(c => c.Included && c.IsPresent)
            .ToDictionary(c => c.Category, c => c.StrengthPercent / 100.0));
    }

    private HashSet<ToneCategory> _lastIncluded = [];
    private Dictionary<ToneCategory, double> _lastStrengths = [];
}
```

- [ ] **Step 2: Write the window**

Create `Src/Views/RandomiseToneDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
        mc:Ignorable="d" d:DesignWidth="560" d:DesignHeight="420"
        x:Class="Integra7AuralAlchemist.Views.RandomiseToneDialog"
        x:DataType="vm:RandomiseToneViewModel"
        Title="Randomise"
        Width="560"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner">

    <!-- Strength is a deviation from the value that is there, not a fresh draw: at 10 % this is still
         recognisably the sound you started from, at 100 % it is barely related. Enumerated parameters —
         filter mode, LFO shape, switches — have no distance between their values, so for them the
         strength is the chance of being re-picked at all. -->

    <StackPanel Orientation="Vertical" Margin="16" Spacing="10">
        <TextBlock Text="{Binding Target}" TextWrapping="Wrap" FontWeight="Bold" />
        <TextBlock Text="Ticked groups move away from their current values by up to the amount set here. Everything else is left exactly as it is."
                   TextWrapping="Wrap"
                   Foreground="{StaticResource SnMutedTextBrush}" />

        <ItemsControl ItemsSource="{Binding Categories}">
            <ItemsControl.ItemTemplate>
                <DataTemplate DataType="vm:RandomiseCategoryViewModel">
                    <Grid ColumnDefinitions="200,*,50" Margin="0,3">
                        <CheckBox Grid.Column="0"
                                  Content="{Binding Label}"
                                  IsChecked="{Binding Included, Mode=TwoWay}"
                                  IsEnabled="{Binding IsPresent}" />
                        <Slider Grid.Column="1"
                                Minimum="0" Maximum="100"
                                TickFrequency="5" IsSnapToTickEnabled="True"
                                Value="{Binding StrengthPercent, Mode=TwoWay}"
                                IsEnabled="{Binding Included}" />
                        <TextBlock Grid.Column="2"
                                   Text="{Binding StrengthPercent, StringFormat='{}{0:F0} %'}"
                                   VerticalAlignment="Center"
                                   HorizontalAlignment="Right" />
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Randomise" Command="{Binding RandomiseCommand}" Padding="14,3" />
            <Button Content="Cancel" Command="{Binding CancelCommand}" Padding="14,3" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `Src/Views/RandomiseToneDialog.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Integra7AuralAlchemist.Views;

/// <summary>Answers true for Randomise and false for Cancel, the shape <see cref="ConfirmDialog"/>
/// uses. The settings themselves stay on the view model, which the caller keeps -- so a second press
/// starts where the first left off.</summary>
public partial class RandomiseToneDialog : ReactiveWindow<RandomiseToneViewModel>
{
    public RandomiseToneDialog()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.WhenActivated(action => action(ViewModel!.RandomiseCommand.Subscribe(Close)));
        this.WhenActivated(action => action(ViewModel!.CancelCommand.Subscribe(Close)));
    }
}
```

- [ ] **Step 3: Build**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
```

Expected: build succeeds. `AVLN2000` means a compiled binding names a member the view model does not
have.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/RandomiseToneViewModel.cs Src/Views/RandomiseToneDialog.axaml Src/Views/RandomiseToneDialog.axaml.cs
git commit -m "feat: add the randomise dialog"
```

---

### Task 10: The four commands and their buttons

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/Views/MainWindow.axaml`
- Modify: `Src/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Extract the snapshot half of the existing tone restore**

`RestoreToneFromFileAsync` (around `Src/ViewModels/MainWindowViewModel.cs:1018`) reads a file and
restores it. Init and Paste need the same thing from a snapshot that did not come from a file. Split it
so all three share one path — read the current method first, keep its comments, and change only the
seam:

```csharp
    private async Task RestoreToneFromFileAsync(IIntegra7Api api, Integra7Domain communicator,
        SelectedTone selected, string path)
    {
        Integra7Snapshot snapshot;
        try
        {
            snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));
        }
        catch (Exception e)
        {
            UserActionLog.Failed("load tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not load the tone: {e.Message}";
            return;
        }

        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, Path.GetFileName(path));
    }

    /// <summary>Write <paramref name="snapshot"/> into <paramref name="selected"/>'s part and re-read that
    /// part afterwards. <paramref name="source"/> is what the status line calls it -- a file name, "the
    /// clipboard", "the init tone".
    ///
    /// <b>Every whole-tone replacement goes through here</b>: Load Tone, the library's own load, Init and
    /// Paste. The engine guard is deliberately not in this method -- <paramref name="selected"/> carries the
    /// engine the part genuinely holds, RestoreToneAsync compares the snapshot's against it and refuses, and
    /// a second caller resolving the engine its own way is exactly how PCM data reaches a SuperNATURAL
    /// part's addresses.</summary>
    private async Task RestoreToneSnapshotAsync(IIntegra7Api api, Integra7Domain communicator,
        SelectedTone selected, Integra7Snapshot snapshot, string source)
    {
        // ... the body of the current RestoreToneFromFileAsync from the Kind check onwards, with
        // Path.GetFileName(path) replaced by source and the FromJson line removed.
    }
```

Move the existing `Kind` check, the lease, the `RestoreToneAsync` call, both catch blocks, the
`EditJournal.Default.Clear()` and the `ResyncPartAsync` into `RestoreToneSnapshotAsync` unchanged.

- [ ] **Step 2: Add the state the new commands need**

Beside the existing interactions (around `Src/ViewModels/MainWindowViewModel.cs:92-114`):

```csharp
    /// <summary>Ask a yes/no question. Init and Paste both replace a whole tone and clear the edit
    /// history, and neither is undoable, so both ask first.</summary>
    public Interaction<ConfirmViewModel, bool> ShowConfirmDialog { get; }

    /// <summary>Ask what a randomise should touch. The view model is kept rather than rebuilt, so a
    /// second press starts from the settings the first used.</summary>
    public Interaction<RandomiseToneViewModel, bool> ShowRandomiseToneDialog { get; }
```

And in the constructor, beside the other `new Interaction<...>()` lines (around line 1812):

```csharp
        ShowConfirmDialog = new Interaction<ConfirmViewModel, bool>();
        ShowRandomiseToneDialog = new Interaction<RandomiseToneViewModel, bool>();
```

Add the fields:

```csharp
    /// <summary>The tone Copy put there, waiting for Paste. One slot, this window's lifetime -- see
    /// ToneClipboard.</summary>
    private readonly ToneClipboard _toneClipboard = new();

    /// <summary>Kept, not rebuilt per press, so the categories and strengths a user set last time are
    /// still there the next time.</summary>
    private readonly RandomiseToneViewModel _randomiseVm = new();

    /// <summary>One generator for the session. A fresh Random per press seeded from the clock can repeat
    /// itself when two presses land in the same tick, which reads as "the button did nothing".</summary>
    private readonly Random _randomiseRng = new();

    [Reactive] private bool _canPasteTone;
```

Wire the clipboard's event in the constructor, after the interactions:

```csharp
        // Fired from whichever thread called Put -- see ToneClipboard.Changed -- and CanPasteTone is
        // bound to a button, so it is set on the UI thread.
        _toneClipboard.Changed += () =>
            RxApp.MainThreadScheduler.Schedule(() => CanPasteTone = _toneClipboard.HasContent);
```

Add `using System.Reactive.Concurrency;` for `Schedule`.

- [ ] **Step 3: Write Copy and Paste**

Add to `MainWindowViewModel`, after `LoadToneAsync`:

```csharp
    /// <summary>Read the tone in the selected part into the clipboard, so it can be pasted into another
    /// part. Nothing is written to the instrument and nothing reaches the disk.</summary>
    [ReactiveCommand]
    public async Task CopyToneAsync()
    {
        UserActionLog.Action("button: Copy Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        var selected = await ResolveSelectedToneAsync("copy");
        if (selected is null) return; // ResolveSelectedToneAsync has already said why

        try
        {
            SignalStartSync();
            SyncInfo = $"Reading tone from part {selected.ZeroBasedPartNo + 1}";
            // One conversation for the whole capture, so nothing else writes into the middle of it and
            // produces a tone that never existed -- the reasoning SaveToneAsync records.
            await using (var lease = await api.BeginConversationAsync("copy tone"))
            {
                _toneClipboard.Put(await StudioSetSnapshotService.CaptureToneAsync(communicator,
                    selected.ZeroBasedPartNo, selected.ToneType, selected.ToneName, lease));
            }

            SnapshotFailed = false;
            SnapshotStatus = $"Copied {selected.ToneName} from part {selected.ZeroBasedPartNo + 1}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("copy tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not copy the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Write the copied tone into the selected part. Refused while comparing and confirmed
    /// first, for the reasons <see cref="LoadToneAsync"/> and <see cref="InitToneAsync"/> give.</summary>
    [ReactiveCommand]
    public async Task PasteToneAsync()
    {
        UserActionLog.Action("button: Paste Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("paste a tone")) return;

        if (_toneClipboard.Content is not { } snapshot)
        {
            // The button is disabled without content, but a command stays reachable, and silently doing
            // nothing is worse than saying why.
            SnapshotFailed = true;
            SnapshotStatus = "Nothing to paste: copy a tone first.";
            return;
        }

        var selected = await ResolveSelectedToneAsync("paste");
        if (selected is null) return;

        if (!await ShowConfirmDialog.Handle(new ConfirmViewModel(
                $"Replacing the tone in part {selected.ZeroBasedPartNo + 1} with {snapshot.Name} cannot be " +
                "undone, and it clears the edit history. Continue?", "Paste"))) return;

        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, "the clipboard");
    }
```

- [ ] **Step 4: Write Init**

```csharp
    /// <summary>Replace the tone in the selected part with the init tone for its engine: the library
    /// entry the user marked, or the tone bundled with this build.
    ///
    /// A real tone snapshot rather than a table of default values, so it is complete by construction --
    /// every block, every parameter -- and so it goes through exactly the restore path (and validation)
    /// that Load Tone does.</summary>
    [ReactiveCommand]
    public async Task InitToneAsync()
    {
        UserActionLog.Action("button: Init Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("initialise a tone")) return;

        var selected = await ResolveSelectedToneAsync("initialise");
        if (selected is null) return;

        var source = InitToneResolution.Resolve(LibrarySettings.LoadAll(LibrarySettings.SettingsPath).InitTones,
            LibraryVm.Folder, selected.ToneType, File.Exists,
            uri => AssetLoader.Exists(new Uri(uri)));

        if (!source.HasTone)
        {
            SnapshotFailed = true;
            // Says how to fix it, not only that it is broken: there is no init tone for this engine in
            // this build, and the user has a way to supply one.
            SnapshotStatus = (source.MarkWasStale
                                 ? $"The tone marked as the init tone for {selected.ToneType} is no longer in the library. "
                                 : $"No init tone is set for {selected.ToneType}. ") +
                             "Add a tone to the library, select it in the Library tab and press " +
                             "\"Use as the init tone\".";
            return;
        }

        if (!await ShowConfirmDialog.Handle(new ConfirmViewModel(
                $"Replacing the tone in part {selected.ZeroBasedPartNo + 1} with the init tone cannot be " +
                "undone, and it clears the edit history. Continue?", "Initialise"))) return;

        Integra7Snapshot snapshot;
        try
        {
            var json = source.FilePath is { } file
                ? await File.ReadAllTextAsync(file)
                : await new StreamReader(AssetLoader.Open(new Uri(source.AssetUri!))).ReadToEndAsync();
            snapshot = Integra7Snapshot.FromJson(json);
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read the init tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not read the init tone for {selected.ToneType}: {e.Message}";
            return;
        }

        // A stale mark still loads the bundled tone, but the user asked for a different one and has to
        // know they did not get it.
        if (source.MarkWasStale)
            SnapshotStatus = $"The tone marked as the init tone for {selected.ToneType} is no longer in " +
                             "the library; using the one bundled with the application.";

        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, "the init tone");
    }
```

Add `using Avalonia.Platform;` for `AssetLoader`.

- [ ] **Step 5: Write Randomise**

```csharp
    /// <summary>Vary the tone in the selected part, under the categories and strengths the dialog
    /// collects. Unlike Init and Paste this is an edit like any other: it records one undo step, so a
    /// result the user does not like is one press away from gone.
    ///
    /// A drum kit is randomised one note at a time -- the note selected in its editor. Every note at once
    /// would be 88 partials and an undo step nobody could use.</summary>
    [ReactiveCommand]
    public async Task RandomiseToneAsync()
    {
        UserActionLog.Action("button: Randomise Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("randomise a tone")) return;

        var selected = await ResolveSelectedToneAsync("randomise");
        if (selected is null) return;

        IReadOnlyList<(string Start, string Offset, string Offset2)> blocks;
        string target;
        if (ToneDomainNames.IsDrumKit(selected.ToneType))
        {
            // Written out rather than nested in a conditional: the two editors are different types, so
            // this is two lookups that happen to answer the same shape, not one expression.
            var part = PartViewModels![_currentPartSelection];
            (int Index, int Note)? note;
            if (selected.ToneType == "SN-D")
                note = part.SNDrumKitEditor?.SelectedNote is { } sn ? (sn.Index, sn.Note) : null;
            else
                note = part.PcmDrumKitEditor?.SelectedNote is { } pcm ? (pcm.Index, pcm.Note) : null;

            if (note is not { } chosen)
            {
                SnapshotFailed = true;
                SnapshotStatus = "Cannot randomise a drum kit: open the part's drum tab and select a note first.";
                return;
            }

            blocks = [ToneDomainNames.DrumPartialFor(selected.ToneType, selected.ZeroBasedPartNo, chosen.Index)];
            target = $"Randomising note {chosen.Note} ({MidiNote.Name(chosen.Note)}) of the kit in " +
                     $"part {selected.ZeroBasedPartNo + 1}";
        }
        else
        {
            blocks = ToneDomainNames.For(selected.ToneType, selected.ZeroBasedPartNo);
            target = $"Randomising the tone in part {selected.ZeroBasedPartNo + 1}";
        }

        _randomiseVm.PrepareFor(selected.ToneType, target);
        if (!await ShowRandomiseToneDialog.Handle(_randomiseVm)) return;

        var strengths = _randomiseVm.Strengths();
        if (!strengths.Any)
        {
            SnapshotFailed = true;
            SnapshotStatus = "Nothing was ticked, so nothing was randomised.";
            return;
        }

        var randomised = false;
        try
        {
            SignalStartSync();
            SyncInfo = $"Randomising part {selected.ZeroBasedPartNo + 1}";
            // One conversation for the whole operation: it reads each block and writes it back, and
            // anything else writing in between would randomise around values that were never heard.
            await using (var lease = await api.BeginConversationAsync("randomise tone"))
            {
                var changed = await ToneRandomisationService.RandomiseAsync(communicator, blocks,
                    strengths, _randomiseRng, lease);
                randomised = true;
                SnapshotFailed = false;
                // "Sent", not "applied": the device acknowledges no parameter write. Undo is named
                // because it is the whole reason this is one step.
                SnapshotStatus = $"Sent {changed} randomised parameters to part " +
                                 $"{selected.ZeroBasedPartNo + 1}. Undo takes all of it back.";
            }
        }
        catch (SnapshotFormatException e)
        {
            UserActionLog.Failed("randomise tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e.Message;
        }
        catch (Exception e)
        {
            UserActionLog.Failed("randomise tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not randomise the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }

        if (randomised)
            // Only this part changed. Outside the lease above, since the resync takes its own, and not
            // at all on failure, because the screen still matches the device.
            await ResyncPartAsync((byte)selected.ZeroBasedPartNo);
    }
```

Check the two drum-editor property names before relying on them —
`grep -n "DrumKitEditor" Src/ViewModels/PartViewModel.cs` shows the generated names (the fields are
`_pcmDrumKitEditor` and `_sNDrumKitEditor`, so the properties are `PcmDrumKitEditor` and
`SNDrumKitEditor`). Check `MidiNote` for the note-name helper's real signature with
`grep -n "public static" Src/Models/Services/MidiNote.cs`; if there is no name-from-number helper, drop
that part of the string and use the note number alone.

- [ ] **Step 6: Add the toolbar buttons**

In `Src/Views/MainWindow.axaml`, after the `Load Tone…` button (which ends around line 245), add four
buttons following the same shape. `Connected`, `!IsSyncing` and `CurrentPartIsNotCommonPart` are the
same three conditions the tone buttons above already use:

```xml
                            <Button Command="{Binding InitToneAsync}"
                                    ToolTip.Tip="Replace the tone in the selected part with a neutral starting point — the library tone marked as this engine's init tone, or the one bundled with the application. This cannot be undone.">
                                <Button.IsEnabled>
                                    <MultiBinding Converter="{x:Static BoolConverters.And}">
                                        <MultiBinding.Bindings>
                                            <Binding Path="Connected" />
                                            <Binding Path="!IsSyncing" />
                                            <Binding Path="CurrentPartIsNotCommonPart" />
                                        </MultiBinding.Bindings>
                                    </MultiBinding>
                                </Button.IsEnabled>
                                Init Tone
                            </Button>
                            <Button Command="{Binding CopyToneAsync}"
                                    ToolTip.Tip="Read the tone in the selected part and hold it, so it can be pasted into another part of the same kind.">
                                <Button.IsEnabled>
                                    <MultiBinding Converter="{x:Static BoolConverters.And}">
                                        <MultiBinding.Bindings>
                                            <Binding Path="Connected" />
                                            <Binding Path="!IsSyncing" />
                                            <Binding Path="CurrentPartIsNotCommonPart" />
                                        </MultiBinding.Bindings>
                                    </MultiBinding>
                                </Button.IsEnabled>
                                Copy Tone
                            </Button>
                            <Button Command="{Binding PasteToneAsync}"
                                    ToolTip.Tip="Write the copied tone into the selected part. The part must already hold a tone of the same kind. This cannot be undone.">
                                <Button.IsEnabled>
                                    <MultiBinding Converter="{x:Static BoolConverters.And}">
                                        <MultiBinding.Bindings>
                                            <Binding Path="Connected" />
                                            <Binding Path="!IsSyncing" />
                                            <Binding Path="CurrentPartIsNotCommonPart" />
                                            <Binding Path="CanPasteTone" />
                                        </MultiBinding.Bindings>
                                    </MultiBinding>
                                </Button.IsEnabled>
                                Paste Tone
                            </Button>
                            <Button Command="{Binding RandomiseToneAsync}"
                                    ToolTip.Tip="Vary the tone in the selected part. You choose which groups of parameters move and how far from their current values; everything else is left alone. One press of Undo takes it all back.">
                                <Button.IsEnabled>
                                    <MultiBinding Converter="{x:Static BoolConverters.And}">
                                        <MultiBinding.Bindings>
                                            <Binding Path="Connected" />
                                            <Binding Path="!IsSyncing" />
                                            <Binding Path="CurrentPartIsNotCommonPart" />
                                        </MultiBinding.Bindings>
                                    </MultiBinding>
                                </Button.IsEnabled>
                                Randomise…
                            </Button>
```

- [ ] **Step 7: Register the two dialog handlers**

In `Src/Views/MainWindow.axaml.cs`, inside `RegisterDialogHandler`:

```csharp
            action(ViewModel!.ShowConfirmDialog.RegisterHandler(DoShowConfirmDialogAsync));
            action(ViewModel!.ShowRandomiseToneDialog.RegisterHandler(DoShowRandomiseToneDialogAsync));
```

And the two handlers, beside `DoShowSaveToLibraryDialogAsync`:

```csharp
    /// <summary>A yes/no question. The window closes with the answer, and a window closed any other way
    /// -- the title bar's X, Escape -- answers false, which is the safe side for every caller: all of
    /// them are about to replace something.</summary>
    private async Task DoShowConfirmDialogAsync(IInteractionContext<ConfirmViewModel, bool> interaction)
    {
        var dialog = new ConfirmDialog { DataContext = interaction.Input };
        interaction.SetOutput(await dialog.ShowDialog<bool>(this));
    }

    private async Task DoShowRandomiseToneDialogAsync(
        IInteractionContext<RandomiseToneViewModel, bool> interaction)
    {
        var dialog = new RandomiseToneDialog { DataContext = interaction.Input };
        interaction.SetOutput(await dialog.ShowDialog<bool>(this));
    }
```

- [ ] **Step 8: Build and run the whole suite**

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj
```

Expected: build succeeds and every test passes. `ShowDialog<bool>` on a window closed with no result
returns `default`, i.e. false — which is why the confirm dialog is safe when dismissed.

- [ ] **Step 9: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/Views/MainWindow.axaml Src/Views/MainWindow.axaml.cs
git commit -m "feat: init, copy, paste and randomise the tone in the selected part"
```

---

### Task 11: The five bundled init tones (needs the instrument)

**Files:**
- Create: `Src/Assets/InitTones/PCMS.json`, `PCMD.json`, `SN-S.json`, `SN-A.json`, `SN-D.json`

This one cannot be done without hardware and is the user's to do. Everything else ships without it: Init
reports that no init tone is set for the engine and says how to set one.

- [ ] **Step 1: Confirm the assets are picked up**

`Src/Integra7AuralAlchemist.csproj:14` already globs `<AvaloniaResource Include="Assets\**"/>`, so a
file dropped into `Src/Assets/InitTones/` is embedded with no csproj change. Verify after adding the
first one:

```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln
```

- [ ] **Step 2: Capture one tone per engine (user, at the instrument)**

For each of the five engines: select a part, load a preset of that engine, build a neutral starting
point on it, then use **Export Tone…** and save the file as `Src/Assets/InitTones/<toneType>.json` where
`<toneType>` is one of `PCMS`, `PCMD`, `SN-S`, `SN-A`, `SN-D`.

- [ ] **Step 3: Check each one loads**

With each file in place, select a part holding that engine and press **Init Tone**. Expected: the status
line reads "Sent the tone from the init tone to part N", and the part's editors show the initialised
values after the resync.

- [ ] **Step 4: Commit**

```bash
git add Src/Assets/InitTones
git commit -m "feat: bundle an init tone for each engine"
```

---

## Hardware verification (user)

Everything above is verified against a fake device. These need the instrument:

- [ ] **Copy and paste between parts of the same engine.** Copy from part 1, select part 2 holding the
  same engine, Paste. The sound moves; the confirmation appears first.
- [ ] **Paste across engines is refused** with a message naming both engines and what to select first.
- [ ] **Randomise a SuperNATURAL Synth tone** with only Filter ticked at 20 %. The filter moves, the
  oscillator does not, and the sound is recognisably the one you started from.
- [ ] **Undo after a randomise** puts every value back in one press.
- [ ] **Randomise a drum kit note.** The selected note changes; its neighbours do not. With no note
  selected the status line says to pick one.
- [ ] **Randomise with Effects ticked** never changes the MFX *type*, only its parameters.
- [ ] **Init** with and without a marked library tone, and after deleting the marked file (the stale-mark
  message).
- [ ] **Every one of the five engines** survives a randomise at 100 % on every category without the
  device falling silent or the resync reporting a block it could not read.
