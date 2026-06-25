# Parameter Data Memory Optimization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the ~58k-line in-code `Integra7Parameters` initializer (~14,180 heap `Integra7ParameterSpec` objects) with a build-time-generated compact binary blob loaded at runtime into a columnar store, exposed through a zero-allocation struct-view `Integra7ParameterSpec`.

**Architecture:** A non-shipped generator project owns the parameter definitions (source of truth), runs the dependency analyzer once, and serializes `parameters.bin`. The app loads that blob into a columnar `ParameterStore`; `Integra7ParameterSpec` becomes a `readonly struct = (store, index)` reading from parallel arrays. Generator and app are decoupled by the binary format only.

**Tech Stack:** C# / .NET 10, Avalonia 12, the existing `Tests` project. Build with the user-local SDK: `"$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`.

**Spec:** `docs/superpowers/specs/2026-06-25-parameter-data-memory-optimization-design.md`

---

## Ordering note (read first)

The `Tests` project references the app project. Changing `Integra7ParameterSpec` from a class to a struct **breaks the app's giant initializer**, so it must happen in the same atomic task that deletes that initializer (Task 6). Therefore:

- Tasks 1–5 are **additive**: the generator gets a copy of the data and emits the blob; the app keeps its old class + initializer and stays green. The format is proven against the store's **raw columns** (no struct dependency).
- Task 6 is the **atomic cut-over**: struct + store `Get()` + rewire `Integra7Parameters` + delete the initializer + fix Address sorting. The app goes briefly red mid-task and green by the end.
- Tasks 7–9 (tests/golden/build wiring) run after the app compiles again.

## Conventions used in every task

- **dotnet** = `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"` (PowerShell). The system `dotnet` only has 8/9 SDKs.
- Build the app to a scratch dir to avoid the running-app exe lock: `-o "$env:TEMP\i7build"`.
- Discover the test project path in Task 0 and use it everywhere (`Tests/Tests.csproj` below is a placeholder until confirmed).
- Commit after each task with the shown message; **never** commit `parameters.bin`.

## File structure (created/modified)

**New — generator (`Tools/ParameterBlobGenerator/`):** `ParameterBlobGenerator.csproj` (console exe, net10.0, not shipped), `ParameterDef.cs`, `ParameterDefinitions.cs` (moved data + Repr tables + CSV loading), `Integra7ParameterDatabaseAnalyzer.cs` (moved), `ParameterBlobWriter.cs`, `Program.cs`. Linked: `..\..\Src\Models\Services\ByteUtils.cs`, `..\..\Src\Models\Data\ParameterBlobFormat.cs`.

**New — app (`Src/Models/Data/`):** `ParameterBlobFormat.cs`, `ParameterStore.cs`.

**Modified — app:** `Integra7ParameterSpec.cs` (class→struct), `Integra7Parameters.cs` (shrinks to a loader), sort comparers (`ByteUtils.Bytes7ToInt(ParSpec.Address)`→`ParSpec.AddressInt`), `DataTemplateProvider.cs` (compute `Ticks` once per slider), `Src/Integra7AuralAlchemist.csproj` (pre-build target), `.gitignore`.

**Modified — tests:** repoint spec-constructing tests; add `TestParameterBlobRoundtrip.cs`.

---

## Task 0: Baseline

**Files:** none.

- [ ] **Step 1: Confirm the test project path and green state**
```
$dotnet = "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"
Get-ChildItem Tests -Filter *.csproj
& $dotnet test <TestsProject>
```
Expected: build + all tests pass. Record the test project path.

- [ ] **Step 2: Record baseline DLL size + live parameter count**
```
& $dotnet build Src/Integra7AuralAlchemist.csproj -c Release -o "$env:TEMP\i7base"
(Get-Item "$env:TEMP\i7base\Integra7AuralAlchemist.dll").Length
```
Note the size, and the live count from a prior run (`14180`; reconfirm by grepping `new(type:` count in `Integra7Parameters.cs`). No commit.

---

## Task 1: Shared blob-format constants

**Files:** Create `Src/Models/Data/ParameterBlobFormat.cs`.

