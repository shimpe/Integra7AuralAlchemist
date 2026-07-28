using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One side of a comparison: a snapshot and where it came from.
///
/// The provenance is held separately from the snapshot because the snapshot does not know it -- a file
/// and a capture of the same sound are the same bytes -- and because it is most of what makes a pasted
/// comparison meaningful. For a capture it includes the time, since "the instrument" means the instrument
/// *as it was then*.</summary>
public sealed partial class CompareSlotViewModel : ViewModelBase
{
    [Reactive] private Integra7Snapshot? _snapshot;
    [Reactive] private string _source = "";

    public bool IsFilled => Snapshot is not null;

    /// <summary>What the slot shows when it is empty, and what it shows when it is full.</summary>
    public string Description => Snapshot is { } s
        ? $"{s.Name} — {(s.Kind == SnapshotKinds.Tone ? $"tone, {s.ToneType}" : "Studio Set")} — {Source}"
        : "nothing chosen yet";

    public void Put(Integra7Snapshot snapshot, string source)
    {
        Snapshot = snapshot;
        Source = source;
        this.RaisePropertyChanged(nameof(IsFilled));
        this.RaisePropertyChanged(nameof(Description));
    }
}

/// <summary>One block's differences, as the result list shows them.</summary>
public sealed class CompareBlockViewModel(string heading, IReadOnlyList<ValueDifference> rows)
    : ViewModelBase
{
    public string Heading { get; } = heading;
    public IReadOnlyList<ValueDifference> Rows { get; } = rows;
}

/// <summary>Two snapshots side by side, and what differs between them.
///
/// <b>Reads only.</b> Every other feature that touches the instrument writes to it; this one captures and
/// compares, so there is no half-applied state to reason about and no confirmation to ask for.
///
/// The callbacks are the pattern <c>LibraryViewModel</c> already uses: a view model inside a tab has
/// no window to reach for, so anything needing one -- a file picker, the clipboard -- arrives as a
/// function.</summary>
public sealed partial class CompareViewModel : ViewModelBase
{
    private readonly Integra7Parameters _parameters;
    private readonly Func<Task<(Integra7Snapshot Snapshot, string Source)?>> _fromFile;
    private readonly Func<Task<(Integra7Snapshot Snapshot, string Source)?>> _fromLibrary;
    private readonly Func<bool, Task<(Integra7Snapshot Snapshot, string Source)?>> _fromInstrument;
    private readonly Func<string, Task> _copy;
    private readonly Func<string, Task<string?>> _saveText;
    private readonly Action<string, bool> _report;

