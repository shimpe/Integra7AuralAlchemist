# PCM Drums Editor — Phase 5 (Comp-EQ) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the PCM Drum kit's **Comp-EQ** tab (the FX/MFX tab is already live from Phase 1), finishing the engine. Reuse the SN-Drums 6-unit compressor/EQ view-models by generalizing them to accept a domain-path prefix.

**Architecture:** The PCM Drum Comp-EQ block is structurally identical to SN-Drums (`Comp1..6` / `EQ1..6`, 84 params) — only the path prefix differs. Generalize `SNDrumCompEqUnitViewModel` / `SNDrumCompEqPanelViewModel` to take the prefix as a constructor argument (the SN-Drums call site passes its existing prefix; PCM drums passes its own). The PCM kit VM then exposes a `CompEq` panel that the Comp-EQ tab hosts via the ViewLocator (reusing `SNDrumCompEqPanelView`).

**Tech Stack:** Avalonia 12 + ReactiveUI, the existing `SNDrumCompEq*` VMs + `SNDrumCompEqPanelView`. Build/test with the user-local SDK in Release. Never use `--no-verify`.

**Verified facts.** PCM Drum Comp-EQ domain accessor: `domain.PCMDrumKitCompEQ(partNo)`. Path prefix: `"PCM Drum Kit Common Comp-EQ/"` (vs SN-Drums `"SuperNATURAL Drum Kit Common Comp-EQ/"`). Leaf names/ranges are identical between the two engines (`Comp{n} Switch/Attack Time/Release Time/Threshold/Ratio/Output Gain`, `EQ{n} Switch/Low Freq/Low Gain/Mid Freq/Mid Gain/Mid Q/High Freq/High Gain`).

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: Generalize the Comp-EQ view-models to take a path prefix

**Files:**
- Modify: `Src/ViewModels/SNDrumCompEqUnitViewModel.cs`
- Modify: `Src/ViewModels/SNDrumCompEqPanelViewModel.cs`
- Modify: `Src/ViewModels/SNDrumKitEditorViewModel.cs` (the existing SN-Drums call site — pass its prefix)

Pure refactor: the behavior for SN-Drums is unchanged. All three changes land together so the build stays green.

- [ ] **Step 1: Add a `pathPrefix` parameter to `SNDrumCompEqUnitViewModel`**

In `Src/ViewModels/SNDrumCompEqUnitViewModel.cs`:

Replace the summary + the `PP` const:
```csharp
/// <summary>One SN-Drums Comp-EQ unit (1..6): a compressor + a 3-band EQ over the shared Comp-EQ domain.</summary>
public sealed class SNDrumCompEqUnitViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Common Comp-EQ/";
    private readonly List<IDisposable> _wrappers = [];
```
with (drop the const; the prefix now comes from the ctor):
```csharp
/// <summary>One drum Comp-EQ unit (1..6): a compressor + a 3-band EQ. Shared by the SuperNATURAL and
/// PCM drum kits — the caller supplies the domain-path prefix.</summary>
public sealed class SNDrumCompEqUnitViewModel : ViewModelBase, IDisposable
{
    private readonly List<IDisposable> _wrappers = [];
```

Replace the constructor signature + the three local helpers:
```csharp
    public SNDrumCompEqUnitViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index)
    {
        Index = index;
        var c = $"Comp{index} ";
        var q = $"EQ{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[PP + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[PP + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[PP + n], writer));
```
with (add `string pathPrefix`, and reference it in the helpers):
```csharp
    public SNDrumCompEqUnitViewModel(DomainBase domain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer, int index,
        string pathPrefix)
    {
        Index = index;
        var c = $"Comp{index} ";
        var q = $"EQ{index} ";
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(domain, byPath[pathPrefix + n], writer, min, max));
        ParamString PS(string n, IReadOnlyList<string>? o = null) => Track(new ParamString(domain, byPath[pathPrefix + n], writer, o));
        ParamBool PB(string n) => Track(new ParamBool(domain, byPath[pathPrefix + n], writer));
```

- [ ] **Step 2: Add a `pathPrefix` parameter to `SNDrumCompEqPanelViewModel`**

In `Src/ViewModels/SNDrumCompEqPanelViewModel.cs`:

Replace the summary:
```csharp
/// <summary>The kit's Comp-EQ section: six compressor/EQ units, one shown at a time.</summary>
```
with:
```csharp
/// <summary>A drum kit's Comp-EQ section: six compressor/EQ units, one shown at a time. Shared by the
/// SuperNATURAL and PCM drum kits — the caller supplies the domain and its path prefix.</summary>
```