- [ ] **Step 1: Create the constants**
```csharp
namespace Integra7AuralAlchemist.Models.Data;

// Shared contract between the build-time generator and the runtime loader.
// Bump Version on any layout change; the loader rejects mismatches.
public static class ParameterBlobFormat
{
    public const uint Magic = 0x49375042; // "I7PB"
    public const int Version = 1;
}
```

- [ ] **Step 2: Build the app**
Run: `& $dotnet build Src/Integra7AuralAlchemist.csproj -o "$env:TEMP\i7build"` → Build succeeded.

- [ ] **Step 3: Commit**
```
git add Src/Models/Data/ParameterBlobFormat.cs
git commit -m "Add shared parameter blob format constants"
```

---

## Task 2: Generator project + `ParameterDef`

**Files:** Create `Tools/ParameterBlobGenerator/ParameterBlobGenerator.csproj`, `ParameterDef.cs`, `Program.cs` (stub).

- [ ] **Step 1: Project file**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\Src\Models\Services\ByteUtils.cs" Link="ByteUtils.cs" />
    <Compile Include="..\..\Src\Models\Data\ParameterBlobFormat.cs" Link="ParameterBlobFormat.cs" />
  </ItemGroup>
</Project>
```
If linking `ByteUtils.cs` drags in Avalonia/app namespaces and fails to build, instead create `Tools/ParameterBlobGenerator/ByteUtils.cs` containing only the `Bytes7ToInt` method (copy it verbatim) in a `Integra7AuralAlchemist.Models.Services` static class, and drop the link.

- [ ] **Step 2: `ParameterDef.cs`** (authoring type — current spec fields, mutable so the analyzer can set `IsParent`/`ParentCtrl2`)
```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.ParameterGen;

public enum SpecType { NUMERIC, ASCII, DISCRETE }

public sealed class ParameterDef
{
    public ParameterDef(SpecType type, string path, byte[] offs, int imin, int imax, float omin, float omax,
        int bytes, bool res, bool nib, string unit, IDictionary<int, string>? repr, string par = "", string parval = "",
        bool isparent = false, string par2 = "", string parval2 = "", float imin2 = float.NaN, float imax2 = float.NaN,
        float omin2 = float.NaN, float omax2 = float.NaN, List<System.Tuple<int, string>>? discrete = null)
    {
        Type = type; Path = path; Address = offs; IMin = imin; IMax = imax; OMin = omin; OMax = omax;
        Bytes = bytes; Reserved = res; PerNibble = nib; Unit = unit; Repr = repr; ParentCtrl = par;
        ParentCtrlDispValue = parval; IsParent = isparent; ParentCtrl2 = par2; ParentCtrlDispValue2 = parval2;
        IMin2 = imin2; IMax2 = imax2; OMin2 = omin2; OMax2 = omax2; Discrete = discrete;
    }

