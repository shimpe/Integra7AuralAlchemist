using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

public class MotionalSurroundPartViewModel : ViewModelBase, IDisposable
{
    private readonly MotionalSurroundViewModel _parent;
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _lrParam;
    private readonly FullyQualifiedParameter _fbParam;
    private readonly FullyQualifiedParameter _widthParam;
    private readonly FullyQualifiedParameter _ambParam;
    private readonly FullyQualifiedParameter? _channelParam; // external only

    private bool _suppress;
    private int _lr, _fb, _width, _amb;
    private string _channel = "OFF";
    private bool _isSelected;

    public bool IsExternal { get; }
    /// <summary>Zero-based internal part index, or -1 for the external part.</summary>
    public int PartIndex { get; }
    public string Key => IsExternal ? "ext" : $"p{PartIndex}";
    public string Label => IsExternal ? "Ext" : $"P{PartIndex + 1}";
    public DomainBase Domain => _domain;
    public string LrPath => _lrParam.ParSpec.Path;
    public string FbPath => _fbParam.ParSpec.Path;

    public MotionalSurroundPartViewModel(MotionalSurroundViewModel parent, DomainBase domain,
        int partIndex, bool isExternal,
        FullyQualifiedParameter lr, FullyQualifiedParameter fb,
        FullyQualifiedParameter width, FullyQualifiedParameter amb,
        FullyQualifiedParameter? channel = null)
    {
        _parent = parent;
        _domain = domain;
        PartIndex = partIndex;
        IsExternal = isExternal;
        _lrParam = lr; _fbParam = fb; _widthParam = width; _ambParam = amb; _channelParam = channel;

        // Initialize from the live model with writes suppressed.
        ApplyFromModel();

        // Track hardware/preset/raw-tab changes on the shared FQP objects.
        lr.PropertyChanged += OnParamChanged;
        fb.PropertyChanged += OnParamChanged;
        width.PropertyChanged += OnParamChanged;
        amb.PropertyChanged += OnParamChanged;
        if (channel != null) channel.PropertyChanged += OnParamChanged;
    }

    private void OnParamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        // FQP changes may arrive on a MIDI background thread; marshal to UI.
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try
        {
            Lr = MotionalSurroundMapping.Clamp(MotionalSurroundMapping.ParseDisplayInt(_lrParam.StringValue),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            Fb = MotionalSurroundMapping.Clamp(MotionalSurroundMapping.ParseDisplayInt(_fbParam.StringValue),
                MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            Width = MotionalSurroundMapping.Clamp(MotionalSurroundMapping.ParseDisplayInt(_widthParam.StringValue),
                MotionalSurroundMapping.WidthMin, MotionalSurroundMapping.WidthMax);
            Ambience = MotionalSurroundMapping.Clamp(MotionalSurroundMapping.ParseDisplayInt(_ambParam.StringValue),
                MotionalSurroundMapping.AmbienceMin, MotionalSurroundMapping.AmbienceMax);
            if (_channelParam != null) Channel = _channelParam.StringValue;
        }
        finally { _suppress = false; }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public int Lr
    {
        get => _lr;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            if (_lr == value) return;
            this.RaiseAndSetIfChanged(ref _lr, value);
            this.RaisePropertyChanged(nameof(CanvasX));
            this.RaisePropertyChanged(nameof(PositionLabel));
            if (!_suppress) _parent.EnqueuePositionWrite(this);
        }
    }

    public int Fb
    {
        get => _fb;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax);
            if (_fb == value) return;
            this.RaiseAndSetIfChanged(ref _fb, value);
            this.RaisePropertyChanged(nameof(CanvasY));
            this.RaisePropertyChanged(nameof(PositionLabel));
            if (!_suppress) _parent.EnqueuePositionWrite(this);
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.WidthMin, MotionalSurroundMapping.WidthMax);
            if (_width == value) return;
            this.RaiseAndSetIfChanged(ref _width, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _widthParam.ParSpec.Path, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public int Ambience
    {
        get => _amb;
        set
        {
            value = MotionalSurroundMapping.Clamp(value, MotionalSurroundMapping.AmbienceMin, MotionalSurroundMapping.AmbienceMax);
            if (_amb == value) return;
            this.RaiseAndSetIfChanged(ref _amb, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _ambParam.ParSpec.Path, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>External part only. Valid values: "1".."16" or "OFF".</summary>
    public string Channel
    {
        get => _channel;
        set
        {
            if (_channelParam == null) return;
            if (!MotionalSurroundMapping.IsValidControlChannel(value)) return;
            if (_channel == value) return;
            this.RaiseAndSetIfChanged(ref _channel, value);
            if (!_suppress) _parent.EnqueueValueWrite(_domain, _channelParam.ParSpec.Path, value);
        }
    }

    // --- Canvas coordinates (set by the parent when the stage is measured) ---
    public double CanvasX => MotionalSurroundMapping.ToNormalized(_lr,
        MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax) * _parent.StageWidth;
    public double CanvasY => MotionalSurroundMapping.ToNormalized(_fb,
        MotionalSurroundMapping.LrFbMin, MotionalSurroundMapping.LrFbMax) * _parent.StageHeight;

    public string PositionLabel
    {
        get
        {
            string lr = _lr == 0 ? "C" : _lr < 0 ? $"L{-_lr}" : $"R{_lr}";
            string fb = _fb == 0 ? "C" : _fb < 0 ? $"F{-_fb}" : $"B{_fb}";
            return $"{lr} / {fb}";
        }
    }

    public void RaiseCanvasChanged()
    {
        this.RaisePropertyChanged(nameof(CanvasX));
        this.RaisePropertyChanged(nameof(CanvasY));
    }

    /// <summary>Set position from the model side (no write enqueued). Used by presets before a direct write.</summary>
    public void SetPositionSuppressed(int lr, int fb)
    {
        _suppress = true;
        try { Lr = lr; Fb = fb; }
        finally { _suppress = false; }
    }

    public void SetWidthAmbienceSuppressed(int width, int ambience)
    {
        _suppress = true;
        try { Width = width; Ambience = ambience; }
        finally { _suppress = false; }
    }

    public async System.Threading.Tasks.Task WritePositionAsync()
    {
        await _domain.WriteToIntegraAsync(_lrParam.ParSpec.Path, _lr.ToString(CultureInfo.InvariantCulture));
        await _domain.WriteToIntegraAsync(_fbParam.ParSpec.Path, _fb.ToString(CultureInfo.InvariantCulture));
    }

    public async System.Threading.Tasks.Task WriteWidthAmbienceAsync()
    {
        await _domain.WriteToIntegraAsync(_widthParam.ParSpec.Path, _width.ToString(CultureInfo.InvariantCulture));
        await _domain.WriteToIntegraAsync(_ambParam.ParSpec.Path, _amb.ToString(CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        _lrParam.PropertyChanged -= OnParamChanged;
        _fbParam.PropertyChanged -= OnParamChanged;
        _widthParam.PropertyChanged -= OnParamChanged;
        _ambParam.PropertyChanged -= OnParamChanged;
        if (_channelParam != null) _channelParam.PropertyChanged -= OnParamChanged;
    }
}
