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

    /// <summary>The single-parameter write door, and with it the single-parameter <em>record</em> door:
    /// every one of this editor's non-position edits funnels through here (the common values via
    /// <see cref="EnqueueCommonWrite"/>, a part's Width, Ambience and Channel from their own setters), so
    /// recording here records all of them.
    ///
    /// Every caller is inside an <c>if (!_suppress)</c> branch -- a user edit rather than a value the
    /// device or a preset pushed back at us -- which is the same distinction, for the same reason, that
    /// <c>ParamInt.Value</c> records inside.</summary>
    public void EnqueueValueWrite(DomainBase domain, string path, string displayValue)
    {
        // Before the enqueue, not after. The old value is read off the block, and nothing has replaced it
        // yet: the write below is debounced (and deferred even without the debounce -- OnNext only pushes
        // the closure), so ModifySingleParameterDisplayedValue has not run.
        DomainEditRecorder.Record(domain, path, displayValue);
        // The block is part of the throttle key, matching ThrottledParameterWriter's own
        // start|offset2|path. Sixteen parts share one path for Width and for Ambience Send Level, so a
        // key of the path alone put them in one debounce group and let a second part's edit supersede a
        // first part's pending write -- leaving the journal describing a change that never went out.
        var key = $"val:{domain.StartAddressName}|{domain.Offset2AddressName}|{path}";
        _writes.OnNext(new MsWrite(key, () => domain.WriteToIntegraAsync(path, displayValue)));
    }

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

    private void InitGlobalsFromModel()
    {
        _suppress = true;
        try
        {
            MotionalSurroundOn = C("Motional Surround Switch").StringValue == "ON";
            RoomTypeIndex = Math.Max(0, Array.IndexOf(RoomTypes, C("Room Type").StringValue));
            RoomSizeIndex = Math.Max(0, Array.IndexOf(RoomSizes, C("Room Size").StringValue));
            Depth = MotionalSurroundMapping.ParseDisplayInt(C("Motional Surround Depth").StringValue);
            AmbienceLevel = MotionalSurroundMapping.ParseDisplayInt(C("Ambience Level").StringValue);
            AmbienceTime = MotionalSurroundMapping.ParseDisplayInt(C("Ambience Time").StringValue);
            AmbienceDensity = MotionalSurroundMapping.ParseDisplayInt(C("Ambience Density").StringValue);
            AmbienceHfDamp = MotionalSurroundMapping.ParseDisplayInt(C("Ambience HF Damp").StringValue);
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

    private void UnsubscribeGlobals()
    {
        void Unsub(string shortName) => C(shortName).PropertyChanged -= OnCommonChanged;
        Unsub("Motional Surround Switch"); Unsub("Room Type"); Unsub("Room Size");
        Unsub("Motional Surround Depth"); Unsub("Ambience Level"); Unsub("Ambience Time");
        Unsub("Ambience Density"); Unsub("Ambience HF Damp");
    }

    private void OnCommonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(InitGlobalsFromModel);
    }

    // ---- Presets (batch updates; write each changed value once, straight out) ----

    /// <summary>Hold one undo step open for the whole of a preset command.
    ///
    /// A preset is one thing the user did -- they pressed "Ambient Hall" -- so undo has to put all of it
    /// back on one press. Without this the globals would coalesce on the clock into a step or two of their
    /// own and each part's position would be strewn across further steps, so one press would restore the
    /// room and leave every part where the preset put it: neither the before state nor the after state, and
    /// nothing on screen to say so.
    ///
    /// The scope outlives several awaits, which is safe and is why the journal counts gesture depth rather
    /// than flagging it. It also has to: <see cref="EditJournal.StaleGestureWindow"/> only gives up on a
    /// gesture that records <em>nothing</em> for ten seconds, and a preset records immediately before each
    /// of its writes, so the gaps within one are a write apart and never approach that.
    ///
    /// The resulting step is large -- up to 17 parts x 2 axes plus the width, ambience and common values, so
    /// on the order of 34 to 70 changes. That is one step against <see cref="EditJournal.Capacity"/>, which
    /// counts steps, but undoing it writes every one of those parameters in turn under a single lease, so it
    /// takes as long as the preset itself did rather than feeling instant like undoing a knob. That is the
    /// right trade: the alternative is a step that only half describes what happened.</summary>
    private static IDisposable BeginPresetStep() => EditJournal.Default.BeginGesture();

    public async Task CenterAll()
    {
        using var step = BeginPresetStep();
        foreach (var p in AllParts) await p.ApplyPositionAsync(0, 0);
    }

    public async Task WideStereoSpread()
    {
        using var step = BeginPresetStep();
        // Spread internal parts evenly across L-R at center depth; external stays centered.
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            await InternalParts[i].ApplyPositionAsync(lr, 0);
        }
    }

    public async Task FrontBandLayout()
    {
        using var step = BeginPresetStep();
        // A row of parts near the front (F-B = -48), spread across L-R.
        const int front = -48;
        for (var i = 0; i < InternalParts.Count; i++)
        {
            var lr = MotionalSurroundMapping.FromNormalized(i / (double)(InternalParts.Count - 1),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            await InternalParts[i].ApplyPositionAsync(lr, front);
        }
    }

    public async Task AmbientHallLayout()
    {
        using var step = BeginPresetStep();
        // Big, lush room + parts pushed slightly back with healthy ambience send.
        RoomTypeIndex = 3;            // Hall2
        RoomSizeIndex = 2;            // Large
        Depth = 80; AmbienceLevel = 100; AmbienceTime = 70; AmbienceDensity = 60; AmbienceHfDamp = 40;
        foreach (var p in InternalParts)
        {
            await p.ApplyPositionAsync(p.Lr, 24);       // nudge back, keep L-R
            await p.ApplyWidthAmbienceAsync(20, 90);
        }
    }

    public async Task ResetMotionalSurround()
    {
        using var step = BeginPresetStep();
        // Opinionated neutral defaults (UI-level reset, not a factory dump).
        MotionalSurroundOn = true;
        RoomTypeIndex = 0; RoomSizeIndex = 1;
        Depth = 50; AmbienceLevel = 64; AmbienceTime = 50; AmbienceDensity = 50; AmbienceHfDamp = 50;
        foreach (var p in AllParts)
        {
            await p.ApplyPositionAsync(0, 0);
            await p.ApplyWidthAmbienceAsync(16, 0);
        }
        ExternalPart.Channel = "OFF";
    }

    public async Task CircleAroundCenter()
    {
        using var step = BeginPresetStep();
        // Evenly space all parts on a circle of radius 32 (L-R/F-B units) around the centre.
        const double radius = 32.0;
        var n = AllParts.Count;
        for (var i = 0; i < n; i++)
        {
            var angle = 2.0 * Math.PI * i / n;
            var lr = (int)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero);
            var fb = (int)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero);
            await AllParts[i].ApplyPositionAsync(lr, fb);
        }
    }

    // A fixed (not regenerated) uniform-ish scatter across the field, one entry per part
    // (16 internal + external). Hand-picked so it looks random but is deterministic.
    private static readonly (int Lr, int Fb)[] ScatterPositions =
    [
        (-58, 41), (33, -50), (12, 18), (-44, -27), (60, 7), (-9, 55), (47, -39), (-31, -60),
        (5, -14), (-52, 23), (29, 49), (-18, -45), (54, 31), (-63, -8), (21, -58), (-37, 12),
        (40, -20)
    ];

    public async Task RandomScatter()
    {
        using var step = BeginPresetStep();
        for (var i = 0; i < AllParts.Count && i < ScatterPositions.Length; i++)
        {
            var (lr, fb) = ScatterPositions[i];
            await AllParts[i].ApplyPositionAsync(lr, fb);
        }
    }

    public void Dispose()
    {
        _writeSub.Dispose();
        _writes.Dispose();
        UnsubscribeGlobals();
        foreach (var p in AllParts) p.Dispose();
    }
}
