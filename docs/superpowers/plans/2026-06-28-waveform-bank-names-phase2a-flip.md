# Waveform Bank Names — Phase 2a (the flip) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn on Group-Type/ID-aware waveform names so both the advanced parameter tabs and the friendly editors show the **correct bank-aware names on load** — by routing both surfaces through the per-parameter `EffectiveRepr` (set by the Phase-1 resolution pass) and retiring the flat `PARTIAL_WAVEFORMS`.

**Architecture:** One lever — `EffectiveRepr ?? ParSpec.Repr`. `ParamString` (friendly) exposes a reactive `Options` that reflects `EffectiveRepr`; the advanced grid (`DataTemplateProvider`) and the reverse converter use the same fallback. The domain read invokes `WaveNameResolution.Apply` (Phase 1) to set each wave param's `EffectiveRepr` + correct `StringValue`. Then the now-bypassed `PARTIAL_WAVEFORMS` is removed.

**Tech Stack:** Avalonia 12 + ReactiveUI; the Phase-1 `WaveformBanks`/`WaveNameResolution`/`EffectiveRepr`. Build/test with the user-local SDK in Release (Debug exe is file-locked; MSB3027/MSB3021 = lock, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

**Testing note:** Phase 2a is integration wiring over live FQP/domain instances (no new pure logic — that was unit-tested in Phase 1; FQPs can't be cheaply fabricated). Each task verifies by **Release build succeeding** and the **257-test suite staying green**. Tasks are ordered so every step leaves a green build; the user-visible flip happens at Task 3.

---

### Task 1: Make `ParamString.Options` reactive + `EffectiveRepr`-aware

**Files:**
- Modify: `Src/ViewModels/SynthParam.cs` (the `ParamString` class)

So every friendly wave picker (which binds `…Options`/`…Value`) reflects the selected bank and refreshes when the wave param is re-read (e.g. after a Group-Type/ID change, which re-reads via `IsParent`). Additive: with `EffectiveRepr` null (all current params), `Options` equals the static list exactly as today.

- [ ] **Step 1: Replace the `Options` field + constructor init**

In `Src/ViewModels/SynthParam.cs`, find this constructor body fragment in `ParamString`:

```csharp
        _domain = domain; _p = p; _writer = writer;
        _key = $"{domain.StartAddressName}|{domain.Offset2AddressName}|{p.ParSpec.Path}";
        Options = options
            ?? (p.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                ?? new List<string>());
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
```

Replace it with:

```csharp
        _domain = domain; _p = p; _writer = writer;
        _key = $"{domain.StartAddressName}|{domain.Offset2AddressName}|{p.ParSpec.Path}";
        _staticOptions = options
            ?? (p.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                ?? new List<string>());
        _options = _staticOptions;
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
```

- [ ] **Step 2: Replace the `Options` property declaration**

Find:

```csharp
    public string Path => _p.ParSpec.Path;
    public IReadOnlyList<string> Options { get; }
```

Replace with:

```csharp
    public string Path => _p.ParSpec.Path;

    private readonly IReadOnlyList<string> _staticOptions;
    private IReadOnlyList<string> _options;
    /// <summary>Allowed values. For a bank-selected parameter this reflects the FQP's EffectiveRepr and
    /// refreshes when the parameter is re-read; otherwise it is the static repr/explicit list.</summary>
    public IReadOnlyList<string> Options { get => _options; private set => this.RaiseAndSetIfChanged(ref _options, value); }
```

- [ ] **Step 3: Refresh `Options` from `EffectiveRepr` in `ApplyFromModel`**

Find:

```csharp
    private void ApplyFromModel()
    {
        _suppress = true;
        try { Value = _p.StringValue; }
        finally { _suppress = false; }
    }
```

Replace with:

```csharp
    private void ApplyFromModel()
    {
        _suppress = true;
        try
        {
            Value = _p.StringValue;
            Options = _p.EffectiveRepr is { } er
                ? er.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                : _staticOptions;
        }
        finally { _suppress = false; }
    }
```

- [ ] **Step 4: Build + test**

Run the Build command (expect `Build succeeded.` 0 errors), then the Test command (expect `Passed! - Failed: 0, Passed: 257`). No behavior change yet (`EffectiveRepr` is null everywhere until Task 3).

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/SynthParam.cs
git commit -m "feat: ParamString.Options reactive + EffectiveRepr-aware"
```

---

### Task 2: Advanced grid + reverse converter use `EffectiveRepr`

**Files:**
- Modify: `Src/DataTemplates/DataTemplateProvider.cs`
- Modify: `Src/Models/Services/DisplayValueToRawValueConverter.cs`

Additive (EffectiveRepr null → identical to today).

- [ ] **Step 1: `DataTemplateProvider` — use the effective repr for the combo/toggle branch**

In `Src/DataTemplates/DataTemplateProvider.cs`, find:

```csharp
        if (p.ParSpec.Repr != null)
        {
            if (p.ParSpec.Repr.Count == 2 && p.ParSpec.Repr[0].ToUpper() == "OFF" &&
                p.ParSpec.Repr[1].ToUpper() == "ON")
            {
                ToggleSwitch c = new();
                c.IsChecked = p.StringValue == p.ParSpec.Repr[1];
                c.IsCheckedChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    if (s is ToggleSwitch checkBox)
                    {
                        var msg = "OFF";
                        if (checkBox?.IsChecked ?? false) msg = "ON";
                        MessageBus.Current.SendMessage(new UpdateMessageSpec(p, msg), "ui2hw");
                    }
                };
                BindToModel(c, p, () => c.IsChecked = p.StringValue == p.ParSpec.Repr[1],
                    () => suppressPush, v => suppressPush = v);
                return c;
            }
            else
            {
                ComboBox c = new();
                foreach (var el in p.ParSpec.Repr) c.Items.Add(el.Value);
                c.SelectedItem = p.StringValue;
                c.SelectionChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
                };
                BindToModel(c, p, () => c.SelectedItem = p.StringValue, () => suppressPush, v => suppressPush = v);
                return c;
            }
        }
