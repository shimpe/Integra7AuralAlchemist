using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One tick box on the selection screen, with what ticking it costs beside it.
///
/// <b>The number is on the row rather than only in the total.</b> A sweep is up to an hour of somebody's
/// instrument, and the whole reason this screen exists instead of a single Go button is that it can be aimed
/// -- which it cannot be if the thing being aimed at is a bare name. Three of these carry a sentence as well:
/// see <see cref="SeedRunViewModel"/> for why they are offered unticked rather than left out.
///
/// No ToolTip, per the rule this branch keeps for anything clicked repeatedly, and these are clicked in
/// columns of twenty-one.</summary>
public sealed class SeedOptionViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _isTicked;
    private int _patches;

    internal SeedOptionViewModel(string name, int patches, string note, bool isTicked, Action changed)
    {
        Name = name;
        _patches = patches;
        Note = note;
        _isTicked = isTicked;
        _changed = changed;
    }

    /// <summary>The engine or bank exactly as the preset table spells it, because that is also what
    /// <see cref="SeedSelection"/> is matched against -- a prettier label here would have to be translated
    /// back, and the translation is the thing that would come to disagree.</summary>
    public string Name { get; }

    /// <summary>How many rows of the catalogue this covers. Not how many will be captured: what is already in
    /// the library and what the internal/user choice leaves out are the total's business, further down the
    /// panel, because they change as the other ticks move.</summary>
    public string Cost => $"{_patches:n0} patches";

    /// <summary>Why this one is unticked by default, or "" for the ones that are not. Shown on the row rather
    /// than in a note at the bottom: the user is deciding about this tick, here.</summary>
    public string Note { get; }

    public bool IsTicked
    {
        get => _isTicked;
        set
        {
            if (_isTicked == value) return;
            this.RaiseAndSetIfChanged(ref _isTicked, value);
            _changed();
        }
    }

    /// <summary>The catalogue has grown -- the user's own tone names arrive from the instrument in the
    /// background, minutes after the window opens, and until they do the count for PRST is short by up to
    /// nine hundred. Assigned rather than rebuilt so that a panel already on screen keeps its ticks.</summary>
    internal void Recount(int patches)
    {
        if (_patches == patches) return;
        _patches = patches;
        this.RaisePropertyChanged(nameof(Cost));
    }
}

/// <summary>Sweeping the instrument into the library: what to capture, how far it has got, and what it did.
///
/// <b>A panel beside the library, not a dialog.</b> A full factory sweep is about 6,000 patches and 54
/// minutes; a modal would lock the user out of their own library for the whole of it, for no reason at all --
/// nothing this does needs the list to hold still. <c>DuplicateScanView</c> is the precedent and this is the
/// fourth panel in that same place.
///
/// <b>It is handed the instrument rather than holding one.</b> <see cref="LibraryViewModel"/> takes callbacks
/// only and knows nothing about a device; the window owns the API, the domain and the preset list, so this is
/// built there and passed in. Everything it needs arrives as a function and is called at the moment it is
/// needed, so a rescan replacing the connection or the user moving their library folder is invisible here.
///
/// <b>It has no tests, and that is why so little is decided in it.</b> A view model cannot be constructed in
/// a test under ReactiveUI 24, so every rule this feature has was pushed out into
/// <see cref="SeedSelection"/>, <see cref="SeedPlan"/>, <see cref="SeedRun"/>, <see cref="SeedNaming"/>,
/// <see cref="SeedBoards"/> and <see cref="SeedRefusal"/>, all six of which a test can reach without an
/// INTEGRA-7 on the desk. What is left in here is sequencing and words.
///
/// <b>The defaults are the spike's measurements, and the three unticked ones say why on the row.</b> GM2 and
/// ExPCM expose no temporary tone at all on the instrument this was measured against -- 796 rows, about
/// twenty minutes of reply deadlines to establish again -- and PCM drum kits are 40% of a full sweep's clock
/// and 137 MB of its bytes for 3.6% of its patches. They are offered unticked rather than left out because
/// another unit may differ, and a user who wants to check should be able to, cheaply, rather than be told
/// what their own instrument can do by a table written about somebody else's.
///
/// <b>The panel owns the clock.</b> <see cref="SeedProgress"/> carries counts and nothing else, deliberately;
/// elapsed comes from a stopwatch here and what is left from <see cref="SeedWork.Estimate"/>.
///
/// No ToolTip anywhere in the view, per the rule this branch keeps: the tick boxes are what this screen is
/// made of, and a tooltip is a popup that swallows the click on the control it describes.</summary>
public sealed partial class SeedRunViewModel : ViewModelBase
{
    /// <summary>The instrument to sweep, or null when nothing is connected. A function rather than an object
    /// because a MIDI rescan replaces both the API and the domain, and a sweep started an hour after the
    /// panel opened must use the connection that exists then.</summary>
    private readonly Func<ISeedInstrument?> _instrument;

