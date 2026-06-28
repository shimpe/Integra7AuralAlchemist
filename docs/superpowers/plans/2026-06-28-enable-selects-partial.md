# Enabling a Partial Selects It (PCM-Synth + SN-S) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Toggling a partial's on/off switch on a PCM-Synth or SN-S partial card makes it the selected partial, mirroring the existing "solo selects the partial" behavior, without the audition machinery or preset loads moving the selection.

**Architecture:** Add an `Enabled` view-model property on each partial VM that wraps `IsOn.Value` and selects the partial on set (the user path only); bind the card's on/off `ToggleSwitch` to it and forward `IsOn` changes back so the switch stays in sync. Programmatic `IsOn.Value` changes never call the setter, so they never move the selection.

**Tech Stack:** Avalonia 12 + ReactiveUI; C# / .NET 10.

**Build/test commands** (user-local .NET 10 SDK; the system `dotnet` is too old):
- Build: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Full tests: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release` → expect `Passed! - Failed: 0, Passed: 281` (no new tests; confirm no regressions).

**Standing constraints:** never `git --no-verify`; this work is on branch `enable-selects-partial` (already created off `main`). Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Transient `Permission denied` on `.git/objects` (Windows AV) — retry the commit once. There is an unrelated uncommitted change to `Src/Models/Services/AsyncMidiInputWrapper.cs` — **do NOT stage or commit it**; use explicit file paths in every `git add`.

---

## File Structure

- **Modify** `Src/ViewModels/PCMPartialViewModel.cs` and `Src/ViewModels/SNSPartialViewModel.cs` — add the `Enabled` property + `IsOn` change-forward (subscribe in ctor, unsubscribe in `Dispose`).
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` and `Src/Views/SNSynthToneEditorView.axaml` — bind the on/off `ToggleSwitch` `IsChecked` to `Enabled`.

No other changes; the audition logic and `ParamBool` are untouched.

---

## Task 1: `Enabled` property on both partial VMs

> **Note on testing:** mirrors the unit-untested `Solo` setter; no headless-UI harness. Verification is build + full suite green (after Task 2, manual). No new unit test.

**Files:**
- Modify: `Src/ViewModels/PCMPartialViewModel.cs`, `Src/ViewModels/SNSPartialViewModel.cs`

### `PCMPartialViewModel.cs`

- [ ] **Step 1a: Add the `Enabled` property.** Find the `IsOn` declaration `public ParamBool IsOn { get; }`. Immediately after it, add:

```csharp

    /// <summary>The card on/off switch as a bindable property. A USER toggle (target→source binding)
    /// runs this setter, which also selects this partial — mirroring <see cref="Solo"/>. Programmatic
    /// IsOn.Value changes (audition recompute, preset load) go through IsOn.Value directly and never call
    /// this setter, so they don't move the selection. The switch stays in sync via OnIsOnChanged.</summary>
    public bool Enabled
    {
        get => IsOn.Value;
        set { IsOn.Value = value; _parent.SelectedPartial = this; }
    }
```

- [ ] **Step 1b: Subscribe in the ctor.** Find the block of summary subscriptions ending with `TvfCutoff.PropertyChanged += OnSummaryChanged;` (currently the last of: WaveGroupType / WaveNumberL / PartialLevel / PartialPan / TvfFilterType / TvfCutoff). Add this line immediately after it:

```csharp
        IsOn.PropertyChanged += OnIsOnChanged;
```

- [ ] **Step 1c: Add the forward handler.** Immediately after the existing `OnSummaryChanged` method (the one that raises the card-summary properties), add:

```csharp

    private void OnIsOnChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParamBool.Value)) this.RaisePropertyChanged(nameof(Enabled));
    }
```

- [ ] **Step 1d: Unsubscribe in `Dispose`.** In `Dispose`, after the line `TvfCutoff.PropertyChanged -= OnSummaryChanged;` (the last summary unsubscribe, before `foreach (var w in _wrappers)`), add:

```csharp
        IsOn.PropertyChanged -= OnIsOnChanged;
```

### `SNSPartialViewModel.cs`

- [ ] **Step 1e: Add the `Enabled` property.** Find `public ParamBool IsOn { get; }` (under the `// --- Card on/off …` comment). Immediately after it, add:

