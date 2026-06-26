namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which oscillator sub-controls are relevant for a given OSC Wave (display string).</summary>
public static class SnsOscillatorRules
{
    public const string WaveSaw = "Saw";
    public const string WavePulseSquare = "Pulse Width Mod. Square";
    public const string WaveSuperSaw = "SuperSaw";
    public const string WavePcm = "Pcm";

    public static bool ShowsPulseWidth(string wave) => wave == WavePulseSquare;
    public static bool ShowsSuperSawDetune(string wave) => wave == WaveSuperSaw;
    public static bool ShowsPcm(string wave) => wave == WavePcm;
}
