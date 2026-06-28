# Waveform Bank Names — Phase 1 (core infrastructure) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the additive infrastructure for Group-Type/ID-aware waveform names — a `WaveformBanks` service over the 13 per-bank CSVs, a pure bank-selection rule, a per-parameter `EffectiveRepr`, the resolution helper, and the `IsParent` tagging — **without changing any displayed behavior yet** (the atomic flip is Phase 2).

**Architecture:** Pure, unit-tested pieces (`WaveBankResolver`, CSV parse, `WaveformBanks`, `WaveNameResolution.Resolve`, `WaveBankRegistry`) plus a nullable `EffectiveRepr` on `FullyQualifiedParameter` and `IsParent` markers on the Wave Group Type/ID params. Nothing here is wired into the live read path or the UI — so all current behavior (the old `PARTIAL_WAVEFORMS` names) is unchanged and all 244 tests stay green.

**Tech Stack:** .NET 10, NUnit, Avalonia `AssetLoader`. Build/test with the user-local SDK in Release (Debug exe is file-locked; MSB3027/MSB3021 = lock, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Filter one class: append `--filter "FullyQualifiedName~<ClassName>"`.

**Facts (verified):** Wave Group Type display values come from `INT_SRX_RES` = `{0:"Internal",1:"SRX",2:"Reserved",3:"Reserved"}`. Mapping: `Internal`→`INT` (Group ID 0), `SRX`→`SRX{GroupID}` (Group ID 1..12). CSV files `Src/Assets/PartialWaveForms_{INT,SRX1..12}.csv` already ship as app resources via `<AvaloniaResource Include="Assets\**"/>`. The CSV format is: first line is a `//` comment (skipped), then one quoted name per line; line order gives the wave number starting at 0 (mirrors `LoadWaveFormHelper`).

---

### Task 1: `WaveBankResolver` — the pure bank-selection rule

**Files:**
- Create: `Src/Models/Services/WaveBankResolver.cs`
- Create: `Tests/TestWaveBankResolver.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/TestWaveBankResolver.cs`:

```csharp
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveBankResolver
{
    [Test]
    public void Internal_MapsToInt()
        => Assert.That(WaveBankResolver.BankName("Internal", 0), Is.EqualTo("INT"));

    [Test]
    public void Srx_MapsToNumberedBank()
    {
        Assert.That(WaveBankResolver.BankName("SRX", 1), Is.EqualTo("SRX1"));
        Assert.That(WaveBankResolver.BankName("SRX", 12), Is.EqualTo("SRX12"));
    }

    [Test]
    public void ReservedOrUnknownType_FallsBackToInt()
    {
        Assert.That(WaveBankResolver.BankName("Reserved", 0), Is.EqualTo("INT"));
        Assert.That(WaveBankResolver.BankName("", 5), Is.EqualTo("INT"));
    }

    [Test]
    public void SrxWithIdOutOfRange_FallsBackToInt()
    {
        Assert.That(WaveBankResolver.BankName("SRX", 0), Is.EqualTo("INT"));
        Assert.That(WaveBankResolver.BankName("SRX", 13), Is.EqualTo("INT"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestWaveBankResolver"`. Expected: FAIL (`WaveBankResolver` does not exist).

- [ ] **Step 3: Implement**

Create `Src/Models/Services/WaveBankResolver.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run the filtered Test command. Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/WaveBankResolver.cs Tests/TestWaveBankResolver.cs
git commit -m "feat: WaveBankResolver (Group Type/ID -> bank name rule) + tests"
```

---

### Task 2: `WaveformBanks` — CSV parse + bank lookups

**Files:**
- Create: `Src/Models/Services/WaveformBanks.cs`
- Create: `Tests/TestWaveformBanks.cs`

The pure CSV parse + lookups are unit-tested with in-memory data; the asset-loading factory (`LoadFromAssets`) is exercised at runtime only (the test project does not copy the CSVs, so tests construct the service from in-memory dictionaries).

- [ ] **Step 1: Write the failing test**

Create `Tests/TestWaveformBanks.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveformBanks
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "StGrand pA L", [2] = "StGrand pA R" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "Kick 1 Menu", [1] = "Kick 2 MenuL" },
    });

    [Test]
    public void ParseWaveCsv_SkipsHeader_AndIndexesFromZero()
    {
        using var r = new StringReader("// header comment\n\"Off\"\n\"Wave A\"\n\"Wave B\"\n");
        var d = WaveformBanks.ParseWaveCsv(r);
        Assert.That(d[0], Is.EqualTo("Off"));
        Assert.That(d[1], Is.EqualTo("Wave A"));
        Assert.That(d[2], Is.EqualTo("Wave B"));
        Assert.That(d.Count, Is.EqualTo(3));
    }

    [Test]
    public void Name_ResolvesViaSelectedBank()
    {
        var b = Sample();
        Assert.That(b.Name("Internal", 0, 1), Is.EqualTo("StGrand pA L"));
        Assert.That(b.Name("SRX", 1, 1), Is.EqualTo("Kick 2 MenuL"));
    }

    [Test]
    public void Name_OutOfRange_ReturnsRawNumber()
        => Assert.That(Sample().Name("Internal", 0, 999), Is.EqualTo("999"));

    [Test]
    public void Number_ReverseLookup()
    {
        Assert.That(Sample().Number("Internal", 0, "StGrand pA R"), Is.EqualTo(2));
        Assert.That(Sample().Number("Internal", 0, "nope"), Is.Null);
    }

    [Test]
    public void FirstWave_ReturnsLowestKey()
    {
        Assert.That(Sample().FirstWave("Internal", 0), Is.EqualTo(0));
        Assert.That(Sample().FirstWave("SRX", 1), Is.EqualTo(0));
    }

    [Test]
    public void Bank_UnknownBank_ReturnsNull()
        => Assert.That(Sample().Bank("SRX", 7), Is.Null); // SRX7 not in sample
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestWaveformBanks"`. Expected: FAIL (`WaveformBanks` does not exist).

- [ ] **Step 3: Implement**

Create `Src/Models/Services/WaveformBanks.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Platform;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Holds the per-bank PCM waveform name lists (INT + SRX1..12) and resolves a wave number to a
/// name given a wave group's Type/ID. Bank selection is delegated to <see cref="WaveBankResolver"/>.</summary>
public sealed class WaveformBanks
{
    private readonly IReadOnlyDictionary<string, IDictionary<int, string>> _banks;

    public WaveformBanks(IReadOnlyDictionary<string, IDictionary<int, string>> banks) => _banks = banks;

    /// <summary>The name list for the bank selected by (groupType, groupId), or null if absent.</summary>
    public IDictionary<int, string>? Bank(string groupType, int groupId)
        => _banks.TryGetValue(WaveBankResolver.BankName(groupType, groupId), out var b) ? b : null;

    /// <summary>The wave name for the given number in the selected bank, or the raw number if unresolved.</summary>
    public string Name(string groupType, int groupId, int number)
    {
        var b = Bank(groupType, groupId);
        return b != null && b.TryGetValue(number, out var n) ? n : number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reverse lookup: the wave number for a name in the selected bank, or null if not found.</summary>
    public int? Number(string groupType, int groupId, string name)
    {
        var b = Bank(groupType, groupId);
        if (b == null) return null;
        foreach (var kv in b)
            if (kv.Value == name) return kv.Key;
        return null;
    }

    /// <summary>The lowest wave number in the selected bank (used for the out-of-range reset), or 0.</summary>
    public int FirstWave(string groupType, int groupId)
    {
        var b = Bank(groupType, groupId);
        if (b == null || b.Count == 0) return 0;
        var min = int.MaxValue;
        foreach (var k in b.Keys)
            if (k < min) min = k;
        return min;
    }

    /// <summary>Parse one wave CSV: skip the first (comment) line, then line order gives the wave number
    /// from 0; the first comma-separated field (unquoted) is the name. Mirrors the build-time loader.</summary>
    public static Dictionary<int, string> ParseWaveCsv(TextReader reader)
    {
        var dict = new Dictionary<int, string>();
        reader.ReadLine(); // header comment
        string? line;
        var id = 0;
        while ((line = reader.ReadLine()) != null)
        {
            var name = line.Split(',')[0].Trim('"');
            dict[id++] = name;
        }
        return dict;
    }

    private static readonly string[] BankNames =
        ["INT", "SRX1", "SRX2", "SRX3", "SRX4", "SRX5", "SRX6", "SRX7", "SRX8", "SRX9", "SRX10", "SRX11", "SRX12"];

    /// <summary>Load all 13 banks from the shipped CSV assets (avares://…/Assets/PartialWaveForms_*.csv).</summary>
    public static WaveformBanks LoadFromAssets()
    {
        var banks = new Dictionary<string, IDictionary<int, string>>();
        foreach (var name in BankNames)
        {
            var uri = new Uri($"avares://Integra7AuralAlchemist/Assets/PartialWaveForms_{name}.csv");
            using var reader = new StreamReader(AssetLoader.Open(uri));
            banks[name] = ParseWaveCsv(reader);
        }
        return new WaveformBanks(banks);
    }

    private static WaveformBanks? _default;
    /// <summary>Lazily-loaded shared instance backed by the CSV assets.</summary>
    public static WaveformBanks Default => _default ??= LoadFromAssets();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the filtered Test command. Expected: PASS (6 tests).

- [ ] **Step 5: Build the whole solution**

Run the Build command. Expected: `Build succeeded.` 0 errors (confirms the `Avalonia.Platform.AssetLoader` reference compiles in the model assembly).

- [ ] **Step 6: Commit**

```bash
git add Src/Models/Services/WaveformBanks.cs Tests/TestWaveformBanks.cs
git commit -m "feat: WaveformBanks service (per-bank wave name lists) + tests"
```

---

### Task 3: `EffectiveRepr` on `FullyQualifiedParameter`

**Files:**
- Modify: `Src/Models/Data/FullyQualifiedParameter.cs`

A nullable per-instance name list that overrides the static `ParSpec.Repr` when set. Added now; consumed in Phase 2.

- [ ] **Step 1: Add the property**

In `Src/Models/Data/FullyQualifiedParameter.cs`, find the `StringValue` property (it has a getter + a setter that raises `PropertyChanged`). Immediately AFTER the `StringValue` property, add:

```csharp
    /// <summary>When set, the name list to use instead of <see cref="ParSpec"/>.Repr (e.g. a wave bank
    /// selected by sibling Group Type/ID). Null for ordinary parameters. Callers use
    /// EffectiveRepr ?? ParSpec.Repr. Set by the domain's post-read resolution pass.</summary>
    public System.Collections.Generic.IDictionary<int, string>? EffectiveRepr { get; set; }
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (Unused for now.)

- [ ] **Step 3: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 254` (244 prior + 4 + 6 from Tasks 1–2).

- [ ] **Step 4: Commit**

```bash
git add Src/Models/Data/FullyQualifiedParameter.cs
git commit -m "feat: EffectiveRepr override on FullyQualifiedParameter"
```

---

### Task 4: `WaveBankRegistry` + `WaveNameResolution`

**Files:**
- Create: `Src/Models/Services/WaveBankRegistry.cs`
- Create: `Src/Models/Services/WaveNameResolution.cs`
- Create: `Tests/TestWaveNameResolution.cs`

The registry lists the 10 wave-number params and their sibling Group Type/ID paths. `WaveNameResolution` has a pure `Resolve` (tested) and an `Apply` glue method that sets `EffectiveRepr` + `StringValue` on a domain's parameters — built now, invoked from the domain in Phase 2.

- [ ] **Step 1: Write the failing test**

Create `Tests/TestWaveNameResolution.cs`:

```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestWaveNameResolution
{
    private static WaveformBanks Sample() => new(new Dictionary<string, IDictionary<int, string>>
    {
        ["INT"] = new Dictionary<int, string> { [0] = "Off", [1] = "StGrand pA L" },
        ["SRX1"] = new Dictionary<int, string> { [0] = "Kick 1 Menu", [1] = "Kick 2 MenuL" },
    });

    [Test]
    public void Resolve_PicksBankAndName()
    {
        var (bank, display) = WaveNameResolution.Resolve(Sample(), "SRX", 1, 1);
        Assert.That(display, Is.EqualTo("Kick 2 MenuL"));
        Assert.That(bank, Is.Not.Null);
        Assert.That(bank![0], Is.EqualTo("Kick 1 Menu"));
    }

    [Test]
    public void Resolve_Internal_UsesIntBank()
    {
        var (_, display) = WaveNameResolution.Resolve(Sample(), "Internal", 0, 1);
        Assert.That(display, Is.EqualTo("StGrand pA L"));
    }

    [Test]
    public void Registry_CoversAllTenWaveParams()
    {
        Assert.That(WaveBankRegistry.Entries.Count, Is.EqualTo(10));
        Assert.That(WaveBankRegistry.Entries.ContainsKey("PCM Synth Tone Partial/Wave Number L (Mono)"));
        Assert.That(WaveBankRegistry.Entries["PCM Synth Tone Partial/Wave Number L (Mono)"].TypePath,
            Is.EqualTo("PCM Synth Tone Partial/Wave Group Type"));
        Assert.That(WaveBankRegistry.Entries.ContainsKey("PCM Drum Kit Partial/WMT3 Wave Number R"));
        Assert.That(WaveBankRegistry.Entries["PCM Drum Kit Partial/WMT3 Wave Number R"].IdPath,
            Is.EqualTo("PCM Drum Kit Partial/WMT3 Wave Group ID"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestWaveNameResolution"`. Expected: FAIL (types do not exist).

- [ ] **Step 3: Implement the registry**

Create `Src/Models/Services/WaveBankRegistry.cs`:

```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The wave-number parameters whose name list is bank-selected, each mapped to its sibling
/// Wave Group Type + Wave Group ID parameter paths (within the same partial domain).</summary>
public static class WaveBankRegistry
{
    public readonly record struct Siblings(string TypePath, string IdPath);

    public static readonly IReadOnlyDictionary<string, Siblings> Entries = Build();

    private static Dictionary<string, Siblings> Build()
    {
        var d = new Dictionary<string, Siblings>();

        const string sp = "PCM Synth Tone Partial/";
        var synthSiblings = new Siblings(sp + "Wave Group Type", sp + "Wave Group ID");
        d[sp + "Wave Number L (Mono)"] = synthSiblings;
        d[sp + "Wave Number R"] = synthSiblings;

        const string dp = "PCM Drum Kit Partial/";
        for (var n = 1; n <= 4; n++)
        {
            var sib = new Siblings($"{dp}WMT{n} Wave Group Type", $"{dp}WMT{n} Wave Group ID");
            d[$"{dp}WMT{n} Wave Number L (Mono)"] = sib;
            d[$"{dp}WMT{n} Wave Number R"] = sib;
        }

        return d;
    }
}
```

- [ ] **Step 4: Implement the resolution helper**

Create `Src/Models/Services/WaveNameResolution.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Resolves bank-selected waveform names. Pure <see cref="Resolve"/> is unit-tested; <see cref="Apply"/>
/// is the domain glue that sets EffectiveRepr + StringValue on a partial's wave-number parameters
/// (invoked from the domain read path in Phase 2).</summary>
public static class WaveNameResolution
{
    /// <summary>The effective bank + display name for a wave number given its sibling Group Type/ID values.</summary>
    public static (IDictionary<int, string>? bank, string display) Resolve(
        WaveformBanks banks, string groupType, int groupId, int number)
        => (banks.Bank(groupType, groupId), banks.Name(groupType, groupId, number));

    /// <summary>For each registered wave-number parameter in <paramref name="ps"/>, read its sibling
    /// Group Type/ID values and set its EffectiveRepr + StringValue from the selected bank.</summary>
    public static void Apply(IReadOnlyList<FullyQualifiedParameter> ps, WaveformBanks banks)
    {
        var byPath = new Dictionary<string, FullyQualifiedParameter>(ps.Count);
        foreach (var p in ps) byPath[p.ParSpec.Path] = p;

        foreach (var (wavePath, sib) in WaveBankRegistry.Entries)
        {
            if (!byPath.TryGetValue(wavePath, out var wave)
                || !byPath.TryGetValue(sib.TypePath, out var type)
                || !byPath.TryGetValue(sib.IdPath, out var id))
                continue;

            if (!int.TryParse(wave.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                continue; // wave StringValue is the raw number until we override it below

            if (!int.TryParse(id.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId))
                groupId = 0;

            var (bank, display) = Resolve(banks, type.StringValue, groupId, number);
            wave.EffectiveRepr = bank;
            wave.StringValue = display;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the filtered Test command. Expected: PASS (3 tests).

- [ ] **Step 6: Run the full suite**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 257` (254 + 3).

- [ ] **Step 7: Commit**

```bash
git add Src/Models/Services/WaveBankRegistry.cs Src/Models/Services/WaveNameResolution.cs Tests/TestWaveNameResolution.cs
git commit -m "feat: WaveBankRegistry + WaveNameResolution (resolve/apply) + tests"
```

---

### Task 5: Mark Wave Group Type/ID as `IsParent`

**Files:**
- Modify: `Tools/ParameterBlobGenerator/ParameterDefinitions.cs` (10 lines)

Mark the 10 discriminator params (`Wave Group Type` + `Wave Group ID` for PCM Synth + each PCM Drum WMT) as `isparent:true` so Phase 2's reactivity (re-read on change) works. This is benign now — it only causes a domain re-read when one of these is edited.

- [ ] **Step 1: Edit the PCM Synth Tone wave group params**

In `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`, change these two lines to append `, isparent:true` before the closing `)`:

```csharp
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Group Type", offs:[0x00, 0x27], imin:0, imax:3, omin:0, omax:3, bytes:1, res:USED, nib:false, unit:"", repr:INT_SRX_RES),
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Group ID", offs:[0x00, 0x28], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:null),
```
become:
```csharp
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Group Type", offs:[0x00, 0x27], imin:0, imax:3, omin:0, omax:3, bytes:1, res:USED, nib:false, unit:"", repr:INT_SRX_RES, isparent:true),
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Group ID", offs:[0x00, 0x28], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:null, isparent:true),
```

- [ ] **Step 2: Edit the 8 PCM Drum WMT wave group params**

Similarly append `, isparent:true` to the 8 PCM Drum WMT lines — `WMT{1..4} Wave Group Type` and `WMT{1..4} Wave Group ID` (the paths `PCM Drum Kit Partial/WMT{n} Wave Group Type` and `… /WMT{n} Wave Group ID`). Each currently ends with `repr:INT_SRX_RES)` (Type) or `repr:null)` (ID); change the trailing `)` to `, isparent:true)`. Change nothing else, and touch no other parameters.

After editing, verify exactly 10 lines matching `Wave Group (Type|ID)` for the PCM Synth + PCM Drum WMT paths now contain `isparent:true`.

- [ ] **Step 3: Build (regenerates the blob)**

Run the Build command. Expected: `Build succeeded.` 0 errors, with the `GenerateParameterBlob: producing …parameters.bin` message (the blob regenerates from the edited source).

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 257` (the blob still loads; no regression).

- [ ] **Step 5: Commit**

```bash
git add Tools/ParameterBlobGenerator/ParameterDefinitions.cs
git commit -m "feat: mark PCM wave group Type/ID params as IsParent (for bank reactivity)"
```

---

## Done criteria

`WaveBankResolver`, `WaveformBanks` (+ CSV parse + asset loader), `EffectiveRepr`, `WaveBankRegistry`, and `WaveNameResolution` exist and are unit-tested (257 tests green); the 10 wave group Type/ID params are `IsParent`. **No displayed behavior has changed** — the old `PARTIAL_WAVEFORMS` names are still in use, and nothing here is wired into the live read path or UI. Phase 2 performs the atomic flip: invoke `WaveNameResolution.Apply` from the domain read, retire `PARTIAL_WAVEFORMS`, point both surfaces at `EffectiveRepr`/`WaveformBanks`, and add the out-of-range reset.