    public SpecType Type { get; }
    public string Path { get; }
    public byte[] Address { get; }
    public int IMin { get; } public int IMax { get; }
    public float OMin { get; } public float OMax { get; }
    public int Bytes { get; }
    public bool Reserved { get; } public bool PerNibble { get; }
    public string Unit { get; }
    public IDictionary<int, string>? Repr { get; }
    public List<System.Tuple<int, string>>? Discrete { get; }
    public string ParentCtrl { get; set; } public string ParentCtrlDispValue { get; set; }
    public bool IsParent { get; set; }
    public string ParentCtrl2 { get; set; } public string ParentCtrlDispValue2 { get; set; }
    public float IMin2 { get; } public float IMax2 { get; }
    public float OMin2 { get; } public float OMax2 { get; }
    public string Name => Path.Split('/')[^1];
}
```

- [ ] **Step 3: Stub `Program.cs` + build**
```csharp
namespace Integra7AuralAlchemist.ParameterGen;
internal static class Program { public static int Main(string[] args) { System.Console.WriteLine("stub"); return 0; } }
```
Run: `& $dotnet build Tools/ParameterBlobGenerator/ParameterBlobGenerator.csproj` → Build succeeded.

- [ ] **Step 4: Commit**
```
git add Tools/ParameterBlobGenerator
git commit -m "Scaffold ParameterBlobGenerator project and ParameterDef"
```

---

## Task 3: Move definitions + Repr tables + CSV loading + analyzer into the generator

The app's `Integra7Parameters.cs` is **left untouched** (temporary data duplication; removed in Task 6).

**Files:** Create `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`; `git mv` the analyzer.

- [ ] **Step 1: `ParameterDefinitions.cs`** — copy from `Src/Models/Data/Integra7Parameters.cs`:
  - all `Repr` dictionaries (`public readonly IDictionary<int,string> ...`, ~lines 14–2050) as fields/locals of `ParameterDefinitions`,
  - the waveform dictionaries + `LoadWaveFormHelper`/`LoadPCMWaveForms` — but replace Avalonia `AssetLoader` with `System.IO` reading from a passed-in `assetsDir` (`Path.Combine(assetsDir, "PcmWavenumbers.csv")`, same parse),
  - the giant `[ new(...) , ... ]` initializer (~lines 2208–57907) as the body of `public List<ParameterDef> Build(string assetsDir)`.
  Mechanical edits: keep `const SpecType NUM = SpecType.NUMERIC;` etc.; `new(type:NUM, ...)` target-types to `ParameterDef`; the `repr:CHORUS_TYPE` references resolve to the moved fields. Call `LoadPCMWaveForms(assetsDir)` at the top of `Build`.

- [ ] **Step 2: Move the analyzer**
```
git mv Src/Models/Services/Integra7ParameterDatabaseAnalyzer.cs Tools/ParameterBlobGenerator/Integra7ParameterDatabaseAnalyzer.cs
```
In the moved file: namespace → `Integra7AuralAlchemist.ParameterGen`; every `Integra7ParameterSpec` → `ParameterDef`. (Its only app caller was the giant `Integra7Parameters` ctor, removed in Task 6; the test is repointed in Task 7.)

- [ ] **Step 3: Verify the generator builds and counts correctly**
Temporarily in `Program.Main`:
```csharp
var defs = new ParameterDefinitions().Build(args.Length > 0 ? args[0] : "Src/Assets");
Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);
System.Console.WriteLine($"defs={defs.Count}");
```
Run from repo root: `& $dotnet run --project Tools/ParameterBlobGenerator -- Src/Assets`
Expected: `defs=14180` (must equal the Task 0 live count).

- [ ] **Step 4: Commit**
```
git add Tools/ParameterBlobGenerator Src/Models/Services/Integra7ParameterDatabaseAnalyzer.cs
git commit -m "Move parameter definitions, repr tables and analyzer into the generator"
```

---

## Task 4: Blob writer + raw store loader + round-trip (TDD, app stays green)

The format is defined here once. The round-trip test asserts on the store's **raw accessors** so it does not depend on the struct (added in Task 6).

### Binary format (authoritative)
`BinaryWriter`/`BinaryReader` (little-endian), in order:
1. `uint Magic`, `int Version`, `int count`.
2. **String table:** `int n`, then n × `writer.Write(string)`. Index = id. Id 0 is `""`.
3. **Repr tables:** `int n`, then each: `int entryCount`, then entryCount × (`int key`, `int valueStringId`). Index = id.
4. **Discrete tables:** `int n`, then each: `int entryCount`, then entryCount × (`int key`, `int valueStringId`). Index = id.
5. **Columns** (each `count` entries, contiguous, this order): `byte type`, `int pathId`, `int nameId`, `uint addrPacked`, `byte addrLen`, `int addrInt`, `int imin`, `int imax`, `float omin`, `float omax`, `byte bytes`, `byte flags` (bit0 reserved|bit1 perNibble|bit2 isParent), `int unitId`, `int reprId`(-1=null), `int discreteId`(-1=null), `int parentPathId`, `int parvalId`, `int parentPath2Id`, `int parval2Id`, `float imin2`, `float imax2`, `float omin2`, `float omax2`.

`addrPacked` = bytes big-endian into a uint; `addrLen` = `Address.Length`; `addrInt` = `ByteUtils.Bytes7ToInt(Address)`. All strings (path, name, unit, parvals, parentPaths, repr/discrete values) interned in the one string table; `""`/absent → id 0. `reprId`/`discreteId` `-1` when the def's `Repr`/`Discrete` is null.

**Files:** Create `Tools/ParameterBlobGenerator/ParameterBlobWriter.cs`, `Src/Models/Data/ParameterStore.cs`, `Tests/TestParameterBlobRoundtrip.cs`; modify the test `.csproj`.

- [ ] **Step 1: Add generator project ref to the test project + write the failing test**
In the test `.csproj`: `<ProjectReference Include="..\Tools\ParameterBlobGenerator\ParameterBlobGenerator.csproj" />` (fix the relative path).

`Tests/TestParameterBlobRoundtrip.cs` (match the framework the project already uses — example shown for NUnit):
```csharp
using System.Collections.Generic;
using System.IO;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.ParameterGen;
using NUnit.Framework;