    /// <summary>Every preset the application knows, read at the moment it is needed. The user's own tone
    /// names arrive from the instrument in the background well after the window opens, so a list captured at
    /// construction would be missing the whole of the user memory -- and missing it silently, which is the
    /// half of this feature the user is likeliest to actually want.</summary>
    private readonly Func<IReadOnlyList<Integra7Preset>> _presets;

    /// <summary>Where the library is, asked each time for the reason the morph pad asks: the user can move it
    /// while this tab is open, and a sweep must land where the browser is looking.</summary>
    private readonly Func<string> _folder;

    /// <summary>The run is over: re-read the folder. Called after a cancel as well as after a finish, because
    /// a cancelled sweep has still written everything it captured and a list that does not show those files
    /// is a list saying the last twenty minutes did nothing.</summary>
    private readonly Action _finished;

    /// <summary>Say something on the window's status bar -- the library's own reporter, handed through, so
    /// that this panel and the browser it sits beside cannot answer in two different places.</summary>
    private readonly Action<string, bool> _report;

    private readonly Action _close;

    /// <summary>What the library folder held when the facts were last gathered. The resume: a patch whose
    /// file name is already in here is not captured again.</summary>
    private IReadOnlyCollection<string> _existing = [];

    /// <summary>What the instrument's four expansion slots hold, so that a bank whose board is already in a
    /// slot costs no 23-second round.</summary>
    private IReadOnlyCollection<int> _boards = [];

    /// <summary>The latest report from the run, and the note about whatever it is doing that is not a patch.
    ///
    /// <b>Volatile fields written by the run and read by the timer, and this is the whole of the marshalling.
    /// </b> The progress handler sits outside <see cref="SeedRun"/>'s per-patch catch on purpose -- a screen
    /// that throws is not a patch that failed -- so anything it can do wrong ends the sweep and takes the
    /// outcome and the restore warning with it. An assignment to a field cannot throw, cannot block and
    /// cannot flood a dispatcher with six thousand posts; the 250 ms timer below is what turns the latest of
    /// them into words. That also makes the display cost the same whether the patches arrive at two a second
    /// or at eight.</summary>
    private volatile SeedProgress? _latest;

    /// <inheritdoc cref="_latest"/>
    private volatile string _saying = "";

    private readonly DispatcherTimer _ticker;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cancel;
    private TimeSpan _estimate;
    private int _total;

    /// <param name="instrument">See <see cref="_instrument"/>.</param>
    /// <param name="presets">See <see cref="_presets"/>.</param>
    /// <param name="folder">See <see cref="_folder"/>.</param>
    /// <param name="finished">See <see cref="_finished"/>.</param>
    /// <param name="report">See <see cref="_report"/>.</param>
    /// <param name="close">Put the editor back.</param>
    public SeedRunViewModel(Func<ISeedInstrument?> instrument, Func<IReadOnlyList<Integra7Preset>> presets,
        Func<string> folder, Action finished, Action<string, bool> report, Action close)
    {
        _instrument = instrument;
        _presets = presets;
        _folder = folder;
        _finished = finished;
        _report = report;
        _close = close;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => ShowProgress();

        // The two sides of the catalogue are a plain pair of flags rather than options in the lists above,
        // because they are not a bank: a user slot reports its bank as "PRST" like the factory tone it sits
        // beside, and this is the only thing that separates the two.
        this.WhenAnyValue(x => x.IncludeInternal, x => x.IncludeUser, (_, _) => 0)
            .Skip(1)
            .Subscribe(_ => Recompute());

        // The generated IsRunning setter announces itself and knows nothing of the property everything on
        // screen is actually enabled by -- DuplicateScanViewModel needs the same wiring for the same reason.
        this.WhenAnyValue(x => x.IsRunning).Subscribe(_ => this.RaisePropertyChanged(nameof(IsIdle)));
    }

