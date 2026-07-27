using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>One category's row in the randomise dialog: whether it is included, and how far its
/// parameters may move.
///
/// <b>A slider, not a RotaryKnob.</b> The knob is the application's control for editing a sound, and it
/// earns that everywhere a parameter is edited. This is a settings row in a modal dialog -- closer to the
/// library's filters than to a filter cutoff -- and a labelled percentage slider says what it does
/// without being turned.</summary>
public sealed partial class RandomiseCategoryViewModel : ViewModelBase
{
    public RandomiseCategoryViewModel(ToneCategory category, string label, bool present)
    {
        Category = category;
        Label = label;
        IsPresent = present;
    }

    public ToneCategory Category { get; }

    public string Label { get; }

    /// <summary>Whether the loaded engine has any parameter in this category. A category it does not have
    /// is shown disabled rather than hidden, so the dialog keeps one shape.</summary>
    public bool IsPresent { get; }

    [Reactive] private bool _included;

    /// <summary>0..100, as the slider shows it. Divided by 100 on the way out -- the service works in
    /// 0..1, and a percentage is what a user reads.
    ///
    /// Starts at 5 %, which is a nudge rather than a new sound: the point of a strength control is that
    /// the result is still recognisably the patch you began with, and a user who wants more will reach
    /// for the slider. A default that lands somewhere unusable teaches people the feature is a toy.</summary>
    [Reactive] private double _strengthPercent = 5;
}

/// <summary>What a randomise should touch and how hard.
///
/// Held by <c>MainWindowViewModel</c> for the life of the window rather than built per press, so a second
/// randomise starts from the settings the first one used -- the point of the feature is trying again.
/// Not persisted across sessions; that is a later addition if it is ever missed.</summary>
public sealed partial class RandomiseToneViewModel : ViewModelBase
{
    private static readonly (ToneCategory Category, string Label)[] Rows =
    [
        (ToneCategory.PitchAndOscillator, "Pitch and oscillator"),
        (ToneCategory.WaveChoice, "Wave choice"),
        (ToneCategory.Filter, "Filter"),
        (ToneCategory.Amplifier, "Amplifier"),
        (ToneCategory.LfoAndModulation, "LFO and modulation"),
        (ToneCategory.Effects, "Effects"),
        (ToneCategory.InstrumentCharacter, "Instrument character"),
    ];

    public RandomiseToneViewModel()
    {
        foreach (var (category, label) in Rows)
            Categories.Add(new RandomiseCategoryViewModel(category, label, present: true));

        RandomiseCommand = ReactiveCommand.Create(() => true);
        CancelCommand = ReactiveCommand.Create(() => false);
    }

    public ObservableCollection<RandomiseCategoryViewModel> Categories { get; } = [];

    /// <summary>What this press will act on, e.g. "Randomising the tone in part 4" or "Randomising note
    /// 38 (D2) of the kit in part 10". Set by the caller before the dialog is shown, because only it
    /// knows which part is selected and what is in it.</summary>
    [Reactive] private string _target = "";

    public ReactiveCommand<Unit, bool> RandomiseCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }

    /// <summary>Whether pressing Randomise would do anything: at least one row ticked, on a category this
    /// engine has, at a strength above zero. The button is bound to it, so "nothing happened" is answered
    /// before the press rather than by a status line afterwards.
    ///
    /// A strength of zero is included in the test because the slider goes there: a ticked row at 0 % is a
    /// user saying "this group, but do not move it", which is indistinguishable in effect from not
    /// ticking it. The command behind the button still refuses an empty selection -- the dialog can be
    /// dismissed in ways that do not consult this.</summary>
    [Reactive] private bool _canRandomise;

    private void RecomputeCanRandomise() =>
        CanRandomise = Categories.Any(c => c.Included && c.IsPresent && c.StrengthPercent > 0);

    /// <summary>Point the rows at an engine: categories it does not have are disabled and unticked, so a
    /// tick left over from a different engine cannot silently do nothing.</summary>
    public void PrepareFor(string toneType, string target)
    {
        Target = target;
        var present = ToneParameterCategories.PresentIn(toneType);

        Categories.Clear();
        foreach (var (category, label) in Rows)
        {
            var row = new RandomiseCategoryViewModel(category, label, present.Contains(category));
            if (_lastIncluded.Contains(category) && row.IsPresent) row.Included = true;
            if (_lastStrengths.TryGetValue(category, out var strength)) row.StrengthPercent = strength;
            // Subscribed here rather than once in the constructor because the rows are rebuilt on every
            // PrepareFor -- a subscription to the old ones would go on answering for a dialog that is no
            // longer on screen. Never unsubscribed: the handler is the only thing holding the row, not
            // the other way round, so a discarded row is collectable.
            row.PropertyChanged += (_, _) => RecomputeCanRandomise();
            Categories.Add(row);
        }

        RecomputeCanRandomise();
    }

    /// <summary>What the user ticked, as the service wants it. Also remembers the settings for the next
    /// press.</summary>
    public RandomisationStrengths Strengths()
    {
        _lastIncluded = [.. Categories.Where(c => c.Included).Select(c => c.Category)];
        _lastStrengths = Categories.ToDictionary(c => c.Category, c => c.StrengthPercent);

        return new RandomisationStrengths(Categories
            .Where(c => c.Included && c.IsPresent)
            .ToDictionary(c => c.Category, c => c.StrengthPercent / 100.0));
    }

    private HashSet<ToneCategory> _lastIncluded = [];
    private Dictionary<ToneCategory, double> _lastStrengths = [];
}
