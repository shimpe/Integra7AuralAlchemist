# Envelope/Filter Previews on PCMS + SN-S Partial Cards — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add small read-only pitch/filter-shape/filter-env/amp-env preview thumbnails to the PCM-Synth partial cards, consistent with the SuperNATURAL-Synth cards, and add the missing pitch-env preview to the SN-S cards — each envelope type in its own colour (pitch=purple, filter=amber, amp=blue).

**Architecture:** Reuse the existing `Preview="True"` graph controls (`FilterCurveControl`, `MultiStageEnvelopeControl`, `PitchEnvelopeControl`, `DualAdsrEnvelopeControl`). Add one preview-only `DualMultiStageEnvelopeControl` (parallels `DualAdsrEnvelopeControl`, reusing the unit-tested `PcmEnvelopeMapping`) for the PCMS amp+filter overlay. All previews bind to existing partial-VM params, so they update live; no view-model changes.

**Tech Stack:** Avalonia 12 (XAML + a rendering `Control`); C# / .NET 10.

**Build/test commands** (user-local .NET 10 SDK; the system `dotnet` is too old):
- Build: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
- Full tests: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release` → expect `Passed! - Failed: 0, Passed: 281` (no new tests; confirm no regressions).

**Standing constraints:** never `git --no-verify`; this work is on branch `partial-card-previews` (already created off `main`). Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Transient `Permission denied` on `.git/objects` (Windows AV) — retry the commit once. There is an unrelated uncommitted change to `Src/Models/Services/AsyncMidiInputWrapper.cs` — **do NOT stage or commit it**; use explicit file paths in every `git add`.

---

## File Structure

- **Create** `Src/Controls/DualMultiStageEnvelopeControl.cs` — preview-only overlay of two PCM multi-stage envelopes (amp + filter) on shared axes.
- **Modify** `Src/Views/PCMSynthToneEditorView.axaml` — add the three preview rows to the partial-card template.
- **Modify** `Src/Views/SNSynthToneEditorView.axaml` — insert the pitch-env preview row into the partial-card template.

No view-model changes (every binding already exists).

---

## Task 1: `DualMultiStageEnvelopeControl`

A non-interactive control that draws the TVA amp + TVF filter multi-stage envelopes overlaid on shared axes, mirroring `DualAdsrEnvelopeControl` but for PCM 5-point envelopes. It is preview-only by construction (no handles, no pointer/keyboard), so it has no `Preview` flag (the spec mentioned one; it would be a no-op here — the control is always a preview). Card usage sets `IsHitTestVisible="False"` so clicks pass through to select the card.

**Files:**
- Create: `Src/Controls/DualMultiStageEnvelopeControl.cs`

> **Note on testing:** thin rendering over the existing unit-tested `PcmEnvelopeMapping.ComputePoints`; no new pure logic. The project has no headless-UI harness, so verification is build + (after Tasks 2–3) manual. No new unit test.

- [ ] **Step 1: Create the control**

Create `Src/Controls/DualMultiStageEnvelopeControl.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>
/// Two 4-segment rate/level envelopes (Amp + Filter) overlaid on ONE shared axis as a non-interactive
/// preview thumbnail (the PCM-Synth partial card). The multi-stage analogue of
/// <see cref="DualAdsrEnvelopeControl"/>; pure geometry is delegated to <see cref="PcmEnvelopeMapping"/>.
/// </summary>
public class DualMultiStageEnvelopeControl : Control
{
    private static StyledProperty<int> I(string name) =>
        AvaloniaProperty.Register<DualMultiStageEnvelopeControl, int>(name);

