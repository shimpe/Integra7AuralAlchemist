# SN-Drums Editor — Phase 2 (Drum tab) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Fill the **Drum** tab — the selected rail note's editor: a searchable drum-sound picker (+ browse arrow) and its tweaks (Level, Pan, Tune, Attack, Decay, Brilliance, Variation, Dynamic Range, Stereo Width, Chorus/Reverb sends, Output Assign), plus Copy / Paste / Init drum.

**Architecture:** A `SNDrumNoteEditorViewModel` holds the 13 params for one note — built fresh whenever the rail selection changes (so the heavy 513-item Inst Number options list exists only for the selected note). `SNDrumKitEditorViewModel` gains `SelectedDrumEditor` (rebuilt in the `SelectedNote` setter) and a shared `DrumClipboard`. The Drum tab renders `SelectedDrumEditor` via `ContentControl` (ViewLocator → `SNDrumNoteEditorView`), which hosts the searchable picker (the PCM wave-field pattern) + tweaks + Copy/Paste/Init. Changing the sound updates the rail row name (shared FQP).

**Tech Stack:** Avalonia 12 + ReactiveUI + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if the Debug exe is locked by a running app.

---

## File Structure

- **Create** `Src/ViewModels/SNDrumNoteEditorViewModel.cs` — the 13 per-note params + Copy/Paste/Init.
- **Modify** `Src/ViewModels/SNDrumKitEditorViewModel.cs` — `DrumClipboard` + `SelectedDrumEditor` (rebuilt on selection).
- **Create** `Src/Views/SNDrumNoteEditorView.axaml` (+ `.axaml.cs` with the browse handler).
- **Modify** `Src/Views/SNDrumKitEditorView.axaml` — Drum tab hosts `SelectedDrumEditor`.

---

## Task 1: SNDrumNoteEditorViewModel + selection wiring

**Files:**
- Create: `Src/ViewModels/SNDrumNoteEditorViewModel.cs`
- Modify: `Src/ViewModels/SNDrumKitEditorViewModel.cs`

- [ ] **Step 1: Create `Src/ViewModels/SNDrumNoteEditorViewModel.cs`:**

```csharp
using System;
using System.Collections.Generic;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Editor for ONE SN-Drums note (the Drum tab): the searchable drum sound + its tweaks.
/// Built fresh for the selected rail note, so the heavy Inst Number options list lives only here.</summary>
public sealed partial class SNDrumNoteEditorViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Partial/";
    private readonly SNDrumKitEditorViewModel _parent;
    private readonly List<IDisposable> _wrappers = [];

    public ParamString InstNumber { get; }   // searchable drum sound (~513)
    public ParamInt Level { get; }
    public ParamInt Pan { get; }
    public ParamInt Tune { get; }            // cents, -1200..1200
    public ParamInt Attack { get; }          // 0..100 %
    public ParamInt Decay { get; }           // -63..0
    public ParamInt Brilliance { get; }      // -15..12
    public ParamString Variation { get; }
    public ParamInt DynamicRange { get; }    // 0..63
    public ParamInt StereoWidth { get; }
    public ParamInt ChorusSend { get; }
    public ParamInt ReverbSend { get; }
    public ParamString OutputAssign { get; }

    private readonly IReadOnlyList<IParam> _editable;

    public SNDrumNoteEditorViewModel(SNDrumKitEditorViewModel parent, DomainBase partialDomain,
        ThrottledParameterWriter writer)
    {
        _parent = parent;
        var byPath = ToDict(partialDomain);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(partialDomain, byPath[PP + n], writer, min, max));
        ParamString PS(string n) => Track(new ParamString(partialDomain, byPath[PP + n], writer));

        InstNumber = PS("Inst Number");
        Level = PI("Level", 0, 127);
        Pan = PI("Pan", -64, 63);
        Tune = PI("Tune", -1200, 1200);
        Attack = PI("Attack", 0, 100);
        Decay = PI("Decay", -63, 0);
        Brilliance = PI("Brilliance", -15, 12);
        Variation = PS("Variation");
        DynamicRange = PI("Dynamic Range", 0, 63);
        StereoWidth = PI("Stereo Width", 0, 127);
        ChorusSend = PI("Chorus Send Level", 0, 127);
        ReverbSend = PI("Reverb Send Level", 0, 127);
        OutputAssign = PS("Output Assign");

        _editable = new IParam[]
        {
            InstNumber, Level, Pan, Tune, Attack, Decay, Brilliance, Variation,
            DynamicRange, StereoWidth, ChorusSend, ReverbSend, OutputAssign,
        };
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    // Copy / Paste / Init (edit-buffer only; shared clipboard lives on the parent kit editor).
    [ReactiveCommand] public void CopyDrum() => _parent.DrumClipboard = SnsPartialClipboard.Snapshot(_editable);
    [ReactiveCommand] public void PasteDrum() { if (_parent.DrumClipboard is { } data) SnsPartialClipboard.Apply(_editable, data); }
    [ReactiveCommand] public void InitDrum() => SnsPartialClipboard.Apply(_editable, InitDefaults);

    // Neutral reset of the tweaks (leaves the drum sound, Variation and Output Assign).
    private static readonly Dictionary<string, string> InitDefaults = new()
    {
        [PP + "Level"] = "100",
        [PP + "Pan"] = "0",
        [PP + "Tune"] = "0",
        [PP + "Attack"] = "0",
        [PP + "Decay"] = "0",
        [PP + "Brilliance"] = "0",
        [PP + "Dynamic Range"] = "0",
        [PP + "Stereo Width"] = "0",
        [PP + "Chorus Send Level"] = "0",
        [PP + "Reverb Send Level"] = "0",
    };

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
```

