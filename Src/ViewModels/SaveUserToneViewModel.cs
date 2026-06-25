using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Integra7AuralAlchemist.Models.Data;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

public partial class SaveUserToneViewModel : ViewModelBase
{
    private readonly ReadOnlyObservableCollection<Integra7Preset> _presets = new([]);
    private readonly SourceCache<Integra7Preset, int> _sourceCachePresets = new(x => x.Id);
    private readonly string _toneTypeStr;
    private readonly List<Integra7Preset> i7presets = [];

    private IDisposable? _cleanupPresets;

    private string _newName = "";
    [Reactive] private string _searchTextPreset = "";

    private UserToneToSave? _userToneToSave;

    public SaveUserToneViewModel(List<Integra7Preset> presets, string toneTypeStr)
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
            _userToneToSave = new UserToneToSave(_newName, SelectedPresetIndex);
            return _userToneToSave;
        });

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

    public Integra7Preset? SelectedPreset { get; }

    public int SelectedPresetIndex { get; set; }
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
        }
    }

    public bool NewNameNotEmpty => NewName != "";

    public ReactiveCommand<Unit, UserToneToSave?> CancelCommand { get; }
    public ReactiveCommand<Unit, UserToneToSave> SaveCommand { get; }
}