namespace Tests;

public class TestParameterBlobRoundtrip
{
    private static List<ParameterDef> SampleDefs() => new()
    {
        new(SpecType.NUMERIC, "Setup/Sound Mode", new byte[]{0x00,0x00}, 1, 4, 1, 4, 1, false, false, "",
            new Dictionary<int,string>{[1]="STUDIO",[2]="GM1"}),
        new(SpecType.NUMERIC, "Studio Set Common Chorus/Chorus Parameter 1/Delay Left", new byte[]{0x00,0x04},
            12768, 52768, -20000, 20000, 4, false, true, "ms", null,
            par:"Studio Set Common Chorus/Chorus Type", parval:"Delay"),
        new(SpecType.ASCII, "SuperNATURAL Synth Tone Common/Tone Name", new byte[]{0x00,0x10}, 32, 127, 32, 127,
            12, false, false, "", null),
    };

    [Test]
    public void Roundtrip_PreservesRawColumns()
    {
        var defs = SampleDefs();
        using var ms = new MemoryStream();
        ParameterBlobWriter.Write(ms, defs);
        ms.Position = 0;
        var store = ParameterStore.Load(ms);

        Assert.That(store.Count, Is.EqualTo(defs.Count));
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            Assert.That(store.Str(store.PathIds[i]), Is.EqualTo(d.Path), $"path@{i}");
            Assert.That(store.Str(store.NameIds[i]), Is.EqualTo(d.Name), $"name@{i}");
            Assert.That((int)store.Types[i], Is.EqualTo((int)d.Type), $"type@{i}");
            Assert.That(store.UnpackAddress(i), Is.EqualTo(d.Address), $"addr@{i}");
            Assert.That(store.IMins[i], Is.EqualTo(d.IMin), $"imin@{i}");
            Assert.That(store.Str(store.ParentPathIds[i]), Is.EqualTo(d.ParentCtrl), $"parent@{i}");
            Assert.That((store.Flags[i] & 2) != 0, Is.EqualTo(d.PerNibble), $"nib@{i}");
            if (d.Repr is null) Assert.That(store.ReprIds[i], Is.EqualTo(-1), $"reprnull@{i}");
            else Assert.That(store.Reprs[store.ReprIds[i]]![1], Is.EqualTo(d.Repr[1]), $"repr@{i}");
        }
    }
}
```

- [ ] **Step 2: Run → fails to compile** (`ParameterBlobWriter`, `ParameterStore` missing).
Run: `& $dotnet test <TestsProject> --filter TestParameterBlobRoundtrip` → FAIL.

- [ ] **Step 3: Implement `ParameterBlobWriter.Write(Stream, IReadOnlyList<ParameterDef>)`** per the format. Use a string interner (`Dictionary<string,int>` seeded with `""`→0) and reference-keyed interners for repr/discrete tables. Helper `int S(string?)` (null/empty→0). Write header, tables, then each column in a `for` loop.

- [ ] **Step 4: Implement `ParameterStore`** — `Load(Stream)` reads header (validate `Magic`/`Version`, else throw), the three tables, and the columns into arrays. Expose these **`public`** (the round-trip test is a separate assembly; alternatively keep them `internal` and add `[assembly: InternalsVisibleTo("<TestsAssemblyName>")]` to the app — public is simpler): `int Count`; column arrays `byte[] Types`, `int[] PathIds`, `int[] NameIds`, `uint[] AddrPacked`, `byte[] AddrLen`, `int[] AddrInts`, `int[] IMins`, `int[] IMaxs`, `float[] OMins`, `float[] OMaxs`, `byte[] BytesCol`, `byte[] Flags`, `int[] UnitIds`, `int[] ReprIds`, `int[] DiscreteIds`, `int[] ParentPathIds`, `int[] ParvalIds`, `int[] ParentPath2Ids`, `int[] Parval2Ids`, `float[] IMin2s`, `float[] IMax2s`, `float[] OMin2s`, `float[] OMax2s`; tables `string Str(int id)`, `IDictionary<int,string>?[] Reprs`, `List<Tuple<int,string>>?[] Discretes`; and `byte[] UnpackAddress(int i)` (rebuild from `AddrPacked[i]`/`AddrLen[i]`). Rebuild each `Reprs[id]` as a `Dictionary<int,string>` using `Str(valueStringId)`. **Do not add `Get()` yet** (the struct doesn't exist).

- [ ] **Step 5: Run → passes**
Run: `& $dotnet test <TestsProject> --filter TestParameterBlobRoundtrip` → PASS.

- [ ] **Step 6: Confirm the app still builds (old class untouched)**
Run: `& $dotnet build Src/Integra7AuralAlchemist.csproj -o "$env:TEMP\i7build"` → Build succeeded.

- [ ] **Step 7: Commit**
```
git add Tools/ParameterBlobGenerator/ParameterBlobWriter.cs Src/Models/Data/ParameterStore.cs Tests/TestParameterBlobRoundtrip.cs <test csproj>
git commit -m "Add parameter blob writer + columnar store with round-trip test"
```

---

## Task 5: Generator emits the blob

**Files:** Modify `Tools/ParameterBlobGenerator/Program.cs`.

- [ ] **Step 1: Final `Program.cs`**
```csharp
namespace Integra7AuralAlchemist.ParameterGen;

