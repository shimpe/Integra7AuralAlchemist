using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Turning a point's weights into the colour drawn there.
///
/// <b>Chosen by looking, not by argument.</b> Four candidates were rendered side by side while this was
/// designed: the plain weighted mix, a sharpened mix, a winner-takes-the-wedge map, and the sharpened mix
/// with a brightness lift. The last was chosen because it says two things at once -- hue for which patch
/// leads, brightness for how decidedly -- and because the seams it leaves fall exactly where two corners
/// are level, which is where the discrete values flip. The picture therefore tells the truth about the
/// sound instead of decorating it.
///
/// It does not draw the winner's hysteresis. Near a boundary the colour can lead the ear by a few pixels;
/// painting the sticky winner instead would make the same position look different depending on how it was
/// approached, which is worse.</summary>
public static class MorphPadFill
{
    /// <summary>How hard the leading corner is pushed in the colour mix. With seven corners the raw
    /// weights leave no colour dominant and the pad reads as one grey-brown wash; this is what gives each
    /// corner visible territory. Only the colour is sharpened -- the sound uses the weights as they
    /// are.</summary>
    private const double Sharpness = 2.5;

    /// <summary>Brightness at a point where every corner is equal, and how much is added as one takes
    /// the lead. 0.55 keeps the middle from going black; 0.55 + 0.60 keeps a corner from blowing out.
    /// </summary>
    private const double Floor = 0.55;

    private const double Lift = 0.60;

    public static double[] Sharpen(IReadOnlyList<double> weights)
    {
        var sharpened = new double[weights.Count];
        var total = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            sharpened[i] = Math.Pow(weights[i], Sharpness);
            total += sharpened[i];
        }

        if (total <= 0) return sharpened;
        for (var i = 0; i < sharpened.Length; i++) sharpened[i] /= total;
        return sharpened;
    }

    /// <summary>How decided a point is: 1 where one corner has it to itself, 0 where the best two are
    /// level.</summary>
    public static double Dominance(IReadOnlyList<double> weights)
    {
        double best = 0, second = 0;
        foreach (var w in weights)
        {
            if (w > best)
            {
                second = best;
                best = w;
            }
            else if (w > second)
            {
                second = w;
            }
        }

        return best <= 0 ? 0 : (best - second) / best;
    }

    public static double Brightness(double dominance) => Floor + Lift * dominance;

    public static (double R, double G, double B) ColourAt(IReadOnlyList<double> weights,
        IReadOnlyList<(double R, double G, double B)> cornerColours)
    {
        var sharpened = Sharpen(weights);
        double r = 0, g = 0, b = 0;
        for (var i = 0; i < sharpened.Length; i++)
        {
            r += cornerColours[i].R * sharpened[i];
            g += cornerColours[i].G * sharpened[i];
            b += cornerColours[i].B * sharpened[i];
        }

        var brightness = Brightness(Dominance(weights));
        return (Math.Clamp(r * brightness, 0, 255),
                Math.Clamp(g * brightness, 0, 255),
                Math.Clamp(b * brightness, 0, 255));
    }
}
