using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>A copy/paste-able synth parameter: its lookup path plus get/set of the display value.</summary>
public interface IParam
{
    string Path { get; }
    string Snapshot();
    void ApplyDisplay(string display);
}

/// <summary>Pure copy/paste over <see cref="IParam"/> (testable without hardware).</summary>
public static class SnsPartialClipboard
{
    public static Dictionary<string, string> Snapshot(IEnumerable<IParam> ps)
        => ps.ToDictionary(p => p.Path, p => p.Snapshot());

    public static void Apply(IEnumerable<IParam> ps, IReadOnlyDictionary<string, string> data)
    {
        foreach (var p in ps)
            if (data.TryGetValue(p.Path, out var v))
                p.ApplyDisplay(v);
    }
}

/// <summary>Two-way bindable wrapper over one numeric FQP: clamps, reflects echoes, throttled writes.</summary>
public sealed class ParamInt : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private readonly int _min, _max;
    private readonly string _key;
    private bool _suppress;
    private int _value;

    public ParamInt(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer, int min, int max)
    {
        _domain = domain; _p = p; _writer = writer; _min = min; _max = max;
        _key = $"{domain.StartAddressName}|{domain.Offset2AddressName}|{p.ParSpec.Path}";
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _min, _max);
            if (_value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) Enqueue();
        }
    }

    private void Enqueue() => _writer.Enqueue(_key,
        () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value.ToString(CultureInfo.InvariantCulture)));

    public string Snapshot() => _value.ToString(CultureInfo.InvariantCulture);

    public void ApplyDisplay(string display)
    {
        if (int.TryParse(display, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            Value = v;
    }

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try
        {
            if (int.TryParse(_p.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                Value = Math.Clamp(v, _min, _max);
        }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}

/// <summary>Two-way bindable wrapper over one enum/string FQP, exposing its allowed Options.</summary>
public sealed class ParamString : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private readonly string _key;
    private bool _suppress;
    private string _value = "";

    public ParamString(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer,
        IReadOnlyList<string>? options = null)
    {
        _domain = domain; _p = p; _writer = writer;
        _key = $"{domain.StartAddressName}|{domain.Offset2AddressName}|{p.ParSpec.Path}";
        Options = options
            ?? (p.ParSpec.Repr?.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList()
                ?? new List<string>());
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;
    public IReadOnlyList<string> Options { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (value is null || _value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) _writer.Enqueue(_key, () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, _value));
        }
    }

    public string Snapshot() => _value;
    public void ApplyDisplay(string display) => Value = display;

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try { Value = _p.StringValue; }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}

/// <summary>Two-way bindable bool over an OFF/ON-style FQP.</summary>
public sealed class ParamBool : ReactiveObject, IParam, IDisposable
{
    private readonly DomainBase _domain;
    private readonly FullyQualifiedParameter _p;
    private readonly ThrottledParameterWriter _writer;
    private readonly string _on, _off;
    private readonly string _key;
    private bool _suppress;
    private bool _value;

    public ParamBool(DomainBase domain, FullyQualifiedParameter p, ThrottledParameterWriter writer,
        string onValue = "ON", string offValue = "OFF")
    {
        _domain = domain; _p = p; _writer = writer; _on = onValue; _off = offValue;
        _key = $"{domain.StartAddressName}|{domain.Offset2AddressName}|{p.ParSpec.Path}";
        ApplyFromModel();
        _p.PropertyChanged += OnModelChanged;
    }

    public string Path => _p.ParSpec.Path;

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_suppress) _writer.Enqueue(_key,
                () => _domain.WriteToIntegraAsync(_p.ParSpec.Path, value ? _on : _off));
        }
    }

    public string Snapshot() => _value ? _on : _off;
    public void ApplyDisplay(string display) => Value = string.Equals(display, _on, StringComparison.OrdinalIgnoreCase);

    private void OnModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FullyQualifiedParameter.StringValue)) return;
        Dispatcher.UIThread.Post(ApplyFromModel);
    }

    private void ApplyFromModel()
    {
        _suppress = true;
        try { Value = string.Equals(_p.StringValue, _on, StringComparison.OrdinalIgnoreCase); }
        finally { _suppress = false; }
    }

    public void Dispose() => _p.PropertyChanged -= OnModelChanged;
}
