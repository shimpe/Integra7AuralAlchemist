# Studio Set Snapshot Files Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save the complete Studio Set currently in the instrument to a file on disk, and write a saved one back.

**Architecture:** A snapshot is a JSON document listing, per parameter domain, every parameter's path and its *displayed* value — the same string space the whole application already works in. Capture reads all 53 Studio Set domains inside one MIDI conversation and records their values; restore pushes the values into the local model and then writes each domain back with the existing bulk range write, so a restore costs 53 transmissions rather than ~1400 single-parameter writes. The file format is pure data with no Avalonia or MIDI dependency, so it is unit-tested headless.

**Tech Stack:** .NET 10, System.Text.Json, NUnit, Avalonia `IStorageProvider` for the file pickers, ReactiveUI `Interaction` for the view-model→view hop.

---

## Background the implementer needs

**Domains.** A `DomainBase` is one contiguous block of parameters at one address (see
`docs/MIDI_DEVICE_ACCESS.md` and `docs/UI_HARDWARE_DATAFLOW.md`). It is identified by three address
*names*: `StartAddressName`, `OffsetAddressName`, `Offset2AddressName`. `Integra7Domain.GetDomain(start, offset, offset2)`
resolves a triple back to the live domain instance.

**A Studio Set is 53 domains:** five common blocks plus three per part × 16 parts.

**Displayed values.** `FullyQualifiedParameter.StringValue` is the mapped, human-readable value
("Room1", "-24", "8000"). `DomainBase.ModifySingleParameterDisplayedValue(path, displayed)` sets it
locally; `DomainBase.WriteToIntegraAsync(lease)` then transmits the whole block.

**Order matters on restore.** Some parameters only exist when a discriminator has a particular value
(chorus type decides what "Chorus Parameter 1" means — see the memory note on conditional
parameters). `ModifySingleParameterDisplayedValue` recomputes the parser context on every call, so
applying values **in the order they were captured** (address order) sets each discriminator before
its dependents. This is why the file stores an ordered *list*, not a JSON object: `Dictionary<K,V>`
round-trips do not guarantee order.

**Limitation to state in the UI.** A Studio Set names each part's tone by bank/program number. It
does not contain the tone. Restoring onto an instrument whose *user* tones have changed will recall
different sounds. Capturing tone data too is a later stage.

---

## File structure

| File | Responsibility |
| --- | --- |
| `Src/Models/Services/StudioSetSnapshot.cs` (create) | The snapshot record types, the format version, and JSON read/write. Pure — no Avalonia, no MIDI. |
| `Src/Models/Services/StudioSetDomainNames.cs` (create) | The list of 53 address-name triples that make up a Studio Set. Pure — plain strings. |
| `Src/Models/Services/StudioSetSnapshotService.cs` (create) | Capture from and restore to an `Integra7Domain`, inside a caller-supplied lease. |
| `Tests/TestStudioSetSnapshot.cs` (create) | Round-trip, ordering, version rejection, domain-name list. |
| `Src/ViewModels/MainWindowViewModel.cs` (modify) | `SaveStudioSetAsync` / `LoadStudioSetAsync` commands, and the two file-picker interactions. |
| `Src/Views/MainWindow.axaml.cs` (modify) | Register the picker handlers, next to the existing `ShowSaveUserToneDialog` registration at line 62. |
| `Src/Views/MainWindow.axaml` (modify) | Two buttons in the existing top button row. |

---

## Task 1: The snapshot format

**Files:**
- Create: `Src/Models/Services/StudioSetSnapshot.cs`
- Test: `Tests/TestStudioSetSnapshot.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class StudioSetSnapshotTests
{
    private static StudioSetSnapshot Sample() => new(
        StudioSetSnapshot.CurrentFormatVersion,
        "World Pop Set",
        [
            new SnapshotDomain("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common",
            [
                new SnapshotValue("Studio Set Common/Studio Set Name", "World Pop Set"),
                new SnapshotValue("Studio Set Common/Studio Set Tempo", "120"),
            ]),
        ]);

    [Test]
    public void Round_trips_through_json()
    {
        var restored = StudioSetSnapshot.FromJson(StudioSetSnapshot.ToJson(Sample()));

        Assert.That(restored.Name, Is.EqualTo("World Pop Set"));
        Assert.That(restored.Domains, Has.Count.EqualTo(1));
        Assert.That(restored.Domains[0].Offset2, Is.EqualTo("Offset2/Studio Set Common"));
        Assert.That(restored.Domains[0].Values[1].Path, Is.EqualTo("Studio Set Common/Studio Set Tempo"));
        Assert.That(restored.Domains[0].Values[1].Value, Is.EqualTo("120"));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~StudioSetSnapshotTests"`