    public static readonly StyledProperty<int> AmpTime1Property = I(nameof(AmpTime1));
    public static readonly StyledProperty<int> AmpTime2Property = I(nameof(AmpTime2));
    public static readonly StyledProperty<int> AmpTime3Property = I(nameof(AmpTime3));
    public static readonly StyledProperty<int> AmpTime4Property = I(nameof(AmpTime4));
    public static readonly StyledProperty<int> AmpLevel0Property = I(nameof(AmpLevel0));
    public static readonly StyledProperty<int> AmpLevel1Property = I(nameof(AmpLevel1));
    public static readonly StyledProperty<int> AmpLevel2Property = I(nameof(AmpLevel2));
    public static readonly StyledProperty<int> AmpLevel3Property = I(nameof(AmpLevel3));
    public static readonly StyledProperty<int> AmpLevel4Property = I(nameof(AmpLevel4));
    public static readonly StyledProperty<int> FilterTime1Property = I(nameof(FilterTime1));
    public static readonly StyledProperty<int> FilterTime2Property = I(nameof(FilterTime2));
    public static readonly StyledProperty<int> FilterTime3Property = I(nameof(FilterTime3));
    public static readonly StyledProperty<int> FilterTime4Property = I(nameof(FilterTime4));
    public static readonly StyledProperty<int> FilterLevel0Property = I(nameof(FilterLevel0));
    public static readonly StyledProperty<int> FilterLevel1Property = I(nameof(FilterLevel1));
    public static readonly StyledProperty<int> FilterLevel2Property = I(nameof(FilterLevel2));
    public static readonly StyledProperty<int> FilterLevel3Property = I(nameof(FilterLevel3));
    public static readonly StyledProperty<int> FilterLevel4Property = I(nameof(FilterLevel4));

    private static StyledProperty<IBrush> B(string name, IBrush def) =>
        AvaloniaProperty.Register<DualMultiStageEnvelopeControl, IBrush>(name, def);