```

Replace it with (introduce `repr = p.EffectiveRepr ?? p.ParSpec.Repr` and use it throughout):

```csharp
        var repr = p.EffectiveRepr ?? p.ParSpec.Repr;
        if (repr != null)
        {
            if (repr.Count == 2 && repr[0].ToUpper() == "OFF" && repr[1].ToUpper() == "ON")
            {
                ToggleSwitch c = new();
                c.IsChecked = p.StringValue == repr[1];
                c.IsCheckedChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    if (s is ToggleSwitch checkBox)
                    {
                        var msg = "OFF";
                        if (checkBox?.IsChecked ?? false) msg = "ON";
                        MessageBus.Current.SendMessage(new UpdateMessageSpec(p, msg), "ui2hw");
                    }
                };
                BindToModel(c, p, () => c.IsChecked = p.StringValue == repr[1],
                    () => suppressPush, v => suppressPush = v);
                return c;
            }
            else
            {
                ComboBox c = new();
                foreach (var el in repr) c.Items.Add(el.Value);
                c.SelectedItem = p.StringValue;
                c.SelectionChanged += (s, e) =>
                {
                    if (suppressPush) return;
                    MessageBus.Current.SendMessage(new UpdateMessageSpec(p, $"{e.AddedItems[0]}"), "ui2hw");
                };
                BindToModel(c, p, () => c.SelectedItem = p.StringValue, () => suppressPush, v => suppressPush = v);
                return c;
            }
        }
