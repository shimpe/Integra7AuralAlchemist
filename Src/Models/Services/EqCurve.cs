using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The three band settings a curve is drawn from: frequencies in Hz, gains in dB.</summary>
public readonly record struct EqBands(
    double LowHz, double LowGainDb,
    double MidHz, double MidGainDb, double MidQ,
    double HighHz, double HighGainDb);

/// <summary>
/// Approximate, indicative magnitude response of the 3-band part EQ — a low shelf, a peaking mid and
/// a high shelf — for the friendly EQ graph. NOT an exact transfer function (same spirit as
/// <see cref="FilterCurve"/>). X is a log-frequency position 0..1 over <see cref="MinHz"/>..
/// <see cref="MaxHz"/>; gains are in dB. Pure (no Avalonia) so it is unit-testable.
/// </summary>
public static class EqCurve
{
    public const int SampleCount = 128;
    public const double MinHz = 20.0;
    public const double MaxHz = 20000.0;

    /// <summary>Vertical half-range of the graph in dB. The hardware range is ±15 per band; the extra
    /// leaves room for the overshoot where two boosted bands overlap.</summary>
    public const double RangeDb = 20.0;

    /// <summary>Half-width of a shelf transition, in decades (~1.2 octaves).</summary>
    private const double ShelfWidthDecades = 0.35;

    private static readonly double LogMin = Math.Log10(MinHz);
    private static readonly double LogSpan = Math.Log10(MaxHz) - Math.Log10(MinHz);

    /// <summary>Normalized horizontal position 0..1 (left→right) of a frequency.</summary>
    public static double XFor(double hz) => Clamp01((Math.Log10(Math.Max(hz, 1e-6)) - LogMin) / LogSpan);

    /// <summary>The frequency at a normalized horizontal position 0..1.</summary>
    public static double HzAt(double x) => Math.Pow(10, LogMin + Clamp01(x) * LogSpan);

    /// <summary>Normalized vertical position 0..1 (top→bottom) of a gain in dB.</summary>
    public static double Y01(double db) => Clamp01(0.5 - db / (2 * RangeDb));

    /// <summary>The gain in dB at a normalized vertical position 0..1, clamped to the graph range.</summary>
    public static double DbAtY01(double y01) => Clamp((0.5 - y01) * 2 * RangeDb, -RangeDb, RangeDb);

    /// <summary>Summed response of the three bands at one frequency, in dB.</summary>
    public static double GainDbAt(double hz, EqBands b)
    {
        var lx = Math.Log10(Math.Max(hz, 1e-6));
        return b.LowGainDb * ShelfLow(lx, Math.Log10(b.LowHz))
             + b.MidGainDb * Bell(lx, Math.Log10(b.MidHz), b.MidQ)
             + b.HighGainDb * ShelfHigh(lx, Math.Log10(b.HighHz));
    }

    /// <summary>The curve as <see cref="SampleCount"/> points evenly spaced along the log-frequency
    /// axis: X 0..1 left→right, Db the summed gain there.</summary>
    public static IReadOnlyList<(double X, double Db)> Sample(EqBands b)
    {
        var pts = new List<(double, double)>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            var x = i / (double)(SampleCount - 1);
            pts.Add((x, GainDbAt(HzAt(x), b)));
        }
        return pts;
    }

    /// <summary>The allowed frequency closest to <paramref name="hz"/>, or <paramref name="hz"/> itself
    /// when nothing is allowed in particular. Distance is measured in log frequency, matching the
    /// graph's axis, so the nearest allowed value is also the nearest one on screen.</summary>
    public static double SnapHz(double hz, IReadOnlyList<double>? allowed)
    {
        if (allowed is null || allowed.Count == 0) return hz;
        var target = Math.Log10(Math.Max(hz, 1e-6));
        var best = hz;
        var bestDistance = double.MaxValue;
        foreach (var candidate in allowed)
        {
            var d = Math.Abs(Math.Log10(Math.Max(candidate, 1e-6)) - target);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = candidate;
        }
        return best;
    }

    /// <summary>Which band's handle sits nearest a normalized point — 0 low, 1 mid, 2 high, or -1 when
    /// none is within <paramref name="maxDistance"/>. Measured in the normalized X/Y space, so a handle
    /// is grabbed by proximity in both axes rather than by frequency alone.</summary>
    public static int NearestBand(double x01, double y01, EqBands b, double maxDistance)
    {
        Span<double> dx = [XFor(b.LowHz), XFor(b.MidHz), XFor(b.HighHz)];
        Span<double> dy = [Y01(b.LowGainDb), Y01(b.MidGainDb), Y01(b.HighGainDb)];
        var best = -1;
        var bestDist = maxDistance;
        for (var i = 0; i < 3; i++)
        {
            var d = Math.Sqrt(Sq(x01 - dx[i]) + Sq(y01 - dy[i]));
            if (d > bestDist) continue;
            bestDist = d;
            best = i;
        }
        return best;
    }

    // 1 well below the corner, 0 well above it, smooth across the transition.
    private static double ShelfLow(double lx, double lfc) => 0.5 * (1 - Math.Tanh((lx - lfc) / ShelfWidthDecades));
    private static double ShelfHigh(double lx, double lfc) => 0.5 * (1 + Math.Tanh((lx - lfc) / ShelfWidthDecades));

    // Gaussian bell centred on the mid frequency, roughly 1/Q octaves wide.
    private static double Bell(double lx, double lfc, double q)
    {
        var sigma = Math.Log10(2) / (2 * Math.Max(q, 0.1));
        var t = (lx - lfc) / sigma;
        return Math.Exp(-t * t);
    }

    private static double Sq(double v) => v * v;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
