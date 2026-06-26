# Friendly MFX panel — nested-discriminator fix (implementation plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the friendly FX grid show the full `ValidInContext` set (honoring `MFX Type → switch → value` chains) and recompute only on discriminator changes, so delays show their actual time value and dragging a value doesn't rebuild the grid.

**Architecture:** All changes are in `Src/ViewModels/MfxPanelViewModel.cs`: source the grid from `GetRelevantParameters(false,false)`; subscribe to the discriminator (parent-referenced) FQPs and recompute on their `StringValue` change (UI-thread marshalled); decouple combo-sync from grid-recompute.

**Tech Stack:** Avalonia 12 (`Dispatcher.UIThread`), ReactiveUI, .NET 10. Build/test with `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"`. Branch: `fix-mfx-nested-discriminators` (created; spec committed).

Reference: spec `docs/superpowers/specs/2026-06-26-mfx-nested-discriminators-fix-design.md`.

---

## Task 1: Rework `MfxPanelViewModel` grid sourcing + triggers

**Files:** Modify `Src/ViewModels/MfxPanelViewModel.cs`

First READ the file to confirm the anchor lines, then apply these six surgical edits.

- [ ] **Step 1: Add usings.** After `using System.Reactive;` insert:

```csharp
using System.ComponentModel;
using Avalonia.Threading;
```

- [ ] **Step 2: Add the discriminators field.** After the line `private readonly IDisposable _typeSub;` insert:

```csharp
    private readonly List<FullyQualifiedParameter> _discriminators;
```

- [ ] **Step 3: Wire discriminator subscriptions + initial recompute in the constructor.** Replace:

```csharp
        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Fires immediately with the current type, then on every change (picker / hardware / raw tab).
        _typeSub = Type.WhenAnyValue(t => t.Value).Subscribe(OnTypeChanged);
    }
```

with:

```csharp
        AdvancedMfxCommand = ReactiveCommand.Create(navigateToAdvanced);

        // Recompute the per-type grid only when a *discriminator* (the MFX Type or a sub-switch such
        // as a delay's ms/Note selector) changes — not on every value edit, which would tear down a
        // control mid-drag. Discriminators are the params referenced as a parent by some MFX param.
        var parentPaths = _allMfxParams
            .SelectMany(p => new[] { p.ParSpec.ParentCtrl, p.ParSpec.ParentCtrl2 })
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet();
        _discriminators = _allMfxParams.Where(p => parentPaths.Contains(p.ParSpec.Path)).ToList();
        foreach (var d in _discriminators) d.PropertyChanged += OnDiscriminatorChanged;

        // Keeps the family/type pickers + Bypass in sync immediately; the grid follows via the
        // discriminator subscription once the underlying FQP settles.
        _typeSub = Type.WhenAnyValue(t => t.Value).Subscribe(OnTypeChanged);

        RecomputeTypeParameters(); // initial population
    }
```

- [ ] **Step 4: Drop the grid-recompute from `OnTypeChanged` and add the discriminator handler.** Replace:

```csharp
        finally { _syncing = false; }

        RecomputeTypeParameters(typeName);
        this.RaisePropertyChanged(nameof(Bypass));
    }
```

with:

```csharp
        finally { _syncing = false; }

        this.RaisePropertyChanged(nameof(Bypass));
    }

    private void OnDiscriminatorChanged(object? s, PropertyChangedEventArgs e)
    {
        // May fire on a MIDI / throttle-timer thread; marshal before touching TypeParameters.
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(RecomputeTypeParameters);
    }
```

- [ ] **Step 5: Replace `RecomputeTypeParameters` with the context-valid version.** Replace the entire method:

```csharp
    private void RecomputeTypeParameters(string typeName)
    {
        var relevant = _allMfxParams
            .Where(p => !p.ParSpec.Reserved
                        && p.ParSpec.Path.Contains("/MFX Parameter ")
                        && p.ParSpec.ParentCtrlDispValue == typeName)
            .OrderBy(p => p.ParSpec.AddressInt)
            .ToList();
        var labels = MfxCatalog.FriendlyParamNames(typeName, relevant.Select(p => p.ParSpec.Name).ToList());

        TypeParameters.Clear();
        for (var i = 0; i < relevant.Count; i++)
            TypeParameters.Add(new MfxParamDisplay(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasTypeParameters));
    }
```

