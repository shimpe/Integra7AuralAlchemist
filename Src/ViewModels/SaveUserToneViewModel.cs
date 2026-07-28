using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Integra7AuralAlchemist.ViewModels;

public partial class SaveUserToneViewModel : ViewModelBase
{
    private readonly ReadOnlyObservableCollection<Integra7Preset> _presets = new([]);
    private readonly SourceCache<Integra7Preset, int> _sourceCachePresets = new(x => x.Id);
    private readonly string _toneTypeStr;
    private readonly List<Integra7Preset> i7presets = [];

    private IDisposable? _cleanupPresets;
    private IDisposable? _cleanupCanSave;

    private string _newName = "";
    [Reactive] private string _searchTextPreset = "";

    /// <summary>The user slot the user clicked in the grid, delivered by the selector's two-way binding.
    /// This -- not its row index -- is what identifies the slot to overwrite.</summary>
    [Reactive] private Integra7Preset? _selectedPreset;

    private UserToneToSave? _userToneToSave;

    /// <param name="presets">The complete, unfiltered preset list (PartViewModel.AllPresets). The grid
    /// below filters it down to this tone type's user slots, but the slot *number* is counted over the
    /// whole thing -- see <see cref="UserToneSlots" />.</param>
    /// <param name="toneTypeStr">The engine whose user slots may be written: "PCMS", "PCMD", "SN-S",
    /// "SN-A" or "SN-D".</param>
    public SaveUserToneViewModel(IReadOnlyList<Integra7Preset> presets, string toneTypeStr)
    {
        _toneTypeStr = toneTypeStr;
        i7presets.AddRange(presets);
        _sourceCachePresets.AddOrUpdate(i7presets);

        CancelCommand = ReactiveCommand.Create(() =>
        {
            _userToneToSave = null;
            return _userToneToSave;
        });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            // The slot number is a hardware address, so it is counted over the full list rather than
            // read off the grid: the grid is filtered by the search box, and a row index in it only
            // agrees with the slot numbering while that box is empty.
            var slot = UserToneSlots.ZeroBasedSlotOf(i7presets, _toneTypeStr, SelectedPreset);
            if (SelectedPreset is null || slot < 0)
            {
                // No usable target. Answer as if cancelled rather than saving over a guess -- the
                // caller already treats null that way, and a wrong slot destroys a saved sound.
                Log.Warning("Not saving a user tone: no {ToneType} user slot is selected.", _toneTypeStr);
                _userToneToSave = null;
                return _userToneToSave;
            }

            _userToneToSave = new UserToneToSave(_newName, slot, SelectedPreset);
            return _userToneToSave;
        });

        // The generated SelectedPreset setter announces itself but knows nothing of CanSave, so the
        // Save button would stay disabled until the name was edited again after picking a row.
        _cleanupCanSave = this.WhenAnyValue(x => x.SelectedPreset)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CanSave)));

        var parFilterPreset = this.WhenAnyValue(x => x.SearchTextPreset)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .DistinctUntilChanged()
            .Select(text => FilterProvider.SaveTonePresetFilter(_toneTypeStr, text));

        _cleanupPresets = _sourceCachePresets.Connect()
            .Batch(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Filter(parFilterPreset)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SortAndBind(
                out _presets,
                SortExpressionComparer<Integra7Preset>.Ascending(t => t.Id))
            .DisposeMany()
            .Subscribe();
    }

    public ReadOnlyObservableCollection<Integra7Preset> Presets => _presets;

    public string NewName
    {
        get => _newName;
        set
        {
            this.RaisePropertyChanging();
            this.RaisePropertyChanging(nameof(NewNameNotEmpty));
            _newName = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(NewNameNotEmpty));
            this.RaisePropertyChanged(nameof(CanSave));
        }
    }

    public bool NewNameNotEmpty => NewName != "";

    /// <summary>Whether Save can do anything. Without the selection half, clicking Save with no row
    /// picked closes the dialog and writes nothing, with only a log line to say why -- the command
    /// answers null, which the caller cannot tell apart from Cancel. Better to not offer it.</summary>
    public bool CanSave => NewNameNotEmpty && SelectedPreset is not null;

    // Qualified for the reason ConfirmViewModel spells out: ReactiveUI 24 ships two ReactiveCommand<,>
    // types and an alias cannot name an open generic, so the declaration has to say which it means.
    public ReactiveUI.Reactive.ReactiveCommand<Unit, UserToneToSave?> CancelCommand { get; }
    /// <summary>Yields null -- which the caller reads as "cancelled" -- when there is no user slot to
    /// write to.</summary>
    public ReactiveUI.Reactive.ReactiveCommand<Unit, UserToneToSave?> SaveCommand { get; }
}