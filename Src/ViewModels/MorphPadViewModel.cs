using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Integra7AuralAlchemist.Controls;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One corner of the pad: which library tone sits on it, and the colour its number is drawn in.
///
/// The colour comes out of the application's own resources rather than being written here, so the row in
/// the list and the marker on the disc are painted from the one set of brushes in <c>App.axaml</c>. The
/// Pick button is a callback rather than a command of its own, the way <c>LibraryTagViewModel</c> reaches
/// its filter: a corner knows which corner it is and nothing else.</summary>
public sealed partial class MorphCornerViewModel : ViewModelBase
{
    /// <summary>What an empty corner says. A phrase rather than a blank, because a blank row beside a
    /// Pick button reads as a list that failed to load.</summary>
    private const string Empty = "— nothing yet —";

    private readonly Func<MorphCornerViewModel, Task> _pick;

    internal MorphCornerViewModel(int number, Func<MorphCornerViewModel, Task> pick)
    {
        Number = number;
        _pick = pick;
        Colour = ColourFor(number);
    }

    /// <summary>1-based, which is what the disc draws and what every message here says.</summary>
    public int Number { get; }

    public IBrush Colour { get; }

    [Reactive] private string _name = Empty;

    public string? FilePath { get; private set; }

    public Integra7Snapshot? Snapshot { get; private set; }

    public bool IsFilled => Snapshot is not null;

    public void Put(string filePath, Integra7Snapshot snapshot)
    {
        FilePath = filePath;
        Snapshot = snapshot;
        // The sound's own name, not the file's: two files can hold the same patch, and the name inside is
        // what the library lists it under.
        Name = snapshot.Name.Trim().Length > 0 ? snapshot.Name : Path.GetFileName(filePath);
        this.RaisePropertyChanged(nameof(IsFilled));
    }

    public void Clear()
    {
        FilePath = null;
        Snapshot = null;
        Name = Empty;
        this.RaisePropertyChanged(nameof(IsFilled));
    }

    public async Task PickAsync() => await _pick(this);

    /// <summary>Corner <paramref name="number"/>'s brush from <c>App.axaml</c>. White when there is no
    /// application to ask -- a test -- or when the resource has been renamed: white is legible on every
    /// panel in this application, and a corner drawn in the wrong colour is better than one that throws.
    /// </summary>
    private static IBrush ColourFor(int number) =>
        Application.Current?.Resources.TryGetResource($"SnMorphCorner{number}Brush", null, out var found)
        == true && found is IBrush brush
            ? brush
            : Brushes.White;
}

/// <summary>The Morph tab: two to seven library tones on a disc, and a point inside it blending them into
/// the selected part's tone as it moves.
///
/// <b>Everything that can be got wrong is somewhere else.</b> The weights are
/// <see cref="MorphWeights"/>'s, the sticky leader <see cref="MorphWinner"/>'s, the blend
/// <see cref="MorphedTone"/>'s, the transmission <see cref="MorphWriter"/>'s and the file
/// <see cref="MorphPadFile"/>'s -- all pure or nearly so, and all tested. What is left here is state, a
/// throttle, and the handful of things a button asks for, which is the split every tab in this
/// application keeps for the same reason: a view model cannot be tested here.
///
/// <b>Nothing on this screen is recorded in the edit journal.</b> The first blend written after arriving
/// clears it, for the reason a tone load does -- the steps in it name parameters of a tone that is no
/// longer loaded -- and after that a drag at four writes a second would fill the 200-step history in
/// under a minute with steps that each replay a whole tone. Undo is not part of this screen; the pad's
/// own position is how you go back.
///
/// The callbacks are the pattern <c>LibraryViewModel</c> and <c>CompareViewModel</c> already use: a view
/// model inside a tab has no window to reach for, so a file dialog, a device and a status bar all arrive
/// as functions.</summary>
public sealed partial class MorphPadViewModel : ViewModelBase, IDisposable
{
    /// <summary>Two at least -- one corner is not a blend -- and seven at most, which is what the disc has
    /// colours for.</summary>
    private const int MinCorners = 2;

    private const int MaxCorners = 7;

    private readonly Integra7Parameters _parameters;
    private readonly Func<string?, Task<string?>> _pickCorner;
    private readonly Func<Integra7Snapshot, Task> _writeBlend;
    private readonly Func<Integra7Snapshot, Task> _saveToLibrary;
    private readonly Func<bool, string, Task<string?>> _pickPadFile;
    private readonly Func<string> _libraryFolder;
    private readonly Action<string, bool> _report;

