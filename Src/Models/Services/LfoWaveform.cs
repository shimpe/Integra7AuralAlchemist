using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Pure, deterministic one-cycle waveform sampler for the LFO shape preview. Returns normalized
/// points: X 0..1 across one cycle, Y 0..1 with 0.5 = center. No RNG so it renders/tests stably.
/// </summary>
public static class LfoWaveform
{
    private static readonly double[] SampleHoldLevels = { 0.85, 0.30, 0.60, 0.10, 0.95, 0.45 };
    private static readonly double[] RandomLevels = { 0.50, 0.90, 0.20, 0.70, 0.35, 0.62, 0.15, 0.80 };

    public static IReadOnlyList<(double X, double Y)> Sample(string shape, int count)
    {
        var pts = new List<(double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            var x = count <= 1 ? 0.0 : i / (double)(count - 1);
            pts.Add((x, Clamp01(YAt(shape, x))));
        }
        return pts;
    }

    private static double YAt(string shape, double x)
    {
        switch (shape)
        {
            case "Sine": return 0.5 + 0.5 * Math.Sin(2 * Math.PI * x);
            case "Sawtooth": case "Saw Up": return x;
            case "Saw Down": return 1.0 - x;
            case "Square": return x < 0.5 ? 1.0 : 0.0;
            case "Sample&Hold": return SampleHoldLevels[StepIndex(x, SampleHoldLevels.Length)];
            case "Random": return RandomLevels[StepIndex(x, RandomLevels.Length)];
            default: return x < 0.5 ? x * 2.0 : 2.0 - x * 2.0; // Triangle
        }
    }

    private static int StepIndex(double x, int len)
    {
        var i = (int)(x * len);
        return i < 0 ? 0 : i >= len ? len - 1 : i;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