with:

```csharp
    // Build the grid from the full context-valid set so 2-level discriminator chains
    // (MFX Type -> switch -> value, e.g. a delay's ms/Note time) are honoured.
    // GetRelevantParameters(false,false) excludes reserved + invalid-in-context and follows the chain.
    private void RecomputeTypeParameters()
    {
        var typeName = Type.Value;
        var relevant = _mfxDomain.GetRelevantParameters(false, false)
            .Where(p => p.ParSpec.Path.Contains("/MFX Parameter "))
            .OrderBy(p => p.ParSpec.AddressInt)
            .ToList();
        var labels = MfxCatalog.FriendlyParamNames(typeName, relevant.Select(p => p.ParSpec.Name).ToList());

        TypeParameters.Clear();
        for (var i = 0; i < relevant.Count; i++)
            TypeParameters.Add(new MfxParamDisplay(relevant[i], labels[i]));
        this.RaisePropertyChanged(nameof(HasTypeParameters));
    }
```

- [ ] **Step 6: Unsubscribe discriminators in `Dispose`.** Replace:

```csharp
    public void Dispose()
    {
        _typeSub.Dispose();
```

with:

```csharp
    public void Dispose()
    {
        foreach (var d in _discriminators) d.PropertyChanged -= OnDiscriminatorChanged;
        _typeSub.Dispose();
```

- [ ] **Step 7: Build to verify it compiles.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`. (Ignore `MSB3027`/`MSB3021` exe-lock messages from a running app — only `error CS/AVLN/XAMLIL` count.) Note: `RecomputeTypeParameters` is now parameterless and called from three places (ctor, the discriminator handler via `Dispatcher.UIThread.Post`, and nowhere in `OnTypeChanged`); confirm there are no remaining calls passing an argument.

- [ ] **Step 8: Commit.**

```bash
git add Src/ViewModels/MfxPanelViewModel.cs
git commit -m "fix(sns): MFX grid honours nested discriminators (delay ms/Note etc.)"
```

---

## Task 2: Verification + smoke checklist

**Files:** none.

- [ ] **Step 1: Full build.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build Integra7AuralAlchemist.sln --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Full test run.**

Run: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test Integra7AuralAlchemist.sln --nologo`
Expected: existing tests pass (172 on this branch off `main`; no new tests this fix). If the app is running and the build can't copy the exe, close it or use `--no-build` against the last good build.

- [ ] **Step 3: Hand the hardware smoke checklist (spec §6) to the user:**
  1. FX tab → **Multi Tap Delay**: the grid shows a time **value** control, not just the ms/Note switch.
  2. Flip the **ms/Note** switch → the time control swaps between a millisecond value and a note value.
  3. Drag a per-type **value** slider continuously → the grid does **not** reset mid-drag.
  4. Switch to another effect and back → grid repopulates (small delay is fine).
  5. Change the MFX type on the **hardware** → friendly grid + pickers follow.
  6. Edit a per-type value, open the raw "Advanced — MFX" tab → same value (shared FQP).
  7. Rapidly switch effects / flip switches → no crash.

- [ ] **Step 4: Finish the branch** — once the user is satisfied, use superpowers:finishing-a-development-branch to merge `fix-mfx-nested-discriminators` to `main` (Option 1, local).

---

## Self-Review notes (author)
- Spec coverage: Task 1 Steps 1–6 = §3a–§3d; Task 2 = §5/§6.
- `RecomputeTypeParameters` becomes parameterless; all call sites updated (ctor initial call, discriminator handler). `OnTypeChanged` no longer recomputes.
- Recompute triggers only on discriminator (`ParentCtrl`/`ParentCtrl2`-referenced) FQP changes → value-slider drags don't rebuild the grid. Marshalled to the UI thread because those FQP changes can arrive on a MIDI/throttle-timer thread.
- `GetRelevantParameters(false,false)` is safe here: the ctor already calls `GetRelevantParameters(true,true)` on this domain without asserting.
