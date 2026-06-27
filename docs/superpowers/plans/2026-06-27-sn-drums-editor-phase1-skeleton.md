# SN-Drums Editor — Phase 1 (Skeleton + note rail + FX) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stand up the friendly SN Drum Kit editor shell — header (Kit Name/Level/Ambience/Phrase/TFX), a 62-note **vertical-keyboard navigation rail** (low note at the bottom, C rows labelled, drum sound name per row) with selection, a working FX tab, and the advanced-tab wiring.

**Architecture:** A new `SNDrumKitEditorViewModel` (built per-part by `PartViewModel`, bound in `MainWindow` to a friendly Editor tab tagged `SN-D`) folds the 5 header params in directly (like the SN-A editor) and owns an `ObservableCollection<SNDrumNoteViewModel>` rail + the reused `MfxPanelViewModel`. Each `SNDrumNoteViewModel` is lightweight — note metadata + the drum-sound name read live from its `Inst Number` FQP (no per-row 513-item options list). The rail is a `ListBox` with a vertical-keyboard `ItemTemplate`. Drum/Comp-EQ tab bodies are stubs for later phases.

**Tech Stack:** Avalonia 12 + ReactiveUI (`[Reactive]`/`[ReactiveCommand]`) + .NET 10, NUnit 3.

**Build:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" build "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`
**Test:** `& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" test "D:\Projects\Integra7AuralAlchemist\Integra7AuralAlchemist.sln" --nologo --configuration Release`

> Do NOT use `--no-verify`. Use Release if a running app holds the Debug exe lock (MSB3027/MSB3021 is a lock, not a compile error).

---

## File Structure

- **Create** `Src/ViewModels/SNDrumNoteViewModel.cs` — one rail row (note metadata + live drum-sound name).
- **Create** `Src/ViewModels/SNDrumKitEditorViewModel.cs` — header + 62-note rail + MFX + selection + advanced-nav.
- **Create** `Src/Views/SNDrumKitEditorView.axaml` (+ `.axaml.cs`) — header + rail `ListBox` + tab shell.
- **Modify** `App.axaml` — two key-color brushes.
- **Modify** `Src/ViewModels/PartViewModel.cs` — `[Reactive] SNDrumKitEditorViewModel?` field + construction.
- **Modify** `Src/Views/MainWindow.axaml` — friendly Editor tab (`SN-D`) + re-tagged `SN-D-*` advanced tabs.

No `ViewLocator` change (MainWindow places the view explicitly). Reuses `MidiNote` (note names) and `MfxPanelViewModel`.

---

## Task 1: SNDrumNoteViewModel (rail row)

**Files:**
- Create: `Src/ViewModels/SNDrumNoteViewModel.cs`

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

/// <summary>One row in the SN-Drums note rail: the MIDI note + key styling, and the drum sound's
/// display name read live from the partial's Inst Number (kept light — no per-row options list).</summary>
public sealed class SNDrumNoteViewModel : ViewModelBase, IDisposable
{
    private const string PP = "SuperNATURAL Drum Kit Partial/";
    private readonly FullyQualifiedParameter _instNumber;

    /// <summary>The note's partial domain — used to build the full editor when this note is selected.</summary>
    public DomainBase PartialDomain { get; }
    public int Index { get; }                 // 0..61
    public int Note { get; }                  // MIDI note 27..88
    public string NoteName => MidiNote.Name(Note);
    public bool IsBlack => MidiNote.IsBlack(Note);
    public bool IsC => MidiNote.IsC(Note);
    public string CLabel => IsC ? NoteName : "";

    public SNDrumNoteViewModel(DomainBase partialDomain, int index)
    {
        PartialDomain = partialDomain;
        Index = index;
        Note = Constants.FIRST_PARTIAL_SN_DRUM + index;
        var byPath = ToDict(partialDomain);
        _instNumber = byPath[PP + "Inst Number"];
        _instNumber.PropertyChanged += OnInstChanged;
    }

    public string InstName => _instNumber.StringValue;

    private void OnInstChanged(object? s, PropertyChangedEventArgs e)
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