    public static readonly StyledProperty<IBrush> AmpLineBrushProperty = B(nameof(AmpLineBrush), new SolidColorBrush(Color.Parse("#7FB6E0")));
    public static readonly StyledProperty<IBrush> AmpFillBrushProperty = B(nameof(AmpFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0x3d, 0x7e, 0xaa)));
    public static readonly StyledProperty<IBrush> FilterLineBrushProperty = B(nameof(FilterLineBrush), new SolidColorBrush(Color.Parse("#E0A23D")));
    public static readonly StyledProperty<IBrush> FilterFillBrushProperty = B(nameof(FilterFillBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xb0, 0x7a, 0x1e)));
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty = B(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#1B1F22")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = B(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff)));
    public static readonly StyledProperty<IBrush> AxisBrushProperty = B(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)));

    public int AmpTime1 { get => GetValue(AmpTime1Property); set => SetValue(AmpTime1Property, value); }
    public int AmpTime2 { get => GetValue(AmpTime2Property); set => SetValue(AmpTime2Property, value); }
    public int AmpTime3 { get => GetValue(AmpTime3Property); set => SetValue(AmpTime3Property, value); }
    public int AmpTime4 { get => GetValue(AmpTime4Property); set => SetValue(AmpTime4Property, value); }
    public int AmpLevel0 { get => GetValue(AmpLevel0Property); set => SetValue(AmpLevel0Property, value); }
    public int AmpLevel1 { get => GetValue(AmpLevel1Property); set => SetValue(AmpLevel1Property, value); }
    public int AmpLevel2 { get => GetValue(AmpLevel2Property); set => SetValue(AmpLevel2Property, value); }
    public int AmpLevel3 { get => GetValue(AmpLevel3Property); set => SetValue(AmpLevel3Property, value); }
    public int AmpLevel4 { get => GetValue(AmpLevel4Property); set => SetValue(AmpLevel4Property, value); }
    public int FilterTime1 { get => GetValue(FilterTime1Property); set => SetValue(FilterTime1Property, value); }
    public int FilterTime2 { get => GetValue(FilterTime2Property); set => SetValue(FilterTime2Property, value); }
    public int FilterTime3 { get => GetValue(FilterTime3Property); set => SetValue(FilterTime3Property, value); }
    public int FilterTime4 { get => GetValue(FilterTime4Property); set => SetValue(FilterTime4Property, value); }
    public int FilterLevel0 { get => GetValue(FilterLevel0Property); set => SetValue(FilterLevel0Property, value); }
    public int FilterLevel1 { get => GetValue(FilterLevel1Property); set => SetValue(FilterLevel1Property, value); }
    public int FilterLevel2 { get => GetValue(FilterLevel2Property); set => SetValue(FilterLevel2Property, value); }
    public int FilterLevel3 { get => GetValue(FilterLevel3Property); set => SetValue(FilterLevel3Property, value); }
    public int FilterLevel4 { get => GetValue(FilterLevel4Property); set => SetValue(FilterLevel4Property, value); }
    public IBrush AmpLineBrush { get => GetValue(AmpLineBrushProperty); set => SetValue(AmpLineBrushProperty, value); }
    public IBrush AmpFillBrush { get => GetValue(AmpFillBrushProperty); set => SetValue(AmpFillBrushProperty, value); }
    public IBrush FilterLineBrush { get => GetValue(FilterLineBrushProperty); set => SetValue(FilterLineBrushProperty, value); }
    public IBrush FilterFillBrush { get => GetValue(FilterFillBrushProperty); set => SetValue(FilterFillBrushProperty, value); }
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    static DualMultiStageEnvelopeControl()
    {
        AffectsRender<DualMultiStageEnvelopeControl>(
            AmpTime1Property, AmpTime2Property, AmpTime3Property, AmpTime4Property,
            AmpLevel0Property, AmpLevel1Property, AmpLevel2Property, AmpLevel3Property, AmpLevel4Property,
            FilterTime1Property, FilterTime2Property, FilterTime3Property, FilterTime4Property,
            FilterLevel0Property, FilterLevel1Property, FilterLevel2Property, FilterLevel3Property, FilterLevel4Property,
            AmpLineBrushProperty, AmpFillBrushProperty, FilterLineBrushProperty, FilterFillBrushProperty,
            BackgroundBrushProperty, GridBrushProperty, AxisBrushProperty);
    }

    private PcmEnvelopeMapping.Point[] AmpPoints(double w, double h) =>
        PcmEnvelopeMapping.ComputePoints(AmpTime1, AmpTime2, AmpTime3, AmpTime4,
            AmpLevel0, AmpLevel1, AmpLevel2, AmpLevel3, AmpLevel4, w, h, false);

    private PcmEnvelopeMapping.Point[] FilterPoints(double w, double h) =>
        PcmEnvelopeMapping.ComputePoints(FilterTime1, FilterTime2, FilterTime3, FilterTime4,
            FilterLevel0, FilterLevel1, FilterLevel2, FilterLevel3, FilterLevel4, w, h, false);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, w, h));

        // Grid + axes, matching MultiStageEnvelopeControl (unipolar, a line every 16 levels).
        var gridPen = new Pen(GridBrush);
        var axisPen = new Pen(AxisBrush);
        for (var level = 0; level <= PcmEnvelopeMapping.Max; level += 16)
        {
            var y = PcmEnvelopeMapping.LevelToY(level, h, false);
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            context.DrawLine(axisPen, new Point(0, y), new Point(6, y));
        }
        context.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        context.DrawLine(axisPen, new Point(0, h), new Point(w, h));

        // Filter behind, amp in front (both shown equally — a thumbnail).
        DrawEnvelope(context, FilterPoints(w, h), FilterLineBrush, FilterFillBrush, h);
        DrawEnvelope(context, AmpPoints(w, h), AmpLineBrush, AmpFillBrush, h);
    }

    private static void DrawEnvelope(DrawingContext ctx, PcmEnvelopeMapping.Point[] p, IBrush line, IBrush fill, double h)
    {
        var fillGeo = new StreamGeometry();
        using (var c = fillGeo.Open())
        {
            c.BeginFigure(new Point(p[0].X, h), true);
            c.LineTo(new Point(p[0].X, p[0].Y));
            c.LineTo(new Point(p[1].X, p[1].Y));
            c.LineTo(new Point(p[2].X, p[2].Y));
            c.LineTo(new Point(p[3].X, p[3].Y));
            c.LineTo(new Point(p[4].X, p[4].Y));
            c.LineTo(new Point(p[4].X, h));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, fillGeo);

        var lineGeo = new StreamGeometry();
        using (var c = lineGeo.Open())
        {
            c.BeginFigure(new Point(p[0].X, p[0].Y), false);
            c.LineTo(new Point(p[1].X, p[1].Y));
            c.LineTo(new Point(p[2].X, p[2].Y));
            c.LineTo(new Point(p[3].X, p[3].Y));
            c.LineTo(new Point(p[4].X, p[4].Y));
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(line, 1.5), lineGeo);
    }
}
```

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Controls/DualMultiStageEnvelopeControl.cs
git commit -m "feat: DualMultiStageEnvelopeControl — preview overlay of two PCM envelopes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: PCMS partial-card previews

Add the three preview rows (filter shape, pitch env, amp+filter overlay) under the card's text rows.

**Files:**
- Modify: `Src/Views/PCMSynthToneEditorView.axaml`

> **Note on testing:** XAML; verification is build + manual. No unit test.

- [ ] **Step 1: Insert the preview rows**

In `Src/Views/PCMSynthToneEditorView.axaml`, **Read** the partial-card `DataTemplate` (around lines 156–190) for exact whitespace. Find the card's "Filter" summary row:

```xml
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Filter" Opacity="0.6"/>
                                    <TextBlock Text="{Binding FilterSummary}"/>
                                </StackPanel>