- [ ] **Step 2: Wire selection in `Src/ViewModels/SNDrumKitEditorViewModel.cs`.**

(a) Replace the existing `SelectedNote` property and add the editor/clipboard members. Find:

```csharp
    private SNDrumNoteViewModel? _selectedNote;
    public SNDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set { if (ReferenceEquals(value, _selectedNote)) return; this.RaiseAndSetIfChanged(ref _selectedNote, value); }
    }
```

Replace it with:

```csharp
    private SNDrumNoteViewModel? _selectedNote;
    public SNDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(value, _selectedNote)) return;
            this.RaiseAndSetIfChanged(ref _selectedNote, value);
            RebuildDrumEditor();
        }
    }

    private SNDrumNoteEditorViewModel? _selectedDrumEditor;
    /// <summary>The Drum-tab editor for the selected note (rebuilt on each selection change).</summary>
    public SNDrumNoteEditorViewModel? SelectedDrumEditor
    {
        get => _selectedDrumEditor;
        private set => this.RaiseAndSetIfChanged(ref _selectedDrumEditor, value);
    }

    /// <summary>Shared drum copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? DrumClipboard { get; set; }

    private void RebuildDrumEditor()
    {
        _selectedDrumEditor?.Dispose();
        SelectedDrumEditor = _selectedNote is { } n
            ? new SNDrumNoteEditorViewModel(this, n.PartialDomain, _writer)
            : null;
    }
```

(b) Build the initial editor in the constructor. Find:

```csharp
        _selectedNote = Notes.Count > 0 ? Notes[0] : null;
```

Add immediately after it:

```csharp
        RebuildDrumEditor();
```

(c) Dispose it. In `Dispose()`, find:

```csharp
        _kitName.PropertyChanged -= OnKitNameChanged;
```

Add immediately after it:

```csharp
        _selectedDrumEditor?.Dispose();
```

- [ ] **Step 3: Build** — Expected: succeeded.
- [ ] **Step 4: Run tests** — Expected: PASS (237).
- [ ] **Step 5: Commit**

```
git add Src/ViewModels/SNDrumNoteEditorViewModel.cs Src/ViewModels/SNDrumKitEditorViewModel.cs
git commit -m "feat: SN-Drums per-note editor view-model + selection wiring"
```

---

## Task 2: SNDrumNoteEditorView + Drum tab

**Files:**
- Create: `Src/Views/SNDrumNoteEditorView.axaml` (+ `.axaml.cs`)
- Modify: `Src/Views/SNDrumKitEditorView.axaml`

- [ ] **Step 1: Create `Src/Views/SNDrumNoteEditorView.axaml.cs`:**

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Integra7AuralAlchemist.Views;

public partial class SNDrumNoteEditorView : UserControl
{
    public SNDrumNoteEditorView()
    {
        InitializeComponent();
    }