Expected: FAIL — `StudioSetSnapshot` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One parameter and the value it displayed when the snapshot was taken.</summary>
public sealed record SnapshotValue(string Path, string Value);

/// <summary>One parameter block, identified by the three address names that resolve it back to a
/// live domain. Values are an ordered list, not a map: restoring has to set a discriminator before
/// the parameters that only exist because of it, and address order gives exactly that.</summary>
public sealed record SnapshotDomain(string Start, string Offset, string Offset2, List<SnapshotValue> Values);

/// <summary>A complete Studio Set as displayed values. Pure data — no Avalonia, no MIDI.</summary>
public sealed record StudioSetSnapshot(int FormatVersion, string Name, List<SnapshotDomain> Domains)
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Indented deliberately: these files are meant to be read and diffed.</summary>
    public static string ToJson(StudioSetSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static StudioSetSnapshot FromJson(string json)
    {
        StudioSetSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<StudioSetSnapshot>(json, Options);
        }
        catch (JsonException e)
        {
            throw new SnapshotFormatException("This file is not a Studio Set snapshot.", e);
        }

        if (snapshot is null)
            throw new SnapshotFormatException("This file is empty.");
        if (snapshot.FormatVersion != CurrentFormatVersion)
            throw new SnapshotFormatException(
                $"This snapshot is format version {snapshot.FormatVersion}; this build reads version {CurrentFormatVersion}.");
        return snapshot;
    }
}

/// <summary>A snapshot file that cannot be read. Carries a message meant for the user.</summary>
public sealed class SnapshotFormatException : Exception
{
    public SnapshotFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
```

- [ ] **Step 4: Run the test to see it pass**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~StudioSetSnapshotTests"`

Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/StudioSetSnapshot.cs Tests/TestStudioSetSnapshot.cs
git commit -m "feat: a file format for Studio Set snapshots"
```

## Task 2: Reject files this build cannot read

**Files:**
- Modify: `Tests/TestStudioSetSnapshot.cs`

- [ ] **Step 1: Write the failing tests**

Add to `StudioSetSnapshotTests`:

```csharp
    [Test]
    public void Rejects_a_future_format_version()
    {
        var json = StudioSetSnapshot.ToJson(Sample() with { FormatVersion = 99 });

        var e = Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson(json));
        Assert.That(e!.Message, Does.Contain("99"));
    }

    [Test]
    public void Rejects_something_that_is_not_a_snapshot()
    {
        Assert.Throws<SnapshotFormatException>(() => StudioSetSnapshot.FromJson("not json at all"));
    }

    [Test]
    public void Keeps_parameters_in_the_order_they_were_captured()
    {
        // Restoring depends on this: a discriminator has to be applied before the parameters that
        // only exist because of its value.
        var ordered = new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, "x",
        [
            new SnapshotDomain("s", "o", "o2",
            [
                new SnapshotValue("Studio Set Common Chorus/Chorus Type", "Delay"),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 1/Delay Left (ms-note)", "ms"),
                new SnapshotValue("Studio Set Common Chorus/Chorus Parameter 2/Delay Left ms", "120"),
            ]),
        ]);

        var restored = StudioSetSnapshot.FromJson(StudioSetSnapshot.ToJson(ordered));

        Assert.That(restored.Domains[0].Values.ConvertAll(v => v.Path), Is.EqualTo(
            ordered.Domains[0].Values.ConvertAll(v => v.Path)));
    }
```

- [ ] **Step 2: Run them**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~StudioSetSnapshotTests"`

Expected: PASS, 4 tests — Task 1's implementation already covers these. If `Rejects_something_that_is_not_a_snapshot` fails with a raw `JsonException`, the `catch` in `FromJson` is missing; add it.

- [ ] **Step 3: Commit**

```bash
git add Tests/TestStudioSetSnapshot.cs
git commit -m "test: pin snapshot version rejection and capture ordering"
```

## Task 3: The list of domains that make up a Studio Set

**Files:**
- Create: `Src/Models/Services/StudioSetDomainNames.cs`
- Modify: `Tests/TestStudioSetSnapshot.cs`

- [ ] **Step 1: Write the failing test**

Add a second fixture to the same test file:

