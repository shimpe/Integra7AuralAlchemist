using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Integra7AuralAlchemist.ViewModels;

public partial class MotionalSurroundViewModel : ViewModelBase, IDisposable
{
    private const string Prefix = "Studio Set Common Motional Surround/";

    private static readonly string[] RoomTypes = ["Room1", "Room2", "Hall1", "Hall2"];
    private static readonly string[] RoomSizes = ["Small", "Medium", "Large"];

    private readonly DomainBase _common;
    private readonly Dictionary<string, FullyQualifiedParameter> _commonByPath;

    private readonly Subject<MsWrite> _writes = new();
    private readonly IDisposable _writeSub;

    private bool _suppress;

    private sealed record MsWrite(string Key, Func<Task> WriteAsync);

    public ObservableCollection<MotionalSurroundPartViewModel> InternalParts { get; } = [];
    public MotionalSurroundPartViewModel ExternalPart { get; }
    public IReadOnlyList<MotionalSurroundPartViewModel> AllParts { get; }
    public string[] RoomTypeOptions => RoomTypes;
    public string[] RoomSizeOptions => RoomSizes;
    public string[] ChannelOptions { get; } =
        Enumerable.Range(1, 16).Select(i => i.ToString(CultureInfo.InvariantCulture)).Append("OFF").ToArray();

    public MotionalSurroundViewModel(Integra7Domain communicator)
    {
        _common = communicator.StudioSetCommonMotionalSurround;
        _commonByPath = _common.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path);

        // Per-key debounce: each key (a part position or a single parameter path) is throttled
        // independently, so a diagonal puck drag flushes BOTH axes (unlike the global ui2hw stream).
        _writeSub = _writes
            .GroupBy(w => w.Key)
            .SelectMany(g => g.Throttle(TimeSpan.FromMilliseconds(Constants.THROTTLE)))
            .Subscribe(async w =>
            {
                try { await w.WriteAsync(); }
                catch (Exception ex) { Log.Error(ex, "Motional Surround write failed for key {Key}", w.Key); }
            });

        // 16 internal parts, each from its own Studio Set Part domain.
        for (var i = 0; i < Constants.NO_OF_PARTS; i++)
        {
            var d = communicator.StudioSetPart(i);
            var byPath = d.GetRelevantParameters(true, true).ToDictionary(p => p.ParSpec.Path);
            var vm = new MotionalSurroundPartViewModel(this, d, i, false,
                byPath["Studio Set Part/Motional Surround L-R"],
                byPath["Studio Set Part/Motional Surround F-B"],
                byPath["Studio Set Part/Motional Surround Width"],
                byPath["Studio Set Part/Motional Surround Ambience Send Level"]);
            InternalParts.Add(vm);
        }

        ExternalPart = new MotionalSurroundPartViewModel(this, _common, -1, true,
            C("Ext Part L-R"), C("Ext Part F-B"), C("Ext Part Width"),
            C("Ext Part Ambience Send Level"), C("Ext Part Control Channel"));

        AllParts = InternalParts.Append(ExternalPart).ToList();
        _selectedPart = InternalParts[0];
        _selectedPart.IsSelected = true;

