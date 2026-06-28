# Playable Note Rail for Tone Editors — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shared, playable note rail (128 MIDI notes, note names only, click-to-audition; out-of-piano-range notes dimmed) to the far left of the three friendly tone editors (PCM Synth, SN-S, SN-A).

**Architecture:** One shared `ToneNoteRailViewModel` (+ `ToneNoteViewModel` rows) and one shared `ToneNoteRailView`, hosted in each editor via a `ContentControl` bound to a new `NoteRail` property (ViewLocator resolves the view by name). Clicking a row auditions the note via a play-note callback threaded from `PartViewModel` (same pattern as the drum editors).

**Tech Stack:** Avalonia 12 + ReactiveUI, the existing `MidiNote` helper, `DrumWhiteKeyBrush`/`DrumBlackKeyBrush`/`DrumKeyBorderBrush` resources. Build/test with the user-local SDK in Release (Debug exe file-lock = MSB3027/MSB3021, not a compile error). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

**Facts (verified):** All three tone editor VMs share the ctor shape `(Integra7Domain domain, int partNo, Action<string,int?>? navigateToRawTab = null)` and are constructed in `PartViewModel.InitializeParameterSourceCachesAsync` (PCM Synth ~line 1368, SN-S ~1421, SN-A ~1443). `MidiNote` is in `Integra7AuralAlchemist.Models.Services` (note 60 = "C4", 0 = "C-1", 127 = "G9"). The drum play-note lambda in `PartViewModel` uses `_i7Api.NoteOnAsync((byte)PartNo, (byte)note, 100)` → `Task.Delay(300)` → `NoteOffAsync`.

---

### Task 1: Rail view-models + tests

**Files:**
- Create: `Src/ViewModels/ToneNoteViewModel.cs`
- Create: `Src/ViewModels/ToneNoteRailViewModel.cs`
- Create: `Tests/TestToneNoteRail.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/TestToneNoteRail.cs`:

```csharp
using Integra7AuralAlchemist.ViewModels;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TestToneNoteRail
{
    [Test]
    public void InPianoRange_Boundaries()
    {
        Assert.That(new ToneNoteViewModel(20).InPianoRange, Is.False); // below A0
        Assert.That(new ToneNoteViewModel(21).InPianoRange, Is.True);  // A0
        Assert.That(new ToneNoteViewModel(108).InPianoRange, Is.True); // C8
        Assert.That(new ToneNoteViewModel(109).InPianoRange, Is.False); // above C8
    }

    [Test]
    public void NoteName_UsesMidiNoteConvention()
        => Assert.That(new ToneNoteViewModel(60).NoteName, Is.EqualTo("C4"));

    [Test]
    public void Rail_Has128Rows_LowNoteAtBottom()
    {
        var rail = new ToneNoteRailViewModel();
        Assert.That(rail.Notes.Count, Is.EqualTo(128));
        Assert.That(rail.Notes[0].Note, Is.EqualTo(127));   // top
        Assert.That(rail.Notes[127].Note, Is.EqualTo(0));   // bottom
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the Test command with `--filter "FullyQualifiedName~TestToneNoteRail"`. Expected: FAIL (types do not exist).

- [ ] **Step 3: Create `Src/ViewModels/ToneNoteViewModel.cs`**

```csharp
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row in a tone editor's playable note rail: a MIDI note and its name/key styling.
/// Immutable — no per-row state changes, so no INotifyPropertyChanged needed.</summary>
public sealed class ToneNoteViewModel
{
    public int Note { get; }   // MIDI note 0..127