    // The drum-sound field is searchable; the browse arrow clears the filter and opens the full list.
    private void BrowseInst(object? sender, RoutedEventArgs e)
    {
        InstBox.Text = string.Empty;
        InstBox.Focus();
        InstBox.IsDropDownOpen = true;
    }
}
```

- [ ] **Step 2: Create `Src/Views/SNDrumNoteEditorView.axaml`:**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             x:DataType="vm:SNDrumNoteEditorViewModel"
             x:CompileBindings="True"
             x:Class="Integra7AuralAlchemist.Views.SNDrumNoteEditorView">
    <UserControl.Styles>
        <Style Selector="TextBlock.sliderLabel">
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="MinHeight" Value="40"/>
        </Style>
    </UserControl.Styles>
    <StackPanel Spacing="12" Margin="0,0,0,8">

        <!-- Sound -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <TextBlock Text="Drum sound" FontWeight="Bold"/>
                <StackPanel Orientation="Horizontal" Spacing="2">
                    <AutoCompleteBox x:Name="InstBox" Width="300" MaxDropDownHeight="320"
                                     MinimumPrefixLength="0" FilterMode="Contains"
                                     ItemsSource="{Binding InstNumber.Options}"
                                     SelectedItem="{Binding InstNumber.Value, Mode=TwoWay}"/>
                    <Button Content="▾" Padding="8,4" VerticalAlignment="Center"
                            Click="BrowseInst" ToolTip.Tip="Browse all drum sounds"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <StackPanel Spacing="2">
                        <TextBlock Text="Variation" ToolTip.Tip="Variation"/>
                        <ComboBox ItemsSource="{Binding Variation.Options}"
                                  SelectedItem="{Binding Variation.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2">
                        <TextBlock Text="Output" ToolTip.Tip="Output Assign"/>
                        <ComboBox ItemsSource="{Binding OutputAssign.Options}"
                                  SelectedItem="{Binding OutputAssign.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Tweaks -->
        <Border Background="{StaticResource SnPanelBackgroundBrush}" CornerRadius="6" Padding="10">
            <StackPanel Spacing="8">
                <TextBlock Text="Tweaks" FontWeight="Bold"/>
                <WrapPanel Orientation="Horizontal">
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Level" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="127" Value="{Binding Level.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Pan (L / C / R)" Classes="sliderLabel"/>
                        <Slider Minimum="-64" Maximum="63" Value="{Binding Pan.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="200" Margin="0,0,16,8">
                        <TextBlock Text="Tune (cents)" Classes="sliderLabel"/>
                        <Slider Minimum="-1200" Maximum="1200" Value="{Binding Tune.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Attack (%)" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="100" Value="{Binding Attack.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Decay" Classes="sliderLabel"/>
                        <Slider Minimum="-63" Maximum="0" Value="{Binding Decay.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Brilliance" Classes="sliderLabel"/>
                        <Slider Minimum="-15" Maximum="12" Value="{Binding Brilliance.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Dynamic Range" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="63" Value="{Binding DynamicRange.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Stereo Width" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="127" Value="{Binding StereoWidth.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Chorus Send" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="127" Value="{Binding ChorusSend.Value, Mode=TwoWay}"/>
                    </StackPanel>
                    <StackPanel Spacing="2" Width="160" Margin="0,0,16,8">
                        <TextBlock Text="Reverb Send" Classes="sliderLabel"/>
                        <Slider Minimum="0" Maximum="127" Value="{Binding ReverbSend.Value, Mode=TwoWay}"/>
                    </StackPanel>
                </WrapPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Content="Copy drum" Command="{Binding CopyDrumCommand}"/>
                    <Button Content="Paste drum" Command="{Binding PasteDrumCommand}"/>
                    <Button Content="Init drum" Command="{Binding InitDrumCommand}"/>
                </StackPanel>
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Host it in the Drum tab.** In `Src/Views/SNDrumKitEditorView.axaml`, find:

```xml
                <TabItem Header="Drum">
                    <TextBlock Margin="12" Opacity="0.6" Text="Drum — selected note's sound + tweaks (Phase 2)."/>
                </TabItem>
```

Replace with:

```xml
                <TabItem Header="Drum">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                        <ContentControl Content="{Binding SelectedDrumEditor}" Margin="0,8,0,0"/>
                    </ScrollViewer>
                </TabItem>
```

- [ ] **Step 4: Build** — Expected: succeeded. (ViewLocator resolves `SNDrumNoteEditorViewModel`→`SNDrumNoteEditorView`; `SnPanelBackgroundBrush` resolves; `AutoCompleteBox` is the proven PCM pattern.)
- [ ] **Step 5: Run tests** — Expected: PASS (237).
- [ ] **Step 6: Commit**

```
git add Src/Views/SNDrumNoteEditorView.axaml Src/Views/SNDrumNoteEditorView.axaml.cs Src/Views/SNDrumKitEditorView.axaml
git commit -m "feat: SN-Drums Drum tab (sound picker + tweaks + copy/paste/init)"
```

---

## Done criteria

- Full suite green (237).
- Selecting a rail note shows its Drum tab: a searchable drum-sound field (+ browse arrow), Variation/Output combos, and the tweak sliders; editing the sound updates the rail row name. Copy → Paste copies a drum's settings to another; Init resets the tweaks.
- Remaining: **Phase 3 — Comp-EQ** (the 6-unit panel).