    private readonly MorphWinner _winner = new();

    /// <summary>Every reason to send a blend, coalesced. A position, a corner, a count -- they all mean
    /// "the sound has changed", and a later one supersedes an earlier one rather than queueing behind
    /// it.</summary>
    private readonly Subject<Unit> _positions = new();

    private readonly IDisposable _writeSub;

    /// <summary>Whether the "one corner is older than the rest" line has been said for this set of
    /// corners. It is true of every flush once it is true of one, and saying it four times a second would
    /// bury the message that mattered.</summary>
    private bool _saidIncomplete;

    /// <param name="parameters">This build's parameter database: what says which values may be averaged
    /// and which are labels. See <see cref="MorphedTone"/>.</param>
    /// <param name="pickCorner">Ask for a tone file, told which engine the pad is locked to, or null when
    /// it is not locked yet. Answers the path, "" for a file with no usable local path, or null for a
    /// cancellation -- the three-way result every picker in this application gives.</param>
    /// <param name="writeBlend">Send one blend to the instrument. The target part is not a parameter: the
    /// window resolves it from the part tab strip at the moment of the write, and the blend carries the
    /// engine that has to match it.</param>
    /// <param name="saveToLibrary">Put one blend into the library, asking what to call it.</param>
    /// <param name="pickPadFile">Ask for a pad file: true to save, false to open.</param>
    /// <param name="libraryFolder">Where the library is now. A function rather than a string because the
    /// user can move it while this tab is open, and a pad's corners are stored relative to it.</param>
    /// <param name="report">The window's status bar: the message, and whether it is a failure.</param>
    public MorphPadViewModel(
        Integra7Parameters parameters,
        Func<string?, Task<string?>> pickCorner,
        Func<Integra7Snapshot, Task> writeBlend,
        Func<Integra7Snapshot, Task> saveToLibrary,
        Func<bool, string, Task<string?>> pickPadFile,
        Func<string> libraryFolder,
        Action<string, bool> report)
    {
        _parameters = parameters;
        _pickCorner = pickCorner;
        _writeBlend = writeBlend;
        _saveToLibrary = saveToLibrary;
        _pickPadFile = pickPadFile;
        _libraryFolder = libraryFolder;
        _report = report;

        // One key, because a morph is one thing being written: a later position supersedes an earlier one
        // rather than joining a queue behind it. THROTTLE is the same 250 ms the knobs and the envelope
        // editors use, so the instrument sees a morph at the rate it sees any other drag.
        _writeSub = _positions
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Subscribe(async _ => await FlushAsync());

        // Both fire on subscription, which is what builds the first three corners and puts the first
        // (inert, because nothing is on them yet) flush in the pipe. After the subscription above, so
        // that nothing is pushed at a Subject nobody is listening to.
        this.WhenAnyValue(x => x.CornerCount).Subscribe(RebuildCorners);
        this.WhenAnyValue(x => x.Point).Subscribe(_ => _positions.OnNext(Unit.Default));
    }

    // ---- what is on the pad ---------------------------------------------------------------------------

    public ObservableCollection<MorphCornerViewModel> Corners { get; } = [];

    /// <summary>How many corners the disc has. Three to start with: two is a crossfade, which the user can
    /// ask for, and three is the smallest arrangement that shows what the pad is for.</summary>
    [Reactive] private int _cornerCount = 3;

    public IReadOnlyList<int> CornerCountOptions { get; } =
        [.. Enumerable.Range(MinCorners, MaxCorners - MinCorners + 1)];

    /// <summary>The corner count as a position in <see cref="CornerCountOptions"/>. The drop-down binds to
    /// this rather than to <see cref="CornerCount"/> through SelectedItem, because SelectedItem is typed
    /// as object and a two-way binding of one to an int leans on a conversion that would fail at run time
    /// rather than at build time. An index is an int at both ends.</summary>
    public int CornerCountIndex
    {
        get => CornerCount - MinCorners;
        set
        {
            // A ComboBox reports -1 while it has no selection, which is not a request for two corners.
            if (value < 0) return;
            CornerCount = Math.Clamp(value + MinCorners, MinCorners, MaxCorners);
        }
    }

    /// <summary>The pointer, in the unit-circle space <see cref="MorphWeights"/> speaks. Two-way with the
    /// disc, so a position restored from a pad moves the marker and a drag moves this.</summary>
    [Reactive] private Point _point;