    /// <param name="parameters">This build's parameter database, which is what names a stored value:
    /// the file says what the value is, and the database says what it is called. See
    /// <see cref="SnapshotValueNames"/>.</param>
    /// <param name="fromInstrument">True for the Studio Set, false for the tone in the selected part. One
    /// callback rather than two because the caller has to resolve the part either way, and the flag is
    /// what it already switches on.</param>
    public CompareViewModel(
        Integra7Parameters parameters,
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> fromFile,
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> fromLibrary,
        Func<bool, Task<(Integra7Snapshot Snapshot, string Source)?>> fromInstrument,
        Func<string, Task> copy,
        Func<string, Task<string?>> saveText,
        Action<string, bool> report)
    {
        _parameters = parameters;
        _fromFile = fromFile;
        _fromLibrary = fromLibrary;
        _fromInstrument = fromInstrument;
        _copy = copy;
        _saveText = saveText;
        _report = report;

        this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplyFilter());
    }

    public CompareSlotViewModel Left { get; } = new();
    public CompareSlotViewModel Right { get; } = new();

    /// <summary>Every block with differences, before the search box narrows it. Kept so that typing in
    /// the box does not re-run the comparison.</summary>
    private IReadOnlyList<CompareBlockViewModel> _allBlocks = [];

    public ObservableCollection<CompareBlockViewModel> Blocks { get; } = [];

    [Reactive] private string _searchText = "";
    [Reactive] private string _summary = "";
    [Reactive] private bool _hasResult;

    public bool CanCompare => Left.IsFilled && Right.IsFilled;

    /// <summary>What the last comparison rendered to, ready for the clipboard or a file. Held rather than
    /// re-rendered so that the text a user copies is the text they are looking at.</summary>
    private string _text = "";

    public async Task FillLeftFromFileAsync() => await FillAsync(Left, _fromFile);
    public async Task FillRightFromFileAsync() => await FillAsync(Right, _fromFile);
    public async Task FillLeftFromLibraryAsync() => await FillAsync(Left, _fromLibrary);
    public async Task FillRightFromLibraryAsync() => await FillAsync(Right, _fromLibrary);
    public async Task FillLeftFromStudioSetAsync() => await FillAsync(Left, () => _fromInstrument(true));
    public async Task FillRightFromStudioSetAsync() => await FillAsync(Right, () => _fromInstrument(true));
    public async Task FillLeftFromToneAsync() => await FillAsync(Left, () => _fromInstrument(false));
    public async Task FillRightFromToneAsync() => await FillAsync(Right, () => _fromInstrument(false));

    /// <summary>Put a snapshot into whichever slot is free, or the left one when both are. What the
    /// Library tab's "Compare this" button reaches.</summary>
    public void PutInFirstFreeSlot(Integra7Snapshot snapshot, string source)
    {
        (Left.IsFilled && !Right.IsFilled ? Right : Left).Put(snapshot, source);
        this.RaisePropertyChanged(nameof(CanCompare));
    }

    private async Task FillAsync(CompareSlotViewModel slot,
        Func<Task<(Integra7Snapshot Snapshot, string Source)?>> source)
    {
        // A cancelled picker or a failed capture leaves the slot exactly as it was: the previous contents
        // are still a side of a comparison the user may be halfway through setting up.
        if (await source() is not { } filled) return;

        slot.Put(filled.Snapshot, filled.Source);
        this.RaisePropertyChanged(nameof(CanCompare));
    }

    public void Compare()
    {
        if (Left.Snapshot is not { } left || Right.Snapshot is not { } right) return;

        SnapshotComparison comparison;
        try
        {
            comparison = SnapshotDiff.Compare(left, right);
        }
        catch (SnapshotFormatException e)
        {
            // Written for the user -- it names both kinds or both engines -- so it is shown as it is.
            _report(e.Message, true);
            return;
        }

        // Named before anything reads it, and once: the rows on screen and the exported text are then the
        // same strings by construction rather than by two places agreeing.
        comparison = Named(comparison);

        _text = ComparisonText.Format(comparison, Left.Source, Right.Source);
        _allBlocks =
        [
            .. comparison.Blocks
                .Where(b => b.Differences.Count > 0)
                .Select(b => new CompareBlockViewModel(
                    $"{b.Name}  ({b.Differences.Count})", b.Differences)),
        ];

        // The exported text's own summary line, not a second copy of it: they must agree, and when this
        // was written twice they did not -- the tab reported "0 differences across 0 blocks" for a
        // comparison whose only finding was a parameter one side does not carry.
        Summary = ComparisonText.Summary(comparison);
        HasResult = true;
        ApplyFilter();
        _report(Summary, false);
    }

    /// <summary>The same comparison with every difference rendered from its raw value through this build's
    /// parameter database.
    ///
    /// A snapshot's display strings are only as good as the build that wrote them: a parameter this build
    /// has since learned to name was stored as a bare number, and two such files compared to "36" against
    /// "36" where the answer is "Synth Pad/Strings". The comparison itself is left database-free -- what
    /// differs is still decided on the raw values alone -- so this is a rendering step and nothing
    /// more.</summary>
    private SnapshotComparison Named(SnapshotComparison comparison) =>
        comparison with
        {
            Blocks =
            [
                .. comparison.Blocks.Select(b => b with
                {
                    Differences = [.. b.Differences.Select(Named)],
                }),
            ],
        };

    private ValueDifference Named(ValueDifference difference) =>
        difference with
        {
            LeftValue = SnapshotValueNames.Best(_parameters, difference.Path, difference.LeftRaw,
                difference.LeftValue),
            RightValue = SnapshotValueNames.Best(_parameters, difference.Path, difference.RightRaw,
                difference.RightValue),
        };

    /// <summary>Narrow by parameter path across every section at once -- "cutoff" answers "what did I
    /// change about the filters" for all sixteen parts in one go. A section whose every row is filtered
    /// out disappears with them, rather than leaving an empty heading.</summary>
    private void ApplyFilter()
    {
        Blocks.Clear();
        var needle = SearchText.Trim();
        foreach (var block in _allBlocks)
        {
            if (needle.Length == 0)
            {
                Blocks.Add(block);
                continue;
            }

            var rows = block.Rows
                .Where(r => r.Path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rows.Count > 0) Blocks.Add(new CompareBlockViewModel(block.Heading, rows));
        }
    }

    public async Task CopyAsync()
    {
        if (_text.Length == 0) return;

        try
        {
            await _copy(_text);
            _report("Copied the comparison to the clipboard.", false);
        }
        catch (Exception e)
        {
            _report($"Could not copy the comparison: {e.Message}", true);
        }
    }

    public async Task SaveAsync()
    {
        if (_text.Length == 0) return;

        var path = await _saveText("comparison.txt");
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            _report("Could not save the comparison: the selected file has no accessible local path.", true);
            return;
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(path, _text);
            _report($"Saved the comparison to {System.IO.Path.GetFileName(path)}.", false);
        }
        catch (Exception e)
        {
            _report($"Could not save the comparison: {e.Message}", true);
        }
    }
}