    public ToneNoteViewModel(int note) => Note = note;

    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    /// <summary>True for the 88-key piano range (A0..C8); out-of-range notes are shown de-emphasized.</summary>
    public bool InPianoRange => Note is >= 21 and <= 108;
}
```

- [ ] **Step 4: Create `Src/ViewModels/ToneNoteRailViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The playable note rail shared by the friendly tone editors: 128 note rows (note 127 at
/// top → 0 at bottom, so low notes sit at the bottom) and a click-to-audition callback.</summary>
public sealed class ToneNoteRailViewModel : ViewModelBase, IDisposable
{
    private readonly Func<int, Task>? _playNote;

    public IReadOnlyList<ToneNoteViewModel> Notes { get; }

    public ToneNoteRailViewModel(Func<int, Task>? playNote = null)
    {
        _playNote = playNote;
        var notes = new List<ToneNoteViewModel>(128);
        for (var n = 127; n >= 0; n--) notes.Add(new ToneNoteViewModel(n));
        Notes = notes;
    }

    /// <summary>Audition a note (note-on/off handled by the host callback). Best-effort.</summary>
    public void PlayNote(int note)
    {
        if (_playNote is not null) _ = _playNote(note);
    }

    public void Dispose() { }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the filtered Test command. Expected: PASS (3 tests).

- [ ] **Step 6: Run the full suite**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 264` (261 + 3).

- [ ] **Step 7: Commit**

```bash
git add Src/ViewModels/ToneNoteViewModel.cs Src/ViewModels/ToneNoteRailViewModel.cs Tests/TestToneNoteRail.cs
git commit -m "feat: tone-editor note-rail view-models + tests"
```

---

### Task 2: Rail view + out-of-range brushes

**Files:**
- Modify: `Src/App.axaml` (two dimmed brushes)
- Create: `Src/Views/ToneNoteRailView.axaml`
- Create: `Src/Views/ToneNoteRailView.axaml.cs`

- [ ] **Step 1: Add the out-of-range brushes to `Src/App.axaml`**

Find the drum key brushes (added earlier):
```xml
            <SolidColorBrush x:Key="DrumWhiteKeyBrush" Color="#c8ccce"/>
            <SolidColorBrush x:Key="DrumBlackKeyBrush" Color="#23262a"/>
            <SolidColorBrush x:Key="DrumKeyBorderBrush" Color="#888888"/>
```
Add immediately after them:
```xml
            <SolidColorBrush x:Key="ToneKeyOutOfRangeWhiteBrush" Color="#9a9ea1"/>
            <SolidColorBrush x:Key="ToneKeyOutOfRangeBlackBrush" Color="#34383c"/>
```

- [ ] **Step 2: Create `Src/Views/ToneNoteRailView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class ToneNoteRailView : UserControl
{
    public ToneNoteRailView()
    {
        InitializeComponent();
    }

    // Clicking a row auditions that note.
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ToneNoteViewModel note } &&
            DataContext is ToneNoteRailViewModel vm)
            vm.PlayNote(note.Note);
    }
}
```

- [ ] **Step 3: Create `Src/Views/ToneNoteRailView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             x:DataType="vm:ToneNoteRailViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.ToneNoteRailView">
    <UserControl.Styles>
        <!-- Sideways piano key: white by default, dark for black keys; dimmed when out of piano range. -->
        <Style Selector="Border.key">
            <Setter Property="Background" Value="{StaticResource DrumWhiteKeyBrush}"/>
        </Style>
        <Style Selector="Border.key.black">
            <Setter Property="Background" Value="{StaticResource DrumBlackKeyBrush}"/>
        </Style>
        <Style Selector="Border.key.outOfRange">
            <Setter Property="Background" Value="{StaticResource ToneKeyOutOfRangeWhiteBrush}"/>
        </Style>
        <Style Selector="Border.key.black.outOfRange">
            <Setter Property="Background" Value="{StaticResource ToneKeyOutOfRangeBlackBrush}"/>
        </Style>
        <!-- Note-name text dims for out-of-range notes. -->
        <Style Selector="TextBlock.dim">
            <Setter Property="Opacity" Value="0.4"/>
        </Style>
    </UserControl.Styles>

    <ListBox Width="88" ItemsSource="{Binding Notes}" Background="Transparent">
        <ListBox.Styles>
            <Style Selector="ListBoxItem">
                <Setter Property="Padding" Value="0"/>
                <Setter Property="MinHeight" Value="0"/>
            </Style>
        </ListBox.Styles>
        <ListBox.ItemTemplate>
            <DataTemplate x:DataType="vm:ToneNoteViewModel">
                <Grid ColumnDefinitions="32,*" Height="22" Background="Transparent"
                      PointerPressed="NoteRow_PointerPressed">
                    <Border Grid.Column="0" Classes="key" Classes.black="{Binding IsBlack}"
                            Classes.outOfRange="{Binding !InPianoRange}"
                            BorderBrush="{StaticResource DrumKeyBorderBrush}" BorderThickness="0,0,1,1">
                        <TextBlock Text="{Binding CLabel}" Foreground="{StaticResource DrumBlackKeyBrush}"
                                   FontSize="9" FontWeight="Bold" VerticalAlignment="Center"
                                   HorizontalAlignment="Right" Margin="0,0,2,0"/>
                    </Border>
                    <TextBlock Grid.Column="1" Text="{Binding NoteName}" Classes.dim="{Binding !InPianoRange}"
                               FontSize="10" VerticalAlignment="Center" Margin="6,0"/>
                </Grid>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