```csharp
public class StudioSetDomainNamesTests
{
    [Test]
    public void Lists_five_common_blocks_and_three_per_part()
    {
        var names = StudioSetDomainNames.All;

        Assert.That(names, Has.Count.EqualTo(5 + 3 * 16));
        Assert.That(names[0], Is.EqualTo(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Common")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part 16")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set Part EQ 1")));
        Assert.That(names, Has.Member(
            ("Temporary Studio Set", "Offset/Not Used", "Offset2/Studio Set MIDI Channel 1")));
    }

    [Test]
    public void Has_no_duplicates()
    {
        Assert.That(StudioSetDomainNames.All, Is.Unique);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~StudioSetDomainNamesTests"`

Expected: FAIL — `StudioSetDomainNames` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Every parameter block that makes up a Studio Set, as the three address names that resolve one
/// back to a live domain via <c>Integra7Domain.GetDomain</c>. Plain strings, so the composition of a
/// Studio Set is testable without a device.
///
/// The order is the order a snapshot is captured and restored in: the common blocks first, then each
/// part. Within a block the parameter order comes from the block itself (address order).
/// </summary>
public static class StudioSetDomainNames
{
    public const int PartCount = 16;
    private const string Start = "Temporary Studio Set";
    private const string Offset = "Offset/Not Used";

    public static IReadOnlyList<(string Start, string Offset, string Offset2)> All { get; } = Build();

    private static List<(string, string, string)> Build()
    {
        List<(string, string, string)> names =
        [
            (Start, Offset, "Offset2/Studio Set Common"),
            (Start, Offset, "Offset2/Studio Set Common Chorus"),
            (Start, Offset, "Offset2/Studio Set Common Reverb"),
            (Start, Offset, "Offset2/Studio Set Common Motional Surround"),
            (Start, Offset, "Offset2/Studio Set Master EQ"),
        ];

        for (var part = 1; part <= PartCount; part++)
        {
            names.Add((Start, Offset, $"Offset2/Studio Set Part {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set Part EQ {part}"));
            names.Add((Start, Offset, $"Offset2/Studio Set MIDI Channel {part}"));
        }

        return names;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj --filter "FullyQualifiedName~StudioSetDomainNamesTests"`

Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add Src/Models/Services/StudioSetDomainNames.cs Tests/TestStudioSetSnapshot.cs
git commit -m "feat: name the domains a Studio Set is made of"
```

## Task 4: Capture and restore against the device

**Files:**
- Create: `Src/Models/Services/StudioSetSnapshotService.cs`

There is no unit test for this task: it is a thin loop over `DomainBase`, which needs a device or a
large fake to construct. The logic worth testing (format, ordering, domain list) is already covered
by Tasks 1–3. It is verified on hardware in Task 7.

- [ ] **Step 1: Write the implementation**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Reads a whole Studio Set off the instrument into a <see cref="StudioSetSnapshot"/>, and writes one
/// back. Both take a lease: a capture that another flow wrote into halfway through would record a
/// Studio Set that never existed, and a restore interleaved with anything else would produce one.
/// See docs/MIDI_DEVICE_ACCESS.md.
/// </summary>
public static class StudioSetSnapshotService
{
    public static async Task<StudioSetSnapshot> CaptureAsync(Integra7Domain domain, string name, IMidiLease lease)
    {
        List<SnapshotDomain> blocks = [];
        foreach (var (start, offset, offset2) in StudioSetDomainNames.All)
        {
            var d = domain.GetDomain(start, offset, offset2);
            await d.ReadFromIntegraAsync(lease);

            List<SnapshotValue> values = [];
            foreach (var p in d.GetRelevantParameters())
                values.Add(new SnapshotValue(p.ParSpec.Path, p.StringValue));

            blocks.Add(new SnapshotDomain(start, offset, offset2, values));
        }

        Log.Information("Captured Studio Set snapshot '{Name}': {Blocks} block(s).", name, blocks.Count);
        return new StudioSetSnapshot(StudioSetSnapshot.CurrentFormatVersion, name, blocks);
    }

    public static async Task RestoreAsync(Integra7Domain domain, StudioSetSnapshot snapshot, IMidiLease lease)
    {
        foreach (var block in snapshot.Domains)
        {
            var d = domain.GetDomain(block.Start, block.Offset, block.Offset2);

            // In captured order, which is address order: a discriminator is applied before the
            // parameters that only exist because of its value.
            foreach (var v in block.Values)
                d.ModifySingleParameterDisplayedValue(v.Path, v.Value);

            // One transmission for the whole block rather than one per parameter.
            await d.WriteToIntegraAsync(lease);
        }

        Log.Information("Restored Studio Set snapshot '{Name}': {Blocks} block(s).", snapshot.Name,
            snapshot.Domains.Count);
    }
}
```

- [ ] **Step 2: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj`

Expected: Build succeeded. If `Src\bin` is locked, build with `-p:OutputPath=<scratch>/` — see the
memory note on locked build output.

- [ ] **Step 3: Commit**

```bash
git add Src/Models/Services/StudioSetSnapshotService.cs
git commit -m "feat: capture and restore a Studio Set over one conversation"
```

## Task 5: File pickers, as interactions

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/Views/MainWindow.axaml.cs:62`

The view model must not touch `TopLevel`. `ShowSaveUserToneDialog` already establishes the pattern:
declare a ReactiveUI `Interaction`, register a handler in the view.

- [ ] **Step 1: Declare the interactions in the view model**

Next to `ShowSaveUserToneDialog` (declared around line 64, constructed around line 660):

```csharp
    /// <summary>Ask the view for a path to write a snapshot to. Null means the user cancelled.</summary>
    public Interaction<string /*suggested file name*/, string?> ShowSaveSnapshotDialog { get; }

    /// <summary>Ask the view for a snapshot to read. Null means the user cancelled.</summary>
    public Interaction<System.Reactive.Unit, string?> ShowOpenSnapshotDialog { get; }
```

and in the constructor, beside the existing one:

```csharp
        ShowSaveSnapshotDialog = new Interaction<string, string?>();
        ShowOpenSnapshotDialog = new Interaction<System.Reactive.Unit, string?>();
```

- [ ] **Step 2: Register the handlers in the view**

In `Src/Views/MainWindow.axaml.cs`, beside the existing registration at line 62:

```csharp
            action(ViewModel!.ShowSaveSnapshotDialog.RegisterHandler(async ctx =>
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Studio Set snapshot",
                    SuggestedFileName = ctx.Input,
                    DefaultExtension = "json",
                    FileTypeChoices = [SnapshotFileType],
                });
                ctx.SetOutput(file?.TryGetLocalPath());
            }));

            action(ViewModel!.ShowOpenSnapshotDialog.RegisterHandler(async ctx =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open Studio Set snapshot",
                    AllowMultiple = false,
                    FileTypeFilter = [SnapshotFileType],
                });
                ctx.SetOutput(files.Count > 0 ? files[0].TryGetLocalPath() : null);
            }));
