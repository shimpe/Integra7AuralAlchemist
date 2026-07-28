using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Domain;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>The PCM Synth partial's step LFO: a type, and sixteen steps of -36..+36.
///
/// <b>Shared by both LFOs.</b> These parameter paths carry no LFO number, unlike "LFO1 Rate" and
/// "LFO2 Rate", so a partial has one step sequence and whichever of its two LFOs is set to the Step
/// waveform plays it. That is the instrument's design; the panel says so on screen, because a tone using
/// Step on both LFOs otherwise looks like a bug.
///
/// Sixteen named properties as well as the list: the list is what the partial tracks and disposes, and
/// the named ones are what the view binds, because a control's styled properties are bound one at a time
/// (see StepLfoControl for why it is built that way).</summary>
public sealed class StepLfoPanelViewModel : ViewModelBase, IDisposable
{
    private const string PP = "PCM Synth Tone Partial/";
    private readonly List<IDisposable> _wrappers = [];

    public ParamString StepType { get; }

    public IReadOnlyList<ParamInt> Steps { get; }

    public ParamInt Step1 => Steps[0];
    public ParamInt Step2 => Steps[1];
    public ParamInt Step3 => Steps[2];
    public ParamInt Step4 => Steps[3];
    public ParamInt Step5 => Steps[4];
    public ParamInt Step6 => Steps[5];
    public ParamInt Step7 => Steps[6];
    public ParamInt Step8 => Steps[7];
    public ParamInt Step9 => Steps[8];
    public ParamInt Step10 => Steps[9];
    public ParamInt Step11 => Steps[10];
    public ParamInt Step12 => Steps[11];
    public ParamInt Step13 => Steps[12];
    public ParamInt Step14 => Steps[13];
    public ParamInt Step15 => Steps[14];
    public ParamInt Step16 => Steps[15];

    public IReadOnlyList<IParam> Params { get; }

    public StepLfoPanelViewModel(DomainBase partialDomain,
        IReadOnlyDictionary<string, FullyQualifiedParameter> byPath, ThrottledParameterWriter writer)
    {
        StepType = Track(new ParamString(partialDomain, byPath[PP + "LFO Step Type"], writer));

        // -36..+36 is the displayed range; the device stores 28..100 and the wrapper converts.
        Steps =
        [
            .. Enumerable.Range(1, 16)
                .Select(n => Track(new ParamInt(partialDomain, byPath[PP + $"LFO Step {n}"], writer, -36, 36))),
        ];

        Params = [StepType, .. Steps];
    }

    private T Track<T>(T w) where T : IDisposable { _wrappers.Add(w); return w; }

    public void Dispose() { foreach (var w in _wrappers) w.Dispose(); }
}
