using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Approximate, indicative filter magnitude response for the friendly filter graph (NOT an exact
/// transfer function). Returns normalized points: X 0..1 = low→high frequency, Y 0..1 = low→high
/// gain. The passband sits at mid-height so the resonance peak has room above it; the cutoff is the
/// X corner and resonance raises a peak there. Pure (no Avalonia) so it is unit-testable.
/// </summary>
public static class FilterCurve
{
    public const int SampleCount = 64;
    public const double PassLevel = 0.5;

    /// <summary>Normalized gain of the resonance peak at the cutoff for a given resonance (0..127).</summary>
    public static double PeakLevel(int resonance) => PassLevel + Clamp01(resonance / 127.0) * (1.0 - PassLevel);

    public static IReadOnlyList<(double X, double Y)> Sample(string mode, bool steep, int cutoff, int resonance)
    {
        var fc = Clamp01(cutoff / 127.0);
        var rolloff = steep ? 0.12 : 0.26;
        var peak = PeakLevel(resonance);
        var pts = new List<(double, double)>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            var f = i / (double)(SampleCount - 1);
            pts.Add((f, Clamp01(GainAt(mode, f, fc, rolloff, peak, steep))));
        }
        return pts;
    }

    private static double GainAt(string mode, double f, double fc, double rolloff, double peak, bool steep)
    {
        double Bump() => (peak - PassLevel) * Math.Exp(-Sq((f - fc) / (rolloff * 0.4)));
        switch (mode)
        {
            case "High pass":
                return (f >= fc ? PassLevel : PassLevel * Math.Max(0, 1 - (fc - f) / rolloff)) + Bump();
            case "Band pass":
                return peak * Math.Max(0, 1 - Math.Abs(f - fc) / (rolloff * (steep ? 1.0 : 1.8)));
            case "Peaking":
                return PassLevel + Bump();
            case "Bypass":
                return PassLevel;
            default: // Low pass and Low pass 2/3/4 variants
                return (f <= fc ? PassLevel : PassLevel * Math.Max(0, 1 - (f - fc) / rolloff)) + Bump();
        }
    }

    private static double Sq(double x) => x * x;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
