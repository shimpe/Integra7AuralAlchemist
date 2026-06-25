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

public sealed partial class SNSynthTonePartialViewModel : PartialViewModel
{
    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _SNSynthTonePartialParameters = new([]);

    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCacheSNSynthTonePartialParameters =
        new(x => x.ParSpec.Path);

    private IDisposable? _cleanupSNSynthTonePartialParameters;
    [Reactive] private string _refreshSNSynthTonePartial = "";

    [Reactive] private string _searchTextSNSynthTonePartial = "";

    public SNSynthTonePartialViewModel(PartViewModel parent, byte zeroBasedPart, byte zeroBasedPartial,
        string toneTypeStr, Integra7StartAddresses i7addr, Integra7Parameters par,
        IIntegra7Api i7api, Integra7Domain i7dom, SemaphoreSlim semaphore) : base(parent, zeroBasedPart,
        zeroBasedPartial, toneTypeStr, i7addr,
        par, i7api, i7dom, semaphore)
    {
        var parFilterSNSynthTonePartialParameters = this.WhenAnyValue(x => x.SearchTextSNSynthTonePartial)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .DistinctUntilChanged()
            .Select(FilterProvider.ParameterFilter);
        var refreshFilterSNSynthTonePartialParameters = this.WhenAnyValue(x => x.RefreshSNSynthTonePartial)
            .Select(FilterProvider.ParameterFilter);

        _cleanupSNSynthTonePartialParameters = _sourceCacheSNSynthTonePartialParameters.Connect()
            .Filter(refreshFilterSNSynthTonePartialParameters)
            .Batch(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Filter(parFilterSNSynthTonePartialParameters)
            .FilterOnObservable(fullyQualifiedParameter =>
                fullyQualifiedParameter.ParSpec.ParentCtrl != "" &&
                fullyQualifiedParameter.ParSpec.ParentCtrl is string parentId
                    ? _sourceCacheSNSynthTonePartialParameters
                        .Watch(parentId)
                        .Select(parentChange => parentChange.Current.StringValue ==
                                                fullyQualifiedParameter.ParSpec.ParentCtrlDispValue)
                    : Observable.Return(true))
            .FilterOnObservable(fullyQualifiedParameter =>
                fullyQualifiedParameter.ParSpec.ParentCtrl2 != "" &&
                fullyQualifiedParameter.ParSpec.ParentCtrl2 is string parentId2
                    ? _sourceCacheSNSynthTonePartialParameters
                        .Watch(parentId2)
                        .Select(parentChange2 => parentChange2.Current.StringValue ==
                                                 fullyQualifiedParameter.ParSpec.ParentCtrlDispValue2)
                    : Observable.Return(true))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SortAndBind(
                out _SNSynthTonePartialParameters,
                SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                    ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
            .DisposeMany()
            .Subscribe();

        // InitializeParameterSourceCachesAsync(); // call outside constructor 
    }

    public ReadOnlyObservableCollection<FullyQualifiedParameter> SNSynthTonePartialParameters =>
        _SNSynthTonePartialParameters;

    public override async Task InitializeParameterSourceCachesAsync()
    {
        if (_i7domain == null)
            return;

        if (IsValidForCurrentPreset())
            await _i7domain.SNSynthTonePartial(_zeroBasedPart, _zeroBasedPartial).ReadFromIntegraAsync();
        List<FullyQualifiedParameter> par = _i7domain.SNSynthTonePartial(_zeroBasedPart, _zeroBasedPartial)
            .GetRelevantParameters(true, true);
        _sourceCacheSNSynthTonePartialParameters.AddOrUpdate(par);
    }

    public override void ForceUiRefresh(string startAddressName, string offsetAddressName, string offset2AddressName,
        string parPath, bool resyncNeeded)
    {
        if (startAddressName == $"Temporary Tone Part {_zeroBasedPart + 1}" &&
            offset2AddressName == $"Offset2/SuperNATURAL Synth Tone Partial {_zeroBasedPartial + 1}")
        {
            RefreshSNSynthTonePartial = ".";
            RefreshSNSynthTonePartial = SearchTextSNSynthTonePartial;
        }
    }

    public override int GetPartialOffset()
    {
        return Constants.FIRST_PARTIAL_SN_SYNTH_TONE;
    }

    public override string GetPartialName()
    {
        return "Partial";
    }

    public override string GetSearchTextPartial()
    {
        return _searchTextSNSynthTonePartial;
    }

    public override void SetSearchTextPartial(string value)
    {
        SearchTextSNSynthTonePartial = value;
    }

    public override ReadOnlyObservableCollection<FullyQualifiedParameter> GetPartialParameters()
    {
        return _SNSynthTonePartialParameters;
    }

    public override bool IsValidForCurrentPreset()
    {
        return _toneTypeStr == "SN-S";
    }

    public override async Task ResyncPartAsync(byte part)
    {
        if (part == _zeroBasedPart && IsValidForCurrentPreset())
        {
            var b = _i7domain.SNSynthTonePartial(_zeroBasedPart, _zeroBasedPartial);
            await b.ReadFromIntegraAsync();
            ForceUiRefresh(b.StartAddressName, b.OffsetAddressName, b.Offset2AddressName, "",
                false /* don't cause inf loop */);
        }
    }
}