```

and, as a field on the class:

```csharp
    private static readonly FilePickerFileType SnapshotFileType =
        new("Studio Set snapshot") { Patterns = ["*.json"] };
```

Add `using Avalonia.Platform.Storage;` at the top of the file.

- [ ] **Step 3: Build**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Src/Integra7AuralAlchemist.csproj`

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/Views/MainWindow.axaml.cs
git commit -m "feat: file pickers for Studio Set snapshots"
```

## Task 6: The commands and the buttons

**Files:**
- Modify: `Src/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/Views/MainWindow.axaml`

- [ ] **Step 1: Add the commands to the view model**

Beside `SaveUserTone` (around line 67):

```csharp
    [ReactiveCommand]
    public async Task SaveStudioSetAsync()
    {
        UserActionLog.Action("button: Save Studio Set");
        if (Integra7 is null || _integra7Communicator is null) return;

        var suggested = _integra7Communicator.StudioSetCommon
            .LookupSingleParameterDisplayedValue("Studio Set Common/Studio Set Name").Trim();
        if (suggested.Length == 0) suggested = "Studio Set";

        var path = await ShowSaveSnapshotDialog.Handle($"{suggested}.json");
        if (path is null) return;

        try
        {
            SignalStartSync();
            SyncInfo = "Reading Studio Set";
            StudioSetSnapshot snapshot;
            await using (var lease = await Integra7.BeginConversationAsync("capture Studio Set"))
                snapshot = await StudioSetSnapshotService.CaptureAsync(_integra7Communicator, suggested, lease);

            await File.WriteAllTextAsync(path, StudioSetSnapshot.ToJson(snapshot));
            UserActionLog.Action($"saved Studio Set snapshot to '{path}'");
        }
        catch (Exception e)
        {
            UserActionLog.Failed("save Studio Set snapshot", e.ToString());
        }
        finally
        {
            SignalStopSync();
        }
    }

    [ReactiveCommand]
    public async Task LoadStudioSetAsync()
    {
        UserActionLog.Action("button: Load Studio Set");
        if (Integra7 is null || _integra7Communicator is null) return;

        var path = await ShowOpenSnapshotDialog.Handle(System.Reactive.Unit.Default);
        if (path is null) return;

        try
        {
            SignalStartSync();
            SyncInfo = "Writing Studio Set";
            var snapshot = StudioSetSnapshot.FromJson(await File.ReadAllTextAsync(path));

            await using (var lease = await Integra7.BeginConversationAsync("restore Studio Set"))
                await StudioSetSnapshotService.RestoreAsync(_integra7Communicator, snapshot, lease);

            UserActionLog.Action($"restored Studio Set snapshot from '{path}'");
        }
        catch (SnapshotFormatException e)
        {
            UserActionLog.Failed("load Studio Set snapshot", e.Message);
        }
        catch (Exception e)
        {
            UserActionLog.Failed("load Studio Set snapshot", e.ToString());
        }
        finally
        {
            SignalStopSync();
        }

        // Everything on the device has just changed. Same reasoning as a Studio Set selected on the
        // front panel — see StudioSetSelectors.
        await ResyncAllPartsAsync();
    }