internal static class Program
{
    public static int Main(string[] args)
    {
        string assetsDir = "Src/Assets", output = "Src/Assets/parameters.bin";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--assets") assetsDir = args[i + 1];
            if (args[i] == "-o") output = args[i + 1];
        }
        var defs = new ParameterDefinitions().Build(assetsDir);
        Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
        Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        using var fs = System.IO.File.Create(output);
        ParameterBlobWriter.Write(fs, defs);
        System.Console.WriteLine($"Wrote {defs.Count} parameters to {output}");
        return 0;
    }
}
```

- [ ] **Step 2: Generate + sanity check**
Run: `& $dotnet run --project Tools/ParameterBlobGenerator -- --assets Src/Assets -o Src/Assets/parameters.bin`
Expected: `Wrote 14180 parameters ...`; `(Get-Item Src/Assets/parameters.bin).Length` ≈ 400–700 KB.

- [ ] **Step 3: Commit (code only)**
```
git add Tools/ParameterBlobGenerator/Program.cs
git commit -m "Emit parameters.bin from the generator"
```

---

## Task 6: Atomic cut-over — struct + blob-backed `Integra7Parameters`

The app goes red after Step 1 and green by Step 5; do the whole task before testing.

**Files:** Rewrite `Src/Models/Data/Integra7ParameterSpec.cs` and `Integra7Parameters.cs`; add `Get()` to `ParameterStore`; fix Address sorts; tweak `DataTemplateProvider`.

- [ ] **Step 1: `Integra7ParameterSpec` → readonly struct view**
```csharp
using System;
using System.Collections.Generic;
using Avalonia.Collections;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Models.Data;

