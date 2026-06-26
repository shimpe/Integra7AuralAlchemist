using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Friendly SuperNATURAL Synth editor for ONE part: header + 3 partials.</summary>
public sealed partial class SNSynthToneEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ThrottledParameterWriter _writer = new();
    private readonly Action<string>? _navigateToRawTab;

    public SNSynthToneHeaderViewModel Header { get; }
    public ObservableCollection<SNSPartialViewModel> Partials { get; } = [];

    /// <summary>Shared partial copy/paste buffer (path → display value).</summary>
    public IReadOnlyDictionary<string, string>? PartialClipboard { get; set; }

    public SNSynthToneEditorViewModel(Integra7Domain domain, int partNo, Action<string>? navigateToRawTab = null)
    {
        _navigateToRawTab = navigateToRawTab;

        var common = domain.SNSynthToneCommon(partNo);
        var commonByPath = ToDict(common);
        Header = new SNSynthToneHeaderViewModel(common, commonByPath, _writer);

        for (var i = 0; i < Constants.NO_OF_PARTIALS_SN_SYNTH_TONE; i++)
            Partials.Add(new SNSPartialViewModel(this, domain.SNSynthTonePartial(partNo, i),
                common, commonByPath, i, _writer));

        _selectedPartial = Partials[0];
        _selectedPartial.IsSelected = true;
    }

    private static Dictionary<string, FullyQualifiedParameter> ToDict(DomainBase d)
    {
        var dict = new Dictionary<string, FullyQualifiedParameter>();
        foreach (var p in d.GetRelevantParameters(true, true)) dict[p.ParSpec.Path] = p;
        return dict;
    }

    private SNSPartialViewModel _selectedPartial;
    public SNSPartialViewModel SelectedPartial
    {
        get => _selectedPartial;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedPartial)) return;
            _selectedPartial.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedPartial, value);
            _selectedPartial.IsSelected = true;
        }
    }

    [ReactiveCommand] public void CopyPartial() => SelectedPartial.Copy();
    [ReactiveCommand] public void PastePartial() => SelectedPartial.Paste();
    [ReactiveCommand] public void InitPartial() => SelectedPartial.Init();

    [ReactiveCommand] public void AdvancedOscillator() => _navigateToRawTab?.Invoke("SN-S-PARTIALS");
    [ReactiveCommand] public void AdvancedAmp() => _navigateToRawTab?.Invoke("SN-S-PARTIALS");
    [ReactiveCommand] public void AdvancedCommon() => _navigateToRawTab?.Invoke("SN-S-COMMON");

    public void Dispose()
    {
        Header.Dispose();
        foreach (var p in Partials) p.Dispose();
        _writer.Dispose();
    }
}
