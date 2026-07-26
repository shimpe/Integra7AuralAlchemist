using System.Globalization;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>How the mixer renders values that are numbers in the parameter database but words on the
/// instrument's own display. Pure, so the rendering is testable without a device.</summary>
public static class MixerFormatting
{
    /// <summary>Pan as the instrument shows it. The parameter is -64..63 (see
    /// <c>StudioSetPartEditorViewModel.Pan</c>); the panel reads that as L64..C..R63, and a mixer strip
    /// that showed "-27" instead of "L27" would be the only place in the application that did.</summary>
    public static string PanLabel(int pan) => pan switch
    {
        0 => "C",
        < 0 => "L" + (-pan).ToString(CultureInfo.InvariantCulture),
        _ => "R" + pan.ToString(CultureInfo.InvariantCulture),
    };
}