Replace the constructor:
```csharp
    public SNDrumCompEqPanelViewModel(DomainBase compEqDomain, ThrottledParameterWriter writer)
    {
        var byPath = ToDict(compEqDomain);
        var units = new List<SNDrumCompEqUnitViewModel>(UnitCount);
        for (var i = 1; i <= UnitCount; i++) units.Add(Track(new SNDrumCompEqUnitViewModel(compEqDomain, byPath, writer, i)));
        Units = units;
        _selectedUnit = units[0];
    }
```
with:
```csharp
    public SNDrumCompEqPanelViewModel(DomainBase compEqDomain, ThrottledParameterWriter writer, string pathPrefix)
    {
        var byPath = ToDict(compEqDomain);
        var units = new List<SNDrumCompEqUnitViewModel>(UnitCount);
        for (var i = 1; i <= UnitCount; i++) units.Add(Track(new SNDrumCompEqUnitViewModel(compEqDomain, byPath, writer, i, pathPrefix)));
        Units = units;
        _selectedUnit = units[0];
    }
```

- [ ] **Step 3: Update the SN-Drums call site to pass its prefix**

In `Src/ViewModels/SNDrumKitEditorViewModel.cs`, find:
```csharp
        CompEq = new SNDrumCompEqPanelViewModel(domain.SNDrumKitCompEQ(partNo), _writer);
```
Replace with:
```csharp
        CompEq = new SNDrumCompEqPanelViewModel(domain.SNDrumKitCompEQ(partNo), _writer, "SuperNATURAL Drum Kit Common Comp-EQ/");
```

- [ ] **Step 4: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. (Both VM ctors now require `pathPrefix`; the SN-Drums call site is updated in the same task, so nothing else breaks.) If the build fails, report BLOCKED with the exact error.

- [ ] **Step 5: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244` (the refactor must not regress SN-Drums).

- [ ] **Step 6: Commit**

```bash
git add Src/ViewModels/SNDrumCompEqUnitViewModel.cs Src/ViewModels/SNDrumCompEqPanelViewModel.cs Src/ViewModels/SNDrumKitEditorViewModel.cs
git commit -m "refactor: parameterize drum Comp-EQ VMs by domain-path prefix"
```

---

### Task 2: Wire the Comp-EQ into the PCM kit editor + tab

**Files:**
- Modify: `Src/ViewModels/PCMDrumKitEditorViewModel.cs`
- Modify: `Src/Views/PCMDrumKitEditorView.axaml`

- [ ] **Step 1: Add the `CompEq` property**

In `Src/ViewModels/PCMDrumKitEditorViewModel.cs`, find:
```csharp
    public ObservableCollection<PCMDrumNoteViewModel> Notes { get; } = [];
    public MfxPanelViewModel Mfx { get; }
```
Replace with:
```csharp
    public ObservableCollection<PCMDrumNoteViewModel> Notes { get; } = [];
    public MfxPanelViewModel Mfx { get; }
    public SNDrumCompEqPanelViewModel CompEq { get; }
```

- [ ] **Step 2: Construct `CompEq` in the ctor**

Find:
```csharp
        Mfx = new MfxPanelViewModel(domain.PCMDrumKitCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCMD-MFX", null));
    }
```
Replace with:
```csharp
        Mfx = new MfxPanelViewModel(domain.PCMDrumKitCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCMD-MFX", null));

        CompEq = new SNDrumCompEqPanelViewModel(domain.PCMDrumKitCompEQ(partNo), _writer, "PCM Drum Kit Common Comp-EQ/");
    }
```

- [ ] **Step 3: Dispose `CompEq`**

Find:
```csharp
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
```
Replace with:
```csharp
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        CompEq.Dispose();
        _writer.Dispose();
```

- [ ] **Step 4: Point the Comp-EQ tab at the panel**

In `Src/Views/PCMDrumKitEditorView.axaml`, find:
```xml
                <TabItem Header="Comp-EQ">
                    <TextBlock Margin="12" Opacity="0.6" Text="Comp-EQ — 6-unit compressor / EQ (Phase 5)."/>
                </TabItem>
```
Replace with:
```xml
                <TabItem Header="Comp-EQ">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                        <ContentControl Content="{Binding CompEq}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
```
(The ViewLocator resolves `SNDrumCompEqPanelViewModel` → the existing `SNDrumCompEqPanelView`.)

- [ ] **Step 5: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. Compiled XAML resolves `CompEq`.

- [ ] **Step 6: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 244`.

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/PCMDrumKitEditorViewModel.cs Src/Views/PCMDrumKitEditorView.axaml
git commit -m "feat: PCM-Drums Comp-EQ tab (reuses shared 6-unit comp/EQ panel)"
```

---

## Done criteria

The PCM Drum kit's **Comp-EQ** tab shows the 6-unit compressor/EQ panel (the same UI as SN-Drums), driven by the PCM drum Comp-EQ domain; the FX tab already hosts the MFX panel. **This completes the PCM Drums friendly editor** — Header, 88-key rail with click-to-audition, per-key Drum editor (Wave/Pitch/Filter/Amp/Setup), Comp-EQ, FX, and the Advanced raw tabs. Build green, 244 tests passing. Next: run the finishing-a-development-branch options.