        InitGlobalsFromModel();
        SubscribeGlobals();
    }

    private FullyQualifiedParameter C(string shortName) => _commonByPath[Prefix + shortName];

    // ---- Write pipeline entry points (called by part VMs and global setters) ----
    public void EnqueuePositionWrite(MotionalSurroundPartViewModel part)
        => _writes.OnNext(new MsWrite($"pos:{part.Key}", part.WritePositionAsync));

    public void EnqueueValueWrite(DomainBase domain, string path, string displayValue)
        => _writes.OnNext(new MsWrite($"val:{path}", () => domain.WriteToIntegraAsync(path, displayValue)));

    private void EnqueueCommonWrite(string shortName, string displayValue)
        => EnqueueValueWrite(_common, Prefix + shortName, displayValue);

    // ---- Selection ----
    private MotionalSurroundPartViewModel _selectedPart;
    public MotionalSurroundPartViewModel SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (ReferenceEquals(_selectedPart, value) || value is null) return;
            _selectedPart.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedPart, value);
            _selectedPart.IsSelected = true;
        }
    }

    // ---- Stage size (pushed by the view; recompute every puck's canvas coords) ----
    private double _stageWidth = 1, _stageHeight = 1;
    public double StageWidth
    {
        get => _stageWidth;
        set { if (value > 0 && Math.Abs(_stageWidth - value) > 0.5) { _stageWidth = value; RaiseAllCanvas(); } }
    }
    public double StageHeight
    {
        get => _stageHeight;
        set { if (value > 0 && Math.Abs(_stageHeight - value) > 0.5) { _stageHeight = value; RaiseAllCanvas(); } }
    }
    private void RaiseAllCanvas() { foreach (var p in AllParts) p.RaiseCanvasChanged(); }

    // ---- Global reactive properties (bound to the common-domain FQPs) ----
    private bool _on;
    public bool MotionalSurroundOn
    {
        get => _on;
        set { if (_on == value) return; this.RaiseAndSetIfChanged(ref _on, value);
              if (!_suppress) EnqueueCommonWrite("Motional Surround Switch", value ? "ON" : "OFF"); }
    }

    private int _roomType;
    public int RoomTypeIndex
    {
        get => _roomType;
        set { value = MotionalSurroundMapping.Clamp(value, 0, RoomTypes.Length - 1);
              if (_roomType == value) return; this.RaiseAndSetIfChanged(ref _roomType, value);
              if (!_suppress) EnqueueCommonWrite("Room Type", RoomTypes[value]); }
    }

    private int _roomSize;
    public int RoomSizeIndex
    {
        get => _roomSize;
        set { value = MotionalSurroundMapping.Clamp(value, 0, RoomSizes.Length - 1);
              if (_roomSize == value) return; this.RaiseAndSetIfChanged(ref _roomSize, value);
              if (!_suppress) EnqueueCommonWrite("Room Size", RoomSizes[value]); }
    }

    private int _depth;
    public int Depth
    {
        get => _depth;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_depth == value) return; this.RaiseAndSetIfChanged(ref _depth, value);
              if (!_suppress) EnqueueCommonWrite("Motional Surround Depth", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambLevel;
    public int AmbienceLevel
    {
        get => _ambLevel;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 127);
              if (_ambLevel == value) return; this.RaiseAndSetIfChanged(ref _ambLevel, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Level", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambTime;
    public int AmbienceTime
    {
        get => _ambTime;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambTime == value) return; this.RaiseAndSetIfChanged(ref _ambTime, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Time", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambDensity;
    public int AmbienceDensity
    {
        get => _ambDensity;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambDensity == value) return; this.RaiseAndSetIfChanged(ref _ambDensity, value);
              if (!_suppress) EnqueueCommonWrite("Ambience Density", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private int _ambHfDamp;
    public int AmbienceHfDamp
    {
        get => _ambHfDamp;
        set { value = MotionalSurroundMapping.Clamp(value, 0, 100);
              if (_ambHfDamp == value) return; this.RaiseAndSetIfChanged(ref _ambHfDamp, value);
              if (!_suppress) EnqueueCommonWrite("Ambience HF Damp", value.ToString(CultureInfo.InvariantCulture)); }
    }

    private static int ParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void InitGlobalsFromModel()
    {
        _suppress = true;
        try
        {
            MotionalSurroundOn = C("Motional Surround Switch").StringValue == "ON";
            RoomTypeIndex = Math.Max(0, Array.IndexOf(RoomTypes, C("Room Type").StringValue));
            RoomSizeIndex = Math.Max(0, Array.IndexOf(RoomSizes, C("Room Size").StringValue));
            Depth = ParseInt(C("Motional Surround Depth").StringValue);
            AmbienceLevel = ParseInt(C("Ambience Level").StringValue);
            AmbienceTime = ParseInt(C("Ambience Time").StringValue);
            AmbienceDensity = ParseInt(C("Ambience Density").StringValue);
            AmbienceHfDamp = ParseInt(C("Ambience HF Damp").StringValue);
        }
        finally { _suppress = false; }
    }

    private void SubscribeGlobals()
    {
        void Sub(string shortName) => C(shortName).PropertyChanged += OnCommonChanged;
        Sub("Motional Surround Switch"); Sub("Room Type"); Sub("Room Size");
        Sub("Motional Surround Depth"); Sub("Ambience Level"); Sub("Ambience Time");
        Sub("Ambience Density"); Sub("Ambience HF Damp");
    }

    private void OnCommonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(InitGlobalsFromModel);
    }

    // ---- Presets (batch updates; suppress UI writes then write each changed value once) ----
    [ReactiveCommand]
    public async Task CenterAll()
    {
        foreach (var p in AllParts) { p.SetPositionSuppressed(0, 0); await p.WritePositionAsync(); }
    }

    [ReactiveCommand]
    public async Task WideStereoSpread()
    {
        // Spread internal parts evenly across L-R at center depth; external stays centered.
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            InternalParts[i].SetPositionSuppressed(lr, 0);
            await InternalParts[i].WritePositionAsync();
        }
    }

    [ReactiveCommand]
    public async Task FrontBandLayout()
    {
        // A row of parts near the front (F-B = -48), spread across L-R.
        const int front = -48;
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            InternalParts[i].SetPositionSuppressed(lr, front);
            await InternalParts[i].WritePositionAsync();
        }
    }

    [ReactiveCommand]
    public async Task AmbientHallLayout()
    {
        // Big, lush room + parts pushed slightly back with healthy ambience send.
        RoomTypeIndex = 3;            // Hall2
        RoomSizeIndex = 2;            // Large
        Depth = 80; AmbienceLevel = 100; AmbienceTime = 70; AmbienceDensity = 60; AmbienceHfDamp = 40;
        foreach (var p in InternalParts)
        {
            p.SetPositionSuppressed(p.Lr, 24);          // nudge back, keep L-R
            p.SetWidthAmbienceSuppressed(20, 90);
            await p.WritePositionAsync();
            await p.WriteWidthAmbienceAsync();
        }
    }

    [ReactiveCommand]
    public async Task ResetMotionalSurround()
    {
        // Opinionated neutral defaults (UI-level reset, not a factory dump).
        MotionalSurroundOn = true;
        RoomTypeIndex = 0; RoomSizeIndex = 1;
        Depth = 50; AmbienceLevel = 64; AmbienceTime = 50; AmbienceDensity = 50; AmbienceHfDamp = 50;
        foreach (var p in AllParts)
        {
            p.SetPositionSuppressed(0, 0);
            p.SetWidthAmbienceSuppressed(16, 0);
            await p.WritePositionAsync();
            await p.WriteWidthAmbienceAsync();
        }
        ExternalPart.Channel = "OFF";
    }

    public void Dispose()
    {
        _writeSub.Dispose();
        _writes.Dispose();
    }
}
