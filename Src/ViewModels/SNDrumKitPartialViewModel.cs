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

public sealed partial class SNDrumKitPartialViewModel : PartialViewModel
{
    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNDrumKitPartialParameters = new([]);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNDrumKitPartialParameters =
        new(x => x.ParSpec.Path);

    private IDisposable? _cleanupSNDrumKitPartialParameters;
    [Reactive] private string _refreshSNDrumKitPartial = "";

    [Reactive] private string _searchTextSNDrumKitPartial = "";

    public SNDrumKitPartialViewModel(PartViewModel parent, byte zeroBasedPart, byte zeroBasedPartial,
        string toneTypeStr, Integra7StartAddresses i7addr,
        Integra7Parameters par, IIntegra7Api i7api, Integra7Domain i7dom, SemaphoreSlim semaphore) :
        base(parent, zeroBasedPart, zeroBasedPartial, toneTypeStr, i7addr, par, i7api, i7dom, semaphore)
    {
        var parFilterSNDrumKitPartialParameters = this.WhenAnyValue(x => x.SearchTextSNDrumKitPartial)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .DistinctUntilChanged()
            .Select(FilterProvider.ParameterFilter);
        var refreshFilterSNDrumKitPartialParameters = this.WhenAnyValue(x => x.RefreshSNDrumKitPartial)
            .Select(FilterProvider.ParameterFilter);

        _cleanupSNDrumKitPartialParameters = _sourceCacheSNDrumKitPartialParameters.Connect()
            .Filter(refreshFilterSNDrumKitPartialParameters)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Filter(parFilterSNDrumKitPartialParameters)
            .FilterOnObservable(fullyQualifiedParameter =>
                fullyQualifiedParameter.ParSpec.ParentCtrl != "" &&
                fullyQualifiedParameter.ParSpec.ParentCtrl is string parentId
                    ? _sourceCacheSNDrumKitPartialParameters
                        .Watch(parentId)
                        .Select(parentChange => parentChange.Current.StringValue ==
                                                fullyQualifiedParameter.ParSpec.ParentCtrlDispValue)
                    : Observable.Return(true))
            .FilterOnObservable(fullyQualifiedParameter =>
                fullyQualifiedParameter.ParSpec.ParentCtrl2 != "" &&
                fullyQualifiedParameter.ParSpec.ParentCtrl2 is string parentId2
                    ? _sourceCacheSNDrumKitPartialParameters
                        .Watch(parentId2)
                        .Select(parentChange2 => parentChange2.Current.StringValue ==
                                                 fullyQualifiedParameter.ParSpec.ParentCtrlDispValue2)
                    : Observable.Return(true))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SortAndBind(
                out _SNDrumKitPartialParameters,
                SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                    ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
            .DisposeMany()
            .Subscribe();

        //InitializeParameterSourceCachesAsync(); // call outside constructor
    }

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNDrumKitPartialParameters =>
        _SNDrumKitPartialParameters;

    public override int GetPartialOffset()
    {
        return Constants.FIRST_PARTIAL_SN_DRUM;
    }

    public override string GetPartialName()
    {
        return "";
    }

    public override string GetSearchTextPartial()
    {
        return _searchTextSNDrumKitPartial;
    }

    public override void SetSearchTextPartial(string value)
    {
        SearchTextSNDrumKitPartial = value;
    }

    public override ReadOnlyObservableCollection<FullyQualifiedParameter> GetPartialParameters()
    {
        return _SNDrumKitPartialParameters;
    }

    public override void ForceUiRefresh(string startAddressName, string offsetAddressName, string offset2AddressName,
        string parPath, bool resyncNeeded)
    {
        if (startAddressName == $"Temporary Tone Part {_zeroBasedPart + 1}" &&
            offset2AddressName == $"Offset2/SuperNATURAL Drum Kit Partial {_zeroBasedPartial + 1}")
        {
            RefreshSNDrumKitPartial = ".";
            RefreshSNDrumKitPartial = SearchTextSNDrumKitPartial;
        }
    }

    public override bool IsValidForCurrentPreset()
    {
        return _toneTypeStr == "SN-D";
    }

    public override async Task InitializeParameterSourceCachesAsync()
    {
        if (_i7domain == null)
            return;

        if (IsValidForCurrentPreset())
            await _i7domain.SNDrumKitPartial(_zeroBasedPart, _zeroBasedPartial).ReadFromIntegraAsync();
        List<FullyQualifiedParameter> par2 = _i7domain.SNDrumKitPartial(_zeroBasedPart, _zeroBasedPartial)
            .GetRelevantParameters(true, true);
        _sourceCacheSNDrumKitPartialParameters.AddOrUpdate(par2);
    }

    public override async Task ResyncPartAsync(byte part)
    {
        if (part == _zeroBasedPart && IsValidForCurrentPreset())
        {
            var b = _i7domain.SNDrumKitPartial(_zeroBasedPart, _zeroBasedPartial);
            await b.ReadFromIntegraAsync();
            ForceUiRefresh(b.StartAddressName, b.OffsetAddressName, b.Offset2AddressName, "",
                false /* don't cause inf loop */);
        }
    }
}