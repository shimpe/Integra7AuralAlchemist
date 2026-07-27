using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Integra7AuralAlchemist.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable CS8618 // Non-nullable field 'xxx' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
    private readonly Integra7StartAddresses _i7startAddresses = new();
    private readonly Integra7Parameters _i7parameters = new();

    [Reactive] private bool _rescanButtonEnabled = true;
    private Integra7Domain? _integra7Communicator;
    [Reactive] private MotionalSurroundViewModel? _motionalSurroundVm;

    /// <summary>The mixer page. Built and replaced exactly like the Motional Surround editor, and for the
    /// same reason: both bind to the live parameters of every part, so both are only valid once all sixteen
    /// Studio Set Part blocks have been read.</summary>
    [Reactive] private MixerViewModel? _mixerVm;

    /// <summary>The Layers page: every part's key and velocity range on one chart. Built, replaced and
    /// disposed wherever <see cref="MixerVm"/> is, the rescan included — it wraps the same live Studio Set Part
    /// parameters, and beyond that it holds <c>PropertyChanged</c> handlers on the <see cref="PartViewModel"/>s
    /// themselves for their tone names, so a rescan that replaced the parts without disposing this would leave a
    /// dead map attached to live objects, rebuilding a snapshot list nothing draws.</summary>
    [Reactive] private LayerMapViewModel? _layerMapVm;

    /// <summary>Which top-level tab is showing. Bound two-way, so the mixer's click-through can take the
    /// user to the Parameters tab — selecting a part is no use while the Mixer tab is still in front.</summary>
    [Reactive] private int _topTabIndex;

    [Reactive] private bool _isSyncing = true;
    private string _syncInfo = "";

    public string SyncInfo
    {
        get => _syncInfo;
        set
        {
            this.RaiseAndSetIfChanged(ref _syncInfo, value);
            if (value != "") Log.Information(value);
        }
    }

    private readonly SyncCounter _syncLevels = new();

    /// <summary>Progress of work that runs after the window is usable (currently the user tone names).
    /// Unlike <see cref="SyncInfo"/> this drives a status line rather than the blocking overlay.</summary>
    [Reactive] private string _backgroundInfo = "";

    /// <summary>Outcome of the last snapshot save or load, or of the last Compare, shown on the status bar.
    /// This is
    /// the only channel this app has for telling the user that something failed -- <c>UserActionLog</c>
    /// only reaches the log file -- so a snapshot that cannot be read must land here, not just there.
    /// Empty means there is nothing to report; a cancelled file dialog leaves it untouched.</summary>
    [Reactive] private string _snapshotStatus = "";

    /// <summary>Whether <see cref="SnapshotStatus"/> describes a failure. Selects which of the two
    /// status-bar TextBlocks renders it, so a success is not shown in the "something is wrong" red.</summary>
    [Reactive] private bool _snapshotFailed;

    /// <summary>Cancels an in-flight background user-preset load. A rescan builds a fresh preset list
    /// and a fresh set of parts, so a loader still running for the previous connection must stop before
    /// it pushes rows from the old list into the new parts.</summary>
    private CancellationTokenSource? _userPresetsCts;

    public ReadOnlyObservableCollection<PartViewModel> PartViewModels { get; private set; }

    private const string INTEGRA_CONNECTION_STRING = "INTEGRA-7";
    private IIntegra7Api? Integra7 { get; set; }

    [Reactive] private bool _connected;

    [Reactive] private string _midiDevices = "No Midi Devices Detected";
    public bool CurrentPartIsNotCommonPart => CurrentPartSelection > 0;
    public Interaction<SaveUserToneViewModel, UserToneToSave?> ShowSaveUserToneDialog { get; }

    /// <summary>Ask the view where to write a snapshot. The input is the suggested file name; the
    /// output is the chosen path, null if the user cancelled, or "" if a file was chosen but has no
    /// usable local path (a cloud or virtual location) -- "" is not a value <c>TryGetLocalPath</c> can
    /// ever produce for an actual pick, so it is a safe sentinel the command can tell apart from a
    /// cancellation and report on the status line instead of silently doing nothing.</summary>
    public Interaction<string, string?> ShowSaveSnapshotDialog { get; }

    /// <summary>Ask the view which snapshot to read. Output is the chosen path, null if the user
    /// cancelled, or "" if a file was chosen but has no usable local path -- see
    /// <see cref="ShowSaveSnapshotDialog"/> for why "" is a safe sentinel here.</summary>
    public Interaction<Unit, string?> ShowOpenSnapshotDialog { get; }

    /// <summary>Ask what a snapshot about to be saved into the library should be called and what should be said
    /// about it. Output is the metadata, or null if the user cancelled -- the same "this or nothing" shape
    /// <see cref="ShowSaveUserToneDialog"/> has, and read the same way.</summary>
    public Interaction<SaveToLibraryViewModel, SnapshotMetadata?> ShowSaveToLibraryDialog { get; }

    /// <summary>Ask the view for a library folder, starting at the one passed in. Output is the chosen folder,
    /// null for a cancellation, or "" for a folder with no usable local path -- the same sentinel as the
    /// snapshot pickers, for the same reason.</summary>
    public Interaction<string, string?> ShowPickLibraryFolderDialog { get; }

    /// <summary>Ask a yes/no question. Init and Paste both replace a whole tone and clear the edit
    /// history, and neither is undoable, so both ask first.</summary>
    public Interaction<ConfirmViewModel, bool> ShowConfirmDialog { get; }

    /// <summary>Ask what a randomise should touch. The view model is kept rather than rebuilt, so a
    /// second press starts from the settings the first used.</summary>
    public Interaction<RandomiseToneViewModel, bool> ShowRandomiseToneDialog { get; }

    /// <summary>The tone Copy put there, waiting for Paste. One slot, this window's lifetime -- see
    /// ToneClipboard.</summary>
    private readonly ToneClipboard _toneClipboard = new();

    /// <summary>Kept, not rebuilt per press, so the categories and strengths a user set last time are
    /// still there the next time.</summary>
    private readonly RandomiseToneViewModel _randomiseVm = new();

    /// <summary>One generator for the session. A fresh Random per press seeded from the clock can repeat
    /// itself when two presses land in the same tick, which reads as "the button did nothing".</summary>
    private readonly Random _randomiseRng = new();

    /// <summary>Whether there is anything to paste. Bound by the Paste button, which is otherwise the
    /// only thing that could tell the user the clipboard is empty.</summary>
    [Reactive] private bool _canPasteTone;

    /// <summary>The library browser. Built here, in the constructor, and never replaced: it reads files rather
    /// than parameters, so unlike <see cref="MixerVm"/>, <see cref="LayerMapVm"/> and
    /// <see cref="MotionalSurroundVm"/> it is valid with no instrument attached and has nothing to dispose on a
    /// rescan. That is also why its tab needs no "connect your Integra-7" placeholder.
    ///
    /// Not <c>[Reactive]</c>, deliberately: a property that never changes has nothing to announce, and making
    /// it observable would suggest to the next reader that it might be replaced -- which is exactly the mistake
    /// that would leave the Save commands writing into a library the browser is no longer showing.</summary>
    public LibraryViewModel LibraryVm { get; }

    /// <summary>Whether the journal has anything left to take back, and anything to put back. Mirrored
    /// onto the UI thread from <c>EditJournal.Changed</c> in the constructor, because the journal is
    /// mutated from both the UI thread and the pool -- see <see cref="EditJournal"/>'s class remarks.
    /// The toolbar buttons bind to these rather than to the commands' CanExecute: an empty history is
    /// the whole reason to disable them, and the journal cannot fill while disconnected.</summary>
    [Reactive] private bool _canUndo;

    [Reactive] private bool _canRedo;

    /// <summary>Whether the Compare button has anything to do -- mirrored from the journal like
    /// <see cref="CanUndo"/>, and for the same threading reason. True while comparing as well as while
    /// there is something to compare, since the button is also the way back.</summary>
    [Reactive] private bool _canCompare;

    /// <summary>What the Compare button says. It doubles as the only indication of which of the two sounds
    /// the instrument is playing, so it names the state rather than the action while comparing.</summary>
    [Reactive] private string _compareLabel = "Compare";

    /// <summary>Take back the last edit the user made, from either editor.</summary>
    [ReactiveCommand]
    public async Task UndoAsync()
    {
        if (!EditJournal.Default.TryUndo(out var pending))
        {
            // The button is bound to CanUndo, so reaching here means the click arrived with nothing to take
            // back. Worth a line rather than a silent return: when a user reports that a button "did
            // nothing", the log is the only thing that can tell a click which never arrived -- eaten by the
            // window's resize edge or a tooltip popup, both of which have happened here -- from one that
            // arrived and found no work. Without it, the two look identical from the outside.
            UserActionLog.Action("button: Undo (nothing to take back)");
            return;
        }

        UserActionLog.Action($"undo {pending.Description}");
        // TryUndo has already moved the step to the redo side, so a write that never happened would
        // leave the history describing an instrument state that was never reached. Moving it back is
        // what TryRedo does, so the failure path is the opposite move rather than a special case.
        if (!await ApplyEditsAsync([pending], "undo/redo")) EditJournal.Default.TryRedo(out _);
    }

    /// <summary>Put back the edit the last undo took away.</summary>
    [ReactiveCommand]
    public async Task RedoAsync()
    {
        if (!EditJournal.Default.TryRedo(out var pending))
        {
            // See UndoAsync: a click that found nothing to do has to be distinguishable in the log from a
            // click that never arrived.
            UserActionLog.Action("button: Redo (nothing to put back)");
            return;
        }

        UserActionLog.Action($"redo {pending.Description}");
        // Mirror of UndoAsync: put the step back where it came from when the write did not happen.
        if (!await ApplyEditsAsync([pending], "undo/redo")) EditJournal.Default.TryUndo(out _);
    }

    /// <summary>Play the sound as it was before the edits in the history, or -- pressed again -- put the
    /// edits back. Both directions are the journal's own steps written through the ordinary write path, so
    /// the editors follow the instrument either way: a write goes through
    /// <c>DomainBase.WriteToIntegraAsync(path, value, lease)</c>, which modifies the parameter in memory,
    /// and the wrappers pick that up through <c>SynthParam</c>'s model subscription. What the user hears
    /// and what the screen shows do not come apart.
    ///
    /// A long session's history is hundreds of writes, and the journal's two-phase toggle spans all of them
    /// -- so something else can move the history in between, and what protects the press is the toggle's
    /// generation stamp, not the overlay. The overlay is up throughout, but it covers the tab area only (it
    /// is a Border in the window's second grid row, the status bar is the third), so the buttons beside this
    /// one stay clickable; the status bar's Undo and Redo are additionally disabled while syncing, which is
    /// what keeps the ordinary case out of the way rather than merely detected.</summary>
    [ReactiveCommand]
    public async Task CompareAsync()
    {
        if (!EditJournal.Default.TryBeginCompareToggle(out var toggle))
        {
            // See UndoAsync: logged rather than returned silently, so that "the button did nothing" can be
            // told apart from "the click never got here".
            UserActionLog.Action("button: Compare (nothing to compare with)");
            return;
        }

        UserActionLog.Action(toggle.Entering ? "button: Compare (hear the original)"
            : "button: Compare (hear the edits)");

        try
        {
            SignalStartSync();
            SyncInfo = toggle.Entering
                ? "Writing the values from before the edits"
                : "Writing the edits back";

            if (!await ApplyEditsAsync(toggle.Steps, "compare"))
            {
                // Nothing was committed, so the journal still says the instrument is on the side it was.
                // Some of the writes may have landed; pressing Compare again repeats the same direction,
                // and every write is an absolute value, so the retry finishes the job.
                SnapshotFailed = true;
                SnapshotStatus = "Compare did not finish writing to the instrument. Press it again to retry.";
            }
            else if (!EditJournal.Default.CommitCompareToggle(toggle))
            {
                // The history changed shape while the writes were going out, so the toggle described a
                // history that is no longer there and the journal refused it. Those writes did land, so the
                // instrument is between the two sounds.
                //
                // Which of the two causes it was decides what the user can do about it, so the message has
                // to ask rather than guess. An edit recorded during the press leaves the history there and
                // a fresh press recomputes from it and converges -- that is worth saying. A Clear (a preset
                // change, or a Studio Set change arriving from the front panel) takes the history away
                // entirely, and then a second press does nothing at all: CanCompare is false and the guard
                // at the top of this method returns before writing anything. Telling them to press it again
                // would be advice that silently fails.
                SnapshotFailed = true;
                SnapshotStatus = EditJournal.Default.CanCompare
                    ? "Compare was interrupted by another edit, so the press was abandoned. Press it again " +
                      "to settle on one of the two sounds."
                    : "Compare was interrupted: the sound it was comparing has been replaced, so there is " +
                      "nothing left to compare. The instrument holds part of what Compare had written.";
            }
            else
            {
                SnapshotFailed = false;
                SnapshotStatus = toggle.Entering
                    // "Playing" rather than "restored": the device acknowledges no parameter write, so
                    // this says the values went out. The truncation note is not a warning about damage --
                    // nothing is lost from the instrument -- but about what this comparison means: the
                    // edits older than the history's capacity are still in the sound being called the
                    // original.
                    //
                    // "changes you make meanwhile are not kept" used to live in the button's tooltip,
                    // which had to be removed: a tooltip is a popup, and sitting under the pointer it
                    // swallowed clicks on the very button it described (see MainWindow.axaml). It belongs
                    // here anyway -- this line is on screen at the moment the warning applies, which is
                    // more than a tooltip nobody hovers can say.
                    ? "Playing the sound from before the edits. Press Compare again to hear them; " +
                      "changes you make meanwhile are not kept." +
                      (EditJournal.Default.HistoryTruncated
                          ? $" Edits older than the last {EditJournal.Capacity} are still included in it."
                          : "")
                    : "Playing the edited sound.";
            }
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Refuse an action that would capture or store the instrument's current sound while Compare
    /// is playing the one from before the edits, and say why. Returns true when the caller must stop.
    ///
    /// Reads the journal rather than the mirrored property: the property is a UI convenience posted from
    /// another thread, and this is a correctness guard -- saving here writes the pre-edit sound into a file
    /// or, worse, over a user slot, and neither is undoable.</summary>
    private bool RefuseWhileComparing(string what)
    {
        if (!EditJournal.Default.IsComparing) return false;
        SnapshotFailed = true;
        SnapshotStatus = $"Cannot {what} while comparing: the instrument is playing the sound from before " +
                         "your edits. Press Compare again first.";
        return true;
    }

    /// <summary>What a set of steps is about to write, short enough for a log line and a lease label.
    ///
    /// One step names its parameters and the values they are getting -- <c>PendingEdit.Description</c>,
    /// the same text the action log carries for a successful undo. This is the only record of a write that
    /// threw part-way (see the catch in <see cref="ApplyEditsAsync"/> and the one in
    /// <see cref="ResyncDependentsAsync"/>), so it must not say less than the success path does: which
    /// parameters were touched, without the values, leaves out exactly what a partial write needs
    /// diagnosing with.
    ///
    /// A whole history would name hundreds, so it is counted instead.</summary>
    private static string Describe(IReadOnlyList<PendingEdit> steps) =>
        steps.Count == 1
            ? steps[0].Description
            : $"{steps.Sum(s => s.Step.Changes.Count)} changes in {steps.Count} steps";

    /// <summary>Write journal steps back to the instrument: every change of every step, in the order the
    /// journal asked for -- which is the dependency order within a step (discriminators first, see
    /// <c>PendingEdit.Writes</c>) and the caller's order between steps. Undo and redo pass a single step;
    /// Compare passes the whole history at once.
    ///
    /// The order between steps is the caller's because only the caller knows which way it is going:
    /// entering a comparison walks the history newest-first so a parameter edited twice lands on the oldest
    /// value it held, coming back walks it oldest-first so the same parameter lands on the newest. Both are
    /// also inherently dependency-safe across steps -- a discriminator and a knob that only exists under
    /// one of its values were edited in some order, and chronological order in either direction sets the
    /// discriminator before the write that needs it.
    ///
    /// Returns false when the writes were not performed in full, so the caller can leave the journal
    /// describing the side the instrument is really on rather than the one it was moving to.</summary>
    /// <param name="label">What to call this on the lease and in the log, e.g. "undo/redo".</param>
    private async Task<bool> ApplyEditsAsync(IReadOnlyList<PendingEdit> steps, string label)
    {
        // Locals, not the properties: everything below runs after awaits, and a rescan in the meantime
        // replaces both of them. Same reasoning as SaveStudioSetAsync.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return false;

        var what = Describe(steps);
        try
        {
            // One lease for the whole set, not one per change: a gesture's changes belong together (an
            // envelope handle's level and its time), and another flow writing into the same block
            // half-way through would leave the handle somewhere neither the user nor the history
            // describes. A comparison has the same need across every step it swaps.
            //
            // The lease is acquired outside ApplyAsync deliberately. Recording is suppressed for the
            // whole of ApplyAsync (otherwise these writes would record themselves and the history would
            // never empty), and an edit the user makes inside that window is dropped -- so the window
            // has to cover the writes and nothing more. Waiting for the wire to come free is the long
            // part of this and is not a write, so it happens first, outside. The writes themselves are
            // awaited wholly inside, which is what stops them escaping the suppression.
            await using var lease = await api.BeginConversationAsync($"{label} {what}");
            var appliedInFull = false;
            await EditJournal.Default.ApplyAsync(async () =>
            {
                foreach (var pending in steps)
                foreach (var (change, value) in pending.Writes)
                {
                    // TryGetDomain, not GetDomain: the latter answers an address it does not recognise
                    // with an unrelated block rather than refusing, and writing this change into that
                    // block would change a part of the instrument the user never touched. Every triple
                    // in the journal was read off a live domain, so this should be unreachable -- which
                    // is exactly what makes it worth a guard rather than a comment.
                    // StudioSetSnapshotService validates a whole snapshot for this reason.
                    if (!communicator.TryGetDomain(change.Start, change.Offset, change.Offset2,
                            out var domain))
                    {
                        // Abandon the rest rather than skip this change and carry on. Two reasons. A
                        // later change may be a dependent whose display value only converts correctly
                        // while the discriminator this one names is where the journal says it is (see
                        // PendingEdit.Writes), so carrying on can write a value nobody asked for. And
                        // returning false leaves the whole set unconsumed on the side it came from, which
                        // is the only way out of a half-applied swap: every write here is an absolute
                        // display value, not a delta, so pressing the button again re-applies the changes
                        // that did land and retries this one. Skipping and reporting would leave the
                        // instrument half-way with nothing left able to finish the job.
                        UserActionLog.Failed($"apply '{change.Path}'",
                            $"no such block (\"{change.Start}\", \"{change.Offset}\", \"{change.Offset2}\"); " +
                            "the rest was abandoned and nothing was consumed");
                        return;
                    }

                    await domain.WriteToIntegraAsync(change.Path, value, lease);
                }

                appliedInFull = true;
            });

            if (appliedInFull) await ResyncDependentsAsync(steps, communicator, lease, what);
            return appliedInFull;
        }
        catch (Exception e)
        {
            // A write that threw part-way leaves appliedInFull false, so nothing is consumed -- see the
            // reasoning at the guard above.
            UserActionLog.Failed($"apply '{what}'", e.ToString());
            return false;
        }
    }

    /// <summary>Bring the dependents of any discriminator the step moved back into line: reset a governed
    /// wave the newly-selected bank does not contain, re-read the block so the dependent slots show what
    /// the device now reports, and tell the editors to re-evaluate. The three things the other two write
    /// doors already do after an <c>IsParent</c> write -- see <see cref="UpdateIntegraFromUiAsync"/> for
    /// the shape -- and undo needs them for the same reason: moving a discriminator back leaves everything
    /// it governs displaying the value it had under the other one.
    ///
    /// The I/O runs once per block, not once per change and not once per step: the reset recomputes from
    /// the block's current values, so a second discriminator in the same block (a wave group's Type and ID
    /// both moving in one gesture) would only repeat identical work -- and so would the same discriminator
    /// moving in twenty steps of one comparison. The refresh is per change, because it is keyed by the
    /// parameter's own path and performs no I/O.
    ///
    /// Deliberately outside <c>EditJournal.ApplyAsync</c> while still inside the lease. Neither call can
    /// record: a read lands on the FQPs and reaches the wrappers through <c>ApplyFromModel</c>, which
    /// suppresses itself, and <c>WaveOutOfRangeReset</c> writes a raw value straight through the domain
    /// rather than through a wrapper's setter -- <see cref="UpdateIntegraFromUiAsync"/> runs both with
    /// recording live and nothing records. Keeping them out of the suppression is what stops an edit the
    /// user makes during a block read -- hundreds of milliseconds of wire time -- from being dropped from
    /// the history.
    ///
    /// Best-effort by design: a failure here is logged and swallowed. The step's writes have already
    /// happened, so reporting failure would put the step back and claim they had not; what is left wrong
    /// is displayed values, which the next read of the block corrects anyway.</summary>
    private async Task ResyncDependentsAsync(IReadOnlyList<PendingEdit> steps, Integra7Domain communicator,
        IMidiLease lease, string what)
    {
        try
        {
            var blocksDone = new HashSet<(string, string, string)>();
            foreach (var pending in steps)
            foreach (var (change, _) in pending.Writes)
            {
                if (!change.IsDiscriminator) continue;

                if (blocksDone.Add((change.Start, change.Offset, change.Offset2))
                    && communicator.TryGetDomain(change.Start, change.Offset, change.Offset2, out var domain))
                {
                    // WaveOutOfRangeReset needs the discriminator as an FQP, not as a path, and answers
                    // "not a wave group discriminator" for everything else by itself.
                    var edited = domain.GetRelevantParameters(true, true)
                        .FirstOrDefault(p => p.ParSpec.Path == change.Path);
                    if (edited != null)
                        await WaveOutOfRangeReset.ApplyAsync(domain, edited, WaveformBanks.Default, lease);
                    await domain.ReadFromIntegraAsync(lease);
                }

                ForceUiRefresh(change.Start, change.Offset, change.Offset2, change.Path, true);
            }
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"resync the dependents of '{what}'", e.ToString());
        }
    }

    [ReactiveCommand]
    public async Task SaveUserTone()
    {
        UserActionLog.Action("button: Save User Tone");
        if (_currentPartSelection == 0)
            return;

        if (RefuseWhileComparing("save a user tone")) return;

        if (PartViewModels is null || PartViewModels.Count < 2)
        {
            Log.Error("Cannot save user tone because there are no parts initialized.");
            return;
        }

        if (PartViewModels[_currentPartSelection].SelectedPreset is null)
        {
            Log.Error("Cannot save user tone because there is  no preset selected.");
            return;
        }

        // Saving reads the part's tone domains, so it cannot run while the part is still loading (the
        // tab can be clicked and saved faster than initialization completes).
        await PartViewModels[_currentPartSelection].EnsureInitializedAsync();

        // AllPresets, not Presets: the latter is part 1's *filtered* view, so whatever is typed in that
        // part's search box would both shrink the list of slots the dialog offers and shift the slot
        // numbering counted over it. Every part shares this one list by reference, so any part serves.
        var presets = PartViewModels[1].AllPresets;
        var preset = PartViewModels[_currentPartSelection].SelectedPreset;
        var toneType = preset.ToneTypeStr;
        var vm = new SaveUserToneViewModel(presets, toneType);
        var tone = await ShowSaveUserToneDialog.Handle(vm);
        if (tone != null)
            if (_integra7Communicator != null)
            {
                string name = tone.NewName;
                if (name.Length > 12)
                    name = name.Substring(0, 12);
                await Integra7?.WriteToneToUserMemory(_integra7Communicator, toneType,
                    (byte)(_currentPartSelection - 1), name, tone.ZeroBasedMemoryId);

                // Rename the slot the user picked. The dialog hands back that preset itself, so there is
                // nothing to count -- and because the preset raises PropertyChanged and every part's grid
                // binds this same instance, all of them redraw.
                tone.Preset.Name = name;
            }
    }

    /// <summary>Read the Studio Set currently in the instrument and save it into the library, asking first
    /// what to call it and what to say about it.
    ///
    /// <b>This is what Save Studio Set does now</b>, and <see cref="ExportStudioSetAsync"/> is the file
    /// dialog it used to open. The library is the default because it is the one place a saved sound can be
    /// found again by anything other than remembering where it was put: it is searchable, it is filterable by
    /// kind, category, rating and tag, and it is one folder to back up. Export stays because a snapshot is
    /// still a file, and sending one to somebody or keeping one beside a project is a real thing to want.
    ///
    /// <b>The metadata is asked for before the capture</b>, for the reason
    /// <see cref="SaveToLibraryViewModel"/> gives: a Studio Set is 53 blocks off the wire, and cancelling at
    /// the end of that would have paid for nothing. Everything else about the lease and the atomic write is
    /// the same as it was; the write now goes through <c>SnapshotLibrary.Create</c>, which is also where the
    /// file name comes from.</summary>
    [ReactiveCommand]
    public async Task SaveStudioSetAsync()
    {
        UserActionLog.Action("button: Save Studio Set");
        // Locals, not the properties: everything below runs after several awaits, and a rescan in the
        // meantime replaces both of them.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("save the Studio Set")) return;

        var name = CurrentStudioSetName(communicator);
        // No category: a Studio Set is sixteen parts each with one of their own, and there is no single tone
        // category for the set. It is found by kind, tags and rating like everything else.
        var metadata = await ShowSaveToLibraryDialog.Handle(new SaveToLibraryViewModel(
            "Studio Set", name, hasCategory: false, "", LibraryVm.Folder));
        if (metadata is null) return; // cancelled -- nothing happened, so say nothing

        try
        {
            SignalStartSync();
            SyncInfo = "Reading Studio Set";
            Integra7Snapshot snapshot;
            // One conversation for the whole capture, so nothing else can write to the instrument
            // partway through and produce a Studio Set that never actually existed. Scoped to just the
            // capture: the MIDI lease has no business being held across the disk write that follows, and
            // holding it would block anything else on the wire for the duration of unrelated I/O.
            await using (var lease = await api.BeginConversationAsync("capture Studio Set"))
            {
                // The name the user typed, not the one the instrument holds: it is what the library will
                // show, what the file will be called, and what the snapshot itself will say -- one answer in
                // all three places rather than a captured name overwritten by a typed one.
                snapshot = await StudioSetSnapshotService.CaptureAsync(communicator, metadata.Name ?? name,
                    lease);
            }

            var path = LibraryVm.SaveIntoLibrary(snapshot, metadata);
            SnapshotFailed = false;
            SnapshotStatus = $"Saved the Studio Set into the library as {Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("save Studio Set into the library", e.ToString());
            SnapshotFailed = true;
            // Naming Export is the whole point of this message: the failure is almost always the folder --
            // a library on a share that is not reachable, or one the user has no right to write to -- and
            // Export is the way to get the sound onto disk anyway while they sort the library out. The
            // capture itself is already done and lost by the time this is read, which is why the advice
            // matters more than the diagnosis.
            SnapshotStatus = $"Could not save the Studio Set into the library: {e.Message} " +
                             "Export… writes it anywhere you like.";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>What the Studio Set in the instrument calls itself, or "Studio Set" when it says nothing. The
    /// Studio Set names itself, and that is a far better suggestion than "snapshot".
    ///
    /// Takes the domain rather than reading the field, so that both callers keep the rule they open with: they
    /// read it into a local before their awaits because a rescan replaces it, and a helper that went back to the
    /// field would quietly undo that for the one line it is on.</summary>
    private static string CurrentStudioSetName(Integra7Domain communicator)
    {
        var name = communicator.StudioSetCommon
            .LookupSingleParameterDisplayedValue("Studio Set Common/Studio Set Name").Trim();
        return name.Length == 0 ? "Studio Set" : name;
    }

    /// <summary>Read the Studio Set currently in the instrument and write it to a file the user picks.
    ///
    /// <b>This is what Save Studio Set used to be</b>, unchanged and relabelled: the library is now the
    /// default (see <see cref="SaveStudioSetAsync"/>) and this is how a snapshot is written somewhere else --
    /// beside a project, onto a stick, into a message. Nothing that worked before this branch stopped
    /// working; it moved one button along.</summary>
    [ReactiveCommand]
    public async Task ExportStudioSetAsync()
    {
        UserActionLog.Action("button: Export Studio Set");
        // Locals, not the properties: everything below runs after several awaits, and a rescan in the
        // meantime replaces both of them.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("save the Studio Set")) return;

        var name = CurrentStudioSetName(communicator);

        // The instrument's character set includes ':', '/' and '*', which a file name cannot hold; the
        // snapshot keeps the real name, only the suggestion in the dialog is scrubbed. Through the library's
        // own function rather than a second copy of the substitution: this one used the running platform's
        // idea of an illegal character, which on Linux and macOS is NUL and '/' and nothing else, so the same
        // name suggested here and created by the library came out differently on those platforms.
        var path = await ShowSaveSnapshotDialog.Handle(SnapshotLibrary.FileNameFor(name));
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            // A file was chosen, but it has no usable local path (a cloud or virtual location) -- see
            // ShowSaveSnapshotDialog. Unlike a cancellation, the user needs to know this did nothing.
            SnapshotFailed = true;
            SnapshotStatus = "Could not save the Studio Set: the selected file has no accessible local path.";
            return;
        }

        try
        {
            SignalStartSync();
            SyncInfo = "Reading Studio Set";
            string json;
            // One conversation for the whole capture, so nothing else can write to the instrument
            // partway through and produce a Studio Set that never actually existed. Scoped to just the
            // capture: the MIDI lease has no business being held across the disk write that follows, and
            // holding it would block anything else on the wire for the duration of unrelated I/O.
            await using (var lease = await api.BeginConversationAsync("capture Studio Set"))
            {
                var snapshot = await StudioSetSnapshotService.CaptureAsync(communicator, name, lease);
                json = Integra7Snapshot.ToJson(snapshot);
            }

            // Write atomically. These files are the user's only copy of a Studio Set, so a failure
            // partway through a direct write must not destroy whatever was already at this path: write
            // to a sibling temp file first, then rename over the target, which is atomic on the same
            // volume. Clean up the temp file if the move itself fails.
            var tempPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            SnapshotFailed = false;
            SnapshotStatus = $"Exported the Studio Set to {Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("export Studio Set", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not export the Studio Set: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Read a snapshot file the user picks and write it back into the instrument, replacing
    /// the Studio Set currently loaded there.</summary>
    [ReactiveCommand]
    public async Task LoadStudioSetAsync()
    {
        UserActionLog.Action("button: Load Studio Set");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        var path = await ShowOpenSnapshotDialog.Handle(Unit.Default);
        if (path is null) return; // cancelled
        if (path.Length == 0)
        {
            // A file was chosen, but it has no usable local path (a cloud or virtual location) -- see
            // ShowOpenSnapshotDialog. Unlike a cancellation, the user needs to know this did nothing.
            SnapshotFailed = true;
            SnapshotStatus = "Could not load the Studio Set: the selected file has no accessible local path.";
            return;
        }

        await RestoreStudioSetFromFileAsync(api, communicator, path);
    }

    /// <summary>Write the Studio Set snapshot at <paramref name="path"/> into the instrument, replacing the
    /// one loaded there, and bring the window back into line with it afterwards.
    ///
    /// <b>Extracted so the library can load through exactly this path</b> and not through one of its own.
    /// Everything that made the file-dialog load correct -- one conversation for the whole restore, the
    /// journal cleared because every step in it describes the Studio Set that has just gone away, the preset
    /// preselection for the parts nobody has opened, the full resync -- is needed identically whichever button
    /// asked, and a second copy of it is a second place for one of those to be forgotten.
    ///
    /// The api and the domain are passed in rather than read here, because the caller read them into locals
    /// before its own awaits for the reason its comment gives: a rescan in the meantime replaces both.</summary>
    private async Task RestoreStudioSetFromFileAsync(IIntegra7Api api, Integra7Domain communicator, string path)
    {
        var restored = false;
        try
        {
            SignalStartSync();
            SyncInfo = "Writing Studio Set";
            var snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));
            // One conversation for the whole restore, same reasoning as the capture. Note that a
            // restore failing partway through leaves the instrument holding a mix of the snapshot and
            // what was there before -- RestoreAsync's own XML doc explains why nothing here can tell
            // which blocks landed. Loading the same file again is safe and finishes the job, because
            // restoring is idempotent: every block is applied independently, in the same order.
            await using (var lease = await api.BeginConversationAsync("restore Studio Set"))
            {
                await StudioSetSnapshotService.RestoreAsync(communicator, snapshot, lease);
            }

            restored = true;
            SnapshotFailed = false;
            // "Sent", not "loaded": the device never acknowledges a parameter write (see
            // StudioSetSnapshotService.RestoreAsync), so this confirms the data went out, not that the
            // instrument applied it. The resync below re-reads the device, so the UI self-corrects if
            // something did not stick -- but this message must not claim more than was verified.
            SnapshotStatus = $"Sent the Studio Set from {Path.GetFileName(path)} to the instrument.";
        }
        catch (SnapshotFormatException e)
        {
            // This one carries a message written for the user, so show it rather than a generic line.
            UserActionLog.Failed("load Studio Set", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e.Message;
        }
        catch (Exception e)
        {
            UserActionLog.Failed("load Studio Set", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not load the Studio Set: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }

        if (restored)
        {
            // A different Studio Set is now loaded, so every step in the history describes a value in
            // the one it replaced.
            EditJournal.Default.Clear();

            // A restore changes all 16 parts, including ones the user has never opened -- and
            // ResyncAllPartsAsync deliberately skips those. Left alone they keep the previous Studio
            // Set's tone type, and opening one later reads the wrong engine's domains, which the device
            // does not answer. RestoreAsync has already refreshed these parameters in memory, so this
            // costs no round trip.
            if (PartViewModels != null)
                foreach (var pvm in PartViewModels)
                    if (!pvm.IsCommonTab)
                        pvm.PreSelectConfiguredPreset(communicator.StudioSetPart(pvm.PartNo));

            // Every part now holds a different tone and every common block a different value, exactly
            // as when a Studio Set is selected on the front panel (see StudioSetSelectors in
            // UpdateUiFromIntegraAsync); updating nothing would leave the window describing the Studio
            // Set that just went away. Outside the lease above, since the resync acquires its own -- and
            // not at all when the restore failed, because the screen still matches whatever is on the
            // device.
            await ResyncAllPartsAsync();
        }
    }

    /// <summary>The part a tone command acts on, resolved once so the several awaits that follow cannot
    /// see it change, plus the engine that part currently holds and the name it goes by.
    ///
    /// <paramref name="ZeroBasedPartNo"/> is what the snapshot service and <see cref="ResyncPartAsync"/>
    /// both take; the tab index it came from counts the common tab as 0, exactly as <c>SaveUserTone</c>
    /// and <c>PlayNoteAsync</c> convert it.
    ///
    /// <paramref name="ToneName"/> comes from the same preset as <paramref name="ToneType"/>, so a file
    /// cannot end up named after one patch and holding another. <paramref name="Category"/> comes from that
    /// same preset for the same reason: it is the instrument's own word for what this sound is (one of the 34
    /// <c>Integra7Preset</c> parses), it is what the library's category filter is built on, and a category
    /// resolved from a different preset than the one being captured would be a lie that filters.</summary>
    private sealed record SelectedTone(int ZeroBasedPartNo, string ToneType, string ToneName, string Category);

    /// <summary>Resolve the selected part and the tone type it holds, or explain on the status line why
    /// there is none and return null.
    ///
    /// The tone type is the load-bearing part. <c>RestoreToneAsync</c> refuses a snapshot whose engine
    /// differs from the one the target part holds, and that guard is only as good as the engine it is
    /// handed: hand it the snapshot's own type, or a guess, and PCM data can be written into a
    /// SuperNATURAL part's addresses, which mean something else entirely there. So it comes from
    /// <c>SelectedPreset.ToneTypeStr</c> -- the same place Save User Tone and the editor tabs read it
    /// from -- and from nowhere else, and a part that has not resolved a preset yields null rather than
    /// a fallback.</summary>
    /// <param name="failurePrefix">How to open the "there is no tone here" messages, e.g. "save".</param>
    private async Task<SelectedTone?> ResolveSelectedToneAsync(string failurePrefix)
    {
        // Read once: CurrentPartSelection is bound to the tab strip and can change under the awaits
        // below, and every later step -- capture, restore, resync -- has to mean the same part.
        var selection = _currentPartSelection;

        // The buttons are disabled on the common tab, but a command is reachable regardless (the tab can
        // change between the click and this line), and silently doing nothing is worse than saying why.
        if (selection == 0 || PartViewModels is null || selection >= PartViewModels.Count)
        {
            SnapshotFailed = true;
            SnapshotStatus = $"Cannot {failurePrefix} a tone: select a part tab first, the Common tab holds none.";
            return null;
        }

        var part = PartViewModels[selection];

        // A tone is read from the part's tone domains, so this cannot run while the part is still
        // loading -- a tab can be clicked and acted on faster than its initialization completes. Same
        // reasoning as SaveUserTone, which does this for the same reason.
        await part.EnsureInitializedAsync();

        Integra7Preset? preset = part.SelectedPreset;
        var toneType = preset?.ToneTypeStr;
        if (toneType is null)
        {
            SnapshotFailed = true;
            SnapshotStatus = $"Cannot {failurePrefix} a tone: this part has not resolved which tone it holds.";
            return null;
        }

        if (!ToneDomainNames.IsKnownToneType(toneType))
        {
            SnapshotFailed = true;
            SnapshotStatus = $"Cannot {failurePrefix} a tone: this build does not know the tone type \"{toneType}\".";
            return null;
        }

        // The preset name is what the user sees on the part, so it is the file name they expect. Empty
        // only if the preset list ever carries a nameless row; "Tone" is a better suggestion than "".
        var name = (preset?.Name ?? "").Trim();
        return new SelectedTone(selection - 1, toneType, name.Length == 0 ? "Tone" : name,
            preset?.CategoryStr ?? "");
    }

    /// <summary>Read the tone currently loaded into the selected part and save it into the library, asking
    /// first what to call it and what to say about it. The Studio Set sibling of this is
    /// <see cref="SaveStudioSetAsync"/>, and every decision behind both is recorded there.
    ///
    /// <b>A tone does get a category</b>, and it starts on the one the instrument itself gives the preset in
    /// that part -- <c>Integra7Preset.CategoryStr</c>, one of the same 34 the drop-down offers. A user saving
    /// an edited E.Piano almost never wants to be asked what kind of sound it is, and a category that is right
    /// by default is the difference between a library that is filterable and one where everything is
    /// uncategorised.</summary>
    [ReactiveCommand]
    public async Task SaveToneAsync()
    {
        UserActionLog.Action("button: Save Tone");
        // Locals, not the properties: everything below runs after several awaits, and a rescan in the
        // meantime replaces both of them.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("save the tone")) return;

        var selected = await ResolveSelectedToneAsync("save");
        if (selected is null) return; // ResolveSelectedToneAsync has already said why

        var metadata = await ShowSaveToLibraryDialog.Handle(new SaveToLibraryViewModel(
            "tone", selected.ToneName, hasCategory: true, selected.Category, LibraryVm.Folder));
        if (metadata is null) return; // cancelled -- nothing happened, so say nothing

        try
        {
            SignalStartSync();
            SyncInfo = $"Reading tone from part {selected.ZeroBasedPartNo + 1}";
            Integra7Snapshot snapshot;
            // One conversation for the whole capture, so nothing else can write to the instrument
            // partway through and produce a tone that never actually existed. Scoped to just the
            // capture: the MIDI lease has no business being held across the disk write that follows.
            await using (var lease = await api.BeginConversationAsync("capture tone"))
            {
                // The typed name rather than the preset's, for the reason SaveStudioSetAsync gives.
                snapshot = await StudioSetSnapshotService.CaptureToneAsync(communicator,
                    selected.ZeroBasedPartNo, selected.ToneType, metadata.Name ?? selected.ToneName, lease);
            }

            var path = LibraryVm.SaveIntoLibrary(snapshot, metadata);
            SnapshotFailed = false;
            SnapshotStatus = $"Saved the tone from part {selected.ZeroBasedPartNo + 1} into the library as " +
                             $"{Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("save tone into the library", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not save the tone into the library: {e.Message} " +
                             "Export… writes it anywhere you like.";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Read the tone currently loaded into the selected part and write it to a file the user picks.
    /// What Save Tone used to be, relabelled -- see <see cref="ExportStudioSetAsync"/>.</summary>
    [ReactiveCommand]
    public async Task ExportToneAsync()
    {
        UserActionLog.Action("button: Export Tone");
        // Locals, not the properties: everything below runs after several awaits, and a rescan in the
        // meantime replaces both of them.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("save the tone")) return;

        var selected = await ResolveSelectedToneAsync("save");
        if (selected is null) return; // ResolveSelectedToneAsync has already said why

        // The instrument's character set includes ':', '/' and '*', which a file name cannot hold; the
        // snapshot keeps the real name, only the suggestion in the dialog is scrubbed. Through the library's
        // own function, for the reason SaveStudioSetAsync gives.
        var path = await ShowSaveSnapshotDialog.Handle(SnapshotLibrary.FileNameFor(selected.ToneName));
        if (path is null) return; // cancelled -- nothing happened, so say nothing
        if (path.Length == 0)
        {
            // A file was chosen, but it has no usable local path (a cloud or virtual location) -- see
            // ShowSaveSnapshotDialog. Unlike a cancellation, the user needs to know this did nothing.
            SnapshotFailed = true;
            SnapshotStatus = "Could not save the tone: the selected file has no accessible local path.";
            return;
        }

        try
        {
            SignalStartSync();
            SyncInfo = $"Reading tone from part {selected.ZeroBasedPartNo + 1}";
            string json;
            // One conversation for the whole capture, so nothing else can write to the instrument
            // partway through and produce a tone that never actually existed. Scoped to just the
            // capture: the MIDI lease has no business being held across the disk write that follows.
            await using (var lease = await api.BeginConversationAsync("capture tone"))
            {
                var snapshot = await StudioSetSnapshotService.CaptureToneAsync(communicator,
                    selected.ZeroBasedPartNo, selected.ToneType, selected.ToneName, lease);
                json = Integra7Snapshot.ToJson(snapshot);
            }

            // Write atomically, for the reason SaveStudioSetAsync gives: a failure partway through a
            // direct write must not destroy whatever was already at this path.
            var tempPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            SnapshotFailed = false;
            SnapshotStatus =
                $"Exported the tone from part {selected.ZeroBasedPartNo + 1} to {Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("export tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not export the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Read a tone snapshot the user picks and write it into the selected part, replacing the
    /// tone loaded there.</summary>
    [ReactiveCommand]
    public async Task LoadToneAsync()
    {
        UserActionLog.Action("button: Load Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        // Refused while comparing, unlike Load Studio Set. The plan's reasoning for letting a load through
        // -- it defines the sound outright, so there is nothing to get wrong -- holds only for a load that
        // replaces everything the comparison covered. This one replaces a single part, then clears the
        // journal, and the journal's buffer is the only copy of the edited values for the other fifteen.
        // See PartViewModel.ApplyPreset, which refuses a preset pick for the same reason.
        if (RefuseWhileComparing("load a tone")) return;

        var selected = await ResolveSelectedToneAsync("load");
        if (selected is null) return; // ResolveSelectedToneAsync has already said why

        var path = await ShowOpenSnapshotDialog.Handle(Unit.Default);
        if (path is null) return; // cancelled
        if (path.Length == 0)
        {
            // A file was chosen, but it has no usable local path -- see ShowOpenSnapshotDialog.
            SnapshotFailed = true;
            SnapshotStatus = "Could not load the tone: the selected file has no accessible local path.";
            return;
        }

        await RestoreToneFromFileAsync(api, communicator, selected, path);
    }

    /// <summary>Read the tone snapshot at <paramref name="path"/> and write it into
    /// <paramref name="selected"/>'s part, replacing the tone there.
    ///
    /// <b>Extracted so the library loads through exactly this path</b>, for the reason
    /// <see cref="RestoreStudioSetFromFileAsync"/> gives. Everything past the file is
    /// <see cref="RestoreToneSnapshotAsync"/>, which Init and Paste reach with a snapshot that never came
    /// from a file -- see there for why the engine guard is not in either of them.
    ///
    /// The read is outside that method rather than inside it because it is the only part of a load that a
    /// file can fail at, and its message names the file rather than the restore.</summary>
    private async Task RestoreToneFromFileAsync(IIntegra7Api api, Integra7Domain communicator,
        SelectedTone selected, string path)
    {
        Integra7Snapshot snapshot;
        try
        {
            snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));
        }
        catch (Exception e)
        {
            UserActionLog.Failed("load tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e is SnapshotFormatException ? e.Message : $"Could not load the tone: {e.Message}";
            return;
        }

        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, Path.GetFileName(path));
    }

    /// <summary>Write <paramref name="snapshot"/> into <paramref name="selected"/>'s part and re-read that
    /// part afterwards. <paramref name="source"/> is what the status line calls it -- a file name, "the
    /// clipboard", "the init tone".
    ///
    /// <b>Every whole-tone replacement goes through here</b>: Load Tone, the library's own load, Init and
    /// Paste. The engine guard is deliberately not in this method -- <paramref name="selected"/> carries the
    /// engine the part genuinely holds, RestoreToneAsync compares the snapshot's against it and refuses, and
    /// a second caller resolving the engine its own way is exactly how PCM data reaches a SuperNATURAL
    /// part's addresses.</summary>
    private async Task RestoreToneSnapshotAsync(IIntegra7Api api, Integra7Domain communicator,
        SelectedTone selected, Integra7Snapshot snapshot, string source)
    {
        var restored = false;
        try
        {
            SignalStartSync();
            SyncInfo = $"Writing tone to part {selected.ZeroBasedPartNo + 1}";

            // RestoreToneAsync refuses this too, but it cannot know which button the user was reaching
            // for, and that is the whole content of the message. FromJson has already narrowed Kind to
            // the kinds this build knows, so the second branch is for a kind a later build adds.
            if (snapshot.Kind != SnapshotKinds.Tone)
            {
                SnapshotFailed = true;
                SnapshotStatus = snapshot.Kind == SnapshotKinds.StudioSet
                    ? "This is a Studio Set snapshot, not a tone — use Load Studio Set… to write it back."
                    : $"This snapshot holds \"{snapshot.Kind}\", not a tone.";
                return;
            }

            // One conversation for the whole restore, same reasoning as the capture. A restore failing
            // partway through leaves the part holding a mix of the snapshot and what was there before;
            // loading the same file again is safe and finishes the job, because every block is applied
            // independently, in the same order.
            //
            // selected.ToneType is what the part genuinely holds right now, which is what makes
            // RestoreToneAsync's engine guard mean anything -- see ResolveSelectedToneAsync.
            await using (var lease = await api.BeginConversationAsync("restore tone"))
            {
                await StudioSetSnapshotService.RestoreToneAsync(communicator, snapshot,
                    selected.ZeroBasedPartNo, selected.ToneType, lease);
            }

            restored = true;
            SnapshotFailed = false;
            // "Sent", not "loaded": the device acknowledges no parameter write, so this confirms the
            // data went out, not that the instrument applied it. The resync below re-reads the device,
            // so the UI self-corrects if something did not stick.
            SnapshotStatus = $"Sent the tone from {source} to part {selected.ZeroBasedPartNo + 1}.";
        }
        catch (SnapshotFormatException e)
        {
            // This one carries a message written for the user -- including the engine-mismatch one,
            // which tells them what to select first -- so show it rather than a generic line.
            UserActionLog.Failed("load tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e.Message;
        }
        catch (Exception e)
        {
            UserActionLog.Failed("load tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not load the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }

        if (restored)
        {
            // A different tone is now in the part, so any step naming one of its parameters describes
            // the tone that was replaced. The history is not per-part, so this drops the lot.
            EditJournal.Default.Clear();

            // Only this part changed, so only this part is re-read -- a Studio Set restore has to resync
            // all 16, a tone restore does not. Outside the lease above, since the resync acquires its
            // own, and not at all when the restore failed, because the screen still matches the device.
            await ResyncPartAsync((byte)selected.ZeroBasedPartNo);
        }
    }

    /// <summary>Send a snapshot the user picked in the library to the instrument.
    ///
    /// <b>Everything here is a decision about which existing path to take, and nothing is a new one.</b> A
    /// Studio Set goes through <see cref="RestoreStudioSetFromFileAsync"/> and a tone through
    /// <see cref="RestoreToneFromFileAsync"/> -- the same two methods the Load buttons use, with the same
    /// leases, the same journal clearing and the same resyncs. What this adds is the routing, which the library
    /// can do without opening the file because the entry's head already says which kind it is.
    ///
    /// <b>The comparing guard differs between the two, as it already does for the buttons.</b> A tone load is
    /// refused while Compare is playing the pre-edit sound, because it replaces one part and then clears the
    /// journal, which holds the only copy of the edited values for the other fifteen. A Studio Set load is
    /// allowed, because it replaces everything the comparison covered. That asymmetry is
    /// <see cref="LoadToneAsync"/>'s reasoning and is repeated here rather than moved, because it belongs to
    /// the decision of whether to start rather than to the restore itself.
    ///
    /// A kind this build does not know cannot reach here: the head's kind came out of the file, and anything
    /// other than the two is refused by <c>FromJson</c> the moment the file is opened, which is inside both
    /// restore methods. Routing it as a Studio Set would apply its blocks somewhere they do not belong, so the
    /// unknown case is sent down the path that will refuse it and say so.</summary>
    private async Task LoadFromLibraryAsync(LibraryEntry entry)
    {
        // Locals, not the properties: everything below runs after several awaits, and a rescan in the
        // meantime replaces both of them.
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null)
        {
            // The library is browsable with no instrument attached -- it is files -- so this is a real state
            // and not a guard against the impossible. Saying so beats a button that does nothing.
            SnapshotFailed = true;
            SnapshotStatus = "Cannot load a snapshot: there is no connection to the instrument.";
            return;
        }

        if (entry.Head.Kind == SnapshotKinds.Tone)
        {
            if (RefuseWhileComparing("load a tone")) return;

            var selected = await ResolveSelectedToneAsync("load");
            if (selected is null) return; // ResolveSelectedToneAsync has already said why

            await RestoreToneFromFileAsync(api, communicator, selected, entry.FilePath);
            return;
        }

        await RestoreStudioSetFromFileAsync(api, communicator, entry.FilePath);
    }

    /// <summary>Read the tone in the selected part into the clipboard, so it can be pasted into another
    /// part. Nothing is written to the instrument and nothing reaches the disk.</summary>
    [ReactiveCommand]
    public async Task CopyToneAsync()
    {
        UserActionLog.Action("button: Copy Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        var selected = await ResolveSelectedToneAsync("copy");
        if (selected is null) return; // ResolveSelectedToneAsync has already said why

        try
        {
            SignalStartSync();
            SyncInfo = $"Reading tone from part {selected.ZeroBasedPartNo + 1}";
            // One conversation for the whole capture, so nothing else writes into the middle of it and
            // produces a tone that never existed -- the reasoning SaveToneAsync records.
            await using (var lease = await api.BeginConversationAsync("copy tone"))
            {
                _toneClipboard.Put(await StudioSetSnapshotService.CaptureToneAsync(communicator,
                    selected.ZeroBasedPartNo, selected.ToneType, selected.ToneName, lease));
            }

            SnapshotFailed = false;
            SnapshotStatus = $"Copied {selected.ToneName} from part {selected.ZeroBasedPartNo + 1}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("copy tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not copy the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Write the copied tone into the selected part. Refused while comparing and confirmed
    /// first, for the reasons <see cref="LoadToneAsync"/> and <see cref="InitToneAsync"/> give.</summary>
    [ReactiveCommand]
    public async Task PasteToneAsync()
    {
        UserActionLog.Action("button: Paste Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("paste a tone")) return;

        if (_toneClipboard.Content is not { } snapshot)
        {
            // The button is disabled without content, but a command stays reachable, and silently doing
            // nothing is worse than saying why.
            SnapshotFailed = true;
            SnapshotStatus = "Nothing to paste: copy a tone first.";
            return;
        }

        var selected = await ResolveSelectedToneAsync("paste");
        if (selected is null) return;

        if (!await ShowConfirmDialog.Handle(new ConfirmViewModel(
                $"Replacing the tone in part {selected.ZeroBasedPartNo + 1} with {snapshot.Name} cannot be " +
                "undone, and it clears the edit history. Continue?", "Paste"))) return;

        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, "the clipboard");
    }

    /// <summary>Replace the tone in the selected part with the init tone for its engine: the library
    /// entry the user marked, or the tone bundled with this build.
    ///
    /// A real tone snapshot rather than a table of default values, so it is complete by construction --
    /// every block, every parameter -- and so it goes through exactly the restore path (and validation)
    /// that Load Tone does.</summary>
    [ReactiveCommand]
    public async Task InitToneAsync()
    {
        UserActionLog.Action("button: Init Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("initialise a tone")) return;

        var selected = await ResolveSelectedToneAsync("initialise");
        if (selected is null) return;

        var initTone = InitToneResolution.Resolve(
            LibrarySettings.LoadAll(LibrarySettings.SettingsPath).InitTones,
            LibraryVm.Folder, selected.ToneType, File.Exists,
            uri => AssetLoader.Exists(new Uri(uri)));

        if (!initTone.HasTone)
        {
            SnapshotFailed = true;
            // Says how to fix it, not only that it is broken: there is no init tone for this engine in
            // this build, and the user has a way to supply one.
            SnapshotStatus = (initTone.MarkWasStale
                                 ? $"The tone marked as the init tone for {selected.ToneType} is no longer in the library. "
                                 : $"No init tone is set for {selected.ToneType}. ") +
                             "Add a tone to the library, select it in the Library tab and press " +
                             "\"Use as the init tone\".";
            return;
        }

        if (!await ShowConfirmDialog.Handle(new ConfirmViewModel(
                $"Replacing the tone in part {selected.ZeroBasedPartNo + 1} with the init tone cannot be " +
                "undone, and it clears the edit history. Continue?", "Initialise"))) return;

        Integra7Snapshot snapshot;
        try
        {
            var json = initTone.FilePath is { } file
                ? await File.ReadAllTextAsync(file)
                : await new StreamReader(AssetLoader.Open(new Uri(initTone.AssetUri!))).ReadToEndAsync();
            snapshot = Integra7Snapshot.FromJson(json);
        }
        catch (Exception e)
        {
            UserActionLog.Failed("read the init tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not read the init tone for {selected.ToneType}: {e.Message}";
            return;
        }

        // A stale mark still loads the bundled tone, but the user asked for a different one and has to
        // know they did not get it. Said in the source rather than on the status line before the restore:
        // the restore writes its own outcome there, so a line set first is one nobody ever reads.
        await RestoreToneSnapshotAsync(api, communicator, selected, snapshot, initTone.MarkWasStale
            ? "the bundled init tone (the one you marked for this engine is no longer in the library)"
            : "the init tone");
    }

    /// <summary>Vary the tone in the selected part, under the categories and strengths the dialog
    /// collects. Unlike Init and Paste this is an edit like any other: it records one undo step, so a
    /// result the user does not like is one press away from gone.
    ///
    /// A drum kit is randomised one note at a time -- the note selected in its editor. Every note at once
    /// would be 88 partials and an undo step nobody could use.</summary>
    [ReactiveCommand]
    public async Task RandomiseToneAsync()
    {
        UserActionLog.Action("button: Randomise Tone");
        var api = Integra7;
        var communicator = _integra7Communicator;
        if (api is null || communicator is null) return;

        if (RefuseWhileComparing("randomise a tone")) return;

        var selected = await ResolveSelectedToneAsync("randomise");
        if (selected is null) return;

        IReadOnlyList<(string Start, string Offset, string Offset2)> blocks;
        string target;
        if (ToneDomainNames.IsDrumKit(selected.ToneType))
        {
            // Indexed from what ResolveSelectedToneAsync settled on, not from CurrentPartSelection again:
            // the tab can have changed under the awaits above, and the editor read here has to belong to
            // the part everything else in this method is about.
            var part = PartViewModels[selected.ZeroBasedPartNo + 1];

            // Written out rather than nested in a conditional: the two editors are different types, so
            // this is two lookups that happen to answer the same shape, not one expression.
            (int Index, int Note)? note;
            if (selected.ToneType == "SN-D")
                note = part.SNDrumKitEditor?.SelectedNote is { } sn ? (sn.Index, sn.Note) : null;
            else
                note = part.PcmDrumKitEditor?.SelectedNote is { } pcm ? (pcm.Index, pcm.Note) : null;

            if (note is not { } chosen)
            {
                SnapshotFailed = true;
                SnapshotStatus = "Cannot randomise a drum kit: open the part's drum tab and select a note first.";
                return;
            }

            blocks = [ToneDomainNames.DrumPartialFor(selected.ToneType, selected.ZeroBasedPartNo, chosen.Index)];
            target = $"Randomising note {chosen.Note} ({MidiNote.Name(chosen.Note)}) of the kit in " +
                     $"part {selected.ZeroBasedPartNo + 1}";
        }
        else
        {
            blocks = ToneDomainNames.For(selected.ToneType, selected.ZeroBasedPartNo);
            target = $"Randomising the tone in part {selected.ZeroBasedPartNo + 1}";
        }

        _randomiseVm.PrepareFor(selected.ToneType, target);
        if (!await ShowRandomiseToneDialog.Handle(_randomiseVm)) return;

        var strengths = _randomiseVm.Strengths();
        if (!strengths.Any)
        {
            SnapshotFailed = true;
            SnapshotStatus = "Nothing was ticked, so nothing was randomised.";
            return;
        }

        var randomised = false;
        try
        {
            SignalStartSync();
            SyncInfo = $"Randomising part {selected.ZeroBasedPartNo + 1}";
            // One conversation for the whole operation: it reads each block and writes it back, and
            // anything else writing in between would randomise around values that were never heard.
            await using (var lease = await api.BeginConversationAsync("randomise tone"))
            {
                var changed = await ToneRandomisationService.RandomiseAsync(communicator, blocks,
                    strengths, _randomiseRng, lease);
                randomised = true;
                SnapshotFailed = false;
                // "Sent", not "applied": the device acknowledges no parameter write. Undo is named
                // because it is the whole reason this is one step.
                SnapshotStatus = $"Sent {changed} randomised parameters to part " +
                                 $"{selected.ZeroBasedPartNo + 1}. Undo takes all of it back.";
            }
        }
        catch (SnapshotFormatException e)
        {
            UserActionLog.Failed("randomise tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = e.Message;
        }
        catch (Exception e)
        {
            UserActionLog.Failed("randomise tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not randomise the tone: {e.Message}";
        }
        finally
        {
            SignalStopSync();
        }

        if (randomised)
            // Only this part changed. Outside the lease above, since the resync takes its own, and not
            // at all on failure, because the screen still matches the device.
            await ResyncPartAsync((byte)selected.ZeroBasedPartNo);
    }

    [ReactiveCommand]
    public async Task PlayNoteAsync()
    {
        UserActionLog.Action("button: Play Note");
        byte zeroBasedMidiChannel = 0;
        if (_currentPartSelection is > 0 and < 17) zeroBasedMidiChannel = (byte)(_currentPartSelection - 1);

        await Integra7?.NoteOnAsync(zeroBasedMidiChannel, 65, 100);
        Thread.Sleep(1000);
        await Integra7?.NoteOffAsync(zeroBasedMidiChannel, 65);
    }

    [ReactiveCommand]
    public async Task PlayPhraseAsync()
    {
        UserActionLog.Action("button: Play Phrase");
        byte zeroBasedMidiChannel = 0;
        if (_currentPartSelection is > 0 and < 17) zeroBasedMidiChannel = (byte)(_currentPartSelection - 1);

        await Integra7?.SendStopPreviewPhraseMsgAsync();
        await Integra7?.SendPlayPreviewPhraseMsgAsync(zeroBasedMidiChannel);
    }

    [ReactiveCommand]
    public async Task StopPhraseAsync()
    {
        UserActionLog.Action("button: Stop Phrase");
        await Integra7?.SendStopPreviewPhraseMsgAsync();
    }

    [ReactiveCommand]
    public async Task PanicAsync()
    {
        UserActionLog.Action("button: Panic");
        await Integra7?.AllNotesOffAsync();
        await Integra7?.SendStopPreviewPhraseMsgAsync();
    }

    [ReactiveCommand]
    public async Task RescanMidiDevicesAsync()
    {
        UserActionLog.Action("button: Rescan MIDI devices");
        MotionalSurroundVm?.Dispose();
        MotionalSurroundVm = null;
        // The mixer holds handlers on the current PartViewModels and their presets, so it has to let go
        // before a rescan replaces them. So does the layer map, for its tone names.
        MixerVm?.Dispose();
        MixerVm = null;
        LayerMapVm?.Dispose();
        LayerMapVm = null;
        Integra7 = new Integra7Api(
            new MidiPort(new MidiOut(INTEGRA_CONNECTION_STRING), new MidiIn(INTEGRA_CONNECTION_STRING)));
        await Integra7.CheckIdentityAsync();
        List<Integra7Preset> presets = LoadPresets();
        await UpdateConnectedAsync(Integra7, presets);
    }

    [Reactive] private int _srxSlot1;
    [Reactive] private int _srxSlot2;
    [Reactive] private int _srxSlot3;
    [Reactive] private int _srxSlot4;

    [ReactiveCommand]
    public async Task LoadSrx()
    {
        UserActionLog.Action($"button: Load SRX (slots {_srxSlot1}, {_srxSlot2}, {_srxSlot3}, {_srxSlot4})");
        if (_connected)
        {
            await Integra7?.SendLoadSrxAsync((byte)_srxSlot1, (byte)_srxSlot2, (byte)_srxSlot3, (byte)_srxSlot4);
            LoadedSrxState.Default.SetFromSlots(_srxSlot1, _srxSlot2, _srxSlot3, _srxSlot4);
            await ResyncAllPartsAsync(); // re-runs the read path -> refreshes Wave Group ID options
        }
    }

    private void SignalStartSync()
    {
        Log.Debug($"Start Sync. Sync level is now {_syncLevels.Enter()}.");
        RefreshSyncOverlay();
    }

    private void SignalStopSync()
    {
        Log.Debug($"Stop Sync. Sync level is now {_syncLevels.Exit()}.");
        RefreshSyncOverlay();
    }

    /// <summary>Bring the overlay in line with the counter, on the UI thread.
    ///
    /// The callback reads the counter rather than being handed a value, so two operations finishing
    /// at once cannot deliver their updates out of order and leave the overlay disagreeing with the
    /// counter: whichever callback runs last reads the truth. Posting is what puts the property
    /// writes on the UI thread at all -- the callers run on thread-pool threads, because the handlers
    /// driving them are throttled.</summary>
    private void RefreshSyncOverlay()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var visible = _syncLevels.Visible;
            if (visible == IsSyncing) return;

            IsSyncing = visible;
            if (!visible)
            {
                Log.Debug("Hide Sync notification.");
                SyncInfo = "";
            }
        });
    }

    private async Task UpdateConnectedAsync(IIntegra7Api integra7Api, List<Integra7Preset> presets)
    {
        Connected = integra7Api.ConnectionOk();

        // Stop any loader left over from a previous connection before its list and parts go stale.
        _userPresetsCts?.Cancel();
        _userPresetsCts?.Dispose();
        _userPresetsCts = new CancellationTokenSource();
        var userPresetsToken = _userPresetsCts.Token;
        BackgroundInfo = ""; // the cancelled loader leaves its last status behind

        try
        {
            if (_connected)
            {
                SignalStartSync();
                SyncInfo = "Fetch loaded SRX...";
                (SrxSlot1, SrxSlot2, SrxSlot3, SrxSlot4) = await integra7Api.GetLoadedSrxAsync();
                LoadedSrxState.Default.SetFromSlots(SrxSlot1, SrxSlot2, SrxSlot3, SrxSlot4);
                Log.Information("Connected to Integra7");
                MidiDevices = "Connected to: " + INTEGRA_CONNECTION_STRING + " with device id " +
                              integra7Api.DeviceId().ToString("x2");
                _integra7Communicator = new Integra7Domain(integra7Api, _i7startAddresses, _i7parameters);

                ObservableCollection<PartViewModel> pvm = [];
                for (byte i = 0; i < 17; i++)
                {
                    if (i == 0)
                    {
                        SyncInfo = "Initializing common tab...";
                        Log.Information("Creating view model for common tab.");
                    }
                    else
                    {
                        SyncInfo = $"Initializing part {i}/16 tab...";
                        Log.Information($"Creating view model for tab part {i}.");
                    }

                    var commonTab = i == 0;
                    var vm = new PartViewModel(this, commonTab ? (byte)255 : (byte)(i - 1),
                        _i7startAddresses, _i7parameters, Integra7,
                        _integra7Communicator, presets, commonTab);
                    await vm.InitializeParameterSourceCachesAsync();
                    pvm.Add(vm);
                }

                PartViewModels = new ReadOnlyObservableCollection<PartViewModel>(pvm);
                this.RaisePropertyChanged(nameof(PartViewModels));

                // A reconnect replaces every part with a fresh, uninitialized instance while the tab
                // index stays put, so the selection setter never fires and the tab on screen would keep
                // showing an empty part. Initialize whatever is selected right now.
                _ = EnsureSelectedPartInitializedAsync(CurrentPartSelection);

                // All Studio Set Part + common Motional Surround domains have now been read,
                // so the spatial editor can bind to their live values.
                MotionalSurroundVm?.Dispose();
                MotionalSurroundVm = new MotionalSurroundViewModel(_integra7Communicator);

                // Same precondition, and the mixer additionally needs the parts themselves for their tone
                // names. Built after PartViewModels is published so it watches the list the tabs show.
                MixerVm?.Dispose();
                MixerVm = new MixerViewModel(_integra7Communicator, PartViewModels, OpenPartTab,
                    OpenCommonTab);

                // The layer map, on the same precondition and for the same reasons: it wraps eight live Studio
                // Set Part parameters per part, and it needs the parts themselves for their tone names, so it is
                // built after PartViewModels is published and watches the list the tabs show.
                //
                // The audition callback sounds the note under the pointer on the part's own channel: note-on, a
                // short hold, note-off, exactly as the drum-kit note rails do it, and with its failures swallowed
                // for the same reason -- hearing a part is a question, not an edit, and a MIDI port that will not
                // answer it must not take the click down with it.
                //
                // The velocity is passed through as pressed, including zero. Zero is what the very bottom of a
                // lane resolves to, and a note-on at velocity zero is a note-off on the wire, so that press makes
                // no sound. Clamping it to 1 was considered and rejected: the chart's whole promise is that where
                // you press is what the part is asked, and the silence at the bottom of a lane is of a piece with
                // the silence outside a part's range -- the map answering "not here" rather than swallowing a
                // note. The API takes a byte, and a byte cannot carry the difference anyway.
                // The API is captured rather than read from the Integra7 property when a note is pressed: a
                // rescan replaces that property, and this map is disposed and rebuilt alongside it, so the
                // connection a press reaches is the one this map's parts belong to.
                LayerMapVm?.Dispose();
                LayerMapVm = new LayerMapViewModel(_integra7Communicator, PartViewModels, OpenPartTab,
                    OpenPartSetPartTab,
                    (part, note, velocity) => _ = AuditionOnPartAsync(integra7Api, part, note, velocity));

                // Fetching the user tone names costs ~10s of sysex round trips, and nothing above
                // depends on it — the factory presets from the CSV are already in place. Let it run
                // after the window is usable and drip the user presets into the lists as they arrive.
                _ = LoadUserPresetsInBackgroundAsync(presets, PartViewModels, userPresetsToken);
            }
            else
            {
                Log.Information("Failed to connect to Integra7");
                MidiDevices = "Could not find " + INTEGRA_CONNECTION_STRING;
                MotionalSurroundVm?.Dispose();
                MotionalSurroundVm = null;
                MixerVm?.Dispose();
                MixerVm = null;
                LayerMapVm?.Dispose();
                LayerMapVm = null;
            }

            RescanButtonEnabled = !_connected;
        }
        finally
        {
            SignalStopSync();
        }
    }

    /// <summary>Fetch the user tone names off the startup path. Runs unawaited, so it must swallow its
    /// own failures: a missing user-name list leaves the factory presets usable rather than killing the
    /// session.</summary>
    private async Task LoadUserPresetsInBackgroundAsync(List<Integra7Preset> presets,
        ReadOnlyObservableCollection<PartViewModel> parts, CancellationToken token)
    {
        try
        {
            await AddUserDefinedPresets(presets, parts, token);

            // A part sitting on a user tone could not be matched earlier, because its name had not
            // been fetched yet. Now that the rows exist, give those parts their selection.
            foreach (var pvm in parts)
            {
                if (token.IsCancellationRequested) return;
                await pvm.EnsurePreselectIsNotNullAsync();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("User preset name loading was superseded by a reconnect.");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load user preset names: {e.Message}");
        }
        finally
        {
            // On cancellation a newer loader owns the status line; do not wipe its text.
            if (!token.IsCancellationRequested) BackgroundInfo = "";
        }
    }

    private async Task AddUserDefinedPresets(List<Integra7Preset> presets,
        ReadOnlyObservableCollection<PartViewModel> parts, CancellationToken token)
    {
        // Every user preset goes through here so it reaches both the shared list (which the parts hold
        // by reference, and which PreSelectConfiguredPreset scans) and each part's live source cache.
        // The caches are read on the UI thread, and this method's continuations resume there too, so
        // the writes stay on that thread. Downstream Batch() coalesces the notifications.
        void AddPreset(Integra7Preset p)
        {
            if (token.IsCancellationRequested) return;
            presets.Add(p);
            foreach (var pvm in parts) pvm.AddPreset(p);
        }

        // One checkpoint per list: reports progress and abandons the remaining lists once a rescan has
        // superseded this load, instead of holding the MIDI semaphore for the rest of the sweep.
        void Step(string info)
        {
            token.ThrowIfCancellationRequested();
            BackgroundInfo = info;
        }

        Step("Loading PCM Drum Kit User Names 0-31...");
        List<string> names = await Integra7?.GetPCMDrumKitUserNames0to31();
        var pc = 0;
        var id = presets.Count;
        foreach (var n in names)
        {
            var msb = 86;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "PCMD", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Drums" /*todo incorrect*/));
            id++;
        }

        Step("Loading PCM Synth Tone User Names 0-63...");
        names = await Integra7?.GetPCMToneUserNames0to63();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 87;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "PCMS", "PRST" /* todo incorrect */, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading PCM Synth Tone User Names 64-127...");
        names = await Integra7?.GetPCMToneUserNames64to127();
        foreach (var n in names)
        {
            var msb = 87;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "PCMS", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading PCM Synth Tone User Names 128-191...");
        names = await Integra7?.GetPCMToneUserNames128to191();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 87;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "PCMS", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading PCM Synth Tone User Names 192-255...");
        names = await Integra7?.GetPCMToneUserNames192to255();
        foreach (var n in names)
        {
            var msb = 87;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "PCMS", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Drum Kit User Names 0-63...");
        names = await Integra7?.GetSuperNATURALDrumKitUserNames0to63();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 88;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-D", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Drums" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Acoustic Tone User Names 0-63...");
        names = await Integra7?.GetSuperNATURALAcousticToneUserNames0to63();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 89;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-A", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Ac.Piano" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Acoustic Tone User Names 64-127...");
        names = await Integra7?.GetSuperNATURALAcousticToneUserNames64to127();
        foreach (var n in names)
        {
            var msb = 89;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-A", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Ac.Piano" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Acoustic Tone User Names 128-191...");
        names = await Integra7?.GetSuperNATURALAcousticToneUserNames128to191();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 89;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-A", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Ac.Piano" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Acoustic Tone User Names 192-255...");
        names = await Integra7?.GetSuperNATURALAcousticToneUserNames192to255();
        foreach (var n in names)
        {
            var msb = 89;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-A", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Ac.Piano" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 0-63...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames0to63();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 64-127...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames64to127();
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 0;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 128-191...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames128to191();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 192-255...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames192to255();
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 1;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 256-319...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames256to319();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 2;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 320-383...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames320to383();
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 2;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 384-447...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames384to447();
        pc = 0;
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 3;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }

        Step("Loading SuperNATURAL Synth Tone User Names 448-511...");
        names = await Integra7?.GetSuperNATURALSynthToneUserNames448to511();
        foreach (var n in names)
        {
            var msb = 95;
            var lsb = 3;
            pc++;
            AddPreset(new Integra7Preset(id, "USR", "SN-S", "PRST" /*todo incorrect*/, pc,
                n, msb, lsb, pc, "Synth Lead" /*todo incorrect*/));
            id++;
        }
    }

    private int _currentPartSelection;

    public int CurrentPartSelection
    {
        get => _currentPartSelection;
        set
        {
            UserActionLog.Action($"select tab index {value} ({(value == 0 ? "Common" : $"Part {value}")})");
            this.RaiseAndSetIfChanged(ref _currentPartSelection, value);
            this.RaisePropertyChanged(nameof(CurrentPartIsNotCommonPart));
            // Opening a part's tab is what pays for the rest of its state. EnsureInitializedAsync is
            // idempotent, so returning to a tab costs nothing.
            _ = EnsureSelectedPartInitializedAsync(value);
        }
    }

    /// <summary>Show a part's own tab: select it, and bring the Parameters tab to the front, since the
    /// mixer is a sibling of that tab rather than inside it. The part tab strip's selection is
    /// <see cref="CurrentPartSelection"/>, which counts the Common tab as 0, and the Parameters tab is the
    /// first TabItem of the top-level TabControl.</summary>
    private void OpenPartTab(int zeroBasedPartNo)
    {
        CurrentPartSelection = zeroBasedPartNo + 1;
        TopTabIndex = 0;
    }

    /// <summary>Show one of the Common tab's friendly editors, named by the <c>Tag</c> on its TabItem. Used by
    /// the mixer strips' Chorus and Reverb buttons: a strip's send knobs feed one shared chorus and one shared
    /// reverb, and this is how the user gets from "how much of this part" to "and what does it do".
    ///
    /// Three moves, because the target is two levels in: the Parameters tab, then the Common part tab, then
    /// the sub-tab itself. The last goes through <c>CommonTabKey</c>, which
    /// <c>TabControlBehaviors.SelectTabByTag</c> watches -- cleared first, because setting the same tag twice
    /// running would otherwise not raise and a second press of the same button would do nothing. That
    /// clear-then-set is the same dance the friendly editors' own "Advanced …" buttons do.</summary>
    private void OpenCommonTab(string tag)
    {
        if (PartViewModels is null || PartViewModels.Count == 0) return;

        TopTabIndex = 0;
        CurrentPartSelection = 0; // the Common tab
        PartViewModels[0].CommonTabKey = "";
        PartViewModels[0].CommonTabKey = tag;
    }

    /// <summary>Show a part's own Set Part tab — where the four fade-width knobs are. The layer map draws fades
    /// but does not drag them, so this is how the user gets from seeing a crossfade to changing it.
    ///
    /// Three moves, like <see cref="OpenCommonTab"/>, because the target is two levels in: the Parameters tab,
    /// then the part's tab, then the sub-tab itself. The last goes through <c>ToneTabKey</c>, which
    /// <c>TabControlBehaviors.SelectTabByTag</c> watches — cleared first, because setting the same tag twice
    /// running raises nothing and a second press of the same button would go nowhere. The tag is
    /// <c>SET-PART-FRIENDLY</c> and not <c>SET-PART</c>: the unsuffixed name belongs to the raw Studio Set Part
    /// grid under "Advanced", and the friendly editor with the knobs on it is the one the user is being sent
    /// to.</summary>
    private void OpenPartSetPartTab(int zeroBasedPartNo)
    {
        if (PartViewModels is null || zeroBasedPartNo + 1 >= PartViewModels.Count) return;

        TopTabIndex = 0;
        CurrentPartSelection = zeroBasedPartNo + 1;
        PartViewModels[zeroBasedPartNo + 1].ToneTabKey = "";
        PartViewModels[zeroBasedPartNo + 1].ToneTabKey = "SET-PART-FRIENDLY";
    }

    /// <summary>Sound one note on a part's own channel and let it go again: what a press on the layer map asks
    /// for. Static, and handed the API rather than reading <see cref="Integra7"/>, so a rescan swapping the
    /// connection mid-note cannot leave the note-off going to a different port than the note-on did.
    ///
    /// Failures are swallowed whole, as the note rails' auditions are: an audition is the user asking a
    /// question about their instrument, not an edit to it, and there is nothing to report and nothing to retry
    /// if the port declines to answer. Everything that can throw is inside the try, so the async void this is
    /// launched from has nothing to escape through.</summary>
    private static async Task AuditionOnPartAsync(IIntegra7Api api, int zeroBasedPartNo, int note, int velocity)
    {
        // How long the note is held. The same 300ms the drum-kit editors' note rails use, so a note sounded
        // from the layer map is the same length as one sounded from a rail.
        const int holdMilliseconds = 300;


        try
        {
            await api.NoteOnAsync((byte)zeroBasedPartNo, (byte)note, (byte)velocity);
            await Task.Delay(holdMilliseconds);
            await api.NoteOffAsync((byte)zeroBasedPartNo, (byte)note);
        }
        catch { /* ignore — auditioning is non-essential */ }
    }

    /// <summary>Initializes the part behind a tab index, reporting progress on the status bar. Runs
    /// unawaited from the selection setter, so it swallows its own failures.</summary>
    private async Task EnsureSelectedPartInitializedAsync(int tabIndex)
    {
        if (PartViewModels is null || tabIndex < 0 || tabIndex >= PartViewModels.Count) return;

        var pvm = PartViewModels[tabIndex];
        if (pvm.IsCommonTab || pvm.IsInitialized) return;

        try
        {
            BackgroundInfo = $"Loading part {tabIndex}...";
            // The BEGIN/END pair is logged inside the part itself, so every initialization is bracketed.
            await pvm.EnsureInitializedAsync();
        }
        catch (Exception e)
        {
            UserActionLog.Failed($"initialize part {tabIndex}", e.ToString());
        }
        finally
        {
            // Do not clear a message the background preset loader may have put there in the meantime.
            if (BackgroundInfo == $"Loading part {tabIndex}...") BackgroundInfo = "";
        }
    }

    private List<Integra7Preset> LoadPresets()
    {
        var uri = @"avares://" + "Integra7AuralAlchemist/" + "Assets/Presets.csv";
        var file = new StreamReader(AssetLoader.Open(new Uri(uri)));
        var data = file.ReadLine();
        char[] separators = [','];
        List<Integra7Preset> Presets = [];
        var id = 0;
        while ((data = file.ReadLine()) != null)
        {
            string[] read = data.Split(separators, StringSplitOptions.None);
            var tonetype = read[0].Trim('"');
            var tonebank = read[1].Trim('"');
            var number = int.Parse(read[2]);
            var name = read[3].Trim('"');
            var msb = int.Parse(read[4]);
            var lsb = int.Parse(read[5]);
            var pc = int.Parse(read[6]);
            var category = read[7].Trim('"');
            Presets.Add(new Integra7Preset(id, "INT", tonetype, tonebank, number, name, msb, lsb, pc, category));
            id++;
        }

        return Presets;
    }

    public MainWindowViewModel()
    {
        MessageBus.Current.Listen<UpdateMessageSpec>("ui2hw").Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Subscribe(async m => await UpdateIntegraFromUiAsync(m));
        MessageBus.Current.Listen<UpdateFromSysexSpec>("hw2ui").Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Subscribe(async m => await UpdateUiFromIntegraAsync(m));
        MessageBus.Current.Listen<UpdateResyncPart>().Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Subscribe(async m => await ResyncPartAsync(m.PartNo));
        MessageBus.Current.Listen<UpdateSetPresetAndResyncPart>()
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Subscribe(async m => await SetPresetAndResyncPartAsync(m.PartNo));

        // The journal is mutated from whichever thread made the edit -- the friendly editors record on
        // the UI thread, the raw grid's path on a pool thread (its message bus subscription is
        // throttled) -- and Changed fires from that thread, so the property writes have to be posted.
        // It fires once per setter call, so a knob drag raises it hundreds of times; every one of those
        // is a no-op assignment that ReactiveUI drops, which is cheap enough not to coalesce until
        // something shows it is not.
        EditJournal.Default.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            CanUndo = EditJournal.Default.CanUndo;
            CanRedo = EditJournal.Default.CanRedo;
            CanCompare = EditJournal.Default.CanCompare;
            CompareLabel = EditJournal.Default.IsComparing ? "Hearing the original" : "Compare";
        });

        ShowSaveUserToneDialog = new Interaction<SaveUserToneViewModel, UserToneToSave?>();
        ShowSaveSnapshotDialog = new Interaction<string, string?>();
        ShowOpenSnapshotDialog = new Interaction<Unit, string?>();
        ShowSaveToLibraryDialog = new Interaction<SaveToLibraryViewModel, SnapshotMetadata?>();
        ShowPickLibraryFolderDialog = new Interaction<string, string?>();
        ShowConfirmDialog = new Interaction<ConfirmViewModel, bool>();
        ShowRandomiseToneDialog = new Interaction<RandomiseToneViewModel, bool>();

        // Fired from whichever thread called Put -- see ToneClipboard.Changed -- and CanPasteTone is
        // bound to a button, so it is set on the UI thread. Posted the same way the journal's Changed
        // above is, rather than through a scheduler: this build of ReactiveUI has no RxApp.
        _toneClipboard.Changed += () =>
            Dispatcher.UIThread.Post(() => CanPasteTone = _toneClipboard.HasContent);

        // After the interactions it reaches through, since it lists its folder while being constructed and
        // reporting a folder that cannot be read needs the status properties -- which are fields on this object
        // and therefore already there, unlike the interactions, which are not until the lines above have run.
        //
        // The two callbacks are closures over this object rather than over anything captured now, so a rescan
        // replacing the API is invisible to them: LoadFromLibraryAsync reads Integra7 when a press happens, and
        // the folder picker's interaction is the same instance for the life of the window.
        LibraryVm = new LibraryViewModel(
            LoadFromLibraryAsync,
            async folder => await ShowPickLibraryFolderDialog.Handle(folder),
            (message, failed) =>
            {
                // The window's own status bar, not a line of the library's own: it is visible from every tab,
                // the save and load commands already report there, and one channel means the user never has to
                // wonder which of two places the last answer went to.
                SnapshotStatus = message;
                SnapshotFailed = failed;
            },
            LibrarySettings.SettingsPath);
    }

    public async Task InitializeAsync()
    {
        // Breadcrumbs: opening the MIDI ports blocks on an async open, and everything that follows
        // depends on it. Without these, a stall here leaves a log containing only "starting".
        Log.Information("Opening the MIDI ports.");
        Integra7 = new Integra7Api(
            new MidiPort(new MidiOut(INTEGRA_CONNECTION_STRING), new MidiIn(INTEGRA_CONNECTION_STRING)));
        Log.Information("MIDI ports open; checking the device identity.");
        await Integra7.CheckIdentityAsync();
        Log.Information("Identity check done; connected: {Connected}.", Integra7.ConnectionOk());
        List<Integra7Preset> presets = LoadPresets();
        await UpdateConnectedAsync(Integra7, presets);
    }

    private async Task UpdateIntegraFromUiAsync(UpdateMessageSpec s)
    {
        var p = s.Par;
        UserActionLog.Action($"edit parameter '{p.ParSpec.Path}' -> '{s.DisplayValue}'");
        // Before the assignment below: afterwards the value it replaced is gone. Record ignores this
        // while an undo is being applied, so an undo writing through here cannot record itself.
        EditJournal.Default.Record(new ParameterChange(
            Start: p.Start, Offset: p.Offset, Offset2: p.Offset2, Path: p.ParSpec.Path,
            OldValue: p.StringValue, NewValue: s.DisplayValue,
            IsDiscriminator: p.ParSpec.IsParent));
        p.StringValue = s.DisplayValue;
        if (Integra7 is null) return;
        // One conversation, for the same reason as the friendly editors' writes: the re-read must see
        // the state this write produced.
        await using var lease = await Integra7!.BeginConversationAsync($"edit {p.ParSpec.Path}");
        await _integra7Communicator?.WriteSingleParameterToIntegraAsync(p, lease);
        if (p.ParSpec.IsParent)
        {
            var resetDomain = _integra7Communicator?.GetDomain(p);
            if (resetDomain != null)
            {
                await WaveOutOfRangeReset.ApplyAsync(resetDomain, p, WaveformBanks.Default, lease);
                await resetDomain.ReadFromIntegraAsync(lease);
            }
            ForceUiRefresh(p);
        }
    }

    private async Task UpdateUiFromIntegraAsync(UpdateFromSysexSpec s)
    {
        List<UpdateMessageSpec> parameters =
            SysexDataTransmissionParser.ConvertSysexToParameterUpdates(s.SysexMsg, _integra7Communicator);

        // A Studio Set was selected on the device. Nothing below is enough: every part now holds a
        // different tone and every common block a different value, so updating only the reported
        // parameter would leave the whole window describing the Studio Set that just went away.
        if (parameters.Any(spec => StudioSetSelectors.Contains(spec.Par.ParSpec.Path)))
        {
            UserActionLog.Action("device reported a Studio Set change; resyncing everything");
            // A Studio Set was selected on the front panel: every step in the history names a value in
            // the Studio Set that just went away, so applying one would write it into a patch that never
            // had it.
            EditJournal.Default.Clear();
            await ResyncAllPartsAsync();
            return;
        }

        var ParentControlModified = parameters.Any(spec => spec.Par.ParSpec.IsParent);
        var PresetChanged = parameters.Any(spec =>
            spec.Par.ParSpec.Path.Contains("Tone Bank Select") ||
            spec.Par.ParSpec.Path.Contains("Tone Bank Program Number"));
        var HighImpactControlChanged = ParentControlModified || PresetChanged;
        if (!HighImpactControlChanged)
        {
            // Update the reported parameters and nothing else. There is deliberately no ForceUiRefresh
            // here: the controls follow the model through INotifyPropertyChanged, and the only thing a
            // refresh adds is re-evaluating which parameters are visible. That depends on IsParent
            // parameters -- IsParent is generated as exactly the set of paths some parameter names as
            // its ParentCtrl -- and none of those changed, which is what makes this the low-impact
            // branch. The refresh that used to be here ran once per reported parameter, always for
            // parameters.First(), and could not affect anything.
            foreach (var spec in parameters)
                _integra7Communicator?.GetDomain(spec.Par)
                    .ModifySingleParameterDisplayedValue(spec.Par.ParSpec.Path, spec.DisplayValue);
        }
        else
        {
            // need to resync all relevant parameters instead of just updating the modified parameters
            HashSet<string> alreadyEncountered = [];
            foreach (var spec in parameters)
            {
                var domainName = spec.Par.Start + spec.Par.Offset;
                if (alreadyEncountered.Add(domainName))
                {
                    await _integra7Communicator?.GetDomain(spec.Par).ReadFromIntegraAsync();
                    ForceUiRefresh(spec.Par);
                }
            }
        }
    }

    private void ForceUiRefresh(FullyQualifiedParameter p)
    {
        ForceUiRefresh(p.Start, p.Offset, p.Offset2, p.ParSpec.Path, p.ParSpec.IsParent);
    }

    private void ForceUiRefresh(string StartAddressName, string OffsetAddressName, string Offset2AddressName,
        string ParPath, bool ResyncNeeded)
    {
        if (PartViewModels != null)
            foreach (var pvm in PartViewModels)
                pvm.ForceUiRefresh(StartAddressName, OffsetAddressName, Offset2AddressName, ParPath, ResyncNeeded);
    }

    private async Task ResyncAllPartsAsync()
    {
        try
        {
            SignalStartSync();
            if (PartViewModels != null)
                foreach (var pvm in PartViewModels)
                {
                    // A part that was never opened still has its mix state on screen: the Mixer tab shows
                    // every part's level, pan, mute, sends and tone name whether its tab has been opened or
                    // not. So its Studio Set Part block is re-read and its preset resolved again -- one read
                    // per part, no tone loads (see ResyncMixStateAsync) -- and the expensive rest of a
                    // resync, the tone domains and the partial view models, is still skipped because none of
                    // it exists yet. Before the mixer, skipping such a part entirely was right; after it,
                    // that left the previous Studio Set's tone names beside the new one's sounds.
                    if (!pvm.IsCommonTab && !pvm.WantsRefresh)
                    {
                        SyncInfo = $"Resync part {pvm.PartNo} mix state";
                        await pvm.ResyncMixStateAsync();
                        continue;
                    }

                    SyncInfo = $"Resync part {pvm.PartNo}";
                    await pvm.EnsurePreselectIsNotNullAsync();
                    await pvm.ResyncPartAsync((byte)pvm.PartNo);
                }
        }
        finally
        {
            SignalStopSync();
        }
    }

    private async Task ResyncPartAsync(byte part)
    {
        try
        {
            SignalStartSync();
            if (PartViewModels != null)
            {
                var i = 0;
                foreach (var pvm in PartViewModels)
                {
                    SyncInfo = $"Resync part {i}";
                    i++;
                    // Same reasoning as ResyncAllPartsAsync: an unopened part refreshes itself when
                    // opened. ResyncPartAsync below ignores every part except `part` anyway.
                    if (!pvm.IsCommonTab && !pvm.WantsRefresh) continue;
                    await pvm.EnsurePreselectIsNotNullAsync();
                    await pvm.ResyncPartAsync(part);
                }
            }
        }
        finally
        {
            SignalStopSync();
        }
    }

    private async Task SetPresetAndResyncPartAsync(byte part)
    {
        try
        {
            SignalStartSync();
            if (PartViewModels != null)
                foreach (var pvm in PartViewModels)
                    if (part == pvm.PartNo)
                    {
                        SyncInfo = $"Resync part {pvm.PartNo}";
                        // The preset itself is refreshed even for a part that was never opened: it is a
                        // single read, and the preset list and tab visibility show it everywhere.
                        var b = _integra7Communicator.StudioSetPart(part);
                        // Only when the read answered: a failed one keeps the previous values, and a
                        // preset derived from those claims a patch the device never reported. See
                        // PartViewModel.EnsurePreselectIsNotNullAsync.
                        if (await b.ReadFromIntegraAsync())
                            pvm.PreSelectConfiguredPreset(b);
                        else
                            Log.Warning("Part {Part}: not preselecting a preset, the device did not answer.", part);
                        // The full resync is not, since an unopened part reads everything when opened.
                        // A part whose initialization was cancelled by this very preset change does
                        // need it though: ResyncPartAsync re-initializes it, now that the device has
                        // confirmed the new tone.
                        if (pvm.IsCommonTab || pvm.WantsRefresh)
                            await pvm.ResyncPartAsync(part);
                    }
        }
        finally
        {
            SignalStopSync();
        }
    }

#pragma warning restore CA1822 // Mark members as static
#pragma warning restore CS8618 // nullable must be assigned in constructor
}
