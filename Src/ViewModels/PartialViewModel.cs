using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

public class PartialViewModel : ViewModelBase
{
    protected readonly byte _zeroBasedPart;
    protected readonly byte _zeroBasedPartial;
    private Integra7StartAddresses _i7addr;
    private IIntegra7Api _i7api;
    protected Integra7Domain? _i7domain;
    private Integra7Parameters _i7par;
    private PartViewModel _parent;
    protected string _toneTypeStr;

    public PartialViewModel(PartViewModel parent, byte zeroBasedPart, byte zeroBasedPartial,
        string toneTypeStr, Integra7StartAddresses i7addr,
        Integra7Parameters par, IIntegra7Api i7api, Integra7Domain i7dom)
    {
        _parent = parent;
        _zeroBasedPart = zeroBasedPart;
        _zeroBasedPartial = zeroBasedPartial;
        _i7addr = i7addr;
        _i7par = par;
        _i7api = i7api;
        _i7domain = i7dom;
        _toneTypeStr = toneTypeStr;
    }

    public ReadOnlyObservableCollection<FullyQualifiedParameter> PartialParameters => GetPartialParameters();

    public string SearchTextPartial
    {
        get => GetSearchTextPartial();
        set
        {
            this.RaisePropertyChanging();
            SetSearchTextPartial(value);
            this.RaisePropertyChanged();
        }
    }

    public Integra7Domain I7Domain
    {
        set => _i7domain = value;
    }

    public string Header => GetPartialName() + $" {_zeroBasedPartial + GetPartialOffset()}";

    public void UpdateToneTypeString(string toneTypeStr)
    {
        _toneTypeStr = toneTypeStr;
    }

    public virtual string GetSearchTextPartial()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
        return "";
    }

    public virtual void SetSearchTextPartial(string value)
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
    }

    public virtual ReadOnlyObservableCollection<FullyQualifiedParameter> GetPartialParameters()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
        return new ReadOnlyObservableCollection<FullyQualifiedParameter>([]);
    }

    public virtual void ForceUiRefresh(string startAddressName, string offsetAddressName, string offset2AddressName,
        string parPath, bool resyncNeeded)
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
    }

    public virtual string GetPartialName()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
        return "";
    }

    public virtual int GetPartialOffset()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
        return 0;
    }

    public virtual async Task InitializeParameterSourceCachesAsync()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
    }

    public virtual async Task ResyncPartAsync(byte part)
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
    }

    public virtual bool IsValidForCurrentPreset()
    {
        // overridden in specific view models
        Debug.Assert(false, "Must implement in child class.");
        return false;
    }
}