    // ---- what to sweep ------------------------------------------------------------------------------------

    /// <summary>The engines, in the preset table's own order, each with the number of rows it covers.</summary>
    public ObservableCollection<SeedOptionViewModel> Engines { get; } = [];

    /// <summary>The banks, likewise.</summary>
    public ObservableCollection<SeedOptionViewModel> Banks { get; } = [];

    [Reactive] private bool _includeInternal = true;

    [Reactive] private bool _includeUser = true;

    /// <summary>Which part the sweep borrows, zero-based -- which is what the combo box's index already is,
    /// so it is bound to that rather than to a parsed label. Its tone is overwritten once per patch and the
    /// Studio Set is put back at the end, so the choice only decides what the user hears while it runs.
    /// </summary>
    [Reactive] private int _partIndex;

    public IReadOnlyList<string> PartNames { get; } = [.. Enumerable.Range(1, 16).Select(n => $"Part {n}")];

    private SeedSelection CurrentSelection() => new(
        [.. Engines.Where(option => option.IsTicked).Select(option => option.Name)],
        [.. Banks.Where(option => option.IsTicked).Select(option => option.Name)],
        IncludeInternal, IncludeUser, PartIndex);

    // ---- what that would cost -----------------------------------------------------------------------------

    /// <summary>How many patches the ticks add up to and how long they would take.</summary>
    [Reactive] private string _plan = "";

    /// <summary>What is being left out and why -- but only the two reasons that are news. "412 are already in
    /// your library" and "412 skipped" are different sentences and only the first tells the user their last
    /// run worked; a count of everything they did not tick would be arithmetic they can already see.</summary>
    [Reactive] private string _skips = "";

    /// <summary>Why the sweep will not start, or "". <see cref="SeedRefusal"/>'s words, including which of
    /// several reasons is worth saying first.</summary>
    [Reactive] private string _refusal = "";

    // ---- how it is going ----------------------------------------------------------------------------------

    [Reactive] private bool _isRunning;

    /// <summary>Everything on the selection screen is enabled by this, and the Close button too: a panel
    /// closed mid-sweep would leave the only Cancel button off screen with an hour still to run.</summary>
    public bool IsIdle => !IsRunning;

    /// <summary>How far along, 0 to 1, for the bar.</summary>
    [Reactive] private double _progress;

    [Reactive] private string _progressLine = "";

    /// <summary>What the instrument is doing that is not a patch, or "".
    ///
    /// <b>Without this the panel looks hung for a minute and a half at a time.</b> A board round polls at
    /// 1.5 s and gives up at 90 s, and reports no progress at all while it does -- so does the Studio Set
    /// capture before the first patch, and so does putting the instrument back at the end. The counter would
    /// simply stop, which is exactly what a crash looks like from the outside.</summary>
    [Reactive] private string _note = "";

    [Reactive] private string _clockLine = "";

    // ---- what it did --------------------------------------------------------------------------------------

    /// <summary>What the sweep did, in one paragraph, or "" before there is anything to say.</summary>
    [Reactive] private string _outcome = "";

    /// <summary>The ways in which the instrument did not come back to where it started, or "".
    ///
    /// <b>Shown in full and never trimmed.</b> It is two sentences, Studio Set first, each saying what the
    /// instrument is holding now and what to do about it -- and it is quite likely the only warning anybody
    /// gets before a part goes silent hours later. Truncating it would remove the half that says what to do.
    /// </summary>
    [Reactive] private string _restoreWarning = "";

    // ---- opening ------------------------------------------------------------------------------------------

    /// <summary>The panel has been opened: refresh the tick boxes from the catalogue as it now stands, ask
    /// the folder and the instrument what they hold, and work out what the current ticks would cost.
    ///
    /// Guarded whole, because the caller cannot await it -- a button binding never can -- so an exception
    /// anywhere in here would otherwise be a panel that opened blank with nothing said about why.</summary>
    public async Task OpenAsync()
    {
        try
        {
            SyncOptions();
            await GatherAsync();
            Recompute();
        }
        catch (Exception e)
        {
            UserActionLog.Failed("open the library seeding panel", e.ToString());
            _report($"Could not work out what a sweep would cost: {e.Message}", true);
        }
    }

