using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

public partial class PartViewModel : ViewModelBase
{
    private readonly IIntegra7Api _i7Api;
    private readonly Integra7Parameters _i7parameters;

    private readonly List<Integra7Preset> _i7presets;
    private readonly Integra7StartAddresses _i7startAddresses;
    private readonly MainWindowViewModel _mwvm;

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMDrumKitCommon2Parameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMDrumKitCommonMFXParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMDrumKitCommonParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMDrumKitCompEQParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMSynthToneCommon2Parameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMSynthToneCommonMFXParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMSynthToneCommonParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMSynthTonePMTParameters = new([]);
    private readonly ReadOnlyObservableCollection<Integra7Preset> _presets = new([]);
    private readonly SemaphoreSlim _semaphore;

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _setupParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNAcousticToneCommonMFXParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNAcousticToneCommonParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNDrumKitCommonMFXParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNDrumKitCommonParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNDrumKitCompEQParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNSynthToneCommonMFXParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNSynthToneCommonParameters = new([]);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMDrumKitCommon2Parameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMDrumKitCommonMFXParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMDrumKitCommonParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMDrumKitCompEQParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMSynthToneCommon2Parameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMSynthToneCommonMFXParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMSynthToneCommonParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMSynthTonePMTParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<Integra7Preset, int> _sourceCachePresets = new(x => x.Id);


    private readonly SourceCache<FullyQualifiedParameter, string>
        _sourceCacheSetupParameters = new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNAcousticToneCommonMFXParameters =
        new(x => x.ParSpec.Path);


    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNAcousticToneCommonParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNDrumKitCommonMFXParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNDrumKitCommonParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNDrumKitCompEQParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNSynthToneCommonMFXParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNSynthToneCommonParameters =
        new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetCommonChorusParameters =
        new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetCommonMasterEQParameters =
        new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string>
        _sourceCacheStudioSetCommonMotionalSurroundParameters = new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetCommonParameters =
        new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetCommonReverbParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetMidiParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetPartEQParameters =
        new(x => x.ParSpec.Path);

    //
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheStudioSetPartParameters =
        new(x => x.ParSpec.Path);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSystem = new(x => x.ParSpec.Path);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetCommonChorusParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetCommonMasterEQParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetCommonMotionalSurroundParameters =
        new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetCommonParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetCommonReverbParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetMidiParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _StudioSetPartEQParameters = new([]);

    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _studioSetPartParameters = new([]);
    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _systemParameters = new([]);
    private IDisposable? _cleanupMidiParams;
    private IDisposable? _cleanupMotionalSurround;
    private IDisposable? _cleanupPCMDrumKitCommon2Params;
    private IDisposable? _cleanupPCMDrumKitCommonMFXParams;

    private IDisposable? _cleanupPCMDrumKitCommonParams;
    private IDisposable? _cleanupPCMDrumKitCompEQParametersParams;
    private IDisposable? _cleanupPCMSynthToneCommon2Params;
    private IDisposable? _cleanupPCMSynthToneCommonMFXParams;

    private IDisposable? _cleanupPCMSynthToneCommonParams;
    private IDisposable? _cleanupPCMSynthTonePMTParametersParams;

    private IDisposable? _cleanupPresets;

    private IDisposable? _cleanupSetup;
    private IDisposable? _cleanupSNAcousticToneCommonMFXParams;

    private IDisposable? _cleanupSNAcousticToneCommonParams;
    private IDisposable? _cleanupSNDrumKitCommonMFXParams;

    private IDisposable? _cleanupSNDrumKitCommonParams;
    private IDisposable? _cleanupSNDrumKitCompEQParametersParams;
    private IDisposable? _cleanupSNSynthToneCommonMFXParams;

    private IDisposable? _cleanupSNSynthToneCommonParams;
    private IDisposable? _cleanupStudioSetChorus;
    private IDisposable? _cleanupStudioSetCommon;
    private IDisposable? _cleanupStudioSetMasterEQ;
    private IDisposable? _cleanupStudioSetPartEQParams;
    private IDisposable? _cleanupStudioSetPartParams;
    private IDisposable? _cleanupStudioSetReverb;
    private IDisposable? _cleanupSystem;
    private Integra7Domain? _i7domain;
    private ViewModelBase _parent;

    //

    //
    [Reactive] private string _refreshCommonChorusNeeded = "";
    [Reactive] public string _refreshCommonMasterEQNeeded = "";
    [Reactive] private string _refreshCommonMotionalSurroundNeeded = "";
    [Reactive] private string _refreshCommonReverbNeeded = "";
    [Reactive] private string _refreshPCMDrumKitCommon = "";
    [Reactive] private string _refreshPCMDrumKitCommon2 = "";
    [Reactive] private string _refreshPCMDrumKitCommonMFX = "";
    [Reactive] private string _refreshPCMDrumKitCompEQ = "";
    [Reactive] private string _refreshPCMSynthToneCommon = "";
    [Reactive] private string _refreshPCMSynthToneCommon2 = "";
    [Reactive] private string _refreshPCMSynthToneCommonMFX = "";
    [Reactive] private string _refreshPCMSynthTonePMT = "";
    [Reactive] private string _refreshSNAcousticToneCommon = "";
    [Reactive] private string _refreshSNAcousticToneCommonMFX = "";
    [Reactive] private string _refreshSNDrumKitCommon = "";
    [Reactive] private string _refreshSNDrumKitCommonMFX = "";
    [Reactive] private string _refreshSNDrumKitCompEQ = "";
    [Reactive] private string _refreshSNSynthToneCommon = "";
    [Reactive] private string _refreshSNSynthToneCommonMFX = "";
    [Reactive] private string _refreshStudioSetPart = "";
    [Reactive] private string _refreshStudioSetPartEQ = "";
    [Reactive] private string _searchSystem = "";

    [Reactive] private string _searchTextPCMDrumKitCommon = "";
    [Reactive] private string _searchTextPCMDrumKitCommon2 = "";
    [Reactive] private string _searchTextPCMDrumKitCommonMFX = "";
    [Reactive] private string _searchTextPCMDrumKitCompEQ = "";

    [Reactive] private string _searchTextPCMSynthToneCommon = "";
    [Reactive] private string _searchTextPCMSynthToneCommon2 = "";
    [Reactive] private string _searchTextPCMSynthToneCommonMFX = "";
    [Reactive] private string _searchTextPCMSynthTonePMT = "";

    [Reactive] private string _searchTextPreset = "";

    [Reactive] private string _searchTextSetup = "";

    [Reactive] private string _searchTextSNAcousticToneCommon = "";
    [Reactive] private string _searchTextSNAcousticToneCommonMFX = "";

    [Reactive] private string _searchTextSNDrumKitCommon = "";
    [Reactive] private string _searchTextSNDrumKitCommonMFX = "";
    [Reactive] private string _searchTextSNDrumKitCompEQ = "";

