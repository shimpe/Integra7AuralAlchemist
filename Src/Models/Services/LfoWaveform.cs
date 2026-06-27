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
            case "Bend Up": return Math.Sin(Math.PI / 2.0 * x);     // eased rise 0 -> 1
            case "Bend Down": return Math.Cos(Math.PI / 2.0 * x);   // eased fall 1 -> 0
            case "Variable Sine":                                   // a fuller / sharper sine
            {
                var s = Math.Sin(2 * Math.PI * x);
                return 0.5 + 0.5 * Math.Sign(s) * Math.Pow(Math.Abs(s), 0.6);
            }
            case "Triangle Pulse": return TrianglePulse(x);         // trapezoid: rise / hold / fall
            case "Chiff": return Chiff(x);                          // brief spike near the onset
            case "Step": return StepIndex(x, 5) / 4.0;              // 5-level rising staircase
            default: return x < 0.5 ? x * 2.0 : 2.0 - x * 2.0;      // Triangle / Triangular
        }
    }

    // A trapezoid: ramp up over the first quarter, hold high, ramp down, then hold low.
    private static double TrianglePulse(double x)
    {
        if (x < 0.25) return x / 0.25;
        if (x < 0.5) return 1.0;
        if (x < 0.75) return 1.0 - (x - 0.5) / 0.25;
        return 0.0;
    }

    // A short breath-like spike just after the onset, otherwise near the floor.
    private static double Chiff(double x)
    {
        var d = (x - 0.08) / 0.06;
        return 0.12 + 0.88 * Math.Exp(-d * d);
    }

    private static int StepIndex(double x, int len)
    {
        var i = (int)(x * len);
        return i < 0 ? 0 : i >= len ? len - 1 : i;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