    /// <summary>Bring the tick boxes in line with the catalogue: one per engine and one per bank, in the
    /// table's own order, with the counts as they now stand.
    ///
    /// <b>Ticks are not rebuilt.</b> A user who opened this panel, ticked eight banks and went to look
    /// something up in the browser must find those eight still ticked -- and the counts move underneath them
    /// as the instrument's own tone names arrive, which is the only thing that actually changes here.</summary>
    private void SyncOptions()
    {
        var presets = _presets();
        // GroupBy keeps first-appearance order, and the table's order is already the useful one: PRST, GM2,
        // the ExSN boards, the twelve SRX boards, ExPCM -- and SN-A, SN-S, SN-D, PCMS, PCMD.
        Sync(Engines, presets.Select(preset => preset.ToneTypeStr), EngineNote);
        Sync(Banks, presets.Select(preset => preset.ToneBankStr), BankNote);
    }

    private void Sync(ObservableCollection<SeedOptionViewModel> options, IEnumerable<string> names,
        Func<string, string> note)
    {
        foreach (var group in names.GroupBy(name => name, StringComparer.Ordinal))
        {
            var already = options.FirstOrDefault(option => option.Name == group.Key);
            if (already is null)
                options.Add(new SeedOptionViewModel(group.Key, group.Count(), note(group.Key),
                    TickedByDefault(group.Key), Recompute));
            else
                already.Recount(group.Count());
        }
    }

    /// <summary>Everything is ticked except the three the spike measured a reason against -- see the class
    /// remarks, and the note each of them carries on its own row.</summary>
    private static bool TickedByDefault(string name) => name is not ("PCMD" or "GM2/GM2#" or "ExPCM");

    private static string EngineNote(string engine) => engine switch
    {
        "PCMD" => "22 minutes and 137 MB for 216 kits on the instrument this was measured on — 40% of a full "
                  + "sweep's time for 3.6% of its patches, because a kit reads all 88 partial blocks whether "
                  + "or not they hold anything.",
        _ => "",
    };

    private static string BankNote(string bank) => bank switch
    {
        "GM2/GM2#" or "ExPCM" =>
            "Not capturable on the instrument this was measured on: the part accepts the selection and then "
            + "exposes no tone at all, on any engine. Unticked because establishing that again costs a reply "
            + "deadline per patch — tick it to find out whether yours differs.",
        _ => "",
    };