public readonly struct Integra7ParameterSpec
{
    public enum SpecType { NUMERIC, ASCII, DISCRETE }
    private readonly ParameterStore _s; private readonly int _i;
    public Integra7ParameterSpec(ParameterStore store, int index) { _s = store; _i = index; }

    public SpecType Type => (SpecType)_s.Types[_i];
    public string Path => _s.Str(_s.PathIds[_i]);
    public string Name => _s.Str(_s.NameIds[_i]);
    public byte[] Address => _s.UnpackAddress(_i);
    public int AddressInt => _s.AddrInts[_i];
    public int IMin => _s.IMins[_i]; public int IMax => _s.IMaxs[_i];
    public float OMin => _s.OMins[_i]; public float OMax => _s.OMaxs[_i];
    public int Bytes => _s.BytesCol[_i];
    public bool Reserved => (_s.Flags[_i] & 1) != 0;
    public bool PerNibble => (_s.Flags[_i] & 2) != 0;
    public bool IsParent => (_s.Flags[_i] & 4) != 0;
    public string Unit => _s.Str(_s.UnitIds[_i]);
    public IDictionary<int, string>? Repr => _s.ReprIds[_i] < 0 ? null : _s.Reprs[_s.ReprIds[_i]];
    public List<Tuple<int, string>>? Discrete => _s.DiscreteIds[_i] < 0 ? null : _s.Discretes[_s.DiscreteIds[_i]];
    public string ParentCtrl => _s.Str(_s.ParentPathIds[_i]);
    public string ParentCtrlDispValue => _s.Str(_s.ParvalIds[_i]);
    public string ParentCtrl2 => _s.Str(_s.ParentPath2Ids[_i]);
    public string ParentCtrlDispValue2 => _s.Str(_s.Parval2Ids[_i]);
    public float IMin2 => _s.IMin2s[_i]; public float IMax2 => _s.IMax2s[_i];
    public float OMin2 => _s.OMin2s[_i]; public float OMax2 => _s.OMax2s[_i];

    public bool IsSameAs(Integra7ParameterSpec other) => Path == other.Path;

    public AvaloniaList<double> Ticks
    {
        get
        {
            if (Type == SpecType.ASCII || Type == SpecType.DISCRETE) return [];
            if (!float.IsNaN(IMin2) && !float.IsNaN(IMax2) && !float.IsNaN(OMin2) && !float.IsNaN(OMax2))
            {
                AvaloniaList<double> t = [];
                for (var i = (long)IMin2; i < (long)(IMax2 + 1); i++) t.Add(Math.Round(Mapping.linlin(i, IMin2, IMax2, OMin2, OMax2), 2));
                return t;
            }
            if (OMin2 == -20000 && OMax2 == 20000) { AvaloniaList<double> t = []; for (long i = 0; i < 128; i++) t.Add(i); return t; }
            AvaloniaList<double> r = []; for (var i = (long)IMin; i <= IMax; i++) r.Add(Mapping.linlin(i, IMin, IMax, OMin, OMax)); return r;
        }
    }
}
```
Add to `ParameterStore`: `public Integra7ParameterSpec Get(int idx) => new(this, idx);`.

- [ ] **Step 2: Rewrite `Integra7Parameters.cs` as a loader**
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Platform;

namespace Integra7AuralAlchemist.Models.Data;

public class Integra7Parameters
{
    private readonly ParameterStore _store;
    private readonly Dictionary<string, int> _index = new();

    public Integra7Parameters(bool testing = false)
    {
        using var s = AssetLoader.Open(new Uri("avares://Integra7AuralAlchemist/Assets/parameters.bin"));
        _store = ParameterStore.Load(s);
        for (int i = 0; i < _store.Count; i++) _index[_store.Str(_store.PathIds[i])] = i;
    }

    public Integra7ParameterSpec Lookup(string path)
    {
        if (!_index.TryGetValue(path, out var i)) { Debug.Assert(false, $"Path {path} not found."); return default; }
        return _store.Get(i);
    }
    public int LookupIndex(string path) => _index.TryGetValue(path, out var i) ? i : -1;

    public List<Integra7ParameterSpec> GetParametersFromTo(string firstPar, string endPar)
    {
        var r = new List<Integra7ParameterSpec>();
        int a = LookupIndex(firstPar), b = LookupIndex(endPar);
        Debug.Assert(a != -1 && b != -1 && a <= b);
        for (int i = a; i <= b; i++) r.Add(_store.Get(i));
        return r;
    }

    public List<Integra7ParameterSpec> GetParametersWithPrefix(string prefix)
    {
        var r = new List<Integra7ParameterSpec>();
        for (int i = 0; i < _store.Count; i++)
            if (_store.Str(_store.PathIds[i]).StartsWith(prefix)) r.Add(_store.Get(i));
        return r;
    }
}
```
Delete the giant initializer, Repr tables, waveform loaders, analyzer calls, and the `_parameters`/`_parametersIndex`/`USED`/`RESERVED` members (grep first: if anything still references `Integra7Parameters.USED`/`RESERVED`, keep those two consts).