```

(Note: the trailing numeric-slider branches after this block remain unchanged — they run when `repr` is null, i.e. a wave param before its domain is read. That fallback is acceptable.)

- [ ] **Step 2: `DisplayValueToRawValueConverter` — reverse via the effective repr**

In `Src/Models/Services/DisplayValueToRawValueConverter.cs`, find:

```csharp
        if (p.ParSpec.Repr != null)
        {
            var key = p.ParSpec.Repr
                .Where(keyvaluepair => keyvaluepair.Value == displayValue)
                .Select(keyvaluepair => keyvaluepair.Key)
                .ToList();
```

Replace with:

```csharp
        var repr = p.EffectiveRepr ?? p.ParSpec.Repr;
        if (repr != null)
        {
            var key = repr
                .Where(keyvaluepair => keyvaluepair.Value == displayValue)
                .Select(keyvaluepair => keyvaluepair.Key)
                .ToList();
```

(Leave the rest of that block — the `key.Count == 0` / `p.RawNumericValue = key.First()` logic — unchanged; it already references the local `key`. If the block has any other `p.ParSpec.Repr` reference in its debug message, it may stay as-is.)

- [ ] **Step 3: Build + test**

Run the Build command (expect 0 errors), then the Test command (expect `Passed! - Failed: 0, Passed: 257`). No behavior change yet.

- [ ] **Step 4: Commit**

```bash
git add Src/DataTemplates/DataTemplateProvider.cs Src/Models/Services/DisplayValueToRawValueConverter.cs
git commit -m "feat: advanced grid + reverse converter honor EffectiveRepr"
```

---

### Task 3: Invoke the resolution pass on domain read (the flip)

**Files:**
- Modify: `Src/Models/Domain/DomainBase.cs`

This sets each wave param's `EffectiveRepr` + correct `StringValue` after every read — turning on correct names in both surfaces.

- [ ] **Step 1: Call `WaveNameResolution.Apply` after parsing**

In `Src/Models/Domain/DomainBase.cs`, find the end of `ReadFromIntegraAsync`:

```csharp
        await r.RetrieveFromIntegraAsync(_integra7Api, _startAddresses, _parameters);
        for (var i = 0; i < r.Range.Count; i++) _domainParameters[i].CopyParsedDataFrom(r.Range[i]);
    }
```

Replace with:

```csharp
        await r.RetrieveFromIntegraAsync(_integra7Api, _startAddresses, _parameters);
        for (var i = 0; i < r.Range.Count; i++) _domainParameters[i].CopyParsedDataFrom(r.Range[i]);
        // Resolve bank-selected waveform names (no-op for domains without wave params).
        WaveNameResolution.Apply(_domainParameters, WaveformBanks.Default);
    }
```

(`DomainBase` already has `using Integra7AuralAlchemist.Models.Services;`; `_domainParameters` is a `List<FullyQualifiedParameter>` which satisfies the `IReadOnlyList<FullyQualifiedParameter>` parameter. `WaveformBanks.Default` lazily loads the 13 CSV assets on first use.)

- [ ] **Step 2: Build + test**

Run the Build command (expect 0 errors), then the Test command (expect `Passed! - Failed: 0, Passed: 257`). (No test reads a domain from hardware, so `Apply`/`Default` are not exercised by the suite; the blob/load path is unaffected.)

- [ ] **Step 3: Commit**

```bash
git add Src/Models/Domain/DomainBase.cs
git commit -m "feat: resolve bank-aware waveform names on domain read"
```

At this point names are correct in both surfaces on load; `PARTIAL_WAVEFORMS` is still attached but bypassed by `EffectiveRepr`. Task 4 removes it.

---

### Task 4: Retire `PARTIAL_WAVEFORMS`

**Files:**
- Modify: `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`

Remove the now-unused flat list: the 10 `repr:PARTIAL_WAVEFORMS` tags, the build-time load, and the field.

- [ ] **Step 1: Drop the repr from the 10 wave-number params**

In `Tools/ParameterBlobGenerator/ParameterDefinitions.cs`, change `repr:PARTIAL_WAVEFORMS` to `repr:null` on exactly these 10 lines (identified by path):

```
PCM Synth Tone Partial/Wave Number L (Mono)
PCM Synth Tone Partial/Wave Number R
PCM Drum Kit Partial/WMT1 Wave Number L (Mono)
PCM Drum Kit Partial/WMT1 Wave Number R
PCM Drum Kit Partial/WMT2 Wave Number L (Mono)
PCM Drum Kit Partial/WMT2 Wave Number R
PCM Drum Kit Partial/WMT3 Wave Number L (Mono)
PCM Drum Kit Partial/WMT3 Wave Number R
PCM Drum Kit Partial/WMT4 Wave Number L (Mono)
PCM Drum Kit Partial/WMT4 Wave Number R
```

For example:
```csharp
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Number L (Mono)", offs:[0x00, 0x2c], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:PARTIAL_WAVEFORMS),
```
becomes (only the final field changes):
```csharp
            new(type:NUM, path:"PCM Synth Tone Partial/Wave Number L (Mono)", offs:[0x00, 0x2c], imin:0, imax:16384, omin:0, omax:16384, bytes:4, res:USED, nib:true, unit:"", repr:null),