```

Add `using System.IO;` if it is not already present.

- [ ] **Step 2: Add the buttons**

In `Src/Views/MainWindow.axaml`, in the horizontal `StackPanel` that already holds Rescan / Play Note
/ Panic / Save User Tone, after the `SaveUserTone` button:

```xml
                        <Button Command="{Binding SaveStudioSetAsync}">
                            <Button.IsEnabled>
                                <MultiBinding Converter="{x:Static BoolConverters.And}">
                                    <MultiBinding.Bindings>
                                        <Binding Path="Connected" />
                                        <Binding Path="!IsSyncing" />
                                    </MultiBinding.Bindings>
                                </MultiBinding>
                            </Button.IsEnabled>
                            Save Studio Set…
                        </Button>
                        <Button Command="{Binding LoadStudioSetAsync}">
                            <Button.IsEnabled>
                                <MultiBinding Converter="{x:Static BoolConverters.And}">
                                    <MultiBinding.Bindings>
                                        <Binding Path="Connected" />
                                        <Binding Path="!IsSyncing" />
                                    </MultiBinding.Bindings>
                                </MultiBinding>
                            </Button.IsEnabled>
                            Load Studio Set…
                        </Button>
```

- [ ] **Step 3: Build and run the whole suite**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Tests/Tests.csproj`

Expected: all green (or the known 10 `parameters.bin` path failures if you redirected the output
path — see the memory note; check the message, not just the count).

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/MainWindowViewModel.cs Src/Views/MainWindow.axaml
git commit -m "feat: save and load a Studio Set from the toolbar"
```

## Task 7: Hardware verification

**Files:** none — this is a manual pass, and the point of the whole plan.

- [ ] **Step 1: Round-trip an untouched Studio Set**

With the instrument connected: pick a factory Studio Set, **Save Studio Set…**, then **Load Studio
Set…** on the same file. Nothing should audibly change, and the UI should come back showing the same
values.

- [ ] **Step 2: Round-trip an edited one**

Change several things across different blocks — a part's level and pan, the chorus type, a part EQ
band, the master EQ, the Studio Set tempo. Save. Select a different Studio Set on the front panel.
Load the file back. Every edit should return.

- [ ] **Step 3: Check the conditional parameters specifically**

Set the chorus to **Delay**, set its delay to a note value rather than milliseconds, save, switch the
chorus to **Off**, then restore. The delay parameters must come back — this is the case the capture
ordering exists for.

- [ ] **Step 4: Check the file**

Open the saved `.json`. It should be readable, one block per domain, parameters in address order,
displayed values as the UI shows them.

- [ ] **Step 5: Commit anything the pass fixed, then finish the branch**

Use `superpowers:finishing-a-development-branch`.

---

## Known limitations to state in the UI or the commit message

- **Tones are referenced, not stored.** Parts point at tones by bank and program number. Restoring
  onto an instrument whose user tones have since changed will recall different sounds.
- **Restore is immediate and destructive** — it overwrites the Studio Set currently in the
  instrument's temporary memory. It does not touch the instrument's stored user Studio Sets.
- **No `.syx` interchange yet.** The format is this application's own. An importer/exporter for raw
  sysex is a separate piece of work.