</UserControl>
```

- [ ] **Step 4: Build + test**

Run the Build command (expect 0 errors; compiled XAML validates the bindings) then the Test command (expect `Passed! - Failed: 0, Passed: 264`). The view is unused until Task 4 — this confirms it compiles.

- [ ] **Step 5: Commit**

```bash
git add Src/App.axaml Src/Views/ToneNoteRailView.axaml Src/Views/ToneNoteRailView.axaml.cs
git commit -m "feat: ToneNoteRailView (playable note strip) + out-of-range brushes"
```

---

### Task 3: Add `NoteRail` to the three tone editor VMs

**Files:**
- Modify: `Src/ViewModels/PCMSynthToneEditorViewModel.cs`
- Modify: `Src/ViewModels/SNSynthToneEditorViewModel.cs`
- Modify: `Src/ViewModels/SNAcousticToneEditorViewModel.cs`

Each gains a `playNote` ctor parameter (default null, so `PartViewModel` still compiles until Task 4), a `NoteRail` property, its construction, and disposal.

- [ ] **Step 1: `PCMSynthToneEditorViewModel`**

Add the property after `public PcmPmtPanelViewModel Pmt { get; }`:
```csharp
    public ToneNoteRailViewModel NoteRail { get; }
```
Change the ctor signature from:
```csharp
    public PCMSynthToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;
```
to:
```csharp
    public PCMSynthToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null, Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(playNote);
```
In `Dispose()`, add before `_writer.Dispose();`:
```csharp
        NoteRail.Dispose();
```

- [ ] **Step 2: `SNSynthToneEditorViewModel`**

Add the property after `public MfxPanelViewModel Mfx { get; }`:
```csharp
    public ToneNoteRailViewModel NoteRail { get; }
```
Change the ctor signature from:
```csharp
    public SNSynthToneEditorViewModel(Integra7Domain domain, int partNo, Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;
```
to:
```csharp
    public SNSynthToneEditorViewModel(Integra7Domain domain, int partNo, Action<string, int?>? navigateToRawTab = null,
        Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(playNote);
```
In `Dispose()`, add before `_writer.Dispose();`:
```csharp
        NoteRail.Dispose();
```

- [ ] **Step 3: `SNAcousticToneEditorViewModel`**

Add the property after `public MfxPanelViewModel Mfx { get; }`:
```csharp
    public ToneNoteRailViewModel NoteRail { get; }
```
Change the ctor signature from:
```csharp
    public SNAcousticToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;
```
to:
```csharp
    public SNAcousticToneEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null, Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        NoteRail = new ToneNoteRailViewModel(playNote);
```
In `Dispose()`, add before `_writer.Dispose();`:
```csharp
        NoteRail.Dispose();
```

- [ ] **Step 4: Build + test**

Run the Build command (expect 0 errors — `PartViewModel` still compiles since `playNote` defaults to null) then the Test command (expect `Passed! - Failed: 0, Passed: 264`).

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/PCMSynthToneEditorViewModel.cs Src/ViewModels/SNSynthToneEditorViewModel.cs Src/ViewModels/SNAcousticToneEditorViewModel.cs
git commit -m "feat: NoteRail on the three tone editor view-models"
```

---

### Task 4: Show the rail + wire the play-note callback

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`
- Modify: `Src/Views/SNSynthToneEditorView.axaml`
- Modify: `Src/Views/SNAcousticToneEditorView.axaml`
- Modify: `Src/ViewModels/PartViewModel.cs` (pass the play-note lambda to all three)

- [ ] **Step 1: PCM Synth view — add the rail column**

In `Src/Views/PCMSynthToneEditorView.axaml`, find:
```xml
        <Grid Grid.Row="1" ColumnDefinitions="240,*">

            <Grid Grid.Column="0" Margin="0,0,8,0" RowDefinitions="Auto,*">
```
Replace with (prepend an `Auto` column, insert the rail in column 0, shift the partial-rail grid to column 1):
```xml
        <Grid Grid.Row="1" ColumnDefinitions="Auto,240,*">

            <ContentControl Grid.Column="0" Content="{Binding NoteRail}" Margin="0,0,8,0"/>

            <Grid Grid.Column="1" Margin="0,0,8,0" RowDefinitions="Auto,*">
```
Then find the right-column grid `<!-- Right column: tab shell + Copy/Paste/Init --> <Grid Grid.Column="1" RowDefinitions="*,Auto">` and change its `Grid.Column="1"` to `Grid.Column="2"`:
```xml
            <!-- Right column: tab shell + Copy/Paste/Init -->
            <Grid Grid.Column="2" RowDefinitions="*,Auto">
```

- [ ] **Step 2: SN-S view — add the rail column**

In `Src/Views/SNSynthToneEditorView.axaml`, find the content grid `<Grid Grid.Row="1" ColumnDefinitions="240,*">`. Read its two direct children (the partial-rail container at `Grid.Column="0"` and the right-hand content at `Grid.Column="1"`). Apply the same transform:
1. Change `ColumnDefinitions="240,*"` → `ColumnDefinitions="Auto,240,*"`.
2. Insert as the first child: `<ContentControl Grid.Column="0" Content="{Binding NoteRail}" Margin="0,0,8,0"/>`.
3. Change the existing partial-rail child's `Grid.Column="0"` → `Grid.Column="1"`.
4. Change the existing right-hand content child's `Grid.Column="1"` → `Grid.Column="2"`.

- [ ] **Step 3: SN-A view — wrap the TabControl with a rail column**

In `Src/Views/SNAcousticToneEditorView.axaml`, find:
```xml
        <TabControl Grid.Row="1">
```
Replace with:
```xml
        <Grid Grid.Row="1" ColumnDefinitions="Auto,*">
            <ContentControl Grid.Column="0" Content="{Binding NoteRail}" Margin="0,0,8,0"/>
            <TabControl Grid.Column="1">
```
Then find the matching closing `</TabControl>` for that TabControl and add a `</Grid>` immediately after it:
```xml
        </TabControl>
        </Grid>
```
(Confirm you close the correct `</TabControl>` — the one that was `Grid.Row="1"`, i.e. the outermost TabControl in the content row.)

- [ ] **Step 4: `PartViewModel` — pass the play-note lambda to all three editors**

In `Src/ViewModels/PartViewModel.cs`, each tone editor is constructed with a nav callback ending in `})`. Append a play-note lambda argument to each (mirroring the drum editor). For **PCM Synth**, find:
```csharp
            PcmSynthToneEditor = new PCMSynthToneEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            });
