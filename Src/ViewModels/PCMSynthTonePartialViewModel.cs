using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

public sealed partial class PCMSynthTonePartialViewModel : PartialViewModel
{
    private readonly SourceCache<FullyQualifiedParameter, string> _sourceCachePCMSynthTonePartialParameters =
        new(x => x.ParSpec.Path);
    private readonly ReadOnlyObservableCollection<FullyQualifiedParameter> _PCMSynthTonePartialParameters = new([]);
    public ReadOnlyObservableCollection<FullyQualifiedParameter> PCMSynthTonePartialParameters =>
        _PCMSynthTonePartialParameters;

    [Reactive] private string _searchTextPCMSynthTonePartial = "";
    [Reactive] private string _refreshPCMSynthTonePartial = "";
    
    IDisposable? _cleanupPCMSynthTonePartialParameters;

    public PCMSynthTonePartialViewModel(PartViewModel parent, byte zeroBasedPart, byte zeroBasedPartial,
        string toneTypeStr, Integra7StartAddresses i7addr, Integra7Parameters par,
        IIntegra7Api i7api, Integra7Domain i7dom) : base(parent, zeroBasedPart, zeroBasedPartial, toneTypeStr, i7addr,
        par, i7api, i7dom)
    {
        var parFilterPCMSynthTonePartialParameters = this.WhenAnyValue(x => x.SearchTextPCMSynthTonePartial)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .DistinctUntilChanged()
            .Select(FilterProvider.ParameterFilter);
        var refreshFilterPCMSynthTonePartialParameters = this.WhenAnyValue(x => x.RefreshPCMSynthTonePartial)
            .Select(FilterProvider.ParameterFilter);
        
        _cleanupPCMSynthTonePartialParameters = _sourceCachePCMSynthTonePartialParameters.Connect()
            .Filter(refreshFilterPCMSynthTonePartialParameters)
            .Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE))
            .Filter(parFilterPCMSynthTonePartialParameters)
            .FilterOnObservable(fullyQualifiedParameter =>
                ((fullyQualifiedParameter.ParSpec.ParentCtrl != "") &&
                 (fullyQualifiedParameter.ParSpec.ParentCtrl is string parentId))
                    ? _sourceCachePCMSynthTonePartialParameters
                        .Watch(parentId)
                        .Select(parentChange => parentChange.Current.StringValue ==
                                                fullyQualifiedParameter.ParSpec.ParentCtrlDispValue)
                    : Observable.Return(true))
            .FilterOnObservable(fullyQualifiedParameter =>
                ((fullyQualifiedParameter.ParSpec.ParentCtrl2 != "") &&
                 (fullyQualifiedParameter.ParSpec.ParentCtrl2 is string parentId2))
                    ? _sourceCachePCMSynthTonePartialParameters
                        .Watch(parentId2)
                        .Select(parentChange2 => parentChange2.Current.StringValue ==
                                                 fullyQualifiedParameter.ParSpec.ParentCtrlDispValue2)
                    : Observable.Return(true))
            .ObserveOn(RxApp.MainThreadScheduler)
            .SortAndBind(
                out _PCMSynthTonePartialParameters,
                SortExpressionComparer<FullyQualifiedParameter>.Ascending(t =>
                    ByteUtils.Bytes7ToInt(t.ParSpec.Address)))
            .DisposeMany()
            .Subscribe();
        
        InitializeParameterSourceCaches();
    }
    public override void InitializeParameterSourceCaches()
    {
        if (_i7domain == null)
            return;
        
        if (IsValidForCurrentPreset())
        {
            _i7domain.PCMSynthTonePartial(_zeroBasedPart, _zeroBasedPartial).ReadFromIntegra();
        }
        List<FullyQualifiedParameter> par = _i7domain.PCMSynthTonePartial(_zeroBasedPart, _zeroBasedPartial)
            .GetRelevantParameters(true, true);
        _sourceCachePCMSynthTonePartialParameters.AddOrUpdate(par);
    }

    public override void ForceUiRefresh(string startAddressName, string offsetAddressName, string offset2AddressName, string parPath, bool resyncNeeded)
    {
        if (startAddressName == $"Temporary Tone Part {_zeroBasedPart + 1}" &&
            offset2AddressName == $"Offset2/PCM Synth Tone Partial {_zeroBasedPartial + 1}")
        {
            RefreshPCMSynthTonePartial = ".";
            RefreshPCMSynthTonePartial = SearchTextPCMSynthTonePartial;
        }
    }

    public override int GetPartialOffset()
    {
        return Constants.FIRST_PARTIAL_PCM_SYNTH_TONE;
    }
    
    public override string GetPartialName()
    {
        return "Partial";
    }
    
    public override string GetSearchTextPartial()
    {
        return _searchTextPCMSynthTonePartial;
    }
    public override void SetSearchTextPartial(string value)
    {
        SearchTextPCMSynthTonePartial = value;
    }
    public override ReadOnlyObservableCollection<FullyQualifiedParameter> GetPartialParameters()
    {
        return _PCMSynthTonePartialParameters;
    }
    
    public override bool IsValidForCurrentPreset()
    {
        return _toneTypeStr == "PCMS";
    }
    
    public override void ResyncPart(byte part)
    {
        if (part == _zeroBasedPart && IsValidForCurrentPreset())
        {
            DomainBase b = _i7domain.PCMSynthTonePartial(_zeroBasedPart, _zeroBasedPartial); 
            b.ReadFromIntegra();
            ForceUiRefresh(b.StartAddressName, b.OffsetAddressName, b.Offset2AddressName, "",
                false /* don't cause inf loop */);
        }
    }
}