```csharp

    /// <summary>The card on/off switch as a bindable property. A USER toggle (target→source binding)
    /// runs this setter, which also selects this partial — mirroring <see cref="Solo"/>. Programmatic
    /// IsOn.Value changes (audition recompute, preset load) go through IsOn.Value directly and never call
    /// this setter, so they don't move the selection. The switch stays in sync via OnIsOnChanged.</summary>
    public bool Enabled
    {
        get => IsOn.Value;
        set { IsOn.Value = value; _parent.SelectedPartial = this; }
    }
```

- [ ] **Step 1f: Subscribe in the ctor.** Find the existing `…PropertyChanged += OnSummaryChanged;` subscriptions in the constructor (e.g. `AmpPan.PropertyChanged += OnSummaryChanged;`). Add, right after that `AmpPan` line:

```csharp
        IsOn.PropertyChanged += OnIsOnChanged;
```

- [ ] **Step 1g: Add the forward handler.** Add this method next to the other `On…Changed` handlers in the class:

```csharp

    private void OnIsOnChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParamBool.Value)) this.RaisePropertyChanged(nameof(Enabled));
    }
```

- [ ] **Step 1h: Unsubscribe in `Dispose`.** In `Dispose`, after the `AmpPan.PropertyChanged -= OnSummaryChanged;` line, add:

```csharp
        IsOn.PropertyChanged -= OnIsOnChanged;
```

(Both files already `using System.ComponentModel;` for the existing `OnSummaryChanged(object?, PropertyChangedEventArgs)` handlers, and `ParamBool` is in the same `Integra7AuralAlchemist.ViewModels` namespace — so `PropertyChangedEventArgs` and `nameof(ParamBool.Value)` resolve with no new usings.)

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`. (The new property compiles even before the views use it.)

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/PCMPartialViewModel.cs Src/ViewModels/SNSPartialViewModel.cs
git commit -m "feat: Enabled property selects the partial on a user on/off toggle

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Bind the card on/off switch to `Enabled`

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`, `Src/Views/SNSynthToneEditorView.axaml`

> **Note on testing:** XAML binding change; verification is build + manual. No unit test.

- [ ] **Step 1: Repoint the bindings.** In `Src/Views/PCMSynthToneEditorView.axaml` (the partial-card `ToggleSwitch`, around line 171) and `Src/Views/SNSynthToneEditorView.axaml` (around line 161), change the on/off switch's `IsChecked` binding from `IsOn.Value` to `Enabled`:

  In **both** files, replace:

```xml
                                                      IsChecked="{Binding IsOn.Value, Mode=TwoWay}"
```

  with:

```xml
                                                      IsChecked="{Binding Enabled, Mode=TwoWay}"
```

  (Each file has exactly one such line — the partial card's on/off `ToggleSwitch`, which also carries `IsEnabled="{Binding !…IsAuditioning}"` and `ToolTip.Tip="Partial on/off …"`. The surrounding `IsEnabled` audition-gate and tooltip are unchanged.)

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Views/PCMSynthToneEditorView.axaml Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat: bind partial card on/off switch to Enabled (selects on toggle)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with the app running):**
- In a PCM-Synth or SN-S tone, toggling a partial's on/off switch (on→off or off→on) selects that partial (its card highlights, the Sound tab shows it).
- The switch still shows the correct on/off state after a preset load and reflects partials being muted/unmuted during solo/mute audition.
- Engaging solo/mute (which toggles partials' on/off programmatically) does **not** change which partial is selected.

---

## Final verification

- [ ] Full suite green (`Passed: 281`); `git log --oneline main..HEAD` shows the spec + Tasks 1–2 commits on `enable-selects-partial`; `git status` shows only the unrelated `AsyncMidiInputWrapper.cs` change uncommitted.

After both tasks: dispatch a final review, then use superpowers:finishing-a-development-branch.

---

## Spec coverage (self-review)

- **`Enabled` property selecting on user toggle** → Task 1 (both VMs), setter mirrors `Solo`.
- **Audition/load-safe** → the setter is only reached via the card's two-way binding; programmatic `IsOn.Value` changes bypass it. The `IsOn.PropertyChanged → Enabled` forward keeps the switch in sync.
- **Card binding** → Task 2 (both views), `Enabled` replacing `IsOn.Value`.
- **Out of scope** (drum editors, SN-A, Mute-selects, audition logic) → untouched.