    /// <summary>The engine every corner has to be, taken from the first corner filled and released when
    /// the pad is emptied. All corners share one because these are the temporary tone's own addresses --
    /// see <c>StudioSetSnapshotService.EnsureToneFitsPart</c>.</summary>
    [Reactive] private string _toneType = "";

    [Reactive] private string _summary = "";

    /// <summary>Which part a blend would land in, in words, set by the window. This tab has no part
    /// selector of its own on purpose -- the target is the part tab the rest of the application is on, so
    /// a second way to choose one would be a second answer to the same question -- but a screen that
    /// replaces a part's sound has to say which part.</summary>
    [Reactive] private string _targetPart = "";

    /// <summary>A whole sentence rather than a value beside a caption, because the panel it is shown in is
    /// narrow enough to clip a row and a wrapping sentence is what survives that.</summary>
    public string EngineLabel => ToneType.Length == 0
        ? "Any engine — the first corner picked decides."
        : $"Engine: {ToneType}. Every corner has to match it.";

    /// <summary>Whether there is a sound to send. Every corner, not merely two of them: a corner with
    /// nothing on it still takes its share of the point, so a pad with a hole in it has no blend to
    /// compute rather than a partial one.</summary>
    public bool CanMorph => Corners.Count >= MinCorners && Corners.All(c => c.IsFilled);

    public bool CanSavePad => Corners.Any(c => c.IsFilled);

    // ---- the corners ----------------------------------------------------------------------------------

    private void RebuildCorners(int count)
    {
        count = Math.Clamp(count, MinCorners, MaxCorners);

        // Grown and shrunk rather than rebuilt, so that going from five corners to six does not make the
        // user pick the first five again.
        while (Corners.Count > count) Corners.RemoveAt(Corners.Count - 1);
        while (Corners.Count < count) Corners.Add(new MorphCornerViewModel(Corners.Count + 1, PickCornerAsync));

        // The pointer has not moved, but what it is being weighed against has, so a leader carried over
        // would be the answer to a different question.
        _winner.Reset();
        _saidIncomplete = false;
        Refresh();
        _positions.OnNext(Unit.Default);
    }

    private async Task PickCornerAsync(MorphCornerViewModel corner)
    {
        UserActionLog.Action($"button: Pick corner {corner.Number} (morph)");

        var path = await _pickCorner(ToneType.Length == 0 ? null : ToneType);
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            _report("Could not use that file: it has no accessible local path.", true);
            return;
        }

        var (snapshot, problem) = await ReadCornerAsync(path, ToneType);
        if (snapshot is null)
        {
            _report(problem ?? "Could not use that file.", true);
            return;
        }

