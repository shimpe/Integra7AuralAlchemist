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
/// one saying what ticking it costs, and two saying why they cannot be ticked at all -- see
/// <see cref="SeedRunViewModel"/>.
///
/// No ToolTip, per the rule this branch keeps for anything clicked repeatedly, and these are clicked in
/// columns of twenty-one. It is also the rule that decides how a row that cannot be ticked explains itself:
/// a tooltip is invisible until hovered, shows nothing at all while the window is inactive, and swallows the
/// click on the control it describes. The reason is plain text on the row.</summary>
public sealed class SeedOptionViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _isTicked;
    private int _patches;

    internal SeedOptionViewModel(string name, int patches, string note, bool isTicked, bool isSweepable,
        Action changed)
    {
        Name = name;
        _patches = patches;
        Note = note;
        _isTicked = isTicked;
        IsSweepable = isSweepable;
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

    /// <summary>Why this one is unticked by default or cannot be ticked at all, or "" for the ones that can.
    /// Shown on the row rather than in a note at the bottom: the user is deciding about this tick, here -- and
    /// where the answer is "you cannot", the question is asked at this row too.</summary>
    public string Note { get; }

    /// <summary>Whether ticking this could capture anything, which is what the row's tick box is enabled by.
    /// False for the two banks whose tones cannot be edited -- see <see cref="SeedRunViewModel"/>. Never false
    /// for an engine.</summary>
    public bool IsSweepable { get; }

    public bool IsTicked
    {
        get => _isTicked;
        set
        {
            // A row that cannot be swept cannot be ticked, and it is refused here rather than only in the
            // view: the sweep is built from whatever this collection says is ticked, so a rule kept only in
            // the tick box's IsEnabled would be one binding away from forty minutes of reply deadlines.
            if (value && !IsSweepable) return;
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
/// <b>The defaults are the spike's measurements, and the three rows that are not ticked say why on the row.
/// </b> PCM drum kits are 40% of a full sweep's clock and 137 MB of its bytes for 3.6% of its patches, which
/// is an expensive thing to want and still a defensible one, so it is offered unticked and the cost is the
/// user's to weigh. GM2 and ExPCM cannot be ticked at all: their tones cannot be edited, so there is no
/// editable temporary tone for a capture to read, and a sweep of those 796 rows would spend about forty
/// minutes at the 3.00 s a silent row was timed at collecting nothing.
///
/// <b>Those two are shown disabled rather than left out</b>, which is the second answer this panel has given
/// to the question and the better one. The first offered them unticked, on the argument that another unit
/// might differ and a user should be able to check rather than be told what their instrument can do by a
/// table written about somebody else's -- right on the evidence it had, which was one measurement. Knowing
/// *why* they answer nothing settled it: an uneditable tone has no editable temporary area on any INTEGRA-7,
/// so there is nothing left to discover by ticking. What a row that vanished would not do is answer "where
/// did GM2 go?", and this list is built from the preset table the user can see elsewhere.
///
/// <b>None of that is a rule the sweep relies on.</b> Availability is discovered and never assumed --
/// <see cref="SeedRun"/> records a patch whose first block does not answer as unavailable and moves on --
/// which is what covers an unloaded SRX or ExSN board, an engine a part cannot hold, and anything unexpected.
/// This panel declining to offer two banks is a saving, not a safety net.
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
    /// file name is already in here is not captured again.
    ///
    /// <b>Gathered when the panel opens, when Start is pressed, and when a run ends.</b> The first two are
    /// what the sweep is planned from; the third is only ever for the screen, and it is needed because a run
    /// is the one thing certain to have changed the folder. Without it the panel spent the minutes after a
    /// sweep offering to capture patches it had just captured.</summary>
    private IReadOnlyCollection<string> _existing = [];

    /// <summary>What the instrument's four expansion slots hold, so that a bank whose board is already in a
    /// slot costs no 23-second round.</summary>
    private IReadOnlyCollection<int> _boards = [];

    /// <summary>The patch the run is working on, and the note about whatever it is doing that is not a patch.
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

    /// <summary>Whether a board load is in flight, so that Cancel can say what stopping will actually cost.
    ///
    /// <b>A cancel pressed during a board round does not take effect for about half a minute</b>, because a
    /// loadout sent to a loading instrument is discarded in silence and the adapter therefore sees a load
    /// through before anything else is sent -- see <see cref="ISeedInstrument.LoadBoardsAsync"/>. Half a
    /// minute of a button that has visibly been pressed and a panel still saying "waiting for the expansion
    /// slots" is indistinguishable from a hang, and the user's next move is to close the window on an
    /// instrument that has not been put back yet.</summary>
    private volatile bool _movingBoards;

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
    /// 1.5 s and gives up at 90 s on each of the two waits it makes -- the one for an instrument that is not
    /// already busy and the one for the loadout it then sends -- and reports no progress at all while it
    /// does. So does the Studio Set capture before the first patch, and so does putting the instrument back
    /// at the end. The counter would simply stop, which is exactly what a crash looks like from the outside.
    /// </summary>
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
        // An engine counts only the rows a tick could actually reach, which means leaving out the 796 that
        // live in the two banks nothing can be captured from. Counting them would put a number beside PCMS
        // that no combination of ticks on this screen can produce -- the same defect as a skip line that
        // says "5 of them" about five patches which are not among the count above it, and it was caught the
        // same way, by adding up what the screen claimed.
        //
        // The banks are counted whole, deliberately. "GM2/GM2# 265 patches" next to the sentence saying why
        // it cannot be swept tells the user how much the instrument holds that this feature will not reach,
        // which is worth knowing; an unsweepable bank reading zero would look like an empty bank.
        Sync(Engines, presets.Where(preset => Sweepable(preset.ToneBankStr)).Select(preset => preset.ToneTypeStr),
            EngineNote);
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
                    TickedByDefault(group.Key), Sweepable(group.Key), Recompute));
            else
                already.Recount(group.Count());
        }
    }

    /// <summary>Whether ticking this could capture anything at all, which is false for exactly two banks and
    /// for no engine.
    ///
    /// <b>GM2 and ExPCM tones cannot be edited</b> -- confirmed from the instrument's own front panel and,
    /// separately, over sysex with the board settled and no load in flight. A capture reads the part's
    /// temporary tone area, and a tone that can never be edited has no editable temporary area for the device
    /// to populate, which is why the Studio Set Part accepts the bank and the program quite happily and then
    /// all five engines' temporary areas stay silent. That is a property of the INTEGRA-7 rather than of the
    /// unit this was measured on, so there is nothing a user could learn by sweeping them.
    ///
    /// The two bank names are written once, here. Three things depend on them -- the tick box's enabled
    /// state, its default, and the sentence explaining it -- and all three ask this rather than carrying
    /// their own copy of the pair, because two of them quietly agreeing while the third does not is precisely
    /// the bug that shape invites: a row that looks tickable and is not, or one that is not and looks
    /// it.</summary>
    private static bool Sweepable(string name) => name is not ("GM2/GM2#" or "ExPCM");

    /// <summary>Everything is ticked except what the spike measured a reason against: PCM drum kits, which
    /// cost 40% of a full sweep's clock for 3.6% of its patches, and the two banks nothing can be captured
    /// from, which are not ticked because they cannot be. See the class remarks and each row's own
    /// note.</summary>
    private static bool TickedByDefault(string name) => Sweepable(name) && name is not "PCMD";

    private static string EngineNote(string engine) => engine switch
    {
        // 188, not the 216 the spike measured: 28 of the table's PCM drum kits are GM2 or ExPCM rows, and
        // those cannot be swept at all. Rescaled from the measurement rather than re-measured -- 6.018 s and
        // 137 MB per 216 kits, so 19 minutes and 119 MB for the 188 a tick can reach. The two percentages
        // survive the rescaling because the rows leaving the numerator leave the denominator with them.
        "PCMD" => "19 minutes and 119 MB for 188 kits on the instrument this was measured on — 40% of a full "
                  + "sweep's time for 3.6% of its patches, because a kit reads all 88 partial blocks whether "
                  + "or not they hold anything.",
        _ => "",
    };

    /// <summary>Why these two rows cannot be ticked, said where somebody would otherwise be reaching for the
    /// tick box.
    ///
    /// <b>It says the mechanism and not the measurement.</b> The rows are greyed, so the only question left
    /// is why — and "nothing was captured from these on the machine this was written about" invites a user to
    /// wonder whether theirs differs, when the answer is that no INTEGRA-7 exposes a temporary tone for a tone
    /// it will not let you edit. See <see cref="Sweepable"/> for how that was established.
    ///
    /// <b>No cost is quoted any more.</b> It used to say what ticking these would spend — roughly 13 minutes
    /// for GM2 and 27 for ExPCM, at the 3.00 s a silent row was timed at — because that was the number
    /// somebody weighing the tick needed and the estimate under the plan does not carry it. Nobody is weighing
    /// anything here now, and a price on a thing that is not for sale is only noise on a panel that already
    /// asks a lot of its reader.</summary>
    private static string BankNote(string bank) => Sweepable(bank)
        ? ""
        : "Cannot be swept: these tones cannot be edited, so the instrument exposes no temporary tone for a "
          + "capture to read. The part accepts the selection and then answers nothing, on any engine.";

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

        // And the panel's own picture of the library is now a whole run out of date. Nothing is decided by
        // it -- Start gathers again before it plans anything, which is what makes a sweep started from a
        // stale screen still correct -- but the number on screen is the one thing this panel exists to show,
        // and after a run it was wrong in both directions: "Nothing to capture." with ten files about to be
        // written, and "261 patches" with every one of them already on disk. A user reading either would be
        // right to stop trusting the ones they cannot check.
        //
        // Guarded, because it happens after the outcome has been put on screen and must not be what replaces
        // it: whatever went wrong listing a folder, the sentence saying what the sweep did is worth more.
        try
        {
            await GatherAsync();
            Recompute();
        }
        catch (Exception e)
        {
            UserActionLog.Failed("work out what a sweep would still cost, after one finished", e.ToString());
        }
    }

    /// <summary>Stop after the patch in flight. Never inside one: the three parameter writes and the capture
    /// share a single lease, and stopping between them would leave the part holding one patch's bank and
    /// another's program.</summary>
    public void Cancel()
    {
        UserActionLog.Action("button: Cancel the library sweep");
        _cancel?.Cancel();
        _saying = _movingBoards
            ? "Stopping — but the instrument is moving its expansion boards, and anything sent to the slots "
              + "while it does that is thrown away, so this has to finish first. It takes about half a "
              + "minute, and then your instrument is put back."
            : "Stopping after this patch, then putting your instrument back…";
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
            // Done is how many have finished and Current is the one in flight, so Done + 1 is where the
            // patch named beside it sits in the plan -- and, in the stretches when nothing is in flight, the
            // number that have finished. One number, true under both readings, which is what lets a single
            // line be right while a kit is loading and still be right through the half-minute of putting the
            // instrument back afterwards.
            //
            // An ordinal, not a count, and the word order is the whole of it. "3 of 216 patches" beside a
            // name invites the reading the user actually complained about -- that the name is a patch
            // already done -- where "Patch 3 of 216" can only be the one being worked on. A verb would
            // say it louder and would then be wrong for a quarter of a minute at a time: "Capturing 216 of
            // 216" under a note reading "Putting your Studio Set back" is two lines of the same panel
            // contradicting each other, and a board round would do it again at every round of a full sweep.
            var reached = latest is null ? 0 : latest.Done + 1;

            // The bar is filled by the same number for the same reason -- a bar resting a patch short while
            // the panel says the last one is in hand is the same disagreement in a second place.
            Progress = _total == 0 ? 0 : (double)reached / _total;
            ProgressLine = latest is null
                ? $"0 of {_total:n0} patches."
                : $"Patch {reached:n0} of {_total:n0} — {latest.Current.Preset.Name}";
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
    /// not a failure</b> -- it means the part was holding nothing for those rows to capture, so calling it a
    /// failure would send a user hunting a fault they do not have. It also says what that usually is, because
    /// the two commonest causes are both things the user can act on and neither is obvious from a bare count:
    /// a board that is not in a slot, and a patch the chosen part cannot hold. (The user's own Studio Set had
    /// a part pointing at an ExSN3 patch with no ExSN3 loaded, which is exactly this.)</summary>
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
            head += $" {outcome.Unavailable.Count:n0} exposed no tone on your instrument — usually an " +
                    "expansion board that is not loaded, or a patch that part cannot hold — which is not a " +
                    "fault and stopped nothing.";

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
            // A patch is starting, so whatever the panel was explaining -- a board round, the Studio Set
            // being read -- has finished and the note would otherwise sit there contradicting the counter
            // beside it. Cleared as the patch begins rather than as it ends, which is the report having
            // moved paying for itself twice: the first kit of a sweep used to load for six seconds under a
            // note still saying the Studio Set was being read.
            panel._saying = "";
        }
    }

    /// <summary>The instrument, with a word said about the three things it does that report no progress.
    ///
    /// <b>A board round can sit for minutes without a single report.</b> The adapter polls the slots at 1.5 s
    /// and gives up at 90, and it does that twice -- once waiting for an instrument that is not already
    /// loading and once for the loadout it then sends; the Studio Set capture before the first patch and the
    /// restore after the last one are silent stretches of the same kind. <see cref="SeedRun"/> has nothing to say about any of
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

        public async Task LoadBoardsAsync(int[] boards, CancellationToken token)
        {
            var loaded = boards.Where(slot => slot != 0).ToList();
            panel._saying = loaded.Count == 0
                ? "Waiting for the instrument to empty its expansion slots. This takes a few seconds."
                : $"Waiting for the instrument's expansion slots to hold {string.Join(", ", loaded)}. " +
                  "This takes about half a minute and cannot be interrupted once it has started.";

            // Raised around the call rather than at the start of it, because what Cancel needs to know is
            // whether stopping will have to wait -- which is true for the whole of this, including the wait
            // for the instrument to be free that happens before anything is sent.
            panel._movingBoards = true;
            try
            {
                await inner.LoadBoardsAsync(boards, token);
            }
            finally
            {
                panel._movingBoards = false;
            }
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