    [Reactive] private string _searchTextSNSynthToneCommon = "";
    [Reactive] private string _searchTextSNSynthToneCommonMFX = "";
    [Reactive] private string _searchTextStudioSetCommon = "";
    [Reactive] private string _searchTextStudioSetCommonChorus = "";
    [Reactive] private string _searchTextStudioSetCommonMasterEQ = "";
    [Reactive] private string _searchTextStudioSetCommonMotionalSurround = "";
    [Reactive] private string _searchTextStudioSetCommonReverb = "";
    [Reactive] private string _searchTextStudioSetMidi = "";
    [Reactive] private string _searchTextStudioSetPart = "";
    [Reactive] private string _searchTextStudioSetPartEQ = "";
    private Integra7Preset? _selectedPreset;

    //

    //


    public PartViewModel(ViewModelBase parent, byte zeroBasedPartNo, Integra7StartAddresses i7startAddr,
        Integra7Parameters i7par, IIntegra7Api i7, Integra7Domain i7dom,
        SemaphoreSlim semaphore, List<Integra7Preset> i7presets,
        bool commonTab = false)
    {
        _parent = parent;
        _mwvm = parent as MainWindowViewModel;
        PartNo = zeroBasedPartNo;
        _i7startAddresses = i7startAddr;
        _i7parameters = i7par;
        _i7Api = i7;
        _i7domain = i7dom;
        _i7presets = i7presets;
        IsCommonTab = commonTab;
        _selectedPreset = null;
        _semaphore = semaphore;

        _sourceCachePresets.AddOrUpdate(i7presets);
        // InitializeParameterSourceCachesAsync(); // call outside constructor

        if (!commonTab)
        {
            var parFilterPreset = this.WhenAnyValue(
                    x => x.SearchTextPreset,
                    x => x._mwvm.SrxSlot1,
                    x => x._mwvm.SrxSlot2,
                    x => x._mwvm.SrxSlot3,
                    x => x._mwvm.SrxSlot4)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(tuple =>
                {
                    var searchText = tuple.Item1;
                    var srx01 = tuple.Item2;
                    var srx02 = tuple.Item3;
                    var srx03 = tuple.Item4;
                    var srx04 = tuple.Item5;
                    return FilterProvider.PresetFilter(searchText, srx01, srx02, srx03, srx04);
                });
            var parFilterStudioSetMidiParameters = this.WhenAnyValue(x => x.SearchTextStudioSetMidi)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var parFilterStudioSetPartParameters = this.WhenAnyValue(x => x.SearchTextStudioSetPart)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterStudioSetPart = this.WhenAnyValue(x => x.RefreshStudioSetPart)
                .Select(FilterProvider.ParameterFilter);
            var parFilterStudioSetPartEQParameters = this.WhenAnyValue(x => x.SearchTextStudioSetPartEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterStudioSetPartEQ = this.WhenAnyValue(x => x.RefreshStudioSetPartEQ)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMSynthToneCommonParameters = this.WhenAnyValue(x => x.SearchTextPCMSynthToneCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMSynthToneCommon = this.WhenAnyValue(x => x.RefreshPCMSynthToneCommon)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMSynthToneCommon2Parameters = this.WhenAnyValue(x => x.SearchTextPCMSynthToneCommon2)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMSynthToneCommon2 = this.WhenAnyValue(x => x.RefreshPCMSynthToneCommon2)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMSynthToneCommonMFXParameters = this.WhenAnyValue(x => x.SearchTextPCMSynthToneCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMSynthToneCommonMFX = this.WhenAnyValue(x => x.RefreshPCMSynthToneCommonMFX)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMSynthTonePMTParameters = this.WhenAnyValue(x => x.SearchTextPCMSynthTonePMT)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMSynthTonePMTParameters = this.WhenAnyValue(x => x.RefreshPCMSynthTonePMT)
                .Select(FilterProvider.ParameterFilter);

            var parFilterPCMDrumKitCommonParameters = this.WhenAnyValue(x => x.SearchTextPCMDrumKitCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMDrumKitCommon = this.WhenAnyValue(x => x.RefreshPCMDrumKitCommon)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMDrumKitCommon2Parameters = this.WhenAnyValue(x => x.SearchTextPCMDrumKitCommon2)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMDrumKitCommon2 = this.WhenAnyValue(x => x.RefreshPCMDrumKitCommon2)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMDrumKitCommonMFXParameters = this.WhenAnyValue(x => x.SearchTextPCMDrumKitCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMDrumKitCommonMFX = this.WhenAnyValue(x => x.RefreshPCMDrumKitCommonMFX)
                .Select(FilterProvider.ParameterFilter);
            var parFilterPCMDrumKitCompEQParameters = this.WhenAnyValue(x => x.SearchTextPCMDrumKitCompEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterPCMDrumKitCompEQParameters = this.WhenAnyValue(x => x.RefreshPCMDrumKitCompEQ)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNSynthToneCommonParameters = this.WhenAnyValue(x => x.SearchTextSNSynthToneCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNSynthToneCommonParameters = this.WhenAnyValue(x => x.RefreshSNSynthToneCommon)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNSynthToneCommonMFXParameters = this.WhenAnyValue(x => x.SearchTextSNSynthToneCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNSynthToneCommonMFXParameters = this.WhenAnyValue(x => x.RefreshSNSynthToneCommonMFX)
                .Select(FilterProvider.ParameterFilter);

            var parFilterSNAcousticToneCommonParameters = this.WhenAnyValue(x => x.SearchTextSNAcousticToneCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNAcousticToneCommonParameters = this.WhenAnyValue(x => x.RefreshSNAcousticToneCommon)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNAcousticToneCommonMFXParameters = this.WhenAnyValue(x => x.SearchTextSNAcousticToneCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNAcousticToneCommonMFXParameters = this
                .WhenAnyValue(x => x.RefreshSNAcousticToneCommonMFX)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNDrumKitCommonParameters = this.WhenAnyValue(x => x.SearchTextSNDrumKitCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNDrumKitCommon = this.WhenAnyValue(x => x.RefreshSNDrumKitCommon)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNDrumKitCommonMFXParameters = this.WhenAnyValue(x => x.SearchTextSNDrumKitCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNDrumKitCommonMFX = this.WhenAnyValue(x => x.RefreshSNDrumKitCommonMFX)
                .Select(FilterProvider.ParameterFilter);
            var parFilterSNDrumKitCompEQParameters = this.WhenAnyValue(x => x.SearchTextSNDrumKitCompEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);
            var refreshFilterSNDrumKitCompEQParameters = this.WhenAnyValue(x => x.RefreshSNDrumKitCompEQ)
                .Select(FilterProvider.ParameterFilter);


            _cleanupPresets = _sourceCachePresets.Connect()
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPreset)
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _presets,
                    SortExpressionComparer<Integra7Preset>.Ascending(t => t.Id))
                .DisposeMany()
                .Subscribe();
            _cleanupMidiParams = _sourceCacheStudioSetMidiParameters.Connect()
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetMidiParameters)
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetMidiParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupStudioSetPartParams = _sourceCacheStudioSetPartParameters.Connect()
                .Filter(refreshFilterStudioSetPart)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetPartParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetPartParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetPartParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetPartParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupStudioSetPartEQParams = _sourceCacheStudioSetPartEQParameters.Connect()
                .Filter(refreshFilterStudioSetPartEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetPartEQParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetPartEQParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetPartEQParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _StudioSetPartEQParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMSynthToneCommonParams = _sourceCachePCMSynthToneCommonParameters.Connect()
                .Filter(refreshFilterPCMSynthToneCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMSynthToneCommonParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMSynthToneCommonParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMSynthToneCommonParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMSynthToneCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMSynthToneCommon2Params = _sourceCachePCMSynthToneCommon2Parameters.Connect()
                .Filter(refreshFilterPCMSynthToneCommon2)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMSynthToneCommon2Parameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMSynthToneCommon2Parameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMSynthToneCommon2Parameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMSynthToneCommon2Parameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMSynthToneCommonMFXParams = _sourceCachePCMSynthToneCommonMFXParameters.Connect()
                .Filter(refreshFilterPCMSynthToneCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMSynthToneCommonMFXParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMSynthToneCommonMFXParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMSynthToneCommonMFXParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMSynthToneCommonMFXParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMSynthTonePMTParametersParams = _sourceCachePCMSynthTonePMTParameters.Connect()
                .Filter(refreshFilterPCMSynthTonePMTParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMSynthTonePMTParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMSynthTonePMTParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMSynthTonePMTParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMSynthTonePMTParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupPCMDrumKitCommonParams = _sourceCachePCMDrumKitCommonParameters.Connect()
                .Filter(refreshFilterPCMDrumKitCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMDrumKitCommonParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMDrumKitCommonParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMDrumKitCommonParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMDrumKitCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMDrumKitCommon2Params = _sourceCachePCMDrumKitCommon2Parameters.Connect()
                .Filter(refreshFilterPCMDrumKitCommon2)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMDrumKitCommon2Parameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMDrumKitCommon2Parameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMDrumKitCommon2Parameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMDrumKitCommon2Parameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMDrumKitCommonMFXParams = _sourceCachePCMDrumKitCommonMFXParameters.Connect()
                .Filter(refreshFilterPCMDrumKitCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMDrumKitCommonMFXParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMDrumKitCommonMFXParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMDrumKitCommonMFXParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMDrumKitCommonMFXParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupPCMDrumKitCompEQParametersParams = _sourceCachePCMDrumKitCompEQParameters.Connect()
                .Filter(refreshFilterPCMDrumKitCompEQParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterPCMDrumKitCompEQParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCachePCMDrumKitCompEQParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCachePCMDrumKitCompEQParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _PCMDrumKitCompEQParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNSynthToneCommonParams = _sourceCacheSNSynthToneCommonParameters.Connect()
                .Filter(refreshFilterSNSynthToneCommonParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNSynthToneCommonParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNSynthToneCommonParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNSynthToneCommonParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNSynthToneCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNSynthToneCommonMFXParams = _sourceCacheSNSynthToneCommonMFXParameters.Connect()
                .Filter(refreshFilterSNSynthToneCommonMFXParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNSynthToneCommonMFXParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNSynthToneCommonMFXParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNSynthToneCommonMFXParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNSynthToneCommonMFXParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNAcousticToneCommonParams = _sourceCacheSNAcousticToneCommonParameters.Connect()
                .Filter(refreshFilterSNAcousticToneCommonParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNAcousticToneCommonParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNAcousticToneCommonParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNAcousticToneCommonParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNAcousticToneCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNAcousticToneCommonMFXParams = _sourceCacheSNAcousticToneCommonMFXParameters.Connect()
                .Filter(refreshFilterSNAcousticToneCommonMFXParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNAcousticToneCommonMFXParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNAcousticToneCommonMFXParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNAcousticToneCommonMFXParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNAcousticToneCommonMFXParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupSNDrumKitCommonParams = _sourceCacheSNDrumKitCommonParameters.Connect()
                .Filter(refreshFilterSNDrumKitCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNDrumKitCommonParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNDrumKitCommonParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNDrumKitCommonParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNDrumKitCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNDrumKitCommonMFXParams = _sourceCacheSNDrumKitCommonMFXParameters.Connect()
                .Filter(refreshFilterSNDrumKitCommonMFX)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNDrumKitCommonMFXParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNDrumKitCommonMFXParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNDrumKitCommonMFXParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNDrumKitCommonMFXParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
            _cleanupSNDrumKitCompEQParametersParams = _sourceCacheSNDrumKitCompEQParameters.Connect()
                .Filter(refreshFilterSNDrumKitCompEQParameters)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterSNDrumKitCompEQParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheSNDrumKitCompEQParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheSNDrumKitCompEQParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _SNDrumKitCompEQParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
        }
        else
        {
            var parFilterSetup = this.WhenAnyValue(x => x.SearchTextSetup)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var parFilterSystem = this.WhenAnyValue(x => x.SearchSystem)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var parFilterStudioSetCommon = this.WhenAnyValue(x => x.SearchTextStudioSetCommon)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var parFilterStudioSetCommonChorus = this.WhenAnyValue(x => x.SearchTextStudioSetCommonChorus)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var refreshCommonChorus = this.WhenAnyValue(x => x.RefreshCommonChorusNeeded)
                .Select(FilterProvider.ParameterFilter);

            var parFilterStudioSetCommonReverb = this.WhenAnyValue(x => x.SearchTextStudioSetCommonReverb)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var refreshCommonReverb = this.WhenAnyValue(x => x.RefreshCommonReverbNeeded)
                .Select(FilterProvider.ParameterFilter);

            var parFilterStudioSetCommonMotionalSurroundParameters = this
                .WhenAnyValue(x => x.SearchTextStudioSetCommonMotionalSurround)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var refreshCommonMotionalSurround = this.WhenAnyValue(x => x.RefreshCommonMotionalSurroundNeeded)
                .Select(FilterProvider.ParameterFilter);

            var parFilterStudioSetCommonMasterEQParameters = this.WhenAnyValue(x => x.SearchTextStudioSetCommonMasterEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .DistinctUntilChanged()
                .Select(FilterProvider.ParameterFilter);

            var refreshCommonMasterEQ = this.WhenAnyValue(x => x.RefreshCommonMasterEQNeeded)
                .Select(FilterProvider.ParameterFilter);

            _cleanupSetup = _sourceCacheSetupParameters.Connect()
                .Filter(parFilterSetup)
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _setupParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupSystem = _sourceCacheSystem.Connect()
                .Filter(parFilterSystem)
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _systemParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupStudioSetCommon = _sourceCacheStudioSetCommonParameters.Connect()
                .Filter(parFilterStudioSetCommon)
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetCommonParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupStudioSetChorus = _sourceCacheStudioSetCommonChorusParameters.Connect()
                .Filter(refreshCommonChorus)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetCommonChorus)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetCommonChorusParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetCommonChorusParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetCommonChorusParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupStudioSetReverb = _sourceCacheStudioSetCommonReverbParameters.Connect()
                .Filter(refreshCommonReverb)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetCommonReverb)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetCommonReverbParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetCommonReverbParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetCommonReverbParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupMotionalSurround = _sourceCacheStudioSetCommonMotionalSurroundParameters.Connect()
                .Filter(refreshCommonMotionalSurround)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetCommonMotionalSurroundParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetCommonMotionalSurroundParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetCommonMotionalSurroundParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetCommonMotionalSurroundParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();

            _cleanupStudioSetMasterEQ = _sourceCacheStudioSetCommonMasterEQParameters.Connect()
                .Filter(refreshCommonMasterEQ)
                .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
                .Filter(parFilterStudioSetCommonMasterEQParameters)
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl != "" && par.ParSpec.ParentCtrl is string parentId
                        ? _sourceCacheStudioSetCommonMasterEQParameters
                            .Watch(parentId)
                            .Select(parentChange => parentChange.Current.StringValue == par.ParSpec.ParentCtrlDispValue)
                        : Observable.Return(true))
                .FilterOnObservable(par =>
                    par.ParSpec.ParentCtrl2 != "" && par.ParSpec.ParentCtrl2 is string parentId2
                        ? _sourceCacheStudioSetCommonMasterEQParameters
                            .Watch(parentId2)
                            .Select(parentChange2 =>
                                parentChange2.Current.StringValue == par.ParSpec.ParentCtrlDispValue2)
                        : Observable.Return(true))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SortAndBind(
                    out _studioSetCommonMasterEQParameters,
                    SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                        ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
                .DisposeMany()
                .Subscribe();
        }
    }

    public Integra7Domain I7Domain
    {
        get => _i7domain;
        set
        {
            _i7domain = value;
            UpdatePartialViewModelDomains(value);
        }
    }

    public ReadOnlyObservableCollection<Integra7Preset> Presets => _presets;
    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetMidiParameters => _studioSetMidiParameters;
    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetPartParameters => _studioSetPartParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetPartEQParameters =>
        _StudioSetPartEQParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMSynthToneCommonParameters =>
        _PCMSynthToneCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMSynthToneCommon2Parameters =>
        _PCMSynthToneCommon2Parameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMSynthToneCommonMFXParameters =>
        _PCMSynthToneCommonMFXParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMSynthTonePMTParameters =>
        _PCMSynthTonePMTParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMDrumKitCommonParameters =>
        _PCMDrumKitCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMDrumKitCommon2Parameters =>
        _PCMDrumKitCommon2Parameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMDrumKitCommonMFXParameters =>
        _PCMDrumKitCommonMFXParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMDrumKitCompEQParameters =>
        _PCMDrumKitCompEQParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNSynthToneCommonParameters =>
        _SNSynthToneCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNSynthToneCommonMFXParameters =>
        _SNSynthToneCommonMFXParameters;

    public ReadOnlyObservableCollection<PartialViewModel>? PcmSynthTonePartialViewModels { get; private set; }

    public ReadOnlyObservableCollection<PartialViewModel>? PcmDrumKitPartialViewModels { get; private set; }

    public ReadOnlyObservableCollection<PartialViewModel>? SNSynthTonePartialViewModels { get; private set; }

    public ReadOnlyObservableCollection<PartialViewModel>? SNDrumKitPartialViewModels { get; private set; }

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNAcousticToneCommonParameters =>
        _SNAcousticToneCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNAcousticToneCommonMFXParameters =>
        _SNAcousticToneCommonMFXParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNDrumKitCommonParameters =>
        _SNDrumKitCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNDrumKitCommonMFXParameters =>
        _SNDrumKitCommonMFXParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNDrumKitCompEQParameters =>
        _SNDrumKitCompEQParameters;

    public byte PartNo { get; }

    public bool SelectedPresetIsPCMSynthTone => _selectedPreset is null ? false : _selectedPreset.ToneTypeStr == "PCMS";
    public bool SelectedPresetIsPCMDrumKit => _selectedPreset is null ? false : _selectedPreset.ToneTypeStr == "PCMD";
    public bool SelectedPresetIsSNSynthTone => _selectedPreset is null ? false : _selectedPreset.ToneTypeStr == "SN-S";

    public bool SelectedPresetIsSNAcousticTone =>
        _selectedPreset is null ? false : _selectedPreset.ToneTypeStr == "SN-A";

    public bool SelectedPresetIsSNDrumKit => _selectedPreset is null ? false : _selectedPreset.ToneTypeStr == "SN-D";

    public string Header => IsCommonTab ? "Common" : $"Part {PartNo + 1}";
    public ReadOnlyObservableCollection<FullyQualifiedParameter> SetupParameters => _setupParameters;
    public ReadOnlyObservableCollection<FullyQualifiedParameter> SystemParameters => _systemParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetCommonParameters =>
        _studioSetCommonParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetCommonChorusParameters =>
        _studioSetCommonChorusParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetCommonReverbParameters =>
        _studioSetCommonReverbParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetCommonMotionalSurroundParameters =>
        _studioSetCommonMotionalSurroundParameters;

    public ReadOnlyObservableCollection<FullyQualifiedParameter> StudioSetCommonMasterEQParameters =>
        _studioSetCommonMasterEQParameters;

    public bool IsCommonTab { get; }

    public bool IsPartTab => !IsCommonTab;

    public Integra7Preset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (_selectedPreset != value && value is not null)
            {
                _selectedPreset = value;
                ChangePresetAsync();
                this.RaisePropertyChanged();
            }
        }
    }

    public async Task EnsurePreselectIsNotNullAsync()
    {
        if (_selectedPreset is null && PartNo != 255)
        {
            await _i7domain.StudioSetPart(PartNo).ReadFromIntegraAsync();
            PreSelectConfiguredPreset(_i7domain.StudioSetPart(PartNo));
        }
    }

    public void PreSelectConfiguredPreset(DomainBase b)
    {
        var msbstr = b.LookupSingleParameterDisplayedValue("Studio Set Part/Tone Bank Select MSB");
        var lsbstr = b.LookupSingleParameterDisplayedValue("Studio Set Part/Tone Bank Select LSB");
        var pcstr = b.LookupSingleParameterDisplayedValue("Studio Set Part/Tone Bank Program Number (PC)");
        foreach (var p in _i7presets)
            if (msbstr == $"{p.Msb}" && lsbstr == $"{p.Lsb}" &&
                pcstr == $"{p.Pc - 1}") // note: seems like integra-7 sends back a one-based program change (PC)??
            {
                UpdatePartialViewModelToneTypeStrings(p);
                SelectedPreset = p;
                return;
            }
    }

    private void UpdatePartialViewModelDomains(Integra7Domain value)
    {
        if (PcmSynthTonePartialViewModels != null)
            foreach (var pvm in PcmSynthTonePartialViewModels)
                pvm.I7Domain = value;

        if (PcmDrumKitPartialViewModels != null)
            foreach (var pvm in PcmDrumKitPartialViewModels)
                pvm.I7Domain = value;

        if (SNSynthTonePartialViewModels != null)
            foreach (var pvm in SNSynthTonePartialViewModels)
                pvm.I7Domain = value;

        if (SNDrumKitPartialViewModels != null)
            foreach (var pvm in SNDrumKitPartialViewModels)
                pvm.I7Domain = value;
    }

    private void UpdatePartialViewModelToneTypeStrings(Integra7Preset p)
    {
        if (PcmSynthTonePartialViewModels != null)
            foreach (var pvm in PcmSynthTonePartialViewModels)
                pvm.UpdateToneTypeString(p.ToneTypeStr);

        if (PcmDrumKitPartialViewModels != null)
            foreach (var pvm in PcmDrumKitPartialViewModels)
                pvm.UpdateToneTypeString(p.ToneTypeStr);

        if (SNSynthTonePartialViewModels != null)
            foreach (var pvm in SNSynthTonePartialViewModels)
                pvm.UpdateToneTypeString(p.ToneTypeStr);

        if (SNDrumKitPartialViewModels != null)
            foreach (var pvm in SNDrumKitPartialViewModels)
                pvm.UpdateToneTypeString(p.ToneTypeStr);
    }

    public void ForceUiRefresh(string StartAddressName, string OffsetAddressName, string Offset2AddressName,
        string ParPath, bool ResyncNeeded)
    {
        if (!ResyncNeeded)
        {
            if (IsCommonTab)
            {
                //Log.Debug("UI Refresh Requested for Common Tab");
                if (Offset2AddressName == "Offset2/Studio Set Common Chorus")
                {
                    // force re-evaluation of the dynamic data filters after the parameters were read from integra-7
                    // this feels like a very ugly hack, but i currently do not know how to do it properly
                    // i tried tons of other stuff (like: "this.RaisePropertyChanged(nameof(RefreshCommonChorusNeeded))"), but nothing seems to work
                    // ... shiver ...
                    RefreshCommonChorusNeeded = "."; // RefreshCommonChorusNeeded must not have any .Throttle clauses
                    RefreshCommonChorusNeeded = SearchTextStudioSetCommonChorus;
                }
                else if (Offset2AddressName == "Offset2/Studio Set Common Reverb")
                {
                    RefreshCommonReverbNeeded = ".";
                    RefreshCommonReverbNeeded = SearchTextStudioSetCommonReverb;
                }
            }
            else if (IsPartTab)
            {
                //Log.Debug($"UI Refresh Requested for Part {_part + 1} Tab");
                if (Offset2AddressName == $"Offset2/Studio Set Part {PartNo + 1}")
                {
                    RefreshStudioSetPart = ".";
                    RefreshStudioSetPart = SearchTextStudioSetPart;
                }
                else if (Offset2AddressName == $"Offset2/Studio Set Part EQ {PartNo + 1}")
                {
                    RefreshStudioSetPart = ".";
                    RefreshStudioSetPart = SearchTextStudioSetPart;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Synth Tone Common")
                {
                    RefreshPCMSynthToneCommon = ".";
                    RefreshPCMSynthToneCommon = SearchTextPCMSynthToneCommon;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Synth Tone Common 2")
                {
                    RefreshPCMSynthToneCommon2 = ".";
                    RefreshPCMSynthToneCommon2 = SearchTextPCMSynthToneCommon2;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Synth Tone Common MFX")
                {
                    RefreshPCMSynthToneCommonMFX = ".";
                    RefreshPCMSynthToneCommonMFX = SearchTextPCMSynthToneCommonMFX;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Synth Tone Partial Mix Table")
                {
                    RefreshPCMSynthTonePMT = ".";
                    RefreshPCMSynthTonePMT = SearchTextPCMSynthTonePMT;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Drum Kit Common")
                {
                    RefreshPCMDrumKitCommon = ".";
                    RefreshPCMDrumKitCommon = SearchTextPCMDrumKitCommon;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Drum Kit Common 2")
                {
                    RefreshPCMDrumKitCommon2 = ".";
                    RefreshPCMDrumKitCommon2 = SearchTextPCMDrumKitCommon2;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Drum Kit Common MFX")
                {
                    RefreshPCMDrumKitCommonMFX = ".";
                    RefreshPCMDrumKitCommonMFX = SearchTextPCMDrumKitCommonMFX;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/PCM Drum Kit Common Comp-EQ")
                {
                    RefreshPCMDrumKitCompEQ = ".";
                    RefreshPCMDrumKitCompEQ = SearchTextPCMDrumKitCompEQ;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Synth Tone Common")
                {
                    RefreshSNSynthToneCommon = ".";
                    RefreshSNSynthToneCommon = SearchTextSNSynthToneCommon;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Synth Tone Common MFX")
                {
                    RefreshSNSynthToneCommonMFX = ".";
                    RefreshSNSynthToneCommonMFX = SearchTextSNSynthToneCommonMFX;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Acoustic Tone Common")
                {
                    RefreshSNAcousticToneCommon = ".";
                    RefreshSNAcousticToneCommon = SearchTextSNAcousticToneCommon;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Acoustic Tone Common MFX")
                {
                    RefreshSNAcousticToneCommonMFX = ".";
                    RefreshSNAcousticToneCommonMFX = SearchTextSNAcousticToneCommonMFX;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Drum Kit Common")
                {
                    RefreshSNDrumKitCommon = ".";
                    RefreshSNDrumKitCommon = SearchTextSNDrumKitCommon;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Drum Kit Common MFX")
                {
                    RefreshSNDrumKitCommonMFX = ".";
                    RefreshSNDrumKitCommonMFX = SearchTextSNDrumKitCommonMFX;
                }
                else if (StartAddressName == $"Temporary Tone Part {PartNo + 1}" &&
                         Offset2AddressName == "Offset2/SuperNATURAL Drum Kit Common Comp-EQ")
                {
                    RefreshSNDrumKitCompEQ = ".";
                    RefreshSNDrumKitCompEQ = SearchTextSNDrumKitCompEQ;
                }

                if ((IsPartTab && ParPath.Contains("Tone Bank Select")) || ParPath.Contains("Tone Bank Program Number"))
                    if (Offset2AddressName == $"Offset2/Studio Set Part {PartNo + 1}")
                        // using MessageBus instead of direct call because it is automatically throttled
                        MessageBus.Current.SendMessage(new UpdateSetPresetAndResyncPart(PartNo));
            }

            if (PcmSynthTonePartialViewModels != null && _selectedPreset?.ToneTypeStr == "PCMS")
                foreach (var pvm in PcmSynthTonePartialViewModels)
                    pvm.ForceUiRefresh(StartAddressName, OffsetAddressName, Offset2AddressName, ParPath, ResyncNeeded);

            if (PcmDrumKitPartialViewModels != null && _selectedPreset?.ToneTypeStr == "PCMD")
                foreach (var pvm in PcmDrumKitPartialViewModels)
                    pvm.ForceUiRefresh(StartAddressName, OffsetAddressName, Offset2AddressName, ParPath, ResyncNeeded);

            if (SNSynthTonePartialViewModels != null && _selectedPreset?.ToneTypeStr == "SN-S")
                foreach (var pvm in SNSynthTonePartialViewModels)
                    pvm.ForceUiRefresh(StartAddressName, OffsetAddressName, Offset2AddressName, ParPath, ResyncNeeded);

            if (SNDrumKitPartialViewModels != null && _selectedPreset?.ToneTypeStr == "SN-D")
                foreach (var pvm in SNDrumKitPartialViewModels)
                    pvm.ForceUiRefresh(StartAddressName, OffsetAddressName, Offset2AddressName, ParPath, ResyncNeeded);
        }
        else
        {
            if (IsPartTab && (OffsetAddressName.Contains($"Part {PartNo + 1}") ||
                              StartAddressName.Contains($"Part {PartNo + 1}")))
                MessageBus.Current.SendMessage(new UpdateResyncPart(PartNo));
            else if (IsCommonTab) MessageBus.Current.SendMessage(new UpdateResyncPart(PartNo));
        }
    }

    public async Task InitializeParameterSourceCachesAsync()
    {
        if (_i7domain is null)
            return;

        if (!IsCommonTab)
        {
            await _i7domain.StudioSetMidi(PartNo).ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_mid = _i7domain.StudioSetMidi(PartNo).GetRelevantParameters(true, true);
            _sourceCacheStudioSetMidiParameters.AddOrUpdate(p_mid);

            await _i7domain.StudioSetPart(PartNo).ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_part = _i7domain.StudioSetPart(PartNo).GetRelevantParameters(true, true);
            _sourceCacheStudioSetPartParameters.AddOrUpdate(p_part);
            PreSelectConfiguredPreset(_i7domain.StudioSetPart(PartNo));

            await _i7domain.StudioSetPartEQ(PartNo).ReadFromIntegraAsync();
            List<FullyQualifiedParameter>
                p_parteq = _i7domain.StudioSetPartEQ(PartNo).GetRelevantParameters(true, true);
            _sourceCacheStudioSetPartEQParameters.AddOrUpdate(p_parteq);

            if (_selectedPreset?.ToneTypeStr == "PCMS")
            {
                await _i7domain.PCMSynthToneCommon(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMSynthToneCommon2(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMSynthToneCommonMFX(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMSynthTonePMT(PartNo).ReadFromIntegraAsync();
            }
            else if (_selectedPreset?.ToneTypeStr == "PCMD")
            {
                await _i7domain.PCMDrumKitCommon(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMDrumKitCommon2(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMDrumKitCommonMFX(PartNo).ReadFromIntegraAsync();
                await _i7domain.PCMDrumKitCompEQ(PartNo).ReadFromIntegraAsync();
            }
            else if (_selectedPreset?.ToneTypeStr == "SN-S")
            {
                await _i7domain.SNSynthToneCommon(PartNo).ReadFromIntegraAsync();
                await _i7domain.SNSynthToneCommonMFX(PartNo).ReadFromIntegraAsync();
            }
            else if (_selectedPreset?.ToneTypeStr == "SN-A")
            {
                await _i7domain.SNAcousticToneCommon(PartNo).ReadFromIntegraAsync();
                await _i7domain.SNAcousticToneCommonMFX(PartNo).ReadFromIntegraAsync();
            }
            else if (_selectedPreset?.ToneTypeStr == "SN-D")
            {
                await _i7domain.SNDrumKitCommon(PartNo).ReadFromIntegraAsync();
                await _i7domain.SNDrumKitCommonMFX(PartNo).ReadFromIntegraAsync();
                await _i7domain.SNDrumKitCompEQ(PartNo).ReadFromIntegraAsync();
            }

            ObservableCollection<PartialViewModel> pvm = [];
            for (byte i = 0; i < Constants.NO_OF_PARTIALS_PCM_SYNTH_TONE; i++)
            {
                var vm = new PCMSynthTonePartialViewModel(this, PartNo, i,
                    _selectedPreset?.ToneTypeStr,
                    _i7startAddresses, _i7parameters, _i7Api,
                    _i7domain, _semaphore);
                await vm.InitializeParameterSourceCachesAsync();
                pvm.Add(vm);
            }

            PcmSynthTonePartialViewModels = new ReadOnlyObservableCollection<PartialViewModel>(pvm);
            foreach (var p in PcmSynthTonePartialViewModels) await p.InitializeParameterSourceCachesAsync();

            ObservableCollection<PartialViewModel> pvm2 = [];
            for (byte i = 0; i < Constants.NO_OF_PARTIALS_PCM_DRUM; i++)
            {
                var vm = new PCMDrumKitPartialViewModel(this, PartNo, i,
                    _selectedPreset?.ToneTypeStr,
                    _i7startAddresses, _i7parameters, _i7Api,
                    _i7domain, _semaphore);
                await vm.InitializeParameterSourceCachesAsync();
                pvm2.Add(vm);
            }

            PcmDrumKitPartialViewModels = new ReadOnlyObservableCollection<PartialViewModel>(pvm2);
            foreach (var p in PcmDrumKitPartialViewModels) await p.InitializeParameterSourceCachesAsync();

            ObservableCollection<PartialViewModel> pvm3 = [];
            for (byte i = 0; i < Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; i++)
            {
                var vm = new SNSynthTonePartialViewModel(this, PartNo, i,
                    _selectedPreset?.ToneTypeStr,
                    _i7startAddresses, _i7parameters, _i7Api,
                    _i7domain, _semaphore);
                await vm.InitializeParameterSourceCachesAsync();
                pvm3.Add(vm);
            }

            SNSynthTonePartialViewModels = new ReadOnlyObservableCollection<PartialViewModel>(pvm3);
            foreach (var p in SNSynthTonePartialViewModels) await p.InitializeParameterSourceCachesAsync();

            ObservableCollection<PartialViewModel> pvm4 = [];
            for (byte i = 0; i < Constants.NO_OF_PARTIALS_SN_DRUM; i++)
            {
                var vm = new SNDrumKitPartialViewModel(this, PartNo, i,
                    _selectedPreset?.ToneTypeStr,
                    _i7startAddresses, _i7parameters, _i7Api,
                    _i7domain, _semaphore);
                await vm.InitializeParameterSourceCachesAsync();
                pvm4.Add(vm);
            }

            SNDrumKitPartialViewModels = new ReadOnlyObservableCollection<PartialViewModel>(pvm4);
            foreach (var p in SNDrumKitPartialViewModels) await p.InitializeParameterSourceCachesAsync();

            List<FullyQualifiedParameter> p_pcmstc =
                _i7domain.PCMSynthToneCommon(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMSynthToneCommonParameters.AddOrUpdate(p_pcmstc);
            List<FullyQualifiedParameter> p_pcmstc2 =
                _i7domain.PCMSynthToneCommon2(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMSynthToneCommon2Parameters.AddOrUpdate(p_pcmstc2);
            List<FullyQualifiedParameter> p_pcmmfx =
                _i7domain.PCMSynthToneCommonMFX(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMSynthToneCommonMFXParameters.AddOrUpdate(p_pcmmfx);
            List<FullyQualifiedParameter>
                p_pcmpmt = _i7domain.PCMSynthTonePMT(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMSynthTonePMTParameters.AddOrUpdate(p_pcmpmt);

            List<FullyQualifiedParameter>
                p_pcmdkc = _i7domain.PCMDrumKitCommon(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMDrumKitCommonParameters.AddOrUpdate(p_pcmdkc);
            List<FullyQualifiedParameter> p_pcmdkc2 =
                _i7domain.PCMDrumKitCommon2(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMDrumKitCommon2Parameters.AddOrUpdate(p_pcmdkc2);
            List<FullyQualifiedParameter> p_pcmdkmfx =
                _i7domain.PCMDrumKitCommonMFX(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMDrumKitCommonMFXParameters.AddOrUpdate(p_pcmdkmfx);
            List<FullyQualifiedParameter> p_pcmcompeq =
                _i7domain.PCMDrumKitCompEQ(PartNo).GetRelevantParameters(true, true);
            _sourceCachePCMDrumKitCompEQParameters.AddOrUpdate(p_pcmcompeq);

            List<FullyQualifiedParameter>
                p_snstc = _i7domain.SNSynthToneCommon(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNSynthToneCommonParameters.AddOrUpdate(p_snstc);
            List<FullyQualifiedParameter> p_snmfx =
                _i7domain.SNSynthToneCommonMFX(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNSynthToneCommonMFXParameters.AddOrUpdate(p_snmfx);

            List<FullyQualifiedParameter> p_snatc =
                _i7domain.SNAcousticToneCommon(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNAcousticToneCommonParameters.AddOrUpdate(p_snatc);
            List<FullyQualifiedParameter> p_snamfx =
                _i7domain.SNAcousticToneCommonMFX(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNAcousticToneCommonMFXParameters.AddOrUpdate(p_snamfx);

            List<FullyQualifiedParameter> p_sndkc = _i7domain.SNDrumKitCommon(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNDrumKitCommonParameters.AddOrUpdate(p_sndkc);
            List<FullyQualifiedParameter> p_sndkmfx =
                _i7domain.SNDrumKitCommonMFX(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNDrumKitCommonMFXParameters.AddOrUpdate(p_sndkmfx);
            List<FullyQualifiedParameter> p_sncompeq =
                _i7domain.SNDrumKitCompEQ(PartNo).GetRelevantParameters(true, true);
            _sourceCacheSNDrumKitCompEQParameters.AddOrUpdate(p_sncompeq);
        }
        else
        {
            await _i7domain?.Setup.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_s = _i7domain?.Setup.GetRelevantParameters();
            _sourceCacheSetupParameters.AddOrUpdate(p_s);

            await _i7domain?.System.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> s_s = _i7domain?.System.GetRelevantParameters();
            _sourceCacheSystem.AddOrUpdate(s_s);

            await _i7domain?.StudioSetCommon.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_ssc = _i7domain?.StudioSetCommon.GetRelevantParameters();
            _sourceCacheStudioSetCommonParameters.AddOrUpdate(p_ssc);

            await _i7domain?.StudioSetCommonChorus.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_sscc = _i7domain?.StudioSetCommonChorus.GetRelevantParameters(true, true);
            _sourceCacheStudioSetCommonChorusParameters.AddOrUpdate(p_sscc);

            await _i7domain?.StudioSetCommonReverb.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_sscr = _i7domain?.StudioSetCommonReverb.GetRelevantParameters(true, true);
            _sourceCacheStudioSetCommonReverbParameters.AddOrUpdate(p_sscr);

            await _i7domain?.StudioSetCommonMotionalSurround.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_ssms =
                _i7domain?.StudioSetCommonMotionalSurround.GetRelevantParameters(true, true);
            _sourceCacheStudioSetCommonMotionalSurroundParameters.AddOrUpdate(p_ssms);

            await _i7domain?.StudioSetCommonMasterEQ.ReadFromIntegraAsync();
            List<FullyQualifiedParameter> p_meq = _i7domain?.StudioSetCommonMasterEQ.GetRelevantParameters(true, true);
            _sourceCacheStudioSetCommonMasterEQParameters.AddOrUpdate(p_meq);
        }
    }

    [ReactiveCommand]
    public async Task ChangePresetAsync()
    {
        var CurrentSelection = _selectedPreset;
        if (CurrentSelection != null)
            await _i7Api.ChangePresetAsync(PartNo, CurrentSelection.Msb, CurrentSelection.Lsb, CurrentSelection.Pc);
    }

    public async Task ResyncPartAsync(byte part)
    {
        if (_i7domain is null)
            return;

        if (part != PartNo)
            return;

        if (IsCommonTab)
        {
            var setup = _i7domain?.Setup;
            await setup?.ReadFromIntegraAsync();
            ForceUiRefresh(setup.StartAddressName, setup.OffsetAddressName, setup.Offset2AddressName, "",
                false /* don't cause inf loop */);

            var system = _i7domain?.System;
            await system?.ReadFromIntegraAsync();
            ForceUiRefresh(system.StartAddressName, system.OffsetAddressName, system.Offset2AddressName, "", false);

            var setcom = _i7domain?.StudioSetCommon;
            await setcom?.ReadFromIntegraAsync();
            ForceUiRefresh(setcom.StartAddressName, setcom.OffsetAddressName, setcom.Offset2AddressName, "", false);

            var setchor = _i7domain?.StudioSetCommonChorus;
            await setchor.ReadFromIntegraAsync();
            ForceUiRefresh(setchor.StartAddressName, setchor.OffsetAddressName, setchor.Offset2AddressName, "", false);

            var setrev = _i7domain?.StudioSetCommonReverb;
            await setrev.ReadFromIntegraAsync();
            ForceUiRefresh(setrev.StartAddressName, setrev.OffsetAddressName, setrev.Offset2AddressName, "", false);

            var setsur = _i7domain?.StudioSetCommonMotionalSurround;
            await setsur.ReadFromIntegraAsync();
            ForceUiRefresh(setsur.StartAddressName, setsur.OffsetAddressName, setsur.Offset2AddressName, "", false);

            var seteq = _i7domain?.StudioSetCommonMasterEQ;
            await seteq.ReadFromIntegraAsync();
            ForceUiRefresh(seteq.StartAddressName, seteq.OffsetAddressName, seteq.Offset2AddressName, "", false);
        }
        else
        {
            var midiPart = _i7domain?.StudioSetMidi(part);
            await midiPart.ReadFromIntegraAsync();
            ForceUiRefresh(midiPart.StartAddressName, midiPart.OffsetAddressName, midiPart.Offset2AddressName, "",
                false /* don't cause inf loop */);
            var setPart = _i7domain?.StudioSetPart(part);
            await setPart.ReadFromIntegraAsync();
            PreSelectConfiguredPreset(setPart);
            ForceUiRefresh(setPart.StartAddressName, setPart.OffsetAddressName, setPart.Offset2AddressName, "",
                false /* don't cause inf loop */);
            if (_selectedPreset.ToneTypeStr == "PCMS")
            {
                var setPCMSTone = _i7domain?.PCMSynthToneCommon(part);
                await setPCMSTone.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMSTone.StartAddressName, setPCMSTone.OffsetAddressName,
                    setPCMSTone.Offset2AddressName, "", false /* don't cause inf loop */);
                var setPCMSTone2 = _i7domain?.PCMSynthToneCommon2(part);
                await setPCMSTone2.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMSTone2.StartAddressName, setPCMSTone2.OffsetAddressName,
                    setPCMSTone2.Offset2AddressName, "", false /* don't cause inf loop */);
                var setPCMSToneMFX = _i7domain?.PCMSynthToneCommonMFX(part);
                await setPCMSToneMFX.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMSToneMFX.StartAddressName, setPCMSToneMFX.OffsetAddressName,
                    setPCMSToneMFX.Offset2AddressName, "", false /* don't cause inf loop */);
                var setPCMSTonePMT = _i7domain?.PCMSynthTonePMT(part);
                await setPCMSTonePMT.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMSTonePMT.StartAddressName, setPCMSTonePMT.OffsetAddressName,
                    setPCMSTonePMT.Offset2AddressName, "", false /* don't cause inf loop */);
                foreach (var p in PcmSynthTonePartialViewModels) await p.ResyncPartAsync(part);
            }
            else if (_selectedPreset.ToneTypeStr == "PCMD")
            {
                var setPCMDKit = _i7domain?.PCMDrumKitCommon(part);
                await setPCMDKit.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMDKit.StartAddressName, setPCMDKit.OffsetAddressName, setPCMDKit.Offset2AddressName,
                    "", false);
                var setPCMDKit2 = _i7domain?.PCMDrumKitCommon2(part);
                await setPCMDKit2.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMDKit2.StartAddressName, setPCMDKit2.OffsetAddressName,
                    setPCMDKit2.Offset2AddressName, "", false);
                var setPCMDMfx = _i7domain?.PCMDrumKitCommonMFX(part);
                await setPCMDMfx.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMDMfx.StartAddressName, setPCMDMfx.OffsetAddressName, setPCMDMfx.Offset2AddressName,
                    "", false);
                var setPCMDCompeq = _i7domain?.PCMDrumKitCompEQ(part);
                await setPCMDCompeq.ReadFromIntegraAsync();
                ForceUiRefresh(setPCMDCompeq.StartAddressName, setPCMDCompeq.OffsetAddressName,
                    setPCMDCompeq.Offset2AddressName, "", false);
                foreach (var p in PcmDrumKitPartialViewModels) await p.ResyncPartAsync(part);
            }
            else if (_selectedPreset.ToneTypeStr == "SN-S")
            {
                var setSNS = _i7domain?.SNSynthToneCommon(part);
                await setSNS.ReadFromIntegraAsync();
                ForceUiRefresh(setSNS.StartAddressName, setSNS.OffsetAddressName, setSNS.Offset2AddressName, "", false);
                var setSNSMFX = _i7domain?.SNSynthToneCommonMFX(part);
                await setSNSMFX.ReadFromIntegraAsync();
                ForceUiRefresh(setSNSMFX.StartAddressName, setSNSMFX.OffsetAddressName, setSNSMFX.Offset2AddressName,
                    "", false);
                foreach (var p in SNSynthTonePartialViewModels) await p.ResyncPartAsync(part);
            }
            else if (_selectedPreset.ToneTypeStr == "SN-A")
            {
                var setSNA = _i7domain?.SNAcousticToneCommon(part);
                await setSNA.ReadFromIntegraAsync();
                ForceUiRefresh(setSNA.StartAddressName, setSNA.OffsetAddressName, setSNA.Offset2AddressName, "", false);
                var setSNAMFX = _i7domain?.SNAcousticToneCommonMFX(part);
                await setSNAMFX.ReadFromIntegraAsync();
                ForceUiRefresh(setSNAMFX.StartAddressName, setSNAMFX.OffsetAddressName, setSNAMFX.Offset2AddressName,
                    "", false);
            }
            else if (_selectedPreset.ToneTypeStr == "SN-D")
            {
                var setSNDKit = _i7domain?.SNDrumKitCommon(part);
                await setSNDKit.ReadFromIntegraAsync();
                ForceUiRefresh(setSNDKit.StartAddressName, setSNDKit.OffsetAddressName, setSNDKit.Offset2AddressName,
                    "", false);
                var setSNDMfx = _i7domain?.SNDrumKitCommonMFX(part);
                await setSNDMfx.ReadFromIntegraAsync();
                ForceUiRefresh(setSNDMfx.StartAddressName, setSNDMfx.OffsetAddressName, setSNDMfx.Offset2AddressName,
                    "", false);
                var setSNDCompeq = _i7domain?.SNDrumKitCompEQ(part);
                await setSNDCompeq.ReadFromIntegraAsync();
                ForceUiRefresh(setSNDCompeq.StartAddressName, setSNDCompeq.OffsetAddressName,
                    setSNDCompeq.Offset2AddressName, "", false);
                foreach (var p in SNDrumKitPartialViewModels) await p.ResyncPartAsync(part);
            }

            PreSelectConfiguredPreset(setPart);
            this.RaisePropertyChanged(nameof(SelectedPresetIsPCMSynthTone));
            this.RaisePropertyChanged(nameof(SelectedPresetIsPCMDrumKit));
            this.RaisePropertyChanged(nameof(SelectedPresetIsSNSynthTone));
            this.RaisePropertyChanged(nameof(SelectedPresetIsSNAcousticTone));
            this.RaisePropertyChanged(nameof(SelectedPresetIsSNDrumKit));
        }
    }
}