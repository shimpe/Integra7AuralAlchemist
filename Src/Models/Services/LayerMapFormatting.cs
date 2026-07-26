using System.Globalization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>How the layer map's readout renders the selected part's four range values, plus its two pairs of
/// fade widths. Pure, for the same reason <see cref="MixerFormatting"/> is pure: this repository has no
/// headless-Avalonia harness and no <c>DomainBase</c> double, so a view model that wraps live parameters cannot
/// be constructed in a test — but the strings it puts on screen can be, provided the building of them lives
/// somewhere a test can call. Everything below takes a <see cref="LayerZone"/> or a plain number and returns a
/// string, which is exactly the part of the readout that could be wrong without anyone noticing.</summary>
public static class LayerMapFormatting
{
    /// <summary>How a pair of numbers is written when both belong to one range: lower, then the dash, then
    /// upper. One constant so the four range readouts cannot drift apart typographically.</summary>
    private const string RangeSeparator = " – ";

    /// <summary>Between a part's number and the tone it holds, and between the two halves of the fade readout.
    /// The same middle dot <c>StudioSetPartEditorViewModel.ToneBankSummary</c> separates its three values
    /// with.</summary>
    private const string FieldSeparator = " · ";

    /// <summary>One key, as its note name and as the raw number: <c>C4 (60)</c>.
    ///
    /// <para>Both, deliberately. The name is what a musician reads a split against — "the break is just below
    /// C4" says something that "the break is at 59" does not — but the number is what the four spin boxes on the
    /// part's own Set Part tab show, and a readout giving only the name would leave the user unable to check
    /// that the two pages agree. The pair also makes this application's note convention explicit
    /// (<see cref="MidiNote"/>: 60 is C4), which matters because vendors disagree about it.</para>
    ///
    /// <para>The number is clamped the same way <see cref="MidiNote.Name"/> clamps, so the two halves always
    /// describe the same key. Out of range should not happen — every caller's value comes from a 0..127
    /// <c>ParamInt</c> — but a readout saying <c>G9 (200)</c> would be worse than either half alone.</para>
    /// </summary>
    public static string Key(int note)
        => $"{MidiNote.Name(note)} ({MidiNote.Clamp(note).ToString(CultureInfo.InvariantCulture)})";

    /// <summary>The key range: <c>C4 (60) – C6 (84)</c>.
    ///
    /// <para>Lower first and upper second, <b>as stored</b> — not sorted. The geometry deliberately tolerates an
    /// inverted range when it draws one, because a rectangle has to be drawn somewhere; a readout has the
    /// opposite duty. Its whole job is to agree with the two numbers on the part's own tab, which are the raw
    /// <c>Keyboard Range Lower</c> and <c>Keyboard Range Upper</c>, so silently swapping them here would hide
    /// exactly the state the user came to this readout to understand.</para></summary>
    public static string KeyRange(LayerZone z) => Key(z.KeyLo) + RangeSeparator + Key(z.KeyHi);

    /// <summary>The velocity range: <c>1 – 127</c>. Numbers only — velocity has no names to give it, and the
    /// instrument's own display shows these as numbers too.</summary>
    public static string VelocityRange(LayerZone z) => Number(z.VelLo) + RangeSeparator + Number(z.VelHi);

    /// <summary>Both pairs of fade widths: <c>keys 12 / 0 · velocity 0 / 0</c>, each pair lower then upper.
    ///
    /// <para>Here because the map draws fades but does not edit them: the four knobs live on the part's own Set
    /// Part tab, which the readout's <b>Edit fades…</b> button leads to. Four zeroes tell the user that pressing
    /// that button will show them four knobs at rest, which is worth knowing before the tab changes under
    /// them.</para></summary>
    public static string Fades(LayerZone z)
        => $"keys {Number(z.KeyFadeLo)} / {Number(z.KeyFadeHi)}{FieldSeparator}"
           + $"velocity {Number(z.VelFadeLo)} / {Number(z.VelFadeHi)}";

    /// <summary>What a part is called in the readout: <c>Part 3</c>, one-based like every part number the user
    /// sees and unlike every part number this feature passes around internally.</summary>
    public static string PartLabel(int zeroBasedPartNo)
        => "Part " + (zeroBasedPartNo + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>The readout's heading: <c>Part 3 · Ac.Piano 1</c>, or just <c>Part 3</c> when the part's tone
    /// name is not known — which is the honest state before the part's preset has resolved, and is also what an
    /// empty user-tone slot legitimately looks like. The separator is dropped with the name rather than left
    /// dangling.</summary>
    public static string Title(LayerZone z)
    {
        var label = PartLabel(z.PartNo);
        var tone = z.ToneName?.Trim();
        return string.IsNullOrEmpty(tone) ? label : label + FieldSeparator + tone;
    }

    private static string Number(int v) => v.ToString(CultureInfo.InvariantCulture);
}
