namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Parameters the app shows but never offers an editor for.
///
/// So far that is exactly the Studio Set selectors — see <see cref="StudioSetSelectors"/> for why
/// writing one from the parameter grid would desync the app from the device. Reading them is useful,
/// so they stay visible, just not editable.
///
/// Pure (no Avalonia) so it is unit-testable; the rendering side is <c>DataTemplateProvider</c>,
/// which turns a match into a plain readout.
/// </summary>
public static class ReadOnlyParameters
{
    public static bool IsReadOnly(string path) => StudioSetSelectors.Contains(path);
}
