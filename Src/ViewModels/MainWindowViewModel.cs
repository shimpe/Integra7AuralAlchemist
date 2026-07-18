using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
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

    private readonly SemaphoreSlim _semaphore = new(1, 1);

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

    private int _syncLevels = 0;

    /// <summary>Progress of work that runs after the window is usable (currently the user tone names).
    /// Unlike <see cref="SyncInfo"/> this drives a status line rather than the blocking overlay.</summary>
    [Reactive] private string _backgroundInfo = "";

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

    [ReactiveCommand]
    public async Task SaveUserTone()
    {
        UserActionLog.Action("button: Save User Tone");
        if (_currentPartSelection == 0)
            return;

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

        List<Integra7Preset> presets = PartViewModels[1].Presets.ToList();
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

                // also update name in preset list
                var presetId = -1;
                foreach (var p in presets)
                    if (p.ToneTypeStr == toneType && p.InternalUserDefinedStr == "USR")
                    {
                        presetId++;
                        if (presetId == tone.ZeroBasedMemoryId) p.Name = name;
                    }
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
        Integra7 = new Integra7Api(new MidiOut(INTEGRA_CONNECTION_STRING), new MidiIn(INTEGRA_CONNECTION_STRING),
            _semaphore);
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
        IsSyncing = true;
        _syncLevels = _syncLevels + 1;
        Log.Debug($"Start Sync. Sync level is now {_syncLevels}.");
    }

    private void SignalStopSync()
    {
        _syncLevels = _syncLevels - 1;
        if (_syncLevels < 0) // happens when starting the program while integra-7 is not switched on/connected
        {
            _syncLevels = 0;
        }
        Log.Debug($"Stop Sync. Sync level is now {_syncLevels}.");
        if (_syncLevels == 0)
        {
            Log.Debug($"Hide Sync notification.");
            IsSyncing = false;
            SyncInfo = "";
        }
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
                _integra7Communicator = new Integra7Domain(integra7Api, _i7startAddresses, _i7parameters, _semaphore);

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
                        _integra7Communicator, _semaphore, presets, commonTab);
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
            UserActionLog.Begin($"initialize part {tabIndex}");
            await pvm.EnsureInitializedAsync();
            UserActionLog.End($"initialize part {tabIndex}");
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

        ShowSaveUserToneDialog = new Interaction<SaveUserToneViewModel, UserToneToSave?>();
    }

    public async Task InitializeAsync()
    {
        Integra7 = new Integra7Api(new MidiOut(INTEGRA_CONNECTION_STRING), new MidiIn(INTEGRA_CONNECTION_STRING),
            _semaphore);
        await Integra7.CheckIdentityAsync();
        List<Integra7Preset> presets = LoadPresets();
        await UpdateConnectedAsync(Integra7, presets);
    }

    private async Task UpdateIntegraFromUiAsync(UpdateMessageSpec s)
    {
        var p = s.Par;
        UserActionLog.Action($"edit parameter '{p.ParSpec.Path}' -> '{s.DisplayValue}'");
        p.StringValue = s.DisplayValue;
        await _integra7Communicator?.WriteSingleParameterToIntegraAsync(p);
        if (p.ParSpec.IsParent)
        {
            var resetDomain = _integra7Communicator?.GetDomain(p);
            if (resetDomain != null)
            {
                await WaveOutOfRangeReset.ApplyAsync(resetDomain, p, WaveformBanks.Default);
                await resetDomain.ReadFromIntegraAsync();
            }
            ForceUiRefresh(p);
        }
    }

    private async Task UpdateUiFromIntegraAsync(UpdateFromSysexSpec s)
    {
        List<UpdateMessageSpec> parameters =
            SysexDataTransmissionParser.ConvertSysexToParameterUpdates(s.SysexMsg, _integra7Communicator);
        var ParentControlModified = parameters.Any(spec => spec.Par.ParSpec.IsParent);
        var PresetChanged = parameters.Any(spec =>
            spec.Par.ParSpec.Path.Contains("Tone Bank Select") ||
            spec.Par.ParSpec.Path.Contains("Tone Bank Program Number"));
        var HighImpactControlChanged = ParentControlModified || PresetChanged;
        if (!HighImpactControlChanged)
        {
            // update only the affected parameters
            foreach (var spec in parameters)
            {
                _integra7Communicator?.GetDomain(spec.Par)
                    .ModifySingleParameterDisplayedValue(spec.Par.ParSpec.Path, spec.DisplayValue);
                ForceUiRefresh(parameters.First().Par);
            }
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
                    if (!pvm.IsCommonTab && !pvm.IsInitialized) continue;
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
                    if (!pvm.IsCommonTab && !pvm.IsInitialized) continue;
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
                        await b.ReadFromIntegraAsync();
                        pvm.PreSelectConfiguredPreset(b);
                        // The full resync is not, since an unopened part reads everything when opened.
                        if (pvm.IsCommonTab || pvm.IsInitialized) await pvm.ResyncPartAsync(part);
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