    /// <summary>Ask the folder what it already holds and the instrument what its slots hold. Both are what
    /// <see cref="SeedPlan.Build"/> needs and neither can be asked on a tick: one reads a directory of up to
    /// six thousand files and the other is a conversation with a MIDI device.
    ///
    /// <b>Each failure leaves an empty answer rather than stopping.</b> A folder that cannot be listed makes
    /// the plan capture everything, which is what an empty library would do anyway; slots that will not
    /// answer make it load boards it may not have needed to, which costs 23 seconds and no correctness. Both
    /// are worth saying and neither is worth refusing over, because the run itself asks again.</summary>
    private async Task GatherAsync()
    {
        var folder = _folder();
        try
        {
            // Off the UI thread: a seeded library is the one folder in this application that genuinely holds
            // thousands of files, which is the point of the feature.
            _existing = await Task.Run(() => Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, SnapshotLibrary.FilePattern, SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName).OfType<string>().ToList()
                : []);
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"list '{folder}' before a sweep", e.ToString());
            _report($"Could not read the library folder, so nothing can be skipped as already captured: " +
                    $"{e.Message}", true);
            _existing = [];
        }

        if (_instrument() is not { } instrument)
        {
            _boards = [];
            return;
        }

        try
        {
            _boards = await instrument.LoadedBoardsAsync();
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read the loaded expansion boards before a sweep", e.ToString());
            _boards = [];
        }
    }

    /// <summary>Work out what the ticks now add up to. Pure and cheap -- <see cref="SeedPlan.Build"/> opens
    /// no file and touches no device -- which is what lets it run on every tick, and a count that only
    /// appeared once the user pressed Start would be a count arriving after the decision it is for.</summary>
    private void Recompute()
    {
        var work = SeedPlan.Build(_presets(), CurrentSelection(), _existing, _boards);

        Plan = work.Count == 0
            ? "Nothing to capture."
            : $"{work.Count:n0} patches, {Roughly(work.Estimate)}.";

        var already = work.Skipped.Count(skip => skip.Why == SeedSkip.AlreadyInLibrary);
        var empty = work.Skipped.Count(skip => skip.Why == SeedSkip.EmptySlot);

        List<string> notes = [];
        // "Another", because these are not among the count above -- they are the ones it has already had
        // taken out of it, which is the whole news in this sentence.
        if (already > 0)
            notes.Add($"Another {already:n0} {(already == 1 ? "is" : "are")} already in your library and " +
                      "will be left alone.");
        if (empty > 0)
            notes.Add($"{empty:n0} user slot{(empty == 1 ? " is" : "s are")} still named INIT and will be " +
                      "passed over.");
        Skips = string.Join(" ", notes);
    }

    // ---- running ------------------------------------------------------------------------------------------

    /// <summary>Sweep. Gathers the facts again first, because the folder and the slots can both have moved
    /// since the panel opened -- and the folder listing in particular is the resume, so a stale one would
    /// re-capture whatever another window has saved in the meantime.</summary>
    public async Task StartAsync()
    {
        UserActionLog.Action("button: Seed the library from the instrument");
        if (IsRunning) return;

        Outcome = "";
        RestoreWarning = "";
        Refusal = "";

        await GatherAsync();
        Recompute();

        var instrument = _instrument();
        var folder = _folder();
        // Read from the journal itself rather than from anything mirrored onto the UI thread: this is a
        // guard against losing the user's edits, not a button's enabled state.
        if (SeedRefusal.Reason(EditJournal.Default.IsComparing, instrument is not null,
                SeedRefusal.FolderTrouble(folder)) is { } refused)
        {
            Refusal = refused;
            _report(refused, true);
            return;
        }

        var selection = CurrentSelection();
        var work = SeedPlan.Build(_presets(), selection, _existing, _boards);
        if (work.Count == 0)
        {
            // Two very different empty plans, and telling them apart is the difference between "your last
            // run worked" and "you have not chosen anything".
            Refusal = work.Skipped.Any(skip => skip.Why == SeedSkip.AlreadyInLibrary)
                ? "Everything you have chosen is already in your library, so there is nothing left to capture."
                : "Nothing is chosen to sweep. Tick at least one engine and one bank.";
            _report(Refusal, false);
            return;
        }

        await SweepAsync(work, selection, instrument!, folder);
    }

    /// <summary>The sweep itself, from the first capture to the words at the end.
    ///
    /// <b>The write is the plan's file name and the device's own name.</b> They are different things: the
    /// file name was chosen before anything was captured, because it is what the resume compares against the
    /// folder, and the snapshot's name is what the instrument answered -- <c>Ring E.Piano</c> where the
    /// catalogue says <c>Ring Piano</c>. Letting <c>SnapshotLibrary.Create</c> derive the file name from the
    /// snapshot instead would put roughly 208 files under names this plan never predicts, and every re-run
    /// would capture them again.</summary>
    private async Task SweepAsync(SeedWork work, SeedSelection selection, ISeedInstrument instrument,
        string folder)
    {
        UserActionLog.Begin($"sweep {work.Count} patches into '{folder}' on part {PartIndex + 1}");
        _report($"Sweeping {work.Count:n0} patches into your library. This should take " +
                $"{Roughly(work.Estimate)}.", false);

        _cancel = new CancellationTokenSource();
        _latest = null;
        _saying = "Reading your Studio Set, so the sweep can put it back afterwards…";
        _estimate = work.Estimate;
        _total = work.Count;
        _clock.Restart();
        Progress = 0;
        IsRunning = true;
        ShowProgress();
        _ticker.Start();

        SeedOutcome? outcome = null;
        string? unstarted = null;
        try
        {
            outcome = await SeedRun.RunAsync(work, selection, new Announcing(instrument, this),
                (item, snapshot) => SnapshotLibrary.Create(folder, snapshot,
                    SeedNaming.MetadataFor(snapshot, item), item.FileName),
                new Reports(this), _cancel.Token);
        }
        catch (Exception e)
        {
            // What is left that can throw out of the run is the Studio Set capture and the first reading of
            // the slots, both of which happen before a single value is written -- deliberately, because
            // something that cannot be captured cannot be put back either, and the right answer to that is to
            // fail with the instrument untouched rather than to start a sweep with nothing to restore.
            unstarted = e.Message;
            UserActionLog.Failed("sweep the instrument into the library", e.ToString());
        }
        finally
        {
            _ticker.Stop();
            _clock.Stop();
            IsRunning = false;
            _cancel.Dispose();
            _cancel = null;
        }

        Say(outcome, unstarted);
        UserActionLog.End($"sweep into '{folder}'");

        // After a cancel as well: everything captured is already on disk, and a list that does not show it is
        // a list saying the last twenty minutes did nothing.
        _finished();
    }

    /// <summary>Stop after the patch in flight. Never inside one: the three parameter writes and the capture
    /// share a single lease, and stopping between them would leave the part holding one patch's bank and
    /// another's program.</summary>
    public void Cancel()
    {
        UserActionLog.Action("button: Cancel the library sweep");
        _cancel?.Cancel();
        _saying = "Stopping after this patch, then putting your instrument back…";
    }

    public void Close()
    {
        UserActionLog.Action("button: Close the seeding panel (library)");
        _close();
    }

    /// <summary>Put the latest report, the note and the clock on screen. Runs on the timer rather than on
    /// every report -- see <see cref="_latest"/>.
    ///
    /// Guarded because it is a timer tick: an exception here has no caller to catch it, and a dispatcher
    /// timer that throws takes the window with it.</summary>
    private void ShowProgress()
    {
        try
        {
            var latest = _latest;
            var done = latest?.Done ?? 0;

            Progress = _total == 0 ? 0 : (double)done / _total;
            ProgressLine = $"{done:n0} of {_total:n0} patches" +
                           (latest is null ? "." : $" — {latest.Current.Preset.Name}");
            Note = _saying;

            var elapsed = _clock.Elapsed;
            var left = _estimate - elapsed;
            ClockLine = left > TimeSpan.Zero
                ? $"{Clock(elapsed)} gone, {Roughly(left)} left."
                : $"{Clock(elapsed)} gone, past the estimate of {About(_estimate)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("show the sweep's progress", e.ToString());
        }
    }

    /// <summary>What the sweep did, in the words the ending deserves.
    ///
    /// <b>Three endings and three sentences.</b> It finished; the user stopped it; or the instrument stopped
    /// it, which is neither of the other two and names the loadout it refused. <b>Unavailable is a count and
    /// not a failure</b> -- it is 13% of a full sweep on the measured unit and means the instrument holds no
    /// tone for those rows, so calling it a failure would send a user hunting a fault they do not have.
    /// </summary>
    private void Say(SeedOutcome? outcome, string? unstarted)
    {
        if (outcome is null)
        {
            Outcome = "The sweep did not start, so nothing on your instrument was changed: what it is holding "
                      + $"now could not be read, and a sweep with nothing to put back afterwards is not worth "
                      + $"running ({unstarted}).";
            _report(Outcome, true);
            return;
        }

        var written = outcome.Written.Count;
        var head = outcome.StoppedEarly is { } why
            ? $"{why} {written:n0} patch{(written == 1 ? " was" : "es were")} captured before that and " +
              "are in your library."
            : outcome.Cancelled
                ? $"You stopped the sweep. {written:n0} patch{(written == 1 ? " is" : "es are")} in your " +
                  "library, and starting it again carries on from there rather than beginning afresh."
                : $"The sweep finished. {written:n0} patch{(written == 1 ? " is" : "es are")} in your library.";

        if (outcome.Unavailable.Count > 0)
            head += $" {outcome.Unavailable.Count:n0} exposed no tone on your instrument, which is what the " +
                    "GM2 and ExPCM banks do and is not a fault.";

        if (outcome.Failed.Count > 0)
        {
            var named = string.Join(", ", outcome.Failed.Take(3).Select(failure => failure.Preset.Name));
            head += $" {outcome.Failed.Count:n0} could not be captured ({named}" +
                    $"{(outcome.Failed.Count > 3 ? " and others" : "")}); the log says why for each.";
        }

        Outcome = head;
        RestoreWarning = outcome.RestoreWarning ?? "";
        _report(head, outcome.Failed.Count > 0 || outcome.StoppedEarly is not null);
    }

    // ---- words --------------------------------------------------------------------------------------------

    /// <summary>A length as a guess, which is what every one of these numbers is. Separate from
    /// <see cref="About"/> only because "about under a minute" is not a sentence, and the short case is the
    /// one a user trying a single bank sees first.</summary>
    private static string Roughly(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1) ? "under a minute" : $"about {About(span)}";

    /// <summary>A length in the units somebody planning an hour of their evening thinks in.</summary>
    private static string About(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "under a minute";

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        var plural = minutes == 1 ? "" : "s";
        return hours == 0
            ? $"{minutes} minute{plural}"
            : $"{hours} hour{(hours == 1 ? "" : "s")} {minutes} minute{plural}";
    }

    /// <summary>Elapsed time, which is read as a clock rather than as a sentence: it moves four times a
    /// second and the thing being watched is whether it is still moving.</summary>
    private static string Clock(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes}:{span.Seconds:00}";

    // ---- the two things handed to the run -------------------------------------------------------------------

    /// <summary>The progress handler, and it is one field assignment on purpose.
    ///
    /// <b>Not <c>System.Progress&lt;T&gt;</c></b>, which posts every report to a synchronization context -- six
    /// thousand posts for a full sweep, on the same UI thread the sweep's own continuations run on. And not a
    /// dispatcher post of its own either: this is called from outside <see cref="SeedRun"/>'s per-patch
    /// catch, where anything that throws ends the sweep and discards the outcome and the restore warning
    /// along with it. A volatile assignment cannot throw, cannot block and cannot fail; the panel's timer is
    /// what turns the latest of them into words.</summary>
    private sealed class Reports(SeedRunViewModel panel) : IProgress<SeedProgress>
    {
        public void Report(SeedProgress value)
        {
            panel._latest = value;
            // A patch has been attempted, so whatever the panel was explaining -- a board round, the Studio
            // Set being read -- has finished and the note would otherwise sit there contradicting the
            // counter beside it.
            panel._saying = "";
        }
    }

    /// <summary>The instrument, with a word said about the three things it does that report no progress.
    ///
    /// <b>A board round can sit for ninety seconds without a single report.</b> The adapter polls the slots
    /// at 1.5 s and gives up at 90; the Studio Set capture before the first patch and the restore after the
    /// last one are silent stretches of the same kind. <see cref="SeedRun"/> has nothing to say about any of
    /// them -- its progress is per patch, rightly -- so the panel learns about them here, by being between
    /// the run and the device.
    ///
    /// <b>The wait is described rather than the intention.</b> This same wrapper sees the sweep's own board
    /// loads and the one the restore sends at the end, and cannot tell them apart; "waiting for the slots to
    /// hold these" is true of both, where "loading" would be a lie about half of them.
    ///
    /// Nothing in here can throw before the call it wraps: each announcement is an assignment to a volatile
    /// field, for <see cref="Reports"/>' reason -- a wrapper that threw would end the sweep in the one place
    /// the instrument has already been moved and not yet put back.</summary>
    private sealed class Announcing(ISeedInstrument inner, SeedRunViewModel panel) : ISeedInstrument
    {
        public Task<int[]> LoadedBoardsAsync() => inner.LoadedBoardsAsync();

        public Task LoadBoardsAsync(int[] boards, CancellationToken token)
        {
            var loaded = boards.Where(slot => slot != 0).ToList();
            panel._saying = loaded.Count == 0
                ? "Waiting for the instrument to empty its expansion slots. This takes a few seconds."
                : $"Waiting for the instrument's expansion slots to hold {string.Join(", ", loaded)}. " +
                  "This takes about twenty seconds and is given a minute and a half.";
            return inner.LoadBoardsAsync(boards, token);
        }

        public Task<Integra7Snapshot?> CaptureAsync(SeedItem item, int zeroBasedPartNo,
            CancellationToken token) => inner.CaptureAsync(item, zeroBasedPartNo, token);

        public Task<Integra7Snapshot> CaptureStudioSetAsync()
        {
            panel._saying = "Reading your Studio Set, so the sweep can put it back afterwards…";
            return inner.CaptureStudioSetAsync();
        }

        public Task RestoreStudioSetAsync(Integra7Snapshot studioSet)
        {
            panel._saying = "Putting your Studio Set back on the instrument…";
            return inner.RestoreStudioSetAsync(studioSet);
        }
    }
}
