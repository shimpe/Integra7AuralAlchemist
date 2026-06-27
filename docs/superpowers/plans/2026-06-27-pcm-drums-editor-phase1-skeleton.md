# PCM Drums Editor — Phase 1 (Skeleton) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the friendly PCM Drum Kit editor shell — header, an 88-key click-to-audition note rail, and the Drum/Comp-EQ/FX tab shell (FX live, others stubbed) — wired into the app as the default tab for PCM Drum presets, with the raw tabs retagged "Advanced — …".

**Architecture:** Mirrors the merged SuperNATURAL Drums editor exactly. A lightweight per-row VM (`PCMDrumNoteViewModel`) reads only each key's Partial Name; the kit VM (`PCMDrumKitEditorViewModel`) holds the header params, the note rail, the shared MFX panel, click-to-audition, and an "Advanced common…" navigation. The view is a header + 340px rail + right-hand TabControl. Wiring lives in `PartViewModel` and `MainWindow.axaml`.

**Tech Stack:** Avalonia 12 + ReactiveUI MVVM, `ParamInt/String/Bool` wrappers over `ThrottledParameterWriter`, `MidiNote`, the engine-agnostic `MfxPanelViewModel`, existing `DrumWhiteKeyBrush`/`DrumBlackKeyBrush` resources.

**Testing note:** Phase 1 is view-model/view glue over live FQP instances — there is no new pure helper to unit-test (the codebase reserves NUnit tests for pure mapping helpers like `PcmEnvelopeMapping`; TDD-style tests resume in the later `WmtVelocityMapping` phase). Each task therefore verifies by **Release build succeeding** and the **full 237-test suite staying green**. Build/test commands use the user-local .NET 10 SDK in Release (Debug exe is file-locked). Never use `--no-verify`.

Build: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
Test: `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

---

### Task 1: Note-rail row view-model

**Files:**
- Create: `Src/ViewModels/PCMDrumNoteViewModel.cs`

This mirrors `SNDrumNoteViewModel` but reads the PCM drum key's **Partial Name** (PCM drum keys have a user name, not a preset Inst Number) and uses the PCM drum partial path/constants.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One row in the PCM-Drums note rail: the MIDI note + key styling, and the drum key's
/// display name read live from its Partial Name (kept light — no per-row parameter wrappers).</summary>
public sealed class PCMDrumNoteViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Drum Kit Partial/";
    private readonly FullyQualifiedParameter _partialName;

    /// <summary>The note's partial domain — used to build the full editor when this note is selected.</summary>
    public DomainBase PartialDomain { get; }
    public int Index { get; }                 // 0..87
    public int Note { get; }                  // MIDI note 21..108
    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    public PCMDrumNoteViewModel(DomainBase partialDomain, int index)
    {
        PartialDomain = partialDomain;
        Index = index;
        Note = Constants.FIRST_PARTIAL_PCM_DRUM + index;
        var byPath = ToDict(partialDomain);
        _partialName = byPath[PP + "Partial Name"];
        _partialName.PropertyChanged += OnNameChanged;
    }

    public string InstName => _partialName.StringValue;

    private void OnNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(InstName)));
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    public void Dispose() => _partialName.PropertyChanged -= OnNameChanged;
}
```

- [ ] **Step 2: Build**

Run the Build command above. Expected: `Build succeeded.` 0 errors. (Unused until Task 2 references it — this just confirms it compiles.)

- [ ] **Step 3: Commit**

```bash
git add Src/ViewModels/PCMDrumNoteViewModel.cs
git commit -m "feat: PCM-Drums note rail row view-model"
```

---

### Task 2: Kit editor view-model

**Files:**
- Create: `Src/ViewModels/PCMDrumKitEditorViewModel.cs`

Header reads from two domains — Kit Name/Level from `PCMDrumKitCommon`, Phrase Number/TFX from `PCMDrumKitCommon 2`. Holds the 88-note rail, the shared MFX panel, click-to-audition, and "Advanced common…" navigation. The per-key Drum editor and Comp-EQ arrive in later phases, so `SelectedNote` exists (drives rail highlight + the placeholder) but there is no `SelectedDrumEditor`/`CompEq` yet.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly PCM Drum Kit editor for ONE part: header, an 88-note navigation rail with
/// click-to-audition, and the shared MFX panel. The Drum and Comp-EQ tab bodies are filled in later phases.</summary>
public sealed partial class PCMDrumKitEditorViewModel : ViewModelBase, IDisposable
{
    private const string CP = "PCM Drum Kit Common/";
    private const string C2P = "PCM Drum Kit Common 2/";

    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly Func<int, System.Threading.Tasks.Task>? _playNote;
    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _kitName;