        corner.Put(path, snapshot);
        _winner.Reset();
        _saidIncomplete = false;
        Refresh();
        _positions.OnNext(Unit.Default);
    }

    /// <summary>Read one corner's file and judge whether it may sit on this pad. Answers the snapshot, or
    /// null and a sentence written for the user.
    ///
    /// <b>The refusals are the whole point of this method.</b> A drum kit is 62 or 88 independent notes,
    /// and blending them mixes unrelated sounds into a kit that is no longer a kit; a tone of another
    /// engine cannot share a corner with these because they are the same addresses meaning different
    /// things. Both are enforced here rather than explained anywhere, and both a Pick and a pad being
    /// loaded come through here so that a hand-edited pad cannot get past what the picker refuses.</summary>
    private static async Task<(Integra7Snapshot? Snapshot, string? Problem)> ReadCornerAsync(string path,
        string engine)
    {
        var file = Path.GetFileName(path);

        Integra7Snapshot snapshot;
        try
        {
            // Awaited rather than read synchronously: this runs on a click, and a Studio Set picked by
            // mistake is large enough that parsing it on the UI thread is visible.
            snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"read '{path}' for a morph corner", e.ToString());
            return (null, e is SnapshotFormatException ? e.Message : $"Could not read \"{file}\": {e.Message}");
        }

        if (snapshot.Kind != SnapshotKinds.Tone)
            return (null, $"\"{file}\" is a Studio Set, not a tone. A morph pad blends tones.");

        if (snapshot.ToneType is not { } toneType || !ToneDomainNames.IsKnownToneType(toneType))
            return (null, $"\"{file}\" names no tone type this build recognises (\"{snapshot.ToneType}\").");

        if (ToneDomainNames.IsDrumKit(toneType))
            return (null, $"\"{file}\" is a drum kit. A kit is dozens of independent notes, and blending " +
                          "them produces a kit that is no longer one, so the pad takes tones only.");

        if (engine.Length > 0 && toneType != engine)
            return (null, $"This pad holds {engine} tones and \"{file}\" is {toneType}. Every corner has to " +
                          "be the same engine; empty the pad to start it on another one.");

        return (snapshot, null);
    }

    /// <summary>Take every corner off the pad, which is also what unlocks its engine. The one way back
    /// from a first corner picked by mistake -- without it the pad is locked to that engine for as long as
    /// the application is open.</summary>
    public void ClearPad()
    {
        UserActionLog.Action("button: Clear the pad (morph)");
        foreach (var corner in Corners) corner.Clear();
        _winner.Reset();
        _saidIncomplete = false;
        Refresh();
    }

    // ---- the blend ------------------------------------------------------------------------------------

    /// <summary>A blend and what it is made of: which corner won the discrete values, and by how much of
    /// the point it holds, which is what the status line says.</summary>
    private sealed record Blended(Integra7Snapshot Snapshot, int Winner, string WinnerName, double Share,
        bool Incomplete);

    private Blended? BuildBlend()
    {
        // Copied onto the stack first: this runs on the throttle's own thread, and the collection belongs
        // to the UI thread, which may be adding a corner to it.
        var corners = Corners.ToList();
        if (corners.Count < MinCorners || corners.Exists(c => c.Snapshot is null)) return null;

        // Clamped, because a pad file can name a position outside the disc and the marker is drawn
        // clamped -- the sound and the picture have to be answers to the same question.
        var weights = MorphWeights.For(MorphPadGeometry.Clamp(Point), MorphWeights.Corners(corners.Count));
        var winner = _winner.Winner(weights);
        var blend = MorphedTone.Blend([.. corners.Select(c => c.Snapshot!)], weights, winner, _parameters,
            out var incomplete);

        return new Blended(blend, winner, corners[winner].Name, weights[winner], incomplete);
    }

    private async Task FlushAsync()
    {
        Blended? built;
        try
        {
            built = BuildBlend();
        }
        catch (Exception e)
        {
            // A blend is arithmetic over the parameter database; nothing here is expected to throw, and a
            // failure that escaped an async subscription would take the process with it.
            Log.Error(e, "Building a morph blend failed.");
            Report("Could not work out the morph for that position.", true);
            return;
        }

        if (built is not { } blended) return; // nothing on the pad yet, which the summary already says

        OnUiThread(() => Summary =
            $"Corner {blended.Winner + 1} ({blended.WinnerName}) leads, with " +
            $"{blended.Share.ToString("P0", CultureInfo.CurrentCulture)} of the point.");

        if (blended.Incomplete && !_saidIncomplete)
        {
            _saidIncomplete = true;
            Report("One corner does not carry every parameter the others do — an older snapshot — so those " +
                   "values come from the leading corner rather than being blended.", true);
        }

        try
        {
            await _writeBlend(blended.Snapshot);
        }
        catch (Exception e)
        {
            // Reported and dropped: the next flush overwrites the part wholesale, so there is no
            // half-applied state to unpick and nothing to retry.
            Log.Error(e, "Sending a morph blend failed.");
            Report($"Could not send the morph: {e.Message}", true);
        }
    }

    public async Task SaveBlendAsync()
    {
        UserActionLog.Action("button: Save blend to library (morph)");
        if (BuildBlend() is not { } blended)
        {
            _report("There is no blend to save yet: every corner needs a tone on it.", true);
            return;
        }

        await _saveToLibrary(blended.Snapshot);
    }

    // ---- the pad file ---------------------------------------------------------------------------------

    public async Task SavePadAsync()
    {
        UserActionLog.Action("button: Save pad (morph)");
        if (!CanSavePad)
        {
            _report("There is nothing to save yet: the pad has no corners on it.", true);
            return;
        }

        var path = await _pickPadFile(true, SnapshotLibrary.FileNameFor($"{ToneType} morph"));
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            _report("Could not save the pad: the selected file has no accessible local path.", true);
            return;
        }

        var library = _libraryFolder();
        // An empty corner is stored as an empty name rather than dropped, so that corner 4 of a saved pad
        // is corner 4 of the loaded one.
        var pad = new MorphPad(ToneType,
            [.. Corners.Select(c => c.FilePath is { } file ? MorphPadFile.RelativeName(library, file) : "")],
            Point.X, Point.Y);

        try
        {
            MorphPadFile.Save(path, pad);
            _report($"Saved the pad as {Path.GetFileName(path)}.", false);
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"save the morph pad '{path}'", e.ToString());
            _report($"Could not save the pad: {e.Message}", true);
        }
    }

    public async Task LoadPadAsync()
    {
        UserActionLog.Action("button: Load pad (morph)");

        var path = await _pickPadFile(false, "");
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            _report("Could not load the pad: the selected file has no accessible local path.", true);
            return;
        }

        MorphPad pad;
        try
        {
            pad = MorphPadFile.Load(path);
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"read the morph pad '{path}'", e.ToString());
            _report(e is SnapshotFormatException ? e.Message : $"Could not read that pad: {e.Message}", true);
            return;
        }

        // The corners first, so the collection is the size the pad describes before anything goes into it.
        // Assigning the same count changes nothing and raises nothing, which is why the reset below is
        // done here rather than left to RebuildCorners.
        CornerCount = Math.Clamp(pad.CornerFiles.Count, MinCorners, MaxCorners);

        var library = _libraryFolder();
        List<string> refused = [];
        for (var i = 0; i < Corners.Count; i++)
        {
            Corners[i].Clear();
            if (i >= pad.CornerFiles.Count || pad.CornerFiles[i].Length == 0) continue;

            var file = MorphPadFile.Resolve(library, pad.CornerFiles[i]);
            // Judged against the pad's own engine rather than against the corners loaded so far, so that a
            // pad whose first corner has gone missing still refuses the rest if they disagree with it.
            var (snapshot, problem) = await ReadCornerAsync(file, pad.ToneType);
            if (snapshot is null)
            {
                refused.Add($"{i + 1} ({problem})");
                continue;
            }

            Corners[i].Put(file, snapshot);
        }

        // Clamped for the same reason the blend clamps it: a hand-edited pad can name a position outside
        // the disc, and the marker is drawn where a drag would have to put it.
        Point = MorphPadGeometry.Clamp(new Point(pad.X, pad.Y));

        // After the corners and the point, and unconditionally: a restored position must resolve from the
        // weights alone, or the pad would sound like wherever the pointer happened to have been left.
        _winner.Reset();
        _saidIncomplete = false;
        Refresh();
        _positions.OnNext(Unit.Default);

        var file2 = Path.GetFileName(path);
        _report(refused.Count == 0
                ? $"Loaded the pad {file2}."
                : $"Loaded {file2}, but corner {string.Join("; corner ", refused)}",
            refused.Count > 0);
    }

    // ---- plumbing -------------------------------------------------------------------------------------

    /// <summary>Recompute everything derived from the corners. One method rather than raises scattered
    /// through the callers, because every one of them changes the same four answers.</summary>
    private void Refresh()
    {
        // From the first corner filled, and back to nothing when none is: that is what lets a pad emptied
        // by mistake be started again on another engine.
        ToneType = Corners.FirstOrDefault(c => c.IsFilled)?.Snapshot?.ToneType ?? "";

        this.RaisePropertyChanged(nameof(CanMorph));
        this.RaisePropertyChanged(nameof(CanSavePad));
        this.RaisePropertyChanged(nameof(EngineLabel));
        this.RaisePropertyChanged(nameof(CornerCountIndex));
        Summary = Describe();
    }

    private string Describe()
    {
        var empty = Corners.Where(c => !c.IsFilled).Select(c => c.Number).ToList();
        if (empty.Count == Corners.Count)
            return "Pick a library tone for each corner. Two to seven of them, all the same engine, and " +
                   "the point between them is the sound.";

        if (empty.Count > 0)
            return empty.Count == 1
                ? $"Corner {empty[0]} is still empty."
                : $"Corners {string.Join(", ", empty)} are still empty.";

        return $"{Corners.Count} {ToneType} corners. Drag the point to morph the selected part.";
    }

    /// <summary>Say something on the window's status bar from whichever thread noticed it. A flush arrives
    /// on the throttle's own thread and the status bar is the UI thread's, which is the same reason the
    /// window posts its journal handler rather than calling it.</summary>
    private void Report(string message, bool failed) =>
        Dispatcher.UIThread.Post(() => _report(message, failed));

    private static void OnUiThread(Action action) => Dispatcher.UIThread.Post(action);

    public void Dispose()
    {
        _writeSub.Dispose();
        _positions.Dispose();
    }
}