```
Replace the closing `});` with the nav lambda + the play-note lambda:
```csharp
            PcmSynthToneEditor = new PCMSynthToneEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            }, async note =>
            {
                try
                {
                    await _i7Api.NoteOnAsync((byte)PartNo, (byte)note, 100);
                    await Task.Delay(300);
                    await _i7Api.NoteOffAsync((byte)PartNo, (byte)note);
                }
                catch { /* ignore — auditioning is non-essential */ }
            });
```
For **SN-S** find:
```csharp
            SNSynthToneEditor = new SNSynthToneEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            });
```
Replace its closing `});` the same way (identical `, async note => { try { ... } catch { } });` block).
For **SN-A** find:
```csharp
            SNAcousticToneEditor = new SNAcousticToneEditorViewModel(_i7domain, PartNo, (tag, _) =>
            {
                ToneTabKey = "";
                ToneTabKey = tag;
            });
```
Replace its closing `});` the same way (identical `, async note => { try { ... } catch { } });` block).

(`PartViewModel` already uses `_i7Api.NoteOnAsync`/`NoteOffAsync` + `Task.Delay` in the drum editor lambda, so the usings/fields are present.)

- [ ] **Step 5: Build + test**

Run the Build command (expect 0 errors; compiled XAML validates `{Binding NoteRail}` against each editor VM) then the Test command (expect `Passed! - Failed: 0, Passed: 264`). A genuine AVLN/binding error IS a true failure — report it.

- [ ] **Step 6: Commit**

```bash
git add Src/Views/PCMSynthToneEditorView.axaml Src/Views/SNSynthToneEditorView.axaml Src/Views/SNAcousticToneEditorView.axaml Src/ViewModels/PartViewModel.cs
git commit -m "feat: show playable note rail in the three tone editors + wire audition"
```

---

## Done criteria

Each friendly tone editor (PCM Synth, SN-S, SN-A) shows a thin playable note strip on its far left: 128 notes (low at the bottom) with sideways piano keys + note names, notes outside A0–C8 dimmed, and clicking any note auditions it on the part's MIDI channel. Build green, 264 tests passing. The strip is one shared control reused by all three editors.