    // --- Header ---
    public ParamInt KitLevel { get; }
    public ParamString PhraseNumber { get; }
    public ParamBool TfxSwitch { get; }

    public ObservableCollection<PCMDrumNoteViewModel> Notes { get; } = [];
    public MfxPanelViewModel Mfx { get; }

    public PCMDrumKitEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null, Func<int, System.Threading.Tasks.Task>? playNote = null)
    {
        _navigateToRawTab = navigateToRawTab;
        _playNote = playNote;

        var common = domain.PCMDrumKitCommon(partNo);
        var common2 = domain.PCMDrumKitCommon2(partNo);
        var byPath = ToDict(common);
        var byPath2 = ToDict(common2);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], _writer, min, max));
        ParamString PS2(string n) => Track(new ParamString(common2, byPath2[C2P + n], _writer));
        ParamBool PB2(string n) => Track(new ParamBool(common2, byPath2[C2P + n], _writer));

        KitLevel = PI("Kit Level", 0, 127);
        PhraseNumber = PS2("Phrase Number");
        TfxSwitch = PB2("TFX Switch");

        _kitName = byPath[CP + "Kit Name"];
        _kitName.PropertyChanged += OnKitNameChanged;

        // The note rail: notes 108 (top) → 21 (bottom) so low notes sit at the bottom.
        for (var i = Constants.NO_OF_PARTIALS_PCM_DRUM - 1; i >= 0; i--)
            Notes.Add(Track(new PCMDrumNoteViewModel(domain.PCMDrumKitPartial(partNo, i), i)));
        _selectedNote = Notes.Count > 0 ? Notes[0] : null;

        Mfx = new MfxPanelViewModel(domain.PCMDrumKitCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("PCMD-MFX", null));
    }

    public string KitName => _kitName.StringValue;

    private void OnKitNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(KitName)));
    }

    private PCMDrumNoteViewModel? _selectedNote;
    public PCMDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(value, _selectedNote)) return;
            this.RaiseAndSetIfChanged(ref _selectedNote, value);
        }
    }

    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("PCMD-KIT", null);

    /// <summary>Audition a drum by sending its MIDI note (note-on/off handled by the host).</summary>
    public void PlayNote(int note)
    {
        if (_playNote is not null) _ = _playNote(note);
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose()
    {
        _kitName.PropertyChanged -= OnKitNameChanged;
        foreach (var w in _wrappers) w.Dispose();
        Mfx.Dispose();
        _writer.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors. Confirm the source generator produced the property `PCMDrumKitEditor`-compatible command `AdvancedCommonCommand` (from `[ReactiveCommand] AdvancedCommon`).

- [ ] **Step 3: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 4: Commit**

```bash
git add Src/ViewModels/PCMDrumKitEditorViewModel.cs
git commit -m "feat: PCM-Drums kit editor view-model (header + note rail + MFX)"
```

---

### Task 3: Kit editor view (header + rail + tab shell)

**Files:**
- Modify: `Src/App.axaml` (add a `DrumKeyBorderBrush` resource)
- Create: `Src/Views/PCMDrumKitEditorView.axaml`
- Create: `Src/Views/PCMDrumKitEditorView.axaml.cs`
- Modify: `Src/Views/SNDrumKitEditorView.axaml` (replace the one inline `#888` with the new resource — keeps both drum rails rule-compliant and identical)

Mirrors `SNDrumKitEditorView`: badge "PCM Drums", header sliders/combos, a 340px note rail (low note bottom) with click-to-audition, and a Drum/Comp-EQ/FX TabControl. FX hosts the live `Mfx`; Drum/Comp-EQ are Phase-2/5 placeholders. The Drum placeholder shows the selected note so the rail wiring is visibly working. Reuses existing `DrumWhiteKeyBrush`/`DrumBlackKeyBrush` and `SnPanelBackgroundBrush`/`SnAccentBrush`/`SnBadgeForegroundBrush` resources. The rail's thin key gridline becomes a new `DrumKeyBorderBrush` resource (honoring the no-hardcoded-colors-in-XAML rule) instead of an inline hex.

- [ ] **Step 1: Add the `DrumKeyBorderBrush` resource to `Src/App.axaml`**

Find the existing drum key brush resources (added for SN-Drums, ~lines 43–44):

```xml
            <SolidColorBrush x:Key="DrumWhiteKeyBrush" Color="#c8ccce"/>
            <SolidColorBrush x:Key="DrumBlackKeyBrush" Color="#23262a"/>
```

Add immediately after them (same attribute form, same indentation):

```xml
            <SolidColorBrush x:Key="DrumKeyBorderBrush" Color="#888888"/>
```

- [ ] **Step 2: Create `PCMDrumKitEditorView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Integra7AuralAlchemist.ViewModels;

namespace Integra7AuralAlchemist.Views;

public partial class PCMDrumKitEditorView : UserControl
{
    public PCMDrumKitEditorView()
    {
        InitializeComponent();
    }

    // Clicking a rail row auditions that drum note (in addition to selecting it).
    private void NoteRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PCMDrumNoteViewModel note } &&
            DataContext is PCMDrumKitEditorViewModel vm)
            vm.PlayNote(note.Note);
    }
}
```

- [ ] **Step 3: Create `PCMDrumKitEditorView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             xmlns:controls="using:Integra7AuralAlchemist.Controls"
             x:DataType="vm:PCMDrumKitEditorViewModel"
             x:CompileBindings="True"
             x:Name="Root"
             x:Class="Integra7AuralAlchemist.Views.PCMDrumKitEditorView">

    <UserControl.Styles>
        <!-- Note-rail key cell: white by default, dark when the row is a black key. -->
        <Style Selector="Border.key">
            <Setter Property="Background" Value="{StaticResource DrumWhiteKeyBrush}"/>
        </Style>
        <Style Selector="Border.key.black">
            <Setter Property="Background" Value="{StaticResource DrumBlackKeyBrush}"/>
        </Style>
    </UserControl.Styles>

    <Grid RowDefinitions="Auto,*" Margin="8">

        <!-- ===== Kit Header ===== -->
        <Border Grid.Row="0" Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10" Margin="0,0,0,8">
            <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
            <StackPanel Orientation="Horizontal" Spacing="16">
                <StackPanel Spacing="2">
                    <Border Background="{StaticResource SnAccentBrush}" CornerRadius="4" Padding="6,2" HorizontalAlignment="Left">
                        <TextBlock Text="PCM Drums" Foreground="{StaticResource SnBadgeForegroundBrush}" FontSize="11"/>
                    </Border>
                    <TextBlock Text="{Binding KitName}" FontSize="16" FontWeight="Bold"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Kit Level"/>
                    <controls:ValueSlider Minimum="0" Maximum="127" Value="{Binding KitLevel.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="260">
                    <TextBlock Text="Phrase" ToolTip.Tip="Phrase Number"/>
                    <ComboBox MaxDropDownHeight="300" HorizontalAlignment="Stretch"
                              ItemsSource="{Binding PhraseNumber.Options}"
                              SelectedItem="{Binding PhraseNumber.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2">
                    <TextBlock Text="Tone FX" ToolTip.Tip="TFX Switch"/>
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="{Binding TfxSwitch.Value, Mode=TwoWay}"/>
                </StackPanel>
                <Button VerticalAlignment="Bottom" Content="Advanced common parameters…"
                        Command="{Binding AdvancedCommonCommand}"/>
            </StackPanel>
            </ScrollViewer>
        </Border>

        <!-- ===== Rail + selected drum ===== -->
        <Grid Grid.Row="1" ColumnDefinitions="340,*">

            <!-- Note rail: low note at the bottom (VM orders 108 -> 21) -->
            <ListBox Grid.Column="0" Margin="0,0,8,0"
                     ItemsSource="{Binding Notes}"
                     SelectedItem="{Binding SelectedNote, Mode=TwoWay}">
                <ListBox.Styles>
                    <Style Selector="ListBoxItem">
                        <Setter Property="Padding" Value="0"/>
                        <Setter Property="MinHeight" Value="0"/>
                    </Style>
                </ListBox.Styles>
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="vm:PCMDrumNoteViewModel">
                        <Grid ColumnDefinitions="54,*" Height="22" Background="Transparent"
                              PointerPressed="NoteRow_PointerPressed">
                            <Border Grid.Column="0" Classes="key" Classes.black="{Binding IsBlack}"
                                    BorderBrush="{StaticResource DrumKeyBorderBrush}" BorderThickness="0,0,1,1">
                                <TextBlock Text="{Binding CLabel}" Foreground="{StaticResource DrumBlackKeyBrush}"
                                           FontSize="9" FontWeight="Bold" VerticalAlignment="Center"
                                           HorizontalAlignment="Right" Margin="0,0,4,0"/>
                            </Border>
                            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center" Margin="6,0">
                                <TextBlock Text="{Binding NoteName}" Opacity="0.45" FontSize="10" Width="34"/>
                                <TextBlock Text="{Binding InstName}"/>
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Right: Drum / Comp-EQ / FX -->
            <TabControl Grid.Column="1">
                <TabItem Header="Drum">
                    <TextBlock Margin="12" Opacity="0.6"
                               Text="{Binding SelectedNote.NoteName, StringFormat='Per-key editor for {0} — coming in Phase 2.'}"/>
                </TabItem>
                <TabItem Header="Comp-EQ">
                    <TextBlock Margin="12" Opacity="0.6" Text="Comp-EQ — 6-unit compressor / EQ (Phase 5)."/>
                </TabItem>
                <TabItem Header="FX">
                    <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                        <ContentControl Content="{Binding Mfx}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
            </TabControl>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: Update the SN-Drums rail to use the shared resource**

In `Src/Views/SNDrumKitEditorView.axaml`, find the rail key border:

```xml
                            <Border Grid.Column="0" Classes="key" Classes.black="{Binding IsBlack}"
                                    BorderBrush="#888" BorderThickness="0,0,1,1">
```

Replace `BorderBrush="#888"` with `BorderBrush="{StaticResource DrumKeyBorderBrush}"` so both drum rails share the resource and no inline hex remains:

```xml
                            <Border Grid.Column="0" Classes="key" Classes.black="{Binding IsBlack}"
                                    BorderBrush="{StaticResource DrumKeyBorderBrush}" BorderThickness="0,0,1,1">
```

- [ ] **Step 5: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors (compiled XAML validates the bindings).

- [ ] **Step 6: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 7: Commit**

```bash
git add Src/App.axaml Src/Views/PCMDrumKitEditorView.axaml Src/Views/PCMDrumKitEditorView.axaml.cs Src/Views/SNDrumKitEditorView.axaml
git commit -m "feat: PCM-Drums kit editor view (header + note rail + tab shell)"
```

---

### Task 4: Wire the editor into PartViewModel

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs` (add a `[Reactive]` field near line 202; construct after the PCM-Drum Comp-EQ source cache is populated, ~line 1385)

- [ ] **Step 1: Add the reactive field**

Find this line (~202):

```csharp
    [Reactive] private PCMSynthToneEditorViewModel? _pcmSynthToneEditor;
```

Add immediately after it:

```csharp
    [Reactive] private PCMDrumKitEditorViewModel? _pcmDrumKitEditor;
```

(The source generator produces the property `PcmDrumKitEditor`, matching the existing `PcmSynthToneEditor`.)

- [ ] **Step 2: Construct the editor**

Find this block (~line 1383–1385):

```csharp
            List<FullyQualifiedParameter> p_pcmcompeq =
                _i7domain.PCMDrumKitCompEQ(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMDrumKitCompEQParameters.AddOrUpdate(p_pcmcompeq);
```

Add immediately after it:

```csharp

            // Friendly PCM Drum Kit editor for this part. Binds to the live PCM-D FQP instances
            // populated above; the nav callback clear-then-sets ToneTabKey so repeat "Advanced …"
            // navigations fire SelectTabByTag, and carries the selected note for "Advanced — Partials".
            _pcmDrumKitEditor?.Dispose();
            PcmDrumKitEditor = new PCMDrumKitEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            }, async note =>
            {
                // Audition the clicked drum on this part's MIDI channel (best-effort).
                try
                {
                    await _i7Api.NoteOnAsync((byte)PartNo, (byte)note, 100);
                    await Task.Delay(300);
                    await _i7Api.NoteOffAsync((byte)PartNo, (byte)note);
                }
                catch { /* ignore — auditioning is non-essential */ }
            });
```

- [ ] **Step 3: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 5: Commit**

```bash
git add Src/ViewModels/PartViewModel.cs
git commit -m "feat: build the friendly PCM-Drums editor per part"
```

---

### Task 5: MainWindow tab — friendly Editor + Advanced retag

**Files:**
- Modify: `Src/Views/MainWindow.axaml` (lines 275–299 raw PCM-Drum tabs and the "Partials" tab ~385)

Add a friendly "Editor" tab as the PCM-Drum default (`Tag="PCMD"`), move `Tag="PCMD"` off the raw Kit tab, and retag the raw tabs "Advanced — …" with stable tags the nav callback targets.

- [ ] **Step 1: Insert the friendly Editor tab and retag the raw Common tabs**

Replace this block (lines 275–299):

```xml
                                            <TabItem Header="Kit"
                                                     Tag="PCMD"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommonParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommon, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Kit extra"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommon2Parameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommon2, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="MFX"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Comp-EQ"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCompEQParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCompEQ, Mode=TwoWay}" />
                                            </TabItem>
```

with:

```xml
                                            <TabItem Header="Editor"
                                                     Tag="PCMD"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:PCMDrumKitEditorView DataContext="{Binding PcmDrumKitEditor}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Kit"
                                                     Tag="PCMD-KIT"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommonParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommon, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Kit extra"
                                                     Tag="PCMD-KIT2"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommon2Parameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommon2, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — MFX"
                                                     Tag="PCMD-MFX"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Comp-EQ"
                                                     Tag="PCMD-COMPEQ"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding PCMDrumKitCompEQParameters}"
                                                    SearchText="{Binding SearchTextPCMDrumKitCompEQ, Mode=TwoWay}" />
                                            </TabItem>
```

- [ ] **Step 2: Retag the PCM-Drum "Partials" raw tab**

Find this opening tag (~line 385):

```xml
                                            <TabItem Header="Partials" IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <TabControl
                                                    ItemsSource="{Binding PcmDrumKitPartialViewModels}"
                                                    TabStripPlacement="Left">
```

Replace those three lines with (adds the "Advanced — " prefix, a stable Tag, and the `AdvancedPartialIndex` binding so "Advanced — Partials" navigation lands on the right key, matching the SN-D/PCM-Syn partial tabs):

```xml
                                            <TabItem Header="Advanced — Partials"
                                                     Tag="PCMD-PARTIALS"
                                                     IsVisible="{Binding SelectedPresetIsPCMDrumKit}">
                                                <TabControl
                                                    SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                    ItemsSource="{Binding PcmDrumKitPartialViewModels}"
                                                    TabStripPlacement="Left">
```

- [ ] **Step 3: Build**

Run the Build command. Expected: `Build succeeded.` 0 errors (compiled XAML validates `PcmDrumKitEditor` and `PCMDrumKitEditorView`).

- [ ] **Step 4: Run tests**

Run the Test command. Expected: `Passed! - Failed: 0, Passed: 237`.

- [ ] **Step 5: Commit**

```bash
git add Src/Views/MainWindow.axaml
git commit -m "feat: friendly PCM-Drums Editor tab + Advanced -- tabs"
```

---

## Done criteria

Loading a PCM Drum Kit preset shows the friendly **Editor** tab by default: a header (Kit Name, Kit Level, Phrase, Tone FX, "Advanced common…"), an 88-key rail (A0 at the bottom → C8 at the top) where clicking a key auditions it and selects it, and a Drum/Comp-EQ/FX TabControl with the live MFX panel under FX. The raw parameter tabs remain reachable as "Advanced — …", and "Advanced common…" jumps to "Advanced — Kit". Build green, 237 tests passing. Phase 2 adds the per-key Drum editor (Setup + Amp tabs).
```
