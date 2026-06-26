using Integra7AuralAlchemist.Models.Services;

namespace Tests;

public class SnsOscillatorRulesTests
{
    [TestCase("Pulse Width Mod. Square", true)]
    [TestCase("Saw", false)]
    [TestCase("SuperSaw", false)]
    public void Pulse_width_controls_only_for_pwm_square(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsPulseWidth(wave), Is.EqualTo(expected));

    [TestCase("SuperSaw", true)]
    [TestCase("Saw", false)]
    public void Super_saw_detune_only_for_super_saw(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsSuperSawDetune(wave), Is.EqualTo(expected));

    [TestCase("Pcm", true)]
    [TestCase("Sine", false)]
    public void Pcm_controls_only_for_pcm(string wave, bool expected)
        => Assert.That(SnsOscillatorRules.ShowsPcm(wave), Is.EqualTo(expected));
}