    public void Dispose() => _instNumber.PropertyChanged -= OnInstChanged;
}
```

- [ ] **Step 2: Build** — Expected: succeeded (type compiles; not yet referenced).
- [ ] **Step 3: Commit**

```
git add Src/ViewModels/SNDrumNoteViewModel.cs
git commit -m "feat: SN-Drums note rail row view-model"
```

---

## Task 2: SNDrumKitEditorViewModel

**Files:**
- Create: `Src/ViewModels/SNDrumKitEditorViewModel.cs`

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

/// <summary>Friendly SuperNATURAL Drum Kit editor for ONE part: header, a 62-note navigation rail,
/// and the shared MFX panel. The Drum and Comp-EQ tab bodies are filled in later phases.</summary>
public sealed partial class SNDrumKitEditorViewModel : ViewModelBase, IDisposable
{
    private const string CP = "SuperNATURAL Drum Kit Common/";

    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string, int?>? _navigateToRawTab;
    private readonly List<IDisposable> _wrappers = [];
    private readonly FullyQualifiedParameter _kitName;

    // --- Header ---
    public ParamInt KitLevel { get; }
    public ParamInt AmbienceLevel { get; }
    public ParamString PhraseNumber { get; }
    public ParamBool TfxSwitch { get; }

    public ObservableCollection<SNDrumNoteViewModel> Notes { get; } = [];
    public MfxPanelViewModel Mfx { get; }

    public SNDrumKitEditorViewModel(Integra7Domain domain, int partNo,
        Action<string, int?>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var common = domain.SNDrumKitCommon(partNo);
        var byPath = ToDict(common);
        ParamInt PI(string n, int min, int max) => Track(new ParamInt(common, byPath[CP + n], _writer, min, max));
        ParamString PS(string n) => Track(new ParamString(common, byPath[CP + n], _writer));
        ParamBool PB(string n) => Track(new ParamBool(common, byPath[CP + n], _writer));

        KitLevel = PI("Kit Level", 0, 127);
        AmbienceLevel = PI("Ambience Level", 0, 127);
        PhraseNumber = PS("Phrase Number");
        TfxSwitch = PB("TFX Switch");

        _kitName = byPath[CP + "Kit Name"];
        _kitName.PropertyChanged += OnKitNameChanged;

        // The note rail: notes 88 (top) → 27 (bottom) so low notes sit at the bottom.
        for (var i = Constants.NO_OF_PARTIALS_SN_DRUM - 1; i >= 0; i--)
            Notes.Add(Track(new SNDrumNoteViewModel(domain.SNDrumKitPartial(partNo, i), i)));
        _selectedNote = Notes.Count > 0 ? Notes[0] : null;

        Mfx = new MfxPanelViewModel(domain.SNDrumKitCommonMFX(partNo), _writer,
            () => _navigateToRawTab?.Invoke("SN-D-MFX", null));
    }

    public string KitName => _kitName.StringValue;

    private void OnKitNameChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(KitName)));
    }

    private SNDrumNoteViewModel? _selectedNote;
    public SNDrumNoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set { if (ReferenceEquals(value, _selectedNote)) return; this.RaiseAndSetIfChanged(ref _selectedNote, value); }
    }

    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("SN-D-KIT", null);

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

- [ ] **Step 2: Build** — Expected: succeeded (source generators produce `SelectedNote` is hand-written; `AdvancedCommonCommand` generated).
- [ ] **Step 3: Run tests** — Expected: PASS (237).
- [ ] **Step 4: Commit**

```
git add Src/ViewModels/SNDrumKitEditorViewModel.cs
git commit -m "feat: SN-Drums kit editor view-model (header + note rail + MFX)"
```

---

## Task 3: App brushes + SNDrumKitEditorView

**Files:**
- Modify: `App.axaml`
- Create: `Src/Views/SNDrumKitEditorView.axaml` (+ `.axaml.cs`)

- [ ] **Step 1: Add two key brushes to `App.axaml`** (next to the other `Sn*`/`PmtZone*` `SolidColorBrush` resources):

```xml
    <SolidColorBrush x:Key="DrumWhiteKeyBrush" Color="#c8ccce"/>
    <SolidColorBrush x:Key="DrumBlackKeyBrush" Color="#23262a"/>
```

- [ ] **Step 2: Create `Src/Views/SNDrumKitEditorView.axaml.cs`:**

```csharp
using Avalonia.Controls;

namespace Integra7AuralAlchemist.Views;