```

- [ ] **Step 2: Remove the build-time load**

Find and delete this line:
```csharp
        LoadWaveFormHelper(assetsDir, "PartialWaveForms_INT.csv", PARTIAL_WAVEFORMS);
```

- [ ] **Step 3: Remove the field declaration**

Find and delete this line:
```csharp
    public readonly IDictionary<int, string> PARTIAL_WAVEFORMS = new Dictionary<int, string>();
```

Then search the file for any remaining `PARTIAL_WAVEFORMS` reference — there must be **zero** (the 10 tags + load + field are the only ones). If any remain, the build will fail; fix by removing them. (Note: `Src/Assets/PartialWaveForms_INT.csv` stays — it is still shipped and loaded at runtime by `WaveformBanks`.)

- [ ] **Step 4: Build (regenerates the blob) + test**

Run the Build command. Expect `Build succeeded.` 0 errors with a `GenerateParameterBlob: producing …parameters.bin` message. Then the Test command (expect `Passed! - Failed: 0, Passed: 257`). If the build fails with `PARTIAL_WAVEFORMS` not found, a reference was missed — remove it.

- [ ] **Step 5: Commit**

```bash
git add Tools/ParameterBlobGenerator/ParameterDefinitions.cs
git commit -m "refactor: retire flat PARTIAL_WAVEFORMS (replaced by bank-aware EffectiveRepr)"
```

---

### Task 5: Mark the known issue resolved

**Files:**
- Modify: `docs/KNOWN_ISSUES.md`

- [ ] **Step 1: Update the status**

In `docs/KNOWN_ISSUES.md`, change the waveform-name issue's `**Status:**` line from the "Open — deferred" text to:

```markdown
**Status:** Resolved (2026-06-28). Wave names now resolve per Wave Group Type/ID via the per-bank lists (`PartialWaveForms_{INT,SRX1..12}.csv`) loaded by the `WaveformBanks` service and surfaced through `FullyQualifiedParameter.EffectiveRepr` in both the advanced tabs and the friendly editors. (Bank-switch-in-editor reactivity + the out-of-range reset land in Phase 2b.)
```

- [ ] **Step 2: Commit**

```bash
git add docs/KNOWN_ISSUES.md
git commit -m "docs: mark waveform name list issue resolved (Phase 2a flip)"
```

---

## Done criteria

Loading a PCM Synth tone or PCM Drum kit shows **correct, bank-aware wave names** (INT or the right SRX board) in both the friendly wave pickers and the advanced parameter tabs, and editing a wave within the current bank writes the right wave number. `PARTIAL_WAVEFORMS` is gone; names flow entirely from `WaveformBanks` via `EffectiveRepr`. Build green, 257 tests passing. Phase 2b adds: advanced raw-grid live refresh when Group Type/ID change mid-edit, the out-of-range reset to the first wave on bank switch (both surfaces), and an SRX-board (Group ID) selector in the friendly editors so users can switch banks there.
