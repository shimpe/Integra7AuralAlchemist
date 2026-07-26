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
                    ? "Playing the sound from before the edits. Press Compare again to hear them." +
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

    /// <summary>Read the Studio Set currently in the instrument and write it to a file the user picks.
    /// </summary>
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

        // The Studio Set names itself; that is a far better default file name than "snapshot".
        var name = communicator.StudioSetCommon
            .LookupSingleParameterDisplayedValue("Studio Set Common/Studio Set Name").Trim();
        if (name.Length == 0) name = "Studio Set";

        // The instrument's character set includes ':', '/' and '*', which a file name cannot hold; the
        // snapshot keeps the real name, only the suggestion in the dialog is scrubbed.
        var suggested = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var path = await ShowSaveSnapshotDialog.Handle(suggested + ".json");
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
            SnapshotStatus = $"Saved the Studio Set to {Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("save Studio Set", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not save the Studio Set: {e.Message}";
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
    /// cannot end up named after one patch and holding another.</summary>
    private sealed record SelectedTone(int ZeroBasedPartNo, string ToneType, string ToneName);

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
        return new SelectedTone(selection - 1, toneType, name.Length == 0 ? "Tone" : name);
    }

    /// <summary>Read the tone currently loaded into the selected part and write it to a file the user
    /// picks. The Studio Set sibling of this is <see cref="SaveStudioSetAsync"/>, and everything about
    /// the lease, the file dialog's "" sentinel and the atomic write is the same there.</summary>
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

        // The instrument's character set includes ':', '/' and '*', which a file name cannot hold; the
        // snapshot keeps the real name, only the suggestion in the dialog is scrubbed.
        var suggested = string.Join("_", selected.ToneName.Split(Path.GetInvalidFileNameChars()));
        var path = await ShowSaveSnapshotDialog.Handle(suggested + ".json");
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
            SnapshotStatus = $"Saved the tone from part {selected.ZeroBasedPartNo + 1} to {Path.GetFileName(path)}.";
        }
        catch (Exception e)
        {
            UserActionLog.Failed("save tone", e.ToString());
            SnapshotFailed = true;
            SnapshotStatus = $"Could not save the tone: {e.Message}";
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

        var restored = false;
        try
        {
            SignalStartSync();
            SyncInfo = $"Writing tone to part {selected.ZeroBasedPartNo + 1}";
            var snapshot = Integra7Snapshot.FromJson(await File.ReadAllTextAsync(path));

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
            SnapshotStatus = $"Sent the tone from {Path.GetFileName(path)} to part {selected.ZeroBasedPartNo + 1}.";
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
                    // A part that was never opened has nothing to refresh: it reads the current state
                    // when it is first opened, so resyncing it now would only spend round trips.
                    if (!pvm.IsCommonTab && !pvm.WantsRefresh) continue;
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
