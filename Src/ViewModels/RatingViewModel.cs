using System;
using System.Collections.Generic;
using ReactiveUI;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Nought to five stars, as five things to click. Used twice: by the library browser to rate whatever is
/// selected, and by the save dialog to rate a sound on the way into the library. It exists as a type of its own
/// rather than as five properties on each of those because it is the same control in both places and it has
/// behaviour -- see <see cref="Set"/>.
///
/// <b>No ToolTip on any of it, ever.</b> This is five click targets side by side, which is the exact shape of the
/// bug that cost two rounds of misclicks on the Compare button: a tooltip is a real popup window, and sitting
/// under the pointer it takes the click and the control never sees it. The stars say what they are.
///
/// <b>Nothing here is platform-backed and nothing is built in a static initialiser</b>, deliberately: a
/// <c>Cursor</c> or a brush built at type-load time takes the whole type down in a unit-test process with no
/// Avalonia application, which is how the layer map earned one red suite. The glyphs are characters and the
/// colours are the view's business.</summary>
public sealed class RatingViewModel : ViewModelBase
{
    /// <summary>The five stars, first to fifth. Fixed and never rebuilt, so a view can bind to this once.</summary>
    public IReadOnlyList<RatingStarViewModel> Stars { get; }

    private int _value;

    public RatingViewModel(int value = 0)
    {
        List<RatingStarViewModel> stars = [];
        for (var position = 1; position <= 5; position++) stars.Add(new RatingStarViewModel(this, position));
        Stars = stars;
        Value = value;
    }

    /// <summary>The rating: 0 to 5, where 0 is unrated. Clamped rather than checked, because the only thing that
    /// can assign it out of range is a caller with a bug, and a snapshot's rating is validated where it is
    /// written (see <c>SnapshotLibrary.WriteMetadata</c>) -- a control silently refusing to display the number it
    /// was handed would hide that, and clamping at least shows something the user can correct.</summary>
    public int Value
    {
        get => _value;
        set
        {
            this.RaiseAndSetIfChanged(ref _value, Math.Clamp(value, 0, 5));
            // Unconditionally, not only when the value moved: five cheap no-op raises are worth less than the
            // one case where a star's glyph is stale because the guard above decided nothing had changed.
            foreach (var star in Stars) star.Refresh();
            this.RaisePropertyChanged(nameof(Label));
        }
    }

    /// <summary>The rating in words, beside the stars. Filled stars alone do not say whether the fourth is lit or
    /// the fifth is dark, which is the one thing a user checks after clicking.</summary>
    public string Label => Value switch
    {
        0 => "unrated",
        1 => "1 star",
        _ => $"{Value} stars",
    };

    /// <summary>What clicking the <paramref name="position"/>th star means: that many stars -- unless it is
    /// already exactly that many, in which case one fewer.
    ///
    /// <b>That is how a rating gets cleared</b>, and it is why this control needs no sixth "no rating" button
    /// beside the five. Clicking the third star of a three-star sound leaves it on two; clicking the first star
    /// of a one-star sound leaves it unrated. Every other star control works this way, so it is what the hand
    /// expects, and the alternative -- a clear button -- is a sixth target in a row of five where the user is
    /// already aiming carefully.</summary>
    public void Set(int position) => Value = Value == position ? position - 1 : position;
}

/// <summary>One of the five. Carries its position and asks its rating to act, rather than holding a value of its
/// own: a star's state is entirely "is the rating at least me", and two places holding the same number is how
/// they come to disagree.
///
/// The click arrives as a parameterless method binding -- <c>Command="{Binding Set}"</c> -- which the XAML
/// compiler type-checks. A <c>CommandParameter</c> of "3" would have to be converted from a string at runtime
/// into whatever the command's parameter type is, and that is a cast that either works or throws where nothing
/// can see it; this is the same shape as <c>MixerStripViewModel.ToggleSolo</c>, for the same reason.</summary>
public sealed class RatingStarViewModel : ViewModelBase
{
    private readonly RatingViewModel _rating;

    internal RatingStarViewModel(RatingViewModel rating, int position)
    {
        _rating = rating;
        Position = position;
    }

    /// <summary>Which star this is, 1 to 5.</summary>
    public int Position { get; }

    public bool IsLit => _rating.Value >= Position;

    /// <summary>Filled or hollow. A glyph rather than two images or a brush swap: it is one character, it scales
    /// with the font, and it is the same character the list column uses for the same meaning (see
    /// <c>LibraryListing.Stars</c>).</summary>
    public string Glyph => IsLit ? "★" : "☆";

    /// <summary>Rate the sound at this star. See <see cref="RatingViewModel.Set"/> for what a second click on the
    /// same star does.</summary>
    public void Set() => _rating.Set(Position);

    internal void Refresh()
    {
        this.RaisePropertyChanged(nameof(IsLit));
        this.RaisePropertyChanged(nameof(Glyph));
    }
}