- [ ] **Step 3: Fix Address-based sorting**
Grep `Bytes7ToInt(` over `Src/`. Where the argument is `<x>.ParSpec.Address` for sort keys (e.g. `SortExpressionComparer<FullyQualifiedParameter>.Ascending(t => ByteUtils.Bytes7ToInt(t.ParSpec.Address))`), replace with `t.ParSpec.AddressInt`. Leave sysex *building* (`ByteUtils.AddressWithOffset(..., ParSpec.Address)`) untouched.

- [ ] **Step 4: `DataTemplateProvider` — compute `Ticks` once per slider** (spec micro-opt)
In `Src/DataTemplates/DataTemplateProvider.cs`, in each slider branch, replace the repeated `p.ParSpec.Ticks` reads with one local: `var ticks = p.ParSpec.Ticks;` then use `ticks` for `Ticks = ticks` and the `ticks.First()/ticks.Last()` checks.

- [ ] **Step 5: Generate blob, build, run**
```
& $dotnet run --project Tools/ParameterBlobGenerator -- --assets Src/Assets -o Src/Assets/parameters.bin
& $dotnet build Src/Integra7AuralAlchemist.csproj -o "$env:TEMP\i7build"
```
Expected: Build succeeded, 0 errors. Then close any running instance and launch; the startup log must read `14180` parameters and the UI must populate.

- [ ] **Step 6: Commit (no blob)**
```
git add Src/Models/Data/Integra7ParameterSpec.cs Src/Models/Data/Integra7Parameters.cs Src/Models/Data/ParameterStore.cs Src/DataTemplates/DataTemplateProvider.cs <sort-fix files>
git commit -m "Cut over to blob-backed parameter store + struct-view spec"
```

---

## Task 7: Repoint existing unit tests

**Files:** Modify `Tests/TestIntegra7ParameterDatabaseAnalyzer.cs`, `TestParameterListSysexSizeCalculator.cs`, `TestDisplayValueToRawValueConverter.cs`, `TestSysexParameterValueInterpreter.cs`, `TestStartAddresses.cs`.

- [ ] **Step 1: Analyzer test** → `using Integra7AuralAlchemist.ParameterGen;`, `Integra7ParameterSpec`→`ParameterDef`. It now tests the generator's analyzer.

- [ ] **Step 2: Spec-constructing service tests** — add this helper to each (or a shared test util):
```csharp
private static ParameterStore StoreOf(params ParameterDef[] defs)
{
    using var ms = new System.IO.MemoryStream();
    ParameterBlobWriter.Write(ms, new System.Collections.Generic.List<ParameterDef>(defs));
    ms.Position = 0; return ParameterStore.Load(ms);
}
```
Rewrite each `new Integra7ParameterSpec(NUM, ...)` as `new ParameterDef(SpecType.NUMERIC, ...)`, build a `ParameterStore` via `StoreOf(...)`, and feed `store.Get(i)` struct-views to the service under test (`ParameterListSysexSizeCalculator`, `SysexParameterValueInterpreter`, `DisplayValueToRawValueConverter`, start-address logic). Keep the assertions identical.

- [ ] **Step 3: Run the full suite**
Run: `& $dotnet test <TestsProject>` → all pass.

- [ ] **Step 4: Commit**
```
git add Tests
git commit -m "Repoint parameter tests onto ParameterDef + ParameterStore"
```

---

## Task 8: Full-database golden test

**Files:** Modify `Tests/TestParameterBlobRoundtrip.cs`.