```

Replace it (a single Edit) with that same block followed by the three preview rows:

```xml
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Filter" Opacity="0.6"/>
                                    <TextBlock Text="{Binding FilterSummary}"/>
                                </StackPanel>
                                <!-- Preview thumbnails (non-interactive), consistent with the SN-S cards.
                                     IsHitTestVisible=False so clicks pass through to select the card. -->
                                <controls:FilterCurveControl Height="40" Preview="True" IsHitTestVisible="False" Opacity="0.85"
                                    Mode="{Binding FilterCurveMode}" Steep="{Binding FilterCurveSteep}"
                                    Cutoff="{Binding TvfCutoff.Value}" Resonance="{Binding TvfResonance.Value}"
                                    LineBrush="{StaticResource SnFilterEnvelopeBrush}" FillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}" GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                                <TextBlock Text="filter shape (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
                                <controls:MultiStageEnvelopeControl Height="40" Bipolar="True" Preview="True" IsHitTestVisible="False" Opacity="0.85"
                                    Time1="{Binding PitchEnvTime1.Value}" Time2="{Binding PitchEnvTime2.Value}"
                                    Time3="{Binding PitchEnvTime3.Value}" Time4="{Binding PitchEnvTime4.Value}"
                                    Level0="{Binding PitchEnvLevel0.Value}" Level1="{Binding PitchEnvLevel1.Value}"
                                    Level2="{Binding PitchEnvLevel2.Value}" Level3="{Binding PitchEnvLevel3.Value}"
                                    Level4="{Binding PitchEnvLevel4.Value}"
                                    LineBrush="{StaticResource SnPitchEnvelopeBrush}" FillBrush="{StaticResource SnPitchEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}" GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                                <TextBlock Text="pitch env (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
                                <controls:DualMultiStageEnvelopeControl Height="44" IsHitTestVisible="False" Opacity="0.85"
                                    AmpTime1="{Binding TvaEnvTime1.Value}" AmpTime2="{Binding TvaEnvTime2.Value}"
                                    AmpTime3="{Binding TvaEnvTime3.Value}" AmpTime4="{Binding TvaEnvTime4.Value}"
                                    AmpLevel0="0" AmpLevel1="{Binding TvaEnvLevel1.Value}" AmpLevel2="{Binding TvaEnvLevel2.Value}"
                                    AmpLevel3="{Binding TvaEnvLevel3.Value}" AmpLevel4="0"
                                    FilterTime1="{Binding TvfEnvTime1.Value}" FilterTime2="{Binding TvfEnvTime2.Value}"
                                    FilterTime3="{Binding TvfEnvTime3.Value}" FilterTime4="{Binding TvfEnvTime4.Value}"
                                    FilterLevel0="{Binding TvfEnvLevel0.Value}" FilterLevel1="{Binding TvfEnvLevel1.Value}"
                                    FilterLevel2="{Binding TvfEnvLevel2.Value}" FilterLevel3="{Binding TvfEnvLevel3.Value}"
                                    FilterLevel4="{Binding TvfEnvLevel4.Value}"
                                    AmpLineBrush="{StaticResource SnAmpEnvelopeBrush}" AmpFillBrush="{StaticResource SnAmpEnvelopeFillBrush}"
                                    FilterLineBrush="{StaticResource SnFilterEnvelopeBrush}" FilterFillBrush="{StaticResource SnFilterEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}" GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                                <TextBlock Text="amp (blue) + filter (amber) env (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
```

(The `controls:` namespace and these `Sn…Brush` resources are already used elsewhere in this file's Sound tab, so no new namespaces/resources are needed. `FilterCurveMode`/`FilterCurveSteep` are computed string/bool properties on `PCMPartialViewModel` bound without `.Value`, exactly as the detail tab does; all others are `ParamInt` → `.Value`.)

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`. (Avalonia compiles XAML at build time; an unresolved binding/type fails here.)

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Views/PCMSynthToneEditorView.axaml
git commit -m "feat: envelope/filter preview thumbnails on PCM Synth partial cards

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: SN-S partial-card pitch-env preview

Insert a pitch-envelope preview into the SN-S card, between the existing filter-shape preview and the amp/filter overlay, so the SN-S card matches the PCMS order (filter shape → pitch → amp+filter).

**Files:**
- Modify: `Src/Views/SNSynthToneEditorView.axaml`

> **Note on testing:** XAML; verification is build + manual. No unit test.

- [ ] **Step 1: Insert the pitch-env preview row**

In `Src/Views/SNSynthToneEditorView.axaml`, **Read** the partial-card `DataTemplate` (around lines 177–198). Find the filter-shape caption immediately followed by the amp/filter overlay comment:

```xml
                                <TextBlock Text="filter shape (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
                                <!-- Row 2: amp + filter envelopes overlaid on shared axes. -->
```

Replace it (a single Edit) with the same caption, the new pitch-env preview + caption, then the comment:

```xml
                                <TextBlock Text="filter shape (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
                                <!-- Pitch envelope preview (purple), consistent with the PCM Synth cards. -->
                                <controls:PitchEnvelopeControl Height="40" Preview="True" IsHitTestVisible="False" Opacity="0.85"
                                    Attack="{Binding PitchEnvAttack.Value}" Decay="{Binding PitchEnvDecay.Value}" Depth="{Binding PitchEnvDepth.Value}"
                                    LineBrush="{StaticResource SnPitchEnvelopeBrush}" FillBrush="{StaticResource SnPitchEnvelopeFillBrush}"
                                    BackgroundBrush="{StaticResource SnEnvelopeBackgroundBrush}" GridBrush="{StaticResource SnEnvelopeGridBrush}"
                                    AxisBrush="{StaticResource SnEnvelopeAxisBrush}"/>
                                <TextBlock Text="pitch env (preview)" FontSize="10" Opacity="0.45" HorizontalAlignment="Center"/>
                                <!-- Row 2: amp + filter envelopes overlaid on shared axes. -->
```

- [ ] **Step 2: Build**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Run the full suite (no regressions)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test "D:/Projects/Integra7AuralAlchemist/Integra7AuralAlchemist.sln" --nologo --configuration Release`
Expected: `Passed! - Failed: 0, Passed: 281`.

- [ ] **Step 4: Commit**

```bash
git add Src/Views/SNSynthToneEditorView.axaml
git commit -m "feat: pitch-envelope preview on SuperNATURAL Synth partial cards

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Manual verification (with the app running):**
- Both editors' partial cards show, under the text rows, three previews in the same order/sizes: filter shape (amber), pitch env (purple), amp(blue)+filter(amber) overlay — each envelope type its own colour, matching between PCMS and SN-S.
- Editing a partial's envelope/filter in the Sound tab updates that card's previews live.
- Clicking anywhere on a card (including on a preview) still selects that partial.

---

## Final verification

- [ ] Full suite green (`Passed: 281`); `git log --oneline main..HEAD` shows the spec + Tasks 1–3 commits on `partial-card-previews`; `git status` shows only the unrelated `AsyncMidiInputWrapper.cs` change uncommitted.

After all tasks: dispatch a final review, then use superpowers:finishing-a-development-branch.

---

## Spec coverage (self-review)

- **Colour scheme** (pitch purple / filter amber / amp blue; filter-shape amber) → the brush resources used in Tasks 2 & 3 and the new control's amp/filter brushes.
- **Card layout** (filter shape → pitch → amp+filter overlay, same on both) → Task 2 (PCMS, all three) + Task 3 (SN-S, insert pitch in the same order).
- **New `DualMultiStageEnvelopeControl`** → Task 1. *Deviation:* no `Preview` flag — the control is inherently non-interactive (a flag would be a no-op); it mirrors `DualAdsrEnvelopeControl`'s amp/filter brush + shared-axes design otherwise.
- **Live updates / no VM changes** → all previews bind to existing `…​.Value` params on the partial VMs.
- **Out of scope** (Drum/SN-A editors, advanced grids, interactive detail controls, colour resources) → untouched.