public partial class SNDrumKitEditorView : UserControl
{
    public SNDrumKitEditorView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Create `Src/Views/SNDrumKitEditorView.axaml`:**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Integra7AuralAlchemist.ViewModels"
             x:DataType="vm:SNDrumKitEditorViewModel"
             x:CompileBindings="True"
             x:Name="Root"
             x:Class="Integra7AuralAlchemist.Views.SNDrumKitEditorView">

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
                        <TextBlock Text="SN Drums" Foreground="{StaticResource SnBadgeForegroundBrush}" FontSize="11"/>
                    </Border>
                    <TextBlock Text="{Binding KitName}" FontSize="16" FontWeight="Bold"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Kit Level"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding KitLevel.Value, Mode=TwoWay}"/>
                </StackPanel>
                <StackPanel Spacing="2" Width="160">
                    <TextBlock Text="Ambience" ToolTip.Tip="Ambience Level"/>
                    <Slider Minimum="0" Maximum="127" Value="{Binding AmbienceLevel.Value, Mode=TwoWay}"/>
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
        <Grid Grid.Row="1" ColumnDefinitions="240,*">

            <!-- Note rail: low note at the bottom (VM orders 88→27) -->
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
                    <DataTemplate x:DataType="vm:SNDrumNoteViewModel">
                        <Grid ColumnDefinitions="54,*" Height="22">
                            <Border Grid.Column="0" Classes="key" Classes.black="{Binding IsBlack}"
                                    BorderBrush="#888" BorderThickness="0,0,1,1">
                                <TextBlock Text="{Binding CLabel}" Foreground="{StaticResource DrumBlackKeyBrush}"
                                           FontSize="9" FontWeight="Bold" VerticalAlignment="Center"
                                           HorizontalAlignment="Right" Margin="0,0,4,0"/>
                            </Border>
                            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center" Margin="6,0">
                                <TextBlock Text="{Binding NoteName}" Opacity="0.45" FontSize="10" Width="34"/>
                                <TextBlock Text="{Binding InstName}" TextTrimming="CharacterEllipsis"/>
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Right: Drum / Comp-EQ / FX -->
            <TabControl Grid.Column="1">
                <TabItem Header="Drum">
                    <TextBlock Margin="12" Opacity="0.6" Text="Drum — selected note's sound + tweaks (Phase 2)."/>
                </TabItem>
                <TabItem Header="Comp-EQ">
                    <TextBlock Margin="12" Opacity="0.6" Text="Comp-EQ — 6-unit compressor / EQ (Phase 3)."/>
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

> Note: `BorderBrush="#888"` is the only inline color and it's a neutral key separator; if the no-hardcoded-colors rule should cover it too, add a `DrumKeyBorderBrush` resource. Otherwise leave as-is (it mirrors the keyboard separators already used in `PmtZoneEditorControl`).

- [ ] **Step 4: Build** — Expected: succeeded (compiled bindings validate against the VM; the `Sn*Brush`/`Drum*KeyBrush` resources resolve).
- [ ] **Step 5: Commit**

```
git add App.axaml Src/Views/SNDrumKitEditorView.axaml Src/Views/SNDrumKitEditorView.axaml.cs
git commit -m "feat: SN-Drums kit editor view (header + note rail + tab shell)"
```

---

## Task 4: Wire the editor into PartViewModel

**Files:**
- Modify: `Src/ViewModels/PartViewModel.cs`

- [ ] **Step 1: Add the reactive field.** Find:

```csharp
    [Reactive] private SNSynthToneEditorViewModel? _sNSynthToneEditor;
    [Reactive] private SNAcousticToneEditorViewModel? _sNAcousticToneEditor;
    [Reactive] private PCMSynthToneEditorViewModel? _pcmSynthToneEditor;
```

Add a fourth line below:

```csharp
    [Reactive] private SNDrumKitEditorViewModel? _sNDrumKitEditor;
```

- [ ] **Step 2: Construct it after the SN-Drums caches are populated.** Find (inside `ResyncPartAsync`):

```csharp
            List<FullyQualifiedParameter> p_sncompeq =
                _i7domain.SNDrumKitCompEQ(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNDrumKitCompEQParameters.AddOrUpdate(p_sncompeq);
```

Immediately after that `_sourceCacheSNDrumKitCompEQParameters.AddOrUpdate(p_sncompeq);` line, insert:

```csharp

            // Friendly SuperNATURAL Drum Kit editor for this part. Binds to the live SN-D FQP
            // instances populated above; the nav callback clear-then-sets ToneTabKey so repeat
            // "Advanced …" navigations fire SelectTabByTag, and carries the selected note for
            // "Advanced — Partials".
            _sNDrumKitEditor?.Dispose();
            SNDrumKitEditor = new SNDrumKitEditorViewModel(_i7domain, PartNo, (tag, partialIdx) =>
            {
                if (partialIdx is int idx) AdvancedPartialIndex = idx;
                ToneTabKey = "";
                ToneTabKey = tag;
            });
```

- [ ] **Step 3: Build** — Expected: succeeded. The source generator exposes `SNDrumKitEditor` from `_sNDrumKitEditor` (matching the `_sNSynthToneEditor`→`SNSynthToneEditor` casing). **If the generated property is instead `SnDrumKitEditor`, note it — Task 5's binding must match.**
- [ ] **Step 4: Run tests** — Expected: PASS (237).
- [ ] **Step 5: Commit**

```
git add Src/ViewModels/PartViewModel.cs
git commit -m "feat: build the friendly SN-Drums editor per part"
```

---

## Task 5: MainWindow tabs

**Files:**
- Modify: `Src/Views/MainWindow.axaml`

The raw "Kit" tab currently owns `Tag="SN-D"` (the default for SN-Drums presets). Move that tag to the new friendly Editor tab and re-tag the raw tabs `SN-D-*`.

- [ ] **Step 1: Add the Editor tab + re-tag Kit/MFX/Comp-EQ.** Find the three SN-Drums raw tabs (the `TabItem`s with `IsVisible="{Binding SelectedPresetIsSNDrumKit}"` for Headers **Kit**, **MFX**, **Comp-EQ**) and replace that group with:

```xml
                                            <TabItem Header="Editor"
                                                     Tag="SN-D"
                                                     IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <local:SNDrumKitEditorView DataContext="{Binding SNDrumKitEditor}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Kit"
                                                     Tag="SN-D-KIT"
                                                     IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNDrumKitCommonParameters}"
                                                    SearchText="{Binding SearchTextSNDrumKitCommon, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — MFX"
                                                     Tag="SN-D-MFX"
                                                     IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNDrumKitCommonMFXParameters}"
                                                    SearchText="{Binding SearchTextSNDrumKitCommonMFX, Mode=TwoWay}" />
                                            </TabItem>
                                            <TabItem Header="Advanced — Comp-EQ"
                                                     Tag="SN-D-COMPEQ"
                                                     IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <local:ParameterCollection
                                                    Parameters="{Binding SNDrumKitCompEQParameters}"
                                                    SearchText="{Binding SearchTextSNDrumKitCompEQ, Mode=TwoWay}" />
                                            </TabItem>
```

(Keep the exact `Parameters`/`SearchText` bindings from the existing tabs — only the `Header`/`Tag` change and the new Editor tab is added first. If the MFX/Comp-EQ tabs use slightly different `SearchText` binding names, preserve whatever is already there.)

- [ ] **Step 2: Tag the SN-Drums "Partials" tab and bind its selected partial.** Find:

```xml
                                            <TabItem Header="Partials" IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <TabControl
                                                    ItemsSource="{Binding SNDrumKitPartialViewModels}"
                                                    TabStripPlacement="Left">
```

Change it to:

```xml
                                            <TabItem Header="Advanced — Partials" Tag="SN-D-PARTIALS" IsVisible="{Binding SelectedPresetIsSNDrumKit}">
                                                <TabControl
                                                    SelectedIndex="{Binding AdvancedPartialIndex, Mode=TwoWay}"
                                                    ItemsSource="{Binding SNDrumKitPartialViewModels}"
                                                    TabStripPlacement="Left">
```

(Leave the rest of that `TabControl` unchanged.)

- [ ] **Step 3: Build** — Expected: succeeded.
- [ ] **Step 4: Run tests** — Expected: PASS (237).
- [ ] **Step 5: Manual verification (app/hardware):** select an SN Drum Kit preset and confirm the **Editor** tab opens by default, the header shows kit name/level/ambience/phrase/TFX, the **rail lists 62 notes (low at bottom, C rows labelled, drum names shown)** and selecting a row highlights it, the **FX** tab shows the MFX panel, and "Advanced common parameters…" opens **Advanced — Kit**.
- [ ] **Step 6: Commit**

```
git add Src/Views/MainWindow.axaml
git commit -m "feat: friendly SN-Drums Editor tab + Advanced — tabs"
```

---

## Done criteria

- Full suite green (237).
- Selecting an SN Drum Kit preset opens the friendly Editor by default; header, the 62-note rail with selection, the FX panel, and Advanced-tab navigation all work.
- Drum / Comp-EQ tab bodies are labelled stubs (Phases 2–3).