- [ ] **Step 1: Add the golden test** (now via the struct, full data)
```csharp
[Test]
public void Golden_FullDatabase_MatchesDefinitions()
{
    var assets = System.IO.Path.Combine(FindRepoRoot(), "Src", "Assets");
    var defs = new ParameterDefinitions().Build(assets);
    Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
    Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);
    using var ms = new System.IO.MemoryStream();
    ParameterBlobWriter.Write(ms, defs);
    ms.Position = 0;
    var store = ParameterStore.Load(ms);

    Assert.That(store.Count, Is.EqualTo(defs.Count));
    for (int i = 0; i < defs.Count; i++)
    {
        var d = defs[i]; var s = store.Get(i);
        Assert.That(s.Path, Is.EqualTo(d.Path), $"path@{i}");
        Assert.That(s.Address, Is.EqualTo(d.Address), $"addr@{i}");
        Assert.That(s.IsParent, Is.EqualTo(d.IsParent), $"isparent@{i}");
        Assert.That(s.ParentCtrl, Is.EqualTo(d.ParentCtrl), $"parent@{i}");
        Assert.That(s.ParentCtrl2, Is.EqualTo(d.ParentCtrl2), $"parent2@{i}");
    }
}

private static string FindRepoRoot()
{
    var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
    while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "Src", "Assets"))) d = d.Parent;
    return d?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
}
```
(`FindRepoRoot` walks up from the test output dir until it finds `Src/Assets`, so the CSVs resolve regardless of CWD.)

- [ ] **Step 2: Run** → `& $dotnet test <TestsProject> --filter TestParameterBlobRoundtrip` → PASS (count 14180).

- [ ] **Step 3: Commit**
```
git add Tests/TestParameterBlobRoundtrip.cs
git commit -m "Add full-database golden test for the parameter blob"
```

---

## Task 9: Build-time generation wiring + gitignore

**Files:** Modify `Src/Integra7AuralAlchemist.csproj`, `.gitignore`.

- [ ] **Step 1: Ignore the blob**
Append to `.gitignore`: `Src/Assets/parameters.bin`. If previously committed: `git rm --cached Src/Assets/parameters.bin`.

- [ ] **Step 2: Pre-build target** (before `</Project>` in the app csproj)
```xml
<ItemGroup>
  <GeneratorSources Include="$(MSBuildProjectDirectory)\..\Tools\ParameterBlobGenerator\**\*.cs" />
  <GeneratorSources Include="$(MSBuildProjectDirectory)\Assets\*.csv" />
</ItemGroup>
<Target Name="GenerateParameterBlob" BeforeTargets="BeforeBuild"
        Inputs="@(GeneratorSources)" Outputs="$(MSBuildProjectDirectory)\Assets\parameters.bin">
  <Exec Command="dotnet run --project &quot;$(MSBuildProjectDirectory)\..\Tools\ParameterBlobGenerator\ParameterBlobGenerator.csproj&quot; -c $(Configuration) -- --assets &quot;$(MSBuildProjectDirectory)\Assets&quot; -o &quot;$(MSBuildProjectDirectory)\Assets\parameters.bin&quot;" />
</Target>
```
The build machine's `dotnet` must be .NET 10 (here: the user-local SDK; CI needs .NET 10 on PATH or `DOTNET_ROOT`). If `dotnet run` inside MSBuild proves flaky, the documented fallback (spec) is a committed blob guarded by the golden test.

- [ ] **Step 3: Clean-build end-to-end**
```
Remove-Item Src/Assets/parameters.bin -ErrorAction SilentlyContinue
& $dotnet build Src/Integra7AuralAlchemist.csproj -o "$env:TEMP\i7build2"
```
Expected: the target runs the generator, recreates `Src/Assets/parameters.bin`, build succeeds; the app DLL is far smaller than the Task 0 baseline.

- [ ] **Step 4: Commit**
```
git add Src/Integra7AuralAlchemist.csproj .gitignore
git commit -m "Generate parameters.bin at build time; ignore the blob"
```

---

## Final verification
- [ ] `& $dotnet test <TestsProject>` — all green.
- [ ] Clean build (blob deleted) succeeds and the app runs; startup log reports `14180` parameters.
- [ ] App DLL size vs Task 0 baseline recorded (expect multi-MB reduction).
- [ ] `git status`/`git log` show no committed `parameters.bin`.
- [ ] Manual hardware pass (user): load tones across all 5 tone types, edit values, confirm sysex read/write correct end-to-end.
