using System;
using Avalonia;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Where the step LFO's bars go, which step a pointer is over, and what height means what value.
///
/// Apart from the control for the reason every other visual editor here keeps its arithmetic apart: a
/// control cannot be unit-tested, and this is the part that can be wrong. See <c>LayerMapGeometry</c> and
/// <c>KnobGeometry</c> for the same split.
///
/// Values are the <em>displayed</em> ones, -36..+36 rather than the raw 28..100 the device stores. The
/// wrappers the control writes through speak displayed values, so converting here would convert
/// twice.</summary>
public sealed class StepLfoGeometry(double width, double height, int steps, int minValue, int maxValue)
{
    /// <summary>The gap between two bars, so sixteen of them read as sixteen rather than as one block.
    /// Taken out of the bar's width, not added to it, so the bars still tile the full width and the x a
    /// user clicks in the gap still belongs to the bar beside it.</summary>
    private const double Gap = 2;

    /// <summary>How tall a bar at zero is drawn. The editor opens with all sixteen steps at rest, and a
    /// bar of no height is a control that cannot be seen or aimed at.</summary>
    private const double RestingHeight = 2;

    private double BarWidth => steps <= 0 ? 0 : width / steps;

    /// <summary>Where the value zero sits. Not simply the middle: the range is only symmetrical because
    /// this parameter happens to be, and a caller with an asymmetric one would get a centre line in the
    /// wrong place.</summary>
    public double CentreY => YFor(0);

    /// <summary>Which step the pointer is over, or null when it is outside the bars entirely -- a press
    /// there should do nothing rather than move the nearest step.</summary>
    public int? StepAt(double x)
    {
        if (width <= 0 || steps <= 0 || x < 0 || x >= width) return null;

        // Clamped as well as bounded: floating point can put x/BarWidth a hair past the last index for an
        // x a hair inside the right edge.
        return Math.Clamp((int)(x / BarWidth), 0, steps - 1);
    }

    /// <summary>The value a pointer at this height means, clamped to the parameter's own range. Clamping
    /// rather than refusing, because a drag does not stop at the control's edge and a user pulling well
    /// above it means "as high as it goes".</summary>
    public int ValueAt(double y)
    {
        if (height <= 0) return 0;

        var fraction = Math.Clamp(y / height, 0, 1);
        return (int)Math.Round(maxValue - fraction * (maxValue - minValue));
    }

    /// <summary>The bar for one step: from the centre line up for a positive value, down for a negative
    /// one, and a sliver on the centre for zero.</summary>
    public Rect BarFor(int step, int value)
    {
        var left = step * BarWidth + Gap / 2;
        var barWidth = Math.Max(0, BarWidth - Gap);

        if (value == 0)
            return new Rect(left, CentreY - RestingHeight / 2, barWidth, RestingHeight);

        var valueY = YFor(value);
        return value > 0
            ? new Rect(left, valueY, barWidth, CentreY - valueY)
            : new Rect(left, CentreY, barWidth, valueY - CentreY);
    }

    private double YFor(int value)
    {
        if (maxValue == minValue) return 0;

        return (maxValue - (double)value) / (maxValue - minValue) * height;
    